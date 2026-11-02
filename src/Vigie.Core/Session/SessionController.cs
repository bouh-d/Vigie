using System.ComponentModel;
using System.Security;
using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;

namespace Vigie.Core.Session;

/// <summary>
/// The wake session as an explicit state machine. Every transition is
/// journaled and every exit runs the restore. It is driven from a single
/// thread: the UI thread in the application, the test thread in tests. The
/// caller feeds it a beat every few seconds and forwards the power events it
/// receives from the window.
/// </summary>
public sealed class SessionController
{
    private const string RequestReason = "Vigie — session d'éveil active";
    private const int ConsoleDisplayOn = 1;

    private static readonly TimeSpan DriftCheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SuspendIdleThreshold = TimeSpan.FromMinutes(2);

    private readonly IPowerApi _power;
    private readonly PowerSettingsGuard _guard;
    private readonly ICrashRecovery _crashRecovery;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly IEnvironment _environment;
    private readonly List<SleepPeriod> _sleeps = [];
    private readonly SleepDetector _sleepDetector;

    private SessionOptions? _options;
    private IPowerRequest? _request;
    private Deadline? _deadline;
    private Func<bool>? _trackedProcessAlive;
    private bool _crashRecoveryArmed;
    private long _startTick;
    private long _lastDriftCheckTick;
    private long? _trackedExitTick;
    private bool _lidClosed;
    private int? _consoleDisplayState;

