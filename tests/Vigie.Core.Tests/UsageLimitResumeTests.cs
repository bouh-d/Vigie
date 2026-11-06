using Vigie.Core.ClaudeCode;
using Vigie.Core.Platform;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class UsageLimitResumeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ResetsAt = Now.AddHours(3);
    private static readonly DateTimeOffset SessionEnd = Now.AddHours(8);

    [Fact]
    public async Task ARunStoppedByTheLimitIsClassifiedAsSuch()
    {
        var process = new FakeManagedProcess(42);
        var launch = new ProcessLaunch(@"C:\claude.exe", [], @"C:\work\app");
        using ClaudeRun run = ClaudeRun.Start(
            new FakeProcessApi { NextProcess = process }, new FakeFileSystem(), new FakeClock(), launch, ClaudeLaunchMode.Autonomous, runDirectory: null);
        var exited = new TaskCompletionSource();
        run.Exited += (_, _) => exited.TrySetResult();

        foreach (string line in Fixtures.ReadLines("streamjson/usage_limit.jsonl"))
        {
            run.OnOutputLine(line);
        }

        process.Exit(1);
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        ClaudeRunSnapshot snapshot = run.Snapshot();

        Assert.Equal(ClaudeRunOutcome.UsageLimited, ClaudeRunClassifier.Classify(snapshot));
        Assert.Equal("4f1c2a", snapshot.ClaudeSessionId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_003_600), snapshot.UsageLimit?.ResetsAt);
    }

    [Fact]
    public void AnErrorWithoutTheLimitIsAFailure()
    {
        ClaudeRunSnapshot run = Stopped(ResetsAt) with { UsageLimit = new UsageLimitEvent("allowed_warning", ResetsAt, "five_hour", UsingOverage: false) };

        Assert.Equal(ClaudeRunOutcome.Failed, ClaudeRunClassifier.Classify(run with { Result = ErrorResult(apiError: null) }));
        Assert.Equal(ClaudeRunOutcome.UsageLimited, ClaudeRunClassifier.Classify(run with { Result = ErrorResult(StreamJsonParser.UsageLimitReached) }));
    }

    [Fact]
    public void TheTaskResumesAMinuteAfterTheReset()
    {
        ResumePlan plan = Plan(Stopped(ResetsAt));

        Assert.Equal(ResumeRefusal.None, plan.Refusal);
        Assert.Equal(ResetsAt + UsageLimitResume.Delay, plan.ResumeAt);
    }

    [Fact]
    public void AResetTimeAlreadyPassedResumesAMinuteFromNow()
        => Assert.Equal(Now + UsageLimitResume.Delay, Plan(Stopped(Now.AddMinutes(-5))).ResumeAt);

    [Fact]
    public void AnUnknownOrDistantResetIsNotWaitedFor()
    {
        Assert.Equal(ResumeRefusal.ResetUnknown, Plan(Stopped(resetsAt: null)).Refusal);
        Assert.Equal(ResumeRefusal.ResetTooFar, Plan(Stopped(Now + UsageLimitResume.MaxWait + TimeSpan.FromMinutes(1))).Refusal);
    }

    [Fact]
    public void TheWaitStaysWithinTheKeepAwakeSession()
    {
        Assert.Equal(ResumeRefusal.NoSession, UsageLimitResume.Plan(Stopped(ResetsAt), [], 0, Now, sessionEnd: null).Refusal);
        Assert.Equal(ResumeRefusal.SessionEndsFirst, UsageLimitResume.Plan(Stopped(ResetsAt), [], 0, Now, sessionEnd: ResetsAt).Refusal);
    }

    [Fact]
    public void ResumingNeedsTheSessionOfTheTask()
    {
        Assert.Equal(ResumeRefusal.SessionUnknown, Plan(Stopped(ResetsAt) with { ClaudeSessionId = null }).Refusal);

        ResumePlan plan = Plan(Stopped(ResetsAt), extraArguments: ["--model", "opus", "--continue"]);
        Assert.Equal(ResumeRefusal.SessionArgument, plan.Refusal);
        Assert.Equal("--continue", plan.Argument);
    }

    [Fact]
    public void ATaskIsResumedAtMostThreeTimes()
    {
        Assert.Equal(ResumeRefusal.None, Plan(Stopped(ResetsAt), resumes: UsageLimitResume.MaxResumes - 1).Refusal);
        Assert.Equal(ResumeRefusal.TooManyResumes, Plan(Stopped(ResetsAt), resumes: UsageLimitResume.MaxResumes).Refusal);
    }

    [Fact]
    public void TheResumeContinuesTheSessionWithTheTaskSettings()
    {
        var task = new ClaudeLaunchRequest(@"C:\work\app", "Fix the tests", ClaudeLaunchMode.Autonomous, "acceptEdits", MaxTurns: null, ["--model", "opus"]);
        var installation = new ClaudeInstallation(ClaudeInstallationSource.NativeInstaller, @"C:\Users\dev\.local\bin\claude.exe");

        ProcessLaunch launch = ClaudeCommand.Build(installation, UsageLimitResume.Request(task, "4f1c2a"));

        Assert.Equal(
            ["-p", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits", "--resume", "4f1c2a", "--model", "opus", "--", UsageLimitResume.Prompt],
            launch.Arguments);
        Assert.Equal(@"C:\work\app", launch.WorkingDirectory);
    }

    [Theory]
    [InlineData("--continue")]
    [InlineData("-c")]
    [InlineData("-r")]
    [InlineData("--resume=4f1c2a")]
    [InlineData("--session-id")]
    [InlineData("--fork-session")]
    [InlineData("-dc")]
    public void ArgumentsThatPickASessionAreFound(string argument)
        => Assert.Equal(argument, ClaudeCommand.FindSessionArgument(["--model", "opus", argument]));

    [Fact]
    public void OtherArgumentsPickNoSession()
        => Assert.Null(ClaudeCommand.FindSessionArgument(["--model", "opus", "--verbose", "-d", "--add-dir", @"C:\shared"]));

    private static ResumePlan Plan(ClaudeRunSnapshot run, IReadOnlyList<string>? extraArguments = null, int resumes = 0)
        => UsageLimitResume.Plan(run, extraArguments ?? [], resumes, Now, SessionEnd);

    private static ClaudeRunSnapshot Stopped(DateTimeOffset? resetsAt) => new(
        ProcessId: 42,
        Mode: ClaudeLaunchMode.Autonomous,
        StartedAt: Now.AddHours(-1),
        ExitedAt: Now,
        ExitCode: 1,
        Result: ErrorResult(StreamJsonParser.UsageLimitReached),
        LastRetry: null,
        PermissionDenials: 0,
        LastErrorLine: null,
        InterruptRequested: false,
        ClaudeSessionId: "4f1c2a",
        RecordingError: null,
        UsageLimit: new UsageLimitEvent("rejected", resetsAt, "five_hour", UsingOverage: false));

    private static ResultEvent ErrorResult(string? apiError)
        => new("success", IsError: true, NumTurns: 3, TotalCostUsd: 0.21m, PermissionDenials: 0, DurationMs: 61_000, ResultText: null, Errors: [], ApiError: apiError);
}
