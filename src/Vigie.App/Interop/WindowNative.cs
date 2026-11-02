using System.Runtime.InteropServices;

namespace Vigie.App.Interop;

/// <summary>
/// The window-message plumbing that belongs to the interface rather than the
/// core: registering for power-setting notifications on the main window and
/// reading the broadcast payload.
/// </summary>
internal static class WindowNative
{
    public const int WmPowerBroadcast = 0x0218;
    public const int PbtApmSuspend = 0x0004;
    public const int PbtApmResumeSuspend = 0x0007;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int PbtPowerSettingChange = 0x8013;

    private const int DeviceNotifyWindowHandle = 0x00000000;

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
