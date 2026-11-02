namespace Vigie.Core.Session;

public enum SessionDurationKind
{
    Fixed,
    UntilTask,
}

/// <summary>
/// The choices made in the window before a session starts. Durations are held
/// as a kind plus a span so the "until the task finishes" mode and the fixed
/// durations share one type.
/// </summary>
public sealed record SessionOptions
{
    public static TimeSpan MinimumDuration { get; } = TimeSpan.FromMinutes(5);

    public static TimeSpan MaximumDuration { get; } = TimeSpan.FromHours(24);

    public SessionDurationKind DurationKind { get; init; } = SessionDurationKind.Fixed;

    public TimeSpan FixedDuration { get; init; } = TimeSpan.FromHours(2);

    /// <summary>The upper bound for the "until the task finishes" mode.</summary>
    public TimeSpan TaskModeCap { get; init; } = TimeSpan.FromHours(8);

    public TimeSpan TaskGracePeriod { get; init; } = TimeSpan.FromMinutes(2);

    public bool KeepDisplayOn { get; init; }

    public bool WorkWithLidClosed { get; init; } = true;

    public bool LockOnLidClose { get; init; } = true;

    public bool SuspendAtEndIfLidClosed { get; init; } = true;

    public int BatteryStopThresholdPercent { get; init; } = 20;

    /// <summary>
    /// For laptops that go to sleep when the screen turns off despite the
    /// keep-awake request: while the lid is closed a display request is held
    /// as well. A display request also prevents the idle lock, so closing the
    /// lid always locks the session in this mode.
    /// </summary>
    public bool ModernStandbyCompatibility { get; init; }

    public bool LocksOnLidClose => LockOnLidClose || ModernStandbyCompatibility;

    /// <summary>The ceiling a fixed session, or the cap of a task session, runs for.</summary>
    public TimeSpan EffectiveCeiling => DurationKind == SessionDurationKind.UntilTask ? TaskModeCap : FixedDuration;

    public bool IsValid(out string? error)
    {
        if (DurationKind == SessionDurationKind.Fixed && (FixedDuration < MinimumDuration || FixedDuration > MaximumDuration))
        {
            error = "The fixed duration must be between 5 minutes and 24 hours.";
            return false;
        }

        if (DurationKind == SessionDurationKind.UntilTask && (TaskModeCap < MinimumDuration || TaskModeCap > MaximumDuration))
        {
            error = "The task-mode cap must be between 5 minutes and 24 hours.";
            return false;
        }

        if (BatteryStopThresholdPercent is < 10 or > 50)
        {
            error = "The battery threshold must be between 10% and 50%.";
            return false;
        }

        error = null;
        return true;
    }
}
