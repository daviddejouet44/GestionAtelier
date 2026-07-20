using GestionAtelier.Models;

namespace GestionAtelier.Services;

/// <summary>Identifiants canoniques des règles preflight (valeurs attendues dans <c>PreflightRuleConfig.Id</c>).</summary>
public static class PreflightRuleIds
{
    /// <summary>RVB détecté → correction « Conversion CMJN ».</summary>
    public const string RgbToCmyk = "rgb_to_cmyk";

    /// <summary>Fond perdu inférieur au seuil configuré → correction « Ajout fond perdu ».</summary>
    public const string BleedInsufficient = "bleed_insufficient";

    /// <summary>Ton direct / Pantone détecté → correction « Conservation Pantone ».</summary>
    public const string SpotColorConservation = "spot_color_conservation";

    /// <summary>TAC supérieur au seuil configuré → correction « Réduction TAC » (requiert étape 5).</summary>
    public const string TacReduction = "tac_reduction";

    /// <summary>TrimBox absente → correction « Création TrimBox ».</summary>
    public const string TrimBoxMissing = "trim_box_missing";

    /// <summary>Résolution d'image inférieure au seuil configuré → correction « Images basse résolution ».</summary>
    public const string LowImageDpi = "low_image_dpi";
}

/// <summary>
/// Moteur de décision preflight automatique (Étape 2).
/// <para>
/// À partir d'un <see cref="PdfAnalysisReport"/> et des <see cref="PreflightRulesSettings"/> lus
/// depuis MongoDB, évalue les règles configurées par l'utilisateur et produit une
/// <see cref="PreflightRecommendation"/> contenant les corrections proposées et le droplet présélectionné.
/// </para>
/// <para>
/// <strong>Le moteur ne lance aucun droplet.</strong> L'exécution reste sur <c>/api/acrobat/preflight</c> existant.
/// Tous les seuils et l'activation de chaque règle sont lus depuis les settings Mongo —
/// aucune constante métier n'est codée en dur dans ce service.
/// </para>
/// </summary>
public static class PreflightDecisionEngine
{
    /// <summary>
    /// Évalue les règles configurées et retourne une recommandation.
    /// </summary>
    /// <param name="report">Rapport d'analyse PDF produit par <see cref="PdfAnalyzer"/>.</param>
    /// <param name="rulesSettings">Règles et seuils configurés par l'utilisateur (lus depuis Mongo).</param>
    /// <param name="autoSettings">
    /// Paramètres d'activation opt-in. Si <c>null</c> ou <c>Enabled = false</c>,
    /// une recommandation inactive est retournée sans modifier aucun comportement existant.
    /// </param>
    /// <param name="preflightSettings">
    /// Paramètres preflight contenant la liste des droplets disponibles pour la présélection.
    /// </param>
    /// <param name="printProfileId">
    /// Identifiant du profil d'impression (ex. "offset", "numerique") pour appliquer les seuils
    /// spécifiques au profil plutôt que les seuils globaux. Facultatif.
    /// </param>
    /// <returns>
    /// Une <see cref="PreflightRecommendation"/> avec les corrections ordonnées et le droplet présélectionné.
    /// </returns>
    public static PreflightRecommendation Evaluate(
        PdfAnalysisReport report,
        PreflightRulesSettings rulesSettings,
        AutoPreflightSettings? autoSettings = null,
        PreflightSettings? preflightSettings = null,
        string? printProfileId = null)
    {
        // Respect du flag opt-in : si la feature est désactivée (ou non configurée), retourner une
        // recommandation inactive. Aucun comportement existant n'est affecté tant que la feature
        // n'est pas explicitement activée (Enabled = true).
        if (autoSettings == null || !autoSettings.Enabled)
            return new PreflightRecommendation { IsActive = false };

        // Rapport en erreur : recommandation active mais vide (pas de corrections possibles).
        if (report.IsError)
            return new PreflightRecommendation { IsActive = true };

        // Résolution des seuils : profil spécifique en priorité, seuils globaux en repli.
        var thresholds = ResolveThresholds(rulesSettings, printProfileId);

        var corrections = new List<PreflightCorrection>();

        foreach (var rule in rulesSettings.Rules)
        {
            if (!rule.Enabled)
                continue;

            var correction = EvaluateRule(rule, report, thresholds);
            if (correction != null)
                corrections.Add(correction);
        }

        var advisedLabel = BuildAdvisedLabel(corrections);
        var selectedDroplet = SelectDroplet(corrections, rulesSettings, preflightSettings);

        return new PreflightRecommendation
        {
            IsActive = true,
            AdvisedPreflightLabel = advisedLabel,
            Corrections = corrections,
            SelectedDroplet = selectedDroplet
        };
    }

    // ── Résolution des seuils ──────────────────────────────────────────────────

