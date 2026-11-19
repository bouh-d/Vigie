using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum RemoteControlExitAction
{
    /// <summary>Start the server again at the given time.</summary>
    Restart,

    /// <summary>The first start ended within seconds: a folder Claude Code does
    /// not trust yet, or no signed-in account. Restarting would only loop.</summary>
    FailedAtStart,
}

public sealed record RemoteControlDecision(RemoteControlExitAction Action, DateTimeOffset? RestartAt);

/// <summary>
/// Keeps Claude Code's Remote Control server (<c>claude remote-control</c>)
/// running for the home lab mode, so a session on claude.ai or the Claude app
/// can work on this PC at any time. The server is started in the Claude Code
/// folder and restarted when it stops, waiting longer after each restart in the
/// same hour, so an outage costs a few attempts rather than a loop.
/// </summary>
public static class RemoteControl
{
    /// <summary>An exit this soon after the first start is a start failure.</summary>
    public static TimeSpan StartupWindow { get; } = TimeSpan.FromSeconds(20);

    public static TimeSpan FirstRestartDelay { get; } = TimeSpan.FromSeconds(30);

    public static TimeSpan LongestRestartDelay { get; } = TimeSpan.FromMinutes(10);

    public static ProcessLaunch Build(ClaudeInstallation installation, string folder, string? permissionMode)
    {
        var arguments = new List<string>();
        if (installation.ScriptPath is not null)
        {
            arguments.Add(installation.ScriptPath);
        }

        arguments.Add("remote-control");
        if (permissionMode is not null)
        {
            arguments.AddRange(["--permission-mode", permissionMode]);
        }

        return new ProcessLaunch(installation.ExecutablePath, arguments, folder);
    }

    /// <summary>Turning permission checks off has to be confirmed at every
    /// launch, which an unattended restart cannot do, so the home lab never
    /// uses that mode.</summary>
    public static bool Allows(string? permissionMode)
        => !string.Equals(permissionMode, ClaudeCommand.BypassPermissionsMode, StringComparison.OrdinalIgnoreCase);

    /// <param name="restartsThisHour">How many times the server was restarted
    /// in the hour before <paramref name="exitedAt"/>.</param>
    public static RemoteControlDecision OnExit(DateTimeOffset startedAt, DateTimeOffset exitedAt, bool firstStart, int restartsThisHour)
    {
        if (firstStart && exitedAt - startedAt < StartupWindow)
        {
            return new RemoteControlDecision(RemoteControlExitAction.FailedAtStart, null);
        }

        double seconds = FirstRestartDelay.TotalSeconds * Math.Pow(2, Math.Min(restartsThisHour, 10));
        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(seconds, LongestRestartDelay.TotalSeconds));
        return new RemoteControlDecision(RemoteControlExitAction.Restart, exitedAt + delay);
    }
}
