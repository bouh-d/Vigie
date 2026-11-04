using System.Globalization;
using Vigie.Core.Power;

namespace Vigie.Core.Session;

/// <summary>
/// Builds the journal lines. Every power change is written with its setting,
/// the power sources it affects, and the value before and after.
/// </summary>
public static class SessionNarration
{
    private const string DisplayKeptNotice = "The screen will stay on and the PC will no longer lock by itself.";

    public static string Started(SessionOptions options)
    {
        string scope = options.WorkWithLidClosed ? ", running with the lid closed" : string.Empty;
        return options.DurationKind == SessionDurationKind.UntilTask
            ? $"Session started until the Claude Code task ends ({Duration(options.TaskModeCap)} at most){scope}."
            : $"Session started for {Duration(options.FixedDuration)}{scope}.";
    }

    public static string KeepAwakeActive(bool usesFallback, bool displayKept)
    {
        string method = usesFallback ? " (fallback method)" : string.Empty;
        string display = displayKept ? " " + DisplayKeptNotice : string.Empty;
        return $"Keep-awake request on{method}.{display}";
    }

    public static string StartRefused(StartRefusal refusal) => refusal switch
    {
        StartRefusal.KeepAwakeBlockedByPolicy => "Start refused: your organization prevents apps from blocking sleep.",
        StartRefusal.UnrestoredJournal => "Start refused: the restore journal of a previous session has to be dealt with first.",
        StartRefusal.TrackedProcessGone => "Start refused: the Claude Code task to follow is no longer running.",
        _ => "Start refused.",
    };

    public static string StartAborted(string cause) => $"Start cancelled, settings put back: {cause}";

    public static IEnumerable<string> Applied(ApplyReport report)
    {
        foreach (IGrouping<(Guid Scheme, Guid Setting, uint Before, uint After), SettingChange> group in
                 report.Changes.GroupBy(c => (c.Scheme, c.Setting, c.Before, c.After)))
        {
            (Guid _, Guid setting, uint before, uint after) = group.Key;
            PowerIndex indices = group.Aggregate(default(PowerIndex), (acc, c) => acc | c.Index);
            yield return $"{SettingName(setting)}: {Value(setting, before)} → {Value(setting, after)} ({Sources(indices)}).";
        }
    }

    public static IEnumerable<string> Restored(RestoreReport report)
    {
        foreach (IGrouping<(Guid Scheme, Guid Setting, uint? Current, uint? Original), RestoreOutcome> group in RestoredGroups(report))
        {
            (Guid _, Guid setting, uint? current, uint? original) = group.Key;
            PowerIndex indices = group.Aggregate(default(PowerIndex), (acc, o) => acc | o.Index);
            yield return $"{SettingName(setting)} restored: {Value(setting, current ?? 0)} → {Value(setting, original ?? 0)} ({Sources(indices)}).";
        }

        foreach (RestoreOutcome outcome in report.Outcomes.Where(o => o.Kind == RestoreOutcomeKind.KeptOtherValue))
        {
            yield return $"{SettingName(outcome.Setting)}: changed in the meantime ({Sources(outcome.Index)}), the user's choice is kept.";
        }

        foreach (RestoreOutcome outcome in report.Outcomes.Where(o => o.Kind == RestoreOutcomeKind.Failed))
        {
            yield return $"Could not restore the {LowerFirst(SettingName(outcome.Setting))} ({Sources(outcome.Index)}): code {outcome.Status}.";
        }

        if (!report.Complete)
        {
            yield return "Restore incomplete: the journal is kept for another attempt.";
        }
    }

    /// <summary>
    /// The restored values in a few words, for the message shown when Vigie
    /// starts after a session that did not end normally, for example
    /// "lid action set to Sleep".
    /// </summary>
    public static string RestoredSummary(RestoreReport report)
    {
        List<string> parts =
        [
            .. RestoredGroups(report)
                .Select(group => group.Key)
                .DistinctBy(key => (key.Setting, key.Original))
                .Select(key => $"{LowerFirst(SettingName(key.Setting))} set to {Value(key.Setting, key.Original ?? 0)}"),
        ];

        return parts.Count == 0
            ? "none of Vigie's values were in place yet"
            : string.Join(", ", parts);
    }

    public static string LeftoverRestored()
        => "A previous session did not end normally: its power settings were restored.";

    public static string JournalUnreadable()
        => "Unreadable restore journal: no setting was changed automatically.";

    public static string Degraded(DegradedReason reason) => reason switch
    {
        DegradedReason.LidActionPolicy => "Degraded mode: a group policy sets the lid action.",
        DegradedReason.WriteDenied => "Degraded mode: the lid action could not be changed.",
        DegradedReason.ThirdPartyConflict => "Degraded mode: another program keeps changing the lid action, so Vigie stops managing it.",
        _ => "Degraded mode.",
    };

    public static string SleepDetected(SleepPeriod period)
        => $"The PC was asleep from {Clock(period.Start)} to {Clock(period.End)} ({Duration(period.Duration)}). Claude Code was paused during that time.";

    public static string Resumed() => "Back from sleep: keep-awake requests recreated.";

    public static string ReassertFailed(string cause) => $"Could not recreate the keep-awake request: {cause}";

    public static string DriftReapplied() => "A power setting had changed: the session's value was applied again.";

    public static string PowerSchemeChanged() => "Power plan changed: the session's settings are applied to it.";

