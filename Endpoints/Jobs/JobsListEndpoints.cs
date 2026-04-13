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

public static class JobsListEndpoints
{
    public static void MapJobsListEndpoints(this WebApplication app, string recyclePath)
    {
app.MapGet("/api/jobs", (string folder) =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var dir  = Path.GetFullPath(Path.Combine(root, folder));
        // Security: ensure directory is within hotfolders root
        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { ok = false, error = "Dossier non autorisé" });
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

    }
}
