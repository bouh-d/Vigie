using Vigie.Core.Power;
using Xunit;

namespace Vigie.Core.Tests;

public class PowerStatusTests
{
    [Fact]
    public void OfflineLineStatusMeansOnBattery()
    {
        PowerStatus status = PowerStatus.FromRaw(acLineStatus: 0, batteryLifePercent: 64, systemStatusFlag: 0);

        Assert.True(status.OnBattery);
        Assert.Equal(64, status.BatteryPercent);
    }

    [Fact]
    public void UnknownChargeIsReportedAsNoPercentage()
        => Assert.Null(PowerStatus.FromRaw(acLineStatus: 1, batteryLifePercent: 255, systemStatusFlag: 0).BatteryPercent);

    [Fact]
    public void TheLowBitOfTheStatusFlagIsBatterySaver()
        => Assert.True(PowerStatus.FromRaw(acLineStatus: 0, batteryLifePercent: 30, systemStatusFlag: 1).BatterySaverOn);

    [Fact]
    public void AnUnexpectedLineStatusIsUnknownAndNotBattery()
    {
        PowerStatus status = PowerStatus.FromRaw(acLineStatus: 255, batteryLifePercent: 80, systemStatusFlag: 0);

        Assert.Equal(PowerSource.Unknown, status.Source);
        Assert.False(status.OnBattery);
    }
}
