using Vigie.Core.Platform;

namespace Vigie.Core.Power;

public enum LeftoverStatus
{
    None,
    OwnerRunning,
    Unreadable,
    Restored,
}

public sealed record LeftoverResult(
    LeftoverStatus Status,
    RestoreJournal? Journal = null,
    RestoreReport? Report = null,
    string? RawContent = null);

/// <summary>
/// Handles a journal left by a session that did not end normally. Used on
/// start-up, by the watchdog and by the <c>--restore</c> command-line modes.
/// </summary>
public sealed class LeftoverJournal(
    RestoreJournalStore store,
    JournalRestorer restorer,
    IProcessApi processes,
    IEnvironment environment,
    IRegistry registry)
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Restores the journal unless the Vigie instance that wrote it is still
    /// running a session. The current process always counts as the owner of
    /// its own journal, so a restore it left unfinished is retried.
    /// </summary>
    public LeftoverResult RestoreIfOrphaned()
    {
        JournalReadResult read = store.Read();
        switch (read.Status)
        {
            case JournalStatus.Absent:
                return new LeftoverResult(LeftoverStatus.None);
            case JournalStatus.Unreadable:
                return new LeftoverResult(LeftoverStatus.Unreadable, RawContent: read.RawContent);
        }

        RestoreJournal journal = read.Journal!;
        if (journal.OwnerProcessId != environment.CurrentProcessId && IsOwnerRunning(journal))
        {
            return new LeftoverResult(LeftoverStatus.OwnerRunning, journal);
        }

        return RestoreAndClear(journal);
    }

    /// <summary>
    /// Restores the journal whatever its owner, for the manual <c>--restore</c>
    /// command. The current instance is expected to be the only one running.
    /// </summary>
    public LeftoverResult RestoreNow()
    {
        JournalReadResult read = store.Read();
        return read.Status switch
        {
            JournalStatus.Absent => new LeftoverResult(LeftoverStatus.None),
            JournalStatus.Unreadable => new LeftoverResult(LeftoverStatus.Unreadable, RawContent: read.RawContent),
            _ => RestoreAndClear(read.Journal!),
        };
    }

    private LeftoverResult RestoreAndClear(RestoreJournal journal)
    {
        RestoreReport report = restorer.Restore(journal);
        if (report.Complete)
        {
            CrashRecovery.RemoveStartupEntry(registry);
        }

        return new LeftoverResult(LeftoverStatus.Restored, journal, report);
    }

    // A matching start time guards against the owner's process identifier
    // having been reused by an unrelated process.
    private bool IsOwnerRunning(RestoreJournal journal)
    {
        DateTimeOffset? started = processes.GetStartTimeUtc(journal.OwnerProcessId);
        return started is DateTimeOffset start
            && (start - journal.OwnerStartTimeUtc).Duration() <= StartTimeTolerance;
    }
}
