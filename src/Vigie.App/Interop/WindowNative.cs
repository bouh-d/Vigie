using System.Runtime.InteropServices;

namespace Vigie.App.Interop;

/// <summary>
/// The window-message plumbing that belongs to the interface rather than the
/// core: registering for power-setting notifications on the main window,
/// reading the broadcast payload, and the activity lock's shortcut.
/// </summary>
internal static class WindowNative
{
    public const int WmPowerBroadcast = 0x0218;
    public const int WmHotKey = 0x0312;
    public const int PbtApmSuspend = 0x0004;
    public const int PbtApmResumeSuspend = 0x0007;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int PbtPowerSettingChange = 0x8013;
    public const uint ModNoRepeat = 0x4000;

    private const int DeviceNotifyWindowHandle = 0x00000000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint RegisterPowerSettingNotification(nint recipient, in Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterPowerSettingNotification(nint handle);

    public static nint Register(nint window, Guid setting)
        => RegisterPowerSettingNotification(window, setting, DeviceNotifyWindowHandle);

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }
}
