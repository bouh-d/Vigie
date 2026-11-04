using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vigie.App.Dialogs;
using Vigie.App.Resources;
using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;
using Vigie.Core.Session;
using Vigie.Core.Storage;

namespace Vigie.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActivityPollInterval = TimeSpan.FromMilliseconds(200);
    private const int JournalCapacity = 500;
    private const long VisibleScanInterval = 3;
    private const long HiddenScanInterval = 10;
    private const long LocateInterval = 30;
    private const long BeatInterval = 5;

    private readonly AppServices _services;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _activityTimer;
    private readonly HashSet<int> _vigieProcessIds = [];
    private readonly HashSet<string> _reportedOnce = [];
    private readonly ObservableCollection<LogEntry> _journal = [];
    private readonly bool _modernStandby;

    private PowerEventWindow? _powerWindow;
    private GlobalShortcut? _shortcut;
    private ClaudeRun? _run;
    private ExternalExit? _lastExternalExit;
    private int? _trackedProcessId;
    private IReadOnlyDictionary<int, ClaudeProcessInfo> _aliveProcesses = new Dictionary<int, ClaudeProcessInfo>();
    private long _sessionStartTick;
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
        ApplySettings(services.Settings.Load());

        foreach (LogEntry entry in services.UiLog.Snapshot())
        {
            _journal.Add(entry);
        }

        services.UiLog.EntryWritten += (_, entry) => _dispatcher.BeginInvoke(() => AppendJournal(entry));
        services.Session.StateChanged += (_, _) => _dispatcher.BeginInvoke(UpdateSessionDisplay);
        services.Session.SleepDetected += (_, period) => _dispatcher.BeginInvoke(() => DisplaySleepBanner(period));
        services.Session.Stopped += (_, result) => _dispatcher.BeginInvoke(() => OnSessionStopped(result));
        services.ActivityLock.StateChanged += (_, _) => OnActivityLockChanged();
        SystemEvents.SessionSwitch += OnSessionSwitch;

        RefreshPowerStatus();
        LocateClaude();
        UpdateSessionDisplay();
        UpdateClaudeDisplay();
        UpdateActivityLockStatus();

        _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();

        // Runs only while the lock is armed, so a mouse movement is answered
        // within a fraction of a second.
        _activityTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = ActivityPollInterval };
        _activityTimer.Tick += (_, _) => _services.ActivityLock.Poll();
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

    public IReadOnlyList<DurationItem> Durations { get; } = DurationItem.All;

    [ObservableProperty]
    public partial string SessionStateText { get; set; } = string.Empty;

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
    public partial bool ActivityLockEnabled { get; set; }

    [ObservableProperty]
    public partial ActivityResponse ActivityResponse { get; set; }

    [ObservableProperty]
    public partial Shortcut ActivityShortcut { get; set; } = Shortcut.Default;

    [ObservableProperty]
    public partial bool ShortcutRejected { get; set; }

    [ObservableProperty]
    public partial string ActivityLockStatusText { get; set; } = string.Empty;

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

    public bool IsCustomDuration => SelectedDuration == DurationChoice.Custom;

    public bool IsUntilTask => SelectedDuration == DurationChoice.UntilTask;

    public string ActivityLockLabel => ActivityResponse == ActivityResponse.Lock ? Strings.Opt_ActivityLock_Lock : Strings.Opt_ActivityLock_Sleep;

    public bool IsSleepResponse => ActivityResponse == ActivityResponse.Sleep;

    public string ActivityShortcutText => ActivityShortcut.ToString();

    public void AttachWindow(HwndSource source)
    {
        _powerWindow = new PowerEventWindow(source);
        _powerWindow.SettingChanged += (_, broadcast) => _dispatcher.BeginInvoke(() => OnPowerBroadcast(broadcast));
        _powerWindow.Resumed += (_, _) => _dispatcher.BeginInvoke(_services.Session.OnResume);

        _shortcut = new GlobalShortcut(source);
        _shortcut.Pressed += (_, _) => _dispatcher.BeginInvoke(OnActivityShortcut);
    }

    /// <summary>Takes the combination typed in the shortcut field, if it is one
    /// that can be registered.</summary>
    public void TrySetActivityShortcut(ShortcutModifiers modifiers, int virtualKey)
    {
        var shortcut = new Shortcut(modifiers, virtualKey);
        ShortcutRejected = !shortcut.IsValid();
        if (!ShortcutRejected)
        {
            ActivityShortcut = shortcut;
        }
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
        _activityTimer.Stop();
        _services.Session.Stop(reason);
        SaveSettings();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _shortcut?.Dispose();
        _shortcut = null;
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

        SaveSettings();

        // Registered before anything else, so a combination another
        // application holds is reported while the user is still there.
        if (ActivityLockEnabled && _shortcut?.TryRegister(ActivityShortcut) != true)
        {
            ShowMessage(Strings.Format(Strings.Dialog_ShortcutTaken, ActivityShortcut), Strings.Dialog_RequestsBlockedTitle);
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
            _shortcut?.Unregister();
            ShowMessage(refusal, Strings.Dialog_RequestsBlockedTitle);
            return;
        }

        if (ActivityLockEnabled)
        {
            _services.ActivityLock.Start(ActivityResponse, ActivityShortcut);
        }

        _sessionTotal = options.EffectiveCeiling;
        _sessionStartTick = _services.Clock.TickCountMilliseconds;
        ShowRebootWarning = preconditions.Policies.RebootPending;
        ShowModernStandbyInfo = _modernStandby && status.OnBattery;
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

        if (_services.ActivityLock.State == ActivityLockState.Arming)
        {
            _services.ActivityLock.Poll();
            UpdateActivityLockStatus();
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

        bool open = _aliveProcesses.ContainsKey(pid) || (_run is { HasExited: false } run && run.ProcessId == pid);
        if (!open)
        {
            return false;
        }

        // A session that stays open after its task, such as a tab of the
        // desktop app, ends the task by going quiet rather than by exiting.
        if (_services.Session.IsRunning
            && _aliveProcesses.TryGetValue(pid, out ClaudeProcessInfo? process)
            && ClaudeProcessMonitor.TaskLooksFinished(process, _sessionStartTick, _services.Clock.TickCountMilliseconds))
        {
            ReportOnce($"idle|{pid}|{_sessionStartTick}", ClaudeNarration.TaskIdle(pid), LogLevel.Info);
            return false;
        }

        return true;
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
        var alive = new Dictionary<int, ClaudeProcessInfo>(scan.Processes.Count);
        foreach (ClaudeProcessInfo process in scan.Processes)
        {
            alive[process.ProcessId] = process;
        }

        _aliveProcesses = alive;
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
                ShowModernStandbyInfo = _modernStandby && status.OnBattery;
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
        _services.ActivityLock.Stop();
        _shortcut?.Unregister();
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
        int working = DetectedProcesses.Count(p => !p.IsIdle);
        int idle = DetectedProcesses.Count - working;

        if (_run is { } run)
        {
            ClaudeRunSnapshot snapshot = run.Snapshot();
            ClaudeRunOutcome outcome = ClaudeRunClassifier.Classify(snapshot);
            ClaudeStateText = DescribeRun(snapshot, outcome, working, idle);
            ClaudeActivity = outcome switch
            {
                ClaudeRunOutcome.Running => ClaudeActivity.Running,
                ClaudeRunOutcome.Succeeded or ClaudeRunOutcome.Interrupted => working > 0 ? ClaudeActivity.Running : ClaudeActivity.Done,
                _ => working > 0 ? ClaudeActivity.Running : ClaudeActivity.Error,
            };
        }
        else if (working + idle > 0)
        {
            ClaudeStateText = RunningText(working, idle);
            ClaudeActivity = working > 0 ? ClaudeActivity.Running : ClaudeActivity.Idle;
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
    }

    private static string DescribeRun(ClaudeRunSnapshot run, ClaudeRunOutcome outcome, int working, int idle) => outcome switch
    {
        ClaudeRunOutcome.Running when run.LastRetry is { } retry => Strings.Format(Strings.Claude_Retry, retry.Attempt, retry.MaxRetries),
        ClaudeRunOutcome.Running => RunningText(Math.Max(working, 1), idle),
        ClaudeRunOutcome.Succeeded => Strings.Format(
            Strings.Claude_Done_Success,
            FormatShort(RunDuration(run)),
            run.Result?.NumTurns ?? 0,
            run.PermissionDenials),
        ClaudeRunOutcome.Interrupted => Strings.Claude_Done_Interrupted,
        ClaudeRunOutcome.UnexpectedStop => Strings.Claude_Error_Unexpected,
        _ => Strings.Format(Strings.Claude_Error, run.ExitCode ?? -1, run.LastErrorLine ?? run.Result?.Errors.FirstOrDefault() ?? run.Result?.Subtype ?? string.Empty),
    };

    private static string RunningText(int working, int idle) => (working, idle) switch
    {
        (0, 1) => Strings.Claude_Waiting_One,
        (0, _) => Strings.Format(Strings.Claude_Waiting_Many, idle),
        (1, 0) => Strings.Claude_Running_One,
        (_, 0) => Strings.Format(Strings.Claude_Running_Many, working),
        _ => Strings.Format(Strings.Claude_Mixed, working, idle),
    };

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
            return duration.Minutes > 0 ? $"{(int)duration.TotalHours} h {duration.Minutes} min" : $"{(int)duration.TotalHours} h";
        }

        return duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes} min" : $"{duration.Seconds} s";
    }

    private void UpdateTrayTooltip()
    {
        TrayTooltip = IsSessionRunning && _services.Session.RemainingTime is TimeSpan remaining
            ? Strings.Format(Strings.Tray_Tip_Active, FormatShort(remaining))
            : Strings.Tray_Tip_Inactive;
    }

    private void OnActivityShortcut()
    {
        ActivityLock activityLock = _services.ActivityLock;
        if (activityLock.State == ActivityLockState.Off)
        {
            return;
        }

        activityLock.OnShortcut();
        NotificationRequested?.Invoke(
            this,
            activityLock.State == ActivityLockState.Disarmed
                ? Strings.Notify_ActivityDisarmed
                : Strings.Format(Strings.Notify_ActivityRearmed, (int)ActivityLock.QuietPeriod.TotalSeconds));
    }

    private void OnActivityLockChanged()
    {
        _activityTimer.IsEnabled = _services.ActivityLock.IsWatching;
        UpdateActivityLockStatus();
    }

    private void UpdateActivityLockStatus()
    {
        ActivityLock activityLock = _services.ActivityLock;
        string shortcut = ActivityShortcut.ToString();
        ActivityLockStatusText = activityLock.State switch
        {
            ActivityLockState.Arming => Strings.Format(Strings.ActivityLock_Arming, (int)Math.Ceiling(activityLock.TimeUntilArmed.TotalSeconds), shortcut),
            ActivityLockState.Armed or ActivityLockState.AwaitingShortcut => Strings.Format(Strings.ActivityLock_Armed, shortcut),
            ActivityLockState.Disarmed => Strings.Format(Strings.ActivityLock_Disarmed, shortcut),
            _ => Strings.Format(Strings.ActivityLock_Idle, shortcut, (int)ActivityLock.QuietPeriod.TotalSeconds),
        };
    }

    // Raised on a system events thread, not the UI thread.
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _dispatcher.BeginInvoke(_services.ActivityLock.OnWorkstationLocked);
        }
    }

    private void ApplySettings(UserSettings settings)
    {
        DurationChoice fixedChoice = settings.DurationMinutes switch
        {
            15 => DurationChoice.FifteenMinutes,
            60 => DurationChoice.OneHour,
            120 => DurationChoice.TwoHours,
            240 => DurationChoice.FourHours,
            _ => DurationChoice.Custom,
        };

        if (fixedChoice == DurationChoice.Custom)
        {
            CustomMinutes = settings.DurationMinutes;
        }

        SelectedDuration = settings.UntilTask ? DurationChoice.UntilTask : fixedChoice;
        TaskCapHours = settings.TaskCapHours;
        WorkWithLidClosed = settings.WorkWithLidClosed;
        KeepDisplayOn = settings.KeepDisplayOn;
        LockOnLidClose = settings.LockOnLidClose;
        SuspendAtEnd = settings.SuspendAtEnd;
        BatteryThreshold = settings.BatteryThresholdPercent;
        ModernStandbyCompatibility = settings.ModernStandbyCompatibility;
        ActivityLockEnabled = settings.ActivityLockEnabled;
        ActivityResponse = settings.ActivityResponse;
        ActivityShortcut = settings.ActivityShortcut;
    }

    private UserSettings CurrentSettings() => new()
    {
        DurationMinutes = SelectedDuration is DurationChoice.Custom or DurationChoice.UntilTask ? CustomMinutes : (int)DurationFromChoice().TotalMinutes,
        UntilTask = SelectedDuration == DurationChoice.UntilTask,
        TaskCapHours = TaskCapHours,
        WorkWithLidClosed = WorkWithLidClosed,
        KeepDisplayOn = KeepDisplayOn,
        LockOnLidClose = LockOnLidClose,
        SuspendAtEnd = SuspendAtEnd,
        BatteryThresholdPercent = BatteryThreshold,
        ModernStandbyCompatibility = ModernStandbyCompatibility,
        ActivityLockEnabled = ActivityLockEnabled,
        ActivityResponse = ActivityResponse,
        ActivityShortcut = ActivityShortcut,
    };

    private void SaveSettings()
    {
        try
        {
            _services.Settings.Save(CurrentSettings());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _services.Log.Warning(SessionNarration.SettingsNotSaved(exception.Message));
        }
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

    partial void OnSelectedDurationChanged(DurationChoice value)
    {
        OnPropertyChanged(nameof(IsCustomDuration));
        OnPropertyChanged(nameof(IsUntilTask));
    }

    partial void OnActivityResponseChanged(ActivityResponse value)
    {
        OnPropertyChanged(nameof(ActivityLockLabel));
        OnPropertyChanged(nameof(IsSleepResponse));
    }

    partial void OnActivityShortcutChanged(Shortcut value)
    {
        OnPropertyChanged(nameof(ActivityShortcutText));
        UpdateActivityLockStatus();
    }

    partial void OnClaudeAvailableChanged(bool value) => OnPropertyChanged(nameof(CanLaunchClaude));

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(CanLaunchClaude));

    partial void OnProjectFolderChanged(string value) => LocateClaude();
}
