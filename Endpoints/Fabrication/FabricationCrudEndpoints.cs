using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Fabrication;

public static class FabricationCrudEndpoints
{
    public static void MapFabricationCrudEndpoints(this WebApplication app)
    {
app.MapGet("/api/fabrication", (string? fullPath, string? fileName) =>
{
    try
    {
    FabricationSheet? sheet = null;
    bool locked = false;

    var fabCol = MongoDbHelper.GetFabricationsCollection();
    BsonDocument? rawDoc = null;

    if (!string.IsNullOrWhiteSpace(fullPath))
    {
        rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();
        if (rawDoc == null)
        {
            var fn = Path.GetFileName(fullPath)?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(fn))
                rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fn)).SortByDescending(x => x["_id"]).FirstOrDefault();
        }
        if (rawDoc != null) sheet = BackendUtils.BsonDocToFabricationSheet(rawDoc);
    }
    if (sheet == null && !string.IsNullOrWhiteSpace(fileName))
    {
        var lf = (fileName ?? "").ToLowerInvariant();
        rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", lf)).SortByDescending(x => x["_id"]).FirstOrDefault();
        if (rawDoc != null) sheet = BackendUtils.BsonDocToFabricationSheet(rawDoc);
    }

    if (sheet != null)
    {
        locked = rawDoc != null && rawDoc.Contains("locked") && rawDoc["locked"] != BsonNull.Value
            && rawDoc["locked"].BsonType == BsonType.Boolean && rawDoc["locked"].AsBoolean;
        // Serialize sheet then append locked field to JSON string to avoid JsonDocument disposal issues
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var json = System.Text.Json.JsonSerializer.Serialize(sheet, opts);
        var resultJson = json.EndsWith("}")
            ? json[..^1] + ",\"locked\":" + (locked ? "true" : "false") + "}"
            : json;
        return Results.Content(resultJson, "application/json");
    }

    return Results.Json(new { ok = false, error = "Aucune fiche de fabrication." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] GET /api/fabrication: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/fabrication", async (HttpContext ctx) =>
{
    try
    {
        // Extract user from token
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string userName = "Système";
        int userProfile = 0;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out userProfile);
                    var users = BackendUtils.LoadUsers();
                    var u = users.FirstOrDefault(x => x.Id == parts[0]);
                    if (u != null) userName = u.Name;
                }
            }
            catch { /* ignore token parse errors */ }
        }

        var input = await ctx.Request.ReadFromJsonAsync<FabricationInput>();
        if (input == null)
            return Results.Json(new { ok = false, error = "JSON vide." });

        if (string.IsNullOrWhiteSpace(input.FullPath) && string.IsNullOrWhiteSpace(input.FileName))
            return Results.Json(new { ok = false, error = "FullPath ou FileName requis." });

        // If fullPath provided but file doesn't exist, warn but proceed (file may have been moved by Acrobat)
        if (!string.IsNullOrWhiteSpace(input.FullPath) && !File.Exists(input.FullPath))
            Console.WriteLine($"[WARN] PUT /api/fabrication: File not found at {input.FullPath}, saving anyway (may have been moved).");

        // If no fullPath provided, try to reconstruct from existing fabrication record
        if (string.IsNullOrWhiteSpace(input.FullPath) && !string.IsNullOrWhiteSpace(input.FileName))
        {
            var existing = BackendUtils.FindFabricationByName(input.FileName);
            // Use existing path if available, otherwise use fileName as placeholder key
            input.FullPath = existing?.FullPath is { Length: > 0 } ? existing.FullPath : (input.FileName ?? "");
        }

        var old = BackendUtils.FindFabrication(input.FullPath);
        // Extra fallback: look up by fileName to ensure media and other fields are preserved
        // even when the file was moved to a different folder (fullPath changes but fileName doesn't)
        if (old == null && !string.IsNullOrWhiteSpace(input.FileName))
            old = BackendUtils.FindFabricationByName(input.FileName);

        // Admin-only fields: only profile 3 can update TypeDocument, NombreFeuilles
        var isAdmin = (userProfile == 3);

        var sheet = new FabricationSheet
        {
            FullPath = input.FullPath,
            FileName = string.IsNullOrWhiteSpace(input.FileName)
                ? Path.GetFileName(input.FullPath)
                : input.FileName,

            // Fields always sent by the JS frontend — use input directly so clearing a field persists
            MoteurImpression = input.MoteurImpression,
            Machine          = input.Machine,
            Operateur        = input.Operateur,
            Quantite         = input.Quantite,
            TypeTravail      = input.TypeTravail,
            Format           = input.Format,
            RectoVerso       = input.RectoVerso,
            FormeDecoupe     = input.FormeDecoupe,
            Bat              = input.Bat,
            RetraitLivraison = input.RetraitLivraison,
            AdresseLivraison = input.AdresseLivraison,
            Client           = input.Client,
            NumeroDossier    = input.NumeroDossier,
            Notes            = input.Notes,
            Faconnage        = input.Faconnage,
            Delai            = input.Delai,

            // Fields NOT always sent by the JS frontend — keep old value as fallback
            Papier           = input.Papier           ?? old?.Papier,
            Encres           = input.Encres           ?? old?.Encres,
            NumeroAffaire    = input.NumeroAffaire    ?? old?.NumeroAffaire,
            Livraison        = input.Livraison        ?? old?.Livraison,

            Media1        = input.Media1,
            Media2        = input.Media2,
            Media3        = input.Media3,
            Media4        = input.Media4,
            TypeDocument  = isAdmin ? input.TypeDocument  : old?.TypeDocument,
            NombreFeuilles = input.NombreFeuilles ?? old?.NombreFeuilles,

            DonneurOrdreNom       = input.DonneurOrdreNom,
            DonneurOrdrePrenom    = input.DonneurOrdrePrenom,
            DonneurOrdreTelephone = input.DonneurOrdreTelephone,
            DonneurOrdreEmail     = input.DonneurOrdreEmail,
            Pagination            = input.Pagination,
            FormatFeuille         = input.FormatFeuille,
            Media1Fabricant       = input.Media1Fabricant,
            Media2Fabricant       = input.Media2Fabricant,
            Media3Fabricant       = input.Media3Fabricant,
            Media4Fabricant       = input.Media4Fabricant,
            MediaCouverture         = input.MediaCouverture,
            MediaCouvertureFabricant = input.MediaCouvertureFabricant,
            Rainage        = input.Rainage,
            Ennoblissement = input.Ennoblissement,
            FaconnageBinding = input.FaconnageBinding,
            Plis             = input.Plis,
            Sortie           = input.Sortie,
            MailDevisFileName = input.MailDevisFileName ?? old?.MailDevisFileName,
            MailBatFileName   = input.MailBatFileName   ?? old?.MailBatFileName,
            DateDepart      = input.DateDepart,
            DateLivraison   = input.DateLivraison,
            PlanningMachine = input.PlanningMachine,
            DateReception         = input.DateReception,
            DateEnvoi             = input.DateEnvoi,
            DateProductionFinitions = input.DateProductionFinitions,
            DateImpression        = input.DateImpression,
            TempsProduitMinutes   = input.TempsProduitMinutes ?? old?.TempsProduitMinutes,
            JustifsClientsQuantite = input.JustifsClientsQuantite,
            JustifsClientsAdresse  = input.JustifsClientsAdresse,
            Repartitions = input.Repartitions,

            History = old?.History ?? new List<FabricationHistory>()
        };

