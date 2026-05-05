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
using GestionAtelier.Constants;

namespace GestionAtelier.Endpoints;

public static class DossiersEndpointsExtensions
{
    public static void MapDossiersEndpoints(this WebApplication app)
    {

app.MapGet("/api/production-folders", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var docs = col.Find(new BsonDocument()).SortByDescending(x => x["createdAt"]).ToList();

        // Orphan cleanup: remove folders whose physical directory no longer exists
        var idsToDelete = new List<ObjectId>();
        var hotRoot = BackendUtils.HotfoldersRoot();
        foreach (var d in docs)
        {
            var fp = d.Contains("folderPath") ? d["folderPath"].AsString : "";
            if (!string.IsNullOrEmpty(fp) && !Directory.Exists(fp))
                idsToDelete.Add(d["_id"].AsObjectId);
        }
        if (idsToDelete.Count > 0)
        {
            col.DeleteMany(Builders<BsonDocument>.Filter.In("_id", idsToDelete));
            docs = docs.Where(d => !idsToDelete.Contains(d["_id"].AsObjectId)).ToList();
        }

        var result = docs.Select(d =>
        {
            var fileName = d.Contains("fileName") ? d["fileName"].AsString : "";
            var numeroDossier = d.Contains("numeroDossier") && d["numeroDossier"] != BsonNull.Value ? d["numeroDossier"].AsString : "";

            // Enrich numeroDossier from fabrication sheet if not set on production folder
            if (string.IsNullOrEmpty(numeroDossier) && !string.IsNullOrEmpty(fileName))
            {
                try
                {
                    var fab = BackendUtils.FindFabricationByName(fileName);
                    if (fab != null && !string.IsNullOrEmpty(fab.NumeroDossier))
                    {
                        numeroDossier = fab.NumeroDossier;
                        // Persist the synced value back to avoid future N+1 lookups
                        col.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", d["_id"]),
                            Builders<BsonDocument>.Update.Set("numeroDossier", numeroDossier));
                    }
                }
                catch { /* non-fatal */ }
            }

            return new
            {
                _id = d["_id"].ToString(),
                number = d.Contains("number") && d["number"] != BsonNull.Value ? d["number"].AsInt32 : 0,
                numeroDossier,
                fileName,
                folderPath = d.Contains("folderPath") ? d["folderPath"].AsString : "",
                createdAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : DateTime.MinValue,
                currentStage = d.Contains("currentStage") ? d["currentStage"].AsString : "",
                files = d.Contains("files") ? d["files"].AsBsonArray.Count : 0
            };
        }).ToList();
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/production-folders/{id}", (string id) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.Json(new { ok = false, error = "Dossier introuvable" });

        var files = new List<object>();
        if (doc.Contains("files"))
        {
            foreach (BsonDocument f in doc["files"].AsBsonArray)
            {
                files.Add(new
                {
                    stage = f.Contains("stage") ? f["stage"].AsString : "",
                    fileName = f.Contains("fileName") ? f["fileName"].AsString : "",
                    addedAt = f.Contains("addedAt") ? f["addedAt"].ToUniversalTime() : DateTime.MinValue
                });
            }
        }

        var fab = doc.Contains("fabricationSheet") ? doc["fabricationSheet"].AsBsonDocument : new BsonDocument();
        var fabricationSheet = new Dictionary<string, string?>();
        foreach (var el in fab.Elements)
            fabricationSheet[el.Name] = el.Value.ToString();

        return Results.Json(new
        {
            _id = doc["_id"].ToString(),
            number = doc.Contains("number") && doc["number"] != BsonNull.Value ? doc["number"].AsInt32 : 0,
            numeroDossier = doc.Contains("numeroDossier") && doc["numeroDossier"] != BsonNull.Value ? doc["numeroDossier"].AsString : "",
            fileName = doc.Contains("fileName") ? doc["fileName"].AsString : "",
            folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "",
            originalFilePath = doc.Contains("originalFilePath") ? doc["originalFilePath"].AsString : "",
            currentFilePath = doc.Contains("currentFilePath") ? doc["currentFilePath"].AsString : "",
            createdAt = doc.Contains("createdAt") ? doc["createdAt"].ToUniversalTime() : DateTime.MinValue,
            currentStage = doc.Contains("currentStage") ? doc["currentStage"].AsString : "",
            fabricationSheet,
            files
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/production-folders/{id}", async (string id, HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));

        var updates = new List<UpdateDefinition<BsonDocument>>();

        if (json.TryGetProperty("currentStage", out var stageEl) && stageEl.ValueKind == JsonValueKind.String)
            updates.Add(Builders<BsonDocument>.Update.Set("currentStage", stageEl.GetString()));

        if (json.TryGetProperty("fabricationSheet", out var fabEl) && fabEl.ValueKind == JsonValueKind.Object)
        {
            var fabDoc = new BsonDocument();
            foreach (var prop in fabEl.EnumerateObject())
                fabDoc[prop.Name] = prop.Value.GetString() ?? "";
            updates.Add(Builders<BsonDocument>.Update.Set("fabricationSheet", fabDoc));
        }

        if (updates.Count > 0)
        {
            var combined = Builders<BsonDocument>.Update.Combine(updates);
            await col.UpdateOneAsync(filter, combined);
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/production-folders/{id}/upload", async (string id, HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        if (!form.Files.Any())
            return Results.Json(new { ok = false, error = "Aucun fichier" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.Json(new { ok = false, error = "Dossier introuvable" });

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        foreach (var file in form.Files)
        {
            var destPath = Path.Combine(folderPath, Path.GetFileName(file.FileName));
            using var fs = new FileStream(destPath, FileMode.Create);
            await file.CopyToAsync(fs);

            var fileEntry = new BsonDocument
            {
                ["stage"] = "Fichier ajouté",
                ["fileName"] = Path.GetFileName(file.FileName),
                ["addedAt"] = DateTime.UtcNow
            };
            await col.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Push("files", fileEntry));
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/production-folders/{id}/files/{filename}", (string id, string filename) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.NotFound();

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        var safeName = Path.GetFileName(filename);
        var full = Path.Combine(folderPath, safeName);
        if (!File.Exists(full)) return Results.NotFound();

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(full, out var ct)) ct = "application/octet-stream";
        return Results.File(File.OpenRead(full), ct, safeName);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// PRODUCTION FOLDERS — GLOBAL PROGRESS

// ======================================================
app.MapGet("/api/production-folders/global-progress", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var docs = col.Find(new BsonDocument()).SortByDescending(x => x["createdAt"]).ToList();
        var result = docs.Select(d =>
        {
            var stage = d.Contains("currentStage") ? d["currentStage"].AsString : "";
            return new
            {
                _id = d["_id"].ToString(),
                number = d.Contains("number") ? d["number"].AsInt32 : 0,
                fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
                numeroDossier = d.Contains("numeroDossier") ? d["numeroDossier"].AsString : "",
                currentStage = stage,
                progress = StageConstants.GetProgress(stage)
            };
        }).ToList();
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// PRODUCTION — Summary (physical scan of all production folders)
// ======================================================
app.MapGet("/api/production/summary", () =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var productionFolders = new[]
        {
            "Début de production", "Corrections", "Corrections et fond perdu",
            "Prêt pour impression", "BAT", "PrismaPrepare", "Fiery",
            "Impression en cours", "Façonnage", "Fin de production"
        };

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var allFabs = fabCol.Find(new BsonDocument()).ToList();

        // Load BAT status collection (batStatus) for status lookup
        var batTrackCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
        var allBatTracking = batTrackCol.Find(new BsonDocument()).ToList();

        // Build a set of BAT_ files found in the BAT folder for quick lookup
        var batDir = Path.Combine(root, "BAT");
        var batFilesInBatFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(batDir))
        {
            foreach (var f in Directory.EnumerateFiles(batDir))
            {
                var n = Path.GetFileName(f);
                if (n.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase))
                    batFilesInBatFolder.Add(n);
            }
        }

        var entries = new List<object>();
        foreach (var folder in productionFolders)
        {
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var filePath in Directory.EnumerateFiles(dir))
            {
                var fName = Path.GetFileName(filePath);
                // Skip BAT_ files — they are represented as a stage of the original job
                if (fName.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Determine effective stage: if BAT_{fName} exists in the BAT folder AND the file is
                // still in a pre-BAT folder (Prêt pour impression / Corrections), mark stage as BAT.
                // Once the file has progressed beyond BAT (e.g. to Façonnage or Fin de production)
                // the BAT_ file is a historical artifact — do NOT override the actual current stage.
                var effectiveStage = folder;
                var preBatFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "Prêt pour impression", "Corrections", "Corrections et fond perdu" };
                if (preBatFolders.Contains(folder))
                {
                    var batVariant = "BAT_" + fName;
                    if (batFilesInBatFolder.Contains(batVariant))
                        effectiveStage = "BAT";
                }

                // Find matching fabrication by fileName or fullPath (guard against BsonNull values)
                var fab = allFabs.FirstOrDefault(f =>
                    (f.Contains("fileName") && f["fileName"] != BsonNull.Value &&
                     string.Equals(f["fileName"].AsString, fName, StringComparison.OrdinalIgnoreCase)) ||
                    (f.Contains("fullPath") && f["fullPath"] != BsonNull.Value &&
                     string.Equals(Path.GetFileName(f["fullPath"].AsString), fName, StringComparison.OrdinalIgnoreCase)));

                var numeroDossier = fab != null && fab.Contains("numeroDossier") && fab["numeroDossier"] != BsonNull.Value ? fab["numeroDossier"].AsString : "";
                var client = fab != null && fab.Contains("client") && fab["client"] != BsonNull.Value ? fab["client"].AsString : "";
                var typeTravail = fab != null && fab.Contains("typeTravail") && fab["typeTravail"] != BsonNull.Value ? fab["typeTravail"].AsString : "";

                // Resolve BAT sub-status (envoyé / validé / refusé) when stage is BAT
                string? batStatus = null;
                if (effectiveStage == "BAT")
                {
                    var batDoc = allBatTracking
                        .Where(d => {
                            if (!d.Contains("fullPath") || d["fullPath"] == BsonNull.Value) return false;
                            var docFn = Path.GetFileName(d["fullPath"].AsString);
                            // Strip BAT_ prefix used in the BAT folder
                            if (docFn.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase)) docFn = docFn.Substring(4);
                            return string.Equals(docFn, fName, StringComparison.OrdinalIgnoreCase);
                        })
                        .OrderByDescending(d => d["_id"].AsObjectId.CreationTime)
                        .FirstOrDefault();
                    if (batDoc != null)
                    {
                        if (batDoc.Contains("rejectedAt") && batDoc["rejectedAt"] != BsonNull.Value)
                            batStatus = "refuse";
                        else if (batDoc.Contains("validatedAt") && batDoc["validatedAt"] != BsonNull.Value)
                            batStatus = "valide";
                        else if (batDoc.Contains("sentAt") && batDoc["sentAt"] != BsonNull.Value)
                            batStatus = "envoye";
                    }
                }

                entries.Add(new
                {
                    fileName = fName,
                    fullPath = filePath,
                    currentStage = effectiveStage,
                    progress = StageConstants.GetProgress(effectiveStage),
                    numeroDossier,
                    client,
                    typeTravail,
                    batStatus
                });
            }
        }

        return Results.Json(entries);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// CONFIG — Schedule (plages horaires + jours fériés)
// ======================================================


    }
}
