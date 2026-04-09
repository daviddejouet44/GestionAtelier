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
            if (!string.IsNullOrEmpty(v.FileName))
            {
                // Normalize to lowercase (fabrication records store fileName as lowercase via fnKey)
                var lowerFn = v.FileName.ToLowerInvariant();
                var fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", lowerFn)).FirstOrDefault();
                if (fabDoc == null && !string.IsNullOrEmpty(v.FullPath))
                    fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", v.FullPath)).FirstOrDefault();
                if (fabDoc != null && fabDoc.Contains("locked") && fabDoc["locked"] != BsonNull.Value
                    && fabDoc["locked"].BsonType == BsonType.Boolean)
                    locked = fabDoc["locked"].AsBoolean;
            }
            return new
            {
                fullPath = v.FullPath,
                fileName = v.FileName,
                date     = v.Date.ToString("yyyy-MM-dd"),
                time     = v.Time,
                locked
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
// FABRICATION
// ======================================================


    }
}