        sheet.History.Add(new FabricationHistory
        {
            Date   = DateTime.Now,
            User   = userName,
            Action = (old == null ? "Création fiche" : "Modification fiche")
        });

        BackendUtils.UpsertFabrication(sheet);

        // Sync numeroDossier to productionFolders and rename physical folder if needed
        if (!string.IsNullOrWhiteSpace(sheet.NumeroDossier))
        {
            try
            {
                var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                var pfFilter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("originalFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("currentFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("fileName", sheet.FileName)
                );
                var pfDoc = pfCol.Find(pfFilter).FirstOrDefault();
                if (pfDoc != null)
                {
                    var oldNumeroDossier = pfDoc.Contains("numeroDossier") && pfDoc["numeroDossier"] != BsonNull.Value
                        ? pfDoc["numeroDossier"].AsString : null;

                    // Update numeroDossier in the document
                    var pfUpdate = Builders<BsonDocument>.Update.Set("numeroDossier", sheet.NumeroDossier);
                    pfCol.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", pfDoc["_id"]), pfUpdate);

                    // Rename physical folder if numeroDossier changed
                    if (oldNumeroDossier != sheet.NumeroDossier)
                    {
                        var existingFolderPath = pfDoc.Contains("folderPath") ? pfDoc["folderPath"].AsString : "";
                        if (!string.IsNullOrEmpty(existingFolderPath) && Directory.Exists(existingFolderPath))
                        {
                            var safeName = BackendUtils.SafeNameRegex.Replace(Path.GetFileNameWithoutExtension(sheet.FileName ?? ""), "_");
                            var newFolderName = $"{sheet.NumeroDossier}_{safeName}";
                            var parentDir = Path.GetDirectoryName(existingFolderPath) ?? "";
                            var newFolderPath = Path.Combine(parentDir, newFolderName);
                            if (!string.Equals(existingFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase)
                                && !Directory.Exists(newFolderPath))
                            {
                                Directory.Move(existingFolderPath, newFolderPath);
                                pfCol.UpdateOne(
                                    Builders<BsonDocument>.Filter.Eq("_id", pfDoc["_id"]),
                                    Builders<BsonDocument>.Update.Set("folderPath", newFolderPath));
                            }
                        }
                    }
                }
            }
            catch (Exception exPf)
            {
                Console.WriteLine($"[WARN] SyncNumeroDossierToProductionFolder failed: {exPf.Message}");
            }
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/fabrication/export-xml", async (string fullPath, HttpContext ctx) =>
{
    try
    {
        var sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        // Build XML job ticket using XDocument for proper escaping
        var jobTicket = new XElement("JobTicket",
            new XElement("FilePath",         sheet.FullPath),
            new XElement("FileName",         sheet.FileName),
            new XElement("NumeroDossier",    sheet.NumeroDossier ?? ""),
            new XElement("Client",           sheet.Client ?? ""),
            new XElement("Quantite",         sheet.Quantite?.ToString() ?? ""),
            new XElement("MoteurImpression", sheet.MoteurImpression ?? sheet.Machine ?? ""),
            new XElement("TypeTravail",      sheet.TypeTravail ?? ""),
            new XElement("Format",           sheet.Format ?? ""),
            new XElement("RectoVerso",       sheet.RectoVerso ?? ""),
            new XElement("Media1",           sheet.Media1 ?? ""),
            new XElement("Media2",           sheet.Media2 ?? ""),
            new XElement("Media3",           sheet.Media3 ?? ""),
            new XElement("Media4",           sheet.Media4 ?? ""),
            new XElement("Faconnage",        sheet.Faconnage != null ? string.Join(", ", sheet.Faconnage) : ""),
            new XElement("Notes",            sheet.Notes ?? ""),
            sheet.Delai.HasValue ? new XElement("Delai", sheet.Delai.Value.ToString("yyyy-MM-dd")) : null
        );
        var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), jobTicket);
        var xmlContent = xdoc.ToString(SaveOptions.None);

        // Save XML to production folder
        string xmlSavePath = "";
        try
        {
            var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
            var pfDoc = pfCol.Find(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("originalFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("currentFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("fileName", sheet.FileName)
                )).SortByDescending(x => x["createdAt"]).FirstOrDefault();

            if (pfDoc != null && pfDoc.Contains("folderPath"))
            {
                var folderPath = pfDoc["folderPath"].AsString;
                Directory.CreateDirectory(folderPath);
                xmlSavePath = Path.Combine(folderPath, "fiche.xml");
                await File.WriteAllTextAsync(xmlSavePath, xmlContent, System.Text.Encoding.UTF8);
            }
        }
        catch (Exception saveEx)
        {
            Console.WriteLine($"[WARN] XML save to production folder failed: {saveEx.Message}");
        }

        return Results.Json(new { ok = true, xml = xmlContent, xmlPath = xmlSavePath });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// FABRICATION — GÉNÉRATION JDF
// ======================================================

app.MapPost("/api/fabrication/generate-jdf", async (HttpContext ctx) =>
{
    try
    {
        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = body.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var fileName = body.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";

        FabricationSheet? sheet = null;
        if (!string.IsNullOrEmpty(fullPath)) sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null && !string.IsNullOrEmpty(fileName)) sheet = BackendUtils.FindFabricationByName(fileName);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        // Load JDF config to know which fields to include
        var jdfConfig = MongoDbHelper.GetSettings<JdfConfig>("jdfConfig") ?? new JdfConfig();
        if (!jdfConfig.Enabled)
            return Results.Json(new { ok = false, error = "La génération JDF n'est pas activée dans les paramètres" });

        var includedFields = jdfConfig.Fields.Where(f => f.Included).Select(f => f.FieldId).ToHashSet();

        // Build JDF XML (Job Definition Format — simplified CIP4 JDF structure)
        var jobId = $"JDF_{sheet.NumeroDossier ?? "0"}_{DateTime.Now:yyyyMMddHHmmss}";
        var jdfEl = new XElement("JDF",
            new XAttribute("ID", jobId),
            new XAttribute("JobID", sheet.NumeroDossier ?? ""),
            new XAttribute("Type", "Product"),
            new XAttribute("Status", "Waiting"),
            new XAttribute("Version", "1.6"),
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XElement("AuditPool",
                new XElement("Created",
                    new XAttribute("TimeStamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                    new XAttribute("AgentName", "GestionAtelier"),
                    new XAttribute("AgentVersion", "1.0")
                )
            )
        );

        var resourcePool = new XElement("ResourcePool");
        if (includedFields.Contains("numeroDossier") || includedFields.Count == 0)
            resourcePool.Add(new XElement("RunList", new XAttribute("ID", "RL1"), new XAttribute("Class", "Parameter"), new XAttribute("Status", "Available"), new XElement("LayoutElement", new XElement("FileSpec", new XAttribute("URL", sheet.FullPath ?? "")))));
        if (includedFields.Contains("quantite") || includedFields.Count == 0)
            resourcePool.Add(new XElement("Component", new XAttribute("ID", "C1"), new XAttribute("Class", "Quantity"), new XAttribute("Status", "Available"), new XAttribute("Amount", sheet.Quantite?.ToString() ?? "0")));

        var jobEl = new XElement("NodeInfo", new XAttribute("JobPriority", "50"));
        if ((includedFields.Contains("numeroDossier") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.NumeroDossier))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "NumeroDossier"), sheet.NumeroDossier));
        if ((includedFields.Contains("client") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.Client))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "Client"), sheet.Client));
        if ((includedFields.Contains("nombreFeuilles") || includedFields.Count == 0) && sheet.NombreFeuilles.HasValue)
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "NombreFeuilles"), sheet.NombreFeuilles.Value.ToString()));
        if ((includedFields.Contains("formatFeuilleMachine") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.FormatFeuille))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "FormatFeuilleMachine"), sheet.FormatFeuille));
        if ((includedFields.Contains("rectoVerso") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.RectoVerso))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "RectoVerso"), sheet.RectoVerso));
        if ((includedFields.Contains("moteurImpression") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.MoteurImpression ?? sheet.Machine))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "MoteurImpression"), sheet.MoteurImpression ?? sheet.Machine ?? ""));
        if ((includedFields.Contains("typeTravail") || includedFields.Count == 0) && !string.IsNullOrEmpty(sheet.TypeTravail))
            jobEl.Add(new XElement("Comment", new XAttribute("Name", "TypeTravail"), sheet.TypeTravail));

        jdfEl.Add(resourcePool);
        jdfEl.Add(jobEl);

        var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), jdfEl);
        var jdfContent = xdoc.ToString(SaveOptions.None);

        // Save JDF file
        string jdfSavePath = "";
        string pdfCopyPath = "";
        try
        {
            var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
            var pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("originalFilePath", sheet.FullPath),
                Builders<BsonDocument>.Filter.Eq("currentFilePath", sheet.FullPath),
                Builders<BsonDocument>.Filter.Eq("fileName", sheet.FileName)
            )).SortByDescending(x => x["createdAt"]).FirstOrDefault();

            if (pfDoc != null && pfDoc.Contains("folderPath"))
            {
                var folderPath = pfDoc["folderPath"].AsString;
                Directory.CreateDirectory(folderPath);
                jdfSavePath = Path.Combine(folderPath, $"{Path.GetFileNameWithoutExtension(sheet.FileName ?? "job")}.jdf");
                await File.WriteAllTextAsync(jdfSavePath, jdfContent, System.Text.Encoding.UTF8);

                // Copy PDF alongside JDF if it exists
                if (!string.IsNullOrEmpty(sheet.FullPath) && File.Exists(sheet.FullPath))
                {
                    pdfCopyPath = Path.Combine(folderPath, Path.GetFileName(sheet.FullPath));
                    if (!string.Equals(sheet.FullPath, pdfCopyPath, StringComparison.OrdinalIgnoreCase))
                        File.Copy(sheet.FullPath, pdfCopyPath, overwrite: true);
                }
            }
        }
        catch (Exception saveEx)
        {
            Console.WriteLine($"[WARN] JDF save failed: {saveEx.Message}");
        }

        // Optionally send to hotfolder if routing configured
        try
        {
            var moteur = sheet.MoteurImpression ?? sheet.Machine ?? "";
            if (!string.IsNullOrEmpty(moteur))
            {
                var routingCol = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRoutings");
                var typeTravail = sheet.TypeTravail ?? "";
                var routingDoc = routingCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
                    Builders<BsonDocument>.Filter.Eq("moteurImpression", moteur)
                )).FirstOrDefault();
                if (routingDoc != null && routingDoc.Contains("prismaSyncPath") && !string.IsNullOrEmpty(routingDoc["prismaSyncPath"].AsString))
                {
                    var hotfolderPath = routingDoc["prismaSyncPath"].AsString;
                    Directory.CreateDirectory(hotfolderPath);
                    if (!string.IsNullOrEmpty(jdfSavePath) && File.Exists(jdfSavePath))
                        File.Copy(jdfSavePath, Path.Combine(hotfolderPath, Path.GetFileName(jdfSavePath)), overwrite: true);
                    if (!string.IsNullOrEmpty(sheet.FullPath) && File.Exists(sheet.FullPath))
                        File.Copy(sheet.FullPath, Path.Combine(hotfolderPath, Path.GetFileName(sheet.FullPath)), overwrite: true);
                }
            }
        }
        catch (Exception hfEx)
        {
            Console.WriteLine($"[WARN] JDF hotfolder send failed: {hfEx.Message}");
        }

        return Results.Json(new { ok = true, jdfPath = jdfSavePath, pdfCopyPath });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// BAT — Execute PrismaPrepare command
