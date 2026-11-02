namespace Vigie.Core.Power;

/// <summary>The AC (plugged in) or DC (on battery) index of a power setting.</summary>
[Flags]
public enum PowerIndex
{
    Ac = 1,
    Dc = 2,
}

public sealed record SessionOwner(Guid SessionId, int ProcessId, DateTimeOffset StartTimeUtc);

public sealed record SettingChange(Guid Scheme, Guid Setting, PowerIndex Index, uint Before, uint After);

public sealed record SettingFailure(Guid Scheme, Guid Setting, uint Status);

public sealed record ApplyReport(IReadOnlyList<SettingChange> Changes, IReadOnlyList<SettingFailure> Failures)
{
    public static ApplyReport Empty { get; } = new([], []);
}

public enum RestoreOutcomeKind
{
    /// <summary>The value Vigie set was still in place and the original was written back.</summary>
    Restored,

    /// <summary>Someone else changed the value during the session; their value was kept.</summary>
    KeptOtherValue,

    /// <summary>The power scheme no longer exists, so there is nothing to restore.</summary>
    SchemeMissing,

    /// <summary>Reading or writing the value failed; the journal is kept for a later attempt.</summary>
    Failed,
}

public sealed record RestoreOutcome(
    Guid Scheme,
    Guid Setting,
    PowerIndex Index,
    RestoreOutcomeKind Kind,
    uint? Current = null,
    uint? Original = null,
    uint? Status = null);

public sealed record RestoreReport(
    IReadOnlyList<RestoreOutcome> Outcomes,
    IReadOnlyList<Guid> ActivationFailures,
    bool Complete)
{
    public static RestoreReport Nothing { get; } = new([], [], Complete: true);
}

public enum DriftKind
{
    None,
    Reapplied,
    StoppedManaging,
}

public sealed record DriftItem(Guid Scheme, Guid Setting, PowerIndex Index, uint Expected, uint Actual);

public sealed record DriftReport(DriftKind Kind, IReadOnlyList<DriftItem> Items, IReadOnlyList<SettingFailure> ReadFailures)
{
    public static DriftReport None { get; } = new(DriftKind.None, [], []);
}
