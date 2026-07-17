using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class PdfBoxMetrics
{
    [JsonPropertyName("present")]
    public bool Present { get; set; } = false;

    [JsonPropertyName("leftPt")]
    public double? LeftPt { get; set; }

    [JsonPropertyName("bottomPt")]
    public double? BottomPt { get; set; }

    [JsonPropertyName("rightPt")]
    public double? RightPt { get; set; }

    [JsonPropertyName("topPt")]
    public double? TopPt { get; set; }

    [JsonPropertyName("widthPt")]
    public double? WidthPt { get; set; }

    [JsonPropertyName("heightPt")]
    public double? HeightPt { get; set; }

    [JsonPropertyName("widthMm")]
    public double? WidthMm { get; set; }

    [JsonPropertyName("heightMm")]
    public double? HeightMm { get; set; }
}

public class PdfAnalysisReport
{
    [JsonPropertyName("isError")]
    public bool IsError { get; set; } = false;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; } = 0;

    [JsonPropertyName("mediaBox")]
    public PdfBoxMetrics MediaBox { get; set; } = new();

    [JsonPropertyName("trimBox")]
    public PdfBoxMetrics TrimBox { get; set; } = new();

    [JsonPropertyName("bleedBox")]
    public PdfBoxMetrics BleedBox { get; set; } = new();

    [JsonPropertyName("cropBox")]
    public PdfBoxMetrics CropBox { get; set; } = new();

    [JsonPropertyName("finishedFormat")]
    public string? FinishedFormat { get; set; }

    [JsonPropertyName("bleedMm")]
    public double? BleedMm { get; set; }

    [JsonPropertyName("usesRgb")]
    public bool UsesRgb { get; set; } = false;

    [JsonPropertyName("usesCmyk")]
    public bool UsesCmyk { get; set; } = false;

    [JsonPropertyName("usesGray")]
    public bool UsesGray { get; set; } = false;

    [JsonPropertyName("spotColors")]
    public List<string> SpotColors { get; set; } = new();

    [JsonPropertyName("hasEmbeddedFonts")]
    public bool HasEmbeddedFonts { get; set; } = false;

    [JsonPropertyName("hasSubsetFonts")]
    public bool HasSubsetFonts { get; set; } = false;

    [JsonPropertyName("hasMissingFonts")]
    public bool HasMissingFonts { get; set; } = false;

    [JsonPropertyName("embeddedFonts")]
    public List<string> EmbeddedFonts { get; set; } = new();

    [JsonPropertyName("subsetFonts")]
    public List<string> SubsetFonts { get; set; } = new();

    [JsonPropertyName("missingFonts")]
    public List<string> MissingFonts { get; set; } = new();

    [JsonPropertyName("minImageDpi")]
    public double? MinImageDpi { get; set; }

    [JsonPropertyName("imagesBelow300DpiCount")]
    public int ImagesBelow300DpiCount { get; set; } = 0;

    // Champs réservés (étape 5) — non calculés à ce stade
    [JsonPropertyName("totalInkCoveragePercent")]
    public double? TotalInkCoveragePercent { get; set; }

    [JsonPropertyName("plateCoveragePercent")]
    public Dictionary<string, double>? PlateCoveragePercent { get; set; }

    [JsonPropertyName("hasOverprint")]
    public bool? HasOverprint { get; set; }
}
