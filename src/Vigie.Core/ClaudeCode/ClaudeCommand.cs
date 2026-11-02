using System.Globalization;
using System.Text.RegularExpressions;
using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum ClaudeLaunchMode
{
    Autonomous,
    Interactive,
}

/// <param name="PermissionMode">A Claude Code permission mode, or null to keep
/// whatever Claude Code's own configuration says.</param>
public sealed record ClaudeLaunchRequest(
    string ProjectDirectory,
    string Prompt,
    ClaudeLaunchMode Mode,
    string? PermissionMode,
    int? MaxTurns,
    IReadOnlyList<string> ExtraArguments);

/// <summary>
/// Builds the argument list for a Claude Code launch. Every argument, the
/// prompt included, stays a separate list element all the way to
/// <c>CreateProcess</c>: nothing is concatenated and no shell is involved.
/// </summary>
public static partial class ClaudeCommand
{
    public const string BypassPermissionsMode = "bypassPermissions";

    public static ProcessLaunch Build(ClaudeInstallation installation, ClaudeLaunchRequest request)
    {
        var arguments = new List<string>();
        if (installation.ScriptPath is not null)
        {
            arguments.Add(installation.ScriptPath);
        }

        if (request.Mode == ClaudeLaunchMode.Autonomous)
        {
            arguments.AddRange(["-p", "--output-format", "stream-json", "--verbose"]);
        }

        if (request.PermissionMode is not null)
        {
            arguments.AddRange(["--permission-mode", request.PermissionMode]);
        }

        if (request.Mode == ClaudeLaunchMode.Autonomous && request.MaxTurns is int maxTurns)
        {
            arguments.AddRange(["--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture)]);
        }

        arguments.AddRange(request.ExtraArguments);

        if (request.Prompt.Length > 0)
        {
            // Everything after "--" is an operand, so a prompt that begins with
            // a dash is never read as an option.
            arguments.Add("--");
            arguments.Add(request.Prompt);
        }

        return new ProcessLaunch(installation.ExecutablePath, arguments, request.ProjectDirectory);
    }

    /// <summary>
    /// Returns the first additional argument that would conflict with what
    /// Vigie sets itself (print mode, the output format, the end of options),
    /// or null when there is none.
    /// </summary>
    public static string? FindReservedArgument(IReadOnlyList<string> extraArguments)
    {
        foreach (string argument in extraArguments)
        {
            string name = argument.Split('=', 2)[0];
            if (name is "--" or "--print" or "--output-format" or "--input-format" || IsShortFlagGroupWithPrint(name))
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>
    /// True when the launch would turn off Claude Code's permission checks,
    /// which calls for an explicit confirmation every time.
    /// </summary>
    public static bool BypassesPermissions(ClaudeLaunchRequest request)
    {
        if (IsBypassMode(request.PermissionMode))
        {
            return true;
        }

        IReadOnlyList<string> arguments = request.ExtraArguments;
        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            if (argument.Equals("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--allow-dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            const string PermissionModeOption = "--permission-mode";
            if (argument.StartsWith(PermissionModeOption + "=", StringComparison.OrdinalIgnoreCase)
                && IsBypassMode(argument[(PermissionModeOption.Length + 1)..]))
            {
                return true;
            }

            if (argument.Equals(PermissionModeOption, StringComparison.OrdinalIgnoreCase)
                && i + 1 < arguments.Count
                && IsBypassMode(arguments[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBypassMode(string? mode)
        => string.Equals(mode, BypassPermissionsMode, StringComparison.OrdinalIgnoreCase);

    // Short boolean flags can be grouped ("-pc"), which would turn print mode
    // on without spelling out -p.
    private static bool IsShortFlagGroupWithPrint(string argument)
        => ShortFlagGroup().IsMatch(argument) && argument.Contains('p', StringComparison.Ordinal);

    [GeneratedRegex("^-[A-Za-z]+$")]
    private static partial Regex ShortFlagGroup();
}
