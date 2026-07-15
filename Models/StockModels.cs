using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

// ======================================================
// Gestion des stocks (point 7)
// Collections : "stockItems" (articles), "stockMovements" (entrées/sorties),
//               "stockCategories" (catégories dynamiques).
// ======================================================

/// <summary>Données d'entrée pour créer / modifier un article de stock.</summary>
public class StockItemInput
{
    [JsonPropertyName("name")]       public string? Name { get; set; }
    [JsonPropertyName("category")]   public string? Category { get; set; }
    [JsonPropertyName("unit")]       public string? Unit { get; set; }
    [JsonPropertyName("quantity")]   public double? Quantity { get; set; }
    [JsonPropertyName("minThreshold")] public double? MinThreshold { get; set; }
    [JsonPropertyName("supplier")]   public string? Supplier { get; set; }
    [JsonPropertyName("reference")]  public string? Reference { get; set; }
    [JsonPropertyName("note")]       public string? Note { get; set; }
}

/// <summary>Données d'entrée pour créer / renommer une catégorie de stock.</summary>
public class StockCategoryInput
{
    [JsonPropertyName("label")]  public string? Label { get; set; }
    [JsonPropertyName("emoji")]  public string? Emoji { get; set; }
    [JsonPropertyName("order")]  public int? Order { get; set; }
}

public class StockMovementInput
{
    /// <summary>entree | sortie | ajustement</summary>
    [JsonPropertyName("type")]     public string? Type { get; set; }
    [JsonPropertyName("quantity")] public double? Quantity { get; set; }
    [JsonPropertyName("reason")]   public string? Reason { get; set; }
}

/// <summary>Corps pour POST /api/stock/ensure-paper — matérialisation d'un papier virtuel.</summary>
public class EnsurePaperInput
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Catégories par défaut (seed si la collection est vide).</summary>
public static class StockCategoryDefaults
{
    public static readonly (string Id, string Label, string Emoji, int Order)[] All =
    {
        ("papier",      "Papiers",      "📄", 0),
        ("encre",       "Encres",       "🎨", 1),
        ("plaque",      "Plaques",      "🟫", 2),
        ("carton",      "Cartons",      "📦", 3),
        ("consommable", "Consommables", "🧰", 4),
    };
}

public static class StockStatus
{
    /// <summary>rupture (≤ 0) | bas (≤ seuil) | ok</summary>
    public static string Compute(double quantity, double minThreshold)
    {
        if (quantity <= 0) return "rupture";
        if (minThreshold > 0 && quantity <= minThreshold) return "bas";
        return "ok";
    }
}
