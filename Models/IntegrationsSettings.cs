using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class IntegrationsSettings
{
    public const string DefaultPrismaPrepareOutputPath = @"C:\FluxAtelier\Base\Sortie";
    public const string DefaultPrismaPrepareDirectOutputPath = @"C:\FluxAtelier\Base\Sortie_PrepareDirect";

    [JsonPropertyName("preparePath")]
    public string PreparePath { get; set; } = "";

    [JsonPropertyName("fieryPath")]
    public string FieryPath { get; set; } = "";

    [JsonPropertyName("tempCopyPath")]
    public string TempCopyPath { get; set; } = "";

    [JsonPropertyName("prismaPrepareExePath")]
    public string PrismaPrepareExePath { get; set; } = "";

    [JsonPropertyName("prismaPrepareOutputPath")]
    public string PrismaPrepareOutputPath { get; set; } = DefaultPrismaPrepareOutputPath;

    [JsonPropertyName("prismaPrepareDirectOutputPath")]
    public string PrismaPrepareDirectOutputPath { get; set; } = DefaultPrismaPrepareDirectOutputPath;
}

