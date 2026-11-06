using System.Globalization;
using Vigie.Core.ClaudeCode;
using Vigie.Core.Power;
using Vigie.Core.Session;
using Xunit;

namespace Vigie.Core.Tests;

public class NarrationTests
{
    private static readonly Guid Scheme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void LidChangeIsJournaledWithBothValuesAndTheSources()
    {
        var report = new ApplyReport(
            [
                new SettingChange(Scheme, PowerKnownSettings.LidAction, PowerIndex.Ac, (uint)LidAction.Sleep, (uint)LidAction.DoNothing),
                new SettingChange(Scheme, PowerKnownSettings.LidAction, PowerIndex.Dc, (uint)LidAction.Sleep, (uint)LidAction.DoNothing),
            ],
            []);

        string line = Assert.Single(SessionNarration.Applied(report));

        Assert.Equal("Lid action: Sleep → Do nothing (AC, battery).", line);
    }

    [Fact]
    public void TheStartupMessageNamesTheValueThatWasPutBack()
    {
        var report = new RestoreReport(
            [new RestoreOutcome(Scheme, PowerKnownSettings.LidAction, PowerIndex.Ac | PowerIndex.Dc, RestoreOutcomeKind.Restored, (uint)LidAction.DoNothing, (uint)LidAction.Hibernate)],
            [],
            Complete: true);

        Assert.Equal("lid action set to Hibernate", SessionNarration.RestoredSummary(report));
    }

    [Fact]
    public void ASuccessfulResultReportsTurnsDenialsAndCost()
    {
        var result = new ResultEvent("success", IsError: false, NumTurns: 8, TotalCostUsd: 0.1234m, PermissionDenials: 3, DurationMs: 1000, ResultText: "ok", Errors: [], ApiError: null);

        Assert.Equal("Claude Code finished: 8 turns, 3 actions denied, cost $0.12.", ClaudeNarration.Result(result));
    }

    [Fact]
    public void ARetryNamesItsErrorCategory()
        => Assert.Contains("(rate_limit)", ClaudeNarration.Retry(new ApiRetryEvent(3, 10, "rate_limit", 429)), StringComparison.Ordinal);

    [Fact]
    public void TheUsageLimitLineNamesTheLimitAndItsReset()
    {
        var now = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset resetsAt = now.AddHours(2);

        Assert.Equal(
            $"Claude Code stopped: 5-hour limit reached, it resets at {ClaudeNarration.ClockTime(resetsAt, now)}.",
            ClaudeNarration.UsageLimitReached(new UsageLimitEvent("rejected", resetsAt, "five_hour", UsingOverage: false), now));
        Assert.Equal("Claude Code stopped: usage limit reached.", ClaudeNarration.UsageLimitReached(limit: null, now));
    }

    [Fact]
    public void AClockTimeShowsTheDateOnlyOnAnotherDay()
    {
        // Noon UTC falls on the same date in every time zone from UTC-11 to UTC+11.
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = now.AddDays(3);

        Assert.Equal(now.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), ClaudeNarration.ClockTime(now, now));
        Assert.Equal(later.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture), ClaudeNarration.ClockTime(later, now));
    }
}
