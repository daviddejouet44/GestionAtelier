using System.IO;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class SimpleStringListSettings
{
    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = new();
}

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

public class AppPathSettings
{
    public static string DefaultPrismaTempCopyPath =>
        Path.Combine(GestionAtelier.Services.BackendUtils.HotfoldersRoot(), "TEMP_COPY_Prepare");
    public static string DefaultPrismaTargetPath =>
        Path.Combine(GestionAtelier.Services.BackendUtils.HotfoldersRoot(), "PrismaPrepare");

    [JsonPropertyName("prisma_temp_copy_path")]
    public string PrismaTempCopyPath { get; set; } = DefaultPrismaTempCopyPath;

    [JsonPropertyName("prisma_target_path")]
    public string PrismaTargetPath { get; set; } = DefaultPrismaTargetPath;
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

public class ExternalLinksSettings
{
    [JsonPropertyName("remoteManagerUrl")]
    public string RemoteManagerUrl { get; set; } = "";

    [JsonPropertyName("primalyticsUrl")]
    public string PrismalyticsUrl { get; set; } = "";
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

public class AutoPreflightSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("minimumBleedMm")]
    public double? MinimumBleedMm { get; set; }

    [JsonPropertyName("minimumImageDpi")]
    public int? MinimumImageDpi { get; set; }

    [JsonPropertyName("maximumTacPercent")]
    public double? MaximumTacPercent { get; set; }
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

    [JsonPropertyName("emailTemplateKeys")]
    public List<string>? EmailTemplateKeys { get; set; }
}

public class KanbanSettings
{
    [JsonPropertyName("columns")]
    public List<KanbanColumnConfig> Columns { get; set; } = new();
}

public class KeyDatesOffsetsSettings
{
    [JsonPropertyName("livraisonEnvoiHeures")]
    public int LivraisonEnvoiHeures { get; set; } = 48;

    [JsonPropertyName("livraisonFinitionsHeures")]
    public int LivraisonFinitionsHeures { get; set; } = 72;

    [JsonPropertyName("livraisonImpressionHeures")]
    public int LivraisonImpressionHeures { get; set; } = 96;

    [JsonPropertyName("retraitEnvoiHeures")]
    public int RetraitEnvoiHeures { get; set; } = 0;

    [JsonPropertyName("retraitFinitionsHeures")]
    public int RetraitFinitionsHeures { get; set; } = 24;

    [JsonPropertyName("retraitImpressionHeures")]
    public int RetraitImpressionHeures { get; set; } = 48;
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

public class BatValidationLinkConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("tokenExpiryHours")]
    public int TokenExpiryHours { get; set; } = 72;

    [JsonPropertyName("publicBaseUrl")]
    public string PublicBaseUrl { get; set; } = "";

    [JsonPropertyName("bodyTemplate")]
    public string BodyTemplate { get; set; } = "Bonjour,\n\nVeuillez consulter votre BAT et nous indiquer votre décision via ce lien :\n\n{{batLink}}\n\nCe lien est valable {{expiryHours}}h.\n\nCordialement";

    [JsonPropertyName("subjectTemplate")]
    public string SubjectTemplate { get; set; } = "Validation BAT — {{fileName}}";
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

    /// <summary>Sub-options per parent option value: { "Option A": ["Sub 1", "Sub 2"] }</summary>
    [JsonPropertyName("subOptions")]
    public Dictionary<string, List<string>>? SubOptions { get; set; }

    [JsonPropertyName("section")]
    public string? Section { get; set; }

    [JsonPropertyName("width")]
    public string? Width { get; set; } = "half";

    [JsonPropertyName("dependsOn")]
    public string? DependsOn { get; set; }

    [JsonPropertyName("dependsOnValue")]
    public string? DependsOnValue { get; set; }

    /// <summary>Show this field when the parent field's value matches ANY of these values (OR logic).</summary>
    [JsonPropertyName("dependsOnValues")]
    public List<string>? DependsOnValues { get; set; }

    [JsonPropertyName("calculationRule")]
    public string? CalculationRule { get; set; }

    /// <summary>Custom field added by admin — may be deleted via the settings UI.</summary>
    [JsonPropertyName("isCustom")]
    public bool IsCustom { get; set; } = false;

    /// <summary>When true, this field is only shown for "fiches sans PDF" (created via the
    /// blank-fiche submission process). It stays hidden for regular jobs.</summary>
    [JsonPropertyName("sansPdfOnly")]
    public bool SansPdfOnly { get; set; } = false;
}

