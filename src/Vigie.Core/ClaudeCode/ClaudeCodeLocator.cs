using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

/// <summary>
/// Finds the Claude Code executable: the path chosen in the settings, then the
/// native installer's location, then the directories on <c>PATH</c>. A
/// candidate is accepted only if it is a console executable that lies outside
/// both the project folder and the current directory.
/// </summary>
public sealed class ClaudeCodeLocator(IFileSystem fileSystem, IEnvironment environment)
{
    private const string ExecutableName = "claude.exe";
    private const string NpmShimName = "claude.cmd";
    private const string NodeExecutableName = "node.exe";

    public ClaudeLocateResult Locate(string? userPath, string? projectDirectory)
    {
        var rejected = new List<RejectedCandidate>();

        if (!string.IsNullOrWhiteSpace(userPath))
        {
            CandidateRejection? reason = Validate(userPath, projectDirectory);
            return reason is null
                ? new ClaudeLocateResult(new ClaudeInstallation(ClaudeInstallationSource.UserSetting, Path.GetFullPath(userPath)), rejected)
                : new ClaudeLocateResult(null, [new RejectedCandidate(userPath, reason.Value)], reason);
        }

        string nativeInstallerPath = Path.Combine(environment.UserProfile, ".local", "bin", ExecutableName);
        if (TryAccept(nativeInstallerPath, projectDirectory, rejected))
        {
            return new ClaudeLocateResult(new ClaudeInstallation(ClaudeInstallationSource.NativeInstaller, nativeInstallerPath), rejected);
        }

        foreach (string directory in SearchPath())
        {
            string candidate = Path.Combine(directory, ExecutableName);
            if (TryAccept(candidate, projectDirectory, rejected))
            {
                return new ClaudeLocateResult(new ClaudeInstallation(ClaudeInstallationSource.Path, candidate), rejected);
            }

            ClaudeInstallation? npm = FindNpmInstallation(directory, projectDirectory, rejected);
            if (npm is not null)
            {
                return new ClaudeLocateResult(npm, rejected);
            }
        }

        return new ClaudeLocateResult(null, rejected);
    }

    /// <summary>
    /// Checks one executable path. Called again right before every launch, so a
    /// file swapped after detection is caught.
    /// </summary>
    /// <returns>Null when the path is acceptable.</returns>
    public CandidateRejection? Validate(string path, string? projectDirectory)
    {
        if (!Path.IsPathFullyQualified(path) || !fileSystem.FileExists(path))
        {
            return CandidateRejection.NotFound;
        }

        string fullPath = Path.GetFullPath(path);
        if (projectDirectory is not null && IsInside(fullPath, projectDirectory))
        {
            return CandidateRejection.InsideProjectFolder;
        }

        if (string.Equals(Path.GetDirectoryName(fullPath), TrimSeparator(Path.GetFullPath(environment.CurrentDirectory)), StringComparison.OrdinalIgnoreCase))
        {
            return CandidateRejection.InCurrentDirectory;
        }

        ushort? subsystem;
        using (Stream stream = fileSystem.OpenRead(fullPath))
        {
            subsystem = PeImage.ReadSubsystem(stream);
        }

        return subsystem switch
        {
            (ushort)PeSubsystem.WindowsConsole => null,
            (ushort)PeSubsystem.WindowsGui => CandidateRejection.GraphicalApplication,
            _ => CandidateRejection.NotAnExecutable,
        };
    }

    private bool TryAccept(string path, string? projectDirectory, List<RejectedCandidate> rejected)
    {
        CandidateRejection? reason = Validate(path, projectDirectory);
        if (reason is null)
        {
            return true;
        }

        // A missing file is the normal case for most locations; only real
        // refusals are worth reporting.
        if (reason != CandidateRejection.NotFound)
        {
            rejected.Add(new RejectedCandidate(path, reason.Value));
        }

        return false;
    }

    // An npm global install puts a claude.cmd shim next to node_modules. The
    // shim itself is never run (cmd.exe would re-parse the arguments); the
    // package's own executable, or node.exe with its script, is started instead.
    private ClaudeInstallation? FindNpmInstallation(string directory, string? projectDirectory, List<RejectedCandidate> rejected)
    {
        if (!fileSystem.FileExists(Path.Combine(directory, NpmShimName)))
        {
            return null;
        }

        string package = Path.Combine(directory, "node_modules", "@anthropic-ai", "claude-code");

        string packagedExecutable = Path.Combine(package, "bin", ExecutableName);
        if (TryAccept(packagedExecutable, projectDirectory, rejected))
        {
            return new ClaudeInstallation(ClaudeInstallationSource.Path, packagedExecutable);
        }

        string script = Path.Combine(package, "cli.js");
        if (!fileSystem.FileExists(script) || (projectDirectory is not null && IsInside(script, projectDirectory)))
        {
            return null;
        }

        string? node = FindNode(directory, projectDirectory, rejected);
        return node is null ? null : new ClaudeInstallation(ClaudeInstallationSource.Path, node, script);
    }

    private string? FindNode(string npmDirectory, string? projectDirectory, List<RejectedCandidate> rejected)
    {
        string besideShim = Path.Combine(npmDirectory, NodeExecutableName);
        if (TryAccept(besideShim, projectDirectory, rejected))
        {
            return besideShim;
        }

        foreach (string directory in SearchPath())
        {
            string candidate = Path.Combine(directory, NodeExecutableName);
            if (TryAccept(candidate, projectDirectory, rejected))
            {
                return candidate;
            }
        }

        return null;
    }

    // Relative PATH entries resolve against the current directory, which is
    // exactly where a planted executable would sit, so they are skipped.
    private IEnumerable<string> SearchPath()
    {
        string? path = environment.GetVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string directory = entry.Trim('"');
            if (Path.IsPathFullyQualified(directory) && seen.Add(TrimSeparator(directory)))
            {
                yield return directory;
            }
        }
    }

    private static bool IsInside(string path, string directory)
    {
        string root = TrimSeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimSeparator(string path)
        => path.Length > 1 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
}