// ======================================================

app.MapPost("/api/bat/send-to-hotfolder", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        // 1. Find fabrication record to get typeTravail
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? fabDoc = null;
        if (!string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fullPath))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();

        var typeTravail = "";
        if (fabDoc != null && fabDoc.Contains("typeTravail") && fabDoc["typeTravail"] != BsonNull.Value)
            typeTravail = fabDoc["typeTravail"].AsString ?? "";

        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "Type de travail non défini dans la fiche de fabrication. Veuillez renseigner le type de travail avant d'effectuer un BAT Complet." });

        // 2. Find hotfolder path for this typeTravail
        var routingCol = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var routingDoc = routingCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();

        if (routingDoc == null || !routingDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(routingDoc["hotfolderPath"].AsString))
            return Results.Json(new { ok = false, error = $"Aucun hotfolder PrismaPrepare configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Hotfolder." });

        var hotfolderPath = routingDoc["hotfolderPath"].AsString;

        // 3. Locate the actual file if fullPath not provided or doesn't exist
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            var found = Directory.GetFiles(hotRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) fullPath = found;
        }

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier source introuvable : {fileName}" });

        // 4. Copy to hotfolder PrismaPrepare
        if (!Directory.Exists(hotfolderPath))
            Directory.CreateDirectory(hotfolderPath);

        var hotfolderDest = Path.Combine(hotfolderPath, Path.GetFileName(fullPath));
        File.Copy(fullPath, hotfolderDest, overwrite: true);
        Console.WriteLine($"[BAT] Copié vers hotfolder: {hotfolderDest}");

        // 5. Store pending rename: when "Epreuve PDF.pdf" arrives in the hotfolder,
        //    it will be renamed to "{originalName} Epreuve.pdf".
        //    Field "batFolder" holds the watched folder path (hotfolder here, reused by process-pending).
        try
        {
            var batPendingCol = MongoDbHelper.GetCollection<BsonDocument>("batPending");
            batPendingCol.InsertOne(new BsonDocument
            {
                ["sourceFileName"] = Path.GetFileNameWithoutExtension(fullPath),
                ["batFolder"] = hotfolderPath,   // watched folder = hotfolder destination
                ["createdAt"] = DateTime.UtcNow,
                ["processed"] = false
            });
        }
        catch { /* non-blocking */ }

        return Results.Json(new { ok = true, hotfolder = hotfolderPath, typeTravail });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] bat/send-to-hotfolder: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// BAT — Copie vers TEMP_COPY + hotfolder PrismaPrepare (nouveau workflow BAT Complet)
