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
    UnrestoredJournal,
    TrackedProcessGone,
    PowerRequestFailed,
    SettingsFailed,
}

/// <summary>Everything read from the system just before a session starts, so
/// the start logic has no side effects of its own and can be tested.</summary>
/// <param name="Journal">The state of the restore journal on disk once any
/// leftover has been dealt with. Anything but absent blocks a new session: its
/// values must be restored, or looked at by hand, before new originals are
/// recorded.</param>
public sealed record SessionPreconditions(
    bool ModernStandby,
    PowerStatus PowerStatus,
    PowerPolicies Policies,
    bool TrackedProcessAlive,
    JournalStatus Journal = JournalStatus.Absent);

/// <param name="Detail">Why the start failed, when it did.</param>
public sealed record StartResult(
    SessionState State,
    StartRefusal Refusal,
    DegradedReason Degraded,
    ApplyReport Applied,
    string? Detail = null);

public sealed record StopResult(
    StopReason Reason,
    RestoreReport Restore,
    bool Suspended,
    TimeSpan ActualDuration,
    int SleepCount);
