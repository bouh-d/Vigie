namespace Vigie.Core.Power;

/// <summary>
/// Takes over the lid close action and, on Modern Standby, the battery sleep
/// timeout for the length of a session. Every original value is journaled
/// before the first write, and a scheme's originals are captured only once, so
/// a recorded original can never be a value Vigie itself set.
/// </summary>
public sealed class PowerSettingsGuard
{
    private static readonly SettingTarget LidTarget = new(
        PowerKnownSettings.Buttons,
        PowerKnownSettings.LidAction,
        Ac: (uint)LidAction.DoNothing,
        Dc: (uint)LidAction.DoNothing);

    // On battery, Windows ends "system required" requests five minutes after
    // the sleep timeout; "never" keeps them in force. Only the battery value
    // changes, the plugged-in timeout is left as it is.
    private static readonly SettingTarget StandbyTarget = new(
        PowerKnownSettings.Sleep,
        PowerKnownSettings.StandbyIdle,
        Ac: null,
        Dc: 0);

    private readonly IPowerApi _power;
    private readonly RestoreJournalStore _store;
    private readonly JournalRestorer _restorer;
    private readonly List<SettingTarget> _targets = [];
    private RestoreJournal? _journal;
    private bool _driftReapplied;
    private bool _stoppedManaging;

    public PowerSettingsGuard(IPowerApi power, RestoreJournalStore store)
    {
        _power = power;
        _store = store;
        _restorer = new JournalRestorer(power, store);
    }

    public bool IsActive => _journal is not null;

    public bool LidManaged { get; private set; }

    public bool StandbyManaged { get; private set; }

    public ApplyReport Begin(SessionOwner owner, bool manageLid, bool manageStandbyOnBattery)
    {
        if (_journal is not null)
        {
            throw new InvalidOperationException("A session is already managing power settings.");
        }

        // A journal on disk holds the originals of an earlier session. Writing a
        // new one over it would lose them, and the values read now could be the
        // ones that session set.
        if (_store.Read().Status != JournalStatus.Absent)
        {
            throw new InvalidOperationException("An earlier restore journal has not been dealt with.");
        }

        _targets.Clear();
        if (manageLid)
        {
            _targets.Add(LidTarget);
        }

        if (manageStandbyOnBattery)
        {
            _targets.Add(StandbyTarget);
        }

        _journal = new RestoreJournal
        {
            SessionId = owner.SessionId,
            OwnerProcessId = owner.ProcessId,
            OwnerStartTimeUtc = owner.StartTimeUtc,
        };
        _driftReapplied = false;
        _stoppedManaging = false;

        ApplyReport report = ApplyToScheme(_power.GetActiveSchemeGuid());
        LidManaged = manageLid && !report.Failures.Any(f => f.Setting == PowerKnownSettings.LidAction);
        StandbyManaged = manageStandbyOnBattery && !report.Failures.Any(f => f.Setting == PowerKnownSettings.StandbyIdle);
        return report;
    }

    /// <summary>
    /// Applies the session's values to <paramref name="scheme"/>, used when the
    /// user switches power scheme mid-session. A scheme already in the journal
    /// is left alone: reading it again would record Vigie's own values as
    /// originals.
    /// </summary>
    public ApplyReport ApplyToScheme(Guid scheme)
    {
        if (_journal is null || _stoppedManaging || _journal.Entries.Exists(e => e.Scheme == scheme))
        {
            return ApplyReport.Empty;
        }

        var failures = new List<SettingFailure>();
        var entries = new List<RestoreEntry>();
        foreach (SettingTarget target in _targets)
        {
            try
            {
                PowerValue original = _power.ReadValue(scheme, target.Subgroup, target.Setting);
                entries.Add(new RestoreEntry
                {
                    Scheme = scheme,
                    Subgroup = target.Subgroup,
                    Setting = target.Setting,
                    OriginalAc = original.Ac,
                    OriginalDc = original.Dc,
                    AppliedAc = target.Ac,
                    AppliedDc = target.Dc,
                    State = RestoreEntryState.Pending,
                });
            }
            catch (PowerApiException exception)
            {
                failures.Add(new SettingFailure(scheme, target.Setting, exception.Status));
            }
        }

        if (entries.Count == 0)
        {
            return new ApplyReport([], failures);
        }

        _journal.Entries.AddRange(entries);
        _store.Save(_journal);

        var changes = new List<SettingChange>();
        foreach (RestoreEntry entry in entries)
        {
            if (!TryApply(entry, changes, out uint status))
            {
                failures.Add(new SettingFailure(scheme, entry.Setting, status));
            }
        }

        if (changes.Count > 0)
        {
            _power.ActivateScheme(scheme);
        }

        _store.Save(_journal);
        return new ApplyReport(changes, failures);
    }

