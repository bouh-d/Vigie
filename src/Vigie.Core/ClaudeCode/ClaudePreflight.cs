using System.ComponentModel;
using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum PreflightStatus
{
    Ready,
    NotExecutable,
    NotLoggedIn,
    CheckFailed,
}

public sealed record PreflightResult(PreflightStatus Status, string? Version = null, string? Detail = null);

/// <summary>
/// Checks, before a launch, that Claude Code runs (<c>--version</c>) and is
/// signed in (<c>auth status</c> exits with 0 when signed in, 1 when not).
/// </summary>
public sealed class ClaudePreflight(IProcessApi processes, IEnvironment environment)
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    public async Task<PreflightResult> CheckAsync(ClaudeInstallation installation, CancellationToken cancellationToken)
    {
        ProcessRunResult version;
        try
        {
            version = await RunAsync(installation, ["--version"], cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception exception)
        {
            return new PreflightResult(PreflightStatus.NotExecutable, Detail: exception.Message);
        }

        if (version.TimedOut || version.ExitCode != 0)
        {
            return new PreflightResult(PreflightStatus.NotExecutable, Detail: LastLine(version.StandardError));
        }

        string? versionText = LastLine(version.StandardOutput);
        ProcessRunResult auth = await RunAsync(installation, ["auth", "status"], cancellationToken).ConfigureAwait(false);
        return auth switch
        {
            { TimedOut: true } => new PreflightResult(PreflightStatus.CheckFailed, versionText),
            { ExitCode: 0 } => new PreflightResult(PreflightStatus.Ready, versionText),
            { ExitCode: 1 } => new PreflightResult(PreflightStatus.NotLoggedIn, versionText),
            _ => new PreflightResult(PreflightStatus.CheckFailed, versionText, LastLine(auth.StandardError)),
        };
    }

    // The checks run from the user's profile rather than the project folder,
    // so a project's own settings take no part in them.
    private Task<ProcessRunResult> RunAsync(ClaudeInstallation installation, string[] arguments, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> fullArguments = installation.ScriptPath is null
            ? arguments
            : [installation.ScriptPath, .. arguments];

        return processes.RunAsync(installation.ExecutablePath, fullArguments, environment.UserProfile, CheckTimeout, cancellationToken);
    }

    private static string? LastLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } lines
            ? lines[^1]
            : null;
}
