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

// ======================================================
// Gestion des priorités (point 2)
// Poids des facteurs de priorité + liste des clients VIP.
// Stocké dans "settings" sous l'_id "priorityConfig".
// ======================================================
public class PriorityConfig
{
    /// <summary>Poids d'un OF marqué « urgent » manuellement.</summary>
    [JsonPropertyName("weightUrgent")]
    public int WeightUrgent { get; set; } = 100;

    /// <summary>Poids d'un OF d'un client VIP.</summary>
    [JsonPropertyName("weightVip")]
    public int WeightVip { get; set; } = 40;

    /// <summary>Poids d'un OF en retard (date d'impression ou de réception dépassée).</summary>
    [JsonPropertyName("weightRetard")]
    public int WeightRetard { get; set; } = 60;

    /// <summary>Poids d'un OF modifié à la dernière minute.</summary>
    [JsonPropertyName("weightModif")]
    public int WeightModif { get; set; } = 25;

    /// <summary>Fenêtre (heures) pendant laquelle une modification est considérée « de dernière minute ».</summary>
    [JsonPropertyName("modifWindowHours")]
    public int ModifWindowHours { get; set; } = 24;

    /// <summary>Noms des clients VIP (comparaison insensible à la casse).</summary>
    [JsonPropertyName("vipClients")]
    public List<string> VipClients { get; set; } = new();
}
