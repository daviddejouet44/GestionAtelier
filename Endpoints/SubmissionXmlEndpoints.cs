using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Xml.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;
using GestionAtelier.Endpoints.Settings;

namespace GestionAtelier.Endpoints;

public static class SubmissionXmlEndpoints
{
    public static void MapSubmissionXmlEndpoints(this WebApplication app)
    {
        // ── Auth helpers ──────────────────────────────────────────────────────
        static bool IsAuthenticated(HttpContext ctx)
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                return decoded.Split(':').Length >= 3;
            }
            catch { return false; }
        }

        // ── GET /api/soumission/upload-with-xml (informational — only POST is supported)
        app.MapGet("/api/soumission/upload-with-xml", () =>
            Results.Json(new
            {
                ok    = false,
                error = "Cet endpoint n'accepte que la méthode POST avec un formulaire multipart (pdf + xml). " +
                        "Utilisez la zone de dépôt de l'onglet Soumission pour importer vos fichiers."
            }));

        // ── POST /api/soumission/upload-with-xml ─────────────────────────────
        // Accepts: multipart form with pdf[] (one or more) + xml (one)
        // Returns: { ok, fichePrefill, jobIds[] }
        app.MapPost("/api/soumission/upload-with-xml", async (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx))
                return Results.Json(new { ok = false, error = "Authentification requise" });

            try
            {
                if (!ctx.Request.HasFormContentType)
                    return Results.Json(new { ok = false, error = "Multipart form requis" });

                var form = await ctx.Request.ReadFormAsync();

                // Collect PDFs and XML files
                var pdfFiles  = form.Files.Where(f => f.FileName.ToLowerInvariant().EndsWith(".pdf")).ToList();
                var xmlFiles  = form.Files.Where(f => f.FileName.ToLowerInvariant().EndsWith(".xml")).ToList();

                if (pdfFiles.Count == 0 && xmlFiles.Count == 0)
                    return Results.Json(new { ok = false, error = "Aucun fichier PDF ou XML reçu" });

                // Use a typed record for saved jobs to avoid dynamic
                var savedJobs = new List<(string FileName, string FullPath)>();

                // Load XML coupling + mapping config
                var intCfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config")
                             ?? new IntegrationsFullConfig();
                var xmlCouplingCfg = MongoDbHelper.GetSettings<SubmissionXmlCouplingSettings>("submission_xml_coupling")
                                     ?? new SubmissionXmlCouplingSettings();
                var mapping  = intCfg.XmlImport?.Mapping ?? new Dictionary<string, string>();
                var dedupKey = intCfg.XmlImport?.DedupKey ?? "referenceCommande";

                var root = BackendUtils.HotfoldersRoot();
                var destDir = Path.Combine(root, "Soumission");
                Directory.CreateDirectory(destDir);

                // --- Parse the XML if present ---
                Dictionary<string, string> fichePrefill = new();
                string? xmlFileName = null;

                if (xmlFiles.Count > 0)
                {
                    var xmlFile = xmlFiles[0]; // Take the first XML (or the one that matches by name)
                    xmlFileName = xmlFile.FileName;

                    // If multiple PDFs and multiple XMLs, try to match by base name later.
                    // For now parse the first (or only) XML.
                    XDocument doc;
                    try
                    {
                        using var stream = xmlFile.OpenReadStream();
                        doc = XmlParserHelper.LoadSafely(stream);
                    }
                    catch (System.Xml.XmlException xmlEx)
                    {
                        return Results.Json(new { ok = false, error = $"Fichier XML invalide ({xmlFile.FileName}) : {xmlEx.Message}" });
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new { ok = false, error = $"Impossible de lire le fichier XML ({xmlFile.FileName}) : {ex.Message}" });
                    }

                    // MasterPrint format: dedicated structured parser as base
                    if (XmlParserHelper.IsMasterPrint(doc))
                    {
                        var commandeEl = doc.Descendants("Commande").FirstOrDefault();
                        if (commandeEl != null)
                            fichePrefill = XmlParserHelper.ParseMasterPrintCommande(commandeEl);
                    }

                    // Always apply user-configured mapping (overrides/extends hardcoded values)
                    {
                        var orderEl = XmlParserHelper.IsMasterPrint(doc)
                            ? doc.Descendants("Commande").FirstOrDefault()
                            : (doc.Descendants("Order")
                                   .Concat(doc.Descendants("Commande"))
                                   .Concat(doc.Descendants("Job"))
                                   .FirstOrDefault()
                                ?? doc.Root);

                        if (orderEl != null && mapping.Count > 0)
                        {
                            foreach (var kv in mapping)
                            {
                                try
                                {
                                    var ficheField = IntegrationsEndpoints.NormalizeFicheFieldKey(kv.Key);
                                    var xmlTag = kv.Value;
                                    if (string.IsNullOrWhiteSpace(xmlTag)) continue;
                                    var el = orderEl.Element(xmlTag) ?? orderEl.Descendants(xmlTag).FirstOrDefault();
                                    if (el != null && !string.IsNullOrWhiteSpace(el.Value))
                                        fichePrefill[ficheField] = el.Value;
                                }
                                catch { /* skip invalid mapping entries */ }
                            }
                        }

                        if (!XmlParserHelper.IsMasterPrint(doc) && orderEl != null)
                        {
                            // Fallback: map direct child tag names (simple scalar elements only)
                            foreach (var el in orderEl.Elements())
                            {
                                if (!fichePrefill.ContainsKey(el.Name.LocalName) && !el.HasElements)
                                    fichePrefill[el.Name.LocalName] = el.Value;
                            }
                        }
                    }
                }

                // --- Save PDF files to Soumission folder ---

                if (pdfFiles.Count == 0 && xmlFiles.Count > 0)
                {
                    // XML only: create a fiche without PDF (dedup / insert)
                    var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
                    var fiche  = new BsonDocument();
                    foreach (var kv in fichePrefill)
                        fiche[kv.Key] = kv.Value;
                    fiche["importedAt"]   = DateTime.UtcNow.ToString("O");
                    fiche["importSource"] = "submission-xml";

                    var dedupValue = fichePrefill.ContainsKey(dedupKey) ? fichePrefill[dedupKey] : null;
                    bool isUpdate  = false;
                    if (!string.IsNullOrEmpty(dedupValue))
                    {
                        var existing = fabCol.Find(Builders<BsonDocument>.Filter.Eq(dedupKey, dedupValue)).FirstOrDefault();
                        if (existing != null)
                        {
                            fiche["_id"] = existing["_id"];
                            fiche["updatedAt"] = DateTime.UtcNow.ToString("O");
                            fabCol.ReplaceOne(Builders<BsonDocument>.Filter.Eq("_id", existing["_id"]), fiche);
                            isUpdate = true;
                        }
                    }
                    if (!isUpdate) fabCol.InsertOne(fiche);

                    return Results.Json(new
                    {
                        ok          = true,
                        fichePrefill,
                        jobIds      = Array.Empty<string>(),
                        mode        = xmlCouplingCfg.Mode,
                        xmlOnly     = true,
                        message     = isUpdate ? "Fiche mise à jour depuis XML" : "Fiche créée depuis XML"
                    });
                }

                foreach (var pdf in pdfFiles)
                {
                    long numero = MongoDbHelper.GetNextFileNumber();
                    var destFileName = $"{numero:D5}_{Path.GetFileName(pdf.FileName)}";
                    var destPath = Path.Combine(destDir, destFileName);

                    try
                    {
                        await using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                            await pdf.CopyToAsync(fs);
                    }
                    catch (Exception ioEx)
                    {
                        return Results.Json(new { ok = false, error = $"Impossible d'enregistrer le fichier PDF ({pdf.FileName}) : {ioEx.Message}" });
                    }

                    savedJobs.Add((destFileName, destPath));
                }

                // If mode is "create" and XML data available, auto-create fiche records
                if (xmlCouplingCfg.Mode == "create" && fichePrefill.Count > 0)
                {
                    var fabCol   = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
                    var logCol   = MongoDbHelper.GetCollection<BsonDocument>("integration_import_log");
                    var fiche    = new BsonDocument();
                    foreach (var kv in fichePrefill) fiche[kv.Key] = kv.Value;
                    fiche["importedAt"]   = DateTime.UtcNow.ToString("O");
                    fiche["importSource"] = "submission-xml";

                    // Attach PDF paths
                    if (savedJobs.Count == 1) fiche["pdfPath"] = savedJobs[0].FullPath;

                    var dedupValue = fichePrefill.ContainsKey(dedupKey) ? fichePrefill[dedupKey] : null;
                    bool isUpdate  = false;
                    if (!string.IsNullOrEmpty(dedupValue))
                    {
                        var existing = fabCol.Find(Builders<BsonDocument>.Filter.Eq(dedupKey, dedupValue)).FirstOrDefault();
                        if (existing != null)
                        {
                            fiche["_id"]       = existing["_id"];
                            fiche["updatedAt"] = DateTime.UtcNow.ToString("O");
                            fabCol.ReplaceOne(Builders<BsonDocument>.Filter.Eq("_id", existing["_id"]), fiche);
                            isUpdate = true;
                        }
                    }
                    if (!isUpdate) fabCol.InsertOne(fiche);

                    try
                    {
                        logCol.InsertOne(new BsonDocument
                        {
                            ["timestamp"]  = DateTime.UtcNow.ToString("O"),
                            ["source"]     = "submission-xml",
                            ["fileName"]   = xmlFileName ?? "",
                            ["status"]     = isUpdate ? "update" : "ok",
                            ["message"]    = isUpdate
                                ? $"Fiche mise à jour via soumission PDF+XML ({dedupKey}={dedupValue})"
                                : $"Fiche créée via soumission PDF+XML"
                        });
                    }
                    catch { /* log failure is non-critical */ }
                }

                return Results.Json(new
                {
                    ok          = true,
                    fichePrefill,
                    jobIds      = savedJobs.Select(j => new { fileName = j.FileName, fullPath = j.FullPath }).ToList(),
                    mode        = fichePrefill.Count > 0 ? xmlCouplingCfg.Mode : "upload-only",
                    xmlFileName
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── GET /api/settings/submission-xml-coupling ────────────────────────
        app.MapGet("/api/settings/submission-xml-coupling", (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx)) return Results.Json(new { ok = false, error = "Authentification requise" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<SubmissionXmlCouplingSettings>("submission_xml_coupling")
                          ?? new SubmissionXmlCouplingSettings();
                return Results.Json(new { ok = true, config = cfg });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/settings/submission-xml-coupling ────────────────────────
        app.MapPut("/api/settings/submission-xml-coupling", async (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx)) return Results.Json(new { ok = false, error = "Authentification requise" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg  = MongoDbHelper.GetSettings<SubmissionXmlCouplingSettings>("submission_xml_coupling")
                           ?? new SubmissionXmlCouplingSettings();

                if (body.TryGetProperty("enabled", out var enEl)) cfg.Enabled = enEl.GetBoolean();
                if (body.TryGetProperty("mode", out var modeEl))
                {
                    var m = modeEl.GetString() ?? "prefill";
                    cfg.Mode = m == "create" ? "create" : "prefill";
                }

                MongoDbHelper.UpsertSettings("submission_xml_coupling", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/settings/submission-erp-lookup ──────────────────────────
        app.MapGet("/api/settings/submission-erp-lookup", (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx)) return Results.Json(new { ok = false, error = "Authentification requise" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<SubmissionErpLookupSettings>("submission_erp_lookup")
                          ?? new SubmissionErpLookupSettings();
                // Never expose erp source passwords/api-keys in the list
                var safeErpSources = cfg.ErpSources.Select(s => new
                {
                    s.Id, s.Name, s.Url, s.AuthType, s.AuthUser,
                    s.ResponseFormat, s.Mapping
                }).ToList();
                return Results.Json(new
                {
                    ok = true,
                    config = new
                    {
                        cfg.Enabled, cfg.DefaultSource, cfg.RefDetectionRegex,
                        cfg.AutoLookup, erpSources = safeErpSources
                    }
                });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/settings/submission-erp-lookup ──────────────────────────
        app.MapPut("/api/settings/submission-erp-lookup", async (HttpContext ctx) =>
        {
            if (!IsAuthenticated(ctx)) return Results.Json(new { ok = false, error = "Authentification requise" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg  = MongoDbHelper.GetSettings<SubmissionErpLookupSettings>("submission_erp_lookup")
                           ?? new SubmissionErpLookupSettings();

                if (body.TryGetProperty("enabled", out var enEl))      cfg.Enabled          = enEl.GetBoolean();
                if (body.TryGetProperty("defaultSource", out var dsEl)) cfg.DefaultSource    = dsEl.GetString() ?? "";
                if (body.TryGetProperty("refDetectionRegex", out var rEl)) cfg.RefDetectionRegex = rEl.GetString() ?? "";
                if (body.TryGetProperty("autoLookup", out var alEl))   cfg.AutoLookup       = alEl.GetBoolean();

                if (body.TryGetProperty("erpSources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Array)
                {
                    // Keep existing sources for credential preservation before resetting the list
                    var existingSources = cfg.ErpSources ?? new List<ErpSourceConfig>();
                    cfg.ErpSources = new List<ErpSourceConfig>();
                    foreach (var s in sourcesEl.EnumerateArray())
                    {
                        var src = new ErpSourceConfig();
                        if (s.TryGetProperty("id",   out var idEl))   src.Id   = idEl.GetString() ?? Guid.NewGuid().ToString("N")[..8];
                        if (s.TryGetProperty("name", out var nEl))    src.Name = nEl.GetString() ?? "";
                        if (s.TryGetProperty("url",  out var uEl))    src.Url  = uEl.GetString() ?? "";
                        if (s.TryGetProperty("authType", out var atEl)) src.AuthType = atEl.GetString() ?? "none";
                        if (s.TryGetProperty("authUser", out var auEl)) src.AuthUser = auEl.GetString() ?? "";
                        if (s.TryGetProperty("authPassword", out var apEl) && !string.IsNullOrEmpty(apEl.GetString()))
                            src.AuthPassword = apEl.GetString()!;
                        if (s.TryGetProperty("authHeader", out var ahEl)) src.AuthHeader = ahEl.GetString() ?? "";
                        if (s.TryGetProperty("authToken", out var tokEl) && !string.IsNullOrEmpty(tokEl.GetString()))
                            src.AuthToken = tokEl.GetString()!;
                        if (s.TryGetProperty("responseFormat", out var rfEl)) src.ResponseFormat = rfEl.GetString() ?? "json";
                        if (s.TryGetProperty("mapping", out var mapEl) && mapEl.ValueKind == JsonValueKind.Object)
                        {
                            src.Mapping = new Dictionary<string, string>();
                            foreach (var prop in mapEl.EnumerateObject())
                                src.Mapping[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        // Preserve existing credentials if not provided in this request
                        var existing = existingSources.FirstOrDefault(x => x.Id == src.Id);
                        if (existing != null)
                        {
                            if (string.IsNullOrEmpty(src.AuthPassword)) src.AuthPassword = existing.AuthPassword;
                            if (string.IsNullOrEmpty(src.AuthToken))    src.AuthToken    = existing.AuthToken;
                        }
                        cfg.ErpSources.Add(src);
                    }
                }

                MongoDbHelper.UpsertSettings("submission_erp_lookup", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }
}

// ── Settings models ──────────────────────────────────────────────────────────

public class SubmissionXmlCouplingSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>"prefill" = open pre-filled form | "create" = auto-create fiche</summary>
    [System.Text.Json.Serialization.JsonPropertyName("mode")]
    public string Mode { get; set; } = "prefill";
}

public class SubmissionErpLookupSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [System.Text.Json.Serialization.JsonPropertyName("defaultSource")]
    public string DefaultSource { get; set; } = "";

    /// <summary>Regex applied to the PDF filename to auto-extract the order reference.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("refDetectionRegex")]
    public string RefDetectionRegex { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("autoLookup")]
    public bool AutoLookup { get; set; } = false;

    [System.Text.Json.Serialization.JsonPropertyName("erpSources")]
    public List<ErpSourceConfig> ErpSources { get; set; } = new();
}

public class ErpSourceConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>none | basic | bearer | apikey</summary>
    [System.Text.Json.Serialization.JsonPropertyName("authType")]
    public string AuthType { get; set; } = "none";

    [System.Text.Json.Serialization.JsonPropertyName("authUser")]
    public string AuthUser { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("authPassword")]
    public string AuthPassword { get; set; } = "";

    /// <summary>Header name for API-key auth (e.g. "X-Api-Key")</summary>
    [System.Text.Json.Serialization.JsonPropertyName("authHeader")]
    public string AuthHeader { get; set; } = "X-Api-Key";

    [System.Text.Json.Serialization.JsonPropertyName("authToken")]
    public string AuthToken { get; set; } = "";

    /// <summary>json | xml</summary>
    [System.Text.Json.Serialization.JsonPropertyName("responseFormat")]
    public string ResponseFormat { get; set; } = "json";

    /// <summary>Maps response JSON/XML fields to fiche fields.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("mapping")]
    public Dictionary<string, string> Mapping { get; set; } = new();
}
