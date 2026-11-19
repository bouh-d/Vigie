using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum RemoteControlExitAction
{
    /// <summary>Start the server again at the given time.</summary>
    Restart,

    /// <summary>The first start ended within seconds: a folder Claude Code does
    /// not trust yet, or no signed-in account. The user is told, and the server
    /// is still started again at the given time.</summary>
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

    /// <summary>The folder Remote Control works in: the one chosen under Claude
    /// Code, or the user's own folder when none is chosen or it is gone.</summary>
    public static string FolderOrDefault(string? chosen, Func<string, bool> directoryExists, string userFolder)
        => !string.IsNullOrWhiteSpace(chosen) && directoryExists(chosen) ? chosen : userFolder;

    /// <param name="restartsThisHour">How many times the server was restarted
    /// in the hour before <paramref name="exitedAt"/>.</param>
    public static RemoteControlDecision OnExit(DateTimeOffset startedAt, DateTimeOffset exitedAt, bool firstStart, int restartsThisHour)
    {
        double seconds = FirstRestartDelay.TotalSeconds * Math.Pow(2, Math.Min(restartsThisHour, 10));
        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(seconds, LongestRestartDelay.TotalSeconds));
        RemoteControlExitAction action = firstStart && exitedAt - startedAt < StartupWindow
            ? RemoteControlExitAction.FailedAtStart
            : RemoteControlExitAction.Restart;
        return new RemoteControlDecision(action, exitedAt + delay);
    }
}
