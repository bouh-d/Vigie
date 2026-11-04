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
