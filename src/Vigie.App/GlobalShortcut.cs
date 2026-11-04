using System.Windows.Interop;
using Vigie.App.Interop;
using Vigie.Core.Session;

namespace Vigie.App;

/// <summary>
/// The activity lock's shortcut, registered with Windows on the main window for
/// the length of a session. Windows delivers it whichever application has the
/// focus and keeps it from the others meanwhile; nothing else about the
/// keyboard reaches Vigie.
/// </summary>
public sealed class GlobalShortcut : IDisposable
{
    private const int Id = 1;

    private readonly HwndSource _source;
    private bool _registered;

    public GlobalShortcut(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>False when another application already holds the combination.</summary>
    public bool TryRegister(Shortcut shortcut)
    {
        Unregister();
        _registered = WindowNative.RegisterHotKey(_source.Handle, Id, (uint)shortcut.Modifiers | WindowNative.ModNoRepeat, (uint)shortcut.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            WindowNative.UnregisterHotKey(_source.Handle, Id);
            _registered = false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WindowNative.WmHotKey && (int)wParam == Id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return nint.Zero;
    }
}
