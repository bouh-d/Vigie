namespace Vigie.Core.Power;

/// <summary>
/// Power scheme subgroup and setting identifiers, and the power-setting
/// notification identifiers, as published by Microsoft. Verified against the
/// Windows power settings documentation.
/// </summary>
public static class PowerKnownSettings
{
    public static readonly Guid Buttons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    public static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public static readonly Guid Sleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    public static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    public static readonly Guid LidSwitchStateChange = new("ba3e0f4d-b817-4094-a2d1-d56379e6a0f3");
    public static readonly Guid AcDcPowerSource = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");
    public static readonly Guid BatteryPercentageRemaining = new("a7ad8041-b45a-4cae-87a3-eecbb468a9e1");
    public static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");
    public static readonly Guid ActivePowerScheme = new("31f9f286-5084-42fe-b720-2b0264993763");
}
