using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Vigie.Core.Interop;

internal static partial class NativeMethods
{
    private const string PowrProf = "powrprof.dll";
    private const string Kernel32 = "kernel32.dll";
    private const string User32 = "user32.dll";
    private const string AdvApi32 = "advapi32.dll";

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(Kernel32)]
    public static partial ExecutionState SetThreadExecutionState(ExecutionState state);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    [LibraryImport(Kernel32)]
    public static partial nint LocalFree(nint handle);

    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial uint WaitForSingleObject(SafeHandle handle, uint milliseconds);

    [LibraryImport(Kernel32)]
    public static partial nint GetCurrentProcess();

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool SetConsoleCtrlHandler(delegate* unmanaged<uint, int> handlerRoutine, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport(Kernel32, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, ref char exeName, ref uint size);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(SafeProcessHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    // The power request functions live in kernel32, unlike the power scheme
    // functions below.
    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial SafePowerRequestHandle PowerCreateRequest(in ReasonContext context);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerSetRequest(SafePowerRequestHandle request, PowerRequestType requestType);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PowerClearRequest(SafePowerRequestHandle request, PowerRequestType requestType);

    [LibraryImport(PowrProf)]
    public static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [LibraryImport(PowrProf)]
    public static partial uint PowerReadACValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, out uint acValueIndex);

    [LibraryImport(PowrProf)]
    public static partial uint PowerReadDCValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, out uint dcValueIndex);

    [LibraryImport(PowrProf)]
    public static partial uint PowerWriteACValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, uint acValueIndex);

    [LibraryImport(PowrProf)]
    public static partial uint PowerWriteDCValueIndex(nint rootPowerKey, in Guid schemeGuid, in Guid subGroupGuid, in Guid settingGuid, uint dcValueIndex);

    [LibraryImport(PowrProf)]
    public static partial uint PowerSetActiveScheme(nint userRootPowerKey, in Guid schemeGuid);

    [LibraryImport(PowrProf)]
    public static partial int CallNtPowerInformation(int informationLevel, nint inputBuffer, uint inputBufferSize, nint outputBuffer, uint outputBufferSize);

    [LibraryImport(PowrProf)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LastInputInfo lastInputInfo);

    [LibraryImport(User32)]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport(AdvApi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out SafeKernelHandle tokenHandle);

    [LibraryImport(AdvApi32, SetLastError = true, EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [LibraryImport(AdvApi32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustTokenPrivileges(SafeKernelHandle tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges, in TokenPrivileges newState, uint bufferLength, nint previousState, nint returnLength);
}
