using Vigie.Core.ClaudeCode;
using Vigie.Core.Platform;
using Vigie.Core.Session;
using Xunit;

namespace Vigie.Core.Tests;

public class HomeLabTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheServerStartsInTheClaudeCodeFolderWithTheChosenPermissions()
    {
        var installation = new ClaudeInstallation(ClaudeInstallationSource.NativeInstaller, @"C:\Users\dev\.local\bin\claude.exe");

        ProcessLaunch launch = RemoteControl.Build(installation, @"C:\work\lab", "acceptEdits");

        Assert.Equal(@"C:\Users\dev\.local\bin\claude.exe", launch.FileName);
        Assert.Equal(["remote-control", "--permission-mode", "acceptEdits"], launch.Arguments);
        Assert.Equal(@"C:\work\lab", launch.WorkingDirectory);
        Assert.Equal(["remote-control"], RemoteControl.Build(installation, @"C:\work\lab", permissionMode: null).Arguments);
    }

    [Fact]
    public void AnNpmInstallationGetsItsScriptBeforeTheSubcommand()
    {
        var installation = new ClaudeInstallation(ClaudeInstallationSource.Path, @"C:\nvm\node.exe", @"C:\npm\claude-code\cli.js");

        Assert.Equal([@"C:\npm\claude-code\cli.js", "remote-control"], RemoteControl.Build(installation, @"C:\work\lab", null).Arguments);
    }

    [Fact]
    public void EveryPermissionModeIsPassedOn()
    {
        var installation = new ClaudeInstallation(ClaudeInstallationSource.NativeInstaller, @"C:\claude.exe");

        Assert.Equal(["remote-control", "--permission-mode", "bypassPermissions"], RemoteControl.Build(installation, @"C:\work\lab", "bypassPermissions").Arguments);
    }

    [Fact]
    public void WithoutAUsableFolderTheServerRunsInTheUserFolder()
    {
        const string UserFolder = @"C:\Users\dev";

        Assert.Equal(UserFolder, RemoteControl.FolderOrDefault(null, _ => true, UserFolder));
        Assert.Equal(UserFolder, RemoteControl.FolderOrDefault(@"C:\gone", _ => false, UserFolder));
        Assert.Equal(@"C:\work\lab", RemoteControl.FolderOrDefault(@"C:\work\lab", _ => true, UserFolder));
    }

    [Fact]
    public void AFirstStartThatEndsWithinSecondsIsReportedAndStillRestarted()
    {
        RemoteControlDecision decision = RemoteControl.OnExit(Start, Start.AddSeconds(5), firstStart: true, restartsThisHour: 0);

        Assert.Equal(RemoteControlExitAction.FailedAtStart, decision.Action);
        Assert.Equal(Start.AddSeconds(35), decision.RestartAt);
    }

    [Fact]
    public void AServerThatStopsLaterIsRestartedLaterAndLaterUpToTenMinutes()
    {
        DateTimeOffset exit = Start.AddHours(3);

        Assert.Equal(exit.AddSeconds(30), RemoteControl.OnExit(Start, exit, firstStart: true, restartsThisHour: 0).RestartAt);

        // A restart that fails at once is not a start failure: it waits longer.
        DateTimeOffset quickExit = exit.AddSeconds(5);
        Assert.Equal(quickExit.AddMinutes(2), RemoteControl.OnExit(exit, quickExit, firstStart: false, restartsThisHour: 2).RestartAt);
        Assert.Equal(exit.AddMinutes(10), RemoteControl.OnExit(Start, exit, firstStart: false, restartsThisHour: 7).RestartAt);
    }

    [Fact]
    public void AHomeLabSessionHasNoEndTime()
    {
        var options = new SessionOptions { DurationKind = SessionDurationKind.UntilStopped };

        Assert.Null(options.EffectiveCeiling);
        Assert.True(options.IsValid(out _));
        Assert.Equal("Home lab started: the PC stays awake until you stop it, running with the lid closed.", SessionNarration.Started(options));
    }
}
