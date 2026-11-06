namespace Vigie.Core.ClaudeCode;

public enum ResumeRefusal
{
    None,
    TooManyResumes,
    ResetUnknown,
    ResetTooFar,
    NoSession,
    SessionEndsFirst,
    SessionUnknown,
    SessionArgument,
}

public enum ResumeCancellation
{
    Interrupted,
    NewTask,
    SessionEnded,
    OptionOff,
}

/// <summary>When to resume a task stopped by the usage limit, or why it is not
/// resumed.</summary>
/// <param name="Argument">The extra argument that picks a session, for
/// <see cref="ResumeRefusal.SessionArgument"/>.</param>
public sealed record ResumePlan(DateTimeOffset? ResumeAt, ResumeRefusal Refusal, string? Argument = null);

/// <summary>
/// Resuming an autonomous task once the account's usage limit has reset. The
/// task is resumed in its own Claude Code session, so it keeps its history.
/// Vigie keeps the PC awake while it waits, so it waits only within the
/// keep-awake session, and never more than <see cref="MaxWait"/>.
/// </summary>
public static class UsageLimitResume
{
    public const int MaxResumes = 3;

    public const string Prompt = "The usage limit has reset. Continue the task from where you stopped.";

    /// <summary>The longest wait: a 5-hour limit fits, a weekly one usually does not.</summary>
    public static TimeSpan MaxWait { get; } = TimeSpan.FromHours(6);

    /// <summary>Added to the reset time, so a clock slightly ahead of the
    /// server's does not resume too early.</summary>
    public static TimeSpan Delay { get; } = TimeSpan.FromMinutes(1);

    /// <param name="run">A run that stopped on the usage limit.</param>
    /// <param name="extraArguments">The extra arguments the task was started with.</param>
    /// <param name="resumes">How many times the task was already resumed.</param>
    /// <param name="sessionEnd">When the keep-awake session ends, or null when
    /// none is running.</param>
    public static ResumePlan Plan(
        ClaudeRunSnapshot run,
        IReadOnlyList<string> extraArguments,
        int resumes,
        DateTimeOffset now,
        DateTimeOffset? sessionEnd)
    {
        if (resumes >= MaxResumes)
        {
            return Refused(ResumeRefusal.TooManyResumes);
        }

        if (run.UsageLimit?.ResetsAt is not DateTimeOffset resetsAt)
        {
            return Refused(ResumeRefusal.ResetUnknown);
        }

        if (resetsAt - now > MaxWait)
        {
            return Refused(ResumeRefusal.ResetTooFar);
        }

        if (sessionEnd is not DateTimeOffset end)
        {
            return Refused(ResumeRefusal.NoSession);
        }

        DateTimeOffset resumeAt = (resetsAt > now ? resetsAt : now) + Delay;
        if (resumeAt > end)
        {
            return Refused(ResumeRefusal.SessionEndsFirst);
        }

        if (run.ClaudeSessionId is null)
        {
            return Refused(ResumeRefusal.SessionUnknown);
        }

        if (ClaudeCommand.FindSessionArgument(extraArguments) is string argument)
        {
            return Refused(ResumeRefusal.SessionArgument, argument);
        }

        return new ResumePlan(resumeAt, ResumeRefusal.None);
    }

    /// <summary>The task's request, turned into one that resumes its session.</summary>
    public static ClaudeLaunchRequest Request(ClaudeLaunchRequest task, string sessionId)
        => task with { Prompt = Prompt, ExtraArguments = ["--resume", sessionId, .. task.ExtraArguments] };

    private static ResumePlan Refused(ResumeRefusal refusal, string? argument = null) => new(null, refusal, argument);
}
