using Vigie.Core.ClaudeCode;
using Vigie.Core.Platform;
using Xunit;

namespace Vigie.Core.Tests;

public class ClaudeCommandTests
{
    private static readonly ClaudeInstallation NativeExecutable =
        new(ClaudeInstallationSource.NativeInstaller, @"C:\Users\dev\.local\bin\claude.exe");

    [Fact]
    public void AutonomousLaunchUsesPrintModeAndStreamJson()
    {
        ProcessLaunch launch = Build(new ClaudeLaunchRequest(
            @"C:\dev\project", "Corrige les tests", ClaudeLaunchMode.Autonomous, PermissionMode: null, MaxTurns: null, ExtraArguments: []));

        Assert.Equal(@"C:\Users\dev\.local\bin\claude.exe", launch.FileName);
        Assert.Equal(@"C:\dev\project", launch.WorkingDirectory);
        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--", "Corrige les tests"], launch.Arguments);
    }

    [Fact]
    public void PermissionModeAndMaxTurnsArePassedAsSeparateArguments()
    {
        ProcessLaunch launch = Build(new ClaudeLaunchRequest(
            @"C:\dev", "go", ClaudeLaunchMode.Autonomous, "acceptEdits", MaxTurns: 12, ExtraArguments: ["--continue"]));

        Assert.Equal(
            ["-p", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits", "--max-turns", "12", "--continue", "--", "go"],
            launch.Arguments);
    }

    [Theory]
    [InlineData("\" & calc & \"")]
    [InlineData("; calc")]
    [InlineData("%PATH%")]
    [InlineData("a | b & c")]
    [InlineData("ligne un\nligne deux")]
    [InlineData("accentué €ö 日本語")]
    public void PromptIsCarriedAsOneUntouchedArgument(string prompt)
    {
        ProcessLaunch launch = Build(new ClaudeLaunchRequest(
            @"C:\dev", prompt, ClaudeLaunchMode.Autonomous, PermissionMode: null, MaxTurns: null, ExtraArguments: []));

        Assert.Equal(prompt, launch.Arguments[^1]);
        Assert.Equal("--", launch.Arguments[^2]);
    }

    [Fact]
    public void NodeInstallationRunsTheScriptBeforeAnythingElse()
    {
        var installation = new ClaudeInstallation(ClaudeInstallationSource.Path, @"C:\nvm\node.exe", @"C:\npm\claude-code\cli.js");

        ProcessLaunch launch = ClaudeCommand.Build(installation, new ClaudeLaunchRequest(
            @"C:\dev", "go", ClaudeLaunchMode.Autonomous, PermissionMode: null, MaxTurns: null, ExtraArguments: []));

        Assert.Equal(@"C:\nvm\node.exe", launch.FileName);
        Assert.Equal(@"C:\npm\claude-code\cli.js", launch.Arguments[0]);
    }

    [Theory]
    [InlineData("bypassPermissions", new string[0], true)]
    [InlineData(null, new[] { "--dangerously-skip-permissions" }, true)]
    [InlineData(null, new[] { "--allow-dangerously-skip-permissions" }, true)]
    [InlineData(null, new[] { "--permission-mode", "bypassPermissions" }, true)]
    [InlineData(null, new[] { "--permission-mode=bypassPermissions" }, true)]
    [InlineData("acceptEdits", new[] { "--continue" }, false)]
    public void BypassDetectionCoversFlagsAndModes(string? permissionMode, string[] extra, bool expected)
    {
        var request = new ClaudeLaunchRequest(@"C:\dev", "go", ClaudeLaunchMode.Autonomous, permissionMode, MaxTurns: null, ExtraArguments: extra);

        Assert.Equal(expected, ClaudeCommand.BypassesPermissions(request));
    }

    [Theory]
    [InlineData("-p", true)]
    [InlineData("--output-format", true)]
    [InlineData("--", true)]
    [InlineData("-pv", true)]
    [InlineData("--model", false)]
    [InlineData("--continue", false)]
    public void ReservedArgumentsAreDetected(string argument, bool reserved)
        => Assert.Equal(reserved, ClaudeCommand.FindReservedArgument([argument]) is not null);

    private static ProcessLaunch Build(ClaudeLaunchRequest request) => ClaudeCommand.Build(NativeExecutable, request);
}
