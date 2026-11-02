using Vigie.Core.Power;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class PowerSettingsGuardTests
{
    private static readonly Guid Scheme = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherScheme = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakePowerApi _power = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly RestoreJournalStore _store;
    private readonly PowerSettingsGuard _guard;

    public PowerSettingsGuardTests()
    {
        _store = new RestoreJournalStore(_fileSystem, @"C:\Vigie\restore.json");
        _guard = new PowerSettingsGuard(_power, _store);
        _power.ActiveScheme = Scheme;
        SeedLid(Scheme, LidAction.Sleep);
        SeedStandby(Scheme, acSeconds: 1200, dcSeconds: 300);
    }

    [Fact]
    public void BeginRecordsTheOriginalAndSetsTheLidToDoNothing()
    {
        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        PowerValue lid = _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction);
        Assert.Equal((uint)LidAction.DoNothing, lid.Ac);
        Assert.Equal((uint)LidAction.DoNothing, lid.Dc);

        RestoreEntry entry = _store.Read().Journal!.Entries.Single(e => e.Setting == PowerKnownSettings.LidAction);
        Assert.Equal((uint)LidAction.Sleep, entry.OriginalAc);
        Assert.Equal(RestoreEntryState.Applied, entry.State);
    }

    [Fact]
    public void RestorePutsTheOriginalLidActionBack()
    {
        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        RestoreReport report = _guard.Restore();

        PowerValue lid = _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction);
        Assert.Equal((uint)LidAction.Sleep, lid.Ac);
        Assert.Equal((uint)LidAction.Sleep, lid.Dc);
        Assert.True(report.Complete);
        Assert.Equal(JournalStatus.Absent, _store.Read().Status);
    }

    [Fact]
    public void RestoreDoesNotOverwriteAValueTheUserChangedDuringTheSession()
    {
        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        // The user sets the plugged-in lid action to Hibernate mid-session.
        _power.WriteAcValue(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)LidAction.Hibernate);

        RestoreReport report = _guard.Restore();

        PowerValue lid = _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction);
        Assert.Equal((uint)LidAction.Hibernate, lid.Ac);
        Assert.Equal((uint)LidAction.Sleep, lid.Dc);
        Assert.Contains(report.Outcomes, o => o.Index == PowerIndex.Ac && o.Kind == RestoreOutcomeKind.KeptOtherValue);
    }

    [Fact]
    public void ARecordedOriginalIsNeverAValueVigieItselfSet()
    {
        // A previous session left the lid at DoNothing with an unrestored journal.
        SetLid(Scheme, LidAction.DoNothing);
        var leftover = new RestoreJournal
        {
            SessionId = Guid.NewGuid(),
            OwnerProcessId = 999,
            OwnerStartTimeUtc = DateTimeOffset.UnixEpoch,
            Entries =
            [
                new RestoreEntry
                {
                    Scheme = Scheme,
                    Subgroup = PowerKnownSettings.Buttons,
                    Setting = PowerKnownSettings.LidAction,
                    OriginalAc = (uint)LidAction.Sleep,
                    OriginalDc = (uint)LidAction.Sleep,
                    AppliedAc = (uint)LidAction.DoNothing,
                    AppliedDc = (uint)LidAction.DoNothing,
                    State = RestoreEntryState.Applied,
                },
            ],
        };
        _store.Save(leftover);

        var environment = new FakeEnvironment { CurrentProcessId = 1 };
        var leftoverJournal = new LeftoverJournal(_store, new JournalRestorer(_power, _store), new FakeProcessApi(), environment, new FakeRegistry());
        leftoverJournal.RestoreIfOrphaned();

        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        RestoreEntry entry = _store.Read().Journal!.Entries.Single(e => e.Setting == PowerKnownSettings.LidAction);
        Assert.Equal((uint)LidAction.Sleep, entry.OriginalAc);
    }

    [Fact]
    public void ModernStandbyNeutralisesOnlyTheBatterySleepTimeout()
    {
        _guard.Begin(Owner(), manageLid: false, manageStandbyOnBattery: true);

        PowerValue standby = _power.Current(Scheme, PowerKnownSettings.Sleep, PowerKnownSettings.StandbyIdle);
        Assert.Equal(1200u, standby.Ac);
        Assert.Equal(0u, standby.Dc);

        RestoreEntry entry = _store.Read().Journal!.Entries.Single(e => e.Setting == PowerKnownSettings.StandbyIdle);
        Assert.Null(entry.AppliedAc);
        Assert.Equal(0u, entry.AppliedDc);
    }

    [Fact]
    public void ASecondSchemeSelectedMidSessionIsAlsoRecordedAndRestored()
    {
        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);
        SeedLid(OtherScheme, LidAction.Sleep);
        _power.ActiveScheme = OtherScheme;

        _guard.ApplyToScheme(OtherScheme);
        Assert.Equal((uint)LidAction.DoNothing, _power.Current(OtherScheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);

        _guard.Restore();
        Assert.Equal((uint)LidAction.Sleep, _power.Current(OtherScheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
    }

    [Fact]
    public void DriftIsReappliedOnceThenManagementStops()
    {
        _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        SetLid(Scheme, LidAction.Sleep);
        Assert.Equal(DriftKind.Reapplied, _guard.CheckForDrift().Kind);
        Assert.Equal((uint)LidAction.DoNothing, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);

        SetLid(Scheme, LidAction.Sleep);
        Assert.Equal(DriftKind.StoppedManaging, _guard.CheckForDrift().Kind);
        Assert.False(_guard.LidManaged);
    }

    [Fact]
    public void BeginRefusesToReplaceAJournalLeftOnDisk()
    {
        const string earlier = "{ \"formatVersion\": 1 }";
        _fileSystem.Write(@"C:\Vigie\restore.json", earlier);

        Assert.Throws<InvalidOperationException>(() => _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false));

        Assert.Equal(earlier, _fileSystem.Read(@"C:\Vigie\restore.json"));
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
    }

    [Fact]
    public void AnAccessDeniedLidWriteLeavesLidUnmanaged()
    {
        _power.DeniedWriteSettings.Add(PowerKnownSettings.LidAction);

        ApplyReport report = _guard.Begin(Owner(), manageLid: true, manageStandbyOnBattery: false);

        Assert.False(_guard.LidManaged);
        Assert.Contains(report.Failures, f => f.Setting == PowerKnownSettings.LidAction);
    }

    private static SessionOwner Owner() => new(Guid.NewGuid(), 4242, DateTimeOffset.UnixEpoch);

    private void SeedLid(Guid scheme, LidAction action)
        => _power.Seed(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)action, (uint)action);

    private void SetLid(Guid scheme, LidAction action)
    {
        _power.WriteAcValue(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)action);
        _power.WriteDcValue(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)action);
    }

    private void SeedStandby(Guid scheme, uint acSeconds, uint dcSeconds)
        => _power.Seed(scheme, PowerKnownSettings.Sleep, PowerKnownSettings.StandbyIdle, acSeconds, dcSeconds);
}
