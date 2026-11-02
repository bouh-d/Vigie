using Vigie.Core.Platform;

namespace Vigie.Core.Tests.Fakes;

/// <summary>
/// A clock the tests drive by hand. Advancing moves both counters and the wall
/// clock together; a simulated sleep moves the tick counter and the wall clock
/// but not the unbiased counter, exactly as a real sleep does.
/// </summary>
public sealed class FakeClock : IClock
{
    public long TickCountMilliseconds { get; private set; } = 1_000;

    public long UnbiasedMilliseconds { get; private set; } = 1_000;

    public DateTimeOffset Now { get; private set; } = new(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan amount)
    {
        long ms = (long)amount.TotalMilliseconds;
        TickCountMilliseconds += ms;
        UnbiasedMilliseconds += ms;
        Now += amount;
    }

    public void Sleep(TimeSpan amount)
    {
        long ms = (long)amount.TotalMilliseconds;
        TickCountMilliseconds += ms;
        Now += amount;
    }

    public void ShiftWallClock(TimeSpan amount) => Now += amount;
}
