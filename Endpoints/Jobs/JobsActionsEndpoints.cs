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

namespace GestionAtelier.Endpoints.Jobs;

public static class JobsActionsEndpoints
{
    public static void MapJobsActionsEndpoints(this WebApplication app, string recyclePath)
    {
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

app.MapPost("/api/acrobat/preflight", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var fullPath = Path.GetFullPath(fpEl.GetString() ?? "");
        var folder = doc.RootElement.TryGetProperty("folder", out var folderEl) ? folderEl.GetString() ?? "" : "";

        if (!File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        // Get droplet path: direct dropletPath param takes precedence over folder-based selection
        string dropletExe;
        if (doc.RootElement.TryGetProperty("dropletPath", out var dpEl) && !string.IsNullOrWhiteSpace(dpEl.GetString()))
        {
            // Mode "droplet direct" — utilisé quand les tuiles Preflight sont masquées
            dropletExe = dpEl.GetString()!;
        }
        else
        {
            // Mode classique — choisir le droplet en fonction du folder
            var preflightCfg = MongoDbHelper.GetSettings<PreflightSettings>("preflight") ?? new PreflightSettings();
            if (string.IsNullOrWhiteSpace(folder))
                return Results.Json(new { ok = false, error = "Paramètre 'folder' requis quand 'dropletPath' n'est pas fourni." });
            if (folder == "Corrections")
                dropletExe = preflightCfg.DropletStandard;
            else if (folder == "Corrections et fond perdu")
                dropletExe = preflightCfg.DropletFondPerdu;
            else
                return Results.Json(new { ok = false, error = $"Dossier non pris en charge pour le Preflight en mode automatique : {folder}" });
        }

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
            // Routage PrismaPrepare: 1) déplacer dans la tuile PrismaPrepare, 2) copier vers hotfolder
            var ppCol = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
            var ppDoc = ppCol.Find(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail)).FirstOrDefault();
            if (ppDoc == null || !ppDoc.Contains("hotfolderPath") || string.IsNullOrEmpty(ppDoc["hotfolderPath"].AsString))
                return Results.Json(new { ok = false, error = $"Aucun hotfolder PrismaPrepare configuré pour le type de travail \"{typeTravail}\". Configurez-le dans Paramétrage > Routage Impression (section 2)." });
            var ppHotfolder = ppDoc["hotfolderPath"].AsString;

            // Step 1: Move original file to PrismaPrepare tile
            var hotRoot3 = BackendUtils.HotfoldersRoot();
            var ppTileDir = Path.Combine(hotRoot3, "PrismaPrepare");
            Directory.CreateDirectory(ppTileDir);
            var ppTileDest = Path.Combine(ppTileDir, Path.GetFileName(fullPath));
            File.Move(fullPath, ppTileDest, overwrite: true);
            Console.WriteLine($"[ACTION] prisma-prepare: déplacé vers tuile {ppTileDest}");

            // Step 2: Copy from tile to hotfolder
            if (!Directory.Exists(ppHotfolder))
                Directory.CreateDirectory(ppHotfolder);
            var ppCopyDest = Path.Combine(ppHotfolder, Path.GetFileName(ppTileDest));
            File.Copy(ppTileDest, ppCopyDest, overwrite: true);
            Console.WriteLine($"[ACTION] prisma-prepare: copié vers hotfolder {ppCopyDest}");

            // Step 3: Try to open in PrismaPrepare
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

            return Results.Json(new { ok = true, message = $"Fichier déplacé dans la tuile PrismaPrepare et envoyé vers le hotfolder", destination = ppTileDest });
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

    }
}
