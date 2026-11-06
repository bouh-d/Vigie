using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using Vigie.App.Resources;
using Vigie.Core.Logging;
using Vigie.Core.Platform;
using Vigie.Core.Power;
using Vigie.Core.Session;
using Vigie.Core.Storage;

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

        if (StartOutsideRedirectingPackage(mutex) is int exitCode)
        {
            return exitCode;
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

    // A Vigie started from inside a packaged app, such as the Claude desktop
    // app, would keep its restore journal and its sign-in entry in that
    // package's storage, out of reach of a later Vigie and of Windows. Explorer
    // starts it again outside; if it is still redirected right after, or the
    // relaunch cannot be made, it says so and stops instead of looping.
    private static int? StartOutsideRedirectingPackage(Mutex singleInstance)
    {
        var fileSystem = new SystemFileSystem();
        string localAppData = new SystemEnvironment().LocalApplicationData;
        string? package = PackageRedirection.Detect(fileSystem, localAppData);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StartPlacement placement = PackageRedirection.Decide(package, PackageRedirection.ReadLastRelaunch(fileSystem, localAppData), now);
        if (placement == StartPlacement.Run)
        {
            return null;
        }

        // The instance Explorer starts must find the single-instance mutex free.
        singleInstance.ReleaseMutex();
        singleInstance.Close();

        if (placement == StartPlacement.Relaunch && Environment.ProcessPath is string path)
        {
            try
            {
                PackageRedirection.RecordRelaunch(fileSystem, now, localAppData);
                var explorer = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                {
                    UseShellExecute = false,
                };
                explorer.ArgumentList.Add(path);
                Process.Start(explorer)?.Dispose();
                return 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
            {
                // Without the marker a failed relaunch would loop, and without
                // Explorer there is no relaunch: both end in the message.
            }
        }

        MessageBox.Show(Strings.Format(Strings.Dialog_RedirectedStart, package), Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        return 1;
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
