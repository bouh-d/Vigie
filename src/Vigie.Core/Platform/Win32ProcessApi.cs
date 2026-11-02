using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Vigie.Core.Interop;

namespace Vigie.Core.Platform;

public sealed class Win32ProcessApi : IProcessApi
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Attaching to a child's console and starting processes both touch
    // process-wide console state, so they never run at the same time.
    private static readonly Lock ConsoleLock = new();
    private static bool _ctrlHandlerInstalled;

    public IReadOnlyList<ProcessEntry> FindProcesses(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        var entries = new List<ProcessEntry>(processes.Length);
        foreach (Process process in processes)
        {
            using (process)
            {
                int processId = process.Id;
                using SafeProcessHandle handle = OpenLimited(processId);
                entries.Add(new ProcessEntry(processId, QueryImagePath(handle), QueryStartTime(handle)));
            }
        }

        return entries;
    }

    public IReadOnlyList<CommandLineProcessEntry> FindNodeProcesses()
    {
        try
        {
            return QueryNodeProcesses();
        }
        catch (Exception exception) when (exception is ManagementException or COMException)
        {
            throw new InvalidOperationException("The process list could not be read from WMI.", exception);
        }
    }

    private static List<CommandLineProcessEntry> QueryNodeProcesses()
    {
        var entries = new List<CommandLineProcessEntry>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, ExecutablePath, CommandLine, CreationDate FROM Win32_Process WHERE Name = 'node.exe'");
        using ManagementObjectCollection results = searcher.Get();

        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                int processId = Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture);
                DateTimeOffset? started = item["CreationDate"] is string creation
                    ? new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(creation)).ToUniversalTime()
                    : null;

                entries.Add(new CommandLineProcessEntry(
                    processId,
                    item["ExecutablePath"] as string,
                    item["CommandLine"] as string,
                    started));
            }
        }

        return entries;
    }

    public DateTimeOffset? GetStartTimeUtc(int processId)
    {
        using SafeProcessHandle handle = OpenLimited(processId);
        if (handle.IsInvalid || NativeMethods.WaitForSingleObject(handle, 0) != NativeConstants.WaitTimeout)
        {
            return null;
        }

        return QueryStartTime(handle);
    }

    public IObservedProcess? Observe(int processId)
    {
        SafeProcessHandle handle = OpenLimited(processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new ObservedProcess(handle);
    }

    public IManagedProcess Start(ProcessLaunch launch, IProcessOutputSink? outputSink)
    {
        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = launch.WorkingDirectory,
            CreateNoWindow = outputSink is not null,
        };

        foreach (string argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (outputSink is not null)
        {
            // Print mode reads standard input when it is not a terminal, so it
            // gets an empty, already closed stream rather than an inherited one.
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Utf8NoBom;
            startInfo.StandardErrorEncoding = Utf8NoBom;
        }

        Process process = StartLocked(startInfo);
        var managed = new ManagedProcess(process);

        if (outputSink is not null)
        {
            process.StandardInput.Close();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    outputSink.OnOutputLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    outputSink.OnErrorLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return managed;
    }

    public void StartDetached(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = StartLocked(startInfo);
    }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = StartLocked(startInfo);
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            return new ProcessRunResult(-1, string.Empty, string.Empty, TimedOut: true);
        }

        return new ProcessRunResult(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false),
            TimedOut: false);
    }

    private static Process StartLocked(ProcessStartInfo startInfo)
    {
        lock (ConsoleLock)
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        }
    }

    private static SafeProcessHandle OpenLimited(int processId)
        => NativeMethods.OpenProcess(
            NativeConstants.Synchronize | NativeConstants.ProcessQueryLimitedInformation,
            inheritHandle: false,
            (uint)processId);

    private static string? QueryImagePath(SafeProcessHandle handle)
    {
        if (handle.IsInvalid)
        {
            return null;
        }

        char[] buffer = new char[1024];
        uint size = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageName(handle, 0, ref buffer[0], ref size)
            ? new string(buffer, 0, (int)size)
            : null;
    }

    private static DateTimeOffset? QueryStartTime(SafeProcessHandle handle)
    {
        if (handle.IsInvalid
            || !NativeMethods.GetProcessTimes(handle, out long creation, out _, out _, out _))
        {
            return null;
        }

        return new DateTimeOffset(DateTime.FromFileTimeUtc(creation));
    }

    private static unsafe void EnsureCtrlHandler()
    {
        if (_ctrlHandlerInstalled)
        {
            return;
        }

        // A handler that reports every console event as handled keeps Vigie
        // alive while it is briefly attached to a child's console. Unlike
        // SetConsoleCtrlHandler(NULL, TRUE), it is not inherited by processes
        // started afterwards, so they still respond to Ctrl+C.
        NativeMethods.SetConsoleCtrlHandler(&SwallowConsoleEvent, add: true);
        _ctrlHandlerInstalled = true;
    }

    [UnmanagedCallersOnly]
    private static int SwallowConsoleEvent(uint ctrlType) => 1;

    private sealed class ObservedProcess(SafeProcessHandle handle) : IObservedProcess
    {
        private readonly ProcessWaitHandle _exitWaitHandle = new(handle);

        public WaitHandle ExitWaitHandle => _exitWaitHandle;

        public bool HasExited => NativeMethods.WaitForSingleObject(handle, 0) == NativeConstants.WaitObject0;

        public int? ExitCode
            => HasExited && NativeMethods.GetExitCodeProcess(handle, out uint code)
                ? unchecked((int)code)
                : null;

        public void Dispose()
        {
            _exitWaitHandle.Dispose();
            handle.Dispose();
        }
    }

    // Lets a process handle take part in WaitHandle.WaitAny without
    // transferring ownership of the handle.
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle handle)
            => SafeWaitHandle = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: false);
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ManagedProcess(Process process)
        {
            _process = process;
            ProcessId = process.Id;

            // Exit is taken from the process handle, not from the end of the
            // redirected streams: a background command that inherited the pipe
            // can keep it open long after the process itself has exited.
            process.Exited += (_, _) => _exited.TrySetResult(process.ExitCode);
            process.EnableRaisingEvents = true;
            if (process.HasExited)
            {
                _exited.TrySetResult(process.ExitCode);
            }
        }

        public int ProcessId { get; }

        public bool HasExited => _process.HasExited;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
            => _exited.Task.WaitAsync(cancellationToken);

        public bool TrySendCtrlC()
        {
            if (_process.HasExited)
            {
                return false;
            }

            lock (ConsoleLock)
            {
                EnsureCtrlHandler();
                NativeMethods.FreeConsole();
                if (!NativeMethods.AttachConsole((uint)ProcessId))
                {
                    return false;
                }

                try
                {
                    return NativeMethods.GenerateConsoleCtrlEvent(NativeConstants.CtrlCEvent, 0);
                }
                finally
                {
                    NativeMethods.FreeConsole();
                }
            }
        }

        public void KillTree()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (_process.HasExited)
            {
                // The process ended between the request and the kill.
            }
        }

        public void Dispose() => _process.Dispose();
    }
}
