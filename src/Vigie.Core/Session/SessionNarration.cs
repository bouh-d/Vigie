using System.Globalization;
using Vigie.Core.Logging;
using Vigie.Core.Power;

namespace Vigie.Core.Session;

/// <summary>
/// Builds the French journal lines. Every power change is written with its
/// setting, the power sources it affects, and the value before and after.
/// </summary>
public static class SessionNarration
{
    public static string Started(SessionOptions options)
    {
        string scope = options.WorkWithLidClosed ? ", fonctionnement capot fermé" : string.Empty;
        return options.DurationKind == SessionDurationKind.UntilTask
            ? Fr($"Session démarrée jusqu'à la fin de la tâche Claude Code ({Duration(options.TaskModeCap)} maximum){scope}.")
            : Fr($"Session démarrée pour {Duration(options.FixedDuration)}{scope}.");
    }

    public static string KeepAwakeActive(bool usesFallback, bool displayKept)
    {
        string method = usesFallback ? " (méthode de repli)" : string.Empty;
        string display = displayKept ? ", écran maintenu allumé" : string.Empty;
        return Fr($"Maintien en éveil activé{method}{display}.");
    }

    public static IEnumerable<string> Applied(ApplyReport report)
    {
        foreach (IGrouping<(Guid Scheme, Guid Setting, uint Before, uint After), SettingChange> group in
                 report.Changes.GroupBy(c => (c.Scheme, c.Setting, c.Before, c.After)))
        {
            (Guid _, Guid setting, uint before, uint after) = group.Key;
            PowerIndex indices = group.Aggregate(default(PowerIndex), (acc, c) => acc | c.Index);
            yield return Fr($"{SettingName(setting)} : {Value(setting, before)} → {Value(setting, after)} ({Sources(indices)}).");
        }
    }

    public static IEnumerable<string> Restored(RestoreReport report)
    {
        foreach (IGrouping<(Guid Scheme, Guid Setting, uint? Current, uint? Original), RestoreOutcome> group in
                 report.Outcomes.Where(o => o.Kind == RestoreOutcomeKind.Restored)
                     .GroupBy(o => (o.Scheme, o.Setting, o.Current, o.Original)))
        {
            (Guid _, Guid setting, uint? current, uint? original) = group.Key;
            PowerIndex indices = group.Aggregate(default(PowerIndex), (acc, o) => acc | o.Index);
            yield return Fr($"{SettingName(setting)} rétabli : {Value(setting, current ?? 0)} → {Value(setting, original ?? 0)} ({Sources(indices)}).");
        }

        foreach (RestoreOutcome outcome in report.Outcomes.Where(o => o.Kind == RestoreOutcomeKind.KeptOtherValue))
        {
            yield return Fr($"{SettingName(outcome.Setting)} : valeur modifiée entre-temps ({Sources(outcome.Index)}), choix de l'utilisateur conservé.");
        }

        foreach (RestoreOutcome outcome in report.Outcomes.Where(o => o.Kind == RestoreOutcomeKind.Failed))
        {
            yield return Fr($"Échec du rétablissement de {SettingName(outcome.Setting)} ({Sources(outcome.Index)}) : code {outcome.Status}.");
        }
    }

    public static string Degraded(DegradedReason reason) => Fr(reason switch
    {
        DegradedReason.LidActionPolicy => "Mode dégradé : l'action du capot est imposée par une stratégie de groupe.",
        DegradedReason.WriteDenied => "Mode dégradé : l'action du capot n'a pas pu être modifiée.",
        DegradedReason.ThirdPartyConflict => "Mode dégradé : un autre logiciel modifie l'action du capot, Vigie cesse de la gérer.",
        _ => "Mode dégradé.",
    });

    public static string SleepDetected(SleepPeriod period)
        => Fr($"Le PC a été en veille de {Clock(period.Start)} à {Clock(period.End)} ({Duration(period.Duration)}). Claude Code était suspendu pendant ce temps.");

