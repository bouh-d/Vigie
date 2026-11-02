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
            return new PowerStatus(PowerSource.Unknown, null, BatterySaverOn: false);
        }

        return PowerStatus.FromRaw(status.AcLineStatus, status.BatteryLifePercent, status.SystemStatusFlag);
    }

    public bool TryLockWorkstation() => NativeMethods.LockWorkStation();

    public bool TrySuspend()
        => TryEnableShutdownPrivilege()
            && NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);

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

    // SetSuspendState needs the shutdown privilege, which a standard user's
    // token holds but does not enable by default.
    private static bool TryEnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeConstants.TokenAdjustPrivileges | NativeConstants.TokenQuery,
                out SafeKernelHandle token))
        {
            return false;
        }

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValue(null, NativeConstants.SeShutdownName, out Luid luid))
            {
                return false;
            }

            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeConstants.SePrivilegeEnabled,
            };

            return NativeMethods.AdjustTokenPrivileges(token, disableAllPrivileges: false, privileges, 0, nint.Zero, nint.Zero);
        }
    }

    private sealed class Win32PowerRequest : IPowerRequest
    {
        private readonly string _reason;
        private SafePowerRequestHandle? _handle;
        private bool _disposed;

        private Win32PowerRequest(string reason, bool displayRequired)
        {
            _reason = reason;
            DisplayRequired = displayRequired;
        }

        public bool UsesFallback => _handle is null;

        public bool DisplayRequired { get; private set; }

        public static Win32PowerRequest Create(string reason, bool displayRequired)
        {
            var request = new Win32PowerRequest(reason, displayRequired);
            request.Assert();
            return request;
        }

        public void Reassert()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Recreated rather than set again: after a user-initiated sleep the
            // old request may no longer count, and a fresh one always does.
            Release();
            Assert();
        }

        public void SetDisplayRequired(bool required)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (required == DisplayRequired)
            {
                return;
            }

            DisplayRequired = required;
            if (_handle is { IsInvalid: false } handle)
            {
                bool changed = required
                    ? NativeMethods.PowerSetRequest(handle, PowerRequestType.DisplayRequired)
                    : NativeMethods.PowerClearRequest(handle, PowerRequestType.DisplayRequired);
                if (!changed)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }
            else
            {
                ApplyFallback();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Release();
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
                if (!NativeMethods.PowerSetRequest(handle, PowerRequestType.SystemRequired))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                if (DisplayRequired && !NativeMethods.PowerSetRequest(handle, PowerRequestType.DisplayRequired))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(reasonPointer);
            }
        }

        // The fallback state belongs to the calling thread, which is why the
        // request is created and changed on the thread that runs the session.
        private void ApplyFallback()
        {
            ExecutionState state = ExecutionState.Continuous | ExecutionState.SystemRequired;
            if (DisplayRequired)
            {
                state |= ExecutionState.DisplayRequired;
            }

            NativeMethods.SetThreadExecutionState(state);
        }

        private void Release()
        {
            if (_handle is { IsInvalid: false } handle)
            {
                // Each set is paired with a clear before the handle is closed,
                // as the PowerSetRequest documentation describes.
                NativeMethods.PowerClearRequest(handle, PowerRequestType.SystemRequired);
                if (DisplayRequired)
                {
                    NativeMethods.PowerClearRequest(handle, PowerRequestType.DisplayRequired);
                }

                handle.Dispose();
                _handle = null;
            }
            else
            {
                NativeMethods.SetThreadExecutionState(ExecutionState.Continuous);
            }
        }
    }
}
