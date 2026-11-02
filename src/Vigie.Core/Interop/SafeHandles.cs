using Microsoft.Win32.SafeHandles;

namespace Vigie.Core.Interop;

internal sealed class SafePowerRequestHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePowerRequestHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}

internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeKernelHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
