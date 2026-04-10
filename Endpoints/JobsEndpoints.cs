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

public static class JobsEndpointsExtensions
{
    public static void MapJobsEndpoints(this WebApplication app, string recyclePath)
    {
app.MapGet("/api/jobs", (string folder) =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var dir  = Path.Combine(root, folder);
        if (!Directory.Exists(dir))
            return Results.Json(Array.Empty<object>());

        var files = Directory.EnumerateFiles(dir)
            .Select(f =>
            {
                try
                {
                    var fi = new FileInfo(f);
                    return new
                    {
                        name     = fi.Name,
                        fullPath = fi.FullName,
                        modified = fi.LastWriteTime,
                        size     = fi.Length
                    };
                }
                catch { return null; }
            })
            .Where(x => x != null)
            .OrderByDescending(x => ((dynamic)x!).modified)
            .ToList();

        return Results.Json(files);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// MOVE FILES
// ======================================================


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

// ======================================================
// DELETE FILE
// ======================================================


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

        File.Move(fullPath, trashPath);

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

// ======================================================
// CLEANUP CORRECTIONS — called after Acrobat deposits files
// ======================================================

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


app.MapPost("/api/bat/execute", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var xmlPath  = json.TryGetProperty("xmlPath",  out var xp) ? xp.GetString() ?? "" : "";

        // Load command template from config
        var cfgCol  = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg     = cfgCol.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("prismaCommand") == true
            ? cfg["prismaCommand"].AsString
            : (cfg?.Contains("prismaPrepareCommand") == true
                ? cfg["prismaPrepareCommand"].AsString
                : "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" /import \"{xmlPath}\" /file \"{filePath}\"");

        var cmd = template
            .Replace("{xmlPath}", xmlPath)
            .Replace("{filePath}", fullPath)
            .Replace("{pdfPath}", fullPath);

        Console.WriteLine($"[INFO] BAT Execute: {cmd}");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);

        return Results.Json(new { ok = true, command = cmd });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/operators", () =>
{
    try
    {
        var users = BackendUtils.LoadUsers();
        var operators = users.Where(u => u.Profile == 2)
            .Select(u => new { id = u.Id, name = u.Name, login = u.Login });
        return Results.Json(new { ok = true, operators });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ASSIGNMENTS
// ======================================================


app.MapGet("/api/assignment", (string fullPath) =>
{
    var a = BackendUtils.FindAssignment(fullPath);
    if (a != null)
        return Results.Json(new { ok = true, assignment = new { fullPath = a.FullPath, operatorId = a.OperatorId, operatorName = a.OperatorName, assignedAt = a.AssignedAt, assignedBy = a.AssignedBy } });
    return Results.Json(new { ok = false, error = "Aucune affectation." });
});

app.MapGet("/api/assignments", () =>
{
    var list = BackendUtils.LoadAssignments();
    var result = list.Select(a => new {
        fullPath = a.FullPath,
        fileName = !string.IsNullOrEmpty(a.FileName) ? a.FileName : Path.GetFileName(a.FullPath),
        operatorId = a.OperatorId,
        operatorName = a.OperatorName,
        assignedAt = a.AssignedAt,
        assignedBy = a.AssignedBy
    });
    return Results.Json(result);
});

app.MapPut("/api/assignment", async (HttpContext ctx) =>
{
    try
    {
        // Extract caller identity from token
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string callerName = "Système";
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
                    if (u != null) callerName = u.Name;
                }
            }
            catch { /* ignore */ }
        }

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("operatorId", out var opIdEl))
            return Results.Json(new { ok = false, error = "operatorId requis." });

        var fullPath = json.TryGetProperty("fullPath", out var fpEl) ? (fpEl.GetString() ?? "") : "";
        var fileNameVal = json.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? "") : "";
        if (string.IsNullOrWhiteSpace(fileNameVal) && !string.IsNullOrWhiteSpace(fullPath))
            fileNameVal = Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(fileNameVal) && string.IsNullOrWhiteSpace(fullPath))
            return Results.Json(new { ok = false, error = "fileName ou fullPath requis." });

        var operatorId = opIdEl.GetString() ?? "";

        var users2 = BackendUtils.LoadUsers();
        var operator2 = users2.FirstOrDefault(u => u.Id == operatorId && u.Profile == 2);
        if (operator2 == null)
            return Results.Json(new { ok = false, error = "Opérateur introuvable ou profil invalide." });

        var assignment = new AssignmentItem
        {
            FullPath     = fullPath,
            FileName     = fileNameVal,
            OperatorId   = operatorId,
            OperatorName = operator2.Name,
            AssignedAt   = DateTime.Now,
            AssignedBy   = callerName
        };
        BackendUtils.UpsertAssignment(assignment);

        // Create notification for assigned operator
        try
        {
            var operatorLogin = operator2.Login;
            var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
            var fileName = Path.GetFileName(fullPath);
            var notif = new BsonDocument
            {
                ["recipientLogin"] = operatorLogin,
                ["message"] = $"Le fichier '{fileName}' vous a été affecté",
                ["timestamp"] = DateTime.UtcNow,
                ["read"] = false
            };
            notifCol.InsertOne(notif);
        }
        catch { /* notification failure is non-fatal */ }

        // Update fabrication history
        var sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet != null)
        {
            var updatedHistory = sheet.History.ToList();
            updatedHistory.Add(new FabricationHistory
            {
                Date   = DateTime.Now,
                User   = callerName,
                Action = $"Affecté à {operator2.Name}"
            });
            var updatedSheet = sheet with
            {
                Operateur = operator2.Name,
                History   = updatedHistory
            };
            BackendUtils.UpsertFabrication(updatedSheet);
        }

