using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Vigie.Core.Interop;

namespace Vigie.Core.Power;

public sealed class Win32PowerApi : IPowerApi
{
    public IPowerRequest CreateSystemRequest(string reason, bool keepDisplayOn)
        => Win32PowerRequest.Create(reason, keepDisplayOn);

    public Guid GetActiveSchemeGuid()
    {
        uint status = NativeMethods.PowerGetActiveScheme(nint.Zero, out nint buffer);
        if (status != NativeConstants.ErrorSuccess || buffer == nint.Zero)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerGetActiveScheme), status);
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(buffer);
        }
        finally
        {
            NativeMethods.LocalFree(buffer);
        }
    }

    public PowerValue ReadValue(Guid scheme, Guid subgroup, Guid setting)
    {
        uint acStatus = NativeMethods.PowerReadACValueIndex(nint.Zero, scheme, subgroup, setting, out uint ac);
        if (acStatus != NativeConstants.ErrorSuccess)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerReadACValueIndex), acStatus);
        }

        uint dcStatus = NativeMethods.PowerReadDCValueIndex(nint.Zero, scheme, subgroup, setting, out uint dc);
        if (dcStatus != NativeConstants.ErrorSuccess)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerReadDCValueIndex), dcStatus);
        }

        return new PowerValue(ac, dc);
    }

    public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        uint status = NativeMethods.PowerWriteACValueIndex(nint.Zero, scheme, subgroup, setting, value);
        if (status != NativeConstants.ErrorSuccess)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerWriteACValueIndex), status);
        }
    }

    public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        uint status = NativeMethods.PowerWriteDCValueIndex(nint.Zero, scheme, subgroup, setting, value);
        if (status != NativeConstants.ErrorSuccess)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerWriteDCValueIndex), status);
        }
    }

    public void ActivateScheme(Guid scheme)
    {
        uint status = NativeMethods.PowerSetActiveScheme(nint.Zero, scheme);
        if (status != NativeConstants.ErrorSuccess)
        {
            throw new PowerApiException(nameof(NativeMethods.PowerSetActiveScheme), status);
        }
    }

    public bool IsModernStandbyAvailable()
    {
        int size = Marshal.SizeOf<SystemPowerCapabilities>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = NativeMethods.CallNtPowerInformation(
                (int)PowerInformationLevel.SystemPowerCapabilities,
                nint.Zero,
                0,
                buffer,
                (uint)size);

            if (status != 0)
            {
                return false;
            }

            SystemPowerCapabilities capabilities = Marshal.PtrToStructure<SystemPowerCapabilities>(buffer);
            return capabilities.AoAc;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public PowerStatus GetPowerStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out SystemPowerStatus status))
        {
            return new PowerStatus(PowerSource.Unknown, null, BatterySaverOn: false, BatteryCritical: false);
        }

        return PowerStatus.FromRaw(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent, status.SystemStatusFlag);
    }

    public void LockWorkstation()
    {
        if (!NativeMethods.LockWorkStation())
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public bool TrySuspend()
    {
        EnableShutdownPrivilege();
        return NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
    }

    public TimeSpan GetTimeSinceLastInput()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        uint elapsed = unchecked((uint)Environment.TickCount - info.TickCount);
        return TimeSpan.FromMilliseconds(elapsed);
    }

    private static void EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeConstants.TokenAdjustPrivileges | NativeConstants.TokenQuery,
                out SafeKernelHandle token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeConstants.SeShutdownName, out Luid luid))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeConstants.SePrivilegeEnabled,
            };

            if (!NativeMethods.AdjustTokenPrivileges(token, disableAllPrivileges: false, privileges, 0, nint.Zero, nint.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    private sealed class Win32PowerRequest : IPowerRequest
    {
        private readonly string _reason;
        private readonly bool _keepDisplayOn;
        private SafePowerRequestHandle? _handle;
        private bool _disposed;

        private Win32PowerRequest(string reason, bool keepDisplayOn)
        {
            _reason = reason;
            _keepDisplayOn = keepDisplayOn;
        }

        public bool UsesFallback => _handle is null;

        public static Win32PowerRequest Create(string reason, bool keepDisplayOn)
        {
            var request = new Win32PowerRequest(reason, keepDisplayOn);
            request.Assert();
            return request;
        }

        public void Reassert()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_handle is { IsInvalid: false } handle)
            {
                Apply(handle);
                return;
            }

            Assert();
        }

        private void Assert()
        {
            nint reasonPointer = Marshal.StringToHGlobalUni(_reason);
            try
            {
                var context = new ReasonContext
                {
                    Version = NativeConstants.PowerRequestContextVersion,
                    Flags = NativeConstants.PowerRequestContextSimpleString,
                    SimpleReasonString = reasonPointer,
                };

                SafePowerRequestHandle handle = NativeMethods.PowerCreateRequest(context);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    ApplyFallback();
                    return;
                }

                _handle = handle;
                Apply(handle);
            }
            finally
            {
                Marshal.FreeHGlobal(reasonPointer);
            }
        }

        private void Apply(SafePowerRequestHandle handle)
        {
            if (!NativeMethods.PowerSetRequest(handle, PowerRequestType.SystemRequired))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (_keepDisplayOn)
            {
                NativeMethods.PowerSetRequest(handle, PowerRequestType.DisplayRequired);
            }
        }

        private void ApplyFallback()
        {
            ExecutionState state = ExecutionState.Continuous | ExecutionState.SystemRequired;
            if (_keepDisplayOn)
            {
                state |= ExecutionState.DisplayRequired;
            }

            NativeMethods.SetThreadExecutionState(state);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_handle is { IsInvalid: false } handle)
            {
                NativeMethods.PowerClearRequest(handle, PowerRequestType.SystemRequired);
                if (_keepDisplayOn)
                {
                    NativeMethods.PowerClearRequest(handle, PowerRequestType.DisplayRequired);
                }

                _handle.Dispose();
                _handle = null;
            }
            else
            {
                NativeMethods.SetThreadExecutionState(ExecutionState.Continuous);
            }
        }
    }
}
