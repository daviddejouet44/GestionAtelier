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
using GestionAtelier.Endpoints.Fabrication;

namespace GestionAtelier.Endpoints.Jobs;

public static class JobsMoveEndpoints
{
    public static void MapJobsMoveEndpoints(this WebApplication app, string recyclePath)
    {
app.MapPost("/api/jobs/move", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();

        static string NormalizeFs(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            var s = Uri.UnescapeDataString(p);
            s = s.Replace('/', '\\');
            s = s.Replace("\u00A0", " ");
            try { 
                return Path.GetFullPath(s); 
            }
            catch { 
                return s; 
            }
        }

        static (bool ok, string? moved, string? error) MoveOne(string? src, string folder, bool overwrite)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(src)) return (false, null, "Source vide.");
                
                Console.WriteLine($"[DEBUG] MoveOne: src={src}, folder={folder}");
                
                var srcDir = Path.GetDirectoryName(src);
                if (Directory.Exists(srcDir))
                {
                    Console.WriteLine($"[DEBUG] Fichiers dans {srcDir}:");
                    foreach (var f in Directory.GetFiles(srcDir))
                    {
                        Console.WriteLine($"  - {f}");
                    }
                }
                
                var root    = BackendUtils.HotfoldersRoot();
                var destDir = Path.Combine(root, folder);
                Directory.CreateDirectory(destDir);
                var dst = Path.Combine(destDir, Path.GetFileName(src));
                
                Console.WriteLine($"[DEBUG] File.Exists({src}) = {File.Exists(src)}");
                
                if (!File.Exists(src)) return (false, null, "Fichier introuvable.");
                File.Move(src, dst, overwrite);
                return (true, Path.GetFullPath(dst), null);
            }
            catch (Exception e) { 
                Console.WriteLine($"[DEBUG] MoveOne exception: {e.Message}");
                return (false, null, e.Message); 
            }
        }

        if (json.TryGetProperty("source", out var s) &&
            json.TryGetProperty("destination", out var d))
        {
            var src       = NormalizeFs(s.GetString());
            var dstFolder = d.GetString() ?? "";
            var overwrite = json.TryGetProperty("overwrite", out var ow) && ow.GetBoolean();
            
            Console.WriteLine($"[DEBUG] /api/jobs/move called");
            Console.WriteLine($"[DEBUG] src (raw) = {s.GetString()}");
            Console.WriteLine($"[DEBUG] src (normalized) = {src}");
            Console.WriteLine($"[DEBUG] dstFolder = {dstFolder}");

            // Validate finition steps when moving OUT of Façonnage (Finitions) tile
            try
            {
                var srcDir = Path.GetFileName(Path.GetDirectoryName(src) ?? "");
                bool movingOutOfFinitions = string.Equals(srcDir, "Façonnage", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(dstFolder.Trim(), "Façonnage", StringComparison.OrdinalIgnoreCase);

                if (movingOutOfFinitions)
                {
                    var fileName = Path.GetFileName(src).ToLowerInvariant();
                    var fabCol   = MongoDbHelper.GetFabricationsCollection();
                    var fabDoc   = fabCol.Find(Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("fullPath", src),
                        Builders<BsonDocument>.Filter.Eq("fileName", fileName)
                    )).SortByDescending(x => x["_id"]).FirstOrDefault();

                    if (fabDoc != null)
                    {
                        var missing = FinitionStepsEndpoints.GetMissingSteps(fabDoc);
                        if (missing.Count > 0)
                        {
                            var stepLabels = new Dictionary<string, string>
                            {
                                ["embellissement"] = "Embellissement",
                                ["rainage"]        = "Rainage",
                                ["pliage"]         = "Pliage",
                                ["faconnage"]      = "Façonnage (reliure)",
                                ["coupe"]          = "Coupe",
                                ["emballage"]      = "Emballage",
                                ["depart"]         = "Départ",
                                ["livraison"]      = "Livraison"
                            };
                            var labels = missing.Select(k => stepLabels.TryGetValue(k, out var lbl) ? lbl : k).ToList();
                            return Results.Json(new
                            {
                                ok = false,
                                error = $"Étapes de finition non validées : {string.Join(", ", labels)}",
                                missingSteps = missing
                            });
                        }
                    }
                }
            }
            catch (Exception exVal)
            {
                Console.WriteLine($"[WARN] Finition steps validation error: {exVal.Message}");
            }
            
            var (ok, moved, error) = MoveOne(src, dstFolder, overwrite);

            if (ok && moved != null)
            {
                // Update delivery path in MongoDB so planning persists after file move
                try
                {
                    BackendUtils.UpdateDeliveryPath(src, moved);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[WARN] UpdateDeliveryPath failed: {ex2.Message}");
                }

                // Update assignment path when file moves
                try
                {
                    var assignCol = MongoDbHelper.GetCollection<BsonDocument>("assignments");
                    var oldPathNorm = src.Replace("\\", "/");
                    var newPathNorm = moved.Replace("\\", "/");
                    // Update with original path (backslash)
                    assignCol.UpdateMany(Builders<BsonDocument>.Filter.Eq("fullPath", src), Builders<BsonDocument>.Update.Set("fullPath", moved));
                    // Also update normalized forward-slash variants
                    assignCol.UpdateMany(Builders<BsonDocument>.Filter.Eq("fullPath", oldPathNorm), Builders<BsonDocument>.Update.Set("fullPath", newPathNorm));
                }
                catch (Exception exAssign)
                {
                    Console.WriteLine($"[WARN] UpdateAssignmentPath failed: {exAssign.Message}");
                }

                // Update fabrication path when file moves (also handle fabricationSheets collection)
                try
                {
                    var oldPathNorm2 = src.Replace("\\", "/");
                    var newPathNorm2 = moved.Replace("\\", "/");
                    // Always set both fullPath and fileName so lookup-by-fileName works reliably
                    var movedFileName = Path.GetFileName(moved).ToLowerInvariant();
                    var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrications");
                    var fabFilter = Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("fullPath", src),
                        Builders<BsonDocument>.Filter.Eq("fullPath", oldPathNorm2));
                    var fabUpdate = Builders<BsonDocument>.Update
                        .Set("fullPath", moved)
                        .Set("fileName", movedFileName);
                    fabCol.UpdateMany(fabFilter, fabUpdate);
                    // Also update fabricationSheets collection
                    var fabSheetsCol = MongoDbHelper.GetCollection<BsonDocument>("fabricationSheets");
                    fabSheetsCol.UpdateMany(
                        Builders<BsonDocument>.Filter.Or(
                            Builders<BsonDocument>.Filter.Eq("fullPath", src),
                            Builders<BsonDocument>.Filter.Eq("fullPath", oldPathNorm2)),
                        Builders<BsonDocument>.Update
                            .Set("fullPath", moved)
                            .Set("fileName", movedFileName));
                }
                catch (Exception exFab)
                {
                    Console.WriteLine($"[WARN] UpdateFabricationPath failed: {exFab.Message}");
                }

                // Log file move activity
                var token2 = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var userLogin2 = "?";
                var userName2 = "?";
                try {
                    var dec2 = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token2));
                    var p2 = dec2.Split(':');
                    if (p2.Length >= 2) userLogin2 = p2[1];
                    var u2 = BackendUtils.LoadUsers().FirstOrDefault(u => u.Login == userLogin2);
                    if (u2 != null) userName2 = u2.Name;
                } catch { /* ignore */ }
                MongoDbHelper.InsertActivityLog(new ActivityLogEntry
                {
                    Timestamp = DateTime.Now,
                    UserLogin = userLogin2,
                    UserName = userName2,
                    Action = "MOVE_FILE",
                    Details = $"Déplacement : {Path.GetFileName(src)} → {dstFolder}"
                });

                // Create production folder when file moves to "Début de production"
                if (string.Equals(dstFolder.Trim(), "Début de production", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await BackendUtils.EnsureProductionFolderAsync(moved);
                    }
                    catch (Exception ex3)
                    {
                        Console.WriteLine($"[WARN] EnsureProductionFolder failed: {ex3.Message}");
                    }
                }
                else
                {
                    // Copy file to production folder stage sub-folder if applicable
                    try
                    {
                        await BackendUtils.CopyToProductionFolderStageAsync(moved, dstFolder);
                    }
                    catch (Exception ex4)
                    {
                        Console.WriteLine($"[WARN] CopyToProductionFolderStage failed: {ex4.Message}");
                    }

                    // For stages not handled by CopyToProductionFolderStageAsync, still update currentStage and currentFilePath
                    try
                    {
                        var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                        var pfFileName = Path.GetFileName(moved);
                        var pfFilter = Builders<BsonDocument>.Filter.Eq("fileName", pfFileName);
                        var pfUpdate = Builders<BsonDocument>.Update
                            .Set("currentStage", dstFolder)
                            .Set("currentFilePath", moved);
                        pfCol.UpdateMany(pfFilter, pfUpdate);
                    }
                    catch (Exception ex5)
                    {
                        Console.WriteLine($"[WARN] UpdateProductionFolderStage failed: {ex5.Message}");
                    }
                }

                // Reset BAT status entry to pending when file moves to BAT folder
                // This prevents stale "Envoyé" state from previous BAT cycles
                if (string.Equals(dstFolder.Trim(), "BAT", StringComparison.OrdinalIgnoreCase))
                {
                    try {
                        var batCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
                        var batFilter = Builders<BsonDocument>.Filter.Eq("fullPath", moved);
                        var batDoc = new BsonDocument { ["fullPath"] = moved, ["status"] = "pending", ["sentAt"] = BsonNull.Value, ["validatedAt"] = BsonNull.Value, ["rejectedAt"] = BsonNull.Value };
                        batCol.ReplaceOne(batFilter, batDoc, new ReplaceOptions { IsUpsert = true });
                    } catch { }
                }

                // When file arrives in Rapport or Prêt pour impression, delete source from Corrections folders
                if (string.Equals(dstFolder.Trim(), "Rapport", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dstFolder.Trim(), "Prêt pour impression", StringComparison.OrdinalIgnoreCase))
                {
                    try {
                        var root3 = BackendUtils.HotfoldersRoot();
                        var baseName = Path.GetFileName(moved);
                        foreach (var corrFolder in new[] { "Corrections", "Corrections et fond perdu" })
                        {
                            var corrPath = Path.Combine(root3, corrFolder, baseName);
                            if (File.Exists(corrPath) && !string.Equals(corrPath, src, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.Delete(corrPath); Console.WriteLine($"[INFO] Auto-deleted source {corrPath}"); }
                                catch (Exception deleteEx) { Console.WriteLine($"[WARN] Could not delete {corrPath}: {deleteEx.Message}"); }
                            }
                        }
                    } catch { }
                }
            }

            return Results.Json(new { ok, moved, error });
        }

        return Results.BadRequest("Format JSON invalide.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] /api/jobs/move exception: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/jobs/delete", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();

        if (!json.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        string fullPath = fpEl.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(fullPath))
            return Results.Json(new { ok = false, error = "fullPath vide" });

        fullPath = Path.GetFullPath(fullPath);

        if (!File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier non trouvé" });

        Directory.CreateDirectory(recyclePath);

        // Derive source folder from path before moving
        var hotRoot = BackendUtils.HotfoldersRoot();
        var sourceDir = Path.GetDirectoryName(fullPath) ?? "";
        var sourceFolder = "";
        try { sourceFolder = Path.GetRelativePath(hotRoot, sourceDir); } catch { }
        // Reject path traversal attempts
        if (sourceFolder.StartsWith("..") || sourceFolder.Contains("..")) sourceFolder = "";

        string fileName = Path.GetFileName(fullPath);
        string trashPath = Path.Combine(recyclePath, fileName);

        int counter = 1;
        while (File.Exists(trashPath))
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            trashPath = Path.Combine(recyclePath, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }

        // Retry loop to handle file lock (e.g. preview handle still open)
        Exception? lastMoveEx = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.Move(fullPath, trashPath);
                lastMoveEx = null;
                break;
            }
            catch (IOException ex) when (i < 2)
            {
                lastMoveEx = ex;
                await Task.Delay(500);
            }
        }
        if (lastMoveEx != null)
            throw lastMoveEx;

        // Save source folder as sidecar metadata
        if (!string.IsNullOrWhiteSpace(sourceFolder))
        {
            try { File.WriteAllText(trashPath + ".meta", sourceFolder); } catch { }
        }

        // Cascade: remove delivery (planning) entry for this file so it no longer appears
        // in the calendar after deletion. Two calls are needed because:
        // - DeleteDeliveryByFileNameOrPath covers filename-keyed records (current format)
        // - DeleteDelivery covers path-keyed records (legacy format)
        try
        {
            var fnKey = fileName.ToLowerInvariant();
            BackendUtils.DeleteDeliveryByFileNameOrPath(fnKey);
            BackendUtils.DeleteDelivery(fullPath);
        }
        catch (Exception exDel) { Console.WriteLine($"[WARN] Could not cascade-delete delivery for {fileName}: {exDel.Message}"); }

        return Results.Json(new { ok = true, message = "Fichier supprimé avec succès" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// Delete source file from Corrections folders (called from "Supprimer source" button on Rapport cards)
app.MapPost("/api/jobs/delete-corrections-source", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("fileName", out var fnEl))
            return Results.Json(new { ok = false, error = "fileName manquant" });

        var fileName = fnEl.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(fileName))
            return Results.Json(new { ok = false, error = "fileName vide" });

        var root = BackendUtils.HotfoldersRoot();
        var deleted = new List<string>();
        foreach (var corrFolder in new[] { "Corrections", "Corrections et fond perdu" })
        {
            var corrPath = Path.Combine(root, corrFolder, fileName);
            if (File.Exists(corrPath))
            {
                try { File.Delete(corrPath); deleted.Add(corrPath); Console.WriteLine($"[INFO] Deleted source {corrPath}"); }
                catch (Exception exDel) { Console.WriteLine($"[WARN] Could not delete {corrPath}: {exDel.Message}"); }
            }
        }
        return Results.Json(new { ok = true, deleted = deleted.Count });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/jobs/cleanup-corrections", () =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var deleted = new List<string>();

        // Scan Rapport and Prêt pour impression folders
        foreach (var srcFolder in new[] { "Rapport", "Prêt pour impression" })
        {
            var srcDir = Path.Combine(root, srcFolder);
            if (!Directory.Exists(srcDir)) continue;

            foreach (var file in Directory.GetFiles(srcDir, "*.pdf", SearchOption.TopDirectoryOnly))
            {
                var baseName = Path.GetFileName(file);
                foreach (var corrFolder in new[] { "Corrections", "Corrections et fond perdu" })
                {
                    var corrPath = Path.Combine(root, corrFolder, baseName);
                    if (File.Exists(corrPath))
                    {
                        try
                        {
                            File.Delete(corrPath);
                            deleted.Add(corrPath);
                            Console.WriteLine($"[INFO] Cleanup: deleted source {corrPath}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WARN] Cleanup: could not delete {corrPath}: {ex.Message}");
                        }
                    }
                }
            }
        }

        return Results.Json(new { ok = true, deleted = deleted.Count, files = deleted });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/jobs/archive", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        var fileName = Path.GetFileName(fullPath);

        // Try to find production folder
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName))
                     .SortByDescending(x => x["createdAt"]).FirstOrDefault();

        string archiveDir;
        if (doc != null && doc.Contains("folderPath") && doc["folderPath"] != BsonNull.Value
            && !string.IsNullOrEmpty(doc["folderPath"].AsString))
        {
            archiveDir = Path.Combine(doc["folderPath"].AsString, "archive");
        }
        else
        {
            var root = BackendUtils.HotfoldersRoot();
            archiveDir = Path.Combine(root, "Corbeille");
        }

        Directory.CreateDirectory(archiveDir);
        var destPath = Path.Combine(archiveDir, fileName);
        if (File.Exists(destPath))
        {
            for (int i = 0; i < 3; i++)
            {
                try { File.Delete(destPath); break; }
                catch (IOException) when (i < 2) { await Task.Delay(500); }
            }
        }

        // Retry loop to handle file lock
        Exception? lastArchiveEx = null;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.Move(fullPath, destPath);
                lastArchiveEx = null;
                break;
            }
            catch (IOException ex) when (i < 2)
            {
                lastArchiveEx = ex;
                await Task.Delay(500);
            }
        }
        if (lastArchiveEx != null)
            throw lastArchiveEx;

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/jobs/lock", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(fullPath))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var fileName = Path.GetFileName(fullPath);
        // Fabrication documents store fileName in lowercase via fnKey
        var fileNameLower = fileName.ToLowerInvariant();

        // Mark as locked in fabrication sheet (search by lowercase fileName or by fullPath)
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var fabFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("fileName", fileNameLower),
            Builders<BsonDocument>.Filter.Eq("fileName", fileName),
            Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)
        );
        var fabUpdate = Builders<BsonDocument>.Update
            .Set("locked", true)
            .Set("lockedAt", DateTime.UtcNow);
        await fabCol.UpdateManyAsync(fabFilter, fabUpdate);

        // Mark calendar delivery as completed (green)
        var deliveryCol = MongoDbHelper.GetCollection<BsonDocument>("deliveries");
        var fnNoExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var fnNoExtLower = fnNoExt.ToLowerInvariant();
        var deliveryFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("fileName", fileNameLower),
            Builders<BsonDocument>.Filter.Eq("fileName", fileName),
            Builders<BsonDocument>.Filter.Eq("fileName", fnNoExt),
            Builders<BsonDocument>.Filter.Eq("fileName", fnNoExtLower)
        );
        var deliveryUpdate = Builders<BsonDocument>.Update.Set("completed", true).Set("color", "#22c55e");
        await deliveryCol.UpdateManyAsync(deliveryFilter, deliveryUpdate);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

    }
}
