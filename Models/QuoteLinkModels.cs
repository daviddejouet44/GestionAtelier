using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

/// <summary>
/// A quote link allows staff to send a formatted email to a client with a unique URL.
/// The client clicks the link, reviews the quote recap, uploads their production PDF
/// and confirms the order — all without needing a portal account.
/// </summary>
public class QuoteLink
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Random hex token used in the client-facing URL.</summary>
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    /// <summary>Quote/devis reference number from the ERP (e.g. "D2024-1234").</summary>
    [JsonPropertyName("devisNumber")]
    public string DevisNumber { get; set; } = "";

    // ── Client info ────────────────────────────────────────────────────────────

    [JsonPropertyName("clientName")]
    public string ClientName { get; set; } = "";

    [JsonPropertyName("clientEmail")]
    public string ClientEmail { get; set; } = "";

    // ── Quote / product fields ─────────────────────────────────────────────────

    /// <summary>Product description (e.g. "Brochure 24 pages").</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("paper")]
    public string Paper { get; set; } = "";

    /// <summary>Color specification (e.g. "4/4", "4/0", "N&B").</summary>
    [JsonPropertyName("encres")]
    public string Encres { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 0;

    [JsonPropertyName("finitions")]
    public List<string> Finitions { get; set; } = new();

    [JsonPropertyName("pagination")]
    public int? Pagination { get; set; }

    [JsonPropertyName("recto")]
    public string Recto { get; set; } = "recto";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    // ── Attached quote PDF (from ERP) ─────────────────────────────────────────

    [JsonPropertyName("quotePdfFileName")]
    public string? QuotePdfFileName { get; set; }

    [JsonPropertyName("quotePdfStoredPath")]
    public string? QuotePdfStoredPath { get; set; }

    // ── Optional link to production sheet ─────────────────────────────────────

    [JsonPropertyName("fichePath")]
    public string? FichePath { get; set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending"; // "pending" | "used" | "revoked"

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("usedAt")]
    public DateTime? UsedAt { get; set; }

    /// <summary>The ClientOrder id created when the client submits via this link.</summary>
    [JsonPropertyName("resultOrderId")]
    public string? ResultOrderId { get; set; }

    [JsonPropertyName("resultOrderNumber")]
    public string? ResultOrderNumber { get; set; }

    /// <summary>Login of the staff member who created this link.</summary>
    [JsonPropertyName("createdByLogin")]
    public string? CreatedByLogin { get; set; }
}
