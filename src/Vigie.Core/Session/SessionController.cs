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
    private static readonly TimeSpan DriftCheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SuspendIdleThreshold = TimeSpan.FromMinutes(2);
    private const int ConsoleDisplayOn = 1;

    private readonly IPowerApi _power;
    private readonly PowerSettingsGuard _guard;
    private readonly ICrashRecovery _crashRecovery;
    private readonly ILog _log;
    private readonly IClock _clock;
    private readonly IEnvironment _environment;
    private readonly List<SleepPeriod> _sleeps = [];
    private readonly SleepDetector _sleepDetector;

    private SessionOptions? _options;
    private SessionOwner? _owner;
    private IPowerRequest? _request;
    private Deadline? _deadline;
    private Func<bool>? _trackedProcessAlive;
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

        bool taskMode = options.DurationKind == SessionDurationKind.UntilTask;
        if (preconditions.Policies.KeepAwakeBlockedFor(preconditions.PowerStatus))
        {
            return new StartResult(SessionState.Inactive, StartRefusal.KeepAwakeBlockedByPolicy, DegradedReason.None, ApplyReport.Empty);
        }

        if (taskMode && !preconditions.TrackedProcessAlive)
        {
            return new StartResult(SessionState.Inactive, StartRefusal.TrackedProcessGone, DegradedReason.None, ApplyReport.Empty);
        }

        State = SessionState.Starting;
        _options = options;
        _owner = new SessionOwner(Guid.NewGuid(), _environment.CurrentProcessId, _environment.CurrentProcessStartTimeUtc);
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
            _request = _power.CreateSystemRequest("Vigie — session d'éveil active", options.KeepDisplayOn);
        }
        catch (Exception exception) when (exception is PowerApiException or System.ComponentModel.Win32Exception)
        {
            _log.Error($"Échec de la demande de maintien en éveil : {exception.Message}");
            State = SessionState.Inactive;
            _options = null;
            return new StartResult(SessionState.Inactive, StartRefusal.PowerRequestFailed, DegradedReason.None, ApplyReport.Empty);
        }

        _log.Info(SessionNarration.KeepAwakeActive(_request.UsesFallback, options.KeepDisplayOn));

        ApplyReport applied = ApplyReport.Empty;
        if (manageLid || manageStandby)
        {
            try
            {
                applied = _guard.Begin(_owner!, manageLid, manageStandby);
            }
            catch (Exception)
            {
                RollBack();
                State = SessionState.Inactive;
                _options = null;
                throw;
            }

            foreach (string line in SessionNarration.Applied(applied))
            {
                _log.Info(line);
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

        TryArmCrashRecovery();

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

        CheckDrift();
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

        bool shouldLock = closed && _options!.LockOnLidClose;
        if (shouldLock)
        {
            _power.LockWorkstation();
        }

        _log.Info(closed ? SessionNarration.LidClosed(shouldLock) : SessionNarration.LidOpened());
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
        if (IsRunning && status.OnBattery)
        {
            _log.Info(SessionNarration.OnBattery());
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

        RestoreReport restore = _guard.Restore();
        foreach (string line in SessionNarration.Restored(restore))
        {
            _log.Info(line);
        }

        _crashRecovery.Disarm(keepStartupEntry: !restore.Complete);

        bool suspended = TrySuspendIfRequested(reason);
        TimeSpan actual = TimeSpan.FromMilliseconds(_clock.TickCountMilliseconds - _startTick);
        var result = new StopResult(reason, restore, suspended, actual, _sleeps.Count);

        _log.Info(SessionNarration.Stopped(reason, actual, _sleeps.Count));

        State = SessionState.Inactive;
        Degraded = DegradedReason.None;
        _deadline = null;
        _options = null;
        _owner = null;
        _trackedProcessAlive = null;

        Stopped?.Invoke(this, result);
        RaiseStateChanged();
        return result;
    }

    private void ReassertAfterResume()
    {
        // A user-initiated sleep ends power requests, so they are recreated,
        // and any setting Windows reset on the way down is written again.
        _request?.Reassert();
        _guard.Reapply();
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
        if (_clock.TickCountMilliseconds - _lastDriftCheckTick < DriftCheckInterval.TotalMilliseconds)
        {
            return;
        }

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

    private void TryArmCrashRecovery()
    {
        try
        {
            _crashRecovery.Arm(_owner!);
        }
        catch (Exception exception) when (exception is IOException or System.Security.SecurityException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The watchdog and RunOnce entry are safety nets; without them the
            // session still restores on its own normal paths.
            _log.Warning($"Filet de sécurité indisponible : {exception.Message}");
        }
    }

    private void RollBack()
    {
        _request?.Dispose();
        _request = null;
        _guard.Restore();
        _crashRecovery.Disarm(keepStartupEntry: false);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
