namespace Vigie.Core.Power;

public enum PowerSource
{
    Unknown,
    Ac,
    Battery,
    ShortTerm,
}

public sealed record PowerStatus(
    PowerSource Source,
    int? BatteryPercent,
    bool BatterySaverOn,
    bool BatteryCritical)
{
    public bool OnBattery => Source is PowerSource.Battery or PowerSource.ShortTerm;

    /// <summary>
    /// Builds a status from the raw bytes of <c>SYSTEM_POWER_STATUS</c>. Kept
    /// separate from the P/Invoke so the mapping can be exercised in isolation.
    /// </summary>
    public static PowerStatus FromRaw(byte acLineStatus, byte batteryFlag, byte batteryLifePercent, byte systemStatusFlag)
    {
        PowerSource source = acLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.Ac,
            2 => PowerSource.ShortTerm,
            _ => PowerSource.Unknown,
        };

        int? percent = batteryLifePercent <= 100 ? batteryLifePercent : null;
        bool saver = (systemStatusFlag & 0x01) != 0;

        // BatteryFlag bit 2 (0x04) marks a critical charge level; 255 means unknown.
        bool critical = batteryFlag != 0xFF && (batteryFlag & 0x04) != 0;

        return new PowerStatus(source, percent, saver, critical);
    }
}
