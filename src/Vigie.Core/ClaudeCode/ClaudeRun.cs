using System.Text;
using Vigie.Core.Platform;

namespace Vigie.Core.ClaudeCode;

public enum InterruptOutcome
{
    AlreadyExited,
    StoppedCleanly,
    Killed,
}

public sealed record ClaudeRunSnapshot(
    int ProcessId,
    ClaudeLaunchMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset? ExitedAt,
    int? ExitCode,
    ResultEvent? Result,
    ApiRetryEvent? LastRetry,
    int PermissionDenials,
    string? LastErrorLine,
    bool InterruptRequested,
    string? ClaudeSessionId,
    string? RecordingError,
    UsageLimitEvent? UsageLimit);

/// <summary>
/// A Claude Code process started by Vigie. In autonomous mode its output is
/// parsed as it arrives and copied to <c>output.jsonl</c> and
/// <c>stderr.log</c> in the run directory.
/// </summary>
public sealed class ClaudeRun : IProcessOutputSink, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly IClock _clock;
    private Stream? _output;
    private Stream? _errors;
    private IManagedProcess? _process;
    private bool _disposed;
    private string? _recordingError;
    private DateTimeOffset? _exitedAt;
    private int? _exitCode;
    private ResultEvent? _result;
    private ApiRetryEvent? _lastRetry;
    private int _permissionDenials;
    private string? _lastErrorLine;
    private bool _interruptRequested;
    private string? _claudeSessionId;
    private UsageLimitEvent? _usageLimit;

    private ClaudeRun(ClaudeLaunchMode mode, IClock clock, IFileSystem fileSystem, string? runDirectory)
    {
        Mode = mode;
        _clock = clock;
        StartedAt = clock.Now;
        RunDirectory = runDirectory;
        if (runDirectory is not null)
        {
            _output = fileSystem.OpenAppend(Path.Combine(runDirectory, "output.jsonl"));
            _errors = fileSystem.OpenAppend(Path.Combine(runDirectory, "stderr.log"));
        }
    }

    /// <summary>Raised on a thread-pool thread for every event Vigie acts on.</summary>
    public event EventHandler<ClaudeStreamEvent>? EventReceived;

    /// <summary>Raised on a thread-pool thread once the process has exited.</summary>
    public event EventHandler? Exited;

    public ClaudeLaunchMode Mode { get; }

    public DateTimeOffset StartedAt { get; }

    public string? RunDirectory { get; }

    public int ProcessId => _process?.ProcessId ?? 0;

    public bool HasExited
    {
        get
        {
            lock (_gate)
            {
                return _exitCode is not null;
            }
        }
    }

    /// <param name="runDirectory">Where autonomous output is recorded; null for
    /// an interactive session, whose output stays in its own console.</param>
    public static ClaudeRun Start(
        IProcessApi processes,
        IFileSystem fileSystem,
        IClock clock,
        ProcessLaunch launch,
        ClaudeLaunchMode mode,
        string? runDirectory)
    {
        var run = new ClaudeRun(mode, clock, fileSystem, mode == ClaudeLaunchMode.Autonomous ? runDirectory : null);
        try
        {
            run._process = processes.Start(launch, mode == ClaudeLaunchMode.Autonomous ? run : null);
        }
        catch
        {
            run.Dispose();
            throw;
        }

        _ = run.WatchExitAsync(run._process);
        return run;
    }

    public ClaudeRunSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ClaudeRunSnapshot(
                ProcessId,
                Mode,
                StartedAt,
                _exitedAt,
                _exitCode,
                _result,
                _lastRetry,
                _result?.PermissionDenials ?? _permissionDenials,
                _lastErrorLine,
                _interruptRequested,
                _claudeSessionId,
                _recordingError,
                _usageLimit);
        }
    }

    /// <summary>
    /// Asks the process to stop with Ctrl+C and kills its whole tree if it is
    /// still running after <paramref name="gracePeriod"/>.
    /// </summary>
    public async Task<InterruptOutcome> InterruptAsync(TimeSpan gracePeriod)
    {
        IManagedProcess process = _process ?? throw new InvalidOperationException("The run has not started.");
        lock (_gate)
        {
            _interruptRequested = true;
        }

        if (process.HasExited)
        {
            return InterruptOutcome.AlreadyExited;
        }

        if (process.TrySendCtrlC())
        {
            using var grace = new CancellationTokenSource(gracePeriod);
            try
            {
                await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                return InterruptOutcome.StoppedCleanly;
            }
            catch (OperationCanceledException)
            {
                // The grace period ran out; the tree is killed below.
            }
        }

        process.KillTree();
        return InterruptOutcome.Killed;
    }

    public void OnOutputLine(string line)
    {
        ClaudeStreamEvent? streamEvent = StreamJsonParser.Parse(line);
        lock (_gate)
        {
            Record(ref _output, line);
            switch (streamEvent)
            {
                case ApiRetryEvent retry:
                    _lastRetry = retry;
                    break;
                case PermissionDeniedEvent:
                    _permissionDenials++;
                    _lastRetry = null;
                    break;
                case ResultEvent result:
                    _result = result;
                    _lastRetry = null;
                    break;
                case SessionInitEvent init:
                    _claudeSessionId = init.SessionId;
                    break;
                case UsageLimitEvent limit:
                    _usageLimit = limit;
                    _lastRetry = null;
                    break;
                case ActivityEvent:
                    _lastRetry = null;
                    break;
            }
        }

        if (streamEvent is not null and not ActivityEvent)
        {
            EventReceived?.Invoke(this, streamEvent);
        }
    }

    public void OnErrorLine(string line)
    {
        lock (_gate)
        {
            Record(ref _errors, line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                _lastErrorLine = line.Trim();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _output?.Dispose();
            _errors?.Dispose();
            _output = null;
            _errors = null;
        }

        _process?.Dispose();
    }

    // Output can still arrive after disposal, from a background command that
    // inherited the pipe, and a full disk can make a write fail. Neither may
    // throw on the thread-pool thread that delivers the line, so recording
    // stops and the reason is kept for the journal.
    private void Record(ref Stream? stream, string line)
    {
        if (_disposed || stream is null)
        {
            return;
        }

        try
        {
            stream.Write(Utf8NoBom.GetBytes(line + Environment.NewLine));
        }
        catch (IOException exception)
        {
            _recordingError ??= exception.Message;
            stream.Dispose();
            stream = null;
        }
    }

    private async Task WatchExitAsync(IManagedProcess process)
    {
        int exitCode = await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_gate)
        {
            _exitCode = exitCode;
            _exitedAt = _clock.Now;
        }

        Exited?.Invoke(this, EventArgs.Empty);
    }
}
