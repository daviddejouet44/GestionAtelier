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

    [JsonPropertyName("fieryPaths")]
    public List<string> FieryPaths { get; set; } = new();
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

public class KeyDatesSettings
{
    [JsonPropertyName("sendOffsetHours")]
    public int SendOffsetHours { get; set; } = 48;

    [JsonPropertyName("finitionsOffsetHours")]
    public int FinitionsOffsetHours { get; set; } = 72;

    [JsonPropertyName("impressionOffsetHours")]
    public int ImpressionOffsetHours { get; set; } = 96;
}

public class GrammageTimeRule
{
    [JsonPropertyName("engineName")]
    public string EngineName { get; set; } = "";

    [JsonPropertyName("grammageMin")]
    public int GrammageMin { get; set; } = 0;

    [JsonPropertyName("grammageMax")]
    public int GrammageMax { get; set; } = 999;

    [JsonPropertyName("timePerSheetSeconds")]
    public int TimePerSheetSeconds { get; set; } = 5;
}

public class GrammageTimeConfig
{
    [JsonPropertyName("rules")]
    public List<GrammageTimeRule> Rules { get; set; } = new();
}

public class JdfFieldConfig
{
    [JsonPropertyName("fieldId")]
    public string FieldId { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("included")]
    public bool Included { get; set; } = false;
}

public class JdfConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("fields")]
    public List<JdfFieldConfig> Fields { get; set; } = new();
}

public class DailyReportConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
    [JsonPropertyName("reportHour")]
    public int ReportHour { get; set; } = 18;
    [JsonPropertyName("reportMinute")]
    public int ReportMinute { get; set; } = 0;
    [JsonPropertyName("reportPath")]
    public string ReportPath { get; set; } = "";
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

public class BatPapierConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
    [JsonPropertyName("hotfolder")]
    public string Hotfolder { get; set; } = "";
}

public class FormFieldConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;

    [JsonPropertyName("readOnly")]
    public bool ReadOnly { get; set; } = false;

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("options")]
    public List<string>? Options { get; set; }

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("width")]
    public string? Width { get; set; } = "half";

    [JsonPropertyName("dependsOn")]
    public string? DependsOn { get; set; }

    [JsonPropertyName("dependsOnValue")]
    public string? DependsOnValue { get; set; }

    [JsonPropertyName("calculationRule")]
    public string? CalculationRule { get; set; }
}

public class FabricationFormConfig
{
    [JsonPropertyName("fields")]
    public List<FormFieldConfig> Fields { get; set; } = new();

    [JsonPropertyName("sections")]
    public List<string> Sections { get; set; } = new();
}
