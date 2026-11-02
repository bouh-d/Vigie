using System.Globalization;
using Vigie.Core.Platform;

namespace Vigie.Core.Power;

/// <summary>
/// The safety nets that restore the journal when Vigie does not get to do it
/// itself: a watchdog process that outlives a crash, and a <c>RunOnce</c>
/// entry for the next sign-in after a power cut.
/// </summary>
public interface ICrashRecovery
{
    void Arm(SessionOwner owner);

    /// <param name="keepStartupEntry">True when the journal could not be fully
    /// restored, so the next sign-in tries again.</param>
    void Disarm(bool keepStartupEntry);
}

public sealed class CrashRecovery(IRegistry registry, IProcessApi processes, IEnvironment environment) : ICrashRecovery
{
    public const string WatchdogSwitch = "--watchdog";
    public const string RestoreIfDirtySwitch = "--restore-if-dirty";

    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string RunOnceValueName = "VigieRestore";

    private EventWaitHandle? _sessionEnded;

    public void Arm(SessionOwner owner)
    {
        string executable = environment.CurrentProcessPath
            ?? throw new InvalidOperationException("The path of the running executable is unknown.");

        registry.WriteCurrentUserString(RunOnceKey, RunOnceValueName, $"\"{executable}\" {RestoreIfDirtySwitch}");

        // The event outlives a previous session while that session's watchdog
        // still holds it, in which case it is already signaled.
        _sessionEnded = new EventWaitHandle(
            initialState: false,
            EventResetMode.ManualReset,
            Watchdog.SessionEndedEventName(owner.ProcessId, owner.StartTimeUtc),
            out bool createdNew);
        if (!createdNew)
        {
            _sessionEnded.Reset();
        }

        processes.StartDetached(executable,
        [
            WatchdogSwitch,
            owner.ProcessId.ToString(CultureInfo.InvariantCulture),
            owner.StartTimeUtc.ToString("o", CultureInfo.InvariantCulture),
        ]);
    }

    public void Disarm(bool keepStartupEntry)
    {
        if (_sessionEnded is not null)
        {
            _sessionEnded.Set();
            _sessionEnded.Dispose();
            _sessionEnded = null;
        }

        if (!keepStartupEntry)
        {
            RemoveStartupEntry(registry);
        }
    }

    public static void RemoveStartupEntry(IRegistry registry)
        => registry.DeleteCurrentUserValue(RunOnceKey, RunOnceValueName);
}
