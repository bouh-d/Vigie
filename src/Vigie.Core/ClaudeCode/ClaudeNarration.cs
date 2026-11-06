using System.Globalization;

namespace Vigie.Core.ClaudeCode;

/// <summary>The journal lines about Claude Code processes and runs.</summary>
public static class ClaudeNarration
{
    public static string Launched(int processId, ClaudeLaunchMode mode)
    {
        string kind = mode == ClaudeLaunchMode.Autonomous ? "autonomous task" : "interactive session";
        return $"Claude Code started (PID {processId}, {kind}).";
    }

    public static string OutputRecordedIn(string directory) => $"Claude Code output recorded in {directory}.";

    public static string SessionIdentified(string sessionId) => $"Claude Code session {sessionId}.";

    public static string Retry(ApiRetryEvent retry)
    {
        string category = retry.ErrorCategory is { Length: > 0 } value ? $" ({value})" : string.Empty;
        return $"Claude Code: unstable connection, retry {retry.Attempt} of {retry.MaxRetries}{category}.";
    }

    public static string PermissionDenied(PermissionDeniedEvent denied)
        => $"Claude Code: action denied ({denied.ToolName ?? "unknown tool"}).";

    public static string Result(ResultEvent result)
    {
        if (result.Succeeded)
        {
            string cost = result.TotalCostUsd is decimal value
                ? $", cost ${value.ToString("0.00", CultureInfo.InvariantCulture)}"
                : string.Empty;
            return $"Claude Code finished: {result.NumTurns ?? 0} turns, {result.PermissionDenials} actions denied{cost}.";
        }

        string detail = result.Errors.Count > 0 ? $": {result.Errors[0]}" : string.Empty;
        return $"Claude Code finished with an error ({result.Subtype}){detail}.";
    }

    public static string Exited(int processId, int exitCode)
        => $"Claude Code (PID {processId}) exited with code {exitCode}.";

    public static string Interrupted(InterruptOutcome outcome) => outcome switch
    {
        InterruptOutcome.StoppedCleanly => "Claude Code interrupted at your request.",
        InterruptOutcome.Killed => "Claude Code did not stop in time: its process tree was ended.",
        _ => "Claude Code had already stopped.",
    };

    public static string ExternalExited(ExternalExit exit)
    {
        string origin = exit.Process.Origin == ClaudeProcessOrigin.DesktopApp ? "Claude app" : "external";
        string code = exit.ExitCode is int value ? $" with code {value}" : string.Empty;
        return $"Claude Code (PID {exit.Process.ProcessId}, {origin}) exited{code}.";
    }

    public static string TaskIdle(int processId)
        => $"Claude Code (PID {processId}) has done nothing for {(int)ClaudeProcessMonitor.TaskIdleLimit.TotalMinutes} min: the task counts as finished.";

    public static string UsageLimitReached(UsageLimitEvent? limit, DateTimeOffset now)
    {
        string reset = limit?.ResetsAt is DateTimeOffset at ? $", it resets at {ClockTime(at, now)}" : string.Empty;
        return $"Claude Code stopped: {LimitName(limit?.LimitType)} reached{reset}.";
    }

    public static string ResumeScheduled(DateTimeOffset resumeAt, DateTimeOffset now)
        => $"The task will resume at {ClockTime(resumeAt, now)}.";

    public static string ResumeRefused(ResumePlan plan) => plan.Refusal switch
    {
        ResumeRefusal.TooManyResumes => $"Not resumed: the task was already resumed {UsageLimitResume.MaxResumes} times.",
        ResumeRefusal.ResetUnknown => "Not resumed: Claude Code did not say when the limit resets.",
        ResumeRefusal.ResetTooFar => $"Not resumed: the limit resets in more than {(int)UsageLimitResume.MaxWait.TotalHours} hours.",
        ResumeRefusal.NoSession => "Not resumed: no keep-awake session is running.",
        ResumeRefusal.SessionEndsFirst => "Not resumed: the keep-awake session ends before the limit resets.",
        ResumeRefusal.SessionUnknown => "Not resumed: Claude Code did not report its session.",
        _ => $"Not resumed: the extra argument {plan.Argument} already picks a session.",
    };

    public static string Resuming(string sessionId) => $"Usage limit reset: resuming Claude Code session {sessionId}.";

    public static string ResumeCancelled(ResumeCancellation reason) => reason switch
    {
        ResumeCancellation.Interrupted => "Resume cancelled at your request.",
        ResumeCancellation.NewTask => "Resume cancelled: another task was started.",
        ResumeCancellation.SessionEnded => "Resume cancelled: the keep-awake session ended.",
        _ => "Resume cancelled: the option was turned off.",
    };

    /// <param name="cause">Why the launch failed, or null when Claude Code was not found.</param>
    public static string ResumeFailed(string? cause) => $"The task could not be resumed: {cause ?? "Claude Code was not found."}";

    /// <summary>A local time of day, with the date when it is not today.</summary>
    public static string ClockTime(DateTimeOffset at, DateTimeOffset now)
    {
        DateTimeOffset local = at.ToLocalTime();
        string format = local.Date == now.ToLocalTime().Date ? "HH:mm" : "d MMM HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string LimitName(string? limitType) => limitType switch
    {
        "five_hour" => "5-hour limit",
        { } type when type.StartsWith("seven_day", StringComparison.Ordinal) => "weekly limit",
        _ => "usage limit",
    };

    public static string CandidateRejected(RejectedCandidate candidate) => candidate.Reason switch
    {
        CandidateRejection.GraphicalApplication => $"{candidate.Path} skipped: a graphical application, probably the Claude desktop app.",
        CandidateRejection.InsideProjectFolder => $"{candidate.Path} skipped: it is inside the project folder.",
        CandidateRejection.InCurrentDirectory => $"{candidate.Path} skipped: it is in the current folder.",
        CandidateRejection.NotAnExecutable => $"{candidate.Path} skipped: not a Windows executable.",
        _ => $"{candidate.Path} not found.",
    };

    public static string NodeScanFailed(string cause) => $"Cannot look for npm installations of Claude Code: {cause}";

    public static string RecordingFailed(string cause) => $"Recording of the Claude Code output stopped: {cause}";

    public static string OldRunsCleanupFailed(string cause) => $"Could not delete old Claude Code output: {cause}";
}
