using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class BackendUtils
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

        // If numeroDossier is known, check if a production folder already exists with the same number
        if (!string.IsNullOrWhiteSpace(numeroDossier))
        {
            var existingByDossier = col.Find(
                Builders<BsonDocument>.Filter.Eq("numeroDossier", numeroDossier)
            ).SortByDescending(x => x["createdAt"]).FirstOrDefault();

            if (existingByDossier != null && existingByDossier.Contains("folderPath"))
            {
                var existingFolderPath = existingByDossier["folderPath"].AsString;
                if (Directory.Exists(existingFolderPath))
                {
                    // Add file to the existing folder
                    var originalDir2 = Path.Combine(existingFolderPath, "Original");
                    Directory.CreateDirectory(originalDir2);
                    if (File.Exists(movedFilePath))
                        File.Copy(movedFilePath, Path.Combine(originalDir2, fileName), overwrite: true);

                    var fileEntry2 = new BsonDocument
                    {
                        ["stage"] = "Original",
                        ["fileName"] = fileName,
                        ["addedAt"] = DateTime.UtcNow
                    };

                    // Update existing document with the new file entry
                    await col.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", existingByDossier["_id"]),
                        Builders<BsonDocument>.Update.Push("files", fileEntry2));

                    // Insert a new document for this file pointing to the same folder
                    // (so CopyToProductionFolderStageAsync can find it by fileName)
                    var newDoc2 = new BsonDocument
                    {
                        ["number"] = BsonNull.Value,
                        ["numeroDossier"] = numeroDossier,
                        ["fileName"] = fileName,
                        ["originalFilePath"] = movedFilePath,
                        ["currentFilePath"] = movedFilePath,
                        ["folderPath"] = existingFolderPath,
                        ["createdAt"] = DateTime.UtcNow,
                        ["currentStage"] = "Début de production",
                        ["fabricationSheet"] = new BsonDocument(),
                        ["files"] = new BsonArray { fileEntry2 }
                    };
                    await col.InsertOneAsync(newDoc2);
                    return;
                }
            }
        }

        // Build folder name: use numeroDossier_safeName if available, else auto-increment NNN_safeName
        var safeName = SafeNameRegex.Replace(Path.GetFileNameWithoutExtension(fileName), "_");
        string folderName;
        int? number = null;
        if (!string.IsNullOrWhiteSpace(numeroDossier))
        {
            // Name the folder with dossier number + PDF name (e.g. "2026-0010_MyFile")
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
            sheet = sheet with { FileName = sheet.FileName.ToLowerInvariant() };

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
            ["formeDecoupe"]      = sheet.FormeDecoupe      == null ? BsonNull.Value : (BsonValue)sheet.FormeDecoupe,
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
            ["bat"]               = sheet.Bat               == null ? BsonNull.Value : (BsonValue)sheet.Bat,
            ["retraitLivraison"]  = sheet.RetraitLivraison  == null ? BsonNull.Value : (BsonValue)sheet.RetraitLivraison,
            ["adresseLivraison"]  = sheet.AdresseLivraison  == null ? BsonNull.Value : (BsonValue)sheet.AdresseLivraison,
            ["history"]           = historyArray,

            ["donneurOrdreNom"]       = sheet.DonneurOrdreNom       == null ? BsonNull.Value : (BsonValue)sheet.DonneurOrdreNom,
            ["donneurOrdrePrenom"]    = sheet.DonneurOrdrePrenom    == null ? BsonNull.Value : (BsonValue)sheet.DonneurOrdrePrenom,
            ["donneurOrdreTelephone"] = sheet.DonneurOrdreTelephone == null ? BsonNull.Value : (BsonValue)sheet.DonneurOrdreTelephone,
            ["donneurOrdreEmail"]     = sheet.DonneurOrdreEmail     == null ? BsonNull.Value : (BsonValue)sheet.DonneurOrdreEmail,
            ["pagination"]            = sheet.Pagination            == null ? BsonNull.Value : (BsonValue)sheet.Pagination,
            ["formatFeuille"]         = sheet.FormatFeuille         == null ? BsonNull.Value : (BsonValue)sheet.FormatFeuille,
            ["media1Fabricant"]       = sheet.Media1Fabricant       == null ? BsonNull.Value : (BsonValue)sheet.Media1Fabricant,
            ["media2Fabricant"]       = sheet.Media2Fabricant       == null ? BsonNull.Value : (BsonValue)sheet.Media2Fabricant,
            ["media3Fabricant"]       = sheet.Media3Fabricant       == null ? BsonNull.Value : (BsonValue)sheet.Media3Fabricant,
            ["media4Fabricant"]       = sheet.Media4Fabricant       == null ? BsonNull.Value : (BsonValue)sheet.Media4Fabricant,
            ["mediaCouverture"]         = sheet.MediaCouverture         == null ? BsonNull.Value : (BsonValue)sheet.MediaCouverture,
            ["mediaCouvertureFabricant"] = sheet.MediaCouvertureFabricant == null ? BsonNull.Value : (BsonValue)sheet.MediaCouvertureFabricant,
            ["rainage"]        = sheet.Rainage        == null ? BsonNull.Value : (BsonValue)sheet.Rainage.Value,
            ["ennoblissement"] = sheet.Ennoblissement == null ? BsonNull.Value : (BsonValue)new BsonArray(sheet.Ennoblissement),
            ["faconnageBinding"] = sheet.FaconnageBinding == null ? BsonNull.Value : (BsonValue)sheet.FaconnageBinding,
            ["plis"]             = sheet.Plis             == null ? BsonNull.Value : (BsonValue)sheet.Plis,
            ["sortie"]           = sheet.Sortie           == null ? BsonNull.Value : (BsonValue)sheet.Sortie,
            ["mailDevisFileName"] = sheet.MailDevisFileName == null ? BsonNull.Value : (BsonValue)sheet.MailDevisFileName,
            ["mailBatFileName"]   = sheet.MailBatFileName   == null ? BsonNull.Value : (BsonValue)sheet.MailBatFileName,
            ["dateDepart"]      = sheet.DateDepart      == null ? BsonNull.Value : (BsonValue)sheet.DateDepart.Value,
            ["dateLivraison"]   = sheet.DateLivraison   == null ? BsonNull.Value : (BsonValue)sheet.DateLivraison.Value,
            ["planningMachine"] = sheet.PlanningMachine == null ? BsonNull.Value : (BsonValue)sheet.PlanningMachine.Value,
            ["justifsClientsQuantite"] = sheet.JustifsClientsQuantite == null ? BsonNull.Value : (BsonValue)(int)sheet.JustifsClientsQuantite,
            ["justifsClientsAdresse"]  = sheet.JustifsClientsAdresse  == null ? BsonNull.Value : (BsonValue)sheet.JustifsClientsAdresse,
            ["repartitions"] = sheet.Repartitions == null
                ? BsonNull.Value
                : (BsonValue)new BsonArray(sheet.Repartitions.Select(r => new BsonDocument
                {
                    ["quantite"] = r.Quantite == null ? BsonNull.Value : (BsonValue)(int)r.Quantite,
                    ["adresse"]  = r.Adresse  == null ? BsonNull.Value : (BsonValue)r.Adresse
                }))
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
            FormeDecoupe     = GetNullableString(d, "formeDecoupe"),
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
            Bat              = GetNullableString(d, "bat"),
            RetraitLivraison = GetNullableString(d, "retraitLivraison"),
            AdresseLivraison = GetNullableString(d, "adresseLivraison"),
            History          = history,

            DonneurOrdreNom       = GetNullableString(d, "donneurOrdreNom"),
            DonneurOrdrePrenom    = GetNullableString(d, "donneurOrdrePrenom"),
            DonneurOrdreTelephone = GetNullableString(d, "donneurOrdreTelephone"),
            DonneurOrdreEmail     = GetNullableString(d, "donneurOrdreEmail"),
            Pagination            = GetNullableString(d, "pagination"),
            FormatFeuille         = GetNullableString(d, "formatFeuille"),
            Media1Fabricant       = GetNullableString(d, "media1Fabricant"),
            Media2Fabricant       = GetNullableString(d, "media2Fabricant"),
            Media3Fabricant       = GetNullableString(d, "media3Fabricant"),
            Media4Fabricant       = GetNullableString(d, "media4Fabricant"),
            MediaCouverture         = GetNullableString(d, "mediaCouverture"),
            MediaCouvertureFabricant = GetNullableString(d, "mediaCouvertureFabricant"),
            Rainage        = d.Contains("rainage") && d["rainage"] != BsonNull.Value && d["rainage"].BsonType == BsonType.Boolean ? (bool?)d["rainage"].AsBoolean : null,
            Ennoblissement = d.Contains("ennoblissement") && d["ennoblissement"] != BsonNull.Value && d["ennoblissement"].IsBsonArray
                             ? d["ennoblissement"].AsBsonArray.Select(v => v.AsString).ToList()
                             : null,
            FaconnageBinding = GetNullableString(d, "faconnageBinding"),
            Plis             = GetNullableString(d, "plis"),
            Sortie           = GetNullableString(d, "sortie"),
            MailDevisFileName = GetNullableString(d, "mailDevisFileName"),
            MailBatFileName   = GetNullableString(d, "mailBatFileName"),
            DateDepart      = d.Contains("dateDepart")      && d["dateDepart"]      != BsonNull.Value ? (DateTime?)d["dateDepart"].ToLocalTime()      : null,
            DateLivraison   = d.Contains("dateLivraison")   && d["dateLivraison"]   != BsonNull.Value ? (DateTime?)d["dateLivraison"].ToLocalTime()   : null,
            PlanningMachine = d.Contains("planningMachine") && d["planningMachine"] != BsonNull.Value ? (DateTime?)d["planningMachine"].ToLocalTime() : null,
            JustifsClientsQuantite = d.Contains("justifsClientsQuantite") && d["justifsClientsQuantite"] != BsonNull.Value ? (int?)d["justifsClientsQuantite"].AsInt32 : null,
            JustifsClientsAdresse  = GetNullableString(d, "justifsClientsAdresse"),
            Repartitions = d.Contains("repartitions") && d["repartitions"] != BsonNull.Value && d["repartitions"].IsBsonArray
                ? d["repartitions"].AsBsonArray.Select(v =>
                {
                    var r = v.AsBsonDocument;
                    return new GestionAtelier.Models.RepartitionItem
                    {
                        Quantite = r.Contains("quantite") && r["quantite"] != BsonNull.Value ? (int?)r["quantite"].AsInt32 : null,
                        Adresse  = r.Contains("adresse")  && r["adresse"]  != BsonNull.Value ? r["adresse"].AsString : null
                    };
                }).ToList()
                : null
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
