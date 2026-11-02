using Vigie.Core.Platform;

namespace Vigie.Core.Session;

/// <summary>
/// A session end time measured against the tick counter, which counts time
/// spent asleep and is unaffected by clock changes. A session therefore ends
/// on schedule even across a forced sleep, and moving the system clock has no
/// effect on it.
/// </summary>
public sealed class Deadline
{
    private readonly long _targetTick;

    private Deadline(long targetTick) => _targetTick = targetTick;

    public static Deadline Starting(IClock clock, TimeSpan duration)
        => new(clock.TickCountMilliseconds + (long)duration.TotalMilliseconds);

    public TimeSpan Remaining(IClock clock)
    {
        long remaining = _targetTick - clock.TickCountMilliseconds;
        return remaining > 0 ? TimeSpan.FromMilliseconds(remaining) : TimeSpan.Zero;
    }

    public bool HasElapsed(IClock clock) => clock.TickCountMilliseconds >= _targetTick;

    /// <summary>The wall-clock end time to show, derived from the remaining
    /// time so a clock change moves the label but not the deadline.</summary>
    public DateTimeOffset EndTime(IClock clock) => clock.Now + Remaining(clock);
}
