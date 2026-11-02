using Vigie.Core.Power;
using Vigie.Core.Tests.Fakes;
using Xunit;

namespace Vigie.Core.Tests;

public class RestoreJournalStoreTests
{
    private const string Path = @"C:\Vigie\restore.json";

    private readonly FakeFileSystem _fileSystem = new();

    [Fact]
    public void SavedJournalReadsBackWithTheSameEntries()
    {
        var store = new RestoreJournalStore(_fileSystem, Path);
        RestoreJournal journal = Journal(Entry(ac: 1, dc: 1, appliedAc: 0, appliedDc: 0));

        store.Save(journal);
        JournalReadResult read = store.Read();

        Assert.Equal(JournalStatus.Loaded, read.Status);
        RestoreEntry entry = Assert.Single(read.Journal!.Entries);
        Assert.Equal(1u, entry.OriginalAc);
        Assert.Equal(0u, entry.AppliedDc);
        Assert.Equal(RestoreEntryState.Applied, entry.State);
    }

    [Fact]
    public void ACrashDuringWriteLeavesThePreviousJournalIntact()
    {
        var store = new RestoreJournalStore(_fileSystem, Path);
        store.Save(Journal(Entry(ac: 1, dc: 1, appliedAc: 0, appliedDc: 0)));

        _fileSystem.CrashDuringAtomicWrite = () => { };
        store.Save(Journal(Entry(ac: 9, dc: 9, appliedAc: 0, appliedDc: 0)));

        JournalReadResult read = store.Read();
        Assert.Equal(JournalStatus.Loaded, read.Status);
        Assert.Equal(1u, read.Journal!.Entries[0].OriginalAc);
    }

    [Fact]
    public void AbsentJournalIsReportedAsAbsent()
        => Assert.Equal(JournalStatus.Absent, new RestoreJournalStore(_fileSystem, Path).Read().Status);

    [Fact]
    public void UnparsableContentIsReturnedVerbatimAndNotGuessed()
    {
        _fileSystem.Write(Path, "{ this is not json");
        JournalReadResult read = new RestoreJournalStore(_fileSystem, Path).Read();

        Assert.Equal(JournalStatus.Unreadable, read.Status);
        Assert.Equal("{ this is not json", read.RawContent);
        Assert.Null(read.Journal);
    }

    [Fact]
    public void AFutureFormatVersionIsTreatedAsUnreadable()
    {
        _fileSystem.Write(Path, """{ "formatVersion": 999, "sessionId": "00000000-0000-0000-0000-000000000000", "entries": [] }""");
        Assert.Equal(JournalStatus.Unreadable, new RestoreJournalStore(_fileSystem, Path).Read().Status);
    }

    private static RestoreJournal Journal(params RestoreEntry[] entries)
        => new()
        {
            SessionId = Guid.NewGuid(),
            OwnerProcessId = 100,
            OwnerStartTimeUtc = DateTimeOffset.UnixEpoch,
            Entries = [.. entries],
        };

    private static RestoreEntry Entry(uint ac, uint dc, uint appliedAc, uint appliedDc)
        => new()
        {
            Scheme = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Subgroup = PowerKnownSettings.Buttons,
            Setting = PowerKnownSettings.LidAction,
            OriginalAc = ac,
            OriginalDc = dc,
            AppliedAc = appliedAc,
            AppliedDc = appliedDc,
            State = RestoreEntryState.Applied,
        };
}
