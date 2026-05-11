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

                // XML+PDF coupling in Soumission: when an XML arrives, look for matching PDF(s) and apply mapping
                var folder = Path.GetFileName(Path.GetDirectoryName(newPath)) ?? "";
                if (folder.Equals("Soumission", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await Task.Delay(2000); // extra delay to let paired PDF arrive
                        var dir = Path.GetDirectoryName(newPath)!;
                        // Apply XML to ALL PDFs currently in Soumission (same behavior as web upload)
                        var matchingPdfs = Directory.EnumerateFiles(dir)
                            .Where(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        // Load mapping config
                        var intCfg = MongoDbHelper.GetSettings<GestionAtelier.Endpoints.Settings.IntegrationsFullConfig>("integrations_full_config")
                                     ?? new GestionAtelier.Endpoints.Settings.IntegrationsFullConfig();
                        var mapping = intCfg.XmlImport?.Mapping ?? new Dictionary<string, string>();

                        System.Xml.Linq.XDocument? xmlDoc = null;
                        try
                        {
                            using var stream = File.OpenRead(newPath);
                            xmlDoc = GestionAtelier.Services.XmlParserHelper.LoadSafely(stream);
                        }
                        catch { xmlDoc = null; }

                        if (xmlDoc != null)
                        {
                            var fichePrefill = new Dictionary<string, string>();
                            if (GestionAtelier.Services.XmlParserHelper.IsMasterPrint(xmlDoc))
                            {
                                var commandeEl = xmlDoc.Descendants("Commande").FirstOrDefault();
                                if (commandeEl != null)
                                    fichePrefill = GestionAtelier.Services.XmlParserHelper.ParseMasterPrintCommande(commandeEl);
                            }

                            // Apply user mapping
                            var orderEl = GestionAtelier.Services.XmlParserHelper.IsMasterPrint(xmlDoc)
                                ? xmlDoc.Descendants("Commande").FirstOrDefault()
                                : (xmlDoc.Descendants("Order")
                                       .Concat(xmlDoc.Descendants("Commande"))
                                       .Concat(xmlDoc.Descendants("Job"))
                                       .FirstOrDefault() ?? xmlDoc.Root);
                            if (orderEl != null && mapping.Any())
                            {
                                foreach (var kv in mapping)
                                {
                                    try
                                    {
                                        var ficheField = GestionAtelier.Endpoints.Settings.IntegrationsEndpoints.NormalizeFicheFieldKey(kv.Key);
                                        var xmlTag = kv.Value;
                                        if (string.IsNullOrWhiteSpace(xmlTag)) continue;
                                        var el = orderEl.Element(xmlTag) ?? orderEl.Descendants(xmlTag).FirstOrDefault();
                                        if (el != null && !string.IsNullOrWhiteSpace(el.Value))
                                            fichePrefill[ficheField] = el.Value;
                                    }
                                    catch { }
                                }
                            }

                            // Save prefill to fabrication for each matching PDF (upsert)
                            if (fichePrefill.Count > 0)
                            {
                                var fabCol = MongoDbHelper.GetFabricationsCollection();
                                foreach (var pdfPath in matchingPdfs)
                                {
                                    var pdfName = Path.GetFileName(pdfPath).ToLowerInvariant();
                                    var fiche = new BsonDocument();
                                    foreach (var kv in fichePrefill)
                                        fiche[kv.Key] = kv.Value;
                                    fiche["fileName"] = pdfName;
                                    fiche["fullPath"] = pdfPath;
                                    fiche["importedAt"] = DateTime.UtcNow.ToString("O");
                                    fiche["importSource"] = "hotfolder-xml";

                                    var pdfFilter = MongoDB.Driver.Builders<BsonDocument>.Filter.Eq("fileName", pdfName);
                                    fabCol.UpdateOne(pdfFilter, new BsonDocument("$set", fiche),
                                        new MongoDB.Driver.UpdateOptions { IsUpsert = true });

                                    Console.WriteLine($"[FSW] XML coupling: {fileName} → {pdfName} ({fichePrefill.Count} fields)");
                                }

                                // Delete the XML after processing
                                try { File.Delete(newPath); } catch { }
                            }
                        }
                    }
                    catch (Exception exXml) { Console.WriteLine($"[FSW][WARN] XML hotfolder coupling: {exXml.Message}"); }
                    return;
                }

                // PrismaPrepare: rename files back to original name (PrismaPrepare often adds suffixes)
                if (folder.Equals("PrismaPrepare", StringComparison.OrdinalIgnoreCase)
                    && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await Task.Delay(3000); // wait for PrismaPrepare to finish writing
                        if (!File.Exists(newPath)) return;

                        var fabCol2 = MongoDbHelper.GetFabricationsCollection();
                        var baseName = Path.GetFileNameWithoutExtension(fileName);

                        // Find best match: longest origBase that is a prefix of baseName
                        var filter = Builders<BsonDocument>.Filter.And(
                            Builders<BsonDocument>.Filter.Exists("fileName"),
                            Builders<BsonDocument>.Filter.Ne("fileName", BsonNull.Value));
                        string? bestOrigFn = null;
                        int bestLen = 0;
                        using (var cursor = fabCol2.Find(filter).ToCursor())
                        {
                            while (cursor.MoveNext())
                            {
                                foreach (var doc in cursor.Current)
                                {
                                    var origFn = doc["fileName"].AsString;
                                    if (string.IsNullOrEmpty(origFn)) continue;
                                    var origBase = Path.GetFileNameWithoutExtension(origFn);
                                    if (baseName.StartsWith(origBase, StringComparison.OrdinalIgnoreCase)
                                        && baseName.Length > origBase.Length
                                        && origBase.Length > bestLen)
                                    {
                                        bestOrigFn = origFn;
                                        bestLen = origBase.Length;
                                    }
                                }
                            }
                        }

                        if (bestOrigFn != null)
                        {
                            var targetName = bestOrigFn.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? bestOrigFn : bestOrigFn + ".pdf";
                            var originalPath = Path.Combine(Path.GetDirectoryName(newPath)!, targetName);
                            if (!string.Equals(newPath, originalPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(originalPath))
                                {
                                    var backupPath = originalPath + $".bak_{DateTime.Now:yyyyMMddHHmmss}";
                                    File.Move(originalPath, backupPath);
                                    Console.WriteLine($"[FSW] PrismaPrepare: backup existing {Path.GetFileName(originalPath)} → {Path.GetFileName(backupPath)}");
                                }
                                File.Move(newPath, originalPath);
                                Console.WriteLine($"[FSW] PrismaPrepare rename: {fileName} → {Path.GetFileName(originalPath)}");
                            }
                        }
                    }
                    catch (Exception exPP) { Console.WriteLine($"[FSW][WARN] PrismaPrepare rename: {exPP.Message}"); }
                }

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
