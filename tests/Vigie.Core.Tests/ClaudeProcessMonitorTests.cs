using Vigie.Core.ClaudeCode;
using Vigie.Core.Platform;
using Vigie.Core.Tests.Fakes;
using Vigie.Core.Tests.Support;
using Xunit;

namespace Vigie.Core.Tests;

public class ClaudeProcessMonitorTests
{
    private readonly FakeProcessApi _processes = new();
    private readonly FakeFileSystem _fileSystem = new();
    private readonly FakeClock _clock = new();

    [Fact]
    public void OnlyConsoleClaudeExecutablesAreListed()
    {
        string consolePath = TestPaths.Of("Users", "dev", ".local", "bin", "claude.exe");
        string desktopPath = TestPaths.Of("Program Files", "Claude", "claude.exe");
        _fileSystem.WriteBytes(consolePath, PeImageBuilder.Console());
        _fileSystem.WriteBytes(desktopPath, PeImageBuilder.Gui());
        _processes.Processes.Add(new ProcessEntry(100, consolePath, StartUtc));
        _processes.Processes.Add(new ProcessEntry(200, desktopPath, StartUtc));

        MonitorScan scan = Monitor().Scan(new HashSet<int> { 100 });

        ClaudeProcessInfo process = Assert.Single(scan.Processes);
        Assert.Equal(100, process.ProcessId);
        Assert.Equal(ClaudeProcessOrigin.Vigie, process.Origin);
    }

    [Fact]
    public void AnExternalProcessKeepsItsExitCodeAfterItEnds()
    {
        string path = TestPaths.Of("tools", "claude.exe");
        _fileSystem.WriteBytes(path, PeImageBuilder.Console());
        _processes.Processes.Add(new ProcessEntry(300, path, StartUtc));
        var observed = new FakeObservedProcess();
        _processes.Observable[300] = observed;

        ClaudeProcessMonitor monitor = Monitor();
        MonitorScan first = monitor.Scan(NoVigie);
        Assert.Equal(ClaudeProcessOrigin.External, Assert.Single(first.Processes).Origin);

        _processes.Processes.Clear();
        observed.HasExited = true;
        observed.ExitCode = 1;
        MonitorScan second = monitor.Scan(NoVigie);

        ExternalExit exit = Assert.Single(second.ExternalExits);
        Assert.Equal(1, exit.ExitCode);
    }

    [Fact]
    public void AnOldNpmInstallationIsListedThroughNode()
    {
        _processes.NodeProcesses.Add(new CommandLineProcessEntry(
            400,
            TestPaths.Of("nvm", "node.exe"),
            @"node.exe C:\npm\node_modules\@anthropic-ai\claude-code\cli.js -p",
            StartUtc));

        ClaudeProcessInfo process = Assert.Single(Monitor().Scan(NoVigie).Processes);

        Assert.Equal(400, process.ProcessId);
        Assert.True(process.IsNodeScript);
    }

    [Fact]
    public void AFailedNodeQueryKeepsThePreviousListAndReportsTheError()
    {
        _processes.NodeProcesses.Add(new CommandLineProcessEntry(
            400, TestPaths.Of("node.exe"), @"node C:\x\@anthropic-ai\claude-code\cli.js", StartUtc));
        ClaudeProcessMonitor monitor = Monitor();
        monitor.Scan(NoVigie);

        _processes.NodeQueryFails = true;
        _clock.Advance(TimeSpan.FromSeconds(11));
        MonitorScan scan = monitor.Scan(NoVigie);

        Assert.NotNull(scan.NodeScanError);
        Assert.Contains(scan.Processes, p => p.ProcessId == 400);
    }

    private ClaudeProcessMonitor Monitor() => new(_processes, _fileSystem, _clock);

    private static HashSet<int> NoVigie => [];

    private static DateTimeOffset StartUtc => new(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);
}
