using System.Diagnostics;
using Vigie.Core.Platform;
using Vigie.Core.Power;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

/// <summary>
/// The watchdog against a real process: a stand-in for the Vigie instance is
/// started, watched through the Windows process API, then killed or left to
/// end its session cleanly.
/// </summary>
public sealed class WatchdogTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakePowerApi _power = new();
    private readonly FakeRegistry _registry = new();
    private readonly Win32ProcessApi _processes = new();
    private readonly RestoreJournalStore _store;
    private Process? _owner;

    public WatchdogTests()
    {
        _store = new RestoreJournalStore(_fileSystem, @"C:\Vigie\restore.json");
        _power.Seed(_power.ActiveScheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction, (uint)LidAction.DoNothing, (uint)LidAction.DoNothing);
    }

    public void Dispose()
    {
        if (_owner is { HasExited: false })
        {
            _owner.Kill();
        }

        _owner?.Dispose();
    }

    [WindowsFact]
    public async Task AKilledOwnerHasItsSettingsRestored()
    {
        (int processId, DateTimeOffset started) = StartOwner();
        using var sessionEnded = new EventWaitHandle(false, EventResetMode.ManualReset, Watchdog.SessionEndedEventName(processId, started));
        Task<LeftoverResult?> watching = Task.Run(() => Watchdog.WaitAndRestore(processId, started, _processes, Leftover()));

        _owner!.Kill();
        LeftoverResult? result = await watching.WaitAsync(Patience);

        Assert.Equal(LeftoverStatus.Restored, result?.Status);
        Assert.Equal((uint)LidAction.Sleep, LidValue().Ac);
    }

    [WindowsFact]
    public async Task ACleanEndLeavesTheRestoreToTheOwner()
    {
        (int processId, DateTimeOffset started) = StartOwner();
        using var sessionEnded = new EventWaitHandle(false, EventResetMode.ManualReset, Watchdog.SessionEndedEventName(processId, started));
        Task<LeftoverResult?> watching = Task.Run(() => Watchdog.WaitAndRestore(processId, started, _processes, Leftover()));

        sessionEnded.Set();
        LeftoverResult? result = await watching.WaitAsync(Patience);

        Assert.Null(result);
        Assert.Equal((uint)LidAction.DoNothing, LidValue().Ac);
    }

    // The instance records its own start time; the watchdog reads the owner's
    // through the process API. The two have to agree for the owner to count
    // as running.
    [WindowsFact]
    public void BothSidesReadTheSameStartTime()
    {
        DateTimeOffset recorded = new SystemEnvironment().CurrentProcessStartTimeUtc;
        DateTimeOffset? observed = _processes.GetStartTimeUtc(Environment.ProcessId);

        Assert.NotNull(observed);
        Assert.InRange((observed.Value - recorded).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private (int ProcessId, DateTimeOffset Started) StartOwner()
    {
        // Any process that keeps running stands in for the Vigie instance.
        var startInfo = new ProcessStartInfo("ping.exe") { CreateNoWindow = true, UseShellExecute = false };
        foreach (string argument in new[] { "-n", "60", "127.0.0.1" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        _owner = Process.Start(startInfo)!;
        DateTimeOffset started = _processes.GetStartTimeUtc(_owner.Id)
            ?? throw new InvalidOperationException("The stand-in owner exited at once.");
        _store.Save(Journal(_owner.Id, started));
        return (_owner.Id, started);
    }

    private LeftoverJournal Leftover()
        => new(_store, new JournalRestorer(_power, _store), _processes, new FakeEnvironment { CurrentProcessId = Environment.ProcessId }, _registry);

    private PowerValue LidValue() => _power.Current(_power.ActiveScheme, PowerKnownSettings.Buttons, PowerKnownSettings.LidAction);

    private RestoreJournal Journal(int ownerProcessId, DateTimeOffset ownerStarted) => new()
    {
        SessionId = Guid.NewGuid(),
        OwnerProcessId = ownerProcessId,
        OwnerStartTimeUtc = ownerStarted,
        Entries =
        [
            new RestoreEntry
            {
                Scheme = _power.ActiveScheme,
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
