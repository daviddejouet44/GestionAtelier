using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

// ======================================================
// Planification intelligente — modèle de coût de calage
// (temps de changement entre deux OF sur une même machine)
// ======================================================

/// <summary>
/// Coûts de calage / changement par défaut, avec surcharges optionnelles par moteur.
/// Stocké dans la collection "settings" sous l'_id "changeoverCosts".
/// </summary>
public class ChangeoverCostSettings
{
    /// <summary>Temps de calage de base à chaque démarrage d'un nouveau tirage (minutes).</summary>
    [JsonPropertyName("calageBaseMinutes")]
    public int CalageBaseMinutes { get; set; } = 15;

    /// <summary>Surcoût lié à un changement de papier (minutes).</summary>
    [JsonPropertyName("changementPapierMinutes")]
    public int ChangementPapierMinutes { get; set; } = 10;

    /// <summary>Surcoût lié à un changement de format feuille (minutes).</summary>
    [JsonPropertyName("changementFormatMinutes")]
    public int ChangementFormatMinutes { get; set; } = 8;

    /// <summary>Surcharges spécifiques par moteur d'impression.</summary>
    [JsonPropertyName("engines")]
    public List<ChangeoverEngineOverride> Engines { get; set; } = new();

    /// <summary>Retourne les coûts effectifs pour un moteur donné (surcharge sinon défaut).</summary>
    public (int calage, int papier, int format) EffectiveFor(string? moteur)
    {
        var ov = Engines?.Find(e =>
            !string.IsNullOrWhiteSpace(e.Moteur) &&
            string.Equals(e.Moteur, moteur, System.StringComparison.OrdinalIgnoreCase));

        int calage = ov?.CalageBaseMinutes ?? CalageBaseMinutes;
        int papier = ov?.ChangementPapierMinutes ?? ChangementPapierMinutes;
        int format = ov?.ChangementFormatMinutes ?? ChangementFormatMinutes;
        return (calage, papier, format);
    }
}

public class ChangeoverEngineOverride
{
    [JsonPropertyName("moteur")]
    public string Moteur { get; set; } = "";

    [JsonPropertyName("calageBaseMinutes")]
    public int? CalageBaseMinutes { get; set; }

    [JsonPropertyName("changementPapierMinutes")]
    public int? ChangementPapierMinutes { get; set; }

    [JsonPropertyName("changementFormatMinutes")]
    public int? ChangementFormatMinutes { get; set; }
}
