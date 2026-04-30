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
                        if (data.TryGetProperty("dedupKey", out var dkEl)) cfg.XmlImport.DedupKey = dkEl.GetString() ?? "referenceCommande";
                        if (data.TryGetProperty("enabled", out var xeEl)) cfg.XmlImport.Enabled = xeEl.GetBoolean();
                        break;

                    case "erp":
                        cfg.Erp ??= new ErpConfig();
                        if (data.TryGetProperty("enabled", out var eeEl)) cfg.Erp.Enabled = eeEl.GetBoolean();
                        if (data.TryGetProperty("url", out var euEl)) cfg.Erp.Url = euEl.GetString() ?? "";
                        if (data.TryGetProperty("apiKey", out var eakEl) && !string.IsNullOrEmpty(eakEl.GetString())) cfg.Erp.ApiKey = eakEl.GetString()!;
                        if (data.TryGetProperty("login", out var elEl)) cfg.Erp.Login = elEl.GetString() ?? "";
                        if (data.TryGetProperty("password", out var epEl) && !string.IsNullOrEmpty(epEl.GetString())) cfg.Erp.Password = epEl.GetString()!;
                        if (data.TryGetProperty("format", out var efEl)) cfg.Erp.Format = efEl.GetString() ?? "xml";
                        if (data.TryGetProperty("intervalMinutes", out var eiEl)) cfg.Erp.IntervalMinutes = eiEl.GetInt32();
                        break;

                    case "pressero":
                        cfg.Pressero ??= new PresseroConfig();
                        if (data.TryGetProperty("enabled", out var peen)) cfg.Pressero.Enabled = peen.GetBoolean();
                        if (data.TryGetProperty("apiUrl", out var pau)) cfg.Pressero.ApiUrl = pau.GetString() ?? "";
                        if (data.TryGetProperty("apiKey", out var pak) && !string.IsNullOrEmpty(pak.GetString())) cfg.Pressero.ApiKey = pak.GetString()!;
                        if (data.TryGetProperty("apiSecret", out var pas) && !string.IsNullOrEmpty(pas.GetString())) cfg.Pressero.ApiSecret = pas.GetString()!;
                        if (data.TryGetProperty("webhookUrl", out var pwu)) cfg.Pressero.WebhookUrl = pwu.GetString() ?? "";
                        if (data.TryGetProperty("autoImport", out var pai)) cfg.Pressero.AutoImport = pai.GetBoolean();
                        break;

                    case "mdsf":
                        cfg.Mdsf ??= new MdsfConfig();
                        if (data.TryGetProperty("enabled", out var meen)) cfg.Mdsf.Enabled = meen.GetBoolean();
                        if (data.TryGetProperty("apiUrl", out var mau)) cfg.Mdsf.ApiUrl = mau.GetString() ?? "";
                        if (data.TryGetProperty("apiKey", out var mak) && !string.IsNullOrEmpty(mak.GetString())) cfg.Mdsf.ApiKey = mak.GetString()!;
                        if (data.TryGetProperty("storeId", out var msi)) cfg.Mdsf.StoreId = msi.GetString() ?? "";
                        if (data.TryGetProperty("autoImport", out var mai)) cfg.Mdsf.AutoImport = mai.GetBoolean();
                        if (data.TryGetProperty("intervalMinutes", out var mim)) cfg.Mdsf.IntervalMinutes = mim.GetInt32();
                        break;

                    case "export":
                        cfg.Export ??= new ExportConfig();
                        if (data.TryGetProperty("enableXml", out var exXml)) cfg.Export.EnableXml = exXml.GetBoolean();
                        if (data.TryGetProperty("enableCsv", out var exCsv)) cfg.Export.EnableCsv = exCsv.GetBoolean();
                        if (data.TryGetProperty("enableErp", out var exErp)) cfg.Export.EnableErp = exErp.GetBoolean();
                        if (data.TryGetProperty("enablePressero", out var exPressero)) cfg.Export.EnablePressero = exPressero.GetBoolean();
                        if (data.TryGetProperty("enableMdsf", out var exMdsf)) cfg.Export.EnableMdsf = exMdsf.GetBoolean();
                        if (data.TryGetProperty("csvSeparator", out var exSep)) cfg.Export.CsvSeparator = exSep.GetString() ?? ";";
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

                // Parse XML
                XDocument doc;
                using (var stream = file.OpenReadStream())
                    doc = XDocument.Load(stream);

                int imported = 0, updated = 0, duplicates = 0;
                var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
                var logCol = MongoDbHelper.GetCollection<BsonDocument>("integration_import_log");

                // Support both flat <Order> and wrapped <Orders><Order> structures
                var orderElements = doc.Descendants("Order")
                    .Concat(doc.Descendants("Commande"))
                    .Concat(doc.Descendants("Job"))
                    .Distinct()
                    .ToList();

                if (orderElements.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucun élément <Order>, <Commande> ou <Job> trouvé dans le XML" });

                foreach (var order in orderElements)
                {
                    var fiche = new BsonDocument();

                    // Apply mapping: ficheField (kv.Key) → xmlTagName (kv.Value)
                    foreach (var kv in mapping)
                    {
                        var ficheField = kv.Key;   // target field in the fiche document
                        var xmlTag = kv.Value;      // source XML element name
                        var el = order.Element(xmlTag) ?? order.Descendants(xmlTag).FirstOrDefault();
                        if (el != null)
                            fiche[ficheField] = el.Value;
                    }

                    // Also try direct field names as fallback
                    foreach (var el in order.Elements())
                    {
                        if (!fiche.Contains(el.Name.LocalName))
                            fiche[el.Name.LocalName] = el.Value;
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
