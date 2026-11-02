namespace Vigie.Core.Power;

public enum PowerSource
{
    Unknown,
    Ac,
    Battery,
}

public sealed record PowerStatus(PowerSource Source, int? BatteryPercent, bool BatterySaverOn)
{
    public bool OnBattery => Source == PowerSource.Battery;

    /// <summary>
    /// Builds a status from the raw bytes of <c>SYSTEM_POWER_STATUS</c>. Kept
    /// separate from the P/Invoke so the mapping can be exercised in isolation.
    /// </summary>
    public static PowerStatus FromRaw(byte acLineStatus, byte batteryLifePercent, byte systemStatusFlag)
    {
        PowerSource source = acLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.Ac,
            _ => PowerSource.Unknown,
        };

        // 255 means the charge is unknown, which is also what a machine
        // without a battery reports.
        int? percent = batteryLifePercent <= 100 ? batteryLifePercent : null;
        bool saver = (systemStatusFlag & 0x01) != 0;

        return new PowerStatus(source, percent, saver);
    }
}
