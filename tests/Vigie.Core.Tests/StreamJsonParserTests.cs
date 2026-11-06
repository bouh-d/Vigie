using Vigie.Core.ClaudeCode;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class StreamJsonParserTests
{
    [Fact]
    public void SuccessStreamYieldsInitAndSuccessfulResult()
    {
        List<ClaudeStreamEvent> events = Parse("streamjson/success.jsonl");

        SessionInitEvent init = Assert.IsType<SessionInitEvent>(events[0]);
        Assert.Equal("abc123", init.SessionId);

        ResultEvent result = Assert.IsType<ResultEvent>(events[^1]);
        Assert.True(result.Succeeded);
        Assert.Equal(8, result.NumTurns);
        Assert.Equal(0.1234m, result.TotalCostUsd);
        Assert.Equal(720000, result.DurationMs);
        Assert.Equal(0, result.PermissionDenials);
    }

    [Fact]
    public void NonJsonLinesAreSkipped()
    {
        List<string> lines = [.. Fixtures.ReadLines("streamjson/success.jsonl")];
        Assert.Contains(lines, line => line.StartsWith("not-json", StringComparison.Ordinal));

        Assert.Null(StreamJsonParser.Parse("not-json garbage line that must be ignored"));
    }

    [Fact]
    public void MaxTurnsResultIsAnError()
    {
        ResultEvent result = LastResult("streamjson/error_max_turns.jsonl");

        Assert.False(result.Succeeded);
        Assert.True(result.IsError);
        Assert.Equal("error_max_turns", result.Subtype);
        Assert.Equal("Reached maximum number of turns (20)", Assert.Single(result.Errors));
    }

    [Fact]
    public void UnknownFieldsOnResultAreIgnored()
    {
        ResultEvent result = LastResult("streamjson/error_during_execution.jsonl");

        Assert.Equal("error_during_execution", result.Subtype);
        Assert.True(result.IsError);
        Assert.Equal("boom", Assert.Single(result.Errors));
    }

    [Fact]
    public void ApiRetryCarriesAttemptAndCategory()
    {
        ApiRetryEvent retry = Assert.IsType<ApiRetryEvent>(Parse("streamjson/api_retry.jsonl").Single());

        Assert.Equal(3, retry.Attempt);
        Assert.Equal(10, retry.MaxRetries);
        Assert.Equal("rate_limit", retry.ErrorCategory);
        Assert.Equal(429, retry.ErrorStatus);
    }

    [Fact]
    public void PermissionDenialsAreCountedOnResult()
    {
        List<ClaudeStreamEvent> events = Parse("streamjson/permission_denied.jsonl");

        Assert.Contains(events, e => e is PermissionDeniedEvent { ToolName: "Bash" });
        Assert.Equal(1, events.OfType<ResultEvent>().Single().PermissionDenials);
    }

    [Fact]
    public void AUsageLimitStopCarriesTheResetTimeAndTheKindOfLimit()
    {
        List<ClaudeStreamEvent> events = Parse("streamjson/usage_limit.jsonl");
        List<UsageLimitEvent> limits = [.. events.OfType<UsageLimitEvent>()];

        Assert.Equal(2, limits.Count);
        Assert.False(limits[0].Reached);
        Assert.Equal("allowed_warning", limits[0].Status);

        UsageLimitEvent refusal = limits[1];
        Assert.True(refusal.Reached);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_003_600), refusal.ResetsAt);
        Assert.Equal("five_hour", refusal.LimitType);

        ResultEvent result = events.OfType<ResultEvent>().Single();
        Assert.False(result.Succeeded);
        Assert.Equal(StreamJsonParser.UsageLimitReached, result.ApiError);
    }

    [Theory]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1800003600,"isUsingOverage":true}}""", false)]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected"}}""", true)]
    [InlineData("""{"type":"assistant","api_error":"usage_limit_reached"}""", true)]
    public void ALimitIsReachedOnlyWhenRequestsAreRefused(string line, bool reached)
    {
        UsageLimitEvent limit = Assert.IsType<UsageLimitEvent>(StreamJsonParser.Parse(line));

        Assert.Equal(reached, limit.Reached);
    }

    [Theory]
    [InlineData("""{"type":"rate_limit_event"}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":"rejected"}""")]
    [InlineData("""{"type":"assistant","api_error":"rate_limit"}""")]
    public void MalformedLimitEventsCountAsActivity(string line)
        => Assert.IsType<ActivityEvent>(StreamJsonParser.Parse(line));

    [Theory]
    [InlineData("-1")]
    [InlineData("1e300")]
    [InlineData("\"1800003600\"")]
    public void AResetTimeOutOfRangeIsLeftUnknown(string resetsAt)
    {
        var limit = (UsageLimitEvent)StreamJsonParser.Parse($$$"""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":{{{resetsAt}}}}}""")!;

        Assert.Null(limit.ResetsAt);
    }

    [Fact]
    public void BlankAndUnknownTypesNeverThrowAndNeverProduceResults()
    {
        List<ClaudeStreamEvent> events = Parse("streamjson/unknown_and_blank.jsonl");

        Assert.DoesNotContain(events, e => e is ResultEvent);
        Assert.All(events, e => Assert.IsType<ActivityEvent>(e));
    }

    private static List<ClaudeStreamEvent> Parse(string fixture)
        => [.. Fixtures.ReadLines(fixture).Select(StreamJsonParser.Parse).OfType<ClaudeStreamEvent>()];

    private static ResultEvent LastResult(string fixture) => Parse(fixture).OfType<ResultEvent>().Last();
}
