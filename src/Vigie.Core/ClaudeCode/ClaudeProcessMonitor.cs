using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum ClaudeProcessOrigin
{
    Vigie,
    External,

    /// <summary>A session of the Claude desktop app, which keeps its process
    /// open between prompts.</summary>
    DesktopApp,
}

public sealed record ClaudeProcessInfo(
    int ProcessId,
    string? ExecutablePath,
    DateTimeOffset? StartTimeUtc,
    ClaudeProcessOrigin Origin,
    bool IsNodeScript)
{
    /// <summary>Neither the process nor the commands it runs have done any work
    /// for a while: a session waiting for its next prompt.</summary>
    public bool IsIdle { get; init; }

    /// <summary>When the process or one of its commands last did some work, on
    /// the clock's tick counter.</summary>
    public long LastActiveTick { get; init; }
}

public sealed record ExternalExit(ClaudeProcessInfo Process, int? ExitCode);

/// <param name="NodeScanError">Set when the npm process query failed; the
/// previous list of npm processes is reused until a query succeeds.</param>
public sealed record MonitorScan(
    IReadOnlyList<ClaudeProcessInfo> Processes,
    IReadOnlyList<ExternalExit> ExternalExits,
    string? NodeScanError);

/// <summary>
/// Lists the running Claude Code processes. A <c>claude.exe</c> counts only if
/// it is a console executable, which leaves out the desktop application; an
/// older npm installation shows up as a <c>node.exe</c> running Claude Code's
/// script. Processes started elsewhere get a handle as soon as they are seen,
/// so their exit code is still known after they end.
/// </summary>
/// <remarks>
/// An open process is not a running task: a terminal or desktop-app session
/// stays open while it waits for the next prompt. Activity is the CPU time of
/// the process and of the commands it runs, a command starting or ending, or a
/// command still running, however quiet: a background task waiting on a build
/// or serving a page. A session waiting for a prompt spends a few milliseconds
/// a second; one at work, far more.
/// </remarks>
public sealed class ClaudeProcessMonitor(IProcessApi processes, IFileSystem fileSystem, IClock clock) : IDisposable
{
    private const long NodeScanIntervalMilliseconds = 10_000;

