using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

/// <summary>Correction individuelle recommandée par le moteur de décision preflight.</summary>
public class PreflightCorrection
{
    /// <summary>Identifiant de la règle qui a déclenché cette correction.</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    /// <summary>Libellé de la correction (ex. « Conversion CMJN »).</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Description détaillée expliquant pourquoi la correction est recommandée.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Recommandation complète produite par le <c>PreflightDecisionEngine</c> à partir d'un
/// <c>PdfAnalysisReport</c> et des <c>PreflightRulesSettings</c>.
/// Le moteur ne lance aucun droplet : il compose et recommande uniquement.
/// </summary>
public class PreflightRecommendation
{
    /// <summary>
    /// Indique si le moteur preflight automatique est actif (flag <c>AutoPreflightSettings.Enabled</c>).
    /// Quand <c>false</c>, les autres champs sont vides et aucun comportement applicatif n'est modifié.
    /// </summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = false;

    /// <summary>Libellé du préflight conseillé (ex. « Preflight avec fond perdu »).</summary>
    [JsonPropertyName("advisedPreflightLabel")]
    public string AdvisedPreflightLabel { get; set; } = "";

    /// <summary>Liste ordonnée des corrections proposées.</summary>
    [JsonPropertyName("corrections")]
    public List<PreflightCorrection> Corrections { get; set; } = new();

    /// <summary>
    /// Droplet présélectionné parmi <c>PreflightSettings.Droplets</c>, par correspondance de nom/rôle.
    /// <c>null</c> si aucun droplet configuré ne correspond ou si aucune correction n'est requise.
    /// L'exécution reste sur <c>/api/acrobat/preflight</c> existant — ce droplet n'est pas lancé ici.
    /// </summary>
    [JsonPropertyName("selectedDroplet")]
    public DropletConfig? SelectedDroplet { get; set; }
}