/// <summary>Admin-configurable substitution PDF used as a placeholder thumbnail when a
/// production sheet is created without importing a real PDF ("fiche sans PDF").</summary>
public class SubstitutionPdfSettings
{
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class FabricationFormConfig
{
    [JsonPropertyName("fields")]
    public List<FormFieldConfig> Fields { get; set; } = new();

    [JsonPropertyName("sections")]
    public List<string> Sections { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Finition time rules — time added to production when a finition is selected
// ──────────────────────────────────────────────────────────────────────────────
public class FinitionTimeRule
{
    [JsonPropertyName("finitionName")]
    public string FinitionName { get; set; } = "";

    [JsonPropertyName("timeMinutes")]
    public int TimeMinutes { get; set; } = 0;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class FinitionTimeConfig
{
    [JsonPropertyName("rules")]
    public List<FinitionTimeRule> Rules { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Finition sheet formulas — per-finition sheet count overrides
// Example: "Dorure à chaud" on "Brochure" = 1 sheet (not all sheets)
// ──────────────────────────────────────────────────────────────────────────────
public class FinitionSheetFormula
{
    [JsonPropertyName("finitionName")]
    public string FinitionName { get; set; } = "";

    /// <summary>Optional: restrict this formula to a specific work type. Empty = applies to all.</summary>
    [JsonPropertyName("typeTravail")]
    public string? TypeTravail { get; set; }

    /// <summary>Fixed number of sheets for this finition (overrides the normal sheet count calculation).</summary>
    [JsonPropertyName("sheetsOverride")]
    public int SheetsOverride { get; set; } = 1;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class FinitionSheetFormulaConfig
{
    [JsonPropertyName("formulas")]
    public List<FinitionSheetFormula> Formulas { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Rainage options — admin-defined list of rainage types
// ──────────────────────────────────────────────────────────────────────────────
public class RainageOptionsConfig
{
    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Custom paper catalog entries (stored in MongoDB, merged with XML catalog)
// ──────────────────────────────────────────────────────────────────────────────
public class CustomPaperEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("grammage")]
    public string? Grammage { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("fabricant")]
    public string? Fabricant { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class CustomPaperCatalog
{
    [JsonPropertyName("papers")]
    public List<CustomPaperEntry> Papers { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Portal: client-facing steps (Kanban tile → stage visible to the client)
// ──────────────────────────────────────────────────────────────────────────────
public class PortalClientStep
{
    [JsonPropertyName("kanbanFolder")]
    public string KanbanFolder { get; set; } = "";

    /// <summary>Label shown to the client (can differ from the internal Kanban tile name).</summary>
    [JsonPropertyName("clientLabel")]
    public string ClientLabel { get; set; } = "";

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = false;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    /// <summary>Optional email template key to send when an order reaches this step.</summary>
    [JsonPropertyName("emailTemplateKey")]
    public string EmailTemplateKey { get; set; } = "";
}

public class PortalClientStepsConfig
{
    [JsonPropertyName("steps")]
    public List<PortalClientStep> Steps { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Production delay alert configuration (admin-customizable)
// ──────────────────────────────────────────────────────────────────────────────
public class ProductionDelayAlertConfig
{
    /// <summary>Whether the production delay alert is active.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Minimum number of days late before a job appears in the alert (0 = any past date).</summary>
    [JsonPropertyName("delayThresholdDays")]
    public int DelayThresholdDays { get; set; } = 0;

    /// <summary>Custom title shown in the sidebar/bandeau for the alert section.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Retard de production";

    /// <summary>Maximum number of jobs displayed per machine group before "+n autres".</summary>
    [JsonPropertyName("maxJobsPerGroup")]
    public int MaxJobsPerGroup { get; set; } = 3;

    /// <summary>If set, only show alerts for jobs assigned to these machines (empty = all machines).</summary>
    [JsonPropertyName("filterMachines")]
    public List<string> FilterMachines { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// Kanban action menu configuration (which actions are shown in the Action dropdown)
// ──────────────────────────────────────────────────────────────────────────────
public class KanbanAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class KanbanActionsConfig
{
    [JsonPropertyName("actions")]
    public List<KanbanAction> Actions { get; set; } = new();
}
