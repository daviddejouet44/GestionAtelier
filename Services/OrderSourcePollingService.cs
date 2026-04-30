using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Endpoints.Settings;
using GestionAtelier.Models;
using GestionAtelier.Services.OrderSources;

namespace GestionAtelier.Services;

/// <summary>
/// Service de polling en arrière-plan pour les sources de commandes automatiques.
/// Chaque source est vérifiée en parallèle selon son intervalle configuré.
/// Le traitement des fichiers par source est séquentiel (file d'attente interne).
/// </summary>
public class OrderSourcePollingService : BackgroundService
{
    private readonly ILogger<OrderSourcePollingService> _logger;
    private readonly Dictionary<string, DateTime> _nextPollTimes = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Delay between service heartbeats (30 seconds)
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public OrderSourcePollingService(ILogger<OrderSourcePollingService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[OrderSourcePolling] Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllSourcesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OrderSourcePolling] Unexpected error in polling loop.");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("[OrderSourcePolling] Service stopped.");
    }

    private async Task PollAllSourcesAsync(CancellationToken ct)
    {
        List<OrderSource> sources;
        try
        {
            var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
            var docs = col.Find(new BsonDocument()).ToList();
            sources = docs.Select(DeserializeSource).Where(s => s != null && s.Enabled).ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderSourcePolling] Failed to load sources from DB.");
            return;
        }

        if (sources.Count == 0) return;

        var now = DateTime.UtcNow;
        var tasks = sources
            .Where(s => ShouldPoll(s, now))
            .Select(s => Task.Run(() => PollSourceAsync(s, ct), ct))
            .ToList();

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private bool ShouldPoll(OrderSource source, DateTime now)
    {
        var key = source.Id;
        if (!_nextPollTimes.TryGetValue(key, out var next))
        {
            // First time: poll immediately
            _nextPollTimes[key] = now.AddMinutes(source.PollingIntervalMinutes);
            return true;
        }
        if (now >= next)
        {
            _nextPollTimes[key] = now.AddMinutes(source.PollingIntervalMinutes);
            return true;
        }
        return false;
    }

    // Public method to allow manual run from API
    public async Task RunSourceNowAsync(string sourceId, CancellationToken ct = default)
    {
        OrderSource? source = null;
        try
        {
            var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", sourceId)).FirstOrDefault();
            if (doc == null) throw new Exception($"Source {sourceId} not found");
            source = DeserializeSource(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrderSourcePolling] RunSourceNow failed to load source {Id}", sourceId);
            throw;
        }

        if (source != null)
            await PollSourceAsync(source, ct);
    }

    private async Task PollSourceAsync(OrderSource source, CancellationToken ct)
    {
        _logger.LogInformation("[OrderSourcePolling] Polling source '{Name}' (type={Type})", source.Name, source.Type);
        var startAt = DateTime.UtcNow;
        string pollStatus = "ok";
        int processed = 0, errors = 0, duplicates = 0;

        try
        {
            var configJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
            if (string.IsNullOrEmpty(configJson))
                throw new Exception("Configuration chiffrée manquante ou corrompue.");

            IOrderSourceProvider provider = source.Type.ToLower() switch
            {
                "sftp" => CreateSftpProvider(configJson),
                "dropbox" => CreateDropboxProvider(configJson),
                "googledrive" => CreateGoogleDriveProvider(configJson),
                "box" => CreateBoxProvider(configJson, source.Id),
                "onedrive" => CreateOneDriveProvider(configJson, source.Id),
                _ => throw new NotSupportedException($"Type de source non supporté : {source.Type}")
            };

            using (provider)
            {
                await provider.ConnectAsync(ct);

                var clientFolders = source.ClientMapping?.Keys.ToList() ?? new List<string>();
                if (clientFolders.Count == 0)
                    clientFolders.Add("default");

                foreach (var folder in clientFolders)
                {
                    var (p, e, d) = await ProcessClientFolderAsync(provider, source, folder, ct);
                    processed += p; errors += e; duplicates += d;
                }

                await provider.DisconnectAsync(ct);
            }

            _logger.LogInformation("[OrderSourcePolling] Source '{Name}' done: {Processed} processed, {Errors} errors, {Dup} duplicates",
                source.Name, processed, errors, duplicates);
        }
        catch (Exception ex)
        {
            pollStatus = "error";
            _logger.LogError(ex, "[OrderSourcePolling] Error polling source '{Name}'", source.Name);
        }
        finally
        {
            // Update lastPollAt and lastPollStatus
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                var update = Builders<BsonDocument>.Update
                    .Set("lastPollAt", startAt.ToString("O"))
                    .Set("lastPollStatus", pollStatus);
                col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", source.Id), update);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[OrderSourcePolling] Failed to update poll status."); }
        }
    }

    private async Task<(int processed, int errors, int duplicates)> ProcessClientFolderAsync(
        IOrderSourceProvider provider, OrderSource source, string folder, CancellationToken ct)
    {
        int processed = 0, errors = 0, duplicates = 0;
        List<RemoteFile> files;

        try { files = await provider.ListFilesAsync(folder, ct); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[OrderSourcePolling] Could not list files for folder '{Folder}'", folder);
            return (0, 1, 0);
        }

        // Group files: pair PDF with metadata (XML/JSON/CSV with same base name)
        var pdfFiles = files.Where(f => f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
        var metaFiles = files.Where(f =>
            f.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var pdf in pdfFiles)
        {
            if (ct.IsCancellationRequested) break;

            // Check file size
            if (source.MaxFileSizeMb > 0 && pdf.Size > (long)source.MaxFileSizeMb * 1024 * 1024)
            {
                _logger.LogWarning("[OrderSourcePolling] File {Name} exceeds max size ({Size} bytes), skipping.", pdf.Name, pdf.Size);
                await provider.MoveToErrorAsync(pdf.Path, folder, $"Fichier trop volumineux ({pdf.Size / 1024 / 1024} Mo > {source.MaxFileSizeMb} Mo)", ct);
                errors++;
                continue;
            }

            try
            {
                // Download PDF
                var pdfBytes = await provider.DownloadFileAsync(pdf.Path, ct);
                var hash = ComputeSha256(pdfBytes);

                // Anti-duplicate check
                var importCol = MongoDbHelper.GetCollection<BsonDocument>("order_source_imports");
                var existing = importCol.Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("fileHash", hash),
                    Builders<BsonDocument>.Filter.Eq("status", "success")
                )).FirstOrDefault();

                if (existing != null)
                {
                    _logger.LogInformation("[OrderSourcePolling] Duplicate file {Name} (hash={Hash}), skipping.", pdf.Name, hash);
                    await provider.MoveToErrorAsync(pdf.Path, folder, "Doublon détecté (hash identique)", ct);
                    LogImport(source, folder, pdf.Name, hash, "duplicate", null, "Doublon détecté");
                    duplicates++;
                    continue;
                }

                // Find matching metadata file
                var baseName = Path.GetFileNameWithoutExtension(pdf.Name);
                var metaFile = metaFiles.FirstOrDefault(m =>
                    Path.GetFileNameWithoutExtension(m.Name).Equals(baseName, StringComparison.OrdinalIgnoreCase));

                // Save PDF to hotfolders
                var clientId = source.ClientMapping?.GetValueOrDefault(folder) ?? folder;
                var jobId = await SavePdfAsJobAsync(source, clientId, pdf.Name, pdfBytes, hash,
                    metaFile != null ? await provider.DownloadFileAsync(metaFile.Path, ct) : null,
                    metaFile?.Name);

                // Move to processed
                await provider.MoveToProcessedAsync(pdf.Path, folder, ct);
                if (metaFile != null)
                    await provider.MoveToProcessedAsync(metaFile.Path, folder, ct);

                LogImport(source, folder, pdf.Name, hash, "success", jobId, null);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OrderSourcePolling] Error processing file {Name}", pdf.Name);
                try { await provider.MoveToErrorAsync(pdf.Path, folder, ex.Message, ct); }
                catch { /* best-effort */ }
                LogImport(source, folder, pdf.Name, "", "error", null, ex.Message);
                errors++;
            }
        }

        return (processed, errors, duplicates);
    }

    private async Task<string> SavePdfAsJobAsync(
        OrderSource source, string clientId, string fileName, byte[] pdfBytes, string hash,
        byte[]? metaBytes, string? metaFileName)
    {
        // Compute job ID - use full GUID for uniqueness
        var jobId = Guid.NewGuid().ToString("N");

        // Save PDF to hotfolder "Soumission"
        var hotRoot = BackendUtils.HotfoldersRoot();
        var soumissionDir = Path.Combine(hotRoot, "Soumission");
        Directory.CreateDirectory(soumissionDir);
        var destPdf = Path.Combine(soumissionDir, fileName);
        await File.WriteAllBytesAsync(destPdf, pdfBytes);

        // Parse metadata if available
        Dictionary<string, string>? metadata = null;
        if (metaBytes != null && metaFileName != null)
        {
            var ext = Path.GetExtension(metaFileName).ToLower();
            try
            {
                metadata = ParseMetadataFile(metaBytes, ext);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[OrderSourcePolling] Could not parse metadata file {Name}", metaFileName);
            }
        }

        // Build fabrication fiche
        var fiche = new BsonDocument
        {
            ["_id"] = jobId,
            ["importSource"] = "order-source",
            ["orderSourceId"] = source.Id,
            ["orderSourceName"] = source.Name,
            ["fileName"] = fileName,
            ["fileHash"] = hash,
            ["client"] = clientId,
            ["nomClient"] = clientId,
            ["quantite"] = source.DefaultQuantity,
            ["formatFini"] = source.DefaultFormat,
            ["statut"] = "brouillon",
            ["importedAt"] = DateTime.UtcNow.ToString("O"),
            ["pdfPath"] = destPdf
        };

        // Apply metadata mappings (reuse XML import config if available)
        if (metadata != null)
        {
            var cfg = MongoDbHelper.GetSettings<IntegrationsFullConfig>("integrations_full_config");
            var mapping = cfg?.XmlImport?.Mapping ?? new Dictionary<string, string>();

            foreach (var kv in metadata)
            {
                // Try reverse mapping first, then use the key directly
                var ficheKey = mapping.FirstOrDefault(m => m.Value.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)).Key
                               ?? kv.Key;
                fiche[ficheKey] = kv.Value;
            }
        }

        var fabCol = MongoDbHelper.GetCollection<BsonDocument>("fabrication");
        fabCol.InsertOne(fiche);

        _logger.LogInformation("[OrderSourcePolling] Created job {JobId} for file {File}", jobId, fileName);
        return jobId;
    }

    private static Dictionary<string, string> ParseMetadataFile(byte[] bytes, string ext)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = Encoding.UTF8.GetString(bytes);

        if (ext == ".xml")
        {
            var doc = System.Xml.Linq.XDocument.Parse(text);
            var root = doc.Root;
            if (root != null)
            {
                foreach (var el in root.Elements())
                    result[el.Name.LocalName] = el.Value;
                // Support nested structures
                foreach (var desc in root.Descendants())
                    result.TryAdd(desc.Name.LocalName, desc.Value);
            }
        }
        else if (ext == ".json")
        {
            using var jsonDoc = JsonDocument.Parse(text);
            foreach (var prop in jsonDoc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.ToString();
        }
        else if (ext == ".csv")
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length >= 2)
            {
                var headers = lines[0].Split(';', ',');
                var values = lines[1].Split(';', ',');
                for (int i = 0; i < Math.Min(headers.Length, values.Length); i++)
                    result[headers[i].Trim('"', ' ')] = values[i].Trim('"', ' ');
            }
        }

        return result;
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void LogImport(OrderSource source, string folder, string fileName, string hash,
        string status, string? jobId, string? errorMessage)
    {
        try
        {
            var col = MongoDbHelper.GetCollection<BsonDocument>("order_source_imports");
            var doc = new BsonDocument
            {
                ["sourceId"] = source.Id,
                ["sourceName"] = source.Name,
                ["clientFolder"] = folder,
                ["fileName"] = fileName,
                ["fileHash"] = hash,
                ["status"] = status,
                ["processedAt"] = DateTime.UtcNow.ToString("O")
            };
            if (!string.IsNullOrEmpty(jobId)) doc["jobId"] = jobId;
            if (!string.IsNullOrEmpty(errorMessage)) doc["errorMessage"] = errorMessage;
            col.InsertOne(doc);
        }
        catch (Exception ex)
        {
            // Log failure to console — audit trail persistence failure should be visible
            Console.WriteLine($"[WARN] OrderSourcePolling: Failed to persist import log entry for {fileName}: {ex.Message}");
        }
    }

    private IOrderSourceProvider CreateSftpProvider(string configJson)
    {
        var cfg = JsonSerializer.Deserialize<SftpSourceConfig>(configJson)
                  ?? throw new Exception("Configuration SFTP invalide");
        return new SftpOrderSourceProvider(cfg, _logger);
    }

    private IOrderSourceProvider CreateDropboxProvider(string configJson)
    {
        var cfg = JsonSerializer.Deserialize<DropboxSourceConfig>(configJson)
                  ?? throw new Exception("Configuration Dropbox invalide");
        return new DropboxOrderSourceProvider(cfg, _logger);
    }

    private IOrderSourceProvider CreateGoogleDriveProvider(string configJson)
    {
        var cfg = JsonSerializer.Deserialize<GoogleDriveSourceConfig>(configJson)
                  ?? throw new Exception("Configuration Google Drive invalide");
        return new GoogleDriveOrderSourceProvider(cfg, _logger);
    }

    private IOrderSourceProvider CreateBoxProvider(string configJson, string sourceId)
    {
        var cfg = JsonSerializer.Deserialize<BoxSourceConfig>(configJson)
                  ?? throw new Exception("Configuration Box invalide");
        return new BoxOrderSourceProvider(cfg, _logger, async updatedCfg =>
        {
            try
            {
                var newJson = JsonSerializer.Serialize(updatedCfg);
                var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("_id", sourceId),
                    Builders<BsonDocument>.Update
                        .Set("configEncrypted", CredentialCrypto.Encrypt(newJson))
                        .Set("updatedAt", DateTime.UtcNow.ToString("O")));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[Box] Failed to persist refreshed tokens for source {Id}", sourceId); }
        });
    }

    private IOrderSourceProvider CreateOneDriveProvider(string configJson, string sourceId)
    {
        var cfg = JsonSerializer.Deserialize<OneDriveSourceConfig>(configJson)
                  ?? throw new Exception("Configuration OneDrive invalide");
        return new OneDriveOrderSourceProvider(cfg, _logger, async updatedCfg =>
        {
            try
            {
                var newJson = JsonSerializer.Serialize(updatedCfg);
                var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("_id", sourceId),
                    Builders<BsonDocument>.Update
                        .Set("configEncrypted", CredentialCrypto.Encrypt(newJson))
                        .Set("updatedAt", DateTime.UtcNow.ToString("O")));
            }
            catch (Exception ex) { _logger.LogWarning(ex, "[OneDrive] Failed to persist refreshed tokens for source {Id}", sourceId); }
        });
    }

    private static OrderSource? DeserializeSource(BsonDocument doc)
    {
        try
        {
            doc.Remove("_id");
            var json = doc.ToJson();
            return JsonSerializer.Deserialize<OrderSource>(json);
        }
        catch { return null; }
    }
}
