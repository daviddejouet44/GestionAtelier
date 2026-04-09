// ======================================================
// Program.cs — DEV final (.NET 8)
// Port = 5080 — Frontend sous /pro
// ======================================================

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Xml.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Routing;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using MongoDB.Driver;
using MongoDB.Bson;

// ======================================================
// HOST — DEV (5080)
// ======================================================

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(k =>
{
    k.ListenAnyIP(5080, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// ======================================================
// CREATION DE LA CORBEILLE
// ======================================================
var recycleEnabled = builder.Configuration["RecycleBin:Enabled"] == "true";
var hotfoldersRootForRecycle = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT") is { Length: > 0 } env ? Path.GetFullPath(env) : @"C:\Flux";
var recyclePath    = builder.Configuration["RecycleBin:Path"] ?? Path.Combine(hotfoldersRootForRecycle, "Corbeille");
var recycleDays    = int.TryParse(builder.Configuration["RecycleBin:DaysToKeep"], out var d) ? d : 7;
Directory.CreateDirectory(recyclePath);

var app = builder.Build();

// QuestPDF community license
QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("[INFO] ContentRoot = " + app.Environment.ContentRootPath);

// ======================================================
// CREATION DES DOSSIERS HOTFOLDERS AU DÉMARRAGE
// ======================================================
{
    var hotRoot = BackendUtils.HotfoldersRoot();
    var hotFolders = new[]
    {
        "Soumission", "Début de production", "Corrections", "Corrections et fond perdu",
        "Rapport", "Prêt pour impression", "BAT", "Impression en cours",
        "PrismaPrepare", "Fiery", "Façonnage", "Fin de production", "Corbeille",
        "DossiersProduction"
    };
    foreach (var f in hotFolders)
    {
        try { Directory.CreateDirectory(Path.Combine(hotRoot, f)); } catch { }
    }
    Console.WriteLine("[INFO] Hotfolders initialized in " + hotRoot);
}

// ======================================================
// FILE SYSTEM WATCHER — Reconcile paths on external moves
// ======================================================
{
    var watchRoot = BackendUtils.HotfoldersRoot();
    if (Directory.Exists(watchRoot))
    {
        var watcher = new FileSystemWatcher(watchRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        watcher.Created += async (sender, e) =>
        {
            try
            {
                // Ignore DossiersProduction sub-folder events
                if (e.FullPath.Contains("DossiersProduction")) return;

                var newPath = e.FullPath;
                var fileName = Path.GetFileName(newPath);
                if (string.IsNullOrWhiteSpace(fileName)) return;

                await Task.Delay(BackendUtils.FileSystemSettleDelayMs); // slight delay to let the file system settle

                // Reconcile assignments
                try
                {
                    var assignCol = MongoDbHelper.GetCollection<BsonDocument>("assignments");
                    // Find assignment with same filename but different path
                    var allAssign = assignCol.Find(new BsonDocument()).ToList();
                    foreach (var doc in allAssign)
                    {
                        var oldPath = doc.Contains("fullPath") ? doc["fullPath"].AsString : "";
                        if (string.IsNullOrEmpty(oldPath)) continue;
                        if (!string.Equals(Path.GetFileName(oldPath), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(oldPath)) continue; // old file still exists, not a move
                        assignCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                            Builders<BsonDocument>.Update.Set("fullPath", newPath));
                        Console.WriteLine($"[FSW] Reconciled assignment: {fileName} → {newPath}");
                    }
                }
                catch (Exception exA) { Console.WriteLine($"[FSW][WARN] Assignment reconcile: {exA.Message}"); }

                // Reconcile deliveries
                try
                {
                    var delivCol = MongoDbHelper.GetCollection<BsonDocument>("deliveries");
                    var allDeliveries = delivCol.Find(new BsonDocument()).ToList();
                    foreach (var doc in allDeliveries)
                    {
                        var oldPath = doc.Contains("fullPath") ? doc["fullPath"].AsString : "";
                        if (string.IsNullOrEmpty(oldPath)) continue;
                        if (!string.Equals(Path.GetFileName(oldPath), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(oldPath)) continue;
                        delivCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                            Builders<BsonDocument>.Update.Set("fullPath", newPath));
                        Console.WriteLine($"[FSW] Reconciled delivery: {fileName} → {newPath}");
                    }
                }
                catch (Exception exD) { Console.WriteLine($"[FSW][WARN] Delivery reconcile: {exD.Message}"); }

                // Reconcile fabrications
                try
                {
                    var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrications");
                    var allFabs = fabCol.Find(new BsonDocument()).ToList();
                    foreach (var doc in allFabs)
                    {
                        var oldPath = doc.Contains("fullPath") ? doc["fullPath"].AsString : "";
                        if (string.IsNullOrEmpty(oldPath)) continue;
                        if (!string.Equals(Path.GetFileName(oldPath), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(oldPath)) continue;
                        fabCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                            Builders<BsonDocument>.Update.Set("fullPath", newPath));
                        Console.WriteLine($"[FSW] Reconciled fabrication: {fileName} → {newPath}");
                    }
                }
                catch (Exception exF) { Console.WriteLine($"[FSW][WARN] Fabrication reconcile: {exF.Message}"); }

                // Reconcile productionFolders — update currentFilePath and add stage
                try
                {
                    var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                    var allPf = pfCol.Find(new BsonDocument()).ToList();
                    foreach (var pfDoc in allPf)
                    {
                        var oldPath = pfDoc.Contains("currentFilePath") ? pfDoc["currentFilePath"].AsString :
                                      (pfDoc.Contains("originalFilePath") ? pfDoc["originalFilePath"].AsString : "");
                        if (string.IsNullOrEmpty(oldPath)) continue;
                        if (!string.Equals(Path.GetFileName(oldPath), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.Exists(oldPath)) continue;

                        // Determine stage from new folder name
                        var newStage = Path.GetFileName(Path.GetDirectoryName(newPath)) ?? "";

                        // Add stage entry and copy to production folder
                        var folderPath = pfDoc.Contains("folderPath") ? pfDoc["folderPath"].AsString : "";
                        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                        {
                            if (BackendUtils.StageFolderMap.TryGetValue(newStage.Trim(), out var subFolder))
                            {
                                var stageDir = Path.Combine(folderPath, subFolder);
                                Directory.CreateDirectory(stageDir);
                                if (File.Exists(newPath))
                                    File.Copy(newPath, Path.Combine(stageDir, fileName), overwrite: true);
                            }
                        }

                        var fileEntry = new BsonDocument
                        {
                            ["stage"] = newStage,
                            ["fileName"] = fileName,
                            ["addedAt"] = DateTime.UtcNow
                        };
                        pfCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", pfDoc["_id"]),
                            Builders<BsonDocument>.Update
                                .Set("currentFilePath", newPath)
                                .Set("currentStage", newStage)
                                .Push("files", fileEntry));
                        Console.WriteLine($"[FSW] Reconciled productionFolder: {fileName} → {newPath} (stage: {newStage})");
                    }
                }
                catch (Exception exPf) { Console.WriteLine($"[FSW][WARN] ProductionFolder reconcile: {exPf.Message}"); }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FSW][ERROR] Reconcile error for {e.FullPath}: {ex.Message}");
            }
        };

        Console.WriteLine($"[INFO] FileSystemWatcher started on {watchRoot}");
    }
}

// ======================================================
// PrismaPrepare Output FileSystemWatcher — detect Epreuve.pdf, rename → BAT_*.pdf, move to BAT
// ======================================================
// Declared in outer scope so the GC never collects it while the app is running.
FileSystemWatcher? tempCopyWatcher = null;
{
    try
    {
        var integCfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations");
        var tempCopyDir = integCfg?.TempCopyPath ?? "";
        var outputDir = !string.IsNullOrWhiteSpace(integCfg?.PrismaPrepareOutputPath)
            ? integCfg!.PrismaPrepareOutputPath
            : IntegrationsSettings.DefaultPrismaPrepareOutputPath;

        if (Path.IsPathRooted(outputDir))
        {
            // Create the directory if it does not yet exist so the watcher can always be started.
            Directory.CreateDirectory(outputDir);

            tempCopyWatcher = new FileSystemWatcher(outputDir)
            {
                Filter = "Epreuve.pdf",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            // Mutex to prevent concurrent Epreuve.pdf processing (race condition fix)
            var batRenameSem = new SemaphoreSlim(1, 1);

            async Task HandleEpreuve(string epreuvePath)
            {
                await batRenameSem.WaitAsync();
                try
                {
                    await Task.Delay(BackendUtils.FileSystemSettleDelayMs * 4);
                    if (!File.Exists(epreuvePath)) return;

                    BatSerializationState.SetStep("processing_epreuve");

                    // Wait for file to be fully written and not locked (retry loop)
                    bool fileUnlocked = false;
                    for (int retry = 0; retry < 10; retry++)
                    {
                        try
                        {
                            // Open and immediately close to verify file is not locked
                            using var fs = File.Open(epreuvePath, FileMode.Open, FileAccess.Read, FileShare.None);
                            fileUnlocked = true;
                            break;
                        }
                        catch (IOException) { await Task.Delay(500); }
                    }
                    if (!fileUnlocked)
                        Console.WriteLine("[BAT_FSW][WARN] Epreuve.pdf still locked after retries, proceeding anyway.");
                    if (!File.Exists(epreuvePath)) return;

                    // Step 0 (HIGHEST PRIORITY): Use correlationId from BatSerializationState for guaranteed 1-to-1 matching
                    string prismaLogContent = "";
                    string sourceFileName = "";
                    var batPendingCol = MongoDbHelper.GetCollection<BsonDocument>("batPending");
                    BsonDocument? pending = null;

                    var (_, _, _, _, currentCorrelationId) = BatSerializationState.Get();
                    if (!string.IsNullOrEmpty(currentCorrelationId))
                    {
                        pending = batPendingCol.Find(
                            Builders<BsonDocument>.Filter.And(
                                Builders<BsonDocument>.Filter.Eq("correlationId", currentCorrelationId),
                                Builders<BsonDocument>.Filter.Eq("processed", false)
                            )
                        ).FirstOrDefault();

                        if (pending != null && pending.Contains("sourceFileName"))
                        {
                            sourceFileName = pending["sourceFileName"].AsString;
                            Console.WriteLine($"[BAT_FSW] Step 0: sourceFileName from correlationId [{currentCorrelationId}]: {sourceFileName}");
                        }
                    }

                    // Step 1: Read the most recent PrismaPrepare log and extract the source file name
                    try
                    {
                        var logFile = Directory.GetFiles(outputDir, "*.log")
                            .Where(f =>
                            {
                                var fn = Path.GetFileName(f);
                                return fn.EndsWith("_WARNING.log", StringComparison.OrdinalIgnoreCase)
                                    || fn.EndsWith("_SUCCESS.log", StringComparison.OrdinalIgnoreCase)
                                    || fn.EndsWith("_OK.log", StringComparison.OrdinalIgnoreCase);
                            })
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .FirstOrDefault();
                        if (logFile != null)
                        {
                            prismaLogContent = File.ReadAllText(logFile, System.Text.Encoding.UTF8);
                            Console.WriteLine($"[BAT_FSW] PrismaPrepare log found: {Path.GetFileName(logFile)}");
                            var inputMatch = System.Text.RegularExpressions.Regex.Match(
                                prismaLogContent,
                                @"fichier d[\u2019'']entr[eéÃ©]+e\s*:\s*(.+?\.pdf)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (inputMatch.Success)
                            {
                                var rawName = inputMatch.Groups[1].Value.Trim();
                                var rawBaseName = Path.GetFileNameWithoutExtension(Path.GetFileName(rawName));
                                // Check if __BAT_ correlation ID is embedded in the filename
                                var batMarkerIdx = rawBaseName.IndexOf("__BAT_", StringComparison.Ordinal);
                                if (batMarkerIdx >= 0)
                                {
                                     var logCorrelationId = rawBaseName.Length >= batMarkerIdx + 6 + 16
                                        ? rawBaseName.Substring(batMarkerIdx + 6, 16)
                                        : rawBaseName.Substring(batMarkerIdx + 6);
                                    Console.WriteLine($"[BAT_FSW] Step 1: __BAT_ correlationId extracted from log: {logCorrelationId}");
                                    if (pending == null && !string.IsNullOrEmpty(logCorrelationId))
                                    {
                                        pending = batPendingCol.Find(
                                            Builders<BsonDocument>.Filter.And(
                                                Builders<BsonDocument>.Filter.Eq("correlationId", logCorrelationId),
                                                Builders<BsonDocument>.Filter.Eq("processed", false)
                                            )
                                        ).FirstOrDefault();
                                        if (pending != null && pending.Contains("sourceFileName"))
                                        {
                                            sourceFileName = pending["sourceFileName"].AsString;
                                            Console.WriteLine($"[BAT_FSW] Step 1: sourceFileName from log correlationId [{logCorrelationId}]: {sourceFileName}");
                                        }
                                    }
                                    if (string.IsNullOrEmpty(sourceFileName))
                                    {
                                        sourceFileName = rawBaseName.Substring(0, batMarkerIdx);
                                        Console.WriteLine($"[BAT_FSW] Step 1: sourceFileName stripped from log filename: {sourceFileName}");
                                    }
                                }
                                else if (string.IsNullOrEmpty(sourceFileName))
                                {
                                    sourceFileName = rawBaseName;
                                    Console.WriteLine($"[BAT_FSW] Step 1: sourceFileName from PrismaPrepare log: {sourceFileName}");
                                }
                            }
                        }
                    }
                    catch (Exception exLog) { Console.WriteLine($"[BAT_FSW][WARN] Reading PrismaPrepare log: {exLog.Message}"); }

                    // Step 2 (FALLBACK): Look in batPending MongoDB if steps 0-1 did not provide the name
                    if (string.IsNullOrEmpty(sourceFileName))
                    {
                        pending = batPendingCol.Find(
                            Builders<BsonDocument>.Filter.Eq("processed", false)
                        ).SortBy(d => d["createdAt"]).FirstOrDefault();

                        if (pending != null && pending.Contains("sourceFileName"))
                        {
                            sourceFileName = pending["sourceFileName"].AsString;
                            Console.WriteLine($"[BAT_FSW] Step 2: sourceFileName from batPending MongoDB (FIFO): {sourceFileName}");
                        }
                    }
                    else if (pending == null)
                    {
                        // Fetch pending entry for requestedBy even when earlier steps already gave us the name
                        pending = batPendingCol.Find(
                            Builders<BsonDocument>.Filter.Eq("processed", false)
                        ).SortBy(d => d["createdAt"]).FirstOrDefault();
                    }

                    // Step 3 (REMOVED): TEMP_COPY scan-by-date was the primary source of filename mixing
                    // and has been intentionally removed. If steps 0-2 all fail, log a warning and skip.

                    if (string.IsNullOrEmpty(sourceFileName))
                    {
                        Console.WriteLine("[BAT_FSW][WARN] Cannot determine job name for Epreuve.pdf — skipping rename.");
                        return;
                    }

                    // Rename Epreuve.pdf → BAT_{sourceFileName}.pdf (in outputDir)
                    BatSerializationState.SetStep("renaming");
                    var batFileName = $"BAT_{sourceFileName}.pdf";
                    var renamedPath = Path.Combine(outputDir, batFileName);
                    File.Move(epreuvePath, renamedPath, overwrite: true);
                    Console.WriteLine($"[BAT_FSW] Renamed Epreuve.pdf → {batFileName}");

                    // Move BAT_{sourceFileName}.pdf to the BAT production folder
                    BatSerializationState.SetStep("moving_to_bat");
                    var (ok, err) = BackendUtils.MoveFileToDestFolder(renamedPath, "BAT");
                    if (ok)
                        Console.WriteLine($"[BAT_FSW] Moved {batFileName} to BAT folder");
                    else
                        Console.WriteLine($"[BAT_FSW][WARN] Move to BAT failed: {err}");

                    // Delete the original copy of the source file in TEMP_COPY
                    if (!string.IsNullOrWhiteSpace(tempCopyDir) && Directory.Exists(tempCopyDir))
                    {
                        var originalInTemp = Directory.GetFiles(tempCopyDir)
                            .Where(f =>
                                Path.GetFileNameWithoutExtension(f).Equals(sourceFileName, StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).StartsWith("BAT_", StringComparison.OrdinalIgnoreCase) &&
                                !Path.GetFileName(f).Equals("Epreuve.pdf", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var orig in originalInTemp)
                        {
                            try { File.Delete(orig); Console.WriteLine($"[BAT_FSW] Deleted temp file {Path.GetFileName(orig)}"); }
                            catch (Exception exDel) { Console.WriteLine($"[BAT_FSW][WARN] Delete temp file: {exDel.Message}"); }
                        }
                    }

                    // Create BAT ready notification for the operator who requested the BAT
                    BatSerializationState.SetStep("creating_notification");
                    try
                    {
                        var requestedBy = pending != null && pending.Contains("requestedBy") ? pending["requestedBy"].AsString : "";
                        if (!string.IsNullOrEmpty(requestedBy))
                        {
                            // Try to get numeroDossier from fabrication sheet
                            var fabCol2 = MongoDbHelper.GetFabricationsCollection();
                            var fabDoc2 = fabCol2.Find(Builders<BsonDocument>.Filter.Or(
                                Builders<BsonDocument>.Filter.Regex("fileName",
                                    new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(sourceFileName)}", "i")),
                                Builders<BsonDocument>.Filter.Eq("fileName", sourceFileName + ".pdf")
                            )).FirstOrDefault();
                            var numeroDossier = fabDoc2 != null && fabDoc2.Contains("numeroDossier") && fabDoc2["numeroDossier"].BsonType == BsonType.String
                                ? fabDoc2["numeroDossier"].AsString : sourceFileName;

                            var notifDoc = new BsonDocument
                            {
                                ["type"] = "bat_ready",
                                ["message"] = $"✅ Le BAT pour le dossier {numeroDossier} est prêt !",
                                ["fileName"] = batFileName,
                                ["numeroDossier"] = numeroDossier,
                                ["recipientLogin"] = requestedBy,
                                ["read"] = false,
                                ["timestamp"] = DateTime.UtcNow
                            };
                            if (!string.IsNullOrEmpty(prismaLogContent))
                                notifDoc["prismaLog"] = prismaLogContent;

                            var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
                            notifCol.InsertOne(notifDoc);
                            Console.WriteLine($"[BAT_FSW] Notification BAT prêt créée pour {requestedBy} (dossier {numeroDossier})");
                        }
                    }
                    catch (Exception exNotif) { Console.WriteLine($"[BAT_FSW][WARN] Create notification: {exNotif.Message}"); }

                    // Mark batPending as processed
                    if (pending != null)
                    {
                        batPendingCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("_id", pending["_id"]),
                            Builders<BsonDocument>.Update.Set("processed", true));
                    }

                    // Delete PrismaPrepare log files from output folder
                    try
                    {
                        var logFiles = Directory.GetFiles(outputDir, "*.log")
                            .Where(f =>
                            {
                                var fn = Path.GetFileName(f);
                                return fn.EndsWith("_WARNING.log", StringComparison.OrdinalIgnoreCase)
                                    || fn.EndsWith("_SUCCESS.log", StringComparison.OrdinalIgnoreCase)
                                    || fn.EndsWith("_OK.log", StringComparison.OrdinalIgnoreCase);
                            });
                        foreach (var logFile in logFiles)
                        {
                            try
                            {
                                File.Delete(logFile);
                                Console.WriteLine($"[BAT_FSW] Deleted PrismaPrepare log: {Path.GetFileName(logFile)}");
                            }
                            catch (Exception exDelLog) { Console.WriteLine($"[BAT_FSW][WARN] Delete log file: {exDelLog.Message}"); }
                        }
                    }
                    catch (Exception exLogs) { Console.WriteLine($"[BAT_FSW][WARN] Log cleanup: {exLogs.Message}"); }

                    // Store last completed info for progress endpoint
                    BatSerializationState.SetStep("completed");
                    BatSerializationState.SetLastCompleted(batFileName, prismaLogContent);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BAT_FSW][ERROR] HandleEpreuve: {ex.Message}");
                }
                finally
                {
                    batRenameSem.Release();
                    // Release BAT serialization lock so the next file can be sent
                    BatSerializationState.Release();
                    Console.WriteLine("[BAT_FSW] BAT serialization lock released.");
                }
            }

            tempCopyWatcher.Created += async (_, e) => await HandleEpreuve(e.FullPath);
            tempCopyWatcher.Changed += async (_, e) => await HandleEpreuve(e.FullPath);
            // Also handle rename-into: some tools write a temp file then rename it to Epreuve.pdf
            tempCopyWatcher.Renamed += async (_, e) =>
            {
                if (e.Name != null && e.Name.Equals("Epreuve.pdf", StringComparison.OrdinalIgnoreCase))
                    await HandleEpreuve(e.FullPath);
            };

            Console.WriteLine($"[INFO] PrismaPrepare output FileSystemWatcher started on {outputDir}");
        }
        else
        {
            Console.WriteLine("[INFO] PrismaPrepare output path not configured — FileSystemWatcher not started.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] PrismaPrepare output watcher init failed: {ex.Message}");
    }
}

// ======================================================
// Logging (console + MongoDB)
// ======================================================

app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Method} {ctx.Request.Path}");
    await next();
    try
    {
        MongoDbHelper.InsertLog(new LogEntry
        {
            Timestamp  = DateTime.Now,
            Method     = ctx.Request.Method,
            Path       = ctx.Request.Path.Value ?? "",
            StatusCode = ctx.Response.StatusCode
        });
    }
    catch (Exception logEx) { Console.WriteLine($"[WARN] MongoDB log failed: {logEx.Message}"); }
});

// ======================================================
// FRONTEND — /pro (static files + fallback SPA)
// ======================================================

var proPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
Console.WriteLine("[INFO] Expected /pro at " + proPath);

if (Directory.Exists(proPath))
{
    var provider = new PhysicalFileProvider(proPath);

    // 1) Static files AVANT le routage
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider     = provider,
        RequestPath      = "/pro",
        DefaultFileNames = new List<string> { "index.html", "index.htm" }
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider        = provider,
        RequestPath         = "/pro",
        ContentTypeProvider = new FileExtensionContentTypeProvider()
    });
}
else
{
    Console.WriteLine("[WARN] wwwroot_pro NOT FOUND at " + proPath);
}

