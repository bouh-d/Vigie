using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Resources;
using Vigie.App.Resources;
using Vigie.App.ViewModels;
using Forms = System.Windows.Forms;

namespace Vigie.App.Tray;

/// <summary>
/// The notification-area icon: a tooltip with the current state and a short
/// menu. It is the only part of the interface that uses Windows Forms.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIcon(MainViewModel viewModel, MainWindow window)
    {
        _viewModel = viewModel;
        _window = window;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = viewModel.TrayTooltip,
            ContextMenuStrip = BuildMenu(),
        };
        _notifyIcon.DoubleClick += (_, _) => _window.RestoreFromTray();

        _viewModel.PropertyChanged += OnViewModelChanged;
        _viewModel.NotificationRequested += OnNotificationRequested;
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel.NotificationRequested -= OnNotificationRequested;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private static Icon LoadIcon()
    {
        StreamResourceInfo? resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/vigie.ico"));
        if (resource is null)
        {
            return SystemIcons.Application;
        }

        using Stream stream = resource.Stream;
        return new Icon(stream, new System.Drawing.Size(16, 16));
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.Tray_Show, image: null, (_, _) => _window.RestoreFromTray());
        menu.Items.Add(Strings.Tray_StopSession, image: null, (_, _) => _viewModel.StopSessionCommand.Execute(null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Strings.Tray_Quit, image: null, (_, _) => _viewModel.QuitCommand.Execute(null));
        return menu;
    }

    private void OnNotificationRequested(object? sender, string message)
        => _notifyIcon.ShowBalloonTip(10_000, Strings.AppTitle, message, Forms.ToolTipIcon.Warning);

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.TrayTooltip))
        {
            _notifyIcon.Text = _viewModel.TrayTooltip;
        }
    }
}
