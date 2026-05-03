using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

public static class IntegrationsEndpoints
{
    public static void MapIntegrationsEndpoints(this WebApplication app)
    {
        // ── Auth helpers ──────────────────────────────────────────────────────
        static bool IsAdmin(HttpContext ctx)
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                return parts.Length >= 3 && parts[2] == "3";
            }
            catch { return false; }
        }

        // ── GET /api/settings/integrations-config ─────────────────────────
        app.MapGet("/api/settings/integrations-config", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                          ?? new IntegrationsFullConfig();
                // Never expose secrets (nullify passwords in response)
                var safeXml = new { mapping = cfg.XmlImport?.Mapping, dedupKey = cfg.XmlImport?.DedupKey, enabled = cfg.XmlImport?.Enabled };
                var safeErp = new { enabled = cfg.Erp?.Enabled, url = cfg.Erp?.Url, login = cfg.Erp?.Login,
                                    format = cfg.Erp?.Format, intervalMinutes = cfg.Erp?.IntervalMinutes };
                var safePressero = new { enabled = cfg.Pressero?.Enabled, apiUrl = cfg.Pressero?.ApiUrl,
                                        webhookUrl = cfg.Pressero?.WebhookUrl, autoImport = cfg.Pressero?.AutoImport };
                var safeMdsf = new { enabled = cfg.Mdsf?.Enabled, apiUrl = cfg.Mdsf?.ApiUrl,
                                     storeId = cfg.Mdsf?.StoreId, autoImport = cfg.Mdsf?.AutoImport,
                                     intervalMinutes = cfg.Mdsf?.IntervalMinutes };
                var safeExport = new { enableXml = cfg.Export?.EnableXml, enableCsv = cfg.Export?.EnableCsv,
                                       enableErp = cfg.Export?.EnableErp, enablePressero = cfg.Export?.EnablePressero,
                                       enableMdsf = cfg.Export?.EnableMdsf, csvSeparator = cfg.Export?.CsvSeparator,
                                       mapping = cfg.Export?.Mapping };
                return Results.Json(new { ok = true, config = new {
                    xmlImport = safeXml, erp = safeErp, pressero = safePressero, mdsf = safeMdsf, export = safeExport
                }});
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/settings/integrations-config ─────────────────────────
        app.MapPut("/api/settings/integrations-config", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var section = body.TryGetProperty("section", out var secEl) ? secEl.GetString() ?? "" : "";
                var data = body.TryGetProperty("data", out var dataEl) ? dataEl : default;

                var cfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                          ?? new IntegrationsFullConfig();

                switch (section)
                {
                    case "xmlImport":
                        cfg.XmlImport ??= new XmlImportConfig();
                        if (data.TryGetProperty("mapping", out var mapEl) && mapEl.ValueKind == JsonValueKind.Object)
                        {
                            cfg.XmlImport.Mapping = new Dictionary<string, string>();
                            foreach (var prop in mapEl.EnumerateObject())
                                cfg.XmlImport.Mapping[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        { var s = TryGetString(data, "dedupKey"); if (s != null) cfg.XmlImport.DedupKey = s; }
                        { var v = TryGetBool(data, "enabled"); if (v.HasValue) cfg.XmlImport.Enabled = v.Value; }
                        break;

                    case "erp":
                        cfg.Erp ??= new ErpConfig();
                        { var v = TryGetBool(data, "enabled"); if (v.HasValue) cfg.Erp.Enabled = v.Value; }
                        { var s = TryGetString(data, "url"); if (s != null) cfg.Erp.Url = s; }
                        { var s = TryGetString(data, "apiKey"); if (!string.IsNullOrEmpty(s)) cfg.Erp.ApiKey = s!; }
                        { var s = TryGetString(data, "login"); if (s != null) cfg.Erp.Login = s; }
                        { var s = TryGetString(data, "password"); if (!string.IsNullOrEmpty(s)) cfg.Erp.Password = s!; }
                        { var s = TryGetString(data, "format"); if (s != null) cfg.Erp.Format = s; }
                        { var i = TryGetInt(data, "intervalMinutes"); if (i.HasValue) cfg.Erp.IntervalMinutes = i.Value; }
                        break;

                    case "pressero":
                        cfg.Pressero ??= new PresseroConfig();
                        { var v = TryGetBool(data, "enabled"); if (v.HasValue) cfg.Pressero.Enabled = v.Value; }
                        { var s = TryGetString(data, "apiUrl"); if (s != null) cfg.Pressero.ApiUrl = s; }
                        { var s = TryGetString(data, "apiKey"); if (!string.IsNullOrEmpty(s)) cfg.Pressero.ApiKey = s!; }
                        { var s = TryGetString(data, "apiSecret"); if (!string.IsNullOrEmpty(s)) cfg.Pressero.ApiSecret = s!; }
                        { var s = TryGetString(data, "webhookUrl"); if (s != null) cfg.Pressero.WebhookUrl = s; }
                        { var v = TryGetBool(data, "autoImport"); if (v.HasValue) cfg.Pressero.AutoImport = v.Value; }
                        break;

                    case "mdsf":
                        cfg.Mdsf ??= new MdsfConfig();
                        { var v = TryGetBool(data, "enabled"); if (v.HasValue) cfg.Mdsf.Enabled = v.Value; }
                        { var s = TryGetString(data, "apiUrl"); if (s != null) cfg.Mdsf.ApiUrl = s; }
                        { var s = TryGetString(data, "apiKey"); if (!string.IsNullOrEmpty(s)) cfg.Mdsf.ApiKey = s!; }
                        { var s = TryGetString(data, "storeId"); if (s != null) cfg.Mdsf.StoreId = s; }
                        { var v = TryGetBool(data, "autoImport"); if (v.HasValue) cfg.Mdsf.AutoImport = v.Value; }
                        { var i = TryGetInt(data, "intervalMinutes"); if (i.HasValue) cfg.Mdsf.IntervalMinutes = i.Value; }
                        break;

                    case "activeProvider":
                        // Enforce mutual exclusivity: only one provider can be enabled
                        var activeProviderVal = TryGetString(data, "provider") ?? "none";
                        cfg.ActiveProvider = activeProviderVal;
                        // Disable all other providers when one is selected
                        if (cfg.Erp     != null) cfg.Erp.Enabled      = activeProviderVal == "erp";
                        if (cfg.Pressero != null) cfg.Pressero.Enabled = activeProviderVal == "pressero";
                        if (cfg.Mdsf    != null) cfg.Mdsf.Enabled     = activeProviderVal == "mdsf";
                        break;

                    case "export":
                        cfg.Export ??= new ExportConfig();
                        { var v = TryGetBool(data, "enableXml"); if (v.HasValue) cfg.Export.EnableXml = v.Value; }
                        { var v = TryGetBool(data, "enableCsv"); if (v.HasValue) cfg.Export.EnableCsv = v.Value; }
                        { var v = TryGetBool(data, "enableErp"); if (v.HasValue) cfg.Export.EnableErp = v.Value; }
                        { var v = TryGetBool(data, "enablePressero"); if (v.HasValue) cfg.Export.EnablePressero = v.Value; }
                        { var v = TryGetBool(data, "enableMdsf"); if (v.HasValue) cfg.Export.EnableMdsf = v.Value; }
                        { var s = TryGetString(data, "csvSeparator"); if (s != null) cfg.Export.CsvSeparator = s; }
                        if (data.TryGetProperty("mapping", out var exMap) && exMap.ValueKind == JsonValueKind.Object)
                        {
                            cfg.Export.Mapping = new Dictionary<string, string>();
                            foreach (var prop in exMap.EnumerateObject())
                                cfg.Export.Mapping[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        break;
                }

                MongoDbHelper.UpsertSettings("integrations_full_config", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── POST /api/settings/integrations/test-connection ───────────────
        app.MapPost("/api/settings/integrations/test-connection", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var source = body.TryGetProperty("source", out var srcEl) ? srcEl.GetString() ?? "" : "";
                var url    = body.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                var apiKey = body.TryGetProperty("apiKey", out var akEl) ? akEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(url))
                    return Results.Json(new { ok = false, error = "URL requise" });

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                if (!string.IsNullOrEmpty(apiKey))
                    http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                try
                {
                    var response = await http.GetAsync(url);
                    return Results.Json(new { ok = response.IsSuccessStatusCode,
                        message = $"HTTP {(int)response.StatusCode} {response.StatusCode}",
                        error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}" });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { ok = false, error = ex.Message });
                }
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── POST /api/integrations/import-xml ────────────────────────────
        app.MapPost("/api/integrations/import-xml", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || (parts[2] != "3" && parts[2] != "2"))
                    return Results.Json(new { ok = false, error = "Accès refusé" });

                if (!ctx.Request.HasFormContentType)
                    return Results.Json(new { ok = false, error = "Multipart form requis" });

                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.GetFile("file");
                if (file == null || file.Length == 0)
                    return Results.Json(new { ok = false, error = "Fichier XML requis" });

                // Load mapping config
                var cfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                          ?? new IntegrationsFullConfig();
                var mapping = cfg.XmlImport?.Mapping ?? new Dictionary<string, string>();
                var dedupKey = cfg.XmlImport?.DedupKey ?? "referenceCommande";

                // Parse XML (handles BOM + ISO-8859-1 / Windows-1252 encoding)
                XDocument doc;
                try
                {
                    using var stream = file.OpenReadStream();
                    doc = GestionAtelier.Services.XmlParserHelper.LoadSafely(stream);
                }
                catch (System.Xml.XmlException xmlEx)
                {
                    return Results.Json(new { ok = false, error = $"Fichier XML invalide ({file.FileName}) : {xmlEx.Message}" });
                }

                int imported = 0, updated = 0, duplicates = 0;
                var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
                var logCol = MongoDbHelper.GetCollection<BsonDocument>("integration_import_log");

                // Detect MasterPrint format or fall back to generic element search
                List<XElement> orderElements;
                bool isMasterPrint = GestionAtelier.Services.XmlParserHelper.IsMasterPrint(doc);
                if (isMasterPrint)
                {
                    orderElements = doc.Descendants("Commande").ToList();
                }
                else
                {
                    orderElements = doc.Descendants("Order")
                        .Concat(doc.Descendants("Commande"))
                        .Concat(doc.Descendants("Job"))
                        .Distinct()
                        .ToList();
                }

                if (orderElements.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucun élément <Order>, <Commande> ou <Job> trouvé dans le XML" });

                foreach (var order in orderElements)
                {
                    var fiche = new BsonDocument();

                    if (isMasterPrint)
                    {
                        // Use the dedicated MasterPrint parser
                        var parsed = GestionAtelier.Services.XmlParserHelper.ParseMasterPrintCommande(order);
                        foreach (var kv in parsed)
                            fiche[kv.Key] = kv.Value;
                    }
                    else
                    {
                        // Apply configured mapping: ficheField (kv.Key) → xmlTagName (kv.Value)
                        foreach (var kv in mapping)
                        {
                            var ficheField = kv.Key;
                            var xmlTag = kv.Value;
                            var el = order.Element(xmlTag) ?? order.Descendants(xmlTag).FirstOrDefault();
                            if (el != null)
                                fiche[ficheField] = el.Value;
                        }

                        // Also try direct field names as fallback (scalar elements only)
                        foreach (var el in order.Elements())
                        {
                            if (!fiche.Contains(el.Name.LocalName) && !el.HasElements)
                                fiche[el.Name.LocalName] = el.Value;
                        }
                    }

                    fiche["importedAt"] = DateTime.UtcNow.ToString("O");
                    fiche["importSource"] = "xml";

                    // Dedup check
                    var dedupValue = fiche.Contains(dedupKey) ? fiche[dedupKey].AsString : null;
                    bool isUpdate = false;
                    if (!string.IsNullOrEmpty(dedupValue))
                    {
                        var existing = fabCol.Find(
                            Builders<BsonDocument>.Filter.Eq(dedupKey, dedupValue)
                        ).FirstOrDefault();

                        if (existing != null)
                        {
                            // Update existing
                            fiche["_id"] = existing["_id"];
                            fiche["updatedAt"] = DateTime.UtcNow.ToString("O");
                            fabCol.ReplaceOne(Builders<BsonDocument>.Filter.Eq("_id", existing["_id"]), fiche);
                            updated++;
                            isUpdate = true;
                        }
                    }

                    if (!isUpdate)
                    {
                        fabCol.InsertOne(fiche);
                        imported++;
                    }

                    // Log entry
                    logCol.InsertOne(new BsonDocument
                    {
                        ["timestamp"] = DateTime.UtcNow.ToString("O"),
                        ["source"] = "xml",
                        ["fileName"] = file.FileName,
                        ["status"] = isUpdate ? "update" : "ok",
                        ["message"] = isUpdate
                            ? $"Mise à jour ({dedupKey}={dedupValue})"
                            : $"Import ({dedupKey}={dedupValue ?? "—"})"
                    });
                }

                return Results.Json(new { ok = true, imported, updated, duplicates });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── GET /api/integrations/import-log ────────────────────────────
        app.MapGet("/api/integrations/import-log", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || parts[2] != "3")
                    return Results.Json(new { ok = false, error = "Admin only" });

                var limitStr = ctx.Request.Query["limit"].ToString();
                var limit = int.TryParse(limitStr, out var l) ? l : 50;

                var logCol = MongoDbHelper.GetCollection<BsonDocument>("integration_import_log");
                var logs = logCol.Find(new BsonDocument())
                    .SortByDescending(x => x["_id"])
                    .Limit(limit)
                    .ToList()
                    .Select(d => new {
                        timestamp = d.Contains("timestamp") ? d["timestamp"].AsString : "",
                        source = d.Contains("source") ? d["source"].AsString : "",
                        fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
                        status = d.Contains("status") ? d["status"].AsString : "",
                        message = d.Contains("message") ? d["message"].AsString : ""
                    });

                return Results.Json(new { ok = true, logs });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/export-log ────────────────────────────
        app.MapGet("/api/integrations/export-log", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || parts[2] != "3")
                    return Results.Json(new { ok = false, error = "Admin only" });

                var limitStr = ctx.Request.Query["limit"].ToString();
                var limit = int.TryParse(limitStr, out var l) ? l : 50;

                var logCol = MongoDbHelper.GetCollection<BsonDocument>("integration_export_log");
                var logs = logCol.Find(new BsonDocument())
                    .SortByDescending(x => x["_id"])
                    .Limit(limit)
                    .ToList()
                    .Select(d => new {
                        timestamp = d.Contains("timestamp") ? d["timestamp"].AsString : "",
                        format = d.Contains("format") ? d["format"].AsString : "",
                        destination = d.Contains("destination") ? d["destination"].AsString : "",
                        status = d.Contains("status") ? d["status"].AsString : "",
                        message = d.Contains("message") ? d["message"].AsString : ""
                    });

                return Results.Json(new { ok = true, logs });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/export ─────────────────────────────────
        // Export commandes en XML ou CSV (téléchargement)
        app.MapGet("/api/integrations/export", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || (parts[2] != "3" && parts[2] != "2" && parts[2] != "1"))
                    return Results.Json(new { ok = false, error = "Accès refusé" });

                var format = ctx.Request.Query["format"].ToString().ToLower();
                if (format != "xml" && format != "csv") format = "xml";

                var limitStr = ctx.Request.Query["limit"].ToString();
                var limit = int.TryParse(limitStr, out var l) && l > 0 ? l : 100;

                // Load export mapping config
                var cfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                          ?? new IntegrationsFullConfig();
                var mapping = cfg.Export?.Mapping ?? new Dictionary<string, string>();
                var separator = cfg.Export?.CsvSeparator ?? ";";
                if (separator == "\\t") separator = "\t";

                // Load fabrication data
                var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
                var fiches = fabCol.Find(new BsonDocument())
                    .SortByDescending(x => x["_id"])
                    .Limit(limit)
                    .ToList();

                // Standard export fields
                var FIELDS = new[] {
                    "numeroDossier","client","nomClient","typeTravail","quantite","formatFini",
                    "moteurImpression","operateur","dateReceptionSouhaitee","dateLivraisonSouhaitee",
                    "retraitLivraison","commentaire","referenceCommande"
                };

                string GetFieldName(string f) => mapping.ContainsKey(f) && !string.IsNullOrEmpty(mapping[f]) ? mapping[f] : f;
                string GetValue(BsonDocument doc, string f) => doc.Contains(f) ? (doc[f]?.ToString() ?? "") : "";

                byte[] content;
                string contentType;
                string fileName;

                if (format == "csv")
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(string.Join(separator, FIELDS.Select(f => $"\"{GetFieldName(f)}\"")));
                    foreach (var doc in fiches)
                    {
                        sb.AppendLine(string.Join(separator, FIELDS.Select(f => $"\"{GetValue(doc, f).Replace("\"", "\"\"")}\"")));
                    }
                    content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                    contentType = "text/csv; charset=utf-8";
                    fileName = $"export-commandes-{DateTime.Now:yyyyMMdd-HHmm}.csv";
                }
                else
                {
                    var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", null));
                    var root = new XElement("Commandes");
                    foreach (var doc in fiches)
                    {
                        var order = new XElement("Commande");
                        foreach (var f in FIELDS)
                            order.Add(new XElement(GetFieldName(f), GetValue(doc, f)));
                        root.Add(order);
                    }
                    xDoc.Add(root);
                    using var ms = new MemoryStream();
                    xDoc.Save(ms);
                    content = ms.ToArray();
                    contentType = "application/xml; charset=utf-8";
                    fileName = $"export-commandes-{DateTime.Now:yyyyMMdd-HHmm}.xml";
                }

                // Log export
                try
                {
                    var logCol = MongoDbHelper.GetCollection<BsonDocument>("integration_export_log");
                    logCol.InsertOne(new BsonDocument
                    {
                        ["timestamp"] = DateTime.UtcNow.ToString("O"),
                        ["format"] = format.ToUpper(),
                        ["destination"] = "téléchargement",
                        ["status"] = "ok",
                        ["message"] = $"{fiches.Count} commande(s) exportée(s)"
                    });
                }
                catch { /* log failure is non-critical */ }

                ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                return Results.File(content, contentType, fileName);
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }

    // ── Null-safe JSON helpers ────────────────────────────────────────────────
    private static bool? TryGetBool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        return null;
    }

    private static string? TryGetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        return null;
    }

    private static int? TryGetInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)) return v;
        return null;
    }
}

