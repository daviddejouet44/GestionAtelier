using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public record FabricationHistory
{
    public DateTime Date { get; init; }
    public string User { get; init; } = "David";
    public string Action { get; init; } = "";
}

public record FabricationSheet
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; set; } = default!;
    public string? Machine { get; init; }
    public string? MoteurImpression { get; init; }
    public string? Operateur { get; init; }
    public int? Quantite { get; init; }
    public string? TypeTravail { get; init; }
    public string? Format { get; init; }
    public string? Papier { get; init; }
    public string? RectoVerso { get; init; }
    public string? Encres { get; init; }
    public string? Client { get; init; }
    public string? NumeroAffaire { get; init; }
    public string? NumeroDossier { get; init; }
    public string? Notes { get; init; }
    public DateTime? Delai { get; init; }
    public string? Media1 { get; init; }
    public string? Media2 { get; init; }
    public string? Media3 { get; init; }
    public string? Media4 { get; init; }
    public string? TypeDocument { get; init; }
    public int? NombreFeuilles { get; init; }
    public List<string>? Faconnage { get; init; }
    public string? Livraison { get; init; }
    public List<FabricationHistory> History { get; init; } = new();
}

public class FabricationInput
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Machine { get; set; }
    public string? MoteurImpression { get; set; }
    public string? Operateur { get; set; }
    public int? Quantite { get; set; }
    public string? TypeTravail { get; set; }
    public string? Format { get; set; }
    public string? Papier { get; set; }
    public string? RectoVerso { get; set; }
    public string? Encres { get; set; }
    public string? Client { get; set; }
    public string? NumeroAffaire { get; set; }
    public string? NumeroDossier { get; set; }
    public string? Notes { get; set; }
    public DateTime? Delai { get; set; }
    public string? Media1 { get; set; }
    public string? Media2 { get; set; }
    public string? Media3 { get; set; }
    public string? Media4 { get; set; }
    public string? TypeDocument { get; set; }
    public int? NombreFeuilles { get; set; }
    public List<string>? Faconnage { get; set; }
    public string? Livraison { get; set; }
}

// ======================================================
// Settings / Log types
// ======================================================

