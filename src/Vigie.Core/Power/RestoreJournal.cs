namespace Vigie.Core.Power;

public enum RestoreEntryState
{
    Pending,
    Applied,
    Restored,
}

/// <summary>
/// One power setting of one power scheme that Vigie took over. The original
/// values are read before anything is written. A null applied value means that
/// power source is left alone.
/// </summary>
public sealed class RestoreEntry
{
    public required Guid Scheme { get; init; }

    public required Guid Subgroup { get; init; }

    public required Guid Setting { get; init; }

    public required uint OriginalAc { get; init; }

    public required uint OriginalDc { get; init; }

    public uint? AppliedAc { get; init; }

    public uint? AppliedDc { get; init; }

    public RestoreEntryState State { get; set; }
}

/// <summary>
/// The on-disk record of every power setting a session changed, written before
/// the first change so that any later process can put the values back.
/// </summary>
public sealed class RestoreJournal
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required Guid SessionId { get; init; }

    public required int OwnerProcessId { get; init; }

    public required DateTimeOffset OwnerStartTimeUtc { get; init; }

    public List<RestoreEntry> Entries { get; init; } = [];

    public bool IsFullyRestored => Entries.TrueForAll(entry => entry.State == RestoreEntryState.Restored);
}