// 2) Routage APRÈS static files, AVANT les Map…
app.UseRouting();

// 3) /pro → /pro/index.html
app.MapGet("/pro", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/pro/index.html");
    return Task.CompletedTask;
});

// 4) Fallback SPA (ne capture pas les fichiers)
app.MapFallback("/pro/{*path}", async (HttpContext ctx) =>
{
    if (Path.HasExtension(ctx.Request.Path))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(Path.Combine(proPath, "index.html"));
});

// ======================================================
// AUTHENTIFICATION — Users Management
// ======================================================

app.MapPost("/api/auth/login", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("login", out var loginEl) ||
            !json.TryGetProperty("password", out var pwdEl))
            return Results.Json(new { ok = false, error = "login et password requis" });

        var login = loginEl.GetString() ?? "";
        var pwd = pwdEl.GetString() ?? "";

        Console.WriteLine($"[DEBUG] Login attempt: {login} / {pwd}");

        var users = BackendUtils.LoadUsers();
        Console.WriteLine($"[DEBUG] Users loaded: {users.Count}");
        foreach (var u in users)
        {
            Console.WriteLine($"[DEBUG]   - {u.Login} / {u.Password}");
        }

        var user = users.FirstOrDefault(u => u.Login == login && u.Password == pwd);

        if (user == null)
        {
            Console.WriteLine($"[DEBUG] User not found or password mismatch");
            return Results.Json(new { ok = false, error = "Identifiants invalides" });
        }

        // Générer un token simple
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user.Id}:{user.Login}:{user.Profile}"));

        Console.WriteLine($"[DEBUG] Login successful for {user.Login}");

        // Log login activity
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = user.Login,
            UserName = user.Name,
            Action = "LOGIN",
            Details = $"Connexion profil {user.Profile}"
        });

        return Results.Json(new
        {
            ok = true,
            token,
            user = new
            {
                id = user.Id,
                login = user.Login,
                profile = user.Profile,
                name = user.Name
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3)
            return Results.Json(new { ok = false, error = "Token invalide" });

        var users = BackendUtils.LoadUsers();
        var user = users.FirstOrDefault(u => u.Id == parts[0]);

        if (user == null)
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        return Results.Json(new
        {
            ok = true,
            user = new
            {
                id = user.Id,
                login = user.Login,
                profile = user.Profile,
                name = user.Name
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/auth/users", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var users = BackendUtils.LoadUsers();
        var list = users.Select(u => new
        {
            id = u.Id,
            login = u.Login,
            profile = u.Profile,
            name = u.Name
        });

        return Results.Json(new { ok = true, users = list });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/auth/register", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("login", out var loginEl) ||
            !json.TryGetProperty("password", out var pwdEl) ||
            !json.TryGetProperty("profile", out var profileEl) ||
            !json.TryGetProperty("name", out var nameEl))
            return Results.BadRequest("login, password, profile, name requis");

        var users = BackendUtils.LoadUsers();
        if (users.Any(u => u.Login == loginEl.GetString()))
            return Results.Json(new { ok = false, error = "Login déjà existant" });

        var newId = MongoDbHelper.GetNextUserId().ToString("D3");
        var newUser = new UserItem
        {
            Id = newId,
            Login = loginEl.GetString() ?? "",
            Password = pwdEl.GetString() ?? "",
            Profile = profileEl.GetInt32(),
            Name = nameEl.GetString() ?? ""
        };

        BackendUtils.InsertUser(newUser);

        // Log account creation
        var creatorLogin = parts.Length >= 2 ? parts[1] : "?";
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = creatorLogin,
            UserName = creatorLogin,
            Action = "CREATE_ACCOUNT",
            Details = $"Compte créé : {newUser.Login} (Profil {newUser.Profile})"
        });

        return Results.Json(new { ok = true, user = new { id = newUser.Id, login = newUser.Login } });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/auth/users/{userId}", (HttpContext ctx, string userId) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        if (!BackendUtils.DeleteUser(userId))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        // Log account deletion
        var delCreatorLogin = parts.Length >= 2 ? parts[1] : "?";
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = delCreatorLogin,
            UserName = delCreatorLogin,
            Action = "DELETE_ACCOUNT",
            Details = $"Compte supprimé : ID {userId}"
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// CORBEILLE — API
// ======================================================

app.MapGet("/api/recycle/list", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var list = Directory.GetFiles(recyclePath)
            .Select(full => new {
                fullPath = full,
                fileName = Path.GetFileName(full),
                deletedAt = File.GetCreationTime(full)
            })
            .OrderByDescending(x => x.deletedAt)
            .ToList();

        return Results.Json(list);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/recycle/restore", (string fullPath, string destinationFolder) =>
{
    try
    {
        var src = Path.GetFullPath(fullPath);
        if (!File.Exists(src))
            return Results.Json(new { ok = false, error = "Fichier introuvable dans la corbeille." });

        var root = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, destinationFolder);
        Directory.CreateDirectory(destDir);

        var dest = Path.Combine(destDir, Path.GetFileName(src));
        File.Move(src, dest);

        return Results.Json(new { ok = true, restoredTo = dest });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/recycle/purge", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var count = 0;
        foreach (var f in Directory.GetFiles(recyclePath))
        {
            var age = DateTime.Now - File.GetCreationTime(f);
            if (age.TotalDays >= recycleDays)
            {
                File.Delete(f);
                count++;
            }
        }
        return Results.Json(new { ok = true, purged = count });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// APIs — Ping / Folders / Jobs (listing)
// ======================================================

app.MapGet("/api/ping", () => "pong");

app.MapGet("/api/file-stage", (string fileName) =>
{
    try
    {
        // Sanitize: only allow the base filename, no path traversal
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            return Results.Json(new { ok = false, folder = (string?)null, fullPath = (string?)null });

        var root = BackendUtils.HotfoldersRoot();
        // Scan in order from most advanced to least advanced so the first match is the real current stage
        var folders = new[]
        {
            // Most advanced first
            "Fin de production", "Façonnage", "Impression en cours",
            "Fiery", "PrismaPrepare", "BAT",
            // Mid-production
            "Prêt pour impression", "Corrections et fond perdu", "Corrections",
            // Early/admin stages
            "Rapport", "Début de production", "Soumission"
        };
        // 1. Check for BAT_{fileName} in the BAT folder first — BAT version takes precedence
        var batName = "BAT_" + safeFileName;
        var batPath = Path.Combine(root, "BAT", batName);
        if (File.Exists(batPath))
            return Results.Json(new { ok = true, folder = "BAT", fullPath = batPath, isBatVersion = true });

        // 2. Physical scan for the file itself, most advanced folder first
        foreach (var folder in folders)
        {
            var path = Path.Combine(root, folder, safeFileName);
            if (File.Exists(path))
                return Results.Json(new { ok = true, folder, fullPath = path });
        }

        return Results.Json(new { ok = false, folder = (string?)null, fullPath = (string?)null });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/folders", () =>
{
    var clean = BackendUtils.Hotfolders()
        .Select(n => n.Replace("\u00A0", " ").Trim())
        .ToArray();
    return Results.Json(clean);
});

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

// ======================================================
// API — FILE
// ======================================================

app.MapGet("/api/file", (string path) =>
{
    var full = Path.GetFullPath(path);
    if (!File.Exists(full))
        return Results.NotFound();

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(full, out var ct))
        ct = "application/octet-stream";

    return Results.File(File.OpenRead(full), ct);
});

// ======================================================
// DELIVERY (planning)
// ======================================================

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
        // Serialize sheet then add locked field
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var json = System.Text.Json.JsonSerializer.Serialize(sheet, opts);
        using var doc2 = System.Text.Json.JsonDocument.Parse(json);
        var root2 = doc2.RootElement;
        var merged = new Dictionary<string, object?>();
        foreach (var prop in root2.EnumerateObject())
            merged[prop.Name] = (object?)prop.Value;
        merged["locked"] = locked;
        return Results.Json(merged);
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

        // Admin-only fields: only profile 3 can update Media1-4, TypeDocument, NombreFeuilles
        var isAdmin = (userProfile == 3);

        var sheet = new FabricationSheet
        {
            FullPath = input.FullPath,
            FileName = string.IsNullOrWhiteSpace(input.FileName)
                ? Path.GetFileName(input.FullPath)
                : input.FileName,

            MoteurImpression = input.MoteurImpression,
            Machine          = input.MoteurImpression ?? input.Machine,
            Operateur        = old?.Operateur,
            Quantite         = input.Quantite,
            TypeTravail      = input.TypeTravail,
            Format           = input.Format,
            Papier           = input.Papier,
            RectoVerso       = input.RectoVerso,
            Encres           = input.Encres,
            Client           = input.Client,
            NumeroAffaire    = input.NumeroAffaire,
            NumeroDossier    = input.NumeroDossier,
            Notes            = input.Notes,
            Faconnage        = input.Faconnage,
            Livraison        = input.Livraison,
            Delai            = input.Delai,

            Media1        = input.Media1        ?? old?.Media1,
            Media2        = input.Media2        ?? old?.Media2,
            Media3        = input.Media3        ?? old?.Media3,
            Media4        = input.Media4        ?? old?.Media4,
            TypeDocument  = isAdmin ? input.TypeDocument  : old?.TypeDocument,
            NombreFeuilles = isAdmin ? input.NombreFeuilles : old?.NombreFeuilles,

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

app.MapGet("/api/fabrication/pdf", (string? fullPath, string? fileName, bool? save) =>
{
    try
    {
        FabricationSheet? sheet = null;
        if (!string.IsNullOrEmpty(fullPath))
            sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null && !string.IsNullOrEmpty(fileName))
            sheet = BackendUtils.FindFabricationByName(fileName);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        var doc = PdfUtils.CreateFabricationPdf(sheet);
        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        ms.Position = 0;
        var pdfBytes = ms.ToArray();

        // Optionally save PDF to production folder (dossier de production)
        if (save == true)
        {
            try
            {
                // Look up production folder for this file
                var safeFileName = Path.GetFileName(sheet.FileName ?? "");
                var baseName = Path.GetFileNameWithoutExtension(safeFileName);
                bool savedToProductionFolder = false;

                var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                BsonDocument? pfDoc = null;
                if (!string.IsNullOrEmpty(safeFileName))
                    pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", safeFileName))
                                .SortByDescending(x => x["createdAt"]).FirstOrDefault();
                if (pfDoc == null && !string.IsNullOrEmpty(sheet.NumeroDossier))
                    pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("numeroDossier", sheet.NumeroDossier))
                                .SortByDescending(x => x["createdAt"]).FirstOrDefault();

                if (pfDoc != null && pfDoc.Contains("folderPath") && !string.IsNullOrEmpty(pfDoc["folderPath"].AsString))
                {
                    var prodFolderPath = pfDoc["folderPath"].AsString;
                    if (Directory.Exists(prodFolderPath))
                    {
                        var pdfPath = Path.Combine(prodFolderPath, $"{baseName}_FicheFabrication.pdf");
                        File.WriteAllBytes(pdfPath, pdfBytes);
                        savedToProductionFolder = true;
                        Console.WriteLine($"[PDF] Fiche enregistrée dans le dossier de production : {pdfPath}");
                    }
                }

                if (!savedToProductionFolder)
                {
                    Console.WriteLine($"[WARN] Dossier de production introuvable pour {safeFileName} — PDF non sauvegardé sur disque");
                }
            }
            catch (Exception saveEx)
            {
                Console.WriteLine($"[WARN] PDF save failed: {saveEx.Message}");
            }
        }

        return Results.File(pdfBytes, "application/pdf", $"FicheFabrication-{sheet.FileName}.pdf");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// FABRICATION — Export XML (for PrismaPrepare)
// ======================================================
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
// BAT — Execute PrismaPrepare command
// ======================================================
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
// ACROBAT — Preflight automatisé en arrière-plan
// ======================================================

app.MapPost("/api/acrobat/preflight", async (HttpContext ctx) =>
{
    string? jsPath = null;
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

        // Determine Preflight profile based on source folder
        string profileName;
        if (folder == "Corrections")
            profileName = "Preflight_Imprimerie";
        else if (folder == "Corrections et fond perdu")
            profileName = "Preflight_Imprimerie_fondperdu";
        else
            return Results.Json(new { ok = false, error = $"Dossier non pris en charge pour le Preflight : {folder}" });

        // Get Acrobat exe path from settings
        var pathsCfg = MongoDbHelper.GetSettings<PathsSettings>("paths");
        var exe = (!string.IsNullOrWhiteSpace(pathsCfg?.AcrobatExePath))
            ? pathsCfg!.AcrobatExePath
            : @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";

        if (!File.Exists(exe))
            return Results.Json(new { ok = false, error = $"Acrobat.exe introuvable : {exe}. Configurez le chemin dans Paramétrage > Chemins d'accès." });

        // Use the per-user JavaScripts folder — no admin rights required,
        // and Acrobat reliably loads scripts from this location at startup.
        var jsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Adobe", "Acrobat", "DC", "JavaScripts");
        Directory.CreateDirectory(jsFolder);

        // Generate unique temp JS script name
        var guid = Guid.NewGuid().ToString("N");
        var jsFileName = $"ga_preflight_{guid}.js";
        jsPath = Path.Combine(jsFolder, jsFileName);

        // Write the temporary Acrobat JavaScript.
        // Wrapped in app.trustedFunction so that app.openDoc() is allowed
        // (Acrobat blocks it in unprivileged contexts).
        // app.setTimeOut delays execution by 2 s to let Acrobat finish initialising.
        var jsFullPath = fullPath.Replace("\\", "/");
        var jsContent = $@"// Script temporaire Preflight - généré par GestionAtelier
var _ga_preflight = app.trustedFunction(function() {{
    app.beginPriv();
    try {{
        var _ga_doc = app.openDoc(""{jsFullPath}"");
        var _ga_profile = Preflight.getProfileByName(""{profileName}"");
        if (_ga_profile) {{
            _ga_doc.preflight(_ga_profile);
            app.execMenuItem(""Save"");
        }} else {{
            console.println(""GestionAtelier Preflight: profil introuvable — {profileName}"");
        }}
        _ga_doc.closeDoc(true);
    }} catch(e) {{
        console.println(""GestionAtelier Preflight error: "" + e);
    }}
    app.endPriv();
    app.quit();
}});
app.setTimeOut('_ga_preflight()', 2000);
";
        await File.WriteAllTextAsync(jsPath, jsContent, System.Text.Encoding.UTF8);

        // Record modification time before launch to detect whether the preflight actually ran.
        var modifiedBefore = File.GetLastWriteTimeUtc(fullPath);

        // Kill any running Acrobat instances so Acrobat starts fresh and loads the new Javascripts/ script.
        // (If a previous Acrobat instance is already open, it will not reload scripts from the Javascripts/ folder
        //  and the new script will never execute.)
        foreach (var pName in new[] { "Acrobat", "AcroRd32" })
        {
            foreach (var existing in System.Diagnostics.Process.GetProcessesByName(pName))
            {
                try { existing.Kill(); existing.WaitForExit(3000); }
                catch (InvalidOperationException) { /* process already exited */ }
                catch (Exception exKill) { Console.WriteLine($"[WARN] Could not kill {pName}: {exKill.Message}"); }
            }
        }
        // Wait for Acrobat to fully release its handles before restarting.
        await Task.Delay(3000);

        // Launch Acrobat minimized — UseShellExecute = true ensures folder-level scripts are loaded.
        // The PDF is opened by the JS script via app.openDoc(), so no CLI argument is needed.
        var acrobatDir = Path.GetDirectoryName(exe)!;
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized,
            WorkingDirectory = acrobatDir
        };
        var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return Results.Json(new { ok = false, error = "Impossible de démarrer Acrobat" });

        // Poll for file modification or Acrobat exit (max 5 minutes = 60 × 5 s).
        // WaitForExitAsync alone is unreliable when Acrobat reattaches to an existing instance.
        var lastMod = File.GetLastWriteTime(fullPath);
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(5000);
            if (!File.Exists(fullPath) || File.GetLastWriteTime(fullPath) > lastMod)
                break;
            // Also stop waiting if Acrobat has already exited
            if (System.Diagnostics.Process.GetProcessesByName("Acrobat").Length == 0 &&
                System.Diagnostics.Process.GetProcessesByName("AcroRd32").Length == 0)
                break;
        }

        // Clean up temp JS script
        try { if (File.Exists(jsPath)) { File.Delete(jsPath); jsPath = null; } }
        catch (Exception exJs) { Console.WriteLine($"[WARN] Could not delete temp JS script {jsPath}: {exJs.Message}"); }

        // Detect potential file rename by Preflight profile (e.g. suffix _X4 added by "Convert to PDF/X-4" profiles).
        // The profile may save the file under a new name in the same directory; we detect this by looking
        // for recently-modified files whose name starts with the original base name but differs from it.
        var dir = Path.GetDirectoryName(fullPath)!;
        var originalName = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        var effectivePath = fullPath;

        if (!File.Exists(fullPath))
        {
            // File was renamed — search for files with similar base name modified recently
            var candidates = Directory.GetFiles(dir, $"{originalName}*{ext}")
                .Where(f => !string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (candidates.Count > 0)
            {
                var renamed = candidates[0];
                File.Move(renamed, fullPath, overwrite: true);
                effectivePath = fullPath;
                Console.WriteLine($"[PREFLIGHT] Renamed {Path.GetFileName(renamed)} → {Path.GetFileName(fullPath)}");
            }
            else
            {
                return Results.Json(new { ok = false, error = $"Fichier introuvable après Preflight (il a peut-être été renommé) : {fullPath}" });
            }
        }

        // Verify that the preflight actually ran by checking whether the file was modified.
        // If the modification time is unchanged the profile was likely not found in Acrobat;
        // in that case abort rather than silently moving an un-processed file.
        var modifiedAfter = File.GetLastWriteTimeUtc(effectivePath);
        if (modifiedAfter <= modifiedBefore)
        {
            Console.WriteLine($"[PREFLIGHT] File not modified after Acrobat exit — profile '{profileName}' may not exist in Acrobat.");
            return Results.Json(new { ok = false, error = $"Le Preflight ne semble pas avoir modifié le fichier. Vérifiez que le profil « {profileName} » existe dans Acrobat." });
        }

        // Move file to "Prêt pour impression"
        var root = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, "Prêt pour impression");
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, Path.GetFileName(effectivePath));
        File.Move(effectivePath, destPath, overwrite: true);

        // Update delivery path in MongoDB
        try { BackendUtils.UpdateDeliveryPath(effectivePath, destPath); } catch (Exception ex2) { Console.WriteLine($"[WARN] UpdateDeliveryPath: {ex2.Message}"); }

        // Update assignment path
        try
        {
            var assignCol = MongoDbHelper.GetCollection<BsonDocument>("assignments");
            var oldNorm = effectivePath.Replace("\\", "/");
            var newNorm = destPath.Replace("\\", "/");
            assignCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", effectivePath),
                    Builders<BsonDocument>.Filter.Eq("fullPath", oldNorm)),
                Builders<BsonDocument>.Update.Set("fullPath", destPath));
        }
        catch (Exception exA) { Console.WriteLine($"[WARN] UpdateAssignmentPath: {exA.Message}"); }

        // Update fabrication path
        try
        {
            var oldNorm2 = effectivePath.Replace("\\", "/");
            var newNorm2 = destPath.Replace("\\", "/");
            var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrications");
            fabCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", effectivePath),
                    Builders<BsonDocument>.Filter.Eq("fullPath", oldNorm2)),
                Builders<BsonDocument>.Update.Set("fullPath", destPath));
            var fabSheetsCol = MongoDbHelper.GetCollection<BsonDocument>("fabricationSheets");
            fabSheetsCol.UpdateMany(
                Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("fullPath", effectivePath),
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
            Details = $"Preflight automatique ({profileName}) : {Path.GetFileName(effectivePath)} → Prêt pour impression"
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        // Clean up temp JS on error
        if (jsPath != null) { try { if (File.Exists(jsPath)) File.Delete(jsPath); } catch { } }
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/tools/prismasync", () =>
{
    try
    {
        var url = "http://172.26.197.212/Authentication/";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
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
// Routes racine
// ======================================================

app.MapGet("/", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/pro/index.html");
    return Task.CompletedTask;
});

app.MapGet("/debug/pro", () =>
{
    var path  = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    var files = Directory.Exists(path)
        ? Directory.GetFiles(path)
                  .Select(f => Path.GetFileName(f))
                  .Where(n => n is not null)
                  .Select(n => n!)
                  .ToArray()
        : Array.Empty<string>();

    return Results.Json(new { expected = path, exists = Directory.Exists(path), files });
});

// ======================================================
// DOSSIERS DE PRODUCTION — API
// ======================================================

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
        var stageProgress = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Début de production", 0 },
            { "1.Reception", 0 },
            { "Corrections", 25 },
            { "Corrections et fond perdu", 25 },
            { "Prêt pour impression", 50 },
            { "6.Archivage", 50 },
            { "BAT", 65 },
            { "4.BAT", 65 },
            { "PrismaPrepare", 75 },
            { "Fiery", 75 },
            { "Impression en cours", 75 },
            { "Façonnage", 90 },
            { "Fin de production", 100 }
        };

        int GetProgress(string? stage)
        {
            if (string.IsNullOrEmpty(stage)) return 0;
            foreach (var kv in stageProgress)
                if (stage.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            return 0;
        }

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
                progress = GetProgress(stage)
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

        var stageProgress = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Début de production", 0 },
            { "Corrections", 25 },
            { "Corrections et fond perdu", 25 },
            { "Prêt pour impression", 50 },
            { "BAT", 65 },
            { "PrismaPrepare", 75 },
            { "Fiery", 75 },
            { "Impression en cours", 75 },
            { "Façonnage", 90 },
            { "Fin de production", 100 }
        };

        int GetProgress(string? stage)
        {
            if (string.IsNullOrEmpty(stage)) return 0;
            foreach (var kv in stageProgress)
                if (stage.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            return 0;
        }

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var allFabs = fabCol.Find(new BsonDocument()).ToList();

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

                // Determine effective stage: if BAT_{fName} exists in the BAT folder, stage is BAT
                var effectiveStage = folder;
                var batVariant = "BAT_" + fName;
                if (batFilesInBatFolder.Contains(batVariant))
                    effectiveStage = "BAT";

                // Find matching fabrication by fileName or fullPath (guard against BsonNull values)
                var fab = allFabs.FirstOrDefault(f =>
                    (f.Contains("fileName") && f["fileName"] != BsonNull.Value &&
                     string.Equals(f["fileName"].AsString, fName, StringComparison.OrdinalIgnoreCase)) ||
                    (f.Contains("fullPath") && f["fullPath"] != BsonNull.Value &&
                     string.Equals(Path.GetFileName(f["fullPath"].AsString), fName, StringComparison.OrdinalIgnoreCase)));

                var numeroDossier = fab != null && fab.Contains("numeroDossier") && fab["numeroDossier"] != BsonNull.Value ? fab["numeroDossier"].AsString : "";
                var client = fab != null && fab.Contains("client") && fab["client"] != BsonNull.Value ? fab["client"].AsString : "";
                var typeTravail = fab != null && fab.Contains("typeTravail") && fab["typeTravail"] != BsonNull.Value ? fab["typeTravail"].AsString : "";

                entries.Add(new
                {
                    fileName = fName,
                    fullPath = filePath,
                    currentStage = effectiveStage,
                    progress = GetProgress(effectiveStage),
                    numeroDossier,
                    client,
                    typeTravail
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
// DEBUG — Endpoints
// ======================================================
var summaries = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
    .OfType<RouteEndpoint>()
    .Where(e => e.RoutePattern.RawText?.StartsWith("/api") ?? false)
    .Select(e => e.RoutePattern.RawText)
    .OrderBy(x => x)
    .ToList();

Console.WriteLine("\n[DEBUG] === ENDPOINTS /api ENREGISTRÉS ===");
foreach (var s in summaries)
    Console.WriteLine($"  {s}");
Console.WriteLine("[DEBUG] === FIN LISTE ===\n");

// ======================================================
// CONFIG — Schedule (plages horaires + jours fériés)
// ======================================================

app.MapGet("/api/config/schedule", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };
        return Results.Json(new { ok = true, config = new { workStart = cfg.WorkStart, workEnd = cfg.WorkEnd, holidays = cfg.Holidays } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/schedule", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        if (json.TryGetProperty("workStart", out var wsEl)) existing.WorkStart = wsEl.GetString() ?? existing.WorkStart;
        if (json.TryGetProperty("workEnd", out var weEl)) existing.WorkEnd = weEl.GetString() ?? existing.WorkEnd;

        MongoDbHelper.UpsertSettings("schedule", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/config/schedule/holidays", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("date", out var dateEl))
            return Results.Json(new { ok = false, error = "date requis" });

        var dateStr = dateEl.GetString() ?? "";
        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        if (!existing.Holidays.Contains(dateStr))
        {
            existing.Holidays.Add(dateStr);
            existing.Holidays.Sort();
            MongoDbHelper.UpsertSettings("schedule", existing);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/schedule/holidays", (HttpContext ctx, string date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        existing.Holidays.Remove(date);
        MongoDbHelper.UpsertSettings("schedule", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Paths (chemins d'accès)
// ======================================================

app.MapGet("/api/config/paths", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };
        return Results.Json(new { ok = true, config = new { hotfoldersRoot = cfg.HotfoldersRoot, recycleBinPath = cfg.RecycleBinPath, acrobatExePath = cfg.AcrobatExePath } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/paths", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };

        if (json.TryGetProperty("hotfoldersRoot", out var hrEl) && !string.IsNullOrWhiteSpace(hrEl.GetString()))
            existing.HotfoldersRoot = hrEl.GetString()!;
        if (json.TryGetProperty("recycleBinPath", out var rbEl))
            existing.RecycleBinPath = rbEl.GetString() ?? existing.RecycleBinPath;
        if (json.TryGetProperty("acrobatExePath", out var aeEl))
            existing.AcrobatExePath = aeEl.GetString() ?? existing.AcrobatExePath;

        MongoDbHelper.UpsertSettings("paths", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Fabrication Imports
// ======================================================

app.MapGet("/api/config/fabrication-imports", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();
        return Results.Json(new { ok = true, config = new {
            media1Path = cfg.Media1Path, media2Path = cfg.Media2Path,
            media3Path = cfg.Media3Path, media4Path = cfg.Media4Path,
            typeDocumentPath = cfg.TypeDocumentPath
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/fabrication-imports", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();

        if (json.TryGetProperty("media1Path", out var m1)) existing.Media1Path = m1.GetString() ?? "";
        if (json.TryGetProperty("media2Path", out var m2)) existing.Media2Path = m2.GetString() ?? "";
        if (json.TryGetProperty("media3Path", out var m3)) existing.Media3Path = m3.GetString() ?? "";
        if (json.TryGetProperty("media4Path", out var m4)) existing.Media4Path = m4.GetString() ?? "";
        if (json.TryGetProperty("typeDocumentPath", out var td)) existing.TypeDocumentPath = td.GetString() ?? "";

        MongoDbHelper.UpsertSettings("fabrication_imports", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — Façonnage options (CSV import)
// ======================================================

app.MapGet("/api/settings/faconnage-options", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
        var docs = col.Find(new BsonDocument()).ToList();
        var labels = docs.Select(d => d.Contains("label") ? d["label"].AsString : "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        return Results.Json(labels);
    }
    catch { return Results.Json(new List<string>()); }
});

app.MapPost("/api/settings/faconnage-import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        if (!ctx.Request.HasFormContentType)
            return Results.Json(new { ok = false, error = "Form data required" });

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null)
            return Results.Json(new { ok = false, error = "Fichier CSV requis" });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var labels = content
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            // Only the first comma-separated column is used as the option label
            .Select(l => l.Split(',').First().Trim().Trim('"'))
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct()
            .ToList();

        if (labels.Count == 0)
            return Results.Json(new { ok = false, error = "Aucune option trouvée dans le CSV" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
        col.DeleteMany(new BsonDocument());
        var docs = labels.Select(l => new BsonDocument { ["label"] = l }).ToList();
        col.InsertMany(docs);

        return Results.Json(new { ok = true, count = labels.Count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});



app.MapGet("/api/config/integrations", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();
        return Results.Json(new { ok = true, config = new { preparePath = cfg.PreparePath, fieryPath = cfg.FieryPath, tempCopyPath = cfg.TempCopyPath, prismaPrepareExePath = cfg.PrismaPrepareExePath, prismaPrepareOutputPath = cfg.PrismaPrepareOutputPath } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/integrations", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();

        if (json.TryGetProperty("preparePath", out var pp)) existing.PreparePath = pp.GetString() ?? "";
        if (json.TryGetProperty("fieryPath", out var fp)) existing.FieryPath = fp.GetString() ?? "";
        if (json.TryGetProperty("tempCopyPath", out var tcp)) existing.TempCopyPath = tcp.GetString() ?? "";
        if (json.TryGetProperty("prismaPrepareExePath", out var ppe)) existing.PrismaPrepareExePath = ppe.GetString() ?? "";
        if (json.TryGetProperty("prismaPrepareOutputPath", out var ppop)) existing.PrismaPrepareOutputPath = ppop.GetString() ?? IntegrationsSettings.DefaultPrismaPrepareOutputPath;

        MongoDbHelper.UpsertSettings("integrations", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Moteurs d'impression (CRUD + MongoDB)
// ======================================================

app.MapGet("/api/config/print-engines", () =>
{
    try
    {
        var engines = MongoDbHelper.GetPrintEnginesWithIp();
        if (engines.Count == 0)
        {
            // Return default list if none configured
            return Results.Json(new[] {
                new { name = "Offset", ip = "" }, new { name = "Numérique", ip = "" },
                new { name = "Jet d'encre", ip = "" }, new { name = "Sérigraphie", ip = "" },
                new { name = "Flexographie", ip = "" }, new { name = "Héliogravure", ip = "" },
                new { name = "Tampographie", ip = "" }, new { name = "Laser", ip = "" }
            });
        }
        return Results.Json(engines);
    }
    catch (Exception)
    {
        return Results.Json(new object[0]);
    }
});

app.MapPost("/api/config/print-engines", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return Results.Json(new { ok = false, error = "name requis" });

        var ip = json.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "" : "";
        MongoDbHelper.AddPrintEngineWithIp(nameEl.GetString()!, ip);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/config/print-engines/import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("engines", out var enginesEl))
            return Results.Json(new { ok = false, error = "engines requis" });

        int count = 0;
        foreach (var e in enginesEl.EnumerateArray())
        {
            string name = "", ip = "";
            if (e.ValueKind == JsonValueKind.Object)
            {
                name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                ip   = e.TryGetProperty("ip",   out var i) ? i.GetString() ?? "" : "";
            }
            else
            {
                name = e.GetString() ?? "";
            }
            if (!string.IsNullOrWhiteSpace(name)) { MongoDbHelper.AddPrintEngineWithIp(name, ip); count++; }
        }

        return Results.Json(new { ok = true, count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/print-engines/{name}", (HttpContext ctx, string name) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        MongoDbHelper.RemovePrintEngine(name);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Work Types
// ======================================================

app.MapGet("/api/config/work-types", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        var types = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => d.Contains("name") ? d["name"].AsString : "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .OrderBy(s => s)
            .ToList();
        return Results.Json(types);
    }
    catch (Exception) { return Results.Json(new string[0]); }
});

app.MapPost("/api/config/work-types/import", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null) return Results.Json(new { ok = false, error = "Fichier manquant" });

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        int count = 0;
        foreach (var line in lines)
        {
            var name = line.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(name)) continue;
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var existing = col.Find(filter).FirstOrDefault();
            if (existing == null)
            {
                col.InsertOne(new BsonDocument { ["name"] = name });
                count++;
            }
        }
        return Results.Json(new { ok = true, count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/work-types/{name}", (string name) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("name", Uri.UnescapeDataString(name)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Hotfolder Routing (type de travail → chemin hotfolder PrismaPrepare)
// ======================================================

app.MapGet("/api/config/hotfolder-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] hotfolder-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/hotfolder-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/hotfolder-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", Uri.UnescapeDataString(typeTravail)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Fiery Routing (type de travail → hotfolder Fiery)
// ======================================================

app.MapGet("/api/config/fiery-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] fiery-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/fiery-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/fiery-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", Uri.UnescapeDataString(typeTravail)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — PrismaSync Routing (presse/moteur → workflow)
// ======================================================

app.MapGet("/api/config/prismasync-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                _id = d["_id"].ToString(),
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                moteurImpression = d.Contains("moteurImpression") ? d["moteurImpression"].AsString : "",
                media1 = d.Contains("media1") ? d["media1"].AsString : "",
                media2 = d.Contains("media2") ? d["media2"].AsString : "",
                media3 = d.Contains("media3") ? d["media3"].AsString : "",
                media4 = d.Contains("media4") ? d["media4"].AsString : "",
                prismaSyncPath = d.Contains("prismaSyncPath") ? d["prismaSyncPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail) && !string.IsNullOrEmpty(r.moteurImpression))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] prismasync-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/prismasync-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var id = json.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : "";
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var moteurImpression = json.TryGetProperty("moteurImpression", out var mi) ? mi.GetString() ?? "" : "";
        var media1 = json.TryGetProperty("media1", out var m1) ? m1.GetString() ?? "" : "";
        var media2 = json.TryGetProperty("media2", out var m2) ? m2.GetString() ?? "" : "";
        var media3 = json.TryGetProperty("media3", out var m3) ? m3.GetString() ?? "" : "";
        var media4 = json.TryGetProperty("media4", out var m4) ? m4.GetString() ?? "" : "";
        var prismaSyncPath = json.TryGetProperty("prismaSyncPath", out var psp) ? psp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail) && string.IsNullOrEmpty(moteurImpression))
            return Results.Json(new { ok = false, error = "typeTravail ou moteurImpression manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var doc = new BsonDocument
        {
            ["typeTravail"] = typeTravail,
            ["moteurImpression"] = moteurImpression,
            ["media1"] = media1,
            ["media2"] = media2,
            ["media3"] = media3,
            ["media4"] = media4,
            ["prismaSyncPath"] = prismaSyncPath
        };
        if (!string.IsNullOrEmpty(id) && MongoDB.Bson.ObjectId.TryParse(id, out var oid))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
            col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = false });
        }
        else
        {
            col.InsertOne(doc);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/prismasync-routing/{id}", (string id) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var decodedId = Uri.UnescapeDataString(id);
        if (MongoDB.Bson.ObjectId.TryParse(decodedId, out var oid))
        {
            col.DeleteMany(Builders<BsonDocument>.Filter.Eq("_id", oid));
        }
        else
        {
            // Fallback: legacy delete by printEngine field
            col.DeleteMany(Builders<BsonDocument>.Filter.Eq("printEngine", decodedId));
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Direct Print Routing (type de travail + moteur → hotfolder)
// ======================================================

app.MapGet("/api/config/direct-print-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                printEngine = d.Contains("printEngine") ? d["printEngine"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] direct-print-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/direct-print-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var printEngine = json.TryGetProperty("printEngine", out var pe) ? pe.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
            Builders<BsonDocument>.Filter.Eq("printEngine", printEngine));
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["printEngine"] = printEngine, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/direct-print-routing", async (HttpContext ctx) =>
{
    try
    {
        var typeTravail = ctx.Request.Query["typeTravail"].ToString();
        var printEngine = ctx.Request.Query["printEngine"].ToString();
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
            Builders<BsonDocument>.Filter.Eq("printEngine", printEngine));
        col.DeleteMany(filter);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ROUTAGE PRISMA PREPARE (action "Ouvrir dans PrismaPrepare")
// ======================================================

app.MapGet("/api/config/prisma-prepare-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        var docs = col.Find(new BsonDocument()).ToList();
        var result = docs.Select(d => new {
            typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
            hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
        });
        return Results.Json(result);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] prisma-prepare-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/prisma-prepare-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(typeTravail)) return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/prisma-prepare-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// PRINT — Send to print (Fiery / PrismaSync / Direct)
// ======================================================

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

app.MapGet("/api/config/paper-catalog", () =>
{
    try
    {
        // Look for Paper Catalog.xml in app directory or common locations
        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Paper Catalog.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Paper Catalog.xml"),
            Path.Combine(BackendUtils.HotfoldersRoot(), "..", "Paper Catalog.xml"),
            "Paper Catalog.xml"
        };

        string? xmlPath = searchPaths.FirstOrDefault(p => File.Exists(p));
        if (xmlPath == null)
            return Results.Json(new string[0]);

        // Load XML with secure settings to prevent XXE attacks
        var xmlSettings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null
        };
        XDocument doc;
        using (var xmlReader = System.Xml.XmlReader.Create(xmlPath, xmlSettings))
        {
            doc = XDocument.Load(xmlReader);
        }

        // JDF format (Fiery/EFI Paper Catalog): <Media DescriptiveName="..." />
        var names = doc.Descendants()
            .Where(el => el.Name.LocalName == "Media")
            .Select(el => (string?)(el.Attribute("DescriptiveName") ?? el.Attribute("descriptiveName")))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (!names.Any())
        {
            // Fallback: try CatalogEntry/Paper/Entry elements with Name/name attribute
            names = doc.Descendants()
                .Where(el => el.Name.LocalName == "CatalogEntry" || el.Name.LocalName == "Paper" || el.Name.LocalName == "Entry")
                .Select(el => (string?)(el.Attribute("Name") ?? el.Attribute("name") ?? el.Attribute("mediaName") ?? el.Attribute("MediaName")))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        if (!names.Any())
        {
            // Last resort: all leaf text content
            names = doc.Descendants()
                .Where(el => !el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
                .Select(el => el.Value.Trim())
                .Where(n => n.Length > 0 && n.Length < 200)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        return Results.Json(names);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] Paper catalog parse error: {ex.Message}");
        return Results.Json(new string[0]);
    }
});

// ======================================================
// ADMIN — Activity Logs
// ======================================================

app.MapGet("/api/admin/activity-logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetActivityLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ADMIN — Logs
// ======================================================

app.MapGet("/api/admin/logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetRecentLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ADMIN — Stats (Dashboard)
// ======================================================

app.MapGet("/api/admin/stats", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3)
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var statsUsers = BackendUtils.LoadUsers();
        if (!statsUsers.Any(u => u.Id == parts[0]))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        var root = BackendUtils.HotfoldersRoot();
        var filesByFolder = new Dictionary<string, int>();
        int totalFiles = 0;

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                var count = Directory.GetFiles(dir).Length;
                filesByFolder[folderName] = count;
                totalFiles += count;
            }
        }

        // Scheduled this week
        var now = DateTime.Now;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);
        var deliveries = BackendUtils.LoadDeliveries();
        var scheduledThisWeek = deliveries.Values.Count(d => d.Date >= startOfWeek && d.Date < endOfWeek);

        // Active assignments
        var assignments = BackendUtils.LoadAssignments();
        var activeAssignments = assignments.Count;

        return Results.Json(new { ok = true, stats = new {
            totalFiles,
            filesByFolder,
            scheduledThisWeek,
            activeAssignments
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/admin/migrate-to-mongo", () =>
{
    var results = new List<string>();
    var appData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FluxAtelier");

    // ---- Users ----
    try
    {
        var usersPath = Path.Combine(appData, "users.json");
        if (File.Exists(usersPath))
        {
            var json = File.ReadAllText(usersPath);
            var users = JsonSerializer.Deserialize<List<UserItem>>(json) ?? new();
            var col = MongoDbHelper.GetUsersCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var u in users)
                BackendUtils.InsertUser(u);
            File.Move(usersPath, usersPath + ".bak", overwrite: true);
            results.Add($"Users: {users.Count} migrated");
        }
        else
        {
            results.Add("Users: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Users error: {ex.Message}"); }

    // ---- Deliveries ----
    try
    {
        var deliveriesPath = Path.Combine(appData, "deliveries.json");
        if (File.Exists(deliveriesPath))
        {
            var json = File.ReadAllText(deliveriesPath);
            var list = JsonSerializer.Deserialize<List<DeliveryItem>>(json) ?? new();
            var col = MongoDbHelper.GetDeliveriesCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var item in list)
                BackendUtils.UpsertDelivery(item);
            File.Move(deliveriesPath, deliveriesPath + ".bak", overwrite: true);
            results.Add($"Deliveries: {list.Count} migrated");
        }
        else
        {
            results.Add("Deliveries: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Deliveries error: {ex.Message}"); }

    // ---- Fabrications ----
    try
    {
        var fabricationsPath = Path.Combine(appData, "fabrications.json");
        if (File.Exists(fabricationsPath))
        {
            var json = File.ReadAllText(fabricationsPath);
            var list = JsonSerializer.Deserialize<List<FabricationSheet>>(json) ?? new();
            var col = MongoDbHelper.GetFabricationsCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var sheet in list)
                BackendUtils.UpsertFabrication(sheet);
            File.Move(fabricationsPath, fabricationsPath + ".bak", overwrite: true);
            results.Add($"Fabrications: {list.Count} migrated");
        }
        else
        {
            results.Add("Fabrications: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Fabrications error: {ex.Message}"); }

    return Results.Json(new { ok = true, results });
});

// ======================================================
// ACROBAT — Complete processing
// ======================================================
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
app.MapGet("/api/config/commands", (HttpContext ctx) =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = col.Find(new BsonDocument()).FirstOrDefault();
    if (doc == null)
        return Results.Json(new { ok = true, config = new {
            batCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /T \"{typeWork}\" /SP /C {quantity}",
            prismaPrepareCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            printCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /SP /C {quantity}",
            modifyCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            fieryHotfolderBase = "C:\\Fiery\\Hotfolders",
            controllerPath = "C:\\PrismaSync\\Controller"
        }});
    return Results.Json(new { ok = true, config = new {
        batCommand = doc.Contains("batCommand") ? doc["batCommand"].AsString : "",
        prismaPrepareCommand = doc.Contains("prismaPrepareCommand") ? doc["prismaPrepareCommand"].AsString : "",
        prismaCommand = doc.Contains("prismaCommand") ? doc["prismaCommand"].AsString : "",
        printCommand = doc.Contains("printCommand") ? doc["printCommand"].AsString : "",
        modifyCommand = doc.Contains("modifyCommand") ? doc["modifyCommand"].AsString : "",
        fieryHotfolderBase = doc.Contains("fieryHotfolderBase") ? doc["fieryHotfolderBase"].AsString : "",
        controllerPath = doc.Contains("controllerPath") ? doc["controllerPath"].AsString : ""
    }});
});

app.MapPut("/api/config/commands", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = new BsonDocument();
    foreach (var prop in json.EnumerateObject())
        doc[prop.Name] = prop.Value.GetString() ?? "";
    col.ReplaceOne(new BsonDocument(), doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// COMMANDS — BAT, Print, Send etc.
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
    return Results.Json(new { ok = true, command = cmd, batAlertDelayHours = alertDelayHours });
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
    col.ReplaceOne(Builders<BsonDocument>.Filter.Empty, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// ALERTS — BAT EN ATTENTE
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
app.MapGet("/api/config/action-buttons", () =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("actionButtonsConfig");
    var doc = col.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
    var defaults = new {
        controller = @"C:\Program Files\Canon\PRISMACore\PrismaSync.exe",
        prismaPrepare = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        print = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        modification = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        fiery = @"C:\FieryHotfolder"
    };
    if (doc == null) return Results.Json(new { ok = true, buttons = defaults });
    return Results.Json(new {
        ok = true,
        buttons = new {
            controller = doc.Contains("controller") ? doc["controller"].AsString : defaults.controller,
            prismaPrepare = doc.Contains("prismaPrepare") ? doc["prismaPrepare"].AsString : defaults.prismaPrepare,
            print = doc.Contains("print") ? doc["print"].AsString : defaults.print,
            modification = doc.Contains("modification") ? doc["modification"].AsString : defaults.modification,
            fiery = doc.Contains("fiery") ? doc["fiery"].AsString : defaults.fiery
        }
    });
});

app.MapPut("/api/config/action-buttons", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var buttons = json.TryGetProperty("buttons", out var b) ? b : default;
    var doc = new BsonDocument();
    if (buttons.ValueKind == JsonValueKind.Object)
    {
        if (buttons.TryGetProperty("controller", out var v1)) doc["controller"] = v1.GetString() ?? "";
        if (buttons.TryGetProperty("prismaPrepare", out var v2)) doc["prismaPrepare"] = v2.GetString() ?? "";
        if (buttons.TryGetProperty("print", out var v3)) doc["print"] = v3.GetString() ?? "";
        if (buttons.TryGetProperty("modification", out var v4)) doc["modification"] = v4.GetString() ?? "";
        if (buttons.TryGetProperty("fiery", out var v5)) doc["fiery"] = v5.GetString() ?? "";
    }
    var col = MongoDbHelper.GetCollection<BsonDocument>("actionButtonsConfig");
    col.ReplaceOne(Builders<BsonDocument>.Filter.Empty, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// DELETE PRODUCTION FOLDER
// ======================================================
app.MapDelete("/api/production-folder", async (string path) =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var prodRoot = Path.GetFullPath(Path.Combine(root, "DossiersProduction"));
        var fullPath = Path.GetFullPath(path);
        // Security: ensure path is within production folders root using canonical paths
        var relative = Path.GetRelativePath(prodRoot, fullPath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative) ||
            !fullPath.StartsWith(prodRoot, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { ok = false, error = "Chemin non autorisé" });
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        // Remove MongoDB entry
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        col.DeleteMany(Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("path", fullPath),
            Builders<BsonDocument>.Filter.Eq("folderPath", fullPath)
        ));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// NOTIFICATIONS
// ======================================================
app.MapGet("/api/notifications", (string? login) =>
{
    if (string.IsNullOrWhiteSpace(login)) return Results.Json(new object[0]);
    var col = MongoDbHelper.GetCollection<BsonDocument>("notifications");
    var filter = Builders<BsonDocument>.Filter.And(
        Builders<BsonDocument>.Filter.Eq("recipientLogin", login),
        Builders<BsonDocument>.Filter.Eq("read", false)
    );
    var docs = col.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("timestamp")).Limit(20).ToList();
    return Results.Json(docs.Select(d => new {
        id = d["_id"].ToString(),
        type = d.Contains("type") ? d["type"].AsString : "general",
        message = d.Contains("message") ? d["message"].AsString : "",
        fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
        numeroDossier = d.Contains("numeroDossier") ? d["numeroDossier"].AsString : "",
        timestamp = d.Contains("timestamp") ? d["timestamp"].ToUniversalTime().ToString("o") : "",
        read = d.Contains("read") && d["read"].AsBoolean,
        prismaLog = d.Contains("prismaLog") ? d["prismaLog"].AsString : ""
    }));
});

app.MapPut("/api/notifications/read", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var login = json.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("notifications");
    var filter = Builders<BsonDocument>.Filter.Eq("recipientLogin", login);
    col.UpdateMany(filter, Builders<BsonDocument>.Update.Set("read", true));
    return Results.Json(new { ok = true });
});

// ======================================================
// JOBS — Archiver (déplacer vers le dossier de production archive/)
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
            var fabFilter = Builders<BsonDocument>.Filter.Eq("fileName", f.name);
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

// ======================================================
// BACKGROUND TASK — Alertes façonnage 12h et 17h
// ======================================================
_ = Task.Run(async () =>
{
    var lastAlertHour = -1;
    while (true)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            var now = DateTime.Now;
            var hour = now.Hour;
            if ((hour == 12 || hour == 17) && lastAlertHour != hour)
            {
                lastAlertHour = hour;
                var root = BackendUtils.HotfoldersRoot();
                var folder = Path.Combine(root, "Impression en cours");
                if (!Directory.Exists(folder)) continue;

                var files = Directory.GetFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(folder, "*.PDF", SearchOption.TopDirectoryOnly))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(f => Path.GetFileName(f))
                    .ToList();

                if (files.Count == 0) continue;

                var fabCol = MongoDbHelper.GetFabricationsCollection();
                var alertItems = new BsonArray();
                foreach (var fn in files)
                {
                    var fabDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fn)).FirstOrDefault();
                    var faconnage = new BsonArray();
                    if (fabDoc != null && fabDoc.Contains("faconnage") && fabDoc["faconnage"] != BsonNull.Value
                        && fabDoc["faconnage"].IsBsonArray)
                        faconnage = fabDoc["faconnage"].AsBsonArray;

                    var nd = fabDoc != null && fabDoc.Contains("numeroDossier") && fabDoc["numeroDossier"] != BsonNull.Value
                        ? fabDoc["numeroDossier"].AsString : "";
                    alertItems.Add(new BsonDocument
                    {
                        ["fileName"] = fn,
                        ["numeroDossier"] = nd,
                        ["faconnage"] = faconnage
                    });
                }

                var alertCol = MongoDbHelper.GetCollection<BsonDocument>("faconnageAlerts");
                await alertCol.InsertOneAsync(new BsonDocument
                {
                    ["generatedAt"] = DateTime.UtcNow,
                    ["hour"] = hour,
                    ["items"] = alertItems
                });

                // Create notification for all operators
                var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
                var users = BackendUtils.LoadUsers();
                foreach (var u in users.Where(u => u.Profile >= 2))
                {
                    await notifCol.InsertOneAsync(new BsonDocument
                    {
                        ["type"] = "faconnage_alert",
                        ["recipientLogin"] = u.Login,
                        ["message"] = $"📋 Façonnage {hour}h : {files.Count} job(s) en impression en cours",
                        ["count"] = files.Count,
                        ["timestamp"] = DateTime.UtcNow,
                        ["read"] = false,
                        ["items"] = alertItems
                    });
                }

                Console.WriteLine($"[INFO] Faconnage alert generated at {hour}h for {files.Count} job(s)");
            }
            else if (hour != 12 && hour != 17)
            {
                // Reset for next occurrence
                if (lastAlertHour == hour) lastAlertHour = -1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Faconnage alert task error: {ex.Message}");
        }
    }
});

// ======================================================
// LOGO — Upload et affichage
// ======================================================
app.MapGet("/api/logo", (HttpContext ctx) =>
{
    var logoDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    // Try all supported extensions
    string? found = null;
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
    {
        var candidate = Path.Combine(logoDir, "logo" + ext);
        if (File.Exists(candidate)) { found = candidate; break; }
    }
    if (found == null)
        return Results.NotFound();
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(found, out var ct)) ct = "image/png";
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
    return Results.File(File.OpenRead(found), ct);
});

app.MapPost("/api/logo", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
            return Results.Json(new { ok = false, error = "Fichier manquant" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif" && ext != ".webp")
            return Results.Json(new { ok = false, error = "Format non supporté (PNG, JPG, GIF, WEBP)" });

        var logoDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
        // Ensure target directory exists before writing
        Directory.CreateDirectory(logoDir);

        // Remove any existing logo files
        foreach (var old in Directory.GetFiles(logoDir, "logo.*"))
        {
            if (Path.GetFileNameWithoutExtension(old).Equals("logo", StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        }

        var logoPath = Path.Combine(logoDir, "logo" + ext);
        using var stream = File.Create(logoPath);
        await file.CopyToAsync(stream);

        // If not png, also copy as logo.png for consistent URL
        if (ext != ".png")
        {
            var pngPath = Path.Combine(logoDir, "logo.png");
            File.Copy(logoPath, pngPath, overwrite: true);
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] POST /api/logo: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/logo", (HttpContext ctx) =>
{
    try
    {
        var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
        if (Directory.Exists(dir))
        {
            foreach (var logoFile in Directory.GetFiles(dir, "logo.*"))
            {
                if (Path.GetFileNameWithoutExtension(logoFile).Equals("logo", StringComparison.OrdinalIgnoreCase))
                    File.Delete(logoFile);
            }
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// RUN
// ======================================================
app.Run();
// Ensure the TEMP_COPY watcher is never GC-collected before the app exits.
GC.KeepAlive(tempCopyWatcher);


// ======================================================
// MongoDB Counter Helper
// ======================================================

file static class MongoDbHelper
{
    private static readonly string ConnectionString = "mongodb://localhost:27017";
    private static readonly string DatabaseName = "GestionAtelier";
    private static readonly string CountersCollection = "counters";

    public static IMongoClient GetClient() => new MongoClient(ConnectionString);

    public static IMongoDatabase GetDatabase() => GetClient().GetDatabase(DatabaseName);

    public static long GetNextFileNumber()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "file_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB counter error: {ex.Message}");
            return 1;
        }
    }

    public static long GetNextUserId()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "user_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB user counter error: {ex.Message}");
            return 1;
        }
    }

    public static IMongoCollection<BsonDocument> GetUsersCollection()
        => GetDatabase().GetCollection<BsonDocument>("users");

    public static IMongoCollection<BsonDocument> GetDeliveriesCollection()
        => GetDatabase().GetCollection<BsonDocument>("deliveries");

    public static IMongoCollection<BsonDocument> GetFabricationsCollection()
        => GetDatabase().GetCollection<BsonDocument>("fabrications");

    public static IMongoCollection<BsonDocument> GetAssignmentsCollection()
        => GetDatabase().GetCollection<BsonDocument>("assignments");

    public static IMongoCollection<BsonDocument> GetSettingsCollection()
        => GetDatabase().GetCollection<BsonDocument>("settings");

    public static IMongoCollection<BsonDocument> GetLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("logs");

    public static IMongoCollection<T> GetCollection<T>(string name)
        => GetDatabase().GetCollection<T>(name);

    public static T? GetSettings<T>(string settingsId) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            var doc = col.Find(filter).FirstOrDefault();
            if (doc == null) return null;
            doc.Remove("_id");
            return JsonSerializer.Deserialize<T>(doc.ToJson());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetSettings({settingsId}) error: {ex.Message}");
            return null;
        }
    }

    public static void UpsertSettings<T>(string settingsId, T value) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var json = JsonSerializer.Serialize(value);
            var doc = BsonDocument.Parse(json);
            doc["_id"] = settingsId;
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] UpsertSettings({settingsId}) error: {ex.Message}");
        }
    }

    public static IMongoCollection<BsonDocument> GetActivityLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("activity_logs");

    public static IMongoCollection<BsonDocument> GetPrintEnginesCollection()
        => GetDatabase().GetCollection<BsonDocument>("print_engines");

    public static void InsertActivityLog(ActivityLogEntry entry)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"] = entry.Timestamp,
                ["userLogin"] = entry.UserLogin,
                ["userName"]  = entry.UserName,
                ["action"]    = entry.Action,
                ["details"]   = entry.Details
            };
            col.InsertOne(doc);
        }
        catch (Exception ex) { Console.WriteLine($"[WARN] Activity log failed: {ex.Message}"); }
    }

    public static List<object> GetActivityLogs(string? dateFilter, int limit = 500)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp = d.Contains("timestamp") ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                userLogin = d.Contains("userLogin") ? d["userLogin"].AsString : "",
                userName  = d.Contains("userName")  ? d["userName"].AsString  : "",
                action    = d.Contains("action")    ? d["action"].AsString    : "",
                details   = d.Contains("details")   ? d["details"].AsString   : ""
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetActivityLogs error: {ex.Message}");
            return new();
        }
    }

    public static List<string> GetPrintEngines()
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var docs = col.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("name"))
                .ToList();
            return docs.Select(d => d.Contains("name") ? d["name"].AsString : "").Where(s => s.Length > 0).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetPrintEngines error: {ex.Message}");
            return new();
        }
    }

    public static List<object> GetPrintEnginesWithIp()
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var docs = col.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("name"))
                .ToList();
            return docs
                .Where(d => d.Contains("name") && !string.IsNullOrWhiteSpace(d["name"].AsString))
                .Select(d => (object)new {
                    name = d["name"].AsString,
                    ip   = d.Contains("ip") ? d["ip"].AsString : ""
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetPrintEnginesWithIp error: {ex.Message}");
            return new();
        }
    }

    public static void AddPrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var existing = col.Find(filter).FirstOrDefault();
            if (existing == null)
            {
                col.InsertOne(new BsonDocument { ["name"] = name, ["ip"] = "" });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] AddPrintEngine error: {ex.Message}");
        }
    }

    public static void AddPrintEngineWithIp(string name, string ip)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var update = Builders<BsonDocument>.Update
                .Set("name", name)
                .Set("ip", ip ?? "");
            col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] AddPrintEngineWithIp error: {ex.Message}");
        }
    }

    public static void RemovePrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            col.DeleteOne(Builders<BsonDocument>.Filter.Eq("name", name));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] RemovePrintEngine error: {ex.Message}");
        }
    }

    public static void InsertLog(LogEntry entry)
    {
        try
        {
            var col = GetLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"]  = entry.Timestamp,
                ["method"]     = entry.Method,
                ["path"]       = entry.Path,
                ["statusCode"] = entry.StatusCode
            };
            col.InsertOne(doc);
        }
        catch { /* ignore log errors */ }
    }

    public static List<object> GetRecentLogs(string? dateFilter, int limit = 200)
    {
        try
        {
            var col = GetLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp  = d.Contains("timestamp")  ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                method     = d.Contains("method")     ? d["method"].AsString : "",
                path       = d.Contains("path")       ? d["path"].AsString : "",
                statusCode = d.Contains("statusCode") ? d["statusCode"].AsInt32 : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetRecentLogs error: {ex.Message}");
            return new();
        }
    }
}


// ======================================================
// TYPES
// ======================================================

file class UserItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
    
    [JsonPropertyName("profile")]
    public int Profile { get; set; } = 1;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

file record DeliveryItem
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; init; } = default!;
    public DateTime Date { get; init; }
    public string Time { get; set; } = "09:00";
}

file record AssignmentItem
{
    public string FullPath     { get; init; } = default!;
    public string FileName     { get; init; } = "";
    public string OperatorId   { get; init; } = default!;
    public string OperatorName { get; init; } = default!;
    public DateTime AssignedAt { get; init; }
    public string AssignedBy   { get; init; } = "";
}

file record FabricationHistory
{
    public DateTime Date { get; init; }
    public string User { get; init; } = "David";
    public string Action { get; init; } = "";
}

file record FabricationSheet
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; set; } = default!;
    public string? Machine { get; init; }
    public string? MoteurImpression { get; init; }
    public string? Operateur { get; init; }
    public int? Quantite { get; init; }
    public string? TypeTravail { get; init; }
    public string? Format { get; init; }
    public string? Papier { get; init; }
    public string? RectoVerso { get; init; }
    public string? Encres { get; init; }
    public string? Client { get; init; }
    public string? NumeroAffaire { get; init; }
    public string? NumeroDossier { get; init; }
    public string? Notes { get; init; }
    public DateTime? Delai { get; init; }
    public string? Media1 { get; init; }
    public string? Media2 { get; init; }
    public string? Media3 { get; init; }
    public string? Media4 { get; init; }
    public string? TypeDocument { get; init; }
    public int? NombreFeuilles { get; init; }
    public List<string>? Faconnage { get; init; }
    public string? Livraison { get; init; }
    public List<FabricationHistory> History { get; init; } = new();
}

file class FabricationInput
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Machine { get; set; }
    public string? MoteurImpression { get; set; }
    public string? Operateur { get; set; }
    public int? Quantite { get; set; }
    public string? TypeTravail { get; set; }
    public string? Format { get; set; }
    public string? Papier { get; set; }
    public string? RectoVerso { get; set; }
    public string? Encres { get; set; }
    public string? Client { get; set; }
    public string? NumeroAffaire { get; set; }
    public string? NumeroDossier { get; set; }
    public string? Notes { get; set; }
    public DateTime? Delai { get; set; }
    public string? Media1 { get; set; }
    public string? Media2 { get; set; }
    public string? Media3 { get; set; }
    public string? Media4 { get; set; }
    public string? TypeDocument { get; set; }
    public int? NombreFeuilles { get; set; }
    public List<string>? Faconnage { get; set; }
    public string? Livraison { get; set; }
}

// ======================================================
// Settings / Log types
// ======================================================

file class ScheduleSettings
{
    [JsonPropertyName("workStart")]
    public string WorkStart { get; set; } = "08:00";

    [JsonPropertyName("workEnd")]
    public string WorkEnd { get; set; } = "18:00";

    [JsonPropertyName("holidays")]
    public List<string> Holidays { get; set; } = new();
}

file class PathsSettings
{
    [JsonPropertyName("hotfoldersRoot")]
    public string HotfoldersRoot { get; set; } = @"C:\Flux";

    [JsonPropertyName("recycleBinPath")]
    public string RecycleBinPath { get; set; } = "";

    [JsonPropertyName("acrobatExePath")]
    public string AcrobatExePath { get; set; } = @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";
}

file class FabricationImportsSettings
{
    [JsonPropertyName("media1Path")]
    public string Media1Path { get; set; } = "";

    [JsonPropertyName("media2Path")]
    public string Media2Path { get; set; } = "";

    [JsonPropertyName("media3Path")]
    public string Media3Path { get; set; } = "";

    [JsonPropertyName("media4Path")]
    public string Media4Path { get; set; } = "";

    [JsonPropertyName("typeDocumentPath")]
    public string TypeDocumentPath { get; set; } = "";
}

file class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
}

