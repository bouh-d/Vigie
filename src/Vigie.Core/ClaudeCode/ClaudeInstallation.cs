namespace Vigie.Core.ClaudeCode;

public enum ClaudeInstallationSource
{
    UserSetting,
    NativeInstaller,
    Path,
}

/// <param name="ExecutablePath">Absolute path of the file to start:
/// <c>claude.exe</c>, or <c>node.exe</c> for an older npm installation.</param>
/// <param name="ScriptPath">For an older npm installation, the Claude Code
/// script that <c>node.exe</c> runs. Null for a native executable.</param>
public sealed record ClaudeInstallation(
    ClaudeInstallationSource Source,
    string ExecutablePath,
    string? ScriptPath = null);

public enum CandidateRejection
{
    NotFound,
    NotAnExecutable,
    GraphicalApplication,
    InsideProjectFolder,
    InCurrentDirectory,
}

public sealed record RejectedCandidate(string Path, CandidateRejection Reason);

/// <param name="UserPathRejection">Set when the user chose a path and it was
/// refused; no other location is tried in that case.</param>
public sealed record ClaudeLocateResult(
    ClaudeInstallation? Installation,
    IReadOnlyList<RejectedCandidate> Rejected,
    CandidateRejection? UserPathRejection = null);
