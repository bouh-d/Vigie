using Vigie.Core.Platform;

namespace Vigie.Core.Tests.Fakes;

public sealed class FakeProcessApi : IProcessApi
{
    public List<ProcessEntry> Processes { get; } = [];

    public List<CommandLineProcessEntry> NodeProcesses { get; } = [];

    public bool NodeQueryFails { get; set; }

    public Dictionary<int, DateTimeOffset?> StartTimes { get; } = [];

    public Dictionary<int, FakeObservedProcess> Observable { get; } = [];

    public Queue<ProcessRunResult> RunResults { get; } = [];

    public List<IReadOnlyList<string>> Runs { get; } = [];

    public List<(string FileName, IReadOnlyList<string> Arguments)> Detached { get; } = [];

    public IReadOnlyList<ProcessEntry> FindProcesses(string processName)
        => Processes.Where(p => string.Equals(ImageName(p.ExecutablePath), processName, StringComparison.OrdinalIgnoreCase)).ToList();

    public IReadOnlyList<CommandLineProcessEntry> FindNodeProcesses()
        => NodeQueryFails ? throw new InvalidOperationException("WMI unavailable") : NodeProcesses;

    /// <summary>Runs after each start-time read, to change what the next one
    /// sees.</summary>
    public Action<int>? AfterStartTimeRead { get; set; }

    public DateTimeOffset? GetStartTimeUtc(int processId)
    {
        DateTimeOffset? started = StartTimes.GetValueOrDefault(processId);
        AfterStartTimeRead?.Invoke(processId);
        return started;
    }

    public IObservedProcess? Observe(int processId) => Observable.GetValueOrDefault(processId);

    public IManagedProcess Start(ProcessLaunch launch, IProcessOutputSink? outputSink)
        => throw new NotSupportedException();

    public void StartDetached(string fileName, IReadOnlyList<string> arguments)
        => Detached.Add((fileName, arguments));

    public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add(arguments);
        return Task.FromResult(RunResults.Count > 0 ? RunResults.Dequeue() : new ProcessRunResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static string? ImageName(string? path) => path is null ? null : Path.GetFileNameWithoutExtension(path);
}

public sealed class FakeObservedProcess : IObservedProcess
{
    public bool HasExited { get; set; }

    public int? ExitCode { get; set; }

    public WaitHandle ExitWaitHandle { get; } = new ManualResetEvent(initialState: false);

    public void Dispose() => ExitWaitHandle.Dispose();
}