    private static ResolvedThresholds ResolveThresholds(PreflightRulesSettings settings, string? profileId)
    {
        if (!string.IsNullOrEmpty(profileId) && settings.ProfileThresholds.Count > 0)
        {
            var profile = settings.ProfileThresholds.FirstOrDefault(p =>
                string.Equals(p.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

            if (profile != null)
            {
                // Les seuils du profil priment ; si non définis, repli sur les seuils globaux.
                return new ResolvedThresholds
                {
                    MaximumTacPercent  = profile.MaximumTacPercent  ?? settings.MaximumTacPercent,
                    MinimumBleedMm     = profile.MinimumBleedMm     ?? settings.MinimumBleedMm,
                    MinimumImageDpi    = profile.MinimumImageDpi    ?? settings.MinimumImageDpi
                };
            }
        }

        return new ResolvedThresholds
        {
            MaximumTacPercent = settings.MaximumTacPercent,
            MinimumBleedMm    = settings.MinimumBleedMm,
            MinimumImageDpi   = settings.MinimumImageDpi
        };
    }

    // ── Évaluation d'une règle ─────────────────────────────────────────────────

    private static PreflightCorrection? EvaluateRule(
        PreflightRuleConfig rule,
        PdfAnalysisReport report,
        ResolvedThresholds thresholds)
    {
        return rule.Id switch
        {
            PreflightRuleIds.RgbToCmyk              => EvaluateRgbToCmyk(rule, report),
            PreflightRuleIds.BleedInsufficient       => EvaluateBleedInsufficient(rule, report, thresholds),
            PreflightRuleIds.SpotColorConservation   => EvaluateSpotColorConservation(rule, report),
            PreflightRuleIds.TacReduction            => EvaluateTacReduction(rule, report, thresholds),
            PreflightRuleIds.TrimBoxMissing          => EvaluateTrimBoxMissing(rule, report),
            PreflightRuleIds.LowImageDpi             => EvaluateLowImageDpi(rule, report, thresholds),
            _                                        => null  // règle inconnue : ignorée
        };
    }

    private static PreflightCorrection? EvaluateRgbToCmyk(PreflightRuleConfig rule, PdfAnalysisReport report)
    {
        if (!report.UsesRgb)
            return null;

        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Conversion CMJN" : rule.Label,
            Description = "Le document contient des espaces couleur RVB qui doivent être convertis en CMJN."
        };
    }

    private static PreflightCorrection? EvaluateBleedInsufficient(
        PreflightRuleConfig rule,
        PdfAnalysisReport report,
        ResolvedThresholds thresholds)
    {
        // La règle est inactive si aucun seuil n'est configuré par l'utilisateur.
        if (!thresholds.MinimumBleedMm.HasValue)
            return null;

        var bleed = report.BleedMm ?? 0.0;
        if (bleed >= thresholds.MinimumBleedMm.Value)
            return null;

        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Ajout fond perdu" : rule.Label,
            Description = $"Le fond perdu ({bleed:F1} mm) est inférieur au minimum requis ({thresholds.MinimumBleedMm.Value:F1} mm)."
        };
    }

