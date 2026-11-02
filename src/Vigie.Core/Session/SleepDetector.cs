using Vigie.Core.Platform;

namespace Vigie.Core.Session;

public sealed record SleepPeriod(DateTimeOffset Start, DateTimeOffset End, TimeSpan Duration);

/// <summary>
/// Detects a sleep the suspend and resume messages may have missed, which
/// happens on Modern Standby. Between beats the tick counter advances by real
/// time while the unbiased counter skips the sleep; a gap larger than the
/// threshold is time the machine was frozen.
/// </summary>
public sealed class SleepDetector(IClock clock, TimeSpan? threshold = null)
{
    private readonly TimeSpan _threshold = threshold ?? TimeSpan.FromSeconds(60);
    private long _lastTick;
    private long _lastUnbiased;

    public void Reset()
    {
        _lastTick = clock.TickCountMilliseconds;
        _lastUnbiased = clock.UnbiasedMilliseconds;
    }

    public SleepPeriod? Beat()
    {
        long tick = clock.TickCountMilliseconds;
        long unbiased = clock.UnbiasedMilliseconds;
        long tickDelta = tick - _lastTick;
        long unbiasedDelta = unbiased - _lastUnbiased;
        long sleptMilliseconds = tickDelta - unbiasedDelta;

        _lastTick = tick;
        _lastUnbiased = unbiased;

        if (sleptMilliseconds <= _threshold.TotalMilliseconds)
        {
            return null;
        }

        var slept = TimeSpan.FromMilliseconds(sleptMilliseconds);
        DateTimeOffset end = clock.Now;
        return new SleepPeriod(end - slept, end, slept);
    }
}
