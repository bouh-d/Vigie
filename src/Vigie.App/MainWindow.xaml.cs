using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
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