        return Results.Json(new { ok = true, operatorName = operator2.Name });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// UPLOAD
// ======================================================


app.MapPost("/api/upload", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        if (!form.Files.Any())
            return Results.Json(new { ok = false, error = "Aucun fichier reçu" });

        var file   = form.Files.First();
        var folder = form["folder"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(folder))
            folder = "Soumission";

        if (!file.FileName.ToLower().EndsWith(".pdf"))
            return Results.Json(new { ok = false, error = "Seuls les PDF sont acceptés" });

        var root    = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, folder);
        Directory.CreateDirectory(destDir);

        string destFileName = Path.GetFileName(file.FileName);
        long numero = MongoDbHelper.GetNextFileNumber();
        destFileName = $"{numero:D5}_{destFileName}";

        var destPath = Path.Combine(destDir, destFileName);

        using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs);
        }

        return Results.Json(new {
            ok      = true,
            fullPath= destPath,
            fileName= destFileName
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// OUTILS
// ======================================================

app.MapPost("/api/acrobat/open", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var full = Path.GetFullPath(fpEl.GetString() ?? "");
        if (!File.Exists(full))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        // Use configurable Acrobat path from settings
        var pathsCfg = MongoDbHelper.GetSettings<PathsSettings>("paths");
        var exe = (!string.IsNullOrWhiteSpace(pathsCfg?.AcrobatExePath))
            ? pathsCfg!.AcrobatExePath
            : @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

        if (!File.Exists(exe))
            return Results.Json(new { ok = false, error = $"Acrobat.exe introuvable : {exe}. Configurez le chemin dans Paramétrage > Chemins d'accès." });

        var psi = new System.Diagnostics.ProcessStartInfo(exe, $"\"{full}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
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
// ACROBAT — Ouvrir Acrobat Pro (sans fichier)
// ======================================================

app.MapPost("/api/acrobat", () =>
{
    try
    {
        var pathsCfg = MongoDbHelper.GetSettings<PathsSettings>("paths");
        var exe = (!string.IsNullOrWhiteSpace(pathsCfg?.AcrobatExePath))
            ? pathsCfg!.AcrobatExePath
            : @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

        if (!File.Exists(exe))
            return Results.Json(new { ok = false, error = $"Acrobat.exe introuvable : {exe}. Configurez le chemin dans Paramétrage > Chemins d'accès." });

        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
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
// ACROBAT — Preflight via droplet Acrobat (.exe)
// ======================================================

app.MapPost("/api/acrobat/preflight", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });
        if (!doc.RootElement.TryGetProperty("folder", out var folderEl))
            return Results.Json(new { ok = false, error = "folder manquant" });

        var fullPath = Path.GetFullPath(fpEl.GetString() ?? "");
        var folder = folderEl.GetString() ?? "";

        if (!File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        // Get droplet paths from settings
        var preflightCfg = MongoDbHelper.GetSettings<PreflightSettings>("preflight") ?? new PreflightSettings();
        string dropletExe;
        if (folder == "Corrections")
            dropletExe = preflightCfg.DropletStandard;
        else if (folder == "Corrections et fond perdu")
            dropletExe = preflightCfg.DropletFondPerdu;
        else
            return Results.Json(new { ok = false, error = $"Dossier non pris en charge pour le Preflight : {folder}" });

        if (string.IsNullOrWhiteSpace(dropletExe))
            return Results.Json(new { ok = false, error = $"Chemin du droplet non configuré pour '{folder}'. Configurez-le dans Paramétrage > Preflight." });

        if (!File.Exists(dropletExe))
            return Results.Json(new { ok = false, error = $"Droplet introuvable : {dropletExe}. Vérifiez le chemin dans Paramétrage > Preflight." });

        // Launch the droplet with the PDF path as argument
        var psi = new ProcessStartInfo(dropletExe, $"\"{fullPath}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        var process = Process.Start(psi);
        if (process == null)
            return Results.Json(new { ok = false, error = "Impossible de démarrer le droplet Preflight" });

        // Wait for the droplet process to complete (max 5 minutes)
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            return Results.Json(new { ok = false, error = "Le droplet Preflight a dépassé le délai d'attente (5 min)" });
        }

        // Give Acrobat a moment to flush/close the file after the droplet exits
        await Task.Delay(5000);

        // Move file to "Prêt pour impression" with retry loop to handle file lock
        var root = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, "Prêt pour impression");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, Path.GetFileName(fullPath));

        bool moved = false;
        Exception? lastEx = null;
        for (int retry = 0; retry < 10; retry++)
        {
            try
            {
                File.Move(fullPath, destPath, overwrite: true);
                moved = true;
                break;
            }
            catch (IOException ex)
            {
                lastEx = ex;
                await Task.Delay(2000);
            }
        }

        if (!moved)
        {
            // Fallback: copy + delete
            try
            {
                File.Copy(fullPath, destPath, overwrite: true);
                try { File.Delete(fullPath); } catch (Exception exDel) { Console.WriteLine($"[PREFLIGHT][WARN] Could not delete source after copy: {exDel.Message}"); }
                moved = true;
            }
            catch (Exception exCopy)
            {
                return Results.Json(new { ok = false, error = $"Impossible de déplacer le fichier après le Preflight : {lastEx?.Message ?? exCopy.Message}" });
            }
        }

        // Update delivery path in MongoDB
        try { BackendUtils.UpdateDeliveryPath(fullPath, destPath); } catch (Exception ex2) { Console.WriteLine($"[WARN] UpdateDeliveryPath: {ex2.Message}"); }

        // Update assignment path
        try
        {
            var assignCol = MongoDbHelper.GetCollection<BsonDocument>("assignments");
            var oldNorm = fullPath.Replace("\\", "/");
            assignCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", fullPath),
                    Builders<BsonDocument>.Filter.Eq("fullPath", oldNorm)),
                Builders<BsonDocument>.Update.Set("fullPath", destPath));
        }
        catch (Exception exA) { Console.WriteLine($"[WARN] UpdateAssignmentPath: {exA.Message}"); }

        // Update fabrication path
        try
        {
            var oldNorm2 = fullPath.Replace("\\", "/");
            var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrications");
            fabCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", fullPath),
                    Builders<BsonDocument>.Filter.Eq("fullPath", oldNorm2)),
                Builders<BsonDocument>.Update.Set("fullPath", destPath));
            var fabSheetsCol = MongoDbHelper.GetCollection<BsonDocument>("fabricationSheets");
            fabSheetsCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", fullPath),
                    Builders<BsonDocument>.Filter.Eq("fullPath", oldNorm2)),
                Builders<BsonDocument>.Update.Set("fullPath", destPath));
        }
        catch (Exception exF) { Console.WriteLine($"[WARN] UpdateFabricationPath: {exF.Message}"); }

        // Log activity
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = "system",
            UserName = "GestionAtelier",
            Action = "PREFLIGHT",
            Details = $"Preflight droplet ({Path.GetFileName(dropletExe)}) : {Path.GetFileName(fullPath)} → Prêt pour impression"
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});


