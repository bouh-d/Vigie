using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using Vigie.App.Dialogs;
using Vigie.App.ViewModels;

namespace Vigie.App;

public partial class MainWindow : Window
{
    private bool _quitting;

    public MainWindow()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => ViewModel?.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
        StateChanged += (_, _) => ViewModel?.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // The installation page opens in the default browser; Vigie itself makes
    // no network request.
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            ViewModel?.AttachWindow(source);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_quitting || ViewModel is not { IsSessionRunning: true } viewModel)
        {
            return;
        }

        switch (CloseSessionDialog.Ask(this, viewModel.IsClaudeRunning))
        {
            case CloseChoice.MinimizeToTray:
                e.Cancel = true;
                Hide();
                break;
            case CloseChoice.StopAndQuit:
                viewModel.StopForQuit();
                _quitting = true;
                break;
            default:
                e.Cancel = true;
                break;
        }
    }
}