// ── Data models ──────────────────────────────────────────────────────────────

public class IntegrationsFullConfig
{
    [JsonPropertyName("xmlImport")]
    public XmlImportConfig? XmlImport { get; set; }

    [JsonPropertyName("erp")]
    public ErpConfig? Erp { get; set; }

    [JsonPropertyName("pressero")]
    public PresseroConfig? Pressero { get; set; }

    [JsonPropertyName("mdsf")]
    public MdsfConfig? Mdsf { get; set; }

    [JsonPropertyName("export")]
    public ExportConfig? Export { get; set; }

    /// <summary>Which provider is active ("none" | "erp" | "pressero" | "mdsf").
    /// Only the active provider may have <c>Enabled = true</c>.</summary>
    [JsonPropertyName("activeProvider")]
    public string ActiveProvider { get; set; } = "none";
}

public class XmlImportConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("mapping")]
    public Dictionary<string, string> Mapping { get; set; } = new();

    [JsonPropertyName("dedupKey")]
    public string DedupKey { get; set; } = "referenceCommande";
}

public class ErpConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("login")]
    public string Login { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("format")]
    public string Format { get; set; } = "xml";

    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; } = 60;
}

public class PresseroConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("apiUrl")]
    public string ApiUrl { get; set; } = "";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("apiSecret")]
    public string ApiSecret { get; set; } = "";

    [JsonPropertyName("webhookUrl")]
    public string WebhookUrl { get; set; } = "";

    [JsonPropertyName("autoImport")]
    public bool AutoImport { get; set; }
}

public class MdsfConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("apiUrl")]
    public string ApiUrl { get; set; } = "";

    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    [JsonPropertyName("storeId")]
    public string StoreId { get; set; } = "";

    [JsonPropertyName("autoImport")]
    public bool AutoImport { get; set; }

    [JsonPropertyName("intervalMinutes")]
    public int IntervalMinutes { get; set; } = 30;
}

public class ExportConfig
{
    [JsonPropertyName("enableXml")]
    public bool EnableXml { get; set; } = true;

    [JsonPropertyName("enableCsv")]
    public bool EnableCsv { get; set; } = true;

    [JsonPropertyName("enableErp")]
    public bool EnableErp { get; set; }

    [JsonPropertyName("enablePressero")]
    public bool EnablePressero { get; set; }

    [JsonPropertyName("enableMdsf")]
    public bool EnableMdsf { get; set; }

    [JsonPropertyName("csvSeparator")]
    public string CsvSeparator { get; set; } = ";";

    [JsonPropertyName("mapping")]
    public Dictionary<string, string> Mapping { get; set; } = new();
}
