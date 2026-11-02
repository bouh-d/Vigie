using System.Globalization;
using Vigie.Core.Logging;

namespace Vigie.Core.ClaudeCode;

/// <summary>The French journal lines about Claude Code processes and runs.</summary>
public static class ClaudeNarration
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Launched(int processId, ClaudeLaunchMode mode)
    {
        string kind = mode == ClaudeLaunchMode.Autonomous ? "tâche autonome" : "session interactive";
        return Fr($"Claude Code lancé (PID {processId}, {kind}).");
    }

    public static string OutputRecordedIn(string directory) => Fr($"Sortie de Claude Code enregistrée dans {directory}.");

    public static string SessionIdentified(string sessionId) => Fr($"Session Claude Code {sessionId}.");

    public static string Retry(ApiRetryEvent retry)
    {
        string category = retry.ErrorCategory is { Length: > 0 } value ? $" ({value})" : string.Empty;
        return Fr($"Claude Code : connexion instable, nouvelle tentative {retry.Attempt} sur {retry.MaxRetries}{category}.");
    }

    public static string PermissionDenied(PermissionDeniedEvent denied)
        => Fr($"Claude Code : action refusée ({denied.ToolName ?? "outil inconnu"}).");

    public static string Result(ResultEvent result)
    {
        if (result.Succeeded)
        {
            string cost = result.TotalCostUsd is decimal value
                ? $", coût {value.ToString("0.00", French)} $"
                : string.Empty;
            return Fr($"Claude Code a terminé : {result.NumTurns ?? 0} tours, {result.PermissionDenials} actions refusées{cost}.");
        }

        string detail = result.Errors.Count > 0 ? $" : {result.Errors[0]}" : string.Empty;
        return Fr($"Claude Code a terminé en erreur ({result.Subtype}){detail}.");
    }

    public static string Exited(int processId, int exitCode)
        => Fr($"Claude Code (PID {processId}) s'est arrêté, code de sortie {exitCode}.");

    public static string Interrupted(InterruptOutcome outcome) => Fr(outcome switch
    {
        InterruptOutcome.StoppedCleanly => "Claude Code interrompu à votre demande.",
        InterruptOutcome.Killed => "Claude Code ne s'est pas arrêté à temps : son arbre de processus a été terminé.",
        _ => "Claude Code s'était déjà arrêté.",
    });

    public static string ExternalExited(ExternalExit exit)
    {
        string code = exit.ExitCode is int value ? $", code de sortie {value}" : string.Empty;
        return Fr($"Claude Code (PID {exit.Process.ProcessId}, externe) s'est arrêté{code}.");
    }

    public static string CandidateRejected(RejectedCandidate candidate) => Fr(candidate.Reason switch
    {
        CandidateRejection.GraphicalApplication => $"{candidate.Path} ignoré : application graphique, sans doute l'application de bureau Claude.",
        CandidateRejection.InsideProjectFolder => $"{candidate.Path} ignoré : il se trouve dans le dossier du projet.",
        CandidateRejection.InCurrentDirectory => $"{candidate.Path} ignoré : il se trouve dans le dossier courant.",
        CandidateRejection.NotAnExecutable => $"{candidate.Path} ignoré : ce n'est pas un exécutable Windows.",
        _ => $"{candidate.Path} introuvable.",
    });

    public static string NodeScanFailed(string cause) => Fr($"Détection des installations npm de Claude Code indisponible : {cause}");

    public static string RecordingFailed(string cause) => Fr($"Enregistrement de la sortie de Claude Code interrompu : {cause}");

    public static string OldRunsCleanupFailed(string cause) => Fr($"Suppression des anciennes sorties de Claude Code impossible : {cause}");

    private static string Fr(string text) => FrenchTypography.Fix(text);
}
