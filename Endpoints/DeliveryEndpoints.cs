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

namespace GestionAtelier.Endpoints;

public static class DeliveryEndpointsExtensions
{
    public static void MapDeliveryEndpoints(this WebApplication app)
    {
app.MapGet("/api/delivery", () =>
{
    var map = BackendUtils.LoadDeliveries();
    var fabCol = MongoDbHelper.GetFabricationsCollection();

    var data = map.Values
        .Select(v => {
            bool locked = false;
            int? tempsProduitMinutes = null;
            string? dateReceptionSouhaitee = null;
            if (!string.IsNullOrEmpty(v.FileName))
            {
                // Normalize to lowercase (fabrication records store fileName as lowercase via fnKey)
                var lowerFn = v.FileName.ToLowerInvariant();
                var fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", lowerFn)).FirstOrDefault();
                if (fabDoc == null && !string.IsNullOrEmpty(v.FullPath))
                    fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", v.FullPath)).FirstOrDefault();
                if (fabDoc != null)
                {
                    if (fabDoc.Contains("locked") && fabDoc["locked"] != BsonNull.Value
                        && fabDoc["locked"].BsonType == BsonType.Boolean)
                        locked = fabDoc["locked"].AsBoolean;
                    if (fabDoc.Contains("tempsProduitMinutes") && fabDoc["tempsProduitMinutes"] != BsonNull.Value)
                        tempsProduitMinutes = fabDoc["tempsProduitMinutes"].AsInt32;
                    if (fabDoc.Contains("dateReceptionSouhaitee") && fabDoc["dateReceptionSouhaitee"] != BsonNull.Value)
                    {
                        var drVal = fabDoc["dateReceptionSouhaitee"];
                        if (drVal.BsonType == BsonType.DateTime)
                            dateReceptionSouhaitee = drVal.ToUniversalTime().ToString("yyyy-MM-dd");
                        else if (drVal.BsonType == BsonType.String)
                        {
                            var raw = drVal.AsString;
                            if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 10)
                                dateReceptionSouhaitee = raw.Substring(0, 10);
                        }
                    }
                }
            }
            return new
            {
                fullPath = v.FullPath,
                fileName = v.FileName,
                date     = v.Date.ToString("yyyy-MM-dd"),
                time     = v.Time,
                locked,
                tempsProduitMinutes,
                dateReceptionSouhaitee
            };
        });
    return Results.Json(data);
});

app.MapPut("/api/delivery", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("date", out var dEl))
            return Results.BadRequest("date requis.");

        var dateStr = dEl.GetString()!;
        if (!DateTime.TryParse(dateStr, out var dt))
            return Results.BadRequest("Format date invalide.");

        var time = "09:00";
        if (json.TryGetProperty("time", out var tEl) && tEl.ValueKind != JsonValueKind.Null)
            time = tEl.GetString() ?? "09:00";

        // Accept fileName (preferred) or derive it from fullPath
        string fileNameKey = "";
        string fullPathVal = "";
        if (json.TryGetProperty("fileName", out var fnEl) && !string.IsNullOrWhiteSpace(fnEl.GetString()))
        {
            fileNameKey = fnEl.GetString()!;
        }
        else if (json.TryGetProperty("fullPath", out var fpEl) && !string.IsNullOrWhiteSpace(fpEl.GetString()))
        {
            fullPathVal = fpEl.GetString()!;
            fileNameKey = Path.GetFileName(fullPathVal);
        }

        if (string.IsNullOrWhiteSpace(fileNameKey))
            return Results.BadRequest("fileName ou fullPath requis.");

