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

public sealed record DurationItem(DurationChoice Choice, string Display)
{
    public static IReadOnlyList<DurationItem> All { get; } =
    [
        new(DurationChoice.FifteenMinutes, Strings.Duration_15m),
        new(DurationChoice.OneHour, Strings.Duration_1h),
        new(DurationChoice.TwoHours, Strings.Duration_2h),
        new(DurationChoice.FourHours, Strings.Duration_4h),
        new(DurationChoice.Custom, Strings.Duration_Custom),
        new(DurationChoice.UntilTask, Strings.Duration_UntilTask),
    ];

    public override string ToString() => Display;
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
public sealed record ClaudeProcessItem(int ProcessId, string Display, string Path, bool IsIdle)
{
    public static ClaudeProcessItem From(ClaudeProcessInfo info)
    {
        string origin = info.Origin switch
        {
            ClaudeProcessOrigin.Vigie => Strings.Claude_OriginVigie,
            ClaudeProcessOrigin.DesktopApp => Strings.Claude_OriginDesktop,
            _ => Strings.Claude_OriginExternal,
        };
        string started = info.StartTimeUtc?.ToLocalTime().ToString("HH:mm") ?? "?";
        string state = info.IsIdle ? Strings.Claude_ProcessIdle : Strings.Claude_ProcessWorking;
        return new ClaudeProcessItem(
            info.ProcessId,
            Strings.Format(Strings.Claude_ProcessLine, info.ProcessId, origin, started, state),
            info.ExecutablePath ?? string.Empty,
            info.IsIdle);
    }

    public override string ToString() => Display;
}
