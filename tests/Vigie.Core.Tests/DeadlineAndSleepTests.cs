using Vigie.Core.Session;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class DeadlineAndSleepTests
{
    [Fact]
    public void RemainingFallsAsTheTickCounterAdvances()
    {
        var clock = new FakeClock();
        Deadline deadline = Deadline.Starting(clock, TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(40), deadline.Remaining(clock));
    }

    [Fact]
    public void AForcedSleepCountsAgainstTheRemainingTime()
    {
        var clock = new FakeClock();
        Deadline deadline = Deadline.Starting(clock, TimeSpan.FromMinutes(30));

        clock.Sleep(TimeSpan.FromMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(20), deadline.Remaining(clock));
    }

    [Fact]
    public void MovingTheWallClockDoesNotChangeTheDeadline()
    {
        var clock = new FakeClock();
        Deadline deadline = Deadline.Starting(clock, TimeSpan.FromHours(2));

        clock.ShiftWallClock(TimeSpan.FromHours(-5));

        Assert.Equal(TimeSpan.FromHours(2), deadline.Remaining(clock));
        Assert.False(deadline.HasElapsed(clock));
    }

    [Fact]
    public void ADeadlinePassedWhileAsleepHasElapsedOnWake()
    {
        var clock = new FakeClock();
        Deadline deadline = Deadline.Starting(clock, TimeSpan.FromMinutes(15));

        clock.Sleep(TimeSpan.FromMinutes(20));

        Assert.True(deadline.HasElapsed(clock));
        Assert.Equal(TimeSpan.Zero, deadline.Remaining(clock));
    }

    [Fact]
    public void SleepDetectorReportsTheGapBetweenTheTwoCounters()
    {
        var clock = new FakeClock();
        var detector = new SleepDetector(clock);
        detector.Reset();

        clock.Sleep(TimeSpan.FromMinutes(29));
        SleepPeriod? period = detector.Beat();

        Assert.NotNull(period);
        Assert.Equal(TimeSpan.FromMinutes(29), period!.Duration);
    }

    [Fact]
    public void SleepDetectorIgnoresOrdinaryBeats()
    {
        var clock = new FakeClock();
        var detector = new SleepDetector(clock);
        detector.Reset();

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Null(detector.Beat());
    }
}
