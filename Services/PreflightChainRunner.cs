using GestionAtelier.Models;

namespace GestionAtelier.Services;

/// <summary>
/// Service d'orchestration preflight (Étape 6).
/// <para>
/// À partir d'une <see cref="PreflightRecommendation"/> (produite par
/// <see cref="PreflightDecisionEngine"/>), exécute séquentiellement chaque correction en passant
/// le PDF produit par une étape en entrée de la suivante.
/// </para>
/// <para>
/// Si une étape échoue, la chaîne est interrompue immédiatement : aucune étape suivante n'est
/// lancée et l'appelant reçoit un <see cref="PreflightChainReport"/> détaillant jusqu'où la
/// chaîne est allée.
/// </para>
/// </summary>
public static class PreflightChainRunner
{
    /// <summary>
    /// Exécute la chaîne de corrections recommandée.
    /// </summary>
    /// <param name="recommendation">
    /// Recommandation produite par <see cref="PreflightDecisionEngine.Evaluate"/>.
    /// Si <see cref="PreflightRecommendation.IsActive"/> est <c>false</c> ou que
    /// <see cref="PreflightRecommendation.Corrections"/> est vide, un rapport de succès vide est retourné.
    /// </param>
    /// <param name="preflightSettings">Paramètres preflight (liste des droplets disponibles).</param>
    /// <param name="rulesSettings">Règles preflight (pour la résolution règle → droplet).</param>
    /// <param name="pdfPath">Chemin complet du fichier PDF à traiter (modifié en place par les droplets).</param>
    /// <param name="ct">Jeton d'annulation optionnel.</param>
    /// <returns>
    /// Un <see cref="PreflightChainReport"/> listant chaque étape tentée, le droplet utilisé,
    /// le statut et l'éventuel message d'erreur.
    /// </returns>
    public static async Task<PreflightChainReport> RunAsync(
        PreflightRecommendation recommendation,
        PreflightSettings preflightSettings,
        PreflightRulesSettings rulesSettings,
        string pdfPath,
        CancellationToken ct = default)
    {
        var report = new PreflightChainReport();

        if (!recommendation.IsActive || recommendation.Corrections.Count == 0)
        {
            report.Ok = true;
            return report;
        }

        foreach (var correction in recommendation.Corrections)
        {
            ct.ThrowIfCancellationRequested();

            // Résolution règle → droplet (logique centralisée dans PreflightDecisionEngine).
            var droplet = PreflightDecisionEngine.ResolveDropletForCorrection(
                correction, rulesSettings, preflightSettings);

            var step = new PreflightChainStepResult
            {
                RuleId     = correction.RuleId,
                Label      = correction.Label,
                DropletName = droplet?.Name,
                DropletPath = droplet?.Path
            };

            if (droplet == null || string.IsNullOrWhiteSpace(droplet.Path))
            {
                step.Ok    = false;
                step.Error = $"Aucun droplet résolu pour la correction « {correction.Label} » (règle : {correction.RuleId}). Vérifiez la configuration des droplets dans Paramétrage > Preflight.";
                report.Steps.Add(step);
                report.Ok           = false;
                report.ErrorMessage = step.Error;
                // Arrêt immédiat : pas d'étape suivante.
                return report;
            }

            Console.WriteLine($"[PREFLIGHT CHAIN] Étape « {correction.Label} » → droplet : {droplet.Name} ({droplet.Path})");

            var result = await DropletRunner.RunAsync(droplet.Path, pdfPath, ct);

            step.Ok    = result.Ok;
            step.Error = result.Error;
            report.Steps.Add(step);

            if (!result.Ok)
            {
                report.Ok           = false;
                report.ErrorMessage = $"La correction « {correction.Label} » a échoué : {result.Error}";
                Console.WriteLine($"[PREFLIGHT CHAIN] Échec à l'étape « {correction.Label} » : {result.Error}");
                // Arrêt immédiat : pas d'étape suivante.
                return report;
            }

            Console.WriteLine($"[PREFLIGHT CHAIN] Étape « {correction.Label} » terminée avec succès.");
        }

        report.Ok = true;
        Console.WriteLine($"[PREFLIGHT CHAIN] Chaîne complète ({report.Steps.Count} étape(s)) terminée avec succès.");
        return report;
    }
}
