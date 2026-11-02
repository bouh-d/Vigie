using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum ClaudeProcessOrigin
{
    Vigie,
    External,
}

public sealed record ClaudeProcessInfo(
    int ProcessId,
    string? ExecutablePath,
    DateTimeOffset? StartTimeUtc,
    ClaudeProcessOrigin Origin,
    bool IsNodeScript);

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
public sealed class ClaudeProcessMonitor(IProcessApi processes, IFileSystem fileSystem, IClock clock) : IDisposable
{
    private const long NodeScanIntervalMilliseconds = 10_000;

    private readonly Dictionary<string, bool> _isConsoleByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int ProcessId, DateTimeOffset? StartTimeUtc), Tracked> _external = [];
    private IReadOnlyList<CommandLineProcessEntry> _nodeProcesses = [];
    private long? _lastNodeScan;

    public MonitorScan Scan(IReadOnlySet<int> vigieProcessIds)
    {
        string? nodeScanError = RefreshNodeProcessesIfDue();
        var found = new List<ClaudeProcessInfo>();

        foreach (ProcessEntry entry in processes.FindProcesses("claude"))
        {
            if (entry.ExecutablePath is not null && IsConsoleExecutable(entry.ExecutablePath))
            {
                found.Add(new ClaudeProcessInfo(entry.ProcessId, entry.ExecutablePath, entry.StartTimeUtc, OriginOf(entry.ProcessId, vigieProcessIds), IsNodeScript: false));
            }
        }

        foreach (CommandLineProcessEntry node in _nodeProcesses)
        {
            if (node.CommandLine is not null
                && IsClaudeCodeScript(node.CommandLine)
                && !HasExternalExited(node.ProcessId, node.StartTimeUtc))
            {
                found.Add(new ClaudeProcessInfo(node.ProcessId, node.ExecutablePath, node.StartTimeUtc, OriginOf(node.ProcessId, vigieProcessIds), IsNodeScript: true));
            }
        }

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

    private static ClaudeProcessOrigin OriginOf(int processId, IReadOnlySet<int> vigieProcessIds)
        => vigieProcessIds.Contains(processId) ? ClaudeProcessOrigin.Vigie : ClaudeProcessOrigin.External;

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

    private bool HasExternalExited(int processId, DateTimeOffset? startTimeUtc)
        => _external.TryGetValue((processId, startTimeUtc), out Tracked? tracked) && tracked.Handle?.HasExited == true;

    private List<ExternalExit> TrackExternal(List<ClaudeProcessInfo> found)
    {
        var seen = new HashSet<(int, DateTimeOffset?)>();
        foreach (ClaudeProcessInfo process in found)
        {
            if (process.Origin != ClaudeProcessOrigin.External)
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
}