app.MapPost("/api/acrobat/complete", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("sourcePath", out var spEl))
            return Results.Json(new { ok = false, error = "sourcePath manquant" });

        var sourcePath = Path.GetFullPath(spEl.GetString() ?? "");
        var root = BackendUtils.HotfoldersRoot();
        var fileName = Path.GetFileName(sourcePath);
        var rapportPath = Path.Combine(root, "Rapport", fileName);
        var printPath = Path.Combine(root, "Prêt pour impression", fileName);

        if (!File.Exists(rapportPath))
            return Results.Json(new { ok = false, error = $"Rapport introuvable: {rapportPath}" });
        if (!File.Exists(printPath))
            return Results.Json(new { ok = false, error = $"PDF corrigé introuvable: {printPath}" });

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
            Console.WriteLine($"[INFO] Acrobat complete: suppression {sourcePath}");
        }

        // Also delete matching files from Corrections and Corrections et fond perdu by base filename
        var baseName = Path.GetFileName(printPath);
        foreach (var corrFolder in new[] { "Corrections", "Corrections et fond perdu" })
        {
            var corrPath = Path.Combine(root, corrFolder, baseName);
            if (File.Exists(corrPath) && !string.Equals(corrPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(corrPath); Console.WriteLine($"[INFO] Acrobat complete: suppression source {corrPath}"); }
                catch (Exception exDel) { Console.WriteLine($"[WARN] Could not delete {corrPath}: {exDel.Message}"); }
            }
        }

        BackendUtils.UpdateDeliveryPath(sourcePath, printPath);
        Console.WriteLine($"[INFO] Acrobat complete: delivery mis à jour → {printPath}");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// COMMANDS CONFIG
