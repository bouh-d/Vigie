using Vigie.Core.Platform;

namespace Vigie.Core.Power;

/// <summary>
/// The body of <c>Vigie.exe --watchdog</c>: a windowless process started with
/// each session that waits for either a clean session end or the death of the
/// Vigie instance, and restores the journal in the second case.
/// </summary>
public static class Watchdog
{
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    public static string SessionEndedEventName(int processId, DateTimeOffset startTimeUtc)
        => $@"Local\Vigie.Session.{processId}.{startTimeUtc.UtcTicks}";

    /// <returns>Null when the session ended cleanly; otherwise what the
    /// restore found.</returns>
    public static LeftoverResult? WaitAndRestore(
        int ownerProcessId,
        DateTimeOffset ownerStartTimeUtc,
        IProcessApi processes,
        LeftoverJournal leftover)
    {
        using IObservedProcess? owner = processes.Observe(ownerProcessId);
        DateTimeOffset? started = processes.GetStartTimeUtc(ownerProcessId);
        bool ownerAlive = owner is not null
            && started is DateTimeOffset start
            && (start - ownerStartTimeUtc).Duration() <= StartTimeTolerance;

        if (ownerAlive)
        {
            // The owner creates the event before starting the watchdog, so a
            // missing event means the session has already ended.
            if (!EventWaitHandle.TryOpenExisting(SessionEndedEventName(ownerProcessId, ownerStartTimeUtc), out EventWaitHandle? sessionEnded))
            {
                return null;
            }

            using (sessionEnded)
            {
                if (WaitHandle.WaitAny([owner!.ExitWaitHandle, sessionEnded]) == 1)
                {
                    return null;
                }
            }
        }

        return leftover.RestoreIfOrphaned();
    }
}
