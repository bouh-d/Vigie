using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vigie.App.Tray;
using Vigie.App.ViewModels;
using Vigie.Core.ClaudeCode;
using Vigie.Core.Logging;
using Vigie.Core.Session;

namespace Vigie.App;

public partial class App : Application
{
    private static readonly TimeSpan RunRetention = TimeSpan.FromDays(14);
    private static readonly TimeSpan CrashRestoreTimeout = TimeSpan.FromSeconds(5);

    private AppServices? _services;
    private MainViewModel? _viewModel;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private ActivationListener? _activation;
    private Exception? _crash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ApplyContrastPalette();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;

        _services = new AppServices();
        _viewModel = new MainViewModel(_services, Dispatcher);
        _viewModel.HandleLeftoverJournal();
        PruneOldRuns(_services);

        _window = new MainWindow { DataContext = _viewModel };
        _tray = new TrayIcon(_viewModel, _window);

        _activation = new ActivationListener(() => Dispatcher.BeginInvoke(() => _window!.RestoreFromTray()));
        _activation.Start();

        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.ShutDown(StopReason.ApplicationClosing);
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
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

    private static void PruneOldRuns(AppServices services)
    {
        try
        {
            services.Storage.PruneRuns(RunRetention, services.Clock.Now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            services.Log.Warning(ClaudeNarration.OldRunsCleanupFailed(exception.Message));
        }
    }

    // A high-contrast theme replaces the accent and warning colours with the
    // theme's own, so status and banners stay readable.
    private void ApplyContrastPalette()
    {
        bool highContrast = SystemParameters.HighContrast;
        SetBrush("AccentBrush", highContrast ? SystemColors.HighlightColor : Color.FromRgb(0x25, 0x63, 0xEB));
        SetBrush("ActiveDotBrush", highContrast ? SystemColors.HighlightColor : Color.FromRgb(0x25, 0x63, 0xEB));
        SetBrush("ErrorDotBrush", highContrast ? SystemColors.WindowTextColor : Color.FromRgb(0xB4, 0x53, 0x09));
        SetBrush("IdleDotBrush", highContrast ? SystemColors.GrayTextColor : Color.FromRgb(0x9C, 0xA3, 0xAF));
        SetBrush("WarningForegroundBrush", highContrast ? SystemColors.WindowTextColor : Color.FromRgb(0x92, 0x40, 0x0E));
        SetBrush("WarningBackgroundBrush", highContrast ? SystemColors.WindowColor : Color.FromRgb(0xFE, 0xF3, 0xC7));
        SetBrush("WarningBorderBrush", highContrast ? SystemColors.WindowTextColor : Color.FromRgb(0xB4, 0x53, 0x09));
    }

    private void SetBrush(string key, Color color) => Resources[key] = new SolidColorBrush(color);

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Dispatcher.BeginInvoke(ApplyContrastPalette);
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

    // The session belongs to the UI thread, so a failure on another thread
    // hands the restore over to it. If the UI thread does not take it in time,
    // the watchdog restores once the process is gone.
    private void RestoreOnCrash(Exception? exception)
    {
        // An exception the dispatcher lets through reaches the domain handler
        // as well.
        if (exception is not null && ReferenceEquals(Interlocked.Exchange(ref _crash, exception), exception))
        {
            return;
        }

        void StopSession()
        {
            _services?.Log.Error($"Erreur interne : {exception?.GetBaseException().Message}");
            _viewModel?.ShutDown(StopReason.UnhandledException);
        }

        try
        {
            if (Dispatcher.CheckAccess())
            {
                StopSession();
            }
            else
            {
                Dispatcher.Invoke(StopSession, DispatcherPriority.Send, CancellationToken.None, CrashRestoreTimeout);
            }
        }
        catch (Exception)
        {
            // An exception handler that throws takes the safety net with it;
            // the watchdog and the next launch still restore.
        }
    }

    private sealed class ActivationListener(Action onActivated) : IDisposable
    {
        private readonly EventWaitHandle _event = new(initialState: false, EventResetMode.AutoReset, Program.ActivationEventName);
        private RegisteredWaitHandle? _registration;

        public void Start()
            => _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onActivated(), null, Timeout.Infinite, executeOnlyOnce: false);

        public void Dispose()
        {
            _registration?.Unregister(null);
            _event.Dispose();
        }
    }
}
