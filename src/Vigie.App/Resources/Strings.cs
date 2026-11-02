using System.Globalization;
using System.Resources;

namespace Vigie.App.Resources;

/// <summary>
/// Typed access to the French interface strings in <c>Strings.resx</c>. Values
/// with placeholders are format strings, applied with <see cref="string.Format(IFormatProvider, string, object?[])"/>.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Vigie.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Format(string format, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, format, arguments);

    public static string AppTitle => Get("AppTitle");

    public static string Zone_Session => Get("Zone_Session");

    public static string Zone_Claude => Get("Zone_Claude");

    public static string Zone_Journal => Get("Zone_Journal");

    public static string Power_Ac => Get("Power_Ac");

    public static string Power_Battery => Get("Power_Battery");

    public static string Power_PercentUnknown => Get("Power_PercentUnknown");

    public static string Power_LidOpen => Get("Power_LidOpen");

    public static string Power_LidClosed => Get("Power_LidClosed");

    public static string Power_ModernStandby => Get("Power_ModernStandby");

    public static string Power_LegacySleep => Get("Power_LegacySleep");

    public static string Power_BatterySaver => Get("Power_BatterySaver");

    public static string Duration_Label => Get("Duration_Label");

    public static string Duration_15m => Get("Duration_15m");

    public static string Duration_1h => Get("Duration_1h");

    public static string Duration_2h => Get("Duration_2h");

    public static string Duration_4h => Get("Duration_4h");

    public static string Duration_Custom => Get("Duration_Custom");

    public static string Duration_CustomUnit => Get("Duration_CustomUnit");

    public static string Duration_UntilTask => Get("Duration_UntilTask");

    public static string Duration_UntilTaskCap => Get("Duration_UntilTaskCap");

    public static string Opt_LidClosed => Get("Opt_LidClosed");

    public static string Opt_KeepDisplay => Get("Opt_KeepDisplay");

    public static string Opt_LockOnLid => Get("Opt_LockOnLid");

    public static string Opt_SuspendAtEnd => Get("Opt_SuspendAtEnd");

    public static string Opt_BatteryStopPrefix => Get("Opt_BatteryStopPrefix");

    public static string Opt_BatteryStopSuffix => Get("Opt_BatteryStopSuffix");

    public static string Opt_LidClosedNote => Get("Opt_LidClosedNote");

    public static string Btn_Start => Get("Btn_Start");

    public static string Btn_Stop => Get("Btn_Stop");

    public static string Claude_Folder => Get("Claude_Folder");

    public static string Claude_Browse => Get("Claude_Browse");

    public static string Claude_Prompt => Get("Claude_Prompt");

    public static string Claude_Mode => Get("Claude_Mode");

    public static string Claude_Mode_Autonomous => Get("Claude_Mode_Autonomous");

    public static string Claude_Mode_Interactive => Get("Claude_Mode_Interactive");

    public static string Claude_Permissions => Get("Claude_Permissions");

    public static string Claude_Args => Get("Claude_Args");

    public static string Claude_TrackTarget => Get("Claude_TrackTarget");

    public static string Btn_Launch => Get("Btn_Launch");

    public static string Btn_Interrupt => Get("Btn_Interrupt");

    public static string Perm_Default => Get("Perm_Default");

    public static string Perm_Auto => Get("Perm_Auto");

    public static string Perm_AcceptEdits => Get("Perm_AcceptEdits");

    public static string Perm_DontAsk => Get("Perm_DontAsk");

    public static string Perm_Plan => Get("Perm_Plan");

    public static string Btn_OpenLogs => Get("Btn_OpenLogs");

    public static string Btn_Copy => Get("Btn_Copy");

    public static string Badge_Active => Get("Badge_Active");

    public static string Badge_Inactive => Get("Badge_Inactive");

    public static string State_Inactive => Get("State_Inactive");

    public static string State_Active => Get("State_Active");

    public static string State_ActiveUntilTask => Get("State_ActiveUntilTask");

    public static string State_Stopping => Get("State_Stopping");

    public static string State_Degraded => Get("State_Degraded");

    public static string Claude_Idle => Get("Claude_Idle");

    public static string Claude_Running_One => Get("Claude_Running_One");

    public static string Claude_Running_Many => Get("Claude_Running_Many");

    public static string Claude_Badge_Running => Get("Claude_Badge_Running");

    public static string Claude_Badge_Idle => Get("Claude_Badge_Idle");

    public static string Claude_Done_Success => Get("Claude_Done_Success");

    public static string Claude_Done_Interrupted => Get("Claude_Done_Interrupted");

    public static string Claude_Done_External => Get("Claude_Done_External");

    public static string Claude_Error => Get("Claude_Error");

    public static string Claude_Error_Unexpected => Get("Claude_Error_Unexpected");

    public static string Claude_Retry => Get("Claude_Retry");

    public static string Claude_ProcessLine => Get("Claude_ProcessLine");

    public static string Claude_OriginVigie => Get("Claude_OriginVigie");

    public static string Claude_OriginExternal => Get("Claude_OriginExternal");

    public static string Warn_Battery => Get("Warn_Battery");

    public static string Warn_ModernStandbyBattery => Get("Warn_ModernStandbyBattery");

    public static string Warn_LidUnmanaged => Get("Warn_LidUnmanaged");

    public static string Warn_RequestsBlocked => Get("Warn_RequestsBlocked");

    public static string Warn_SleepDetected => Get("Warn_SleepDetected");

    public static string Warn_BatteryThreshold => Get("Warn_BatteryThreshold");

    public static string Warn_KeepDisplay => Get("Warn_KeepDisplay");

    public static string Warn_Interactive => Get("Warn_Interactive");

    public static string Warn_RebootPending => Get("Warn_RebootPending");

    public static string Dialog_BypassTitle => Get("Dialog_BypassTitle");

    public static string Dialog_Bypass => Get("Dialog_Bypass");

    public static string Dialog_Continue => Get("Dialog_Continue");

    public static string Dialog_Cancel => Get("Dialog_Cancel");

    public static string Dialog_CloseTitle => Get("Dialog_CloseTitle");

    public static string Dialog_Close => Get("Dialog_Close");

    public static string Dialog_Close_ClaudeRunning => Get("Dialog_Close_ClaudeRunning");

    public static string Dialog_Minimize => Get("Dialog_Minimize");

    public static string Dialog_StopQuit => Get("Dialog_StopQuit");

    public static string Dialog_RequestsBlockedTitle => Get("Dialog_RequestsBlockedTitle");

    public static string Dialog_RestoredTitle => Get("Dialog_RestoredTitle");

    public static string Dialog_Restored => Get("Dialog_Restored");

    public static string Dialog_JournalUnreadableTitle => Get("Dialog_JournalUnreadableTitle");

    public static string Dialog_JournalUnreadable => Get("Dialog_JournalUnreadable");

    public static string Tip_LidClosed => Get("Tip_LidClosed");

    public static string Tip_KeepDisplay => Get("Tip_KeepDisplay");

    public static string Tip_LockOnLid => Get("Tip_LockOnLid");

    public static string Tip_SuspendAtEnd => Get("Tip_SuspendAtEnd");

    public static string Tip_BatteryStop => Get("Tip_BatteryStop");

    public static string Tip_Autonomous => Get("Tip_Autonomous");

    public static string Claude_NotFound => Get("Claude_NotFound");

    public static string Claude_NotFoundLink => Get("Claude_NotFoundLink");

    public static string Claude_NotLoggedIn => Get("Claude_NotLoggedIn");

    public static string Claude_FolderInvalid => Get("Claude_FolderInvalid");

    public static string Claude_CheckFailed => Get("Claude_CheckFailed");

    public static string Tray_Show => Get("Tray_Show");

    public static string Tray_StopSession => Get("Tray_StopSession");

    public static string Tray_Quit => Get("Tray_Quit");

    public static string Tray_Tip_Active => Get("Tray_Tip_Active");

    public static string Tray_Tip_Inactive => Get("Tray_Tip_Inactive");

    public static string Link_InstallNotLoggedIn => Get("Link_InstallNotLoggedIn");

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
