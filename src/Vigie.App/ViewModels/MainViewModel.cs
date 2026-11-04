using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vigie.App.Dialogs;
using Vigie.App.Resources;
using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Power;
using Vigie.Core.Session;

namespace Vigie.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(10);
    private const int JournalCapacity = 500;
    private const long VisibleScanInterval = 3;
    private const long HiddenScanInterval = 10;
    private const long LocateInterval = 30;
    private const long BeatInterval = 5;

    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<int> _vigieProcessIds = [];
    private readonly HashSet<string> _reportedOnce = [];
    private readonly ObservableCollection<LogEntry> _journal = [];
    private readonly bool _modernStandby;

    private PowerEventWindow? _powerWindow;
    private ClaudeRun? _run;
    private ExternalExit? _lastExternalExit;
    private int? _trackedProcessId;
    private IReadOnlySet<int> _aliveProcessIds = new HashSet<int>();
    private TimeSpan _sessionTotal;
    private bool _windowVisible = true;
    private long _tick;
    private long _lastScanTick = long.MinValue / 2;
    private long _lastLocateTick;
    private bool _lidClosed;

    public MainViewModel(AppServices services, Dispatcher dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;
        _modernStandby = services.Power.IsModernStandbyAvailable();
        services.Log.Info(SessionNarration.SleepModel(_modernStandby));

        Journal = new ReadOnlyObservableCollection<LogEntry>(_journal);
        Permissions = PermissionItem.All;
        SelectedPermission = Permissions[0];

        foreach (LogEntry entry in services.UiLog.Snapshot())
        {
            _journal.Add(entry);
        }

        services.UiLog.EntryWritten += (_, entry) => _dispatcher.BeginInvoke(() => AppendJournal(entry));
        services.Session.StateChanged += (_, _) => _dispatcher.BeginInvoke(UpdateSessionDisplay);
        services.Session.SleepDetected += (_, period) => _dispatcher.BeginInvoke(() => DisplaySleepBanner(period));
        services.Session.Stopped += (_, result) => _dispatcher.BeginInvoke(() => OnSessionStopped(result));

        RefreshPowerStatus();
        LocateClaude();
        UpdateSessionDisplay();
        UpdateClaudeDisplay();

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    /// <summary>A short message for the notification area, such as the
    /// battery threshold being reached.</summary>
    public event EventHandler<string>? NotificationRequested;

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
    public partial int TaskCapHours { get; set; } = 8;

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
    public partial bool ModernStandbyCompatibility { get; set; }

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
    public partial ClaudeActivity ClaudeActivity { get; set; }

    [ObservableProperty]
    public partial bool ClaudeAvailable { get; set; }

    [ObservableProperty]
    public partial bool IsLaunching { get; set; }

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

    public bool LockOptionEnabled => OptionsEnabled && !ModernStandbyCompatibility;

    public bool CanLaunchClaude => ClaudeAvailable && !IsLaunching;

    public bool IsClaudeRunning => _run is { HasExited: false };

    public void AttachWindow(HwndSource source)
    {
        _powerWindow = new PowerEventWindow(source);
        _powerWindow.SettingChanged += (_, broadcast) => _dispatcher.BeginInvoke(() => OnPowerBroadcast(broadcast));
        _powerWindow.Resumed += (_, _) => _dispatcher.BeginInvoke(_services.Session.OnResume);
    }

    public void SetWindowVisible(bool visible) => _windowVisible = visible;

    /// <summary>
    /// Deals with a journal left by a session that did not end normally: it is
    /// restored unless its owner still runs, and an unreadable one is shown as
    /// it is, with nothing written. Returns the journal state that remains.
    /// </summary>
    public JournalStatus HandleLeftoverJournal()
    {
        LeftoverResult result = _services.Leftover.RestoreIfOrphaned();
        switch (result.Status)
        {
            case LeftoverStatus.Restored:
                RestoreReport report = result.Report!;
                _services.Log.Warning(SessionNarration.LeftoverRestored());
                foreach (string line in SessionNarration.Restored(report))
                {
                    _services.Log.Info(line);
                }

                ShowMessage(
                    report.Complete
                        ? Strings.Format(Strings.Dialog_Restored, SessionNarration.RestoredSummary(report))
                        : Strings.Dialog_UnrestoredJournal,
                    Strings.Dialog_RestoredTitle);
                break;
            case LeftoverStatus.Unreadable:
                _services.Log.Error(SessionNarration.JournalUnreadable());
                JournalUnreadableDialog.Show(result.RawContent ?? string.Empty);
                break;
        }

        return _services.JournalStore.Read().Status;
    }

    public void StopForQuit() => _services.Session.Stop(StopReason.UserStopped);

    public void ShutDown(StopReason reason)
    {
        _timer.Stop();
        _services.Session.Stop(reason);
        _powerWindow?.Dispose();
        _powerWindow = null;
    }

    [RelayCommand]
    private void StartSession()
    {
        if (!TryBuildOptions(out SessionOptions options))
        {
            ShowMessage(Strings.Dialog_InvalidOptions, Strings.AppTitle);
            return;
        }

        JournalStatus journal = HandleLeftoverJournal();
        PowerStatus status = _services.Power.GetPowerStatus();
        var preconditions = new SessionPreconditions(
            _modernStandby,
            status,
            PowerPolicies.Read(_services.Registry),
            TrackedProcessAlive(),
            journal);

        StartResult result = _services.Session.Start(options, preconditions, TrackedProcessAlive);
        string? refusal = result.Refusal switch
        {
            StartRefusal.None => null,
            StartRefusal.KeepAwakeBlockedByPolicy => Strings.Warn_RequestsBlocked,
            StartRefusal.UnrestoredJournal => Strings.Dialog_UnrestoredJournal,
            StartRefusal.TrackedProcessGone => Strings.Claude_TargetGone,
            _ => Strings.Format(Strings.Dialog_StartFailed, result.Detail ?? string.Empty),
        };

        if (refusal is not null)
        {
            ShowMessage(refusal, Strings.Dialog_RequestsBlockedTitle);
            return;
        }

        _sessionTotal = options.EffectiveCeiling;
        ShowRebootWarning = preconditions.Policies.RebootPending;
        ShowModernStandbyInfo = _modernStandby;
        ShowBatteryWarning = status.OnBattery;
        ShowSleepBanner = false;
        UpdateDegradedBanner(result.Degraded);
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

        if (IsAutonomous && string.IsNullOrWhiteSpace(Prompt))
        {
            ShowMessage(Strings.Claude_PromptRequired, Strings.AppTitle);
            return;
        }

        if (!CommandLineSplitter.TrySplit(ExtraArguments, out IReadOnlyList<string> extra))
        {
            ShowMessage(Strings.Claude_ArgumentsInvalid, Strings.AppTitle);
            return;
        }

        if (ClaudeCommand.FindReservedArgument(extra) is string reserved)
        {
            ShowMessage(Strings.Format(Strings.Claude_ArgumentReserved, reserved), Strings.AppTitle);
            return;
        }

        // Located again right before the launch, so a file swapped since the
        // last check is caught.
        if (LocateClaude() is not { } installation)
        {
            ShowMessage(Strings.Format(Strings.Claude_NotFound, Strings.Claude_InstallUrl), Strings.AppTitle);
            return;
        }

        IsLaunching = true;
        try
        {
            PreflightResult preflight = await _services.Preflight.CheckAsync(installation, CancellationToken.None);
            if (!HandlePreflight(preflight))
            {
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
            IsLaunching = false;
        }
    }

    [RelayCommand]
    private async Task InterruptClaude()
    {
        if (_run is { } run && !run.HasExited)
        {
            CanInterruptClaude = false;
            InterruptOutcome outcome = await run.InterruptAsync(InterruptGrace);
            _services.Log.Info(ClaudeNarration.Interrupted(outcome));
            UpdateClaudeDisplay();
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
        _services.FileSystem.CreateDirectory(_services.Storage.LogDirectory);
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

    private void OnTick()
    {
        _tick++;

        if (_tick - _lastScanTick >= (_windowVisible ? VisibleScanInterval : HiddenScanInterval))
        {
            _lastScanTick = _tick;
            RefreshProcesses();
        }

        if (_tick - _lastLocateTick >= LocateInterval)
        {
            LocateClaude();
        }

        if (_services.Session.IsRunning)
        {
            if (_tick % BeatInterval == 0)
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

    private void StartRun(ClaudeInstallation installation, ClaudeLaunchRequest request, ClaudeLaunchMode mode)
    {
        _run?.Dispose();
        string? runDirectory = mode == ClaudeLaunchMode.Autonomous ? _services.Storage.CreateRunDirectory(_services.Clock.Now) : null;
        ClaudeRun run = ClaudeRun.Start(_services.Processes, _services.FileSystem, _services.Clock, ClaudeCommand.Build(installation, request), mode, runDirectory);
        _run = run;
        _vigieProcessIds.Add(run.ProcessId);
        _trackedProcessId = run.ProcessId;

        run.EventReceived += (_, streamEvent) => _dispatcher.BeginInvoke(() => OnRunEvent(streamEvent));
        run.Exited += (_, _) => _dispatcher.BeginInvoke(() => OnRunExited(run));

        _services.Log.Info(ClaudeNarration.Launched(run.ProcessId, mode));
        if (runDirectory is not null)
        {
            _services.Log.Info(ClaudeNarration.OutputRecordedIn(runDirectory));
        }

        RefreshProcesses();
    }

    private void OnRunEvent(ClaudeStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case SessionInitEvent { SessionId: { } sessionId }:
                _services.Log.Info(ClaudeNarration.SessionIdentified(sessionId));
                break;
            case ApiRetryEvent retry:
                _services.Log.Warning(ClaudeNarration.Retry(retry));
                break;
            case PermissionDeniedEvent denied:
                _services.Log.Warning(ClaudeNarration.PermissionDenied(denied));
                break;
            case ResultEvent result:
                _services.Log.Write(result.Succeeded ? LogLevel.Info : LogLevel.Warning, ClaudeNarration.Result(result));
                break;
        }

        UpdateClaudeDisplay();
    }

    private void OnRunExited(ClaudeRun run)
    {
        ClaudeRunSnapshot snapshot = run.Snapshot();
        _services.Log.Info(ClaudeNarration.Exited(snapshot.ProcessId, snapshot.ExitCode ?? -1));
        if (snapshot.RecordingError is { } error)
        {
            _services.Log.Warning(ClaudeNarration.RecordingFailed(error));
        }

        RefreshProcesses();
    }

    private bool HandlePreflight(PreflightResult preflight)
    {
        string? problem = preflight.Status switch
        {
            PreflightStatus.Ready => null,
            PreflightStatus.NotLoggedIn => Strings.Claude_NotLoggedIn,
            PreflightStatus.NotExecutable => Strings.Format(Strings.Claude_NotFound, Strings.Claude_InstallUrl),
            _ => Strings.Claude_CheckFailed,
        };

        if (problem is not null)
        {
            ShowMessage(problem, Strings.AppTitle);
        }

        return problem is null;
    }

    private bool TryBuildOptions(out SessionOptions options)
    {
        bool untilTask = SelectedDuration == DurationChoice.UntilTask;
        if (untilTask)
        {
            _trackedProcessId = SelectedTaskTarget?.ProcessId ?? _trackedProcessId;
        }

        options = new SessionOptions
        {
            DurationKind = untilTask ? SessionDurationKind.UntilTask : SessionDurationKind.Fixed,
            FixedDuration = DurationFromChoice(),
            TaskModeCap = TimeSpan.FromHours(TaskCapHours),
            KeepDisplayOn = KeepDisplayOn,
            WorkWithLidClosed = WorkWithLidClosed,
            LockOnLidClose = LockOnLidClose,
            SuspendAtEndIfLidClosed = SuspendAtEnd,
            BatteryStopThresholdPercent = BatteryThreshold,
            ModernStandbyCompatibility = ModernStandbyCompatibility,
        };

        return TaskCapHours is >= 1 and <= 24 && options.IsValid(out _);
    }

    private TimeSpan DurationFromChoice() => SelectedDuration switch
    {
        DurationChoice.FifteenMinutes => TimeSpan.FromMinutes(15),
        DurationChoice.OneHour => TimeSpan.FromHours(1),
        DurationChoice.FourHours => TimeSpan.FromHours(4),
        DurationChoice.Custom => TimeSpan.FromMinutes(CustomMinutes),
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

    private ClaudeInstallation? LocateClaude()
    {
        _lastLocateTick = _tick;
        string? project = string.IsNullOrWhiteSpace(ProjectFolder) ? null : ProjectFolder;
        ClaudeLocateResult located = _services.Locator.Locate(userPath: null, project);
        foreach (RejectedCandidate candidate in located.Rejected)
        {
            ReportOnce($"{candidate.Path}|{candidate.Reason}", ClaudeNarration.CandidateRejected(candidate), LogLevel.Info);
        }

        ClaudeAvailable = located.Installation is not null;
        return located.Installation;
    }

    private void RefreshProcesses()
    {
        MonitorScan scan = _services.Monitor.Scan(_vigieProcessIds);
        _aliveProcessIds = scan.Processes.Select(p => p.ProcessId).ToHashSet();
        if (scan.NodeScanError is { } error)
        {
            ReportOnce($"node|{error}", ClaudeNarration.NodeScanFailed(error), LogLevel.Warning);
        }

        foreach (ExternalExit exit in scan.ExternalExits)
        {
            _services.Log.Info(ClaudeNarration.ExternalExited(exit));
            _lastExternalExit = exit;
        }

        int? selected = SelectedTaskTarget?.ProcessId;
        DetectedProcesses.Clear();
        TaskTargets.Clear();
        foreach (ClaudeProcessInfo process in scan.Processes)
        {
            ClaudeProcessItem item = ClaudeProcessItem.From(process);
            DetectedProcesses.Add(item);
            TaskTargets.Add(item);
        }

        // The task Vigie launched is offered first.
        SelectedTaskTarget = TaskTargets.FirstOrDefault(t => t.ProcessId == selected)
            ?? TaskTargets.FirstOrDefault(t => t.ProcessId == _trackedProcessId)
            ?? TaskTargets.FirstOrDefault();
        UpdateClaudeDisplay();
    }

    private void OnPowerBroadcast(PowerBroadcast broadcast)
    {
        if (broadcast.Setting == PowerKnownSettings.AcDcPowerSource)
        {
            PowerStatus status = _services.Power.GetPowerStatus();
            _services.Session.OnPowerSourceChanged(status);
            if (IsSessionRunning)
            {
                ShowBatteryWarning = status.OnBattery;
            }
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
        var parts = new List<string> { status.OnBattery ? Strings.Power_OnBattery : Strings.Power_Ac };
        if (status.BatteryPercent is int percent)
        {
            parts.Add(Strings.Format(Strings.Power_Battery, percent));
        }

        parts.Add(_lidClosed ? Strings.Power_LidClosed : Strings.Power_LidOpen);
        parts.Add(_modernStandby ? Strings.Power_ModernStandby : Strings.Power_LegacySleep);

        if (status.BatterySaverOn)
        {
            parts.Add(Strings.Power_BatterySaver);
        }

        PowerStatusText = string.Join(" · ", parts);
    }

    private void UpdateSessionDisplay()
    {
        IsSessionRunning = _services.Session.IsRunning;
        SessionBadgeText = IsSessionRunning ? Strings.Badge_Active : Strings.Badge_Inactive;

        if (!IsSessionRunning)
        {
            SessionStateText = _services.Session.State == SessionState.Stopping ? Strings.State_Stopping : Strings.State_Inactive;
            Progress = 0;
            return;
        }

        if (_services.Session.Degraded == DegradedReason.ThirdPartyConflict)
        {
            UpdateDegradedBanner(DegradedReason.ThirdPartyConflict);
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        if (_services.Session.RemainingTime is not TimeSpan remaining || _services.Session.EndTime is not DateTimeOffset end)
        {
            return;
        }

        string endLabel = end.ToLocalTime().ToString("HH:mm");
        if (_services.Session.Degraded != DegradedReason.None)
        {
            SessionStateText = Strings.State_Degraded;
        }
        else if (SelectedDuration == DurationChoice.UntilTask)
        {
            SessionStateText = Strings.Format(Strings.State_ActiveUntilTask, endLabel);
        }
        else
        {
            SessionStateText = Strings.Format(Strings.State_Active, FormatClock(remaining), endLabel);
        }

        Progress = _sessionTotal > TimeSpan.Zero
            ? Math.Clamp(1 - (remaining.TotalSeconds / _sessionTotal.TotalSeconds), 0, 1)
            : 0;
    }

    private void OnSessionStopped(StopResult result)
    {
        ShowBatteryWarning = false;
        ShowModernStandbyInfo = false;
        ShowRebootWarning = false;
        ShowDegradedBanner = false;
        if (result.Reason == StopReason.BatteryThreshold)
        {
            int percent = _services.Power.GetPowerStatus().BatteryPercent ?? BatteryThreshold;
            NotificationRequested?.Invoke(this, Strings.Format(Strings.Warn_BatteryThreshold, percent));
        }

        UpdateSessionDisplay();
    }

    private void UpdateDegradedBanner(DegradedReason reason)
    {
        ShowDegradedBanner = reason != DegradedReason.None;
        DegradedText = reason == DegradedReason.LidActionPolicy ? Strings.Warn_LidUnmanaged : Strings.Warn_LidNotChanged;
    }

    private void DisplaySleepBanner(SleepPeriod period)
    {
        SleepBannerText = Strings.Format(
            Strings.Warn_SleepDetected,
            period.Start.ToLocalTime().ToString("HH:mm"),
            period.End.ToLocalTime().ToString("HH:mm"),
            FormatShort(period.Duration));
        ShowSleepBanner = true;
    }

    private void UpdateClaudeDisplay()
    {
        CanInterruptClaude = _run is { HasExited: false };
        int running = DetectedProcesses.Count;

        if (_run is { } run)
        {
            ClaudeRunSnapshot snapshot = run.Snapshot();
            ClaudeRunOutcome outcome = ClaudeRunClassifier.Classify(snapshot);
            ClaudeStateText = DescribeRun(snapshot, outcome, running);
            ClaudeActivity = outcome switch
            {
                ClaudeRunOutcome.Running => ClaudeActivity.Running,
                ClaudeRunOutcome.Succeeded or ClaudeRunOutcome.Interrupted => running > 0 ? ClaudeActivity.Running : ClaudeActivity.Done,
                _ => running > 0 ? ClaudeActivity.Running : ClaudeActivity.Error,
            };
        }
        else if (running > 0)
        {
            ClaudeStateText = RunningText(running);
            ClaudeActivity = ClaudeActivity.Running;
        }
        else if (_lastExternalExit is { } exit)
        {
            ClaudeStateText = exit.ExitCode is int code ? Strings.Format(Strings.Claude_Done_External, code) : Strings.Claude_Done_Unknown;
            ClaudeActivity = exit.ExitCode is null or 0 ? ClaudeActivity.Done : ClaudeActivity.Error;
        }
        else
        {
            ClaudeStateText = Strings.Claude_Idle;
            ClaudeActivity = ClaudeActivity.Idle;
        }

        ClaudeBadgeText = ClaudeActivity switch
        {
            ClaudeActivity.Running => Strings.Format(Strings.Claude_Badge_Running, Math.Max(running, 1)),
            ClaudeActivity.Done => Strings.Claude_Badge_Done,
            ClaudeActivity.Error => Strings.Claude_Badge_Error,
            _ => Strings.Claude_Badge_Idle,
        };
    }

    private static string DescribeRun(ClaudeRunSnapshot run, ClaudeRunOutcome outcome, int running) => outcome switch
    {
        ClaudeRunOutcome.Running when run.LastRetry is { } retry => Strings.Format(Strings.Claude_Retry, retry.Attempt, retry.MaxRetries),
        ClaudeRunOutcome.Running => RunningText(Math.Max(running, 1)),
        ClaudeRunOutcome.Succeeded => Strings.Format(
            Strings.Claude_Done_Success,
            FormatShort(RunDuration(run)),
            run.Result?.NumTurns ?? 0,
            run.PermissionDenials),
        ClaudeRunOutcome.Interrupted => Strings.Claude_Done_Interrupted,
        ClaudeRunOutcome.UnexpectedStop => Strings.Claude_Error_Unexpected,
        _ => Strings.Format(Strings.Claude_Error, run.ExitCode ?? -1, run.LastErrorLine ?? run.Result?.Errors.FirstOrDefault() ?? run.Result?.Subtype ?? string.Empty),
    };

    private static string RunningText(int running)
        => running == 1 ? Strings.Claude_Running_One : Strings.Format(Strings.Claude_Running_Many, running);

    // The result's own duration is preferred: it excludes the time the
    // process spends winding down background commands.
    private static TimeSpan RunDuration(ClaudeRunSnapshot run)
        => run.Result?.DurationMs is long milliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : (run.ExitedAt ?? run.StartedAt) - run.StartedAt;

    private static string FormatClock(TimeSpan duration)
        => $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    private static string FormatShort(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.Minutes > 0 ? $"{(int)duration.TotalHours} h {duration.Minutes:00}" : $"{(int)duration.TotalHours} h";
        }

        return duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min" : $"{duration.Seconds} s";
    }

    private void UpdateTrayTooltip()
    {
        TrayTooltip = IsSessionRunning && _services.Session.RemainingTime is TimeSpan remaining
            ? Strings.Format(Strings.Tray_Tip_Active, FormatShort(remaining))
            : Strings.Tray_Tip_Inactive;
    }

    private void ReportOnce(string key, string message, LogLevel level)
    {
        if (_reportedOnce.Add(key))
        {
            _services.Log.Write(level, message);
        }
    }

    private void AppendJournal(LogEntry entry)
    {
        _journal.Add(entry);
        while (_journal.Count > JournalCapacity)
        {
            _journal.RemoveAt(0);
        }
    }

    private static bool ConfirmBypass()
        => MessageBox.Show(Strings.Dialog_Bypass, Strings.Dialog_BypassTitle, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            == MessageBoxResult.OK;

    private static void ShowMessage(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    partial void OnIsSessionRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(OptionsEnabled));
        OnPropertyChanged(nameof(LockOptionEnabled));
    }

    partial void OnModernStandbyCompatibilityChanged(bool value)
    {
        if (value)
        {
            LockOnLidClose = true;
        }

        OnPropertyChanged(nameof(LockOptionEnabled));
    }

    partial void OnClaudeAvailableChanged(bool value) => OnPropertyChanged(nameof(CanLaunchClaude));

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(CanLaunchClaude));

    partial void OnProjectFolderChanged(string value) => LocateClaude();
}