    public static string Resumed() => Fr("Sortie de veille : demandes de maintien en éveil recréées.");

    public static string DriftReapplied() => Fr("Un réglage d'alimentation avait changé : valeur de la session réappliquée.");

    public static string PowerSchemeChanged() => Fr("Mode d'alimentation changé : les réglages de la session y sont appliqués.");

    public static string PowerSchemeApplied(ApplyReport report)
        => string.Join(" ", Applied(report));

    public static string LidClosed(bool locked)
        => Fr(locked ? "Capot fermé : session Windows verrouillée." : "Capot fermé.");

    public static string LidOpened() => Fr("Capot rouvert.");

    public static string OnBattery()
        => Fr("Passage sur batterie pendant la session.");

    public static string BatteryStop(int percent)
        => Fr($"Batterie à {percent} % : session arrêtée, réglages d'alimentation rétablis.");

    public static string Suspended(bool succeeded)
        => Fr(succeeded
            ? "Mise en veille du PC (fin de session, capot fermé)."
            : "Mise en veille demandée ; le PC suivra le délai de Windows.");

    public static string RebootPending()
        => Fr("Un redémarrage de Windows Update est en attente : il peut interrompre la tâche.");

    public static string ModernStandbyOnBattery()
        => Fr("Sur batterie, la mise en veille automatique est désactivée pendant la session, puis rétablie.");

    public static string Stopped(StopReason reason, TimeSpan actualDuration, int sleepCount)
    {
        string sleeps = sleepCount switch
        {
            0 => string.Empty,
            1 => ", 1 veille détectée",
            _ => $", {sleepCount} veilles détectées",
        };
        return Fr($"Session terminée ({ReasonText(reason)}, durée réelle {Duration(actualDuration)}{sleeps}).");
    }

    private static string ReasonText(StopReason reason) => reason switch
    {
        StopReason.TimerElapsed => "minuteur écoulé",
        StopReason.UserStopped => "arrêt demandé",
        StopReason.TaskFinished => "tâche terminée",
        StopReason.BatteryThreshold => "seuil de batterie",
        StopReason.TaskModeCap => "plafond atteint",
        StopReason.ApplicationClosing => "fermeture de Vigie",
        StopReason.WindowsSessionEnding => "fermeture de session Windows",
        StopReason.UnhandledException => "erreur interne",
        _ => "arrêt",
    };

    private static string SettingName(Guid setting)
    {
        if (setting == PowerKnownSettings.LidAction)
        {
            return "Action à la fermeture du capot";
        }

        if (setting == PowerKnownSettings.StandbyIdle)
        {
            return "Mise en veille automatique";
        }

        return setting.ToString("D");
    }

    private static string Value(Guid setting, uint value)
    {
        if (setting == PowerKnownSettings.LidAction)
        {
            return value switch
            {
                (uint)LidAction.DoNothing => "Ne rien faire",
                (uint)LidAction.Sleep => "Veille",
                (uint)LidAction.Hibernate => "Veille prolongée",
                (uint)LidAction.Shutdown => "Arrêter",
                _ => $"valeur {value}",
            };
        }

        if (setting == PowerKnownSettings.StandbyIdle)
        {
            return value == 0 ? "Jamais" : Duration(TimeSpan.FromSeconds(value));
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Sources(PowerIndex indices) => indices switch
    {
        PowerIndex.Ac => "secteur",
        PowerIndex.Dc => "batterie",
        _ => "secteur, batterie",
    };

    private static string Duration(TimeSpan duration)
    {
        int hours = (int)duration.TotalHours;
        int minutes = duration.Minutes;

        if (hours > 0 && minutes > 0)
        {
            return $"{hours} h {minutes:00}";
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

    private static string Clock(DateTimeOffset time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Fr(string text) => FrenchTypography.Fix(text);
}
