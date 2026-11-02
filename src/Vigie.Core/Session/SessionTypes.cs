using Vigie.Core.Power;

namespace Vigie.Core.Session;

public enum SessionState
{
    Inactive,
    Starting,
    Active,
    Degraded,
    Stopping,
}

public enum DegradedReason
{
    None,
    LidActionPolicy,
    WriteDenied,
    ThirdPartyConflict,
}

public enum StopReason
{
    TimerElapsed,
    UserStopped,
    TaskFinished,
    BatteryThreshold,
    TaskModeCap,
    ApplicationClosing,
    WindowsSessionEnding,
    UnhandledException,
}

public enum StartRefusal
{
    None,
    KeepAwakeBlockedByPolicy,
    TrackedProcessGone,
    PowerRequestFailed,
}

/// <summary>Everything read from the system just before a session starts, so
/// the start logic has no side effects of its own and can be tested.</summary>
public sealed record SessionPreconditions(
    bool ModernStandby,
    PowerStatus PowerStatus,
    PowerPolicies Policies,
    bool TrackedProcessAlive);

public sealed record StartResult(
    SessionState State,
    StartRefusal Refusal,
    DegradedReason Degraded,
    ApplyReport Applied)
{
    public bool Started => State is SessionState.Active or SessionState.Degraded;
}

public sealed record StopResult(
    StopReason Reason,
    RestoreReport Restore,
    bool Suspended,
    TimeSpan ActualDuration,
    int SleepCount);
