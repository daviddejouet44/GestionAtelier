using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class ClientAccount
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "";

    [JsonPropertyName("contactPhone")]
    public string ContactPhone { get; set; } = "";

    [JsonPropertyName("defaultDeliveryAddress")]
    public string DefaultDeliveryAddress { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    [JsonPropertyName("failedLoginAttempts")]
    public int FailedLoginAttempts { get; set; } = 0;

    [JsonPropertyName("lockedUntil")]
    public DateTime? LockedUntil { get; set; }

    [JsonPropertyName("passwordResetToken")]
    public string? PasswordResetToken { get; set; }

    [JsonPropertyName("passwordResetExpiry")]
    public DateTime? PasswordResetExpiry { get; set; }
}

public class ClientOrder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("clientAccountId")]
    public string ClientAccountId { get; set; } = "";

    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; } = 0;

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("paper")]
    public string Paper { get; set; } = "";

    [JsonPropertyName("recto")]
    public string Recto { get; set; } = "recto"; // "recto" | "recto-verso"

    [JsonPropertyName("finitions")]
    public List<string> Finitions { get; set; } = new();

    [JsonPropertyName("desiredDeliveryDate")]
    public DateTime? DesiredDeliveryDate { get; set; }

    [JsonPropertyName("deliveryMode")]
    public string DeliveryMode { get; set; } = "retrait"; // "livraison" | "retrait"

    [JsonPropertyName("deliveryAddress")]
    public string DeliveryAddress { get; set; } = "";

    [JsonPropertyName("comments")]
    public string Comments { get; set; } = "";

    [JsonPropertyName("files")]
    public List<ClientOrderFile> Files { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft"; // draft, received, in_production, bat_pending, completed, delivered

    [JsonPropertyName("atelierJobPath")]
    public string AtelierJobPath { get; set; } = ""; // path in hotfolder

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("statusHistory")]
    public List<ClientOrderStatusEntry> StatusHistory { get; set; } = new();
}

public class ClientOrderFile
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("storedPath")]
    public string StoredPath { get; set; } = "";

    [JsonPropertyName("uploadedAt")]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("size")]
    public long Size { get; set; } = 0;
}

public class ClientOrderStatusEntry
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";
}

public class ClientBatAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("batFileRef")]
    public string BatFileRef { get; set; } = "";

    [JsonPropertyName("batFileName")]
    public string BatFileName { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "pending"; // "pending" | "validated" | "refused"

    [JsonPropertyName("motif")]
    public string Motif { get; set; } = "";

    [JsonPropertyName("attachmentRef")]
    public string AttachmentRef { get; set; } = "";

    [JsonPropertyName("attachmentName")]
    public string AttachmentName { get; set; } = "";

    [JsonPropertyName("performedAt")]
    public DateTime? PerformedAt { get; set; }

    [JsonPropertyName("performedByClientId")]
    public string PerformedByClientId { get; set; } = "";

    [JsonPropertyName("sentAt")]
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("notificationEmailSent")]
    public bool NotificationEmailSent { get; set; } = false;
}

public class PortalSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("portalUrl")]
    public string PortalUrl { get; set; } = "";

    [JsonPropertyName("welcomeText")]
    public string WelcomeText { get; set; } = "Bienvenue sur votre espace client.";

    [JsonPropertyName("maxUploadSizeMb")]
    public int MaxUploadSizeMb { get; set; } = 500;

    [JsonPropertyName("maxFilesPerOrder")]
    public int MaxFilesPerOrder { get; set; } = 10;

    [JsonPropertyName("acceptedFormats")]
    public List<string> AcceptedFormats { get; set; } = new() { ".pdf" };

    [JsonPropertyName("availableFormats")]
    public List<string> AvailableFormats { get; set; } = new() { "A4", "A3", "A5", "210x297", "148x210", "100x148", "DL", "Personnalisé" };

    [JsonPropertyName("availablePapers")]
    public List<string> AvailablePapers { get; set; } = new() { "Couché mat 135g", "Couché brillant 135g", "Couché mat 170g", "Offset 80g", "Offset 90g", "Offset 120g", "Offset 160g" };

    [JsonPropertyName("availableFinitions")]
    public List<string> AvailableFinitions { get; set; } = new() { "Pelliculage mat", "Pelliculage brillant", "Rainage", "Façonnage", "Dorure" };

    [JsonPropertyName("maxLoginAttempts")]
    public int MaxLoginAttempts { get; set; } = 5;

    [JsonPropertyName("lockDurationMinutes")]
    public int LockDurationMinutes { get; set; } = 30;

    [JsonPropertyName("webOrderKanbanFolder")]
    public string WebOrderKanbanFolder { get; set; } = "Commandes web";

    [JsonPropertyName("smtpFrom")]
    public string SmtpFrom { get; set; } = "";
}

public class PortalThemeConfig
{
    [JsonPropertyName("primaryColor")]
    public string PrimaryColor { get; set; } = "#1d4ed8";

    [JsonPropertyName("primaryDarkColor")]
    public string PrimaryDarkColor { get; set; } = "#1e40af";

    [JsonPropertyName("primaryLightColor")]
    public string PrimaryLightColor { get; set; } = "#eff6ff";

    [JsonPropertyName("backgroundColor")]
    public string BackgroundColor { get; set; } = "#f9fafb";

    [JsonPropertyName("textColor")]
    public string TextColor { get; set; } = "#374151";

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "system-ui";

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = "Espace client";

    [JsonPropertyName("tagline")]
    public string Tagline { get; set; } = "";

    [JsonPropertyName("contactLink")]
    public string ContactLink { get; set; } = "";

    [JsonPropertyName("footerText")]
    public string FooterText { get; set; } = "";

    [JsonPropertyName("loginBackground")]
    public string LoginBackground { get; set; } = "";

    [JsonPropertyName("ordersPageText")]
    public string OrdersPageText { get; set; } = "";

    [JsonPropertyName("customCss")]
    public string CustomCss { get; set; } = "";

    [JsonPropertyName("usePortalSpecificLogo")]
    public bool UsePortalSpecificLogo { get; set; } = false;

    [JsonPropertyName("portalLogoPath")]
    public string PortalLogoPath { get; set; } = "";
}

public class PortalFormFieldConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("customLabel")]
    public string CustomLabel { get; set; } = "";

    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("visible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;

    [JsonPropertyName("order")]
    public int Order { get; set; } = 0;

    [JsonPropertyName("defaultValue")]
    public string DefaultValue { get; set; } = "";

    [JsonPropertyName("allowedValues")]
    public List<string> AllowedValues { get; set; } = new();

    [JsonPropertyName("critical")]
    public bool Critical { get; set; } = false;
}

public class PortalFormFieldsConfig
{
    [JsonPropertyName("fields")]
    public List<PortalFormFieldConfig> Fields { get; set; } = new();
}
