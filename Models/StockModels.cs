using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

// ======================================================
// Gestion des stocks (point 7)
// Collections : "stockItems" (articles) et "stockMovements" (entrées/sorties).
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

public class StockMovementInput
{
    /// <summary>entree | sortie | ajustement</summary>
    [JsonPropertyName("type")]     public string? Type { get; set; }
    [JsonPropertyName("quantity")] public double? Quantity { get; set; }
    [JsonPropertyName("reason")]   public string? Reason { get; set; }
}

public static class StockCategories
{
    public static readonly string[] All = { "papier", "encre", "plaque", "carton", "consommable" };

    public static bool IsValid(string? c) =>
        !string.IsNullOrWhiteSpace(c) && Array.Exists(All, x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase));

    public static string Canonical(string c) =>
        Array.Find(All, x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) ?? c.ToLowerInvariant();
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
