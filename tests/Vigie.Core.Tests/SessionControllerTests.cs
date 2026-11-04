using Vigie.Core.Power;
using Vigie.Core.Session;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class SessionControllerTests
{
    private static readonly Guid Scheme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly FakePowerApi _power = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakeClock _clock = new();
    private readonly FakeEnvironment _environment = new();
    private readonly FakeCrashRecovery _crashRecovery = new();
    private readonly RecordingLog _log = new();
    private readonly PowerSettingsGuard _guard;
    private readonly SessionController _controller;

    public SessionControllerTests()
    {
        var store = new RestoreJournalStore(_fileSystem, @"C:\Vigie\restore.json");
        _guard = new PowerSettingsGuard(_power, store);
        _controller = new SessionController(_power, _guard, _crashRecovery, _log, _clock, _environment);
        _power.ActiveScheme = Scheme;
        _power.Seed(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)LidAction.Sleep, (uint)LidAction.Sleep);
        _power.Seed(Scheme, PowerKnownSettings.Sleep, PowerKnownSettings.StandbyIdle, 1200, 300);
    }

    [Fact]
    public void StartingAppliesSettingsCreatesRequestAndArmsRecovery()
    {
        StartResult result = Start(Options());

        Assert.Equal(SessionState.Active, result.State);
        Assert.NotNull(_power.LastRequest);
        Assert.Equal(1, _crashRecovery.ArmCount);
        Assert.Equal((uint)LidAction.DoNothing, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
    }

    [Fact]
    public void StartIsRefusedWhenPolicyBlocksKeepingAwake()
    {
        var policies = new PowerPolicies(false, false, KeepAwakeBlockedOnAc: true, false, false);

        StartResult result = _controller.Start(Options(), Preconditions(policies: policies), trackedProcessAlive: null);

        Assert.Equal(StartRefusal.KeepAwakeBlockedByPolicy, result.Refusal);
        Assert.Equal(SessionState.Inactive, _controller.State);
        Assert.Null(_power.LastRequest);
    }

    [Fact]
    public void StartIsDegradedWhenTheLidActionIsEnforcedByPolicy()
    {
        var policies = new PowerPolicies(LidActionEnforced: true, false, false, false, false);

        StartResult result = _controller.Start(Options(), Preconditions(policies: policies), trackedProcessAlive: null);

        Assert.Equal(SessionState.Degraded, result.State);
        Assert.Equal(DegradedReason.LidActionPolicy, result.Degraded);
        // The lid is left untouched in degraded mode.
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
    }

    [Fact]
    public void StartIsDegradedWhenTheLidWriteIsDenied()
    {
        _power.DeniedWriteSettings.Add(PowerKnownSettings.LidAction);

        StartResult result = Start(Options());

        Assert.Equal(SessionState.Degraded, result.State);
        Assert.Equal(DegradedReason.WriteDenied, result.Degraded);
    }

    [Fact]
    public void TaskModeIsRefusedWhenTheTargetProcessIsGone()
    {
        SessionOptions options = Options() with { DurationKind = SessionDurationKind.UntilTask };

        StartResult result = _controller.Start(options, Preconditions(trackedAlive: false), trackedProcessAlive: () => false);

        Assert.Equal(StartRefusal.TrackedProcessGone, result.Refusal);
    }

    [Fact]
    public void AFailedPowerRequestRollsBackWithoutArmingAnything()
    {
        _power.FailCreateRequest = true;

        StartResult result = Start(Options());

        Assert.Equal(StartRefusal.PowerRequestFailed, result.Refusal);
        Assert.Equal(SessionState.Inactive, _controller.State);
        Assert.Equal(0, _crashRecovery.ArmCount);
        Assert.Equal(JournalStatus.Absent, new RestoreJournalStore(_fileSystem, @"C:\Vigie\restore.json").Read().Status);
    }

    [Fact]
    public void AStartCutShortByACrashCountsItsDurationFromTheAttempt()
    {
        _clock.Advance(TimeSpan.FromMinutes(30));
        _power.CreateRequestException = new EntryPointNotFoundException();

        Assert.Throws<EntryPointNotFoundException>(() => Start(Options()));
        StopResult? result = _controller.Stop(StopReason.UnhandledException);

        Assert.Equal(TimeSpan.Zero, result!.ActualDuration);
    }

    [Fact]
    public void AFailureWhileApplyingUndoesTheEarlierStepsAndReportsTheCause()
    {
        _power.ThrowOnActivate = true;

        StartResult result = Start(Options());

        Assert.Equal(StartRefusal.SettingsFailed, result.Refusal);
        Assert.NotNull(result.Detail);
        Assert.Equal(SessionState.Inactive, _controller.State);
        Assert.True(_power.LastRequest!.Disposed);
        Assert.Equal(0, _crashRecovery.ArmCount);
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
        Assert.Equal(JournalStatus.Absent, Store().Read().Status);
    }

    [Theory]
    [InlineData(JournalStatus.Loaded)]
    [InlineData(JournalStatus.Unreadable)]
    public void StartIsRefusedWhileAnEarlierJournalIsStillOnDisk(JournalStatus journal)
    {
        StartResult result = _controller.Start(Options(), Preconditions() with { Journal = journal }, trackedProcessAlive: null);

        Assert.Equal(StartRefusal.UnrestoredJournal, result.Refusal);
        Assert.Null(_power.LastRequest);
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
    }

    [Fact]
    public void AJournalThatAppearsAfterTheChecksIsNeverOverwritten()
    {
        const string earlier = "{ \"left\": \"by an earlier session\" }";
        _fileSystem.Write(@"C:\Vigie\restore.json", earlier);

        StartResult result = Start(Options());

        Assert.Equal(StartRefusal.SettingsFailed, result.Refusal);
        Assert.Equal(earlier, _fileSystem.Read(@"C:\Vigie\restore.json"));
        Assert.True(_power.LastRequest!.Disposed);
    }

    [Fact]
    public void NoSafetyNetIsArmedWhenNoSettingIsChanged()
    {
        StartResult result = Start(Options() with { WorkWithLidClosed = false });

        Assert.Equal(SessionState.Active, result.State);
        Assert.Equal(0, _crashRecovery.ArmCount);

        _controller.Stop(StopReason.UserStopped);
        Assert.Equal(0, _crashRecovery.DisarmCount);
    }

    [Fact]
    public void StoppingRestoresSettingsClearsTheRequestAndDisarmsRecovery()
    {
        Start(Options());

        StopResult? result = _controller.Stop(StopReason.UserStopped);

        Assert.NotNull(result);
        Assert.True(_power.LastRequest!.Disposed);
        Assert.Equal(1, _crashRecovery.DisarmCount);
        Assert.False(_crashRecovery.LastKeepStartupEntry);
        Assert.Equal((uint)LidAction.Sleep, _power.Current(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
        Assert.Equal(SessionState.Inactive, _controller.State);
    }

    [Fact]
    public void TheTimerEndsTheSession()
    {
        Start(Options() with { FixedDuration = TimeSpan.FromMinutes(30) });

        _clock.Advance(TimeSpan.FromMinutes(31));
        _controller.Beat();

        Assert.Equal(SessionState.Inactive, _controller.State);
    }

    [Fact]
    public void FallingBelowTheBatteryThresholdEndsTheSession()
    {
        Start(Options() with { BatteryStopThresholdPercent = 20 });

        _power.Status = new PowerStatus(PowerSource.Battery, 19, BatterySaverOn: false);
        _controller.Beat();

        Assert.Equal(SessionState.Inactive, _controller.State);
        Assert.Contains(_log.Entries, e => e.Message.Contains("19", StringComparison.Ordinal));
    }

    [Fact]
    public void TaskModeEndsTwoMinutesAfterTheProcessExits()
    {
        bool alive = true;
        SessionOptions options = Options() with { DurationKind = SessionDurationKind.UntilTask };
        _controller.Start(options, Preconditions(trackedAlive: true), () => alive);

        alive = false;
        _controller.Beat();
        Assert.True(_controller.IsRunning);

        _clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        _controller.Beat();

        Assert.Equal(SessionState.Inactive, _controller.State);
    }

    [Fact]
    public void TaskModeStopsAtTheCapWhenTheTaskNeverEnds()
    {
        SessionOptions options = Options() with { DurationKind = SessionDurationKind.UntilTask, TaskModeCap = TimeSpan.FromHours(8) };
        _controller.Start(options, Preconditions(trackedAlive: true), () => true);

        _clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1));
        _controller.Beat();

        Assert.Equal(SessionState.Inactive, _controller.State);
    }

    [Fact]
    public void AForcedSleepReassertsThePowerRequest()
    {
        Start(Options());
        FakePowerRequest request = _power.LastRequest!;

        _clock.Sleep(TimeSpan.FromMinutes(5));
        _controller.Beat();

        Assert.True(request.ReassertCount > 0);
        Assert.Single(_controller.Sleeps);
    }

    [Fact]
    public void ClosingTheLidLocksTheWorkstation()
    {
        Start(Options() with { LockOnLidClose = true });

        _controller.OnLidChanged(closed: true);

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void TheSessionSuspendsAtTheEndWhenTheLidIsClosedAndNobodyIsThere()
    {
        Start(Options() with { SuspendAtEndIfLidClosed = true });
        _controller.OnConsoleDisplayStateChanged(0);
        _controller.OnLidChanged(closed: true);
        _power.TimeSinceLastInput = TimeSpan.FromMinutes(5);

        _clock.Advance(Options().FixedDuration + TimeSpan.FromMinutes(1));
        _controller.Beat();

        Assert.Equal(1, _power.SuspendCount);
    }

    [Fact]
    public void RepeatedThirdPartyChangesMoveTheSessionToDegraded()
    {
        Start(Options());

        for (int i = 0; i < 2; i++)
        {
            _power.WriteAcValue(Scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)LidAction.Sleep);
            _clock.Advance(TimeSpan.FromSeconds(61));
            _controller.Beat();
        }

        Assert.Equal(SessionState.Degraded, _controller.State);
        Assert.Equal(DegradedReason.ThirdPartyConflict, _controller.Degraded);
    }

    [Fact]
    public void AFailedLockIsJournaledRatherThanThrown()
    {
        Start(Options() with { LockOnLidClose = true });
        _power.LockSucceeds = false;

        _controller.OnLidChanged(closed: true);

        Assert.True(_controller.IsRunning);
        Assert.True(_log.Contains("locking the Windows session failed"));
    }

    [Fact]
    public void CompatibilityModeHoldsTheDisplayOnlyWhileTheLidIsClosed()
    {
        Start(Options() with { ModernStandbyCompatibility = true });
        FakePowerRequest request = _power.LastRequest!;
        Assert.False(request.DisplayRequired);

        _controller.OnLidChanged(closed: true);
        Assert.True(request.DisplayRequired);

        _controller.OnLidChanged(closed: false);
        Assert.False(request.DisplayRequired);
    }

    [Fact]
    public void CompatibilityModeLocksOnLidCloseEvenWhenLockingWasUnticked()
    {
        Start(Options() with { ModernStandbyCompatibility = true, LockOnLidClose = false });

        _controller.OnLidChanged(closed: true);

        Assert.Equal(1, _power.LockCount);
    }

    [Fact]
    public void CompatibilityModeHoldsTheDisplayFromTheStartWhenTheLidIsAlreadyClosed()
    {
        _controller.OnLidChanged(closed: true);

        Start(Options() with { ModernStandbyCompatibility = true });

        Assert.True(_power.LastRequest!.DisplayRequired);
    }

    [Fact]
    public void KeepingTheScreenOnSurvivesOpeningTheLidInCompatibilityMode()
    {
        Start(Options() with { ModernStandbyCompatibility = true, KeepDisplayOn = true });

        _controller.OnLidChanged(closed: true);
        _controller.OnLidChanged(closed: false);

        Assert.True(_power.LastRequest!.DisplayRequired);
    }

    [Fact]
    public void WithoutCompatibilityModeTheLidLeavesTheDisplayRequestAlone()
    {
        Start(Options());

        _controller.OnLidChanged(closed: true);

        Assert.False(_power.LastRequest!.DisplayRequired);
    }

    [Fact]
    public void UnpluggingChecksTheBatterySleepTimeoutAtOnce()
    {
        _controller.Start(Options() with { WorkWithLidClosed = false }, Preconditions() with { ModernStandby = true }, trackedProcessAlive: null);

        // Another program restores a battery sleep timeout mid-session.
        _power.WriteDcValue(Scheme, PowerKnownSettings.Sleep, PowerKnownSettings.StandbyIdle, 300);
        _controller.OnPowerSourceChanged(new PowerStatus(PowerSource.Battery, 80, BatterySaverOn: false));

        Assert.Equal(0u, _power.Current(Scheme, PowerKnownSettings.Sleep, PowerKnownSettings.StandbyIdle).Dc);
    }

    private RestoreJournalStore Store() => new(_fileSystem, @"C:\Vigie\restore.json");

    private StartResult Start(SessionOptions options)
        => _controller.Start(options, Preconditions(), trackedProcessAlive: null);

    private static SessionOptions Options() => new()
    {
        FixedDuration = TimeSpan.FromHours(2),
        WorkWithLidClosed = true,
        SuspendAtEndIfLidClosed = false,
    };

    private SessionPreconditions Preconditions(PowerPolicies? policies = null, bool trackedAlive = true)
        => new(ModernStandby: false, _power.Status, policies ?? NoPolicies, trackedAlive);

    private static PowerPolicies NoPolicies => new(false, false, false, false, false);
}