file class ActivityLogEntry
{
    public DateTime Timestamp { get; set; }
    public string UserLogin { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

file class IntegrationsSettings
{
    public const string DefaultPrismaPrepareOutputPath = @"C:\FluxAtelier\Base\Sortie";

    [JsonPropertyName("preparePath")]
    public string PreparePath { get; set; } = "";

    [JsonPropertyName("fieryPath")]
    public string FieryPath { get; set; } = "";

    [JsonPropertyName("tempCopyPath")]
    public string TempCopyPath { get; set; } = "";

    [JsonPropertyName("prismaPrepareExePath")]
    public string PrismaPrepareExePath { get; set; } = "";

    [JsonPropertyName("prismaPrepareOutputPath")]
    public string PrismaPrepareOutputPath { get; set; } = DefaultPrismaPrepareOutputPath;
}

// ======================================================
// BAT SERIALIZATION STATE — Prevent concurrent BAT processing
// ======================================================
// In-memory state + 30-second timeout safety
static class BatSerializationState
{
    private static readonly object _lock = new();
    private static bool _inProgress = false;
    private static string? _currentFileName = null;
    private static DateTime _startedAt = DateTime.MinValue;
    private const int TimeoutSeconds = 180; // PrismaPrepare can take 60-120s; 180s provides a safe margin

    // Current workflow step (updated at key moments in HandleEpreuve / copy-for-bat)
    private static string _currentStep = "";

    // Correlation ID for the current BAT (set in TryAcquire, cleared in Release)
    private static string? _correlationId = null;

    // Last completed BAT info (updated by HandleEpreuve on success)
    private static string? _lastCompletedFileName = null;
    private static DateTime? _lastCompletedAt = null;
    private static string? _lastPrismaLog = null;

    public static (bool inProgress, string? currentFileName, DateTime startedAt, string currentStep, string? correlationId) Get()
    {
        lock (_lock)
        {
            // Auto-reset if timed out
            if (_inProgress && (DateTime.UtcNow - _startedAt).TotalSeconds >= TimeoutSeconds)
            {
                Console.WriteLine($"[BAT][WARN] BAT serialization timeout — auto-reset (was: {_currentFileName})");
                _inProgress = false;
                _currentFileName = null;
                _startedAt = DateTime.MinValue;
                _currentStep = "";
                _correlationId = null;
            }
            return (_inProgress, _currentFileName, _startedAt, _currentStep, _correlationId);
        }
    }

    public static void SetStep(string step)
    {
        lock (_lock) { _currentStep = step; }
    }

    public static (string? lastCompletedFileName, DateTime? lastCompletedAt, string? lastPrismaLog) GetLastCompleted()
    {
        lock (_lock)
        {
            return (_lastCompletedFileName, _lastCompletedAt, _lastPrismaLog);
        }
    }

    public static void SetLastCompleted(string fileName, string prismaLog)
    {
        lock (_lock)
        {
            _lastCompletedFileName = fileName;
            _lastCompletedAt = DateTime.UtcNow;
            _lastPrismaLog = prismaLog;
        }
    }

    public static bool TryAcquire(string fileName, string correlationId = "")
    {
        lock (_lock)
        {
            // Auto-reset if timed out
            if (_inProgress && (DateTime.UtcNow - _startedAt).TotalSeconds >= TimeoutSeconds)
            {
                Console.WriteLine($"[BAT][WARN] BAT serialization timeout — auto-reset (was: {_currentFileName})");
                _inProgress = false;
                _currentFileName = null;
                _startedAt = DateTime.MinValue;
                _currentStep = "";
                _correlationId = null;
            }
            if (_inProgress) return false;
            _inProgress = true;
            _currentFileName = fileName;
            _startedAt = DateTime.UtcNow;
            _currentStep = "";
            _correlationId = string.IsNullOrEmpty(correlationId) ? null : correlationId;
            return true;
        }
    }

    public static void Release()
    {
        lock (_lock)
        {
            _inProgress = false;
            _currentFileName = null;
            _startedAt = DateTime.MinValue;
            _currentStep = "";
            _correlationId = null;
        }
    }
}

// ======================================================
// BackendUtils
// ======================================================

file static class BackendUtils
{
    public static readonly System.Text.RegularExpressions.Regex SafeNameRegex =
        new(@"[^\w\-]", System.Text.RegularExpressions.RegexOptions.Compiled);

    public const int FileSystemSettleDelayMs = 500;

    public static readonly IReadOnlyDictionary<string, string> StageFolderMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rapport"] = "Rapport",
            ["Prêt pour impression"] = "PDF_Impression",
            ["BAT"] = "BAT",
            ["PrismaPrepare"] = "PrismaPrepare",
            ["Fin de production"] = "PDF_Imprime"
        };
    public static string HotfoldersRoot()
    {
        var env = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        return @"C:\Flux";
    }

    public static string[] Hotfolders()
    {
        try
        {
            var root = HotfoldersRoot();
            if (!Directory.Exists(root)) return Array.Empty<string>();

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    // ---- Users ----

    public static List<UserItem> LoadUsers()
    {
        try
        {
            var col = MongoDbHelper.GetUsersCollection();
            return col.Find(new BsonDocument()).ToList()
                .Select(d => new UserItem
                {
                    Id       = d.Contains("id")       ? d["id"].AsString       : "",
                    Login    = d.Contains("login")    ? d["login"].AsString    : "",
                    Password = d.Contains("password") ? d["password"].AsString : "",
                    Profile  = d.Contains("profile")  ? d["profile"].AsInt32   : 1,
                    Name     = d.Contains("name")     ? d["name"].AsString     : ""
                }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadUsers MongoDB error: {ex.Message}");
            return new();
        }
    }

    public static void InsertUser(UserItem user)
    {
        var col = MongoDbHelper.GetUsersCollection();
        var doc = new BsonDocument
        {
            ["id"]       = user.Id,
            ["login"]    = user.Login,
            ["password"] = user.Password,
            ["profile"]  = user.Profile,
            ["name"]     = user.Name
        };
        col.InsertOne(doc);
    }

    public static bool DeleteUser(string userId)
    {
        var col = MongoDbHelper.GetUsersCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("id", userId);
        var result = col.DeleteOne(filter);
        return result.DeletedCount > 0;
    }

    // ---- Deliveries ----

    public static Dictionary<string, DeliveryItem> LoadDeliveries()
    {
        try
        {
            var col = MongoDbHelper.GetDeliveriesCollection();
            var result = new Dictionary<string, DeliveryItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in col.Find(new BsonDocument()).ToList())
            {
                var fullPath = d.Contains("fullPath") ? d["fullPath"].AsString : "";
                string fileName;
                if (d.Contains("fileName") && !string.IsNullOrEmpty(d["fileName"].AsString))
                    fileName = d["fileName"].AsString;
                else
                    fileName = string.IsNullOrEmpty(fullPath) ? "" : Path.GetFileName(fullPath);
                // Key by fileName (universal key resilient to path changes)
                var key = !string.IsNullOrEmpty(fileName) ? fileName : fullPath;
                if (!string.IsNullOrEmpty(key))
                {
                    result[key] = new DeliveryItem
                    {
                        FullPath = fullPath,
                        FileName = fileName,
                        Date     = d.Contains("date") ? d["date"].ToLocalTime() : DateTime.MinValue,
                        Time     = d.Contains("time") ? d["time"].AsString : "09:00"
                    };
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadDeliveries MongoDB error: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void UpsertDelivery(DeliveryItem item)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var fileNameKey = !string.IsNullOrEmpty(item.FileName) ? item.FileName : Path.GetFileName(item.FullPath);
        var doc = new BsonDocument
        {
            ["fullPath"] = item.FullPath,
            ["fileName"] = fileNameKey,
            ["date"]     = item.Date,
            ["time"]     = item.Time
        };

        // Build a broad filter covering both new records (by fileName) and old records (by fullPath).
        // This prevents duplicate deliveries when old records lack the fileName field.
        var filterParts = new List<FilterDefinition<BsonDocument>>();
        if (!string.IsNullOrEmpty(fileNameKey))
            filterParts.Add(Builders<BsonDocument>.Filter.Eq("fileName", fileNameKey));
        if (!string.IsNullOrEmpty(item.FullPath))
            filterParts.Add(Builders<BsonDocument>.Filter.Eq("fullPath", item.FullPath));
        var broadFilter = filterParts.Count > 1
            ? Builders<BsonDocument>.Filter.Or(filterParts)
            : filterParts.Count == 1 ? filterParts[0]
            : Builders<BsonDocument>.Filter.Eq("fileName", fileNameKey);

        // Delete all matching records (removes stale duplicates), then insert fresh
        col.DeleteMany(broadFilter);
        col.InsertOne(doc);
    }

    public static bool DeleteDelivery(string fullPath)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        // Try deleting by fileName first, then by fullPath
        var fileName = Path.GetFileName(fullPath);
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("fileName", fileName),
            Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)
        );
        var result = col.DeleteMany(filter);
        return result.DeletedCount > 0;
    }

    public static bool DeleteDeliveryByFileName(string fileName)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
        var result = col.DeleteMany(filter);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// Deletes deliveries matching by fileName OR by fullPath.
    /// Handles both new records (with fileName field) and old records (fullPath-only).
    /// </summary>
    public static bool DeleteDeliveryByFileNameOrPath(string fileName)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        // Match by fileName field, or by fullPath that equals just the fileName (old storage format)
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("fileName", fileName),
            Builders<BsonDocument>.Filter.Eq("fullPath", fileName)
        );
        var result = col.DeleteMany(filter);
        if (result.DeletedCount > 0) return true;

        // Fallback: use server-side regex to match old records whose fullPath ends with /fileName or \fileName
        // Handles old records stored with a full Windows path but no fileName field
        var escapedName = System.Text.RegularExpressions.Regex.Escape(fileName);
        var pattern = new BsonRegularExpression($"(^|[/\\\\\\\\]){escapedName}$", "i");
        var fallbackFilter = Builders<BsonDocument>.Filter.Regex("fullPath", pattern);
        var fallbackResult = col.DeleteMany(fallbackFilter);
        return fallbackResult.DeletedCount > 0;
    }

    public static void DeleteDeliveries(List<string> fullPaths)
    {
        if (fullPaths.Count == 0) return;
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.In("fullPath", fullPaths);
        col.DeleteMany(filter);
    }

    public static void UpdateDeliveryPath(string oldPath, string newPath)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", oldPath);
        var update = Builders<BsonDocument>.Update
            .Set("fullPath", newPath)
            .Set("fileName", Path.GetFileName(newPath));
        col.UpdateMany(filter, update);
    }

    public static async Task EnsureProductionFolderAsync(string movedFilePath)
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var fileName = Path.GetFileName(movedFilePath);

        // Check if already exists (by originalFilePath or currentFilePath)
        var existing = col.Find(
            Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("originalFilePath", movedFilePath),
                Builders<BsonDocument>.Filter.Eq("currentFilePath", movedFilePath)
            )).FirstOrDefault();
        if (existing != null) return;

        // Look up the fabrication sheet to get numeroDossier (prefer fileName-based lookup)
        var fabSheet = BackendUtils.FindFabricationByName(fileName) ?? BackendUtils.FindFabrication(movedFilePath);
        var numeroDossier = fabSheet?.NumeroDossier;

        // Build folder name: use numeroDossier if available, else auto-increment NNN
        var safeName = SafeNameRegex.Replace(Path.GetFileNameWithoutExtension(fileName), "_");
        string folderName;
        int? number = null;
        if (!string.IsNullOrWhiteSpace(numeroDossier))
        {
            // Use the dossier number from the fabrication sheet
            folderName = $"{numeroDossier}_{safeName}";
        }
        else
        {
            // Fall back to auto-increment counter
            var count = (int)col.CountDocuments(new BsonDocument()) + 1;
            number = count;
            folderName = $"{number:D3}_{safeName}";
        }

        var root = HotfoldersRoot();
        var dossiersRoot = Path.Combine(root, "DossiersProduction");
        Directory.CreateDirectory(dossiersRoot);
        var folderPath = Path.Combine(dossiersRoot, folderName);
        Directory.CreateDirectory(folderPath);

        // Copy original file to "Original" subfolder
        var originalDir = Path.Combine(folderPath, "Original");
        Directory.CreateDirectory(originalDir);
        if (File.Exists(movedFilePath))
            File.Copy(movedFilePath, Path.Combine(originalDir, fileName), overwrite: true);

        var doc = new BsonDocument
        {
            ["number"] = number.HasValue ? (BsonValue)number.Value : BsonNull.Value,
            ["numeroDossier"] = string.IsNullOrWhiteSpace(numeroDossier) ? BsonNull.Value : (BsonValue)numeroDossier,
            ["fileName"] = fileName,
            ["originalFilePath"] = movedFilePath,
            ["currentFilePath"] = movedFilePath,
            ["folderPath"] = folderPath,
            ["createdAt"] = DateTime.UtcNow,
            ["currentStage"] = "Début de production",
            ["fabricationSheet"] = new BsonDocument(),
            ["files"] = new BsonArray
            {
                new BsonDocument
                {
                    ["stage"] = "Original",
                    ["fileName"] = fileName,
                    ["addedAt"] = DateTime.UtcNow
                }
            }
        };
        await col.InsertOneAsync(doc);
    }

    public static async Task CopyToProductionFolderStageAsync(string filePath, string stage)
    {
        if (!StageFolderMap.TryGetValue(stage.Trim(), out var subFolder)) return;

        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var fileName = Path.GetFileName(filePath);

        // Find production folder by matching fileName
        var filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
        var doc = col.Find(filter).SortByDescending(x => x["createdAt"]).FirstOrDefault();
        if (doc == null) return;

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        if (string.IsNullOrEmpty(folderPath)) return;

        var stageDir = Path.Combine(folderPath, subFolder);
        Directory.CreateDirectory(stageDir);

        if (File.Exists(filePath))
            File.Copy(filePath, Path.Combine(stageDir, fileName), overwrite: true);

        var fileEntry = new BsonDocument
        {
            ["stage"] = subFolder,
            ["fileName"] = fileName,
            ["addedAt"] = DateTime.UtcNow
        };
        var update = Builders<BsonDocument>.Update
            .Push("files", fileEntry)
            .Set("currentStage", stage)
            .Set("currentFilePath", filePath);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), update);
    }

    // ---- Fabrications ----

    public static Dictionary<string, FabricationSheet> LoadFabrications()
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var result = new Dictionary<string, FabricationSheet>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in col.Find(new BsonDocument()).ToList())
            {
                var sheet = BsonDocToFabricationSheet(d);
                if (sheet != null)
                    result[sheet.FullPath] = sheet;
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadFabrications MongoDB error: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static FabricationSheet? FindFabrication(string fullPath)
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
            var doc = col.Find(filter).FirstOrDefault();
            if (doc != null) return BsonDocToFabricationSheet(doc);

            // Fallback: look up by fileName (resilient to path changes by Acrobat Pro)
            // Normalize to lowercase (fabrication records store fileName as lowercase via fnKey)
            var fileName = Path.GetFileName(fullPath)?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(fileName))
            {
                filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
                doc = col.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
                if (doc != null) return BsonDocToFabricationSheet(doc);
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindFabrication MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static FabricationSheet? FindFabricationByName(string fileName)
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            // Normalize to lowercase (fabrication records store fileName as lowercase via fnKey)
            var lowerFileName = (fileName ?? "").ToLowerInvariant();
            var filter = Builders<BsonDocument>.Filter.Eq("fileName", lowerFileName);
            // When multiple records share a fileName (e.g. file moved between folders),
            // take the most recently inserted one (newest ObjectId = most recent).
            var doc = col.Find(filter).SortByDescending(x => x["_id"]).FirstOrDefault();
            return doc == null ? null : BsonDocToFabricationSheet(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindFabricationByName MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static void UpsertFabrication(FabricationSheet sheet)
    {
        var col = MongoDbHelper.GetFabricationsCollection();

        // Always store fileName in lowercase for consistent lookups
        if (!string.IsNullOrEmpty(sheet.FileName))
            sheet.FileName = sheet.FileName.ToLowerInvariant();

        // Primary lookup by fileName (stable key even when file moves between folders).
        // Fall back to fullPath for legacy records that only have fullPath stored.
        BsonDocument? existing = null;
        if (!string.IsNullOrEmpty(sheet.FileName))
        {
            // Case-insensitive search to handle legacy records stored with different casing
            existing = col.Find(Builders<BsonDocument>.Filter.Regex("fileName",
                new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(sheet.FileName) + "$", "i")))
                          .SortByDescending(x => x["_id"]).FirstOrDefault();
        }
        if (existing == null && !string.IsNullOrEmpty(sheet.FullPath))
            existing = col.Find(Builders<BsonDocument>.Filter.Eq("fullPath", sheet.FullPath)).FirstOrDefault();

        var historyArray = new BsonArray(sheet.History.Select(h => new BsonDocument
        {
            ["date"]   = h.Date,
            ["user"]   = h.User,
            ["action"] = h.Action
        }));
        var doc = new BsonDocument
        {
            ["fullPath"]          = sheet.FullPath,
            ["fileName"]          = sheet.FileName,
            ["machine"]           = sheet.Machine           == null ? BsonNull.Value : (BsonValue)sheet.Machine,
            ["moteurImpression"]  = sheet.MoteurImpression  == null ? BsonNull.Value : (BsonValue)sheet.MoteurImpression,
            ["operateur"]         = sheet.Operateur         == null ? BsonNull.Value : (BsonValue)sheet.Operateur,
            ["quantite"]          = sheet.Quantite          == null ? BsonNull.Value : (BsonValue)(int)sheet.Quantite,
            ["typeTravail"]       = sheet.TypeTravail       == null ? BsonNull.Value : (BsonValue)sheet.TypeTravail,
            ["format"]            = sheet.Format            == null ? BsonNull.Value : (BsonValue)sheet.Format,
            ["papier"]            = sheet.Papier            == null ? BsonNull.Value : (BsonValue)sheet.Papier,
            ["rectoVerso"]        = sheet.RectoVerso        == null ? BsonNull.Value : (BsonValue)sheet.RectoVerso,
            ["encres"]            = sheet.Encres            == null ? BsonNull.Value : (BsonValue)sheet.Encres,
            ["client"]            = sheet.Client            == null ? BsonNull.Value : (BsonValue)sheet.Client,
            ["numeroAffaire"]     = sheet.NumeroAffaire     == null ? BsonNull.Value : (BsonValue)sheet.NumeroAffaire,
            ["numeroDossier"]     = sheet.NumeroDossier     == null ? BsonNull.Value : (BsonValue)sheet.NumeroDossier,
            ["notes"]             = sheet.Notes             == null ? BsonNull.Value : (BsonValue)sheet.Notes,
            ["delai"]             = sheet.Delai             == null ? BsonNull.Value : (BsonValue)sheet.Delai.Value,
            ["media1"]            = sheet.Media1            == null ? BsonNull.Value : (BsonValue)sheet.Media1,
            ["media2"]            = sheet.Media2            == null ? BsonNull.Value : (BsonValue)sheet.Media2,
            ["media3"]            = sheet.Media3            == null ? BsonNull.Value : (BsonValue)sheet.Media3,
            ["media4"]            = sheet.Media4            == null ? BsonNull.Value : (BsonValue)sheet.Media4,
            ["typeDocument"]      = sheet.TypeDocument      == null ? BsonNull.Value : (BsonValue)sheet.TypeDocument,
            ["nombreFeuilles"]    = sheet.NombreFeuilles    == null ? BsonNull.Value : (BsonValue)(int)sheet.NombreFeuilles,
            ["faconnage"]         = sheet.Faconnage == null
                                        ? BsonNull.Value
                                        : (BsonValue)new BsonArray(sheet.Faconnage),
            ["livraison"]         = sheet.Livraison         == null ? BsonNull.Value : (BsonValue)sheet.Livraison,
            ["history"]           = historyArray
        };
        if (existing != null)
        {
            // Preserve the locked field if it was set on the existing document
            if (existing.Contains("locked") && existing["locked"] != BsonNull.Value
                && existing["locked"].BsonType == BsonType.Boolean)
                doc["locked"] = existing["locked"];
            col.ReplaceOne(Builders<BsonDocument>.Filter.Eq("_id", existing["_id"]), doc);
        }
        else
            col.InsertOne(doc);
    }

    public static FabricationSheet? BsonDocToFabricationSheet(BsonDocument d)
    {
        var fullPath = d.Contains("fullPath") && d["fullPath"] != BsonNull.Value ? d["fullPath"].AsString : "";
        var fileName = d.Contains("fileName") && d["fileName"] != BsonNull.Value ? d["fileName"].AsString : "";
        // Allow records that only have fileName (no fullPath yet)
        if (string.IsNullOrEmpty(fullPath) && string.IsNullOrEmpty(fileName)) return null;

        var history = new List<FabricationHistory>();
        if (d.Contains("history") && d["history"].IsBsonArray)
        {
            foreach (var item in d["history"].AsBsonArray)
            {
                var h = item.AsBsonDocument;
                history.Add(new FabricationHistory
                {
                    Date   = h.Contains("date")   ? h["date"].ToUniversalTime() : DateTime.MinValue,
                    User   = h.Contains("user")   && h["user"]   != BsonNull.Value ? h["user"].AsString : "David",
                    Action = h.Contains("action") && h["action"] != BsonNull.Value ? h["action"].AsString : ""
                });
            }
        }

        var machineVal = GetNullableString(d, "machine");
        return new FabricationSheet
        {
            FullPath         = fullPath,
            FileName         = !string.IsNullOrEmpty(fileName) ? fileName : (Path.GetFileName(fullPath) ?? ""),
            Machine          = machineVal,
            MoteurImpression = GetNullableString(d, "moteurImpression") ?? machineVal,
            Operateur        = GetNullableString(d, "operateur"),
            Quantite         = d.Contains("quantite")         && d["quantite"]         != BsonNull.Value ? (int?)d["quantite"].AsInt32   : null,
            TypeTravail      = GetNullableString(d, "typeTravail"),
            Format           = GetNullableString(d, "format"),
            Papier           = GetNullableString(d, "papier"),
            RectoVerso       = GetNullableString(d, "rectoVerso"),
            Encres           = GetNullableString(d, "encres"),
            Client           = GetNullableString(d, "client"),
            NumeroAffaire    = GetNullableString(d, "numeroAffaire"),
            NumeroDossier    = GetNullableString(d, "numeroDossier"),
            Notes            = GetNullableString(d, "notes"),
            Delai            = d.Contains("delai")            && d["delai"]            != BsonNull.Value ? (DateTime?)d["delai"].ToLocalTime() : null,
            Media1           = GetNullableString(d, "media1"),
            Media2           = GetNullableString(d, "media2"),
            Media3           = GetNullableString(d, "media3"),
            Media4           = GetNullableString(d, "media4"),
            TypeDocument     = GetNullableString(d, "typeDocument"),
            NombreFeuilles   = d.Contains("nombreFeuilles")   && d["nombreFeuilles"]   != BsonNull.Value ? (int?)d["nombreFeuilles"].AsInt32 : null,
            Faconnage        = d.Contains("faconnage") && d["faconnage"] != BsonNull.Value
                               ? (d["faconnage"].IsBsonArray
                                  ? d["faconnage"].AsBsonArray.Select(v => v.AsString).ToList()
                                  : new List<string> { d["faconnage"].AsString })
                               : null,
            Livraison        = GetNullableString(d, "livraison"),
            History          = history
        };
    }

    private static string? GetNullableString(BsonDocument d, string key)
        => d.Contains(key) && d[key] != BsonNull.Value ? d[key].AsString : null;

    // ---- Assignments ----

    public static AssignmentItem? FindAssignment(string fullPath)
    {
        try
        {
            var col = MongoDbHelper.GetAssignmentsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
            var doc = col.Find(filter).FirstOrDefault();
            return doc == null ? null : BsonDocToAssignment(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindAssignment MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static List<AssignmentItem> LoadAssignments()
    {
        try
        {
            var col = MongoDbHelper.GetAssignmentsCollection();
            return col.Find(new BsonDocument()).ToList()
                .Select(d => BsonDocToAssignment(d))
                .Where(a => a != null)
                .Select(a => a!)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadAssignments MongoDB error: {ex.Message}");
            return new();
        }
    }

    public static void UpsertAssignment(AssignmentItem item)
    {
        var col = MongoDbHelper.GetAssignmentsCollection();
        var fileNameKey = !string.IsNullOrEmpty(item.FileName) ? item.FileName : Path.GetFileName(item.FullPath);
        // Upsert by fileName (universal key), fall back to fullPath for old records
        var filter = !string.IsNullOrEmpty(fileNameKey)
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("fileName", fileNameKey),
                Builders<BsonDocument>.Filter.Eq("fullPath", item.FullPath))
            : Builders<BsonDocument>.Filter.Eq("fullPath", item.FullPath);
        var doc = new BsonDocument
        {
            ["fullPath"]     = item.FullPath,
            ["fileName"]     = fileNameKey,
            ["operatorId"]   = item.OperatorId,
            ["operatorName"] = item.OperatorName,
            ["assignedAt"]   = item.AssignedAt,
            ["assignedBy"]   = item.AssignedBy
        };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    private static AssignmentItem? BsonDocToAssignment(BsonDocument d)
    {
        var fullPath = d.Contains("fullPath") ? d["fullPath"].AsString : "";
        var fileName = d.Contains("fileName") ? d["fileName"].AsString : "";
        if (string.IsNullOrEmpty(fullPath) && string.IsNullOrEmpty(fileName)) return null;
        if (string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(fullPath))
            fileName = Path.GetFileName(fullPath);
        return new AssignmentItem
        {
            FullPath     = fullPath,
            FileName     = fileName,
            OperatorId   = d.Contains("operatorId")   ? d["operatorId"].AsString   : "",
            OperatorName = d.Contains("operatorName") ? d["operatorName"].AsString : "",
            AssignedAt   = d.Contains("assignedAt")   ? d["assignedAt"].ToUniversalTime() : DateTime.MinValue,
            AssignedBy   = d.Contains("assignedBy")   ? d["assignedBy"].AsString   : ""
        };
    }

    public static string BuildCommandTemplate(string template, string filePath, string typeWork = "", int quantity = 1)
    {
        return template
            .Replace("{filePath}", filePath)
            .Replace("{typeWork}", typeWork)
            .Replace("{type}", typeWork)
            .Replace("{quantity}", quantity.ToString())
            .Replace("{qty}", quantity.ToString());
    }

    public static (bool ok, string? error) MoveFileToDestFolder(string filePath, string destFolder)
    {
        try
        {
            var root = HotfoldersRoot();
            var destDir = Path.Combine(root, destFolder);
            Directory.CreateDirectory(destDir);
            var dst = Path.Combine(destDir, Path.GetFileName(filePath));
            File.Move(filePath, dst, true);
            UpdateDeliveryPath(filePath, dst);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}

// ======================================================
// PdfUtils
// ======================================================

file static class PdfUtils
{
    public static Document CreateFabricationPdf(FabricationSheet s)
    {
        // Determine creation date from history (first entry) or now
        var historyOrdered = s.History.OrderBy(h => h.Date).ToList();
        var creationDate = historyOrdered.FirstOrDefault()?.Date ?? DateTime.Now;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(hdr =>
                {
                    hdr.Item().AlignCenter().Text("Fiche de Fabrication").FontSize(24).SemiBold();
                    if (!string.IsNullOrWhiteSpace(s.NumeroDossier))
                        hdr.Item().AlignCenter().Text(s.NumeroDossier).FontSize(16).SemiBold();
                    hdr.Item().PaddingVertical(6).LineHorizontal(2).LineColor("#1a1a2e");
                });

                page.Content().Column(col =>
                {
                    // ── Informations générales ──────────────────────────
                    col.Item().PaddingBottom(8).Text("Informations générales").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val, bool bold = false)
                        {
                            table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                            var v = table.Cell().PaddingBottom(4).Text(val ?? "—");
                            if (bold) v.SemiBold();
                        }
                        Row("Client", s.Client);
                        Row("Date de création", creationDate.ToString("dd/MM/yyyy"));
                        Row("N° de dossier", s.NumeroDossier, bold: true);
                        Row("Date de livraison", s.Delai.HasValue ? s.Delai.Value.ToString("dd/MM/yyyy") : "—");
                        Row("Nom du fichier", s.FileName);
                        Row("Opérateur", s.Operateur);
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Données de production ──────────────────────────
                    col.Item().PaddingBottom(8).Text("Données de production").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val)
                        {
                            table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                            table.Cell().PaddingBottom(4).Text(val ?? "—");
                        }
                        Row("Moteur d'impression", s.MoteurImpression ?? s.Machine);
                        Row("Quantité", s.Quantite.HasValue ? s.Quantite.Value.ToString("N0") : "—");
                        Row("Type de travail", s.TypeTravail);
                        Row("Format fini", s.Format);
                        Row("Recto/Verso", s.RectoVerso);
                        Row("Type document", s.TypeDocument);
                        Row("Nombre de feuilles", s.NombreFeuilles.HasValue ? s.NombreFeuilles.Value.ToString() : "—");
                        Row("N° affaire", s.NumeroAffaire);
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Médias ──────────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.Media1) || !string.IsNullOrWhiteSpace(s.Media2) ||
                        !string.IsNullOrWhiteSpace(s.Media3) || !string.IsNullOrWhiteSpace(s.Media4))
                    {
                        col.Item().PaddingBottom(8).Text("Médias / Support").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                            if (!string.IsNullOrWhiteSpace(s.Media1)) Row("Média 1", s.Media1);
                            if (!string.IsNullOrWhiteSpace(s.Media2)) Row("Média 2", s.Media2);
                            if (!string.IsNullOrWhiteSpace(s.Media3)) Row("Média 3", s.Media3);
                            if (!string.IsNullOrWhiteSpace(s.Media4)) Row("Média 4", s.Media4);
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Façonnage ────────────────────────────────────────
                    col.Item().PaddingBottom(4).Text("Façonnage").FontSize(13).SemiBold();
                    if (s.Faconnage != null && s.Faconnage.Count > 0)
                    {
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            foreach (var f in s.Faconnage)
                            {
                                row.AutoItem().Border(1).BorderColor("#d1d5db").Padding(4).Text("✓ " + f).FontSize(10);
                                row.AutoItem().Width(6);
                            }
                        });
                    }
                    else
                    {
                        col.Item().PaddingBottom(8).Text("Aucun façonnage sélectionné").FontColor("#9ca3af").FontSize(10);
                    }

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Observations ────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.Notes))
                    {
                        col.Item().PaddingBottom(4).Text("Observations / Notes").FontSize(13).SemiBold();
                        col.Item().PaddingBottom(8).Border(1).BorderColor("#e5e7eb").Padding(8).Text(s.Notes).FontSize(10);
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Historique ───────────────────────────────────────
                    if (historyOrdered.Count > 0)
                    {
                        col.Item().PaddingBottom(4).Text("Historique").FontSize(13).SemiBold();
                        foreach (var h in historyOrdered)
                            col.Item().Text($"{h.Date:dd/MM/yyyy HH:mm} — {h.User} — {h.Action}").FontSize(9).FontColor("#6b7280");
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span($"Généré le {DateTime.Now:dd/MM/yyyy à HH:mm} — ").FontSize(8).FontColor("#9ca3af");
                    t.Span("Gestion d'Atelier").FontSize(8).FontColor("#9ca3af").SemiBold();
                });
            });
        });
    }
}