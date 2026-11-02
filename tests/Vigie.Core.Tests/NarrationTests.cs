using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Power;
using Vigie.Core.Session;
using Xunit;

namespace Vigie.Core.Tests;

public class NarrationTests
{
    private const char NoBreakSpace = '\u00A0';
    private static readonly Guid Scheme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void FrenchTypographyPutsANoBreakSpaceBeforeDoublePunctuation()
        => Assert.Equal($"Batterie{NoBreakSpace}: faible{NoBreakSpace}! «{NoBreakSpace}oui{NoBreakSpace}»", FrenchTypography.Fix("Batterie : faible ! « oui »"));

    [Fact]
    public void FrenchTypographyLeavesClockTimesAndPathsAlone()
        => Assert.Equal(@"de 14:02 à C:\dev", FrenchTypography.Fix(@"de 14:02 à C:\dev"));

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

        Assert.Equal($"Action du capot{NoBreakSpace}: Veille → Ne rien faire (secteur, batterie).", line);
    }

    [Fact]
    public void TheStartupMessageNamesTheValueThatWasPutBack()
    {
        var report = new RestoreReport(
            [new RestoreOutcome(Scheme, PowerKnownSettings.LidAction, PowerIndex.Ac | PowerIndex.Dc, RestoreOutcomeKind.Restored, (uint)LidAction.DoNothing, (uint)LidAction.Hibernate)],
            [],
            Complete: true);

        Assert.Equal("action du capot sur Veille prolongée", SessionNarration.RestoredSummary(report));
    }

    [Fact]
    public void ASuccessfulResultReportsTurnsDenialsAndCostInFrenchNotation()
    {
        var result = new ResultEvent("success", IsError: false, NumTurns: 8, TotalCostUsd: 0.1234m, PermissionDenials: 3, DurationMs: 1000, ResultText: "ok", Errors: []);

        Assert.Equal($"Claude Code a terminé{NoBreakSpace}: 8 tours, 3 actions refusées, coût 0,12 $.", ClaudeNarration.Result(result));
    }

    [Fact]
    public void ARetryNamesItsErrorCategory()
        => Assert.Contains("(rate_limit)", ClaudeNarration.Retry(new ApiRetryEvent(3, 10, "rate_limit", 429)), StringComparison.Ordinal);
}
