namespace Vigie.Core.ClaudeCode;

public enum ClaudeRunOutcome
{
    Running,
    Succeeded,
    Failed,
    Interrupted,
    UnexpectedStop,
}

public static class ClaudeRunClassifier
{
    /// <summary>
    /// The state to show for a run. A <c>result</c> event settles it even
    /// while the process is still running background commands; the session
    /// only ends once the process itself has exited.
    /// </summary>
    public static ClaudeRunOutcome Classify(ClaudeRunSnapshot run)
    {
        if (run.InterruptRequested && run.ExitCode is not null)
        {
            return ClaudeRunOutcome.Interrupted;
        }

        if (run.Result is ResultEvent result)
        {
            return result.Succeeded && run.ExitCode is null or 0
                ? ClaudeRunOutcome.Succeeded
                : ClaudeRunOutcome.Failed;
        }

        if (run.ExitCode is not int exitCode)
        {
            return ClaudeRunOutcome.Running;
        }

        if (exitCode == 0)
        {
            return ClaudeRunOutcome.Succeeded;
        }

        // An interactive session reports no result, so its exit code is all
        // there is. An autonomous run that exits without one either failed to
        // start, which leaves a message on standard error, or was killed.
        return run.Mode == ClaudeLaunchMode.Interactive || run.LastErrorLine is not null
            ? ClaudeRunOutcome.Failed
            : ClaudeRunOutcome.UnexpectedStop;
    }
}
