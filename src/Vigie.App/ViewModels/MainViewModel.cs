using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vigie.App.Resources;
using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Power;
using Vigie.Core.Session;

namespace Vigie.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(10);

    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<int> _vigieProcessIds = [];
    private readonly ObservableCollection<LogEntry> _journal = [];

    private PowerEventWindow? _powerWindow;
    private ClaudeRun? _run;
    private int? _trackedProcessId;
    private IReadOnlySet<int> _aliveProcessIds = new HashSet<int>();
    private TimeSpan _sessionTotal;
    private bool _windowVisible = true;
    private long _tick;
    private long _lastScanTick = long.MinValue;
    private readonly bool _modernStandby;
    private bool _onBattery;
    private int? _batteryPercent;
    private bool _batterySaver;
    private bool _lidClosed;

    public MainViewModel(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;
        _modernStandby = services.Power.IsModernStandbyAvailable();

        Journal = new ReadOnlyObservableCollection<LogEntry>(_journal);
        Permissions = PermissionItem.All;
        SelectedPermission = Permissions[0];

        services.UiLog.EntryWritten += (_, entry) => _dispatcher.BeginInvoke(() => AppendJournal(entry));
        foreach (LogEntry entry in services.UiLog.Snapshot())
        {
            _journal.Add(entry);
        }

        services.Session.StateChanged += (_, _) => _dispatcher.BeginInvoke(UpdateSessionDisplay);
        services.Session.SleepDetected += (_, period) => _dispatcher.BeginInvoke(() => DisplaySleepBanner(period));
        services.Session.Stopped += (_, _) => _dispatcher.BeginInvoke(OnSessionStopped);

        RefreshPowerStatus();
        UpdateSessionDisplay();
        UpdateClaudeDisplay();

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    public ReadOnlyObservableCollection<LogEntry> Journal { get; }

    public IReadOnlyList<PermissionItem> Permissions { get; }

    public ObservableCollection<ClaudeProcessItem> DetectedProcesses { get; } = [];

    public ObservableCollection<ClaudeProcessItem> TaskTargets { get; } = [];

    [ObservableProperty]
    public partial string PowerStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SessionBadgeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSessionRunning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string TrayTooltip { get; set; } = Strings.Tray_Tip_Inactive;

    [ObservableProperty]
    public partial DurationChoice SelectedDuration { get; set; } = DurationChoice.TwoHours;

    [ObservableProperty]
    public partial int CustomMinutes { get; set; } = 90;

    [ObservableProperty]
    public partial bool KeepDisplayOn { get; set; }

    [ObservableProperty]
    public partial bool WorkWithLidClosed { get; set; } = true;

    [ObservableProperty]
    public partial bool LockOnLidClose { get; set; } = true;

    [ObservableProperty]
    public partial bool SuspendAtEnd { get; set; } = true;

    [ObservableProperty]
    public partial int BatteryThreshold { get; set; } = 20;

    [ObservableProperty]
    public partial bool IsAutonomous { get; set; } = true;

    [ObservableProperty]
    public partial PermissionItem SelectedPermission { get; set; }

    [ObservableProperty]
    public partial ClaudeProcessItem? SelectedTaskTarget { get; set; }

    [ObservableProperty]
    public partial string ProjectFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Prompt { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExtraArguments { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClaudeStateText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClaudeBadgeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanLaunchClaude { get; set; } = true;

    [ObservableProperty]
    public partial bool CanInterruptClaude { get; set; }

    [ObservableProperty]
    public partial bool ShowBatteryWarning { get; set; }

    [ObservableProperty]
    public partial bool ShowModernStandbyInfo { get; set; }

    [ObservableProperty]
    public partial bool ShowRebootWarning { get; set; }

    [ObservableProperty]
    public partial bool ShowDegradedBanner { get; set; }

    [ObservableProperty]
    public partial string DegradedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowSleepBanner { get; set; }

    [ObservableProperty]
    public partial string SleepBannerText { get; set; } = string.Empty;

    public bool OptionsEnabled => !IsSessionRunning;

    public void AttachWindow(HwndSource source)
    {
        _powerWindow = new PowerEventWindow(source);
        _powerWindow.SettingChanged += (_, broadcast) => _dispatcher.BeginInvoke(() => OnPowerBroadcast(broadcast));
        _powerWindow.Resumed += (_, _) => _dispatcher.BeginInvoke(_services.Session.OnResume);
    }

    public void SetWindowVisible(bool visible) => _windowVisible = visible;

    public void ShutDown(StopReason reason)
    {
        _timer.Stop();
        _services.Session.Stop(reason);
        _powerWindow?.Dispose();
        _powerWindow = null;
    }

    public bool IsClaudeRunning => _run is { HasExited: false };

    public void StopForQuit() => _services.Session.Stop(StopReason.UserStopped);

    private void OnTick()
    {
        _tick++;

        long scanInterval = _windowVisible ? 3 : 10;
        if (_tick - _lastScanTick >= scanInterval)
        {
            _lastScanTick = _tick;
            RefreshProcesses();
        }

        if (_services.Session.IsRunning)
        {
            if (_tick % 5 == 0)
            {
                _services.Session.Beat();
            }

            if (_windowVisible)
            {
                UpdateCountdown();
            }
        }

        UpdateTrayTooltip();
    }

    [RelayCommand]
    private void StartSession()
    {
        if (!TryBuildOptions(out SessionOptions options))
        {
            return;
        }

        PowerStatus status = _services.Power.GetPowerStatus();
        var preconditions = new SessionPreconditions(
            _modernStandby,
            status,
            PowerPolicies.Read(_services.Registry),
            TrackedProcessAlive());

        StartResult result = _services.Session.Start(options, preconditions, TrackedProcessAlive);
        switch (result.Refusal)
        {
            case StartRefusal.KeepAwakeBlockedByPolicy:
                ShowMessage(Strings.Warn_RequestsBlocked, Strings.Dialog_RequestsBlockedTitle);
                return;
            case StartRefusal.TrackedProcessGone:
                ShowMessage(Strings.Claude_Idle, Strings.AppTitle);
                return;
            case StartRefusal.PowerRequestFailed:
                ShowMessage(Strings.Warn_RequestsBlocked, Strings.Dialog_RequestsBlockedTitle);
                return;
        }

        _sessionTotal = options.EffectiveCeiling;
        ShowRebootWarning = preconditions.Policies.RebootPending;
        ShowModernStandbyInfo = _modernStandby;
        ShowBatteryWarning = status.OnBattery;
        UpdateDegradedBanner(result.Degraded);
        ShowSleepBanner = false;
        UpdateSessionDisplay();
    }

    [RelayCommand]
    private void StopSession() => _services.Session.Stop(StopReason.UserStopped);

    [RelayCommand]
    private async Task LaunchClaude()
    {
        if (!_services.FileSystem.DirectoryExists(ProjectFolder))
        {
            ShowMessage(Strings.Claude_FolderInvalid, Strings.AppTitle);
            return;
        }

        ClaudeLocateResult located = _services.Locator.Locate(userPath: null, ProjectFolder);
        if (located.Installation is not { } installation)
        {
            ShowMessage(Strings.Claude_NotFound, Strings.AppTitle);
            return;
        }

        CanLaunchClaude = false;
        try
        {
            PreflightResult preflight = await _services.Preflight.CheckAsync(installation, CancellationToken.None);
            if (!HandlePreflight(preflight))
            {
                return;
            }

            if (!CommandLineSplitter.TrySplit(ExtraArguments, out IReadOnlyList<string> extra))
            {
                ShowMessage(Strings.Claude_CheckFailed, Strings.AppTitle);
                return;
            }

            ClaudeLaunchMode mode = IsAutonomous ? ClaudeLaunchMode.Autonomous : ClaudeLaunchMode.Interactive;
            var request = new ClaudeLaunchRequest(ProjectFolder, Prompt, mode, SelectedPermission.Mode, MaxTurns: null, extra);
            if (ClaudeCommand.BypassesPermissions(request) && !ConfirmBypass())
            {
                return;
            }

            StartRun(installation, request, mode);
        }
        finally
        {
            CanLaunchClaude = true;
        }
    }

    [RelayCommand]
    private async Task InterruptClaude()
    {
        if (_run is { } run && !run.HasExited)
        {
            CanInterruptClaude = false;
            await run.InterruptAsync(InterruptGrace);
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Strings.Claude_Folder };
        if (dialog.ShowDialog() == true)
        {
            ProjectFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void OpenLogs()
    {
        _services.Storage.EnsureCreated();
        Process.Start(new ProcessStartInfo(_services.Storage.LogDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyLog()
    {
        string text = string.Join(Environment.NewLine, _journal.Select(e => $"{e.Timestamp:HH:mm:ss}  {e.Message}"));
        Clipboard.SetText(text);
    }

    [RelayCommand]
    private void Quit()
    {
        ShutDown(StopReason.ApplicationClosing);
        Application.Current.Shutdown();
    }

    private void StartRun(ClaudeInstallation installation, ClaudeLaunchRequest request, ClaudeLaunchMode mode)
    {
        _run?.Dispose();
        string? runDirectory = mode == ClaudeLaunchMode.Autonomous ? _services.Storage.CreateRunDirectory(_services.Clock.Now) : null;
        var launch = ClaudeCommand.Build(installation, request);
        _run = ClaudeRun.Start(_services.Processes, _services.FileSystem, _services.Clock, launch, mode, runDirectory);
        _vigieProcessIds.Add(_run.ProcessId);
        _trackedProcessId = _run.ProcessId;

        _run.EventReceived += (_, _) => _dispatcher.BeginInvoke(UpdateClaudeDisplay);
        _run.Exited += (_, _) => _dispatcher.BeginInvoke(UpdateClaudeDisplay);

        _services.Log.Info(FrenchTypography.Fix($"Claude Code lancé (PID {_run.ProcessId})."));
        RefreshProcesses();
    }

    private bool HandlePreflight(PreflightResult preflight)
    {
        switch (preflight.Status)
        {
            case PreflightStatus.Ready:
                return true;
            case PreflightStatus.NotLoggedIn:
                ShowMessage(Strings.Claude_NotLoggedIn, Strings.AppTitle);
                return false;
            case PreflightStatus.NotExecutable:
                ShowMessage(Strings.Claude_NotFound, Strings.AppTitle);
                return false;
            default:
                ShowMessage(Strings.Claude_CheckFailed, Strings.AppTitle);
                return false;
        }
    }

    private bool TryBuildOptions(out SessionOptions options)
    {
        bool untilTask = SelectedDuration == DurationChoice.UntilTask;
        options = new SessionOptions
        {
            DurationKind = untilTask ? SessionDurationKind.UntilTask : SessionDurationKind.Fixed,
            FixedDuration = DurationFromChoice(),
            KeepDisplayOn = KeepDisplayOn,
            WorkWithLidClosed = WorkWithLidClosed,
            LockOnLidClose = LockOnLidClose,
            SuspendAtEndIfLidClosed = SuspendAtEnd,
            BatteryStopThresholdPercent = Math.Clamp(BatteryThreshold, 10, 50),
            TrackedProcessId = untilTask ? SelectedTaskTarget?.ProcessId ?? _trackedProcessId : null,
        };

        if (untilTask)
        {
            _trackedProcessId = options.TrackedProcessId;
        }

        if (!options.IsValid(out _))
        {
            ShowMessage(Strings.Format(Strings.Opt_BatteryStopPrefix + " 10–50 %."), Strings.AppTitle);
            return false;
        }

        return true;
    }

    private TimeSpan DurationFromChoice() => SelectedDuration switch
    {
        DurationChoice.FifteenMinutes => TimeSpan.FromMinutes(15),
        DurationChoice.OneHour => TimeSpan.FromHours(1),
        DurationChoice.TwoHours => TimeSpan.FromHours(2),
        DurationChoice.FourHours => TimeSpan.FromHours(4),
        DurationChoice.Custom => TimeSpan.FromMinutes(Math.Clamp(CustomMinutes, 5, 24 * 60)),
        _ => TimeSpan.FromHours(2),
    };

    private bool TrackedProcessAlive()
    {
        if (_trackedProcessId is not int pid)
        {
            return false;
        }

        if (_run is { HasExited: false } run && run.ProcessId == pid)
        {
            return true;
        }

        return _aliveProcessIds.Contains(pid);
    }

    private void RefreshProcesses()
    {
        MonitorScan scan = _services.Monitor.Scan(_vigieProcessIds);
        _aliveProcessIds = scan.Processes.Select(p => p.ProcessId).ToHashSet();

        DetectedProcesses.Clear();
        TaskTargets.Clear();
        foreach (ClaudeProcessInfo process in scan.Processes)
        {
            ClaudeProcessItem item = ClaudeProcessItem.From(process);
            DetectedProcesses.Add(item);
            TaskTargets.Add(item);
        }

        SelectedTaskTarget ??= TaskTargets.FirstOrDefault(t => t.ProcessId == _trackedProcessId) ?? TaskTargets.FirstOrDefault();
        UpdateClaudeDisplay();
    }

    private void OnPowerBroadcast(PowerBroadcast broadcast)
    {
        if (broadcast.Setting == PowerKnownSettings.AcDcPowerSource)
        {
            _onBattery = broadcast.Value != 0;
            _services.Session.OnPowerSourceChanged(_services.Power.GetPowerStatus());
            if (IsSessionRunning)
            {
                ShowBatteryWarning = _onBattery;
            }
        }
        else if (broadcast.Setting == PowerKnownSettings.BatteryPercentageRemaining)
        {
            _batteryPercent = broadcast.Value;
        }
        else if (broadcast.Setting == PowerKnownSettings.LidSwitchStateChange)
        {
            _lidClosed = broadcast.Value == 0;
            _services.Session.OnLidChanged(_lidClosed);
        }
        else if (broadcast.Setting == PowerKnownSettings.ConsoleDisplayState)
        {
            _services.Session.OnConsoleDisplayStateChanged(broadcast.Value);
        }
        else if (broadcast.Setting == PowerKnownSettings.ActivePowerScheme)
        {
            _services.Session.OnActivePowerSchemeChanged(_services.Power.GetActiveSchemeGuid());
        }

        RefreshPowerStatus();
    }

    private void RefreshPowerStatus()
    {
        PowerStatus status = _services.Power.GetPowerStatus();
        _onBattery = status.OnBattery;
        _batteryPercent = status.BatteryPercent ?? _batteryPercent;
        _batterySaver = status.BatterySaverOn;

        var parts = new List<string>
        {
            _onBattery
                ? (_batteryPercent is int percent ? Strings.Format(Strings.Power_Battery, percent) : Strings.Power_PercentUnknown)
                : Strings.Power_Ac,
            _lidClosed ? Strings.Power_LidClosed : Strings.Power_LidOpen,
            _modernStandby ? Strings.Power_ModernStandby : Strings.Power_LegacySleep,
        };

        if (_batterySaver)
        {
            parts.Add(Strings.Power_BatterySaver);
        }

        PowerStatusText = string.Join(" · ", parts);
    }

    private void UpdateSessionDisplay()
    {
        IsSessionRunning = _services.Session.IsRunning;
        OnPropertyChanged(nameof(OptionsEnabled));
        StartSessionCommand.NotifyCanExecuteChanged();

        SessionBadgeText = IsSessionRunning ? Strings.Badge_Active : Strings.Badge_Inactive;

        if (!IsSessionRunning)
        {
            SessionStateText = _services.Session.State == SessionState.Stopping ? Strings.State_Stopping : Strings.State_Inactive;
            Progress = 0;
            return;
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        TimeSpan? remaining = _services.Session.RemainingTime;
        DateTimeOffset? end = _services.Session.EndTime;
        if (remaining is null || end is null)
        {
            return;
        }

        string endLabel = end.Value.ToLocalTime().ToString("HH:mm");
        bool untilTask = SelectedDuration == DurationChoice.UntilTask;

        if (_services.Session.Degraded != DegradedReason.None)
        {
            SessionStateText = Strings.State_Degraded;
        }
        else if (untilTask)
        {
            SessionStateText = Strings.Format(Strings.State_ActiveUntilTask, endLabel);
        }
        else
        {
            SessionStateText = Strings.Format(Strings.State_Active, FormatDuration(remaining.Value), endLabel);
        }

        Progress = _sessionTotal > TimeSpan.Zero
            ? Math.Clamp(1 - (remaining.Value.TotalSeconds / _sessionTotal.TotalSeconds), 0, 1)
            : 0;
    }

    private void OnSessionStopped()
    {
        ShowBatteryWarning = false;
        ShowModernStandbyInfo = false;
        ShowRebootWarning = false;
        ShowDegradedBanner = false;
        UpdateSessionDisplay();
    }

    private void UpdateDegradedBanner(DegradedReason reason)
    {
        ShowDegradedBanner = reason != DegradedReason.None;
        DegradedText = reason switch
        {
            DegradedReason.LidActionPolicy => Strings.Warn_LidUnmanaged,
            DegradedReason.WriteDenied => Strings.Warn_LidUnmanaged,
            DegradedReason.ThirdPartyConflict => Strings.Warn_LidUnmanaged,
            _ => string.Empty,
        };
    }

    private void DisplaySleepBanner(SleepPeriod period)
    {
        SleepBannerText = Strings.Format(
            Strings.Warn_SleepDetected,
            period.Start.ToLocalTime().ToString("HH:mm"),
            period.End.ToLocalTime().ToString("HH:mm"),
            FormatDuration(period.Duration));
        ShowSleepBanner = true;
    }

    private void UpdateClaudeDisplay()
    {
        CanInterruptClaude = _run is { HasExited: false };

        if (_run is { } run)
        {
            ClaudeRunSnapshot snapshot = run.Snapshot();
            ClaudeRunOutcome outcome = ClaudeRunClassifier.Classify(snapshot);
            ClaudeStateText = DescribeRun(snapshot, outcome);
            ClaudeBadgeText = outcome == ClaudeRunOutcome.Running
                ? Strings.Format(Strings.Claude_Badge_Running, DetectedProcesses.Count)
                : Strings.Claude_Badge_Idle;
            if (outcome != ClaudeRunOutcome.Running)
            {
                return;
            }
        }

        if (DetectedProcesses.Count == 0)
        {
            ClaudeStateText = _run is null ? Strings.Claude_Idle : ClaudeStateText;
            ClaudeBadgeText = Strings.Claude_Badge_Idle;
        }
        else if (_run is null)
        {
            ClaudeStateText = DetectedProcesses.Count == 1
                ? Strings.Claude_Running_One
                : Strings.Format(Strings.Claude_Running_Many, DetectedProcesses.Count);
            ClaudeBadgeText = Strings.Format(Strings.Claude_Badge_Running, DetectedProcesses.Count);
        }
    }

    private static string DescribeRun(ClaudeRunSnapshot run, ClaudeRunOutcome outcome) => outcome switch
    {
        ClaudeRunOutcome.Running when run.LastRetry is { } retry =>
            Strings.Format(Strings.Claude_Retry, retry.Attempt, retry.MaxRetries),
        ClaudeRunOutcome.Running => Strings.Claude_Running_One,
        ClaudeRunOutcome.Succeeded => Strings.Format(
            Strings.Claude_Done_Success,
            FormatRunDuration(run),
            run.Result?.NumTurns ?? 0,
            run.PermissionDenials),
        ClaudeRunOutcome.Interrupted => Strings.Claude_Done_Interrupted,
        ClaudeRunOutcome.UnexpectedStop => Strings.Claude_Error_Unexpected,
        _ => Strings.Format(Strings.Claude_Error, run.ExitCode ?? -1, run.LastErrorLine ?? run.Result?.Subtype ?? string.Empty),
    };

    private static string FormatRunDuration(ClaudeRunSnapshot run)
    {
        TimeSpan duration = (run.ExitedAt ?? run.StartedAt) - run.StartedAt;
        return FormatDuration(duration);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min" : $"{duration.Seconds} s";
    }

    private void UpdateTrayTooltip()
    {
        TrayTooltip = _services.Session.RemainingTime is { } remaining && IsSessionRunning
            ? Strings.Format(Strings.Tray_Tip_Active, FormatDuration(remaining))
            : Strings.Tray_Tip_Inactive;
    }

    private void AppendJournal(LogEntry entry)
    {
        _journal.Add(entry);
        while (_journal.Count > 500)
        {
            _journal.RemoveAt(0);
        }
    }

    private static bool ConfirmBypass()
        => MessageBox.Show(Strings.Dialog_Bypass, Strings.Dialog_BypassTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    private static void ShowMessage(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    partial void OnIsSessionRunningChanged(bool value) => OnPropertyChanged(nameof(OptionsEnabled));
}
