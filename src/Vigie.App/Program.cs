using System.Globalization;
using System.Threading;
using Vigie.Core.Logging;
using Vigie.Core.Power;
using Vigie.Core.Session;

namespace Vigie.App;

public static class Program
{
    internal const string SingleInstanceMutexName = @"Local\Vigie.SingleInstance";
    internal const string ActivationEventName = @"Local\Vigie.Activate";

    [STAThread]
    public static int Main(string[] args)
    {
        // The restore and watchdog modes run before any window: they must do
        // their work and exit even when the interface cannot start.
        if (args is [CrashRecovery.WatchdogSwitch, ..])
        {
            return RunWatchdog(args);
        }

        if (args is [CrashRecovery.RestoreIfDirtySwitch])
        {
            return RunRestore(force: false);
        }

        if (args is ["--restore"])
        {
            return RunRestore(force: true);
        }

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunWatchdog(string[] args)
    {
        if (args.Length != 3
            || !int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ownerPid)
            || !DateTimeOffset.TryParse(args[2], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset ownerStart))
        {
            return 1;
        }

        using var services = new AppServices();
        Journal(services, Watchdog.WaitAndRestore(ownerPid, ownerStart, services.Processes, services.Leftover));
        return 0;
    }

    private static int RunRestore(bool force)
    {
        using var services = new AppServices();
        LeftoverResult result = force ? services.Leftover.RestoreNow() : services.Leftover.RestoreIfOrphaned();
        Journal(services, result);
        return result.Status == LeftoverStatus.Unreadable ? 2 : 0;
    }

    // These modes have no window, so what they did goes to the log file.
    private static void Journal(AppServices services, LeftoverResult? result)
    {
        switch (result?.Status)
        {
            case LeftoverStatus.Restored:
                services.Log.Warning(SessionNarration.LeftoverRestored());
                foreach (string line in SessionNarration.Restored(result.Report!))
                {
                    services.Log.Info(line);
                }

                break;
            case LeftoverStatus.Unreadable:
                services.Log.Error(SessionNarration.JournalUnreadable());
                break;
        }
    }

    private static void SignalExistingInstance()
    {
        if (EventWaitHandle.TryOpenExisting(ActivationEventName, out EventWaitHandle? activation))
        {
            using (activation)
            {
                activation.Set();
            }
        }
    }
}
