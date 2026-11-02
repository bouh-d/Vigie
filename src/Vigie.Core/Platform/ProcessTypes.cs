namespace Vigie.Core.Platform;

public sealed record ProcessEntry(int ProcessId, string? ExecutablePath, DateTimeOffset? StartTimeUtc);

public sealed record CommandLineProcessEntry(
    int ProcessId,
    string? ExecutablePath,
    string? CommandLine,
    DateTimeOffset? StartTimeUtc);

/// <summary>
/// What to start and how. Arguments are always passed one by one: the
/// implementation hands them to <c>ProcessStartInfo.ArgumentList</c> and never
/// builds a command-line string or goes through a shell.
/// </summary>
public sealed record ProcessLaunch(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>Receives redirected output, one line at a time, from any thread.</summary>
public interface IProcessOutputSink
{
    void OnOutputLine(string line);

    void OnErrorLine(string line);
}

/// <summary>A process Vigie did not start but keeps a handle on, so its exit code
/// survives the process.</summary>
public interface IObservedProcess : IDisposable
{
    bool HasExited { get; }

    int? ExitCode { get; }

    /// <summary>Signaled when the process exits.</summary>
    WaitHandle ExitWaitHandle { get; }
}

/// <summary>A process Vigie started and owns.</summary>
public interface IManagedProcess : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Sends Ctrl+C to the process console. Returns false when the
    /// console could not be reached.</summary>
    bool TrySendCtrlC();

    void KillTree();
}
