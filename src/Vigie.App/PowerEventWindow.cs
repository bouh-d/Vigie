using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vigie.App.Interop;
using Vigie.Core.Power;

namespace Vigie.App;

public sealed record PowerBroadcast(Guid Setting, int Value);

/// <summary>
/// Hooks the main window's message loop and turns power broadcasts into events.
/// A top-level window is used on purpose: a message-only window does not
/// receive the broadcast messages.
/// </summary>
public sealed class PowerEventWindow : IDisposable
{
    private static readonly Guid[] WatchedSettings =
    [
        PowerKnownSettings.AcDcPowerSource,
        PowerKnownSettings.BatteryPercentageRemaining,
        PowerKnownSettings.LidSwitchStateChange,
        PowerKnownSettings.ConsoleDisplayState,
        PowerKnownSettings.ActivePowerScheme,
    ];

    private readonly HwndSource _source;
    private readonly List<nint> _registrations = [];

    public PowerEventWindow(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
        foreach (Guid setting in WatchedSettings)
        {
            nint registration = WindowNative.Register(source.Handle, setting);
            if (registration != nint.Zero)
            {
                _registrations.Add(registration);
            }
        }
    }

    public event EventHandler<PowerBroadcast>? SettingChanged;

    public event EventHandler? Suspending;

    public event EventHandler? Resumed;

    public void Dispose()
    {
        foreach (nint registration in _registrations)
        {
            WindowNative.UnregisterPowerSettingNotification(registration);
        }

        _registrations.Clear();
        _source.RemoveHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WindowNative.WmPowerBroadcast)
        {
            return nint.Zero;
        }

        switch ((int)wParam)
        {
            case WindowNative.PbtApmSuspend:
                Suspending?.Invoke(this, EventArgs.Empty);
                break;
            case WindowNative.PbtApmResumeSuspend:
            case WindowNative.PbtApmResumeAutomatic:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
            case WindowNative.PbtPowerSettingChange:
                RaiseSettingChange(lParam);
                break;
        }

        return nint.Zero;
    }

    private void RaiseSettingChange(nint lParam)
    {
        WindowNative.PowerBroadcastSetting setting = Marshal.PtrToStructure<WindowNative.PowerBroadcastSetting>(lParam);
        SettingChanged?.Invoke(this, new PowerBroadcast(setting.PowerSetting, setting.Data));
    }
}