// ======================================================

app.MapPost("/api/bat/copy-for-bat", async (HttpContext ctx) =>
{
    bool lockAcquired = false;
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var requestedBy = json.TryGetProperty("requestedBy", out var rb) ? rb.GetString() ?? "" : "";

        Console.WriteLine($"[BAT] copy-for-bat: fileName={fileName}, fullPath={fullPath}, requestedBy={requestedBy}");

        // 0. Check BAT serialization — only one BAT at a time
        var displayName = !string.IsNullOrEmpty(fileName) ? fileName : (Path.GetFileName(fullPath) ?? "unknown");
        var correlationId = Guid.NewGuid().ToString("N").Substring(0, 16);
        if (!BatSerializationState.TryAcquire(displayName, correlationId))
        {
            var (_, currentFile, startedAt, _, _) = BatSerializationState.Get();
            var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
            Console.WriteLine($"[BAT] copy-for-bat: BAT déjà en cours pour {currentFile} (depuis {elapsed:0}s) — rejet de {fileName}");
            return Results.Json(new { ok = false, error = "bat_in_progress", message = $"Un BAT est en cours de génération pour \"{currentFile}\". Veuillez patienter avant d'en envoyer un nouveau." });
        }
        lockAcquired = true;

        // 1. Find fabrication record to get typeTravail
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? fabDoc = null;
        if (!string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fullPath))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();
        // Case-insensitive fallback: search by fileName ignoring case
        if (fabDoc == null && !string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Regex("fileName",
                new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(fileName)}$", "i"))).FirstOrDefault();

        if (fabDoc == null)
        {
            Console.WriteLine($"[BAT] copy-for-bat: fiche de fabrication introuvable pour fileName={fileName}");
            return Results.Json(new { ok = false, error = $"Fiche de fabrication introuvable pour \"{fileName}\". Veuillez ouvrir la fiche et enregistrer avant d'effectuer un BAT Complet." });
        }

        var typeTravail = "";
        if (fabDoc.Contains("typeTravail") && fabDoc["typeTravail"].BsonType == BsonType.String)
            typeTravail = fabDoc["typeTravail"].AsString ?? "";

        Console.WriteLine($"[BAT] copy-for-bat: typeTravail={typeTravail}");

        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "Type de travail non défini dans la fiche de fabrication. Veuillez renseigner le type de travail avant d'effectuer un BAT Complet." });

        // 2. Find hotfolder path for this typeTravail
        var routingCol = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var routingDoc = routingCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();

        if (routingDoc == null ||
            !routingDoc.Contains("hotfolderPath") ||
            routingDoc["hotfolderPath"].BsonType != BsonType.String ||
            string.IsNullOrEmpty(routingDoc["hotfolderPath"].AsString))
        {
            Console.WriteLine($"[BAT] copy-for-bat: aucun routage hotfolder pour typeTravail={typeTravail}");
            return Results.Json(new { ok = false, error = $"Aucun hotfolder PrismaPrepare configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Hotfolder." });
        }

        var hotfolderPath = routingDoc["hotfolderPath"].AsString;

        // 3. Locate the actual file if fullPath not provided or doesn't exist
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            var found = Directory.GetFiles(hotRoot, string.IsNullOrEmpty(fileName) ? "*" : fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) fullPath = found;
            Console.WriteLine(found != null ? $"[BAT] copy-for-bat: fichier trouvé via scan: {fullPath}" : $"[BAT] copy-for-bat: fichier introuvable via scan (hotRoot={hotRoot}, fileName={fileName})");
        }

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier source introuvable : {fileName}" });

        var sourceBaseName = Path.GetFileNameWithoutExtension(fullPath);

        // 4. Get TEMP_COPY path from settings
        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations") ?? new IntegrationsSettings();
        var tempCopyPath = integCfg.TempCopyPath ?? "";
        if (string.IsNullOrWhiteSpace(tempCopyPath))
            return Results.Json(new { ok = false, error = "Chemin TEMP_COPY non configuré dans Paramétrage > Prepare / Fiery." });

        Directory.CreateDirectory(tempCopyPath);

        // 5. Copy to TEMP_COPY (original stays in place)
        BatSerializationState.SetStep("copying_to_temp");
        var tempCopyDest = Path.Combine(tempCopyPath, Path.GetFileName(fullPath));
        File.Copy(fullPath, tempCopyDest, overwrite: true);
        Console.WriteLine($"[BAT] Copié vers TEMP_COPY: {tempCopyDest}");

        // 6. Copy to hotfolder PrismaPrepare — rename file to include correlationId for reliable tracking
        if (!Directory.Exists(hotfolderPath))
            Directory.CreateDirectory(hotfolderPath);

        var hotfolderFileName = $"{sourceBaseName}__BAT_{correlationId}.pdf";
        var hotfolderDest = Path.Combine(hotfolderPath, hotfolderFileName);
        File.Copy(fullPath, hotfolderDest, overwrite: true);
        Console.WriteLine($"[BAT] Copié vers hotfolder PrismaPrepare: {hotfolderDest} (correlationId={correlationId})");
        BatSerializationState.SetStep("sent_to_hotfolder");

        // 7. Store pending rename in MongoDB so TEMP_COPY watcher can find the job name + requestedBy for notification
        try
        {
            var batPendingCol = MongoDbHelper.GetCollection<BsonDocument>("batPending");
            batPendingCol.InsertOne(new BsonDocument
            {
                ["sourceFileName"] = sourceBaseName,
                ["batFolder"] = tempCopyPath,
                ["createdAt"] = DateTime.UtcNow,
                ["processed"] = false,
                ["requestedBy"] = requestedBy,
                ["correlationId"] = correlationId
            });
        }
        catch { /* non-blocking */ }

        // Lock is intentionally NOT released here — HandleEpreuve (FSW) releases it when Epreuve.pdf is processed
        BatSerializationState.SetStep("waiting_for_epreuve");
        lockAcquired = false;
        return Results.Json(new { ok = true, hotfolder = hotfolderPath, tempCopy = tempCopyPath, typeTravail });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] bat/copy-for-bat: {ex.Message}\n{ex.StackTrace}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
    finally
    {
        // Release lock on any error path (lockAcquired remains true only if an exception occurred
        // after acquiring the lock but before the successful return clears it)
        if (lockAcquired) BatSerializationState.Release();
    }
});

// ======================================================
// CONFIG — Paper Catalog
// ======================================================


