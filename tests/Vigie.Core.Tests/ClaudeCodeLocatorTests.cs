using Vigie.Core.ClaudeCode;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class ClaudeCodeLocatorTests
{
    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakeEnvironment _environment = new();

    public ClaudeCodeLocatorTests()
    {
        _environment.UserProfile = TestPaths.Of("Users", "dev");
        _environment.CurrentDirectory = TestPaths.Of("work");
        _environment.Variables["PATH"] = string.Join(Path.PathSeparator, TestPaths.Of("tools"), TestPaths.Of("npm"));
    }

    [Fact]
    public void AcceptsAConsoleExecutableChosenByTheUser()
    {
        string path = TestPaths.Of("apps", "claude.exe");
        PutConsole(path);

        ClaudeLocateResult result = Locate(userPath: path);

        Assert.NotNull(result.Installation);
        Assert.Equal(ClaudeInstallationSource.UserSetting, result.Installation!.Source);
    }

    [Fact]
    public void RejectsAGraphicalExecutableChosenByTheUserWithoutFallback()
    {
        string path = TestPaths.Of("apps", "claude.exe");
        _fileSystem.WriteBytes(path, PeImageBuilder.Gui());
        PutConsole(NativeInstallerPath);

        ClaudeLocateResult result = Locate(userPath: path);

        Assert.Null(result.Installation);
        Assert.Equal(CandidateRejection.GraphicalApplication, result.UserPathRejection);
    }

    [Fact]
    public void PrefersTheNativeInstallerOverThePath()
    {
        PutConsole(NativeInstallerPath);
        PutConsole(TestPaths.Of("tools", "claude.exe"));

        ClaudeLocateResult result = Locate(userPath: null);

        Assert.Equal(ClaudeInstallationSource.NativeInstaller, result.Installation!.Source);
        Assert.Equal(NativeInstallerPath, result.Installation.ExecutablePath);
    }

    [Fact]
    public void FallsBackToThePathWhenNoNativeInstallerIsPresent()
    {
        string onPath = TestPaths.Of("tools", "claude.exe");
        PutConsole(onPath);

        ClaudeLocateResult result = Locate(userPath: null);

        Assert.Equal(ClaudeInstallationSource.Path, result.Installation!.Source);
        Assert.Equal(onPath, result.Installation.ExecutablePath);
    }

    [Fact]
    public void RejectsAnExecutableInsideTheProjectFolder()
    {
        string projectDirectory = TestPaths.Of("proj");
        string path = Path.Combine(projectDirectory, "claude.exe");
        PutConsole(path);

        ClaudeLocateResult result = Locate(userPath: path, projectDirectory);

        Assert.Equal(CandidateRejection.InsideProjectFolder, result.UserPathRejection);
    }

    [Fact]
    public void RejectsAnExecutableInTheCurrentDirectory()
    {
        string path = Path.Combine(_environment.CurrentDirectory, "claude.exe");
        PutConsole(path);

        ClaudeLocateResult result = Locate(userPath: path);

        Assert.Equal(CandidateRejection.InCurrentDirectory, result.UserPathRejection);
    }

    private string NativeInstallerPath => Path.Combine(_environment.UserProfile, ".local", "bin", "claude.exe");

    private void PutConsole(string path) => _fileSystem.WriteBytes(path, PeImageBuilder.Console());

    private ClaudeLocateResult Locate(string? userPath, string? projectDirectory = null)
        => new ClaudeCodeLocator(_fileSystem, _environment).Locate(userPath, projectDirectory);
}
