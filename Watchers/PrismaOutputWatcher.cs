using System;
using System.IO;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Services;
using GestionAtelier.Models;
using Microsoft.AspNetCore.Builder;

namespace GestionAtelier.Watchers;

public static class PrismaOutputWatcherExtensions
{
    public static FileSystemWatcher? UsePrismaOutputWatcher(this WebApplication app)
    {
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

                    // Copy BAT PDF to production folder (sous-dossier "BAT")
                    try
                    {
                        var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                        BsonDocument? pfDoc = pfCol.Find(
                            Builders<BsonDocument>.Filter.Or(
                                Builders<BsonDocument>.Filter.Regex("fileName",
                                    new BsonRegularExpression($"^{System.Text.RegularExpressions.Regex.Escape(sourceFileName)}", "i")),
                                Builders<BsonDocument>.Filter.Eq("fileName", sourceFileName + ".pdf")
                            )
                        ).SortByDescending(x => x["createdAt"]).FirstOrDefault();

                        if (pfDoc != null && pfDoc.Contains("folderPath") && pfDoc["folderPath"].BsonType == BsonType.String)
                        {
                            var prodFolderPath = pfDoc["folderPath"].AsString;
                            if (Directory.Exists(prodFolderPath))
                            {
                                var batSubDir = Path.Combine(prodFolderPath, "BAT");
                                Directory.CreateDirectory(batSubDir);
                                // The BAT file has been moved to the BAT hotfolder; find it there
                                var hotfoldersRoot = BackendUtils.HotfoldersRoot();
                                var batFolderPath = Path.Combine(hotfoldersRoot, "BAT");
                                var movedBatFile = Path.Combine(batFolderPath, batFileName);
                                if (File.Exists(movedBatFile))
                                {
                                    var destCopy = Path.Combine(batSubDir, batFileName);
                                    File.Copy(movedBatFile, destCopy, overwrite: true);
                                    Console.WriteLine($"[BAT_FSW] Copié BAT vers dossier de production : {destCopy}");
                                }
                            }
                        }
                    }
                    catch (Exception exProd) { Console.WriteLine($"[BAT_FSW][WARN] Copy BAT to production folder: {exProd.Message}"); }

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
        return tempCopyWatcher;
    }
}
