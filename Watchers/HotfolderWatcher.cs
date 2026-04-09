using System;
using System.IO;
using System.Threading.Tasks;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Services;
using Microsoft.AspNetCore.Builder;

namespace GestionAtelier.Watchers;

public static class HotfolderWatcherExtensions
{
    public static void UseHotfolderWatcher(this WebApplication app)
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
}