        // Resolve fullPath if we only have fileName
        if (string.IsNullOrWhiteSpace(fullPathVal))
        {
            // Try to locate the file
            var root = BackendUtils.HotfoldersRoot();
            foreach (var folder in new[] { "Soumission", "Début de production", "Corrections", "Corrections et fond perdu",
                "Rapport", "Prêt pour impression", "BAT", "PrismaPrepare", "Fiery", "Impression en cours", "Façonnage", "Fin de production" })
            {
                var tryPath = Path.Combine(root, folder, fileNameKey);
                if (File.Exists(tryPath)) { fullPathVal = tryPath; break; }
            }
            if (string.IsNullOrWhiteSpace(fullPathVal))
                fullPathVal = fileNameKey; // Use fileName as placeholder if not found
        }
        else
        {
            // Normalize the provided fullPath
            try { fullPathVal = Path.GetFullPath(fullPathVal); } catch { }
        }

        var delivery = new DeliveryItem
        {
            FullPath = fullPathVal,
            FileName = fileNameKey,
            Date     = dt.Date,
            Time     = time
        };
        BackendUtils.UpsertDelivery(delivery);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/delivery", (HttpContext ctx) =>
{
    try
    {
        // Accept fileName (preferred) or fullPath for backward compatibility
        var fileName = ctx.Request.Query["fileName"].ToString();
        var fullPath = ctx.Request.Query["fullPath"].ToString();

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            // Try deletion by fileName first, then by fullPath fallback (handles old records without fileName field)
            if (BackendUtils.DeleteDeliveryByFileNameOrPath(fileName))
                return Results.Json(new { ok = true });
        }
        else if (!string.IsNullOrWhiteSpace(fullPath))
        {
            if (BackendUtils.DeleteDelivery(fullPath))
                return Results.Json(new { ok = true });
        }

        return Results.Json(new { ok = false, error = "Aucune livraison trouvée." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// URGENCES — jobs groupés par moteur d'impression (livraison dans les 3 prochains jours)
// ======================================================
app.MapGet("/api/urgences", () =>
{
    try
    {
        var map = BackendUtils.LoadDeliveries();
        var today = DateTime.Today;
        var fabCol = MongoDbHelper.GetFabricationsCollection();

        // Build a set of all known files in hotfolders (one scan pass) for orphan detection
        var root = BackendUtils.HotfoldersRoot();
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            foreach (var folder in Directory.GetDirectories(root))
                foreach (var file in Directory.EnumerateFiles(folder))
                    knownFiles.Add(Path.GetFileName(file));
        }

        // Group urgent entries (livraison dans 0..3 jours) by moteurImpression
        var grouped = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in map.Values)
        {
            if (kv.Date == default) continue;
            var delivDate = kv.Date.Date;
            var diff = (delivDate - today).Days;
            if (diff < 0 || diff > 3) continue;

            // Garbage-collect: skip orphaned deliveries (file no longer exists)
            bool fileExists =
                (!string.IsNullOrWhiteSpace(kv.FullPath) && File.Exists(kv.FullPath)) ||
                (!string.IsNullOrWhiteSpace(kv.FileName) && knownFiles.Contains(kv.FileName));
            if (!fileExists) continue;

            // Look up fabrication for this entry
            BsonDocument? fabDoc = null;
            if (!string.IsNullOrEmpty(kv.FileName))
                fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", kv.FileName)).FirstOrDefault();
            if (fabDoc == null && !string.IsNullOrEmpty(kv.FullPath))
                fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", kv.FullPath)).FirstOrDefault();

            var moteur = (fabDoc != null && fabDoc.Contains("moteurImpression") && fabDoc["moteurImpression"].BsonType == BsonType.String)
                ? fabDoc["moteurImpression"].AsString ?? "—" : "—";
            var numeroDossier = (fabDoc != null && fabDoc.Contains("numeroDossier") && fabDoc["numeroDossier"].BsonType == BsonType.String)
                ? fabDoc["numeroDossier"].AsString ?? "" : "";
            var nomClient = (fabDoc != null && fabDoc.Contains("nomClient") && fabDoc["nomClient"].BsonType == BsonType.String)
                ? fabDoc["nomClient"].AsString ?? "" : "";
            DateTime? planningMachine = null;
            if (fabDoc != null && fabDoc.Contains("planningMachine") && fabDoc["planningMachine"].BsonType == BsonType.DateTime)
                planningMachine = fabDoc["planningMachine"].ToUniversalTime().ToLocalTime();

            bool termine = (fabDoc != null && fabDoc.Contains("locked") && fabDoc["locked"].BsonType == BsonType.Boolean && fabDoc["locked"].AsBoolean);

            var entry = (object)new
            {
                fileName = kv.FileName ?? Path.GetFileName(kv.FullPath) ?? "",
                numeroDossier,
                nomClient,
                dateLivraison = kv.Date.ToString("yyyy-MM-dd"),
                datePlanningMachine = planningMachine.HasValue ? planningMachine.Value.ToString("yyyy-MM-dd") : null,
                diff,
                termine
            };

            if (!grouped.ContainsKey(moteur))
                grouped[moteur] = new List<object>();
            grouped[moteur].Add(entry);
        }

        // Sort each group by delivery date
        var result = grouped
            .OrderBy(g => g.Key == "—" ? 1 : 0)
            .ThenBy(g => g.Key)
            .Select(g => new
            {
                moteur = g.Key,
                jobs = g.Value.OrderBy(j => ((dynamic)j).dateLivraison).ToList<object>()
            })
            .ToList();

        return Results.Json(new { ok = true, groups = result });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, groups = Array.Empty<object>() });
    }
});

