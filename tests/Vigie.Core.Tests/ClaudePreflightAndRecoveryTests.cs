using Vigie.Core.ClaudeCode;
using Vigie.Core.Platform;
using Vigie.Core.Power;
using Vigie.Core.Storage;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class ClaudePreflightAndRecoveryTests
{
    private static readonly ClaudeInstallation Installation =
        new(ClaudeInstallationSource.NativeInstaller, @"C:\Users\dev\.local\bin\claude.exe");

    [Fact]
    public async Task PreflightReportsReadyWhenVersionAndAuthSucceed()
    {
        var processes = new FakeProcessApi();
        processes.RunResults.Enqueue(new ProcessRunResult(0, "2.1.316 (Claude Code)", string.Empty, TimedOut: false));
        processes.RunResults.Enqueue(new ProcessRunResult(0, string.Empty, string.Empty, TimedOut: false));

        PreflightResult result = await Preflight(processes).CheckAsync(Installation, CancellationToken.None);

        Assert.Equal(PreflightStatus.Ready, result.Status);
        Assert.Equal("2.1.316 (Claude Code)", result.Version);
    }

    [Fact]
    public async Task PreflightReportsNotLoggedInWhenAuthExitsWithOne()
    {
        var processes = new FakeProcessApi();
        processes.RunResults.Enqueue(new ProcessRunResult(0, "2.1.316", string.Empty, TimedOut: false));
        processes.RunResults.Enqueue(new ProcessRunResult(1, string.Empty, "Not logged in", TimedOut: false));

        PreflightResult result = await Preflight(processes).CheckAsync(Installation, CancellationToken.None);

        Assert.Equal(PreflightStatus.NotLoggedIn, result.Status);
    }

    [Fact]
    public async Task PreflightReportsNotExecutableWhenVersionFails()
    {
        var processes = new FakeProcessApi();
        processes.RunResults.Enqueue(new ProcessRunResult(-1, string.Empty, "boom", TimedOut: true));

        PreflightResult result = await Preflight(processes).CheckAsync(Installation, CancellationToken.None);

        Assert.Equal(PreflightStatus.NotExecutable, result.Status);
    }

    [Fact]
    public void AnOrphanedJournalIsRestoredWhereasARunningOwnersIsLeftAlone()
    {
        var fileSystem = new FakeFileSystem();
        var power = new FakePowerApi();
        var store = new RestoreJournalStore(fileSystem, @"C:\Vigie\restore.json");
        Guid scheme = power.ActiveScheme;
        power.Seed(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)LidAction.DoNothing, (uint)LidAction.DoNothing);
        store.Save(Journal(scheme, ownerPid: 999));

        var processes = new FakeProcessApi();
        var registry = new FakeRegistry();
        var leftover = new LeftoverJournal(store, new JournalRestorer(power, store), processes, new FakeEnvironment { CurrentProcessId = 1 }, registry);

        // The owner is still running: nothing is restored.
        processes.StartTimes[999] = DateTimeOffset.UnixEpoch;
        Assert.Equal(LeftoverStatus.OwnerRunning, leftover.RestoreIfOrphaned().Status);
        Assert.Equal((uint)LidAction.DoNothing, power.Current(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);

        // The owner is gone: the values come back and the sign-in entry is removed.
        processes.StartTimes.Remove(999);
        registry.CurrentUserStrings[(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "VigieRestore")] = "x";
        Assert.Equal(LeftoverStatus.Restored, leftover.RestoreIfOrphaned().Status);
        Assert.Equal((uint)LidAction.Sleep, power.Current(scheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction).Ac);
        Assert.False(registry.CurrentUserStrings.ContainsKey((@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "VigieRestore")));
    }

    [Fact]
    public void RestoringAMissingSchemeIsTreatedAsNothingToDo()
    {
        var fileSystem = new FakeFileSystem();
        var power = new FakePowerApi();
        var store = new RestoreJournalStore(fileSystem, @"C:\Vigie\restore.json");
        Guid scheme = power.ActiveScheme;
        power.MissingSchemes.Add(scheme);
        store.Save(Journal(scheme, ownerPid: 5));

        RestoreReport report = new JournalRestorer(power, store).Restore(store.Read().Journal!);

        Assert.True(report.Complete);
        Assert.Contains(report.Outcomes, o => o.Kind == RestoreOutcomeKind.SchemeMissing);
    }

    [Fact]
    public void RetentionRemovesOnlyRunsOlderThanTheLimit()
    {
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironment { LocalApplicationData = TestPaths.Of("Local") };
        var storage = new VigieStorage(environment, fileSystem);
        var now = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        fileSystem.Write(Path.Combine(storage.RunsDirectory, "20260101-120000", "output.jsonl"), "old");
        fileSystem.Write(Path.Combine(storage.RunsDirectory, "20260119-120000", "output.jsonl"), "recent");

        int removed = storage.PruneRuns(TimeSpan.FromDays(14), now);

        Assert.Equal(1, removed);
        Assert.False(fileSystem.Has(Path.Combine(storage.RunsDirectory, "20260101-120000", "output.jsonl")));
        Assert.True(fileSystem.Has(Path.Combine(storage.RunsDirectory, "20260119-120000", "output.jsonl")));
    }

    private static ClaudePreflight Preflight(FakeProcessApi processes) => new(processes, new FakeEnvironment());

    private static RestoreJournal Journal(Guid scheme, int ownerPid) => new()
    {
        SessionId = Guid.NewGuid(),
        OwnerProcessId = ownerPid,
        OwnerStartTimeUtc = DateTimeOffset.UnixEpoch,
        Entries =
        [
            new RestoreEntry
            {
                Scheme = scheme,
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
}
