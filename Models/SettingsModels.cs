using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class ScheduleSettings
{
    [JsonPropertyName("workStart")]
    public string WorkStart { get; set; } = "08:00";

    [JsonPropertyName("workEnd")]
    public string WorkEnd { get; set; } = "18:00";

    [JsonPropertyName("holidays")]
    public List<string> Holidays { get; set; } = new();
}

public class PathsSettings
{
    [JsonPropertyName("hotfoldersRoot")]
    public string HotfoldersRoot { get; set; } = @"C:\Flux";

    [JsonPropertyName("recycleBinPath")]
    public string RecycleBinPath { get; set; } = "";

    [JsonPropertyName("acrobatExePath")]
    public string AcrobatExePath { get; set; } = @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";
}

public class FabricationImportsSettings
{
    [JsonPropertyName("media1Path")]
    public string Media1Path { get; set; } = "";

    [JsonPropertyName("media2Path")]
    public string Media2Path { get; set; } = "";

    [JsonPropertyName("media3Path")]
    public string Media3Path { get; set; } = "";

    [JsonPropertyName("media4Path")]
    public string Media4Path { get; set; } = "";

    [JsonPropertyName("typeDocumentPath")]
    public string TypeDocumentPath { get; set; } = "";
}

public class PreflightSettings
{
    [JsonPropertyName("dropletStandard")]
    public string DropletStandard { get; set; } = "";

    [JsonPropertyName("dropletFondPerdu")]
    public string DropletFondPerdu { get; set; } = "";

    [JsonPropertyName("droplets")]
    public List<DropletConfig> Droplets { get; set; } = new();
}

public class DropletConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}

public class KanbanColumnConfig
{
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = "";

    [JsonPropertyName("folderPath")]
    public string? FolderPath { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#8f8f8f";

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("visibleActions")]
    public List<string>? VisibleActions { get; set; }
}

public class KanbanSettings
{
    [JsonPropertyName("columns")]
    public List<KanbanColumnConfig> Columns { get; set; } = new();
}

public class BatMailTemplate
{
    [JsonPropertyName("to")]
    public string To { get; set; } = "";

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "BAT - Dossier {{numeroDossier}} - {{nomClient}}";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "Bonjour,\n\nVeuillez trouver ci-joint le BAT pour le dossier {{numeroDossier}}.\n\nCordialement,";
}


public class CoverProductsSettings
{
    [JsonPropertyName("products")]
    public List<string> Products { get; set; } = new();
}

public class SheetCalculationSettings
{
    [JsonPropertyName("rules")]
    public Dictionary<string, int> Rules { get; set; } = new()
    {
        ["Brochure"] = 4,
        ["CDV"] = 21,
        ["Affiche A3"] = 1,
        ["Affiche A4"] = 2,
        ["Flyer A5"] = 4
    };
}

public class DeliveryDelaySettings
{
    [JsonPropertyName("delayHours")]
    public int DelayHours { get; set; } = 48;
}

public class PassesConfig
{
    [JsonPropertyName("faconnage")]
    public int Faconnage { get; set; } = 0;
    [JsonPropertyName("pelliculageRecto")]
    public int PelliculageRecto { get; set; } = 0;
    [JsonPropertyName("pelliculageRectoVerso")]
    public int PelliculageRectoVerso { get; set; } = 0;
    [JsonPropertyName("rainage")]
    public int Rainage { get; set; } = 0;
    [JsonPropertyName("dorure")]
    public int Dorure { get; set; } = 0;
    [JsonPropertyName("dosCarreColle")]
    public int DosCarreColle { get; set; } = 0;
}