    public static string LidClosed(bool locked, bool displayHeld)
    {
        string compatibility = displayHeld ? ", display kept active (Modern Standby compatibility)" : string.Empty;
        return locked
            ? $"Lid closed: Windows session locked{compatibility}."
            : $"Lid closed{compatibility}.";
    }

    public static string LockFailed() => "Lid closed: locking the Windows session failed.";

    public static string LidOpened() => "Lid opened.";

    public static string OnBattery() => "Switched to battery during the session.";

    public static string BatteryStop(int percent)
        => $"Battery at {percent}%: session stopped, power settings restored.";

    public static string Suspended(bool succeeded)
        => succeeded
            ? "Putting the PC to sleep (end of session, lid closed)."
            : "Sleep requested; the PC will follow the Windows timeout.";

    public static string RebootPending()
        => "A Windows Update restart is pending and may interrupt the task.";

    public static string SleepModel(bool modernStandby)
        => modernStandby ? "This PC uses Modern Standby." : "This PC uses S3 sleep.";

    public static string ModernStandbyOnBattery()
        => "On battery, sleep is turned off during the session and turned back on afterwards.";

    public static string ActivityLockOn(Shortcut shortcut)
        => $"Lock ready: on after {Duration(ActivityLock.QuietPeriod)} without activity, {shortcut} to turn it off.";

    public static string ActivityLockArmed(ActivityResponse response)
        => response == ActivityResponse.Lock
            ? "Lock on: the next movement will lock the PC."
            : "Lock on: the next movement will put the PC to sleep.";

    public static string ActivityLockDisarmed(Shortcut shortcut) => $"Lock turned off ({shortcut}).";

    public static string ActivityLockRearmed()
        => $"Lock turned back on: on after {Duration(ActivityLock.QuietPeriod)} without activity.";

    public static string ActivityLockPausedByWindowsLock(Shortcut shortcut)
        => $"Windows locked: lock turned off, {shortcut} to turn it back on.";

    public static string ActivityLocked(bool succeeded)
        => succeeded
            ? "Activity detected: PC locked."
            : "Activity detected, but locking the PC failed.";

    public static string ActivitySuspending() => "Activity detected: putting the PC to sleep.";

    public static string ActivitySuspendFailed() => "Putting the PC to sleep failed.";

    public static string SettingsNotSaved(string cause) => $"Could not save the settings: {cause}";

    public static string RecoveryUnavailable(string cause) => $"Safety net unavailable: {cause}";

    public static string RecoveryCleanupFailed(string cause) => $"Safety net cleanup incomplete: {cause}";

    public static string RestoreFailed(string cause)
        => $"The restore journal could not be updated: {cause}. The next launch will pick it up.";

    public static string Stopped(StopReason reason, TimeSpan actualDuration, int sleepCount)
    {
        string sleeps = sleepCount switch
        {
            0 => string.Empty,
            1 => ", 1 sleep detected",
            _ => $", {sleepCount} sleeps detected",
        };
        return $"Session ended ({ReasonText(reason)}, ran for {Duration(actualDuration)}{sleeps}).";
    }

    private static IEnumerable<IGrouping<(Guid Scheme, Guid Setting, uint? Current, uint? Original), RestoreOutcome>> RestoredGroups(RestoreReport report)
        => report.Outcomes
            .Where(o => o.Kind == RestoreOutcomeKind.Restored)
            .GroupBy(o => (o.Scheme, o.Setting, o.Current, o.Original));

    private static string ReasonText(StopReason reason) => reason switch
    {
        StopReason.TimerElapsed => "timer elapsed",
        StopReason.UserStopped => "stopped by you",
        StopReason.TaskFinished => "task finished",
        StopReason.BatteryThreshold => "battery threshold",
        StopReason.TaskModeCap => "task mode limit reached",
        StopReason.ApplicationClosing => "Vigie closing",
        StopReason.WindowsSessionEnding => "Windows signing out",
        StopReason.UnhandledException => "internal error",
        _ => "stopped",
    };

    private static string SettingName(Guid setting)
    {
        if (setting == PowerKnownSettings.LidAction)
        {
            return "Lid action";
        }

        if (setting == PowerKnownSettings.StandbyIdle)
        {
            return "Sleep timeout";
        }

        return setting.ToString("D");
    }

    private static string Value(Guid setting, uint value)
    {
        if (setting == PowerKnownSettings.LidAction)
        {
            return value switch
            {
                (uint)LidAction.DoNothing => "Do nothing",
                (uint)LidAction.Sleep => "Sleep",
                (uint)LidAction.Hibernate => "Hibernate",
                (uint)LidAction.Shutdown => "Shut down",
                _ => $"value {value}",
            };
        }

        if (setting == PowerKnownSettings.StandbyIdle)
        {
            return value == 0 ? "Never" : Duration(TimeSpan.FromSeconds(value));
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Sources(PowerIndex indices) => indices switch
    {
        PowerIndex.Ac => "AC",
        PowerIndex.Dc => "battery",
        _ => "AC, battery",
    };

    private static string Duration(TimeSpan duration)
    {
        int hours = (int)duration.TotalHours;
        int minutes = duration.Minutes;

        if (hours > 0 && minutes > 0)
        {
            return $"{hours} h {minutes} min";
        }

        if (hours > 0)
        {
            return $"{hours} h";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} min";
        }

        return $"{(int)duration.TotalSeconds} s";
    }

    private static string LowerFirst(string text)
        => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string Clock(DateTimeOffset time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
