namespace Vigie.Core.Power;

/// <summary>
/// Puts back the original values recorded in a journal. A value is only
/// written back when it still holds what Vigie set: anything changed in the
/// meantime is the user's or another program's choice and is left alone.
/// </summary>
public sealed class JournalRestorer(IPowerApi power, RestoreJournalStore store)
{
    private const uint ErrorFileNotFound = 2;
    private const uint ErrorInvalidParameter = 87;

    public RestoreReport Restore(RestoreJournal journal)
    {
        var outcomes = new List<RestoreOutcome>();
        var rewrittenSchemes = new HashSet<Guid>();

        foreach (RestoreEntry entry in journal.Entries)
        {
            if (entry.State == RestoreEntryState.Restored)
            {
                continue;
            }

            PowerValue current;
            try
            {
                current = power.ReadValue(entry.Scheme, entry.Subgroup, entry.Setting);
            }
            catch (PowerApiException exception) when (exception.Status is ErrorFileNotFound or ErrorInvalidParameter)
            {
                outcomes.Add(new RestoreOutcome(entry.Scheme, entry.Setting, PowerIndex.Ac | PowerIndex.Dc, RestoreOutcomeKind.SchemeMissing));
                entry.State = RestoreEntryState.Restored;
                continue;
            }
            catch (PowerApiException exception)
            {
                outcomes.Add(new RestoreOutcome(entry.Scheme, entry.Setting, PowerIndex.Ac | PowerIndex.Dc, RestoreOutcomeKind.Failed, Status: exception.Status));
                continue;
            }

            bool failed = false;
            if (entry.AppliedAc is uint appliedAc)
            {
                failed |= RestoreIndex(entry, PowerIndex.Ac, appliedAc, entry.OriginalAc, current.Ac, outcomes, rewrittenSchemes);
            }

            if (entry.AppliedDc is uint appliedDc)
            {
                failed |= RestoreIndex(entry, PowerIndex.Dc, appliedDc, entry.OriginalDc, current.Dc, outcomes, rewrittenSchemes);
            }

            if (!failed)
            {
                entry.State = RestoreEntryState.Restored;
            }
        }

        List<Guid> activationFailures = ActivateRewrittenSchemes(rewrittenSchemes);

        if (journal.IsFullyRestored)
        {
            store.Delete();
        }
        else
        {
            store.Save(journal);
        }

        return new RestoreReport(outcomes, activationFailures, journal.IsFullyRestored);
    }

    /// <returns>True when writing the original value failed.</returns>
    private bool RestoreIndex(
        RestoreEntry entry,
        PowerIndex index,
        uint applied,
        uint original,
        uint current,
        List<RestoreOutcome> outcomes,
        HashSet<Guid> rewrittenSchemes)
    {
        if (current != applied)
        {
            // A pending entry still at its original value was journaled but
            // never written: the process stopped between the two steps.
            if (entry.State == RestoreEntryState.Pending && current == original)
            {
                return false;
            }

            outcomes.Add(new RestoreOutcome(entry.Scheme, entry.Setting, index, RestoreOutcomeKind.KeptOtherValue, current, original));
            return false;
        }

        if (current == original)
        {
            return false;
        }

        try
        {
            if (index == PowerIndex.Ac)
            {
                power.WriteAcValue(entry.Scheme, entry.Subgroup, entry.Setting, original);
            }
            else
            {
                power.WriteDcValue(entry.Scheme, entry.Subgroup, entry.Setting, original);
            }
        }
        catch (PowerApiException exception)
        {
            outcomes.Add(new RestoreOutcome(entry.Scheme, entry.Setting, index, RestoreOutcomeKind.Failed, current, original, exception.Status));
            return true;
        }

        rewrittenSchemes.Add(entry.Scheme);
        outcomes.Add(new RestoreOutcome(entry.Scheme, entry.Setting, index, RestoreOutcomeKind.Restored, current, original));
        return false;
    }

    // A write only takes effect on the active scheme once it is activated
    // again; values written to an inactive scheme apply when it is selected.
    private List<Guid> ActivateRewrittenSchemes(HashSet<Guid> rewrittenSchemes)
    {
        var failures = new List<Guid>();
        if (rewrittenSchemes.Count == 0)
        {
            return failures;
        }

        Guid active;
        try
        {
            active = power.GetActiveSchemeGuid();
        }
        catch (PowerApiException)
        {
            failures.AddRange(rewrittenSchemes);
            return failures;
        }

        if (!rewrittenSchemes.Contains(active))
        {
            return failures;
        }

        try
        {
            power.ActivateScheme(active);
        }
        catch (PowerApiException)
        {
            failures.Add(active);
        }

        return failures;
    }
}
