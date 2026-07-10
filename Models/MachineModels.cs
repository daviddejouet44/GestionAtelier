using System;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

// ======================================================
// Suivi temps réel des machines (point 3)
// Un document par moteur d'impression dans la collection "machineStatus".
// ======================================================
public class MachineStatusItem
{
    [JsonPropertyName("moteur")]
    public string Moteur { get; set; } = "";

    /// <summary>Disponible | En impression | En attente | En panne | Maintenance</summary>
    [JsonPropertyName("statut")]
    public string Statut { get; set; } = "Disponible";

    [JsonPropertyName("papierCharge")]
    public string PapierCharge { get; set; } = "";

    [JsonPropertyName("compteurFeuilles")]
    public long CompteurFeuilles { get; set; }

    /// <summary>Nom de fichier de l'OF en cours.</summary>
    [JsonPropertyName("ofEnCours")]
    public string OfEnCours { get; set; } = "";

    /// <summary>N° de dossier de l'OF en cours (affichage).</summary>
    [JsonPropertyName("ofEnCoursDossier")]
    public string OfEnCoursDossier { get; set; } = "";

    [JsonPropertyName("tempsRestantMinutes")]
    public int TempsRestantMinutes { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; set; } = "";
}

public static class MachineStatuses
{
    public static readonly string[] All =
        { "Disponible", "En impression", "En attente", "En panne", "Maintenance" };

    public static bool IsValid(string? s) =>
        !string.IsNullOrWhiteSpace(s) && Array.Exists(All, x => string.Equals(x, s, StringComparison.OrdinalIgnoreCase));
}
