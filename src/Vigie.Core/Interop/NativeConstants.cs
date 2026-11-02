namespace Vigie.Core.Interop;

internal enum PowerRequestType
{
    DisplayRequired = 0,
    SystemRequired = 1,
    AwayModeRequired = 2,
    ExecutionRequired = 3,
}

[Flags]
internal enum ExecutionState : uint
{
    SystemRequired = 0x00000001,
    DisplayRequired = 0x00000002,
    Continuous = 0x80000000,
}

internal enum PowerInformationLevel
{
    SystemPowerCapabilities = 4,
}

internal static class NativeConstants
{
    public const uint ErrorSuccess = 0;

    public const uint PowerRequestContextVersion = 0;
    public const uint PowerRequestContextSimpleString = 0x00000001;

    public const uint DeviceNotifyWindowHandle = 0x00000000;

    // WM_POWERBROADCAST and its wParam event codes.
    public const int WmPowerBroadcast = 0x0218;
    public const int PbtApmSuspend = 0x0004;
    public const int PbtApmResumeSuspend = 0x0007;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int PbtPowerSettingChange = 0x8013;

    // Console control events for a graceful Ctrl+C to a child console process.
    public const uint CtrlCEvent = 0;

    // Process access rights used to observe an externally started process.
    public const uint Synchronize = 0x00100000;
    public const uint ProcessQueryLimitedInformation = 0x00001000;

    public const uint StillActive = 259;

    public const uint WaitObject0 = 0;
    public const uint WaitTimeout = 0x00000102;
    public const uint WaitFailed = 0xFFFFFFFF;
    public const uint Infinite = 0xFFFFFFFF;

    // AdjustTokenPrivileges / shutdown privilege.
    public const uint TokenAdjustPrivileges = 0x0020;
    public const uint TokenQuery = 0x0008;
    public const uint SePrivilegeEnabled = 0x00000002;
    public const string SeShutdownName = "SeShutdownPrivilege";
}
