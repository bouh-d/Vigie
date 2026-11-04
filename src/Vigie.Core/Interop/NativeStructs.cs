using System.Runtime.InteropServices;

namespace Vigie.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ReasonContext
{
    public uint Version;
    public uint Flags;
    public nint SimpleReasonString;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LastInputInfo
{
    public uint Size;
    public uint TickCount;
}

// PROCESSENTRY32W. Only the identifiers are read; the image name is a fixed
// buffer so the structure stays blittable.
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProcessEntry32
{
    public uint Size;
    public uint Usage;
    public uint ProcessId;
    public nuint DefaultHeapId;
    public uint ModuleId;
    public uint Threads;
    public uint ParentProcessId;
    public int PriorityClassBase;
    public uint Flags;
    public fixed char ExeFile[260];
}

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TokenPrivileges
{
    public uint PrivilegeCount;
    public Luid Luid;
    public uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BatteryReportingScale
{
    public uint Granularity;
    public uint Capacity;
}

// Field order follows the SYSTEM_POWER_CAPABILITIES definition documented by
// Microsoft. Only AoAc is read; the surrounding fields exist so the structure
// marshals at the correct size and offset.
// https://learn.microsoft.com/windows/win32/power/system-power-capabilities
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerCapabilities
{
    [MarshalAs(UnmanagedType.I1)] public bool PowerButtonPresent;
    [MarshalAs(UnmanagedType.I1)] public bool SleepButtonPresent;
    [MarshalAs(UnmanagedType.I1)] public bool LidPresent;
    [MarshalAs(UnmanagedType.I1)] public bool SystemS1;
    [MarshalAs(UnmanagedType.I1)] public bool SystemS2;
    [MarshalAs(UnmanagedType.I1)] public bool SystemS3;
    [MarshalAs(UnmanagedType.I1)] public bool SystemS4;
    [MarshalAs(UnmanagedType.I1)] public bool SystemS5;
    [MarshalAs(UnmanagedType.I1)] public bool HiberFilePresent;
    [MarshalAs(UnmanagedType.I1)] public bool FullWake;
    [MarshalAs(UnmanagedType.I1)] public bool VideoDimPresent;
    [MarshalAs(UnmanagedType.I1)] public bool ApmPresent;
    [MarshalAs(UnmanagedType.I1)] public bool UpsPresent;
    [MarshalAs(UnmanagedType.I1)] public bool ThermalControl;
    [MarshalAs(UnmanagedType.I1)] public bool ProcessorThrottle;
    public byte ProcessorMinThrottle;
    public byte ProcessorMaxThrottle;
    [MarshalAs(UnmanagedType.I1)] public bool FastSystemS4;
    [MarshalAs(UnmanagedType.I1)] public bool Hiberboot;
    [MarshalAs(UnmanagedType.I1)] public bool WakeAlarmPresent;
    [MarshalAs(UnmanagedType.I1)] public bool AoAc;
    [MarshalAs(UnmanagedType.I1)] public bool DiskSpinDown;
    public byte HiberFileType;
    [MarshalAs(UnmanagedType.I1)] public bool AoAcConnectivitySupported;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public byte[] Spare3;
    [MarshalAs(UnmanagedType.I1)] public bool SystemBatteriesPresent;
    [MarshalAs(UnmanagedType.I1)] public bool BatteriesAreShortTerm;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public BatteryReportingScale[] BatteryScale;
    public int AcOnLineWake;
    public int SoftLidWake;
    public int RtcWake;
    public int MinDeviceWakeState;
    public int DefaultLowLatencyWake;
}