    private static PreflightCorrection? EvaluateSpotColorConservation(PreflightRuleConfig rule, PdfAnalysisReport report)
    {
        if (report.SpotColors.Count == 0)
            return null;

        var colorList = string.Join(", ", report.SpotColors);
        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Conservation Pantone" : rule.Label,
            Description = $"Le document utilise des tons directs : {colorList}."
        };
    }

    private static PreflightCorrection? EvaluateTacReduction(
        PreflightRuleConfig rule,
        PdfAnalysisReport report,
        ResolvedThresholds thresholds)
    {
        // Le TAC n'est calculé qu'à l'étape 5 : si le champ n'est pas renseigné, la règle reste inerte.
        if (!report.TotalInkCoveragePercent.HasValue)
            return null;

        // La règle est inactive si aucun seuil n'est configuré par l'utilisateur.
        if (!thresholds.MaximumTacPercent.HasValue)
            return null;

        if (report.TotalInkCoveragePercent.Value <= thresholds.MaximumTacPercent.Value)
            return null;

        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Réduction TAC" : rule.Label,
            Description = $"Le taux d'encrage total ({report.TotalInkCoveragePercent.Value:F1} %) " +
                          $"dépasse le seuil configuré ({thresholds.MaximumTacPercent.Value:F1} %)."
        };
    }

    private static PreflightCorrection? EvaluateTrimBoxMissing(PreflightRuleConfig rule, PdfAnalysisReport report)
    {
        if (report.TrimBox.Present)
            return null;

        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Création TrimBox" : rule.Label,
            Description = "Le document ne possède pas de TrimBox."
        };
    }

    private static PreflightCorrection? EvaluateLowImageDpi(
        PreflightRuleConfig rule,
        PdfAnalysisReport report,
        ResolvedThresholds thresholds)
    {
        // La règle est inactive si aucun seuil n'est configuré par l'utilisateur.
        if (!thresholds.MinimumImageDpi.HasValue)
            return null;

        // Pas d'images dans le document : règle non applicable.
        if (!report.MinImageDpi.HasValue)
            return null;

        if (report.MinImageDpi.Value >= thresholds.MinimumImageDpi.Value)
            return null;

        return new PreflightCorrection
        {
            RuleId      = rule.Id,
            Label       = string.IsNullOrEmpty(rule.Label) ? "Images basse résolution" : rule.Label,
            Description = $"La résolution minimale des images ({report.MinImageDpi.Value:F0} dpi) " +
                          $"est inférieure au seuil requis ({thresholds.MinimumImageDpi.Value} dpi)."
        };
    }

    // ── Libellé du préflight conseillé ────────────────────────────────────────

    private static string BuildAdvisedLabel(List<PreflightCorrection> corrections)
    {
        if (corrections.Count == 0)
            return "Aucune correction requise";

        bool needsBleed = corrections.Any(c => c.RuleId == PreflightRuleIds.BleedInsufficient);
        return needsBleed ? "Preflight avec fond perdu" : "Preflight standard avec corrections";
    }

    // ── Sélection du droplet ──────────────────────────────────────────────────

    /// <summary>
    /// Résout le droplet à utiliser pour une <paramref name="correction"/> donnée.
    /// <para>
    /// Priorité 1 : <c>TargetDropletName</c> de la règle correspondante.<br/>
    /// Priorité 2 : correspondance par mot-clé (« fond perdu » si règle <c>bleed_insufficient</c>).<br/>
    /// Priorité 3 : droplet standard (mot-clé « standard », « correction », « preflight »).<br/>
    /// Priorité 4 : premier droplet de la liste.
    /// </para>
    /// </summary>
    public static DropletConfig? ResolveDropletForCorrection(
        PreflightCorrection correction,
        PreflightRulesSettings rulesSettings,
        PreflightSettings? preflightSettings)
    {
        if (preflightSettings == null || preflightSettings.Droplets.Count == 0)
            return null;

        // Priorité 1 : TargetDropletName défini sur la règle.
        var rule = rulesSettings.Rules.FirstOrDefault(r => r.Id == correction.RuleId);
        if (rule != null && !string.IsNullOrEmpty(rule.TargetDropletName))
        {
            var targeted = preflightSettings.Droplets.FirstOrDefault(d =>
                string.Equals(d.Name, rule.TargetDropletName, StringComparison.OrdinalIgnoreCase));
            if (targeted != null)
                return targeted;
        }

        // Priorité 2 : mot-clé spécifique à la règle fond perdu.
        if (correction.RuleId == PreflightRuleIds.BleedInsufficient)
        {
            var bleedDroplet = FindDropletByKeyword(preflightSettings.Droplets, "fond perdu", "fondperdu", "bleed");
            if (bleedDroplet != null)
                return bleedDroplet;
        }

        // Priorité 3 : droplet générique de corrections/preflight.
        var standardDroplet = FindDropletByKeyword(preflightSettings.Droplets, "standard", "correction", "preflight");
        if (standardDroplet != null)
            return standardDroplet;

        // Priorité 4 : premier droplet disponible.
        return preflightSettings.Droplets.FirstOrDefault();
    }

    private static DropletConfig? SelectDroplet(
        List<PreflightCorrection> corrections,
        PreflightRulesSettings rulesSettings,
        PreflightSettings? preflightSettings)
    {
        if (preflightSettings == null || preflightSettings.Droplets.Count == 0)
            return null;

        if (corrections.Count == 0)
            return null;

        // Priorité 1 : droplet cible défini sur une règle déclenchée.
        foreach (var correction in corrections)
        {
            var rule = rulesSettings.Rules.FirstOrDefault(r => r.Id == correction.RuleId);
            if (rule != null && !string.IsNullOrEmpty(rule.TargetDropletName))
            {
                var targeted = preflightSettings.Droplets.FirstOrDefault(d =>
                    string.Equals(d.Name, rule.TargetDropletName, StringComparison.OrdinalIgnoreCase));
                if (targeted != null)
                    return targeted;
            }
        }

        bool needsBleed = corrections.Any(c => c.RuleId == PreflightRuleIds.BleedInsufficient);

        // Priorité 2 : droplet « fond perdu » si la règle fond perdu est déclenchée.
        if (needsBleed)
        {
            var bleedDroplet = FindDropletByKeyword(preflightSettings.Droplets, "fond perdu", "fondperdu", "bleed");
            if (bleedDroplet != null)
                return bleedDroplet;
        }

        // Priorité 3 : droplet de corrections standard.
        var standardDroplet = FindDropletByKeyword(preflightSettings.Droplets, "standard", "correction", "preflight");
        if (standardDroplet != null)
            return standardDroplet;

        // Dernier recours : premier droplet de la liste.
        return preflightSettings.Droplets.FirstOrDefault();
    }

    private static DropletConfig? FindDropletByKeyword(List<DropletConfig> droplets, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            var match = droplets.FirstOrDefault(d =>
                d.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }
        return null;
    }

    // ── Type interne ──────────────────────────────────────────────────────────

    private sealed class ResolvedThresholds
    {
        public double? MaximumTacPercent { get; set; }
        public double? MinimumBleedMm    { get; set; }
        public int?    MinimumImageDpi   { get; set; }
    }
}
