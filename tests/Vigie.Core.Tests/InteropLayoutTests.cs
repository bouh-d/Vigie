using System.Runtime.InteropServices;
using Vigie.Core.Interop;
using Xunit;

namespace Vigie.Core.Tests;

/// <summary>
/// The marshalled sizes must match the native ones, or Windows rejects the
/// buffer and Modern Standby detection silently reports false.
/// </summary>
public class InteropLayoutTests
{
    [Fact]
    public void SystemPowerCapabilitiesMatchesTheNativeSize()
        => Assert.Equal(76, Marshal.SizeOf<SystemPowerCapabilities>());

    [Fact]
    public void AoAcSitsAtItsNativeOffset()
        => Assert.Equal(20, (int)Marshal.OffsetOf<SystemPowerCapabilities>(nameof(SystemPowerCapabilities.AoAc)));

    [Fact]
    public void SystemPowerStatusMatchesTheNativeSize()
        => Assert.Equal(12, Marshal.SizeOf<SystemPowerStatus>());

    [Fact]
    public void LastInputInfoMatchesTheNativeSize()
        => Assert.Equal(8, Marshal.SizeOf<LastInputInfo>());
}