app.MapPost("/api/commands/bat", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var typeWork = json.TryGetProperty("typeWork", out var tw) ? tw.GetString() ?? "" : "";
        var quantity = json.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 1;

        var batCfgCol = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
        var batCfg = batCfgCol.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
        var template = batCfg != null && batCfg.Contains("command") ? batCfg["command"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /T \"{type}\" /SP /C {qty}";

        var cmd = BackendUtils.BuildCommandTemplate(template, Path.GetFileName(filePath), typeWork, quantity);
        Console.WriteLine($"[INFO] BAT command: {cmd}");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);

        // Store pending BAT rename so "Epreuve PDF.pdf" can be renamed to {originalName}.Epreuve.pdf
        try
        {
            var batPendingCol = MongoDbHelper.GetCollection<BsonDocument>("batPending");
            var batFolderCfg = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig")
                .Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
            var batFolder = batFolderCfg != null && batFolderCfg.Contains("batFolder")
                ? batFolderCfg["batFolder"].AsString
                : Path.GetDirectoryName(filePath) ?? "";
            batPendingCol.InsertOne(new BsonDocument
            {
                ["sourceFileName"] = Path.GetFileNameWithoutExtension(filePath),
                ["batFolder"] = batFolder,
                ["createdAt"] = DateTime.UtcNow,
                ["processed"] = false
            });
        }
        catch { /* non-blocking */ }

        return Results.Json(new { ok = true, command = cmd });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-controller", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var controllerPath = cfg?.Contains("controllerPath") == true ? cfg["controllerPath"].AsString : @"C:\PrismaSync\Controller";
        Directory.CreateDirectory(controllerPath);
        File.Copy(filePath, Path.Combine(controllerPath, Path.GetFileName(filePath)), overwrite: true);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Impression en cours");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-prisma", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("prismaPrepareCommand") == true ? cfg["prismaPrepareCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "PrismaPrepare");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-print", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var quantity = json.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 1;
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("printCommand") == true ? cfg["printCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /SP /C {quantity}";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath, "", quantity);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Impression en cours");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/modify", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("modifyCommand") == true ? cfg["modifyCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "PrismaPrepare");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-fiery", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var fieryBase = cfg?.Contains("fieryHotfolderBase") == true ? cfg["fieryHotfolderBase"].AsString : @"C:\Fiery\Hotfolders";
        Directory.CreateDirectory(fieryBase);
        File.Copy(filePath, Path.Combine(fieryBase, Path.GetFileName(filePath)), overwrite: true);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Fiery");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// BAT TRACKING
// ======================================================

app.MapGet("/api/bat/status", (HttpContext ctx) =>
{
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.Json(new { ok = false, error = "path manquant" });
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var doc = col.Find(filter).FirstOrDefault();
    if (doc == null) return Results.Json(new { status = "none", sentAt = (object?)null, validatedAt = (object?)null, rejectedAt = (object?)null });
    return Results.Json(new {
        status = doc.Contains("status") ? doc["status"].AsString : "none",
        sentAt = doc.Contains("sentAt") && doc["sentAt"] != BsonNull.Value ? doc["sentAt"].ToUniversalTime() : (DateTime?)null,
        validatedAt = doc.Contains("validatedAt") && doc["validatedAt"] != BsonNull.Value ? doc["validatedAt"].ToUniversalTime() : (DateTime?)null,
        rejectedAt = doc.Contains("rejectedAt") && doc["rejectedAt"] != BsonNull.Value ? doc["rejectedAt"].ToUniversalTime() : (DateTime?)null
    });
});

// GET /api/bat/serialization-status — returns whether a BAT is currently being generated
app.MapGet("/api/bat/serialization-status", () =>
{
    var (inProgress, currentFileName, startedAt, _, _) = BatSerializationState.Get();
    return Results.Json(new
    {
        inProgress,
        currentFileName = currentFileName ?? "",
        startedAt = inProgress ? startedAt : (DateTime?)null
    });
});

// GET /api/bat/progress — returns real-time progress info for the operator
app.MapGet("/api/bat/progress", () =>
{
    var (inProgress, currentFileName, startedAt, currentStep, _) = BatSerializationState.Get();
    var (lastCompletedFileName, lastCompletedAt, lastPrismaLog) = BatSerializationState.GetLastCompleted();
    double elapsedSeconds = inProgress ? (DateTime.UtcNow - startedAt).TotalSeconds : 0;
    string status = inProgress ? "processing" : (lastCompletedFileName != null ? "completed" : "idle");

    // Parse PrismaPrepare log content into structured fields
    object? parsedLog = null;
    if (!string.IsNullOrEmpty(lastPrismaLog))
    {
        var logLines = lastPrismaLog.Split('\n');

        // warnings count: "Il y a N avertissements"
        int warnings = 0;
        var warnMatch = System.Text.RegularExpressions.Regex.Match(lastPrismaLog, @"Il y a (\d+) avertissements?");
        if (warnMatch.Success) int.TryParse(warnMatch.Groups[1].Value, out warnings);

        // success: log contains "Succès"
        bool success = lastPrismaLog.Contains("Succès", StringComparison.OrdinalIgnoreCase);

        // operations: lines containing "Exécutez l'opération :"
        var operations = logLines
            .Where(l => l.Contains("Exécutez l'opération :", StringComparison.OrdinalIgnoreCase))
            .Select(l =>
            {
                var idx = l.IndexOf("Exécutez l'opération :", StringComparison.OrdinalIgnoreCase);
                var start = idx + "Exécutez l'opération :".Length;
                return start < l.Length ? l.Substring(start).Trim() : "";
            })
            .Where(op => op.Length > 0)
            .ToList();

        // startTime: "Le travail a démarré à DD/MM/YYYY HH:MM:SS"
        string? startTime = null;
        var startMatch = System.Text.RegularExpressions.Regex.Match(lastPrismaLog, @"Le travail a démarré à ([\d/]+ [\d:]+)");
        if (startMatch.Success) startTime = startMatch.Groups[1].Value;

        // endTime: "Le fichier est traité à 'DD/MM/YYYY HH:MM:SS'"
        string? endTime = null;
        var endMatch = System.Text.RegularExpressions.Regex.Match(lastPrismaLog, @"Le fichier est traité à '([\d/]+ [\d:]+)'");
        if (endMatch.Success) endTime = endMatch.Groups[1].Value;

        // inputFile: "fichier d'entrée : filename.pdf"
        string? inputFile = null;
        var inputMatch = System.Text.RegularExpressions.Regex.Match(lastPrismaLog, @"fichier d'entrée\s*:\s*(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (inputMatch.Success) inputFile = inputMatch.Groups[1].Value.Trim();

        // outputFile: "Fichier de sortie : filename.pdf"
        string? outputFile = null;
        var outputMatch = System.Text.RegularExpressions.Regex.Match(lastPrismaLog, @"Fichier de sortie\s*:\s*(.+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (outputMatch.Success) outputFile = outputMatch.Groups[1].Value.Trim();

        parsedLog = new { warnings, success, operations, startTime, endTime, inputFile, outputFile };
    }

    return Results.Json(new
    {
        inProgress,
        currentFileName = currentFileName ?? "",
        currentStep = currentStep ?? "",
        startedAt = inProgress ? startedAt : (DateTime?)null,
        elapsedSeconds = (int)Math.Round(elapsedSeconds),
        status,
        lastCompletedFileName = lastCompletedFileName ?? "",
        lastCompletedAt = lastCompletedAt.HasValue ? lastCompletedAt : (DateTime?)null,
        lastPrismaLog = lastPrismaLog ?? "",
        parsedLog
    });
});

// GET /api/bat/log/{fileName} — returns raw PrismaPrepare log for a given file
app.MapGet("/api/bat/log/{fileName}", (string fileName) =>
{
    try
    {
        // Strip BAT_ prefix and .pdf extension for matching
        var baseName = fileName;
        if (baseName.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(4);
        if (baseName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            baseName = baseName.Substring(0, baseName.Length - 4);

        // Look in PrismaPrepare output directory
        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations");
        var outputDir2 = !string.IsNullOrWhiteSpace(integCfg?.PrismaPrepareOutputPath)
            ? integCfg!.PrismaPrepareOutputPath
            : IntegrationsSettings.DefaultPrismaPrepareOutputPath;

        if (Directory.Exists(outputDir2))
        {
            var logFile = Directory.GetFiles(outputDir2, "*.log")
                .Where(f => Path.GetFileName(f).Contains(baseName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .FirstOrDefault();

            if (logFile != null)
            {
                var content = File.ReadAllText(logFile, System.Text.Encoding.UTF8);
                var info = new FileInfo(logFile);
                return Results.Json(new { ok = true, logFileName = info.Name, lastModified = info.LastWriteTime, content });
            }
        }

        // Fallback: search MongoDB notifications for prismaLog field
        var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
        var notifDoc = notifCol.Find(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("type", "bat_ready"),
                Builders<BsonDocument>.Filter.Regex("fileName", new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(baseName), "i"))
            )
        ).SortByDescending(d => d["timestamp"]).FirstOrDefault();

        if (notifDoc != null && notifDoc.Contains("prismaLog") && notifDoc["prismaLog"].BsonType == BsonType.String)
        {
            var content = notifDoc["prismaLog"].AsString;
            return Results.Json(new { ok = true, logFileName = (string?)null, lastModified = (DateTime?)null, content, source = "mongodb" });
        }

        return Results.Json(new { ok = false, error = $"Aucun log PrismaPrepare trouvé pour \"{fileName}\"" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/bat/send", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var doc = new BsonDocument { ["fullPath"] = path, ["status"] = "sent", ["sentAt"] = DateTime.UtcNow, ["validatedAt"] = BsonNull.Value, ["rejectedAt"] = BsonNull.Value };
    col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/bat/validate", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var update = Builders<BsonDocument>.Update.Set("status", "validated").Set("validatedAt", DateTime.UtcNow);
    col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/bat/reject", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var update = Builders<BsonDocument>.Update.Set("status", "rejected").Set("rejectedAt", DateTime.UtcNow);
    col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// Process pending BAT output renames: rename "Epreuve PDF.pdf" → {sourceFileName}.Epreuve.pdf
app.MapPost("/api/bat/process-pending", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("batPending");
        var pendingFilter = Builders<BsonDocument>.Filter.Eq("processed", false);
        var pending = col.Find(pendingFilter).ToList();
        var renamed = 0;
        foreach (var doc in pending)
        {
            var sourceName = doc.Contains("sourceFileName") ? doc["sourceFileName"].AsString : "";
            var batFolder = doc.Contains("batFolder") ? doc["batFolder"].AsString : "";
            if (string.IsNullOrEmpty(sourceName) || string.IsNullOrEmpty(batFolder)) continue;
            var epreuveSrc = Path.Combine(batFolder, "Epreuve PDF.pdf");
            if (File.Exists(epreuveSrc))
            {
                var dest = Path.Combine(batFolder, $"{sourceName} Epreuve.pdf");
                try
                {
                    File.Move(epreuveSrc, dest, overwrite: true);
                    col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                        Builders<BsonDocument>.Update.Set("processed", true));
                    renamed++;
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] BAT rename failed: {ex.Message}"); }
            }
        }
        return Results.Json(new { ok = true, renamed });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// BAT COMMAND CONFIG
// ======================================================

app.MapGet("/api/config/bat-command", () =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
    var doc = col.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
    var cmd = doc != null && doc.Contains("command") ? doc["command"].AsString :
        @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe ""{filePath}"" /T ""{type}"" /SP /C {qty}";
    var alertDelayHours = doc != null && doc.Contains("batAlertDelayHours") ? doc["batAlertDelayHours"].AsInt32 : 48;
    var batSimpleDropletPath = doc != null && doc.Contains("batSimpleDropletPath") ? doc["batSimpleDropletPath"].AsString : "";
    return Results.Json(new { ok = true, command = cmd, batAlertDelayHours = alertDelayHours, batSimpleDropletPath });
});

app.MapPut("/api/config/bat-command", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var cmd = json.TryGetProperty("command", out var c) ? c.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
    var existing = col.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
    var doc = existing ?? new BsonDocument();
    doc["command"] = cmd;
    if (json.TryGetProperty("batAlertDelayHours", out var dh))
        doc["batAlertDelayHours"] = dh.ValueKind == JsonValueKind.Number ? dh.GetInt32() : 48;
    if (json.TryGetProperty("batSimpleDropletPath", out var dp))
        doc["batSimpleDropletPath"] = dp.GetString() ?? "";
    col.ReplaceOne(Builders<BsonDocument>.Filter.Empty, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// BAT SIMPLE — Lance le droplet configuré
// ======================================================

app.MapPost("/api/bat/simple", async (HttpContext ctx) =>
{
    try
    {
        var doc2 = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc2.RootElement.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var fullPath = Path.GetFullPath(fpEl.GetString() ?? "");
        if (!File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        var cfgCol = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
        var cfgDoc = cfgCol.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
        var dropletPath = cfgDoc != null && cfgDoc.Contains("batSimpleDropletPath")
            ? cfgDoc["batSimpleDropletPath"].AsString : "";

        if (string.IsNullOrWhiteSpace(dropletPath))
            return Results.Json(new { ok = false, error = "Aucun droplet BAT Simple configuré. Paramétrez-le dans Paramétrage > Configuration BAT." });

        if (!File.Exists(dropletPath))
            return Results.Json(new { ok = false, error = $"Droplet introuvable : {dropletPath}. Vérifiez le chemin dans Paramétrage > Configuration BAT." });

        var psi = new System.Diagnostics.ProcessStartInfo(dropletPath, $"\"{fullPath}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(dropletPath) ?? Path.GetDirectoryName(fullPath) ?? ""
        };
        System.Diagnostics.Process.Start(psi);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// BAT MAIL TEMPLATE
// ======================================================

app.MapGet("/api/config/bat-mail-template", () =>
{
    var cfg = MongoDbHelper.GetSettings<BatMailTemplate>("batMailTemplate") ?? new BatMailTemplate();
    return Results.Json(new { ok = true, template = new { to = cfg.To, subject = cfg.Subject, body = cfg.Body } });
});

app.MapPut("/api/config/bat-mail-template", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var tmpl = json.TryGetProperty("template", out var tEl) ? tEl : json;
        var existing = MongoDbHelper.GetSettings<BatMailTemplate>("batMailTemplate") ?? new BatMailTemplate();

        if (tmpl.TryGetProperty("to", out var toEl)) existing.To = toEl.GetString() ?? existing.To;
        if (tmpl.TryGetProperty("subject", out var subEl)) existing.Subject = subEl.GetString() ?? existing.Subject;
        if (tmpl.TryGetProperty("body", out var bodyEl)) existing.Body = bodyEl.GetString() ?? existing.Body;

        MongoDbHelper.UpsertSettings("batMailTemplate", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ALERTS — BAT EN ATTENTE
// ======================================================

// ======================================================
// IMPORT MAIL DEVIS
// ======================================================
app.MapPost("/api/fabrication/import-mail-devis", async (HttpContext ctx) =>
{
    try
    {
        if (!ctx.Request.HasFormContentType)
            return Results.Json(new { ok = false, error = "Form data required" });

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var fileName = form["fileName"].ToString();

        if (file == null)
            return Results.Json(new { ok = false, error = "Fichier requis" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".eml" && ext != ".msg")
            return Results.Json(new { ok = false, error = "Seuls les fichiers .eml et .msg sont acceptés" });

        // Find production folder for this fabrication file
        var savedName = "";
        if (!string.IsNullOrEmpty(fileName))
        {
            var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
            var pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName))
                             .SortByDescending(x => x["createdAt"]).FirstOrDefault();
            if (pfDoc != null && pfDoc.Contains("folderPath") && !string.IsNullOrEmpty(pfDoc["folderPath"].AsString))
            {
                var folderPath = pfDoc["folderPath"].AsString;
                Directory.CreateDirectory(folderPath);
                var destPath = Path.Combine(folderPath, "mail_validation_devis" + ext);
                using var stream = file.OpenReadStream();
                using var fs = File.Create(destPath);
                await stream.CopyToAsync(fs);
                savedName = file.FileName;

                // Update fabrication record with mail file name
                var fabCol = MongoDbHelper.GetFabricationsCollection();
                var fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
                if (fabDoc != null)
                    fabCol.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", fabDoc["_id"]),
                        Builders<BsonDocument>.Update.Set("mailDevisFileName", file.FileName));
            }
        }

        return Results.Json(new { ok = true, fileName = savedName });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// IMPORT MAIL BAT
// ======================================================
app.MapPost("/api/fabrication/import-mail-bat", async (HttpContext ctx) =>
{
    try
    {
        if (!ctx.Request.HasFormContentType)
            return Results.Json(new { ok = false, error = "Form data required" });

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var fileName = form["fileName"].ToString();

        if (file == null)
            return Results.Json(new { ok = false, error = "Fichier requis" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".eml" && ext != ".msg")
            return Results.Json(new { ok = false, error = "Seuls les fichiers .eml et .msg sont acceptés" });

        var savedName = "";
        if (!string.IsNullOrEmpty(fileName))
        {
            var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
            var pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName))
                             .SortByDescending(x => x["createdAt"]).FirstOrDefault();
            if (pfDoc != null && pfDoc.Contains("folderPath") && !string.IsNullOrEmpty(pfDoc["folderPath"].AsString))
            {
                var folderPath = pfDoc["folderPath"].AsString;
                Directory.CreateDirectory(folderPath);
                var destPath = Path.Combine(folderPath, "mail_validation_bat" + ext);
                using var stream = file.OpenReadStream();
                using var fs = File.Create(destPath);
                await stream.CopyToAsync(fs);
                savedName = file.FileName;

                var fabCol = MongoDbHelper.GetFabricationsCollection();
                var fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
                if (fabDoc != null)
                    fabCol.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", fabDoc["_id"]),
                        Builders<BsonDocument>.Update.Set("mailBatFileName", file.FileName));
            }
        }

        return Results.Json(new { ok = true, fileName = savedName });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});


// ======================================================
// BAT PAPIER — same process as BAT Complet, dedicated mail template
// ======================================================
app.MapPost("/api/bat/bat-papier", async (HttpContext ctx) =>
{
    bool lockAcquired = false;
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var requestedBy = json.TryGetProperty("requestedBy", out var rb) ? rb.GetString() ?? "" : "";

        var displayName = !string.IsNullOrEmpty(fileName) ? fileName : (Path.GetFileName(fullPath) ?? "unknown");
        var correlationId = Guid.NewGuid().ToString("N").Substring(0, 16);
        if (!BatSerializationState.TryAcquire(displayName, correlationId))
        {
            var (_, currentFile, _, _, _) = BatSerializationState.Get();
            return Results.Json(new { ok = false, error = "bat_in_progress", message = $"Un BAT est en cours de génération pour \"{currentFile}\". Veuillez patienter." });
        }
        lockAcquired = true;

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? fabDoc = null;
        if (!string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fullPath))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Regex("fileName",
                new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(fileName)}$", "i"))).FirstOrDefault();

        if (fabDoc == null)
            return Results.Json(new { ok = false, error = $"Fiche de fabrication introuvable pour \"{fileName}\"." });

        var typeTravail = fabDoc.Contains("typeTravail") && fabDoc["typeTravail"].BsonType == BsonType.String
            ? fabDoc["typeTravail"].AsString ?? "" : "";

        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "Type de travail non défini dans la fiche de fabrication." });

        var routingCol = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var routingDoc = routingCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
        if (routingDoc == null || !routingDoc.Contains("hotfolderPath") ||
            routingDoc["hotfolderPath"].BsonType != BsonType.String ||
            string.IsNullOrEmpty(routingDoc["hotfolderPath"].AsString))
            return Results.Json(new { ok = false, error = $"Aucun hotfolder PrismaPrepare configuré pour le type \"{typeTravail}\"." });

        var hotfolderPath = routingDoc["hotfolderPath"].AsString;

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            var found = Directory.GetFiles(hotRoot, string.IsNullOrEmpty(fileName) ? "*" : fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) fullPath = found;
        }
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier source introuvable : {fileName}" });

        var sourceBaseName = Path.GetFileNameWithoutExtension(fullPath);
        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations") ?? new IntegrationsSettings();
        var tempCopyPath = integCfg.TempCopyPath ?? "";
        if (string.IsNullOrWhiteSpace(tempCopyPath))
            return Results.Json(new { ok = false, error = "Chemin TEMP_COPY non configuré." });

        Directory.CreateDirectory(tempCopyPath);
        BatSerializationState.SetStep("copying_to_temp");
        var tempCopyDest = Path.Combine(tempCopyPath, Path.GetFileName(fullPath));
        File.Copy(fullPath, tempCopyDest, overwrite: true);

        if (!Directory.Exists(hotfolderPath)) Directory.CreateDirectory(hotfolderPath);
        var hotfolderFileName = $"{sourceBaseName}__BATPAPIER_{correlationId}.pdf";
        var hotfolderDest = Path.Combine(hotfolderPath, hotfolderFileName);
        File.Copy(fullPath, hotfolderDest, overwrite: true);
        BatSerializationState.SetStep("sent_to_hotfolder");

        try
        {
            var batPendingCol = MongoDbHelper.GetCollection<BsonDocument>("batPending");
            batPendingCol.InsertOne(new BsonDocument
            {
                ["sourceFileName"] = sourceBaseName,
                ["batFolder"] = tempCopyPath,
                ["createdAt"] = DateTime.UtcNow,
                ["processed"] = false,
                ["requestedBy"] = requestedBy,
                ["correlationId"] = correlationId,
                ["batType"] = "papier"
            });
        }
        catch { /* non-blocking */ }

        BatSerializationState.SetStep("waiting_for_epreuve");
        lockAcquired = false;
        return Results.Json(new { ok = true, hotfolder = hotfolderPath, tempCopy = tempCopyPath, typeTravail });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
    finally
    {
        if (lockAcquired) BatSerializationState.Release();
    }
});

// ======================================================
// MAIL TEMPLATES — Production start / end / BAT papier
// ======================================================

app.MapGet("/api/config/mail-template-production-start", () =>
{
    var cfg = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateProductionStart")
        ?? new BatMailTemplate { Subject = "Début de production — Dossier {{numeroDossier}}", Body = "Bonjour,\n\nLa production de votre dossier {{numeroDossier}} vient de démarrer.\n\nCordialement," };
    return Results.Json(new { ok = true, template = new { to = cfg.To, subject = cfg.Subject, body = cfg.Body } });
});

app.MapPut("/api/config/mail-template-production-start", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var tmpl = json.TryGetProperty("template", out var tEl) ? tEl : json;
        var existing = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateProductionStart") ?? new BatMailTemplate();
        if (tmpl.TryGetProperty("to", out var toEl)) existing.To = toEl.GetString() ?? existing.To;
        if (tmpl.TryGetProperty("subject", out var subEl)) existing.Subject = subEl.GetString() ?? existing.Subject;
        if (tmpl.TryGetProperty("body", out var bodyEl)) existing.Body = bodyEl.GetString() ?? existing.Body;
        MongoDbHelper.UpsertSettings("mailTemplateProductionStart", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/config/mail-template-production-end", () =>
{
    var cfg = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateProductionEnd")
        ?? new BatMailTemplate { Subject = "Fin de production — Dossier {{numeroDossier}}", Body = "Bonjour,\n\nLa production de votre dossier {{numeroDossier}} est terminée.\n\nCordialement," };
    return Results.Json(new { ok = true, template = new { to = cfg.To, subject = cfg.Subject, body = cfg.Body } });
});

app.MapPut("/api/config/mail-template-production-end", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var tmpl = json.TryGetProperty("template", out var tEl) ? tEl : json;
        var existing = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateProductionEnd") ?? new BatMailTemplate();
        if (tmpl.TryGetProperty("to", out var toEl)) existing.To = toEl.GetString() ?? existing.To;
        if (tmpl.TryGetProperty("subject", out var subEl)) existing.Subject = subEl.GetString() ?? existing.Subject;
        if (tmpl.TryGetProperty("body", out var bodyEl)) existing.Body = bodyEl.GetString() ?? existing.Body;
        MongoDbHelper.UpsertSettings("mailTemplateProductionEnd", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/config/mail-template-bat-papier", () =>
{
    var cfg = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateBatPapier")
        ?? new BatMailTemplate { Subject = "BAT Papier — Dossier {{numeroDossier}}", Body = "Bonjour,\n\nVeuillez trouver ci-joint le BAT papier pour le dossier {{numeroDossier}}.\n\nCordialement," };
    return Results.Json(new { ok = true, template = new { to = cfg.To, subject = cfg.Subject, body = cfg.Body } });
});

app.MapPut("/api/config/mail-template-bat-papier", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var tmpl = json.TryGetProperty("template", out var tEl) ? tEl : json;
        var existing = MongoDbHelper.GetSettings<BatMailTemplate>("mailTemplateBatPapier") ?? new BatMailTemplate();
        if (tmpl.TryGetProperty("to", out var toEl)) existing.To = toEl.GetString() ?? existing.To;
        if (tmpl.TryGetProperty("subject", out var subEl)) existing.Subject = subEl.GetString() ?? existing.Subject;
        if (tmpl.TryGetProperty("body", out var bodyEl)) existing.Body = bodyEl.GetString() ?? existing.Body;
        MongoDbHelper.UpsertSettings("mailTemplateBatPapier", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// PUT /api/fabrication/statut-production — Met à jour le statut de production (valide/refuse)
app.MapPut("/api/fabrication/statut-production", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string userName = "Système";
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length >= 3)
                {
                    var users = BackendUtils.LoadUsers();
                    var u = users.FirstOrDefault(x => x.Id == parts[0]);
                    if (u != null) userName = u.Name;
                }
            }
            catch { }
        }

        var json = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fnEl) ? fnEl.GetString() ?? "" : "";
        var statut = json.TryGetProperty("statut", out var stEl) ? stEl.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(fileName))
            return Results.Json(new { ok = false, error = "fileName requis" });

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName.ToLowerInvariant());
        var doc = fabCol.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
        if (doc == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        var update = Builders<BsonDocument>.Update.Set("statutProduction", statut);
        fabCol.UpdateOne(filter, update);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// FABRICATION — CALENDAR EVENTS (dates clés → planning)
// ======================================================
app.MapGet("/api/fabrication/events", () =>
{
    try
    {
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        // Get all fabrication docs that have at least one key date set
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("dateEnvoi"),
            Builders<BsonDocument>.Filter.Exists("dateImpression"),
            Builders<BsonDocument>.Filter.Exists("dateProductionFinitions")
        );
        var docs = fabCol.Find(filter).ToList();

        var events = new List<object>();
        foreach (var doc in docs)
        {
            var fileName = doc.Contains("fileName") ? doc["fileName"].AsString : "";
            var fullPath = doc.Contains("fullPath") ? doc["fullPath"].AsString : "";
            var numeroDossier = doc.Contains("numeroDossier") && doc["numeroDossier"] != BsonNull.Value ? doc["numeroDossier"].AsString : "";
            var client = doc.Contains("client") && doc["client"] != BsonNull.Value ? doc["client"].AsString : "";
            var moteurImpression = doc.Contains("moteurImpression") && doc["moteurImpression"] != BsonNull.Value ? doc["moteurImpression"].AsString : "";
            var operateur = doc.Contains("operateur") && doc["operateur"] != BsonNull.Value ? doc["operateur"].AsString : "";
            var title = !string.IsNullOrWhiteSpace(numeroDossier) ? $"#{numeroDossier} {client}" : fileName;

            if (doc.Contains("dateEnvoi") && doc["dateEnvoi"] != BsonNull.Value)
            {
                try {
                    var dt = doc["dateEnvoi"].ToUniversalTime();
                    events.Add(new { type = "envoi", date = dt.ToString("yyyy-MM-dd"), title = $"📤 Envoi: {title}", fileName, fullPath, moteurImpression, operateur });
                } catch { }
            }
            if (doc.Contains("dateImpression") && doc["dateImpression"] != BsonNull.Value)
            {
                try {
                    var dt = doc["dateImpression"].ToUniversalTime();
                    events.Add(new { type = "impression", date = dt.ToString("yyyy-MM-dd"), title = $"🖨️ Impression: {title}", fileName, fullPath, moteurImpression, operateur });
                } catch { }
            }
            if (doc.Contains("dateProductionFinitions") && doc["dateProductionFinitions"] != BsonNull.Value)
            {
                try {
                    var dt = doc["dateProductionFinitions"].ToUniversalTime();
                    events.Add(new { type = "finitions", date = dt.ToString("yyyy-MM-dd"), title = $"✂️ Finitions: {title}", fileName, fullPath, moteurImpression, operateur });
                } catch { }
            }
        }
        return Results.Json(new { ok = true, events });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, events = new object[0] });
    }
});

    }
}
