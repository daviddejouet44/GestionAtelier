using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

/// <summary>Résultat d'une étape individuelle dans la chaîne d'exécution preflight.</summary>
public class PreflightChainStepResult
{
    /// <summary>Identifiant de la règle/correction (ex. "rgb_to_cmyk").</summary>
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = "";

    /// <summary>Libellé humain de la correction tentée (ex. "Conversion CMJN").</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Nom du droplet utilisé pour cette étape (<c>null</c> si aucun droplet résolu).</summary>
    [JsonPropertyName("dropletName")]
    public string? DropletName { get; set; }

    /// <summary>Chemin du droplet exécuté (<c>null</c> si étape non exécutée).</summary>
    [JsonPropertyName("dropletPath")]
    public string? DropletPath { get; set; }

    /// <summary><c>true</c> si l'étape s'est terminée avec succès.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>Message d'erreur si <see cref="Ok"/> est <c>false</c>.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Rapport complet de l'exécution d'une chaîne de corrections preflight produit par
/// <c>PreflightChainRunner</c>.
/// </summary>
public class PreflightChainReport
{
    /// <summary><c>true</c> si toutes les étapes se sont terminées avec succès.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    /// <summary>
    /// Message d'erreur global (étape qui a échoué et a interrompu la chaîne).
    /// <c>null</c> si <see cref="Ok"/> est <c>true</c>.
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>Liste ordonnée des étapes tentées (succès et/ou échec jusqu'à l'arrêt).</summary>
    [JsonPropertyName("steps")]
    public List<PreflightChainStepResult> Steps { get; set; } = new();
}