// ======================================================
// PRISMASYNC URL SETTING
// ======================================================
app.MapGet("/api/config/prismasync-url", () =>
{
    var doc = MongoDbHelper.GetCollection<BsonDocument>("appSettings")
        .Find(Builders<BsonDocument>.Filter.Eq("key", "prismaSyncUrl")).FirstOrDefault();
    var url = (doc != null && doc.Contains("value") && doc["value"].BsonType == BsonType.String)
        ? doc["value"].AsString ?? "" : "";
    return Results.Json(new { ok = true, url });
});

app.MapPut("/api/config/prismasync-url", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var url = json.TryGetProperty("url", out var uEl) ? uEl.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("appSettings");
        col.ReplaceOne(
            Builders<BsonDocument>.Filter.Eq("key", "prismaSyncUrl"),
            new BsonDocument { ["key"] = "prismaSyncUrl", ["value"] = url },
            new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});
// ======================================================
// CLEANUP — suppression des livraisons orphelines
// (fichier inexistant sur le disque)
// ======================================================
app.MapPost("/api/delivery/cleanup-orphans", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var map = BackendUtils.LoadDeliveries();
        var root = BackendUtils.HotfoldersRoot();

        // Build a single set of all filenames present in hotfolders (one scan pass)
        var knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            foreach (var folder in Directory.GetDirectories(root))
                foreach (var file in Directory.EnumerateFiles(folder))
                    knownFiles.Add(Path.GetFileName(file));
        }

        var deleted = 0;
        foreach (var kv in map)
        {
            var item = kv.Value;
            // A delivery is not orphaned if the file still exists at its known path or anywhere in hotfolders
            bool fileExists =
                (!string.IsNullOrWhiteSpace(item.FullPath) && File.Exists(item.FullPath)) ||
                (!string.IsNullOrWhiteSpace(item.FileName) && knownFiles.Contains(item.FileName));

            if (!fileExists)
            {
                // DeleteDeliveryByFileNameOrPath handles filename-keyed lookup
                BackendUtils.DeleteDeliveryByFileNameOrPath(item.FileName ?? "");
                // DeleteDelivery handles path-keyed lookup (legacy records)
                if (!string.IsNullOrWhiteSpace(item.FullPath))
                    BackendUtils.DeleteDelivery(item.FullPath);
                deleted++;
            }
        }

        return Results.Json(new { ok = true, deleted });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});


    }
}
