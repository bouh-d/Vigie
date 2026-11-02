namespace Vigie.Core.Platform;

/// <summary>
/// Two monotonic millisecond counters plus wall-clock time. The interrupt-time
/// counter excludes time the machine spent asleep; the tick counter includes
/// it. The gap between them over an interval is how long the machine slept.
/// </summary>
public interface IClock
{
    /// <summary>Milliseconds since boot, including time spent asleep (<c>Environment.TickCount64</c>).</summary>
    long TickCountMilliseconds { get; }

    /// <summary>Milliseconds since boot, excluding time spent asleep (<c>QueryUnbiasedInterruptTime</c>).</summary>
    long UnbiasedMilliseconds { get; }

    /// <summary>Wall-clock time, used only for display and the end-time label.</summary>
    DateTimeOffset Now { get; }
}
