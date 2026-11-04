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

    [Fact]
    public void ASessionThatKeepsUsingTheCpuIsWorking()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();

        ClaudeProcessInfo session = ScanEvery3Seconds(monitor, TimeSpan.FromMinutes(3), () => Spend(SessionId, 150));

        Assert.False(session.IsIdle);
    }

    [Fact]
    public void ASessionWaitingForAPromptShowsAsIdleOnceAFullWindowIsQuiet()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();

        Assert.False(ScanEvery3Seconds(monitor, TimeSpan.FromSeconds(15), () => Spend(SessionId, 15)).IsIdle);
        Assert.True(ScanEvery3Seconds(monitor, TimeSpan.FromSeconds(18), () => Spend(SessionId, 15)).IsIdle);
    }

    [Fact]
    public void ASessionThatWorkedShowsAsIdleOnlyAfterTwoQuietMinutes()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();
        ScanEvery3Seconds(monitor, TimeSpan.FromSeconds(30), () => Spend(SessionId, 300));

        // The work stays in the 30-second window for a while after it stops,
        // so the quiet minutes start to count a little later.
        Assert.False(ScanEvery3Seconds(monitor, TimeSpan.FromMinutes(2), () => { }).IsIdle);
        Assert.True(ScanEvery3Seconds(monitor, TimeSpan.FromSeconds(30), () => { }).IsIdle);
    }

    [Fact]
    public void ACommandTheSessionRunsKeepsItWorking()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();
        AddCommand(101, SessionId);

        ClaudeProcessInfo session = ScanEvery3Seconds(monitor, TimeSpan.FromMinutes(3), () => Spend(101, 300));

        Assert.False(session.IsIdle);
    }

    [Fact]
    public void ACommandStartingCountsAsWork()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();
        Assert.True(ScanEvery3Seconds(monitor, TimeSpan.FromMinutes(3), () => { }).IsIdle);

        AddCommand(101, SessionId);

        Assert.False(ScanEvery3Seconds(monitor, TimeSpan.FromSeconds(3), () => { }).IsIdle);
    }

    [Fact]
    public void AProcessOlderThanItsParentIsNotOneOfItsCommands()
    {
        ClaudeProcessMonitor monitor = MonitorWithSession();

        // Its parent identifier is the session's, but it was given out before
        // the session started.
        _processes.Parents[102] = SessionId;
        _processes.Times[102] = new ProcessTimes(StartUtc - TimeSpan.FromHours(1), TimeSpan.Zero);

        Assert.True(ScanEvery3Seconds(monitor, TimeSpan.FromMinutes(3), () => Spend(102, 500)).IsIdle);
    }

    [Fact]
    public void ASessionOfTheDesktopAppIsTaggedAsSuch()
    {
        string desktopPath = TestPaths.Of("Program Files", "Claude", "claude.exe");
        _fileSystem.WriteBytes(desktopPath, PeImageBuilder.Gui());
        _processes.Processes.Add(new ProcessEntry(50, desktopPath, StartUtc));
        ClaudeProcessMonitor monitor = MonitorWithSession();
        _processes.Parents[SessionId] = 50;

        ClaudeProcessInfo session = Assert.Single(monitor.Scan(NoVigie).Processes);

        Assert.Equal(ClaudeProcessOrigin.DesktopApp, session.Origin);
    }

    [Fact]
    public void AFollowedTaskCountsAsFinishedAfterTenQuietMinutesWithinTheWakeSession()
    {
        long sessionStart = (long)TimeSpan.FromHours(1).TotalMilliseconds;
        var waitingBefore = new ClaudeProcessInfo(SessionId, null, StartUtc, ClaudeProcessOrigin.DesktopApp, IsNodeScript: false) { LastActiveTick = 0 };
        ClaudeProcessInfo workedDuring = waitingBefore with { LastActiveTick = sessionStart + Minutes(5) };

        Assert.False(ClaudeProcessMonitor.TaskLooksFinished(waitingBefore, sessionStart, sessionStart + Minutes(9)));
        Assert.True(ClaudeProcessMonitor.TaskLooksFinished(waitingBefore, sessionStart, sessionStart + Minutes(10)));
        Assert.False(ClaudeProcessMonitor.TaskLooksFinished(workedDuring, sessionStart, sessionStart + Minutes(14)));
        Assert.True(ClaudeProcessMonitor.TaskLooksFinished(workedDuring, sessionStart, sessionStart + Minutes(15)));
    }

    private const int SessionId = 100;

    private ClaudeProcessMonitor Monitor() => new(_processes, _fileSystem, _clock);

    private ClaudeProcessMonitor MonitorWithSession()
    {
        string path = TestPaths.Of("Users", "dev", ".local", "bin", "claude.exe");
        _fileSystem.WriteBytes(path, PeImageBuilder.Console());
        _processes.Processes.Add(new ProcessEntry(SessionId, path, StartUtc));
        _processes.Times[SessionId] = new ProcessTimes(StartUtc, TimeSpan.Zero);
        return Monitor();
    }

    private void AddCommand(int processId, int parentId)
    {
        _processes.Parents[processId] = parentId;
        _processes.Times[processId] = new ProcessTimes(StartUtc + TimeSpan.FromMinutes(1), TimeSpan.Zero);
    }

    private void Spend(int processId, int milliseconds)
        => _processes.Times[processId] = _processes.Times[processId] with { CpuTime = _processes.Times[processId].CpuTime + TimeSpan.FromMilliseconds(milliseconds) };

    private ClaudeProcessInfo ScanEvery3Seconds(ClaudeProcessMonitor monitor, TimeSpan duration, Action beforeEachScan)
    {
        ClaudeProcessInfo? last = null;
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += TimeSpan.FromSeconds(3))
        {
            beforeEachScan();
            _clock.Advance(TimeSpan.FromSeconds(3));
            last = monitor.Scan(NoVigie).Processes.Single(p => p.ProcessId == SessionId);
        }

        return last!;
    }

    private static long Minutes(int minutes) => (long)TimeSpan.FromMinutes(minutes).TotalMilliseconds;

    private static HashSet<int> NoVigie => [];

    private static DateTimeOffset StartUtc => new(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);
}