// ======================================================

app.MapGet("/api/alerts/bat-pending", () =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var batDir = Path.Combine(root, "BAT");
        if (!Directory.Exists(batDir))
            return Results.Json(new List<object>());

        // Get configured delay (default 48h)
        var cfgCol = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
        var cfgDoc = cfgCol.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
        var delayHours = cfgDoc != null && cfgDoc.Contains("batAlertDelayHours") ? cfgDoc["batAlertDelayHours"].AsInt32 : 48;

        // Get all bat statuses to filter out validated/rejected
        var batStatusCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
        var allStatuses = batStatusCol.Find(new BsonDocument()).ToList();

        var alerts = new List<object>();
        foreach (var filePath in Directory.EnumerateFiles(batDir))
        {
            var fName = Path.GetFileName(filePath);
            var fi = new FileInfo(filePath);
            var ageHours = (DateTime.UtcNow - fi.CreationTimeUtc).TotalHours;
            if (ageHours < delayHours) continue;

            // Check if validated or rejected
            var normalizedPath = filePath.Replace('/', '\\');
            var status = allStatuses.FirstOrDefault(s =>
            {
                var sp = s.Contains("fullPath") ? s["fullPath"].AsString : "";
                return string.Equals(sp.Replace('/', '\\'), normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Path.GetFileName(sp), fName, StringComparison.OrdinalIgnoreCase);
            });

            var validatedAt = status != null && status.Contains("validatedAt") && status["validatedAt"] != BsonNull.Value
                ? status["validatedAt"].ToUniversalTime() : (DateTime?)null;
            var rejectedAt = status != null && status.Contains("rejectedAt") && status["rejectedAt"] != BsonNull.Value
                ? status["rejectedAt"].ToUniversalTime() : (DateTime?)null;

            if (validatedAt.HasValue || rejectedAt.HasValue) continue;

            var days = (int)Math.Floor(ageHours / 24);
            var ageHoursInt = (int)Math.Floor(ageHours);
            alerts.Add(new
            {
                fileName = fName,
                fullPath = filePath,
                createdAt = fi.CreationTimeUtc,
                ageHours = ageHoursInt,
                ageDays = days,
                message = days >= 1
                    ? $"⚠️ BAT en attente depuis {days} jour(s) : {fName}"
                    : $"⚠️ BAT en attente depuis {ageHoursInt}h : {fName}"
            });
        }
        return Results.Json(alerts);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ACTION BUTTONS CONFIG
// ======================================================

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
        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(fullPath, destPath);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// JOBS — Verrouiller (Fin de production terminée → vert calendrier)
// ======================================================
app.MapPost("/api/jobs/lock", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(fullPath))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var fileName = Path.GetFileName(fullPath);

        // Mark as locked in fabrication sheet
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var fabFilter = Builders<BsonDocument>.Filter.Or(
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
        var deliveryFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("fileName", fileName),
            Builders<BsonDocument>.Filter.Eq("fileName", fnNoExt)
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

// ======================================================
// JOBS — Ouvrir dans Fiery
// ======================================================
app.MapPost("/api/jobs/open-in-fiery", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations") ?? new IntegrationsSettings();
        var fieryExePath = integCfg.FieryPath ?? "";

        if (string.IsNullOrWhiteSpace(fieryExePath))
            return Results.Json(new { ok = false, error = "Chemin Fiery non configuré dans Paramétrage > Chemins d'accès." });

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = fieryExePath,
            Arguments = $"\"{fullPath}\"",
            UseShellExecute = true
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ALERTES FAÇONNAGE
// ======================================================
app.MapGet("/api/alerts/faconnage", () =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var folder = Path.Combine(root, "Impression en cours");
        if (!Directory.Exists(folder))
            return Results.Json(new { ok = true, alerts = new object[0], lastGeneratedAt = (object?)null });

        var files = Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(folder, "*.PDF", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => new { path = f, name = Path.GetFileName(f) })
            .ToList();

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var alerts = new List<object>();
        foreach (var f in files)
        {
            var fabFilter = Builders<BsonDocument>.Filter.Eq("fileName", f.name.ToLowerInvariant());
            var fabDoc = fabCol.Find(fabFilter).FirstOrDefault();
            var faconnage = new List<string>();
            if (fabDoc != null && fabDoc.Contains("faconnage") && fabDoc["faconnage"] != BsonNull.Value
                && fabDoc["faconnage"].IsBsonArray)
                faconnage = fabDoc["faconnage"].AsBsonArray.Select(v => v.AsString).ToList();

            var numeroDossier = fabDoc != null && fabDoc.Contains("numeroDossier") && fabDoc["numeroDossier"] != BsonNull.Value
                ? fabDoc["numeroDossier"].AsString : "";
            int? quantite = fabDoc != null && fabDoc.Contains("quantite") && fabDoc["quantite"] != BsonNull.Value
                && fabDoc["quantite"].BsonType == BsonType.Int32 ? fabDoc["quantite"].AsInt32
                : fabDoc != null && fabDoc.Contains("quantite") && fabDoc["quantite"] != BsonNull.Value
                && fabDoc["quantite"].IsNumeric ? (int?)fabDoc["quantite"].ToDouble() : null;
            alerts.Add(new { fileName = f.name, fullPath = f.path, faconnage, numeroDossier, quantite });
        }

        // Get last generated time from MongoDB
        var alertCol = MongoDbHelper.GetCollection<BsonDocument>("faconnageAlerts");
        var lastAlert = alertCol.Find(new BsonDocument()).SortByDescending(x => x["generatedAt"]).FirstOrDefault();
        DateTime? lastGeneratedAt = lastAlert != null && lastAlert.Contains("generatedAt")
            ? lastAlert["generatedAt"].ToUniversalTime() : (DateTime?)null;

        return Results.Json(new { ok = true, alerts, lastGeneratedAt });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// FABRICATION — Parcours fichiers (trail par étapes)
// ======================================================
app.MapGet("/api/fabrication/files-trail", (string fileName) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Results.Json(new { ok = false, error = "fileName manquant" });

        var root = BackendUtils.HotfoldersRoot();
        var fnBase = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var fnFull = System.IO.Path.GetFileName(fileName);

        // Check each stage folder for the file
        var stages = new[]
        {
            new { key = "original",         folder = "Début de production",         label = "Original" },
            new { key = "preflight",        folder = "Corrections",                  label = "Preflight" },
            new { key = "preflight_fp",     folder = "Corrections et fond perdu",    label = "Preflight avec fond perdu" },
            new { key = "en_attente",       folder = "Prêt pour impression",         label = "En attente" },
            new { key = "bat",              folder = "BAT",                          label = "BAT" },
            new { key = "rapport",          folder = "Rapport",                      label = "Rapport" },
            new { key = "prisma",           folder = "PrismaPrepare",                label = "PrismaPrepare" },
            new { key = "fiery",            folder = "Fiery",                        label = "Fiery" },
            new { key = "impression",       folder = "Impression en cours",          label = "Impression en cours" },
            new { key = "faconnage",        folder = "Façonnage",                    label = "Façonnage" },
            new { key = "fin_prod",         folder = "Fin de production",            label = "Fin de production" }
        };

        var result = new List<object>();
        foreach (var s in stages)
        {
            var folderPath = Path.Combine(root, s.folder);
            string? found = null;
            if (Directory.Exists(folderPath))
            {
                found = Directory.GetFiles(folderPath, fnFull, SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (found == null)
                    found = Directory.GetFiles(folderPath, fnBase + "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }

            // Also check production folder subfolders
            if (found == null)
            {
                var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                var pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fnFull))
                               .SortByDescending(x => x["createdAt"]).FirstOrDefault();
                if (pfDoc != null && pfDoc.Contains("folderPath"))
                {
                    var stageSubFolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Début de production"] = "Original",
                        ["Prêt pour impression"] = "PDF_Impression",
                        ["BAT"] = "BAT",
                        ["Rapport"] = "Rapport",
                        ["Fin de production"] = "PDF_Imprime"
                    };
                    if (stageSubFolderMap.TryGetValue(s.folder, out var sub))
                    {
                        var subDir = Path.Combine(pfDoc["folderPath"].AsString, sub);
                        if (Directory.Exists(subDir))
                        {
                            found = Directory.GetFiles(subDir, fnFull, SearchOption.TopDirectoryOnly).FirstOrDefault();
                            if (found == null)
                                found = Directory.GetFiles(subDir, fnBase + "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
                        }
                    }
                }
            }

            result.Add(new
            {
                key = s.key,
                label = s.label,
                folder = s.folder,
                found = found != null,
                fullPath = found
            });
        }

        return Results.Json(new { ok = true, files = result });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});


app.MapPost("/api/jobs/send-to-print", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var action = json.TryGetProperty("action", out var ac) ? ac.GetString() ?? "" : "";

        // Find the actual file if fullPath not provided or doesn't exist
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            if (!string.IsNullOrEmpty(fileName))
            {
                var found = Directory.GetFiles(hotRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) fullPath = found;
            }
        }

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier introuvable : {fileName}" });

        // Get fabrication data for routing
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? fabDoc = null;
        if (!string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fullPath))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();

        var typeTravail = fabDoc != null && fabDoc.Contains("typeTravail") ? fabDoc["typeTravail"].AsString : "";
        var moteurImpression = fabDoc != null && fabDoc.Contains("moteurImpression") ? fabDoc["moteurImpression"].AsString : "";
        if (string.IsNullOrEmpty(moteurImpression) && fabDoc != null && fabDoc.Contains("printEngine"))
            moteurImpression = fabDoc["printEngine"].AsString;

        string destPath;

        if (action == "send-to-print")
        {
            // Route to Fiery or PrismaSync based on print engine
            var engineLower = (moteurImpression ?? "").ToLowerInvariant();
            if (engineLower.Contains("fiery"))
            {
                // Fiery routing: typeTravail → hotfolder
                var fieryCol = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
                var fieryDoc = fieryCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
                if (fieryDoc == null || !fieryDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(fieryDoc["hotfolderPath"].AsString))
                    return Results.Json(new { ok = false, error = $"Aucun hotfolder Fiery configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Impression." });
                destPath = fieryDoc["hotfolderPath"].AsString;
            }
            else
            {
                // PrismaSync routing: printEngine → workflow
                var syncCol = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
                var syncDoc = syncCol.Find(Builders<BsonDocument>.Filter.Eq("printEngine", moteurImpression)).FirstOrDefault();
                if (syncDoc == null || !syncDoc.Contains("workflowPath") || string.IsNullOrEmpty(syncDoc["workflowPath"].AsString))
                    return Results.Json(new { ok = false, error = $"Aucun workflow PrismaSync configuré pour le moteur \"{moteurImpression}\". Configurez-le dans Paramétrage > Routage Impression." });
                destPath = syncDoc["workflowPath"].AsString;
            }
        }
        else if (action == "direct-print")
        {
            // Direct print routing: typeTravail + printEngine → hotfolder
            var dpCol = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
            var dpFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
                Builders<BsonDocument>.Filter.Eq("printEngine", moteurImpression));
            var dpDoc = dpCol.Find(dpFilter).FirstOrDefault();
            // Fallback: match only typeTravail if combined not found
            if (dpDoc == null)
                dpDoc = dpCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            if (dpDoc == null || !dpDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(dpDoc["hotfolderPath"].AsString))
                return Results.Json(new { ok = false, error = $"Aucun hotfolder d'impression directe configuré pour le type \"{typeTravail}\" / moteur \"{moteurImpression}\". Configurez-le dans Paramétrage > Routage Impression." });
            destPath = dpDoc["hotfolderPath"].AsString;
        }
        else
        {
            return Results.Json(new { ok = false, error = $"Action inconnue : {action}" });
        }

        if (!Directory.Exists(destPath))
            Directory.CreateDirectory(destPath);

        var dest = Path.Combine(destPath, Path.GetFileName(fullPath));
        File.Copy(fullPath, dest, overwrite: true);
        Console.WriteLine($"[PRINT] {action}: copié vers {dest}");

        return Results.Json(new { ok = true, message = $"Fichier envoyé vers {destPath}", destPath });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] send-to-print: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ACTIONS — Endpoint unifié pour les 4 actions du bouton "Actions" (tuile En attente)
// ======================================================

app.MapPost("/api/jobs/send-to-action", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var action = json.TryGetProperty("action", out var ac) ? ac.GetString() ?? "" : "";

        // Find the actual file if fullPath not provided or doesn't exist
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            if (!string.IsNullOrEmpty(fileName))
            {
                var found = Directory.GetFiles(hotRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) fullPath = found;
            }
        }

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier introuvable : {fileName}" });

        // Get fabrication data for routing
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        BsonDocument? fabDoc = null;
        if (!string.IsNullOrEmpty(fileName))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fileName)).FirstOrDefault();
        if (fabDoc == null && !string.IsNullOrEmpty(fullPath))
            fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();

        var typeTravail = fabDoc != null && fabDoc.Contains("typeTravail") && fabDoc["typeTravail"] != BsonNull.Value ? fabDoc["typeTravail"].AsString : "";
        var moteurImpression = fabDoc != null && fabDoc.Contains("moteurImpression") && fabDoc["moteurImpression"] != BsonNull.Value ? fabDoc["moteurImpression"].AsString : "";
        if (string.IsNullOrEmpty(moteurImpression) && fabDoc != null && fabDoc.Contains("printEngine") && fabDoc["printEngine"] != BsonNull.Value)
            moteurImpression = fabDoc["printEngine"].AsString;
        var media1 = fabDoc != null && fabDoc.Contains("media1") && fabDoc["media1"] != BsonNull.Value ? fabDoc["media1"].AsString : "";
        var media2 = fabDoc != null && fabDoc.Contains("media2") && fabDoc["media2"] != BsonNull.Value ? fabDoc["media2"].AsString : "";
        var media3 = fabDoc != null && fabDoc.Contains("media3") && fabDoc["media3"] != BsonNull.Value ? fabDoc["media3"].AsString : "";
        var media4 = fabDoc != null && fabDoc.Contains("media4") && fabDoc["media4"] != BsonNull.Value ? fabDoc["media4"].AsString : "";

        string copyDestPath; // hotfolder/workflow to copy PDF to
        string tileFolder;   // kanban tile folder to move the original to

        if (action == "prismasync")
        {
            // Routage PrismaSync: typeTravail + moteurImpression + médias → prismaSyncPath
            var syncCol = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
            // Match on typeTravail + moteurImpression first (exact), then fallback to typeTravail only
            var syncFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
                Builders<BsonDocument>.Filter.Eq("moteurImpression", moteurImpression));
            var syncDoc = syncCol.Find(syncFilter).FirstOrDefault();
            if (syncDoc == null)
            {
                Console.WriteLine($"[ACTION] prismasync: pas de routage exact pour typeTravail={typeTravail}+moteur={moteurImpression}, fallback sur typeTravail seul");
                syncDoc = syncCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            }
            if (syncDoc == null || !syncDoc.Contains("prismaSyncPath") || string.IsNullOrEmpty(syncDoc["prismaSyncPath"].AsString))
                return Results.Json(new { ok = false, error = $"⚠️ Aucun routage PrismaSync configuré pour ce type de travail \"{typeTravail}\" / moteur \"{moteurImpression}\". Configurez-le dans Paramétrage > Routage Impression." });
            copyDestPath = syncDoc["prismaSyncPath"].AsString;
            tileFolder = "Impression en cours";
        }
        else if (action == "prisma-prepare")
        {
            // Routage PrismaPrepare: typeTravail → hotfolder PrismaPrepare (dedicated collection)
            // NOTE: The file is NOT moved to the PrismaPrepare tile — PrismaPrepare handles its own workflow.
            //       We only copy the file to the hotfolder and open it in PrismaPrepare.
            var ppCol = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
            var ppDoc = ppCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            if (ppDoc == null || !ppDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(ppDoc["hotfolderPath"].AsString))
                return Results.Json(new { ok = false, error = $"Aucun hotfolder PrismaPrepare configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Impression (section 2)." });
            var ppHotfolder = ppDoc["hotfolderPath"].AsString;

            // Copy to hotfolder only — do NOT move original
            if (!Directory.Exists(ppHotfolder))
                Directory.CreateDirectory(ppHotfolder);
            var ppCopyDest = Path.Combine(ppHotfolder, Path.GetFileName(fullPath));
            File.Copy(fullPath, ppCopyDest, overwrite: true);
            Console.WriteLine($"[ACTION] prisma-prepare: copié vers hotfolder {ppCopyDest} (fichier original conservé en place)");

            // Also try to open in PrismaPrepare directly
            var integCfg2 = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations") ?? new IntegrationsSettings();
            var prismaPrepPath2 = integCfg2.PrismaPrepareExePath ?? "";
            if (!string.IsNullOrWhiteSpace(prismaPrepPath2))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = prismaPrepPath2,
                        Arguments = $"\"{ppCopyDest}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception exPp) { Console.WriteLine($"[WARN] Impossible d'ouvrir PrismaPrepare: {exPp.Message}"); }
            }

            return Results.Json(new { ok = true, message = "Fichier envoyé vers PrismaPrepare (fichier original conservé dans sa tuile)" });
        }
        else if (action == "direct-print")
        {
            // Routage Impression directe: typeTravail + moteurImpression → hotfolder
            var dpCol = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
            var dpFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
                Builders<BsonDocument>.Filter.Eq("printEngine", moteurImpression));
            var dpDoc = dpCol.Find(dpFilter).FirstOrDefault();
            if (dpDoc == null)
                dpDoc = dpCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            if (dpDoc == null || !dpDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(dpDoc["hotfolderPath"].AsString))
                return Results.Json(new { ok = false, error = $"Aucun hotfolder d'impression directe configuré pour le type \"{typeTravail}\" / moteur \"{moteurImpression}\". Configurez-le dans Paramétrage > Routage Impression." });
            copyDestPath = dpDoc["hotfolderPath"].AsString;
            tileFolder = "Impression en cours";
        }
        else if (action == "fiery")
        {
            // Routage Fiery: typeTravail → hotfolder Fiery
            var fieryCol = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
            var fieryDoc = fieryCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            if (fieryDoc == null || !fieryDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(fieryDoc["hotfolderPath"].AsString))
                return Results.Json(new { ok = false, error = $"Aucun hotfolder Fiery configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Impression." });
            copyDestPath = fieryDoc["hotfolderPath"].AsString;
            tileFolder = "Fiery";
        }
        else
        {
            return Results.Json(new { ok = false, error = $"Action inconnue : {action}" });
        }

        // Copy PDF to the hotfolder/workflow
        if (!Directory.Exists(copyDestPath))
            Directory.CreateDirectory(copyDestPath);
        var copyDest = Path.Combine(copyDestPath, Path.GetFileName(fullPath));
        File.Copy(fullPath, copyDest, overwrite: true);
        Console.WriteLine($"[ACTION] {action}: copié vers {copyDest}");

        // Move original to the target tile folder
        var hotRoot2 = BackendUtils.HotfoldersRoot();
        var tileDir = Path.Combine(hotRoot2, tileFolder);
        Directory.CreateDirectory(tileDir);
        var tileDest = Path.Combine(tileDir, Path.GetFileName(fullPath));
        File.Move(fullPath, tileDest, overwrite: true);
        Console.WriteLine($"[ACTION] {action}: déplacé vers {tileDest}");

        var actionLabels = new Dictionary<string, string>
        {
            ["prismasync"] = "envoyé vers PrismaSync",
            ["prisma-prepare"] = "ouvert dans PrismaPrepare",
            ["direct-print"] = "envoyé en impression directe",
            ["fiery"] = "envoyé dans Fiery"
        };
        var label = actionLabels.TryGetValue(action, out var lbl) ? lbl : action;
        return Results.Json(new { ok = true, message = $"Fichier {label} et déplacé dans la tuile \"{tileFolder}\"", destination = tileDest });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] send-to-action: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// PRINT — Open in PrismaPrepare
// ======================================================

app.MapPost("/api/jobs/open-in-prismaprepare", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fileName = json.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";

        // Find the actual file
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            var hotRoot = BackendUtils.HotfoldersRoot();
            if (!string.IsNullOrEmpty(fileName))
            {
                var found = Directory.GetFiles(hotRoot, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) fullPath = found;
            }
        }

        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return Results.Json(new { ok = false, error = $"Fichier introuvable : {fileName}" });

        // Get PrismaPrepare path from settings
        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations") ?? new IntegrationsSettings();
        var prismaPrepPath = integCfg.PrismaPrepareExePath ?? "";

        if (string.IsNullOrWhiteSpace(prismaPrepPath))
            return Results.Json(new { ok = false, error = "Chemin PrismaPrepare non configuré dans Paramétrage > Routage Impression." });

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = prismaPrepPath,
            Arguments = $"\"{fullPath}\"",
            UseShellExecute = true
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] open-in-prismaprepare: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// BAT — Envoi vers hotfolder PrismaPrepare (BAT Complet)
// ======================================================


    }
}
