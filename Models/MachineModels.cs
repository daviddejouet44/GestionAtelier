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

// ── Pilotage machines (point 8) : configuration de connexion ─────────────
public class MachineConnectionInput
{
    [JsonPropertyName("moteur")]         public string? Moteur { get; set; }
    /// <summary>manual | push | http</summary>
    [JsonPropertyName("protocol")]       public string? Protocol { get; set; }
    /// <summary>URL de statut (protocol http) ou identifiant de l'agent (push).</summary>
    [JsonPropertyName("address")]        public string? Address { get; set; }
    [JsonPropertyName("pollIntervalSec")] public int? PollIntervalSec { get; set; }
    [JsonPropertyName("enabled")]        public bool? Enabled { get; set; }
}

public class MachineTokenConfig
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
}

public static class MachineProtocols
{
    // manual : saisie opérateur ; push : l'agent/passerelle pousse la télémétrie ;
    // http : le serveur interroge une URL de statut (pull).
    public static readonly string[] All = { "manual", "push", "http" };

    public static bool IsValid(string? p) =>
        !string.IsNullOrWhiteSpace(p) && Array.Exists(All, x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));

    public static string Canonical(string p) =>
        Array.Find(All, x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)) ?? "manual";
}
