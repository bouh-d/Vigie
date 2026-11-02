using Vigie.App.Resources;
using Vigie.Core.ClaudeCode;

namespace Vigie.App.ViewModels;

public enum DurationChoice
{
    FifteenMinutes,
    OneHour,
    TwoHours,
    FourHours,
    Custom,
    UntilTask,
}

/// <summary>What the Claude Code status dot shows.</summary>
public enum ClaudeActivity
{
    Idle,
    Running,
    Done,
    Error,
}

public sealed record PermissionItem(string Display, string? Mode)
{
    public static IReadOnlyList<PermissionItem> All { get; } =
    [
        new(Strings.Perm_Default, null),
        new(Strings.Perm_Auto, "auto"),
        new(Strings.Perm_AcceptEdits, "acceptEdits"),
        new(Strings.Perm_DontAsk, "dontAsk"),
        new(Strings.Perm_Plan, "plan"),
    ];

    public override string ToString() => Display;
}

/// <summary>A detected Claude Code process, for the list and the task-target picker.</summary>
public sealed record ClaudeProcessItem(int ProcessId, string Display, string Path)
{
    public static ClaudeProcessItem From(ClaudeProcessInfo info)
    {
        string origin = info.Origin == ClaudeProcessOrigin.Vigie ? Strings.Claude_OriginVigie : Strings.Claude_OriginExternal;
        string started = info.StartTimeUtc?.ToLocalTime().ToString("HH:mm") ?? "?";
        return new ClaudeProcessItem(
            info.ProcessId,
            Strings.Format(Strings.Claude_ProcessLine, info.ProcessId, origin, started),
            info.ExecutablePath ?? string.Empty);
    }

    public override string ToString() => Display;
}
