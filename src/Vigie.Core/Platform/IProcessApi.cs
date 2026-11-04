namespace Vigie.Core.Platform;

public interface IProcessApi
{
    /// <summary>Processes whose image name is <paramref name="processName"/>,
    /// without the <c>.exe</c> extension.</summary>
    IReadOnlyList<ProcessEntry> FindProcesses(string processName);

    /// <summary><c>node.exe</c> processes together with their command lines.
    /// Backed by WMI, so callers keep the polling rate low.</summary>
    /// <exception cref="InvalidOperationException">WMI could not be queried.</exception>
    IReadOnlyList<CommandLineProcessEntry> FindNodeProcesses();

    /// <summary>The start time of a running process, or null when no process
    /// with that identifier is running or it cannot be queried.</summary>
    DateTimeOffset? GetStartTimeUtc(int processId);

    /// <summary>Every running process with its parent, from one snapshot.</summary>
    IReadOnlyList<ProcessParent> ListParents();

    /// <summary>Start and CPU time of a process, or null when it cannot be queried.</summary>
    ProcessTimes? GetTimes(int processId);

    IObservedProcess? Observe(int processId);

    /// <summary>Starts a process. When <paramref name="outputSink"/> is set its
    /// standard output and error are redirected to it and standard input is
    /// closed; otherwise the process gets a visible console of its own.</summary>
    IManagedProcess Start(ProcessLaunch launch, IProcessOutputSink? outputSink);

    /// <summary>Starts a process without a window and without keeping a handle
    /// to it, so it outlives Vigie.</summary>
    void StartDetached(string fileName, IReadOnlyList<string> arguments);

    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
