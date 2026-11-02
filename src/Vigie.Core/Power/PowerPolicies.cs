using Vigie.Core.Platform;

namespace Vigie.Core.Power;

/// <summary>
/// Machine policies that limit what Vigie may do. All of them are read-only
/// checks under <c>HKLM</c>; Vigie never writes there.
/// </summary>
public sealed record PowerPolicies(
    bool LidActionEnforced,
    bool StandbyIdleEnforced,
    bool KeepAwakeBlockedOnAc,
    bool KeepAwakeBlockedOnBattery,
    bool RebootPending)
{
    private const string PowerSettingsPolicies = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\";

    // "Allow applications to prevent automatic sleep" (ADMX_Power).
    private const string AllowAppsToPreventSleep = "A4B195F5-8225-47D8-8012-9D41369786E2";

    private const string RebootRequired = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";

    private const string AcIndex = "ACSettingIndex";
    private const string DcIndex = "DCSettingIndex";

    public static PowerPolicies Read(IRegistry registry)
    {
        return new PowerPolicies(
            LidActionEnforced: IsEnforced(registry, PowerKnownSettings.LidAction),
            StandbyIdleEnforced: IsEnforced(registry, PowerKnownSettings.StandbyIdle),
            KeepAwakeBlockedOnAc: IsZero(registry, AllowAppsToPreventSleep, AcIndex),
            KeepAwakeBlockedOnBattery: IsZero(registry, AllowAppsToPreventSleep, DcIndex),
            RebootPending: registry.MachineSubKeyExists(RebootRequired));
    }

    public bool KeepAwakeBlockedFor(PowerStatus status)
        => status.OnBattery ? KeepAwakeBlockedOnBattery : KeepAwakeBlockedOnAc;

    private static bool IsEnforced(IRegistry registry, Guid setting)
    {
        string key = PowerSettingsPolicies + setting.ToString("D").ToUpperInvariant();
        return registry.TryReadMachineInt32(key, AcIndex, out _)
            || registry.TryReadMachineInt32(key, DcIndex, out _);
    }

    private static bool IsZero(IRegistry registry, string setting, string valueName)
        => registry.TryReadMachineInt32(PowerSettingsPolicies + setting, valueName, out int value) && value == 0;
}
