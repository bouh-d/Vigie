using Vigie.Core.Platform;

namespace Vigie.Core.Tests.Fakes;

public sealed class FakeProcessApi : IProcessApi
{
    public List<ProcessEntry> Processes { get; } = [];

    public List<CommandLineProcessEntry> NodeProcesses { get; } = [];

    public bool NodeQueryFails { get; set; }

    public Dictionary<int, DateTimeOffset?> StartTimes { get; } = [];

    public Dictionary<int, FakeObservedProcess> Observable { get; } = [];

    public Dictionary<int, int> Parents { get; } = [];

    public Dictionary<int, ProcessTimes> Times { get; } = [];

    public Queue<ProcessRunResult> RunResults { get; } = [];

    public List<IReadOnlyList<string>> Runs { get; } = [];

    public List<(string FileName, IReadOnlyList<string> Arguments)> Detached { get; } = [];

    /// <summary>What <see cref="Start"/> returns; without one it throws.</summary>
    public FakeManagedProcess? NextProcess { get; set; }

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

    public IReadOnlyList<ProcessParent> ListParents()
        => Parents.Select(pair => new ProcessParent(pair.Key, pair.Value)).ToList();

    public ProcessTimes? GetTimes(int processId) => Times.TryGetValue(processId, out ProcessTimes times) ? times : null;

    public IObservedProcess? Observe(int processId) => Observable.GetValueOrDefault(processId);

    public IManagedProcess Start(ProcessLaunch launch, IProcessOutputSink? outputSink)
        => NextProcess ?? throw new NotSupportedException();

    public void StartDetached(string fileName, IReadOnlyList<string> arguments)
        => Detached.Add((fileName, arguments));

    public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add(arguments);
        return Task.FromResult(RunResults.Count > 0 ? RunResults.Dequeue() : new ProcessRunResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static string? ImageName(string? path) => path is null ? null : Path.GetFileNameWithoutExtension(path);
}

public sealed class FakeManagedProcess(int processId) : IManagedProcess
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ProcessId { get; } = processId;

    public bool HasExited => _exit.Task.IsCompleted;

    public void Exit(int exitCode) => _exit.TrySetResult(exitCode);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _exit.Task.WaitAsync(cancellationToken);

    public bool TrySendCtrlC() => false;

    public void KillTree() => Exit(-1);

    public void Dispose()
    {
    }
}

public sealed class FakeObservedProcess : IObservedProcess
{
    public bool HasExited { get; set; }

    public int? ExitCode { get; set; }

    public WaitHandle ExitWaitHandle { get; } = new ManualResetEvent(initialState: false);

    public void Dispose() => ExitWaitHandle.Dispose();
}