    /// <summary>
    /// Checks that the values Vigie set are still in place. The first time a
    /// third party changes them they are applied again; the second time Vigie
    /// stops managing them rather than fight another program.
    /// </summary>
    public DriftReport CheckForDrift()
    {
        if (_journal is null || _stoppedManaging)
        {
            return DriftReport.None;
        }

        var items = new List<DriftItem>();
        var readFailures = new List<SettingFailure>();
        foreach (RestoreEntry entry in _journal.Entries)
        {
            if (entry.State != RestoreEntryState.Applied)
            {
                continue;
            }

            PowerValue current;
            try
            {
                current = _power.ReadValue(entry.Scheme, entry.Subgroup, entry.Setting);
            }
            catch (PowerApiException exception)
            {
                readFailures.Add(new SettingFailure(entry.Scheme, entry.Setting, exception.Status));
                continue;
            }

            if (entry.AppliedAc is uint ac && current.Ac != ac)
            {
                items.Add(new DriftItem(entry.Scheme, entry.Setting, PowerIndex.Ac, ac, current.Ac));
            }

            if (entry.AppliedDc is uint dc && current.Dc != dc)
            {
                items.Add(new DriftItem(entry.Scheme, entry.Setting, PowerIndex.Dc, dc, current.Dc));
            }
        }

        if (items.Count == 0)
        {
            return new DriftReport(DriftKind.None, [], readFailures);
        }

        if (!_driftReapplied && TryReapply(items))
        {
            _driftReapplied = true;
            return new DriftReport(DriftKind.Reapplied, items, readFailures);
        }

        _stoppedManaging = true;
        LidManaged = false;
        StandbyManaged = false;
        return new DriftReport(DriftKind.StoppedManaging, items, readFailures);
    }

    public RestoreReport Restore()
    {
        if (_journal is null)
        {
            return RestoreReport.Nothing;
        }

        RestoreReport report = _restorer.Restore(_journal);
        _journal = null;
        _targets.Clear();
        LidManaged = false;
        StandbyManaged = false;
        return report;
    }

    private bool TryApply(RestoreEntry entry, List<SettingChange> changes, out uint failureStatus)
    {
        failureStatus = 0;
        bool acWritten = false;
        try
        {
            if (entry.AppliedAc is uint ac && ac != entry.OriginalAc)
            {
                _power.WriteAcValue(entry.Scheme, entry.Subgroup, entry.Setting, ac);
                acWritten = true;
            }

            if (entry.AppliedDc is uint dc && dc != entry.OriginalDc)
            {
                _power.WriteDcValue(entry.Scheme, entry.Subgroup, entry.Setting, dc);
            }
        }
        catch (PowerApiException exception)
        {
            failureStatus = exception.Status;
            if (!acWritten || TryRollBackAc(entry))
            {
                _journal!.Entries.Remove(entry);
            }

            return false;
        }

        if (acWritten)
        {
            changes.Add(new SettingChange(entry.Scheme, entry.Setting, PowerIndex.Ac, entry.OriginalAc, entry.AppliedAc!.Value));
        }

        if (entry.AppliedDc is uint appliedDc && appliedDc != entry.OriginalDc)
        {
            changes.Add(new SettingChange(entry.Scheme, entry.Setting, PowerIndex.Dc, entry.OriginalDc, appliedDc));
        }

        entry.State = RestoreEntryState.Applied;
        return true;
    }

    // Undoes the plugged-in half of an entry whose battery half could not be
    // written. If that fails too the entry stays journaled for the restorer.
    private bool TryRollBackAc(RestoreEntry entry)
    {
        try
        {
            _power.WriteAcValue(entry.Scheme, entry.Subgroup, entry.Setting, entry.OriginalAc);
            return true;
        }
        catch (PowerApiException)
        {
            return false;
        }
    }

    private bool TryReapply(List<DriftItem> items)
    {
        var schemes = new HashSet<Guid>();
        try
        {
            foreach (DriftItem item in items)
            {
                RestoreEntry entry = _journal!.Entries.First(e => e.Scheme == item.Scheme && e.Setting == item.Setting);
                if (item.Index == PowerIndex.Ac)
                {
                    _power.WriteAcValue(entry.Scheme, entry.Subgroup, entry.Setting, item.Expected);
                }
                else
                {
                    _power.WriteDcValue(entry.Scheme, entry.Subgroup, entry.Setting, item.Expected);
                }

                schemes.Add(entry.Scheme);
            }

            Guid active = _power.GetActiveSchemeGuid();
            if (schemes.Contains(active))
            {
                _power.ActivateScheme(active);
            }

            return true;
        }
        catch (PowerApiException)
        {
            return false;
        }
    }

    private sealed record SettingTarget(Guid Subgroup, Guid Setting, uint? Ac, uint? Dc);
}
