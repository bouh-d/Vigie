using System.Globalization;
using System.Resources;

namespace Vigie.App.Resources;

/// <summary>
/// Typed access to the interface strings in <c>Strings.resx</c>. Values
/// with placeholders are format strings, applied with <see cref="Format"/>.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("Vigie.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Format(string format, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, format, arguments);

    public static string AppTitle => Get("AppTitle");

    public static string Zone_Claude => Get("Zone_Claude");

    public static string Zone_Journal => Get("Zone_Journal");

    public static string Power_Ac => Get("Power_Ac");

    public static string Power_OnBattery => Get("Power_OnBattery");

    public static string Power_Battery => Get("Power_Battery");

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

    public static string Duration_CustomName => Get("Duration_CustomName");

    public static string Duration_UntilTask => Get("Duration_UntilTask");

    public static string Duration_CapLabel => Get("Duration_CapLabel");

    public static string Duration_CapUnit => Get("Duration_CapUnit");

    public static string Duration_CapName => Get("Duration_CapName");

    public static string Opt_LidClosed => Get("Opt_LidClosed");

    public static string Opt_KeepDisplay => Get("Opt_KeepDisplay");

    public static string Opt_LockOnLid => Get("Opt_LockOnLid");

    public static string Opt_SuspendAtEnd => Get("Opt_SuspendAtEnd");

    public static string Opt_BatteryStopPrefix => Get("Opt_BatteryStopPrefix");

    public static string Opt_BatteryStopSuffix => Get("Opt_BatteryStopSuffix");

    public static string Opt_LidClosedNote => Get("Opt_LidClosedNote");

    public static string Opt_Advanced => Get("Opt_Advanced");

    public static string Opt_ModernStandbyCompat => Get("Opt_ModernStandbyCompat");

    public static string Opt_ActivityLock_Lock => Get("Opt_ActivityLock_Lock");

    public static string Opt_ActivityLock_Sleep => Get("Opt_ActivityLock_Sleep");

    public static string ActivityLock_Idle => Get("ActivityLock_Idle");

    public static string ActivityLock_Arming => Get("ActivityLock_Arming");

    public static string ActivityLock_Armed => Get("ActivityLock_Armed");

    public static string ActivityLock_Disarmed => Get("ActivityLock_Disarmed");

    public static string Adv_ActivityHeader => Get("Adv_ActivityHeader");

    public static string Adv_Response => Get("Adv_Response");

    public static string Adv_ResponseLock => Get("Adv_ResponseLock");

    public static string Adv_ResponseSleep => Get("Adv_ResponseSleep");

    public static string Adv_SleepNote => Get("Adv_SleepNote");

    public static string Adv_Shortcut => Get("Adv_Shortcut");

    public static string Adv_ShortcutName => Get("Adv_ShortcutName");

    public static string Adv_ShortcutHint => Get("Adv_ShortcutHint");

    public static string Adv_ShortcutInvalid => Get("Adv_ShortcutInvalid");

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

    public static string Claude_Processes => Get("Claude_Processes");

    public static string Btn_Launch => Get("Btn_Launch");

    public static string Btn_Interrupt => Get("Btn_Interrupt");

    public static string Perm_Default => Get("Perm_Default");

    public static string Perm_Auto => Get("Perm_Auto");

    public static string Perm_AcceptEdits => Get("Perm_AcceptEdits");

    public static string Perm_DontAsk => Get("Perm_DontAsk");

    public static string Perm_Plan => Get("Perm_Plan");

    public static string Btn_OpenLogs => Get("Btn_OpenLogs");

    public static string Btn_Copy => Get("Btn_Copy");

    public static string State_Inactive => Get("State_Inactive");

    public static string State_Active => Get("State_Active");

    public static string State_ActiveUntilTask => Get("State_ActiveUntilTask");

    public static string State_Stopping => Get("State_Stopping");

    public static string State_Degraded => Get("State_Degraded");

    public static string Claude_Idle => Get("Claude_Idle");

    public static string Claude_Running_One => Get("Claude_Running_One");

    public static string Claude_Running_Many => Get("Claude_Running_Many");

    public static string Claude_Done_Success => Get("Claude_Done_Success");

    public static string Claude_Done_Interrupted => Get("Claude_Done_Interrupted");

    public static string Claude_Done_External => Get("Claude_Done_External");

    public static string Claude_Done_Unknown => Get("Claude_Done_Unknown");

    public static string Claude_Error => Get("Claude_Error");

    public static string Claude_Error_Unexpected => Get("Claude_Error_Unexpected");

    public static string Claude_Retry => Get("Claude_Retry");

    public static string Claude_ProcessLine => Get("Claude_ProcessLine");

    public static string Claude_OriginVigie => Get("Claude_OriginVigie");

    public static string Claude_OriginExternal => Get("Claude_OriginExternal");

    public static string Claude_Missing => Get("Claude_Missing");

    public static string Claude_InstallLinkText => Get("Claude_InstallLinkText");

    public static string Claude_InstallUrl => Get("Claude_InstallUrl");

    // Hyperlink.NavigateUri takes a Uri, and x:Static hands its value over
    // without the conversion XAML applies to literal text.
    public static Uri Claude_InstallUri => new(Claude_InstallUrl);

    public static string Warn_Battery => Get("Warn_Battery");

    public static string Warn_ModernStandbyBattery => Get("Warn_ModernStandbyBattery");

    public static string Warn_LidUnmanaged => Get("Warn_LidUnmanaged");

    public static string Warn_LidNotChanged => Get("Warn_LidNotChanged");

    public static string Warn_RequestsBlocked => Get("Warn_RequestsBlocked");

    public static string Warn_SleepDetected => Get("Warn_SleepDetected");

    public static string Warn_BatteryThreshold => Get("Warn_BatteryThreshold");

    public static string Warn_KeepDisplay => Get("Warn_KeepDisplay");

    public static string Warn_Interactive => Get("Warn_Interactive");

    public static string Warn_RebootPending => Get("Warn_RebootPending");

    public static string Dialog_Ok => Get("Dialog_Ok");

    public static string Dialog_Cancel => Get("Dialog_Cancel");

    public static string Dialog_BypassTitle => Get("Dialog_BypassTitle");

    public static string Dialog_Bypass => Get("Dialog_Bypass");

    public static string Dialog_CloseTitle => Get("Dialog_CloseTitle");

    public static string Dialog_Close => Get("Dialog_Close");

    public static string Dialog_Close_ClaudeRunning => Get("Dialog_Close_ClaudeRunning");

    public static string Dialog_Minimize => Get("Dialog_Minimize");

    public static string Dialog_StopQuit => Get("Dialog_StopQuit");

    public static string Dialog_RequestsBlockedTitle => Get("Dialog_RequestsBlockedTitle");

    public static string Dialog_StartFailed => Get("Dialog_StartFailed");

    public static string Dialog_UnrestoredJournal => Get("Dialog_UnrestoredJournal");

    public static string Dialog_InvalidOptions => Get("Dialog_InvalidOptions");

    public static string Dialog_ShortcutTaken => Get("Dialog_ShortcutTaken");

    public static string Dialog_RestoredTitle => Get("Dialog_RestoredTitle");

    public static string Dialog_Restored => Get("Dialog_Restored");

    public static string Dialog_JournalUnreadableTitle => Get("Dialog_JournalUnreadableTitle");

    public static string Dialog_JournalUnreadable => Get("Dialog_JournalUnreadable");

    public static string Tip_LidClosed => Get("Tip_LidClosed");

    public static string Tip_LockOnLid => Get("Tip_LockOnLid");

    public static string Tip_SuspendAtEnd => Get("Tip_SuspendAtEnd");

    public static string Tip_BatteryStop => Get("Tip_BatteryStop");

    public static string Tip_Autonomous => Get("Tip_Autonomous");

    public static string Tip_ActivityLock => Get("Tip_ActivityLock");

    public static string Tip_ModernStandbyCompat => Get("Tip_ModernStandbyCompat");

    public static string Claude_NotFound => Get("Claude_NotFound");

    public static string Claude_NotLoggedIn => Get("Claude_NotLoggedIn");

    public static string Claude_FolderInvalid => Get("Claude_FolderInvalid");

    public static string Claude_CheckFailed => Get("Claude_CheckFailed");

    public static string Claude_TargetGone => Get("Claude_TargetGone");

    public static string Claude_ArgumentsInvalid => Get("Claude_ArgumentsInvalid");

    public static string Claude_ArgumentReserved => Get("Claude_ArgumentReserved");

    public static string Claude_PromptRequired => Get("Claude_PromptRequired");

    public static string Tray_Show => Get("Tray_Show");

    public static string Tray_StopSession => Get("Tray_StopSession");

    public static string Tray_Quit => Get("Tray_Quit");

    public static string Tray_Tip_Active => Get("Tray_Tip_Active");

    public static string Tray_Tip_Inactive => Get("Tray_Tip_Inactive");

    public static string Notify_ActivityDisarmed => Get("Notify_ActivityDisarmed");

    public static string Notify_ActivityRearmed => Get("Notify_ActivityRearmed");

    private static string Get(string name) => Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