    public SessionController(
        IPowerApi power,
        PowerSettingsGuard guard,
        ICrashRecovery crashRecovery,
        ILog log,
        IClock clock,
        IEnvironment environment)
    {
        _power = power;
        _guard = guard;
        _crashRecovery = crashRecovery;
        _log = log;
        _clock = clock;
        _environment = environment;
        _sleepDetector = new SleepDetector(clock);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<SleepPeriod>? SleepDetected;

    public event EventHandler? Resumed;

    public event EventHandler<StopResult>? Stopped;

    public SessionState State { get; private set; } = SessionState.Inactive;

    public DegradedReason Degraded { get; private set; } = DegradedReason.None;

    public bool IsRunning => State is SessionState.Active or SessionState.Degraded;

    public IReadOnlyList<SleepPeriod> Sleeps => _sleeps;

    public TimeSpan? RemainingTime => _deadline?.Remaining(_clock);

    public DateTimeOffset? EndTime => _deadline?.EndTime(_clock);

    public StartResult Start(SessionOptions options, SessionPreconditions preconditions, Func<bool>? trackedProcessAlive)
    {
        if (State != SessionState.Inactive)
        {
            throw new InvalidOperationException("A session is already running.");
        }

        StartRefusal refusal = CheckPreconditions(options, preconditions);
        if (refusal != StartRefusal.None)
        {
            _log.Warning(SessionNarration.StartRefused(refusal));
            return Refused(refusal);
        }

        State = SessionState.Starting;
        _options = options;
        _trackedProcessAlive = trackedProcessAlive;
        _sleeps.Clear();
        _log.Info(SessionNarration.Started(options));
        if (preconditions.Policies.RebootPending)
        {
            _log.Warning(SessionNarration.RebootPending());
        }

        DegradedReason degraded = DegradedReason.None;
        bool manageLid = options.WorkWithLidClosed;
        if (manageLid && preconditions.Policies.LidActionEnforced)
        {
            manageLid = false;
            degraded = DegradedReason.LidActionPolicy;
        }

        bool manageStandby = preconditions.ModernStandby;
        if (manageStandby)
        {
            _log.Info(SessionNarration.ModernStandbyOnBattery());
        }

        try
        {
            _request = _power.CreateSystemRequest(RequestReason, DisplayRequiredFor(options, _lidClosed));
        }
        catch (Exception exception) when (exception is PowerApiException or Win32Exception)
        {
            _log.Error(SessionNarration.StartAborted(exception.Message));
            ResetToInactive();
            return Refused(StartRefusal.PowerRequestFailed, exception.Message);
        }

        _log.Info(SessionNarration.KeepAwakeActive(_request.UsesFallback, options.KeepDisplayOn));

        ApplyReport applied = ApplyReport.Empty;
        if (manageLid || manageStandby)
        {
            try
            {
                var owner = new SessionOwner(Guid.NewGuid(), _environment.CurrentProcessId, _environment.CurrentProcessStartTimeUtc);
                applied = _guard.Begin(owner, manageLid, manageStandby);
                foreach (string line in SessionNarration.Applied(applied))
                {
                    _log.Info(line);
                }

                ArmCrashRecovery(owner);
            }
            catch (Exception exception) when (exception is PowerApiException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // A step failed part-way: undo the earlier steps in reverse
                // order, then report why.
                RollBack();
                _log.Error(SessionNarration.StartAborted(exception.Message));
                ResetToInactive();
                return Refused(StartRefusal.SettingsFailed, exception.Message);
            }

            if (manageLid && !_guard.LidManaged)
            {
                degraded = DegradedReason.WriteDenied;
            }
        }

        if (degraded != DegradedReason.None)
        {
            _log.Warning(SessionNarration.Degraded(degraded));
        }

        _deadline = Deadline.Starting(_clock, options.EffectiveCeiling);
        _startTick = _clock.TickCountMilliseconds;
        _lastDriftCheckTick = _startTick;
        _trackedExitTick = null;
        _sleepDetector.Reset();

        Degraded = degraded;
        State = degraded == DegradedReason.None ? SessionState.Active : SessionState.Degraded;
        RaiseStateChanged();
        return new StartResult(State, StartRefusal.None, degraded, applied);
    }

    public void Beat()
    {
        if (!IsRunning)
        {
            return;
        }

        if (_sleepDetector.Beat() is SleepPeriod period)
        {
            _sleeps.Add(period);
            _log.Warning(SessionNarration.SleepDetected(period));
            ReassertAfterResume();
            SleepDetected?.Invoke(this, period);
        }

        if (_deadline!.HasElapsed(_clock))
        {
            Stop(_options!.DurationKind == SessionDurationKind.UntilTask ? StopReason.TaskModeCap : StopReason.TimerElapsed);
            return;
        }

        if (_options!.DurationKind == SessionDurationKind.UntilTask && CheckTaskFinished())
        {
            return;
        }

        if (CheckBatteryThreshold())
        {
            return;
        }

        if (_clock.TickCountMilliseconds - _lastDriftCheckTick >= DriftCheckInterval.TotalMilliseconds)
        {
            CheckDrift();
        }
    }

    public void OnResume()
    {
        if (!IsRunning)
        {
            return;
        }

        ReassertAfterResume();
        _log.Info(SessionNarration.Resumed());
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    public void OnLidChanged(bool closed)
    {
        _lidClosed = closed;
        if (!IsRunning)
        {
            return;
        }

        SessionOptions options = _options!;
        if (options.ModernStandbyCompatibility)
        {
            SetDisplayRequired(DisplayRequiredFor(options, closed));
        }

        if (!closed)
        {
            _log.Info(SessionNarration.LidOpened());
            return;
        }

        bool locked = options.LocksOnLidClose && _power.TryLockWorkstation();
        if (options.LocksOnLidClose && !locked)
        {
            _log.Warning(SessionNarration.LockFailed());
        }

        _log.Info(SessionNarration.LidClosed(locked, options.ModernStandbyCompatibility));
    }

    public void OnConsoleDisplayStateChanged(int state) => _consoleDisplayState = state;

    public void OnActivePowerSchemeChanged(Guid scheme)
    {
        if (!IsRunning || Degraded == DegradedReason.ThirdPartyConflict)
        {
            return;
        }

        ApplyReport report = _guard.ApplyToScheme(scheme);
        if (report.Changes.Count == 0 && report.Failures.Count == 0)
        {
            return;
        }

        _log.Info(SessionNarration.PowerSchemeChanged());
        foreach (string line in SessionNarration.Applied(report))
        {
            _log.Info(line);
        }
    }

    public void OnPowerSourceChanged(PowerStatus status)
    {
        if (!IsRunning || !status.OnBattery)
        {
            return;
        }

        _log.Info(SessionNarration.OnBattery());

        // The battery sleep timeout only matters from now on, so it is checked
        // straight away rather than at the next periodic check.
        if (_guard.StandbyManaged)
        {
            CheckDrift();
        }
    }

    public StopResult? Stop(StopReason reason)
    {
        if (State is SessionState.Inactive or SessionState.Stopping)
        {
            return null;
        }

        State = SessionState.Stopping;
        RaiseStateChanged();

        _request?.Dispose();
        _request = null;

        RestoreReport restore = RestoreSettings();
        DisarmCrashRecovery(keepStartupEntry: !restore.Complete);

        bool suspended = TrySuspendIfRequested(reason);
        TimeSpan actual = TimeSpan.FromMilliseconds(_clock.TickCountMilliseconds - _startTick);
        var result = new StopResult(reason, restore, suspended, actual, _sleeps.Count);
        _log.Info(SessionNarration.Stopped(reason, actual, _sleeps.Count));

        ResetToInactive();
        Stopped?.Invoke(this, result);
        RaiseStateChanged();
        return result;
    }

    private static StartRefusal CheckPreconditions(SessionOptions options, SessionPreconditions preconditions)
    {
        if (preconditions.Journal != JournalStatus.Absent)
        {
            return StartRefusal.UnrestoredJournal;
        }

        if (preconditions.Policies.KeepAwakeBlockedFor(preconditions.PowerStatus))
        {
            return StartRefusal.KeepAwakeBlockedByPolicy;
        }

        return options.DurationKind == SessionDurationKind.UntilTask && !preconditions.TrackedProcessAlive
            ? StartRefusal.TrackedProcessGone
            : StartRefusal.None;
    }

    private static StartResult Refused(StartRefusal refusal, string? detail = null)
        => new(SessionState.Inactive, refusal, DegradedReason.None, ApplyReport.Empty, detail);

    // In compatibility mode a display request is held while the lid is closed,
    // on top of the user's own choice to keep the screen on.
    private static bool DisplayRequiredFor(SessionOptions options, bool lidClosed)
        => options.KeepDisplayOn || (options.ModernStandbyCompatibility && lidClosed);

    private void SetDisplayRequired(bool required)
    {
        try
        {
            _request?.SetDisplayRequired(required);
        }
        catch (Win32Exception exception)
        {
            _log.Error(SessionNarration.ReassertFailed(exception.Message));
        }
    }

    private void ReassertAfterResume()
    {
        // A user-initiated sleep ends power requests, so they are recreated.
        // The settings are read back too, through the same drift check as the
        // periodic one, so a value the user changed is not fought over.
        try
        {
            _request?.Reassert();
        }
        catch (Exception exception) when (exception is PowerApiException or Win32Exception)
        {
            _log.Error(SessionNarration.ReassertFailed(exception.Message));
        }

        CheckDrift();
    }

    private bool CheckTaskFinished()
    {
        bool alive = _trackedProcessAlive?.Invoke() ?? false;
        if (alive)
        {
            _trackedExitTick = null;
            return false;
        }

        _trackedExitTick ??= _clock.TickCountMilliseconds;
        if (_clock.TickCountMilliseconds - _trackedExitTick >= _options!.TaskGracePeriod.TotalMilliseconds)
        {
            Stop(StopReason.TaskFinished);
            return true;
        }

        return false;
    }

    private bool CheckBatteryThreshold()
    {
        PowerStatus status = _power.GetPowerStatus();
        if (status.OnBattery && status.BatteryPercent is int percent && percent <= _options!.BatteryStopThresholdPercent)
        {
            _log.Warning(SessionNarration.BatteryStop(percent));
            Stop(StopReason.BatteryThreshold);
            return true;
        }

        return false;
    }

    private void CheckDrift()
    {
        _lastDriftCheckTick = _clock.TickCountMilliseconds;
        DriftReport drift = _guard.CheckForDrift();
        switch (drift.Kind)
        {
            case DriftKind.Reapplied:
                _log.Warning(SessionNarration.DriftReapplied());
                break;
            case DriftKind.StoppedManaging:
                Degraded = DegradedReason.ThirdPartyConflict;
                State = SessionState.Degraded;
                _log.Warning(SessionNarration.Degraded(DegradedReason.ThirdPartyConflict));
                RaiseStateChanged();
                break;
        }
    }

    private RestoreReport RestoreSettings()
    {
        try
        {
            RestoreReport restore = _guard.Restore();
            foreach (string line in SessionNarration.Restored(restore))
            {
                _log.Info(line);
            }

            return restore;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The values may be back while the journal could not be updated;
            // it stays on disk and the next launch finishes the job.
            _log.Error(SessionNarration.RestoreFailed(exception.Message));
            return new RestoreReport([], [], Complete: false);
        }
    }

    private bool TrySuspendIfRequested(StopReason reason)
    {
        if (!_options!.SuspendAtEndIfLidClosed || !_lidClosed || !AllowsSuspend(reason))
        {
            return false;
        }

        if (_consoleDisplayState == ConsoleDisplayOn || _power.GetTimeSinceLastInput() < SuspendIdleThreshold)
        {
            return false;
        }

        bool suspended = _power.TrySuspend();
        _log.Info(SessionNarration.Suspended(suspended));
        return suspended;
    }

    // Only the unattended end reasons suspend the machine; when the user or
    // Windows ends the session, Vigie just restores and steps aside.
    private static bool AllowsSuspend(StopReason reason)
        => reason is StopReason.TimerElapsed or StopReason.TaskFinished or StopReason.TaskModeCap or StopReason.BatteryThreshold;

    // The watchdog and the RunOnce entry exist only to put journaled values
    // back, so they are armed only once there is a journal.
    private void ArmCrashRecovery(SessionOwner owner)
    {
        try
        {
            _crashRecovery.Arm(owner);
            _crashRecoveryArmed = true;
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            // A missing safety net is reported, not fatal: the session still
            // restores on its normal paths and at the next launch.
            _log.Warning(SessionNarration.RecoveryUnavailable(exception.Message));
        }
    }

    private void DisarmCrashRecovery(bool keepStartupEntry)
    {
        if (!_crashRecoveryArmed)
        {
            return;
        }

        try
        {
            _crashRecovery.Disarm(keepStartupEntry);
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            _log.Warning(SessionNarration.RecoveryCleanupFailed(exception.Message));
        }

        _crashRecoveryArmed = false;
    }

    private void RollBack()
    {
        _request?.Dispose();
        _request = null;
        RestoreSettings();
        DisarmCrashRecovery(keepStartupEntry: false);
    }

    private void ResetToInactive()
    {
        State = SessionState.Inactive;
        Degraded = DegradedReason.None;
        _deadline = null;
        _options = null;
        _trackedProcessAlive = null;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
