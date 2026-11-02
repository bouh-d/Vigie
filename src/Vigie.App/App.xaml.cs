using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Vigie.App.Dialogs;
using Vigie.App.Resources;
using Vigie.App.Tray;
using Vigie.App.ViewModels;
using Vigie.Core.Logging;
using Vigie.Core.Power;
using Vigie.Core.Session;

namespace Vigie.App;

public partial class App : Application
{
    private static readonly TimeSpan RunRetention = TimeSpan.FromDays(14);

    private AppServices? _services;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private ActivationListener? _activation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _services = new AppServices();
        RestoreLeftoverAtStartup(_services);
        PruneOldRuns(_services);

        _viewModel = new MainViewModel(_services, Dispatcher);
        _window = new MainWindow { DataContext = _viewModel };
        _tray = new TrayIcon(_viewModel, _window);

        _activation = new ActivationListener(() => Dispatcher.BeginInvoke(() => _window!.RestoreFromTray()));
        _activation.Start();

        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.ShutDown(StopReason.ApplicationClosing);
        _activation?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Restore immediately, before Windows closes the process, and never put
        // up a dialog that could hold up the shutdown.
        _viewModel?.ShutDown(StopReason.WindowsSessionEnding);
        base.OnSessionEnding(e);
    }

    private void RestoreLeftoverAtStartup(AppServices services)
    {
        LeftoverResult result = services.Leftover.RestoreIfOrphaned();
        switch (result.Status)
        {
            case LeftoverStatus.Restored:
                MessageBox.Show(Strings.Dialog_Restored, Strings.Dialog_RestoredTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case LeftoverStatus.Unreadable:
                JournalUnreadableDialog.Show(result.RawContent ?? string.Empty);
                break;
        }
    }

    private static void PruneOldRuns(AppServices services)
    {
        try
        {
            services.Storage.PruneRuns(RunRetention, services.Clock.Now);
        }
        catch (IOException)
        {
            // Cleaning up old recordings is best-effort and never blocks start-up.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RestoreOnCrash(e.Exception);

        // Let the process terminate after the settings are back in place rather
        // than carry on in an unknown state.
        e.Handled = false;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => RestoreOnCrash(e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        RestoreOnCrash(e.Exception);
        e.SetObserved();
    }

    private void RestoreOnCrash(Exception? exception)
    {
        try
        {
            _services?.Log.Error($"Erreur interne : {exception?.Message}");
            _viewModel?.ShutDown(StopReason.UnhandledException);
        }
        catch (Exception)
        {
            // Nothing more can be done from a failing exception handler.
        }
    }

    private sealed class ActivationListener(Action onActivated) : IDisposable
    {
        private readonly EventWaitHandle _event = new(initialState: false, EventResetMode.AutoReset, Program.ActivationEventName);
        private readonly CancellationTokenSource _cancellation = new();
        private RegisteredWaitHandle? _registration;

        public void Start()
            => _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onActivated(), null, Timeout.Infinite, executeOnlyOnce: false);

        public void Dispose()
        {
            _cancellation.Cancel();
            _registration?.Unregister(null);
            _event.Dispose();
            _cancellation.Dispose();
        }
    }
}