    private readonly Dictionary<string, bool> _isConsoleByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int ProcessId, DateTimeOffset? StartTimeUtc), Tracked> _external = [];
    private readonly Dictionary<(int ProcessId, DateTimeOffset? StartTimeUtc), Activity> _activity = [];
    private IReadOnlyList<CommandLineProcessEntry> _nodeProcesses = [];
    private long? _lastNodeScan;

    /// <summary>The span over which CPU time is added up.</summary>
    public static TimeSpan ActivityWindow { get; } = TimeSpan.FromSeconds(30);

    /// <summary>CPU time within the window that counts as work.</summary>
    public static TimeSpan ActivityThreshold { get; } = TimeSpan.FromMilliseconds(600);

    /// <summary>How long without work before a process is shown as idle.</summary>
    public static TimeSpan IdleAfter { get; } = TimeSpan.FromMinutes(2);

    /// <summary>How long a followed task may do nothing before it counts as finished.</summary>
    public static TimeSpan TaskIdleLimit { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// What Claude Code starts this soon after its own start is one of its
    /// helpers, such as an MCP server, and lives as long as the session. What
    /// it starts later runs a command, and counts as work while it runs.
    /// </summary>
    public static TimeSpan HelperWindow { get; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a followed task is over although its process is still open: it
    /// and its commands have done nothing for <see cref="TaskIdleLimit"/>,
    /// counted from <paramref name="sinceTick"/> at the earliest, so a session
    /// that was already waiting when the wake session started gets the full
    /// limit.
    /// </summary>
    public static bool TaskLooksFinished(ClaudeProcessInfo process, long sinceTick, long nowTick)
        => nowTick - Math.Max(process.LastActiveTick, sinceTick) >= TaskIdleLimit.TotalMilliseconds;

    public MonitorScan Scan(IReadOnlySet<int> vigieProcessIds)
    {
        string? nodeScanError = RefreshNodeProcessesIfDue();
        IReadOnlyList<ProcessParent> parents = processes.ListParents();
        var parentOf = new Dictionary<int, int>(parents.Count);
        foreach (ProcessParent link in parents)
        {
            parentOf[link.ProcessId] = link.ParentProcessId;
        }

        ILookup<int, int> childrenOf = parents.ToLookup(p => p.ParentProcessId, p => p.ProcessId);
        var desktopApps = new HashSet<int>();
        var consoleProcesses = new List<ProcessEntry>();
        foreach (ProcessEntry entry in processes.FindProcesses("claude"))
        {
            if (entry.ExecutablePath is not null && IsConsoleExecutable(entry.ExecutablePath))
            {
                consoleProcesses.Add(entry);
            }
            else
            {
                desktopApps.Add(entry.ProcessId);
            }
        }

        ClaudeProcessOrigin OriginOf(int processId)
        {
            if (vigieProcessIds.Contains(processId))
            {
                return ClaudeProcessOrigin.Vigie;
            }

            return parentOf.TryGetValue(processId, out int parent) && desktopApps.Contains(parent)
                ? ClaudeProcessOrigin.DesktopApp
                : ClaudeProcessOrigin.External;
        }

        var found = new List<ClaudeProcessInfo>();
        foreach (ProcessEntry entry in consoleProcesses)
        {
            found.Add(WithActivity(new ClaudeProcessInfo(entry.ProcessId, entry.ExecutablePath, entry.StartTimeUtc, OriginOf(entry.ProcessId), IsNodeScript: false), childrenOf));
        }

        foreach (CommandLineProcessEntry node in _nodeProcesses)
        {
            if (node.CommandLine is not null
                && IsClaudeCodeScript(node.CommandLine)
                && !HasExternalExited(node.ProcessId, node.StartTimeUtc))
            {
                found.Add(WithActivity(new ClaudeProcessInfo(node.ProcessId, node.ExecutablePath, node.StartTimeUtc, OriginOf(node.ProcessId), IsNodeScript: true), childrenOf));
            }
        }

        ForgetActivityOfGoneProcesses(found);
        return new MonitorScan(found, TrackExternal(found), nodeScanError);
    }

    public void Dispose()
    {
        foreach (Tracked tracked in _external.Values)
        {
            tracked.Handle?.Dispose();
        }

        _external.Clear();
    }

    private static bool IsClaudeCodeScript(string commandLine)
        => commandLine.Contains(@"@anthropic-ai\claude-code", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("@anthropic-ai/claude-code", StringComparison.OrdinalIgnoreCase);

    private string? RefreshNodeProcessesIfDue()
    {
        long now = clock.TickCountMilliseconds;
        if (_lastNodeScan is long last && now - last < NodeScanIntervalMilliseconds)
        {
            return null;
        }

        _lastNodeScan = now;
        try
        {
            _nodeProcesses = processes.FindNodeProcesses();
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private bool IsConsoleExecutable(string path)
    {
        if (_isConsoleByPath.TryGetValue(path, out bool cached))
        {
            return cached;
        }

        bool isConsole;
        try
        {
            using Stream stream = fileSystem.OpenRead(path);
            isConsole = PeImage.ReadSubsystem(stream) == (ushort)PeSubsystem.WindowsConsole;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An image that cannot be read cannot be told apart from the
            // desktop application, so it is left out.
            isConsole = false;
        }

        _isConsoleByPath[path] = isConsole;
        return isConsole;
    }

    private ClaudeProcessInfo WithActivity(ClaudeProcessInfo process, ILookup<int, int> childrenOf)
    {
        long now = clock.TickCountMilliseconds;
        (Dictionary<(int, DateTimeOffset), TimeSpan> tree, bool commandRunning) = TreeCpuTimes(process.ProcessId, childrenOf);
        var key = (process.ProcessId, process.StartTimeUtc);

        if (!_activity.TryGetValue(key, out Activity? activity))
        {
            // Whether a process seen for the first time was busy is unknown:
            // unless a command is running, it shows as idle once a full window
            // has passed without work.
            activity = new Activity(tree, commandRunning ? now : now - (long)(IdleAfter - ActivityWindow).TotalMilliseconds);
            _activity[key] = activity;
        }
        else
        {
            double spent = tree.Sum(member => (member.Value - activity.CpuTimes.GetValueOrDefault(member.Key)).TotalMilliseconds);
            bool commandsChanged = !activity.CpuTimes.Keys.ToHashSet().SetEquals(tree.Keys);
            activity.CpuTimes = tree;
            activity.Spent.Enqueue((now, Math.Max(spent, 0)));
            while (activity.Spent.Peek().Tick <= now - ActivityWindow.TotalMilliseconds)
            {
                activity.Spent.Dequeue();
            }

            if (commandRunning
                || commandsChanged
                || activity.Spent.Sum(sample => sample.Milliseconds) >= ActivityThreshold.TotalMilliseconds)
            {
                activity.LastActiveTick = now;
            }
        }

        return process with
        {
            IsIdle = now - activity.LastActiveTick >= IdleAfter.TotalMilliseconds,
            LastActiveTick = activity.LastActiveTick,
        };
    }

    // The process and everything it started, each with its CPU time, and
    // whether one of its own children runs a command (see HelperWindow). A
    // parent identifier outlives its process and can be given to another one,
    // so a child older than its parent belongs to someone else and is left out.
    private (Dictionary<(int, DateTimeOffset), TimeSpan> Tree, bool CommandRunning) TreeCpuTimes(int rootId, ILookup<int, int> childrenOf)
    {
        var tree = new Dictionary<(int, DateTimeOffset), TimeSpan>();
        bool commandRunning = false;
        var pending = new Stack<(int ProcessId, DateTimeOffset? ParentStart, bool IsRootChild)>();
        pending.Push((rootId, null, false));
        while (pending.Count > 0)
        {
            (int processId, DateTimeOffset? parentStart, bool isRootChild) = pending.Pop();
            if (processes.GetTimes(processId) is not { } times
                || times.StartTimeUtc < parentStart
                || !tree.TryAdd((processId, times.StartTimeUtc), times.CpuTime))
            {
                continue;
            }

            if (isRootChild && times.StartTimeUtc - parentStart >= HelperWindow)
            {
                commandRunning = true;
            }

            foreach (int child in childrenOf[processId])
            {
                pending.Push((child, times.StartTimeUtc, processId == rootId));
            }
        }

        return (tree, commandRunning);
    }

    private void ForgetActivityOfGoneProcesses(List<ClaudeProcessInfo> found)
    {
        var alive = found.Select(p => (p.ProcessId, p.StartTimeUtc)).ToHashSet();
        foreach (var key in _activity.Keys.Where(key => !alive.Contains(key)).ToList())
        {
            _activity.Remove(key);
        }
    }

    private bool HasExternalExited(int processId, DateTimeOffset? startTimeUtc)
        => _external.TryGetValue((processId, startTimeUtc), out Tracked? tracked) && tracked.Handle?.HasExited == true;

    private List<ExternalExit> TrackExternal(List<ClaudeProcessInfo> found)
    {
        var seen = new HashSet<(int, DateTimeOffset?)>();
        foreach (ClaudeProcessInfo process in found)
        {
            if (process.Origin == ClaudeProcessOrigin.Vigie)
            {
                continue;
            }

            var key = (process.ProcessId, process.StartTimeUtc);
            seen.Add(key);
            if (!_external.ContainsKey(key))
            {
                _external[key] = new Tracked(process, processes.Observe(process.ProcessId));
            }
        }

        var exits = new List<ExternalExit>();
        foreach (var (key, tracked) in _external.Where(pair => !seen.Contains(pair.Key)).ToList())
        {
            exits.Add(new ExternalExit(tracked.Process, tracked.Handle?.ExitCode));
            tracked.Handle?.Dispose();
            _external.Remove(key);
        }

        return exits;
    }

    private sealed record Tracked(ClaudeProcessInfo Process, IObservedProcess? Handle);

    private sealed class Activity(Dictionary<(int, DateTimeOffset), TimeSpan> cpuTimes, long lastActiveTick)
    {
        public Dictionary<(int, DateTimeOffset), TimeSpan> CpuTimes { get; set; } = cpuTimes;

        public Queue<(long Tick, double Milliseconds)> Spent { get; } = new();

        public long LastActiveTick { get; set; } = lastActiveTick;
    }
}
