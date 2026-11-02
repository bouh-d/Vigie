using System.Windows;
using System.Windows.Controls;
using Vigie.App.Resources;

namespace Vigie.App.Dialogs;

public enum CloseChoice
{
    Cancel,
    MinimizeToTray,
    StopAndQuit,
}

/// <summary>
/// The choice offered when the window is closed during a session. Built in code
/// rather than XAML because it is a short, fixed dialog.
/// </summary>
public static class CloseSessionDialog
{
    public static CloseChoice Ask(Window owner, bool claudeRunning)
    {
        var result = CloseChoice.Cancel;

        string message = Strings.Dialog_Close;
        if (claudeRunning)
        {
            message += "\n\n" + Strings.Dialog_Close_ClaudeRunning;
        }

        var dialog = new Window
        {
            Title = Strings.Dialog_CloseTitle,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var buttons = new StackPanel { Orientation = Orientation.Vertical };
        buttons.Children.Add(MakeButton(Strings.Dialog_Minimize, () => { result = CloseChoice.MinimizeToTray; dialog.Close(); }, isDefault: true));
        buttons.Children.Add(MakeButton(Strings.Dialog_StopQuit, () => { result = CloseChoice.StopAndQuit; dialog.Close(); }));
        buttons.Children.Add(MakeButton(Strings.Dialog_Cancel, () => { result = CloseChoice.Cancel; dialog.Close(); }, isCancel: true));

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Margin = new Thickness(0, 0, 0, 12) },
                buttons,
            },
        };

        dialog.ShowDialog();
        return result;
    }

    private static Button MakeButton(string text, Action onClick, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            IsDefault = isDefault,
            IsCancel = isCancel,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}

/// <summary>Shows the raw content of a restore journal that could not be read,
/// with the manual recovery pointer, and changes nothing automatically.</summary>
public static class JournalUnreadableDialog
{
    public static void Show(string rawContent)
    {
        var dialog = new Window
        {
            Title = Strings.Dialog_JournalUnreadableTitle,
            Width = 560,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        var layout = new DockPanel { Margin = new Thickness(16) };

        var message = new TextBlock
        {
            Text = Strings.Dialog_JournalUnreadable,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        DockPanel.SetDock(message, Dock.Top);

        var ok = new Button { Content = "OK", Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsDefault = true };
        ok.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(ok, Dock.Bottom);

        var raw = new TextBox
        {
            Text = rawContent,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };

        layout.Children.Add(message);
        layout.Children.Add(ok);
        layout.Children.Add(raw);
        dialog.Content = layout;
        dialog.ShowDialog();
    }
}
