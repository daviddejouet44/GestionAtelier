using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

public static class DashboardEndpointsExtensions
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {

// ======================================================
// GET /api/dashboard/stats
// Comprehensive production statistics for the dashboard.
// Requires a valid auth token (any profile >= 2).
// ======================================================
app.MapGet("/api/dashboard/stats", (HttpContext ctx) =>
{
    try
    {
        // Auth check (any authenticated user)
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        string userId;
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3) return Results.Json(new { ok = false, error = "Token invalide" });
            userId = parts[0];
        }
        catch { return Results.Json(new { ok = false, error = "Token invalide" }); }

        var users = BackendUtils.LoadUsers();
        if (!users.Any(u => u.Id == userId))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        // ──────────────────────────────────────────────
        // 1. Jobs par dossier (filesystem scan)
        // ──────────────────────────────────────────────
        var root = BackendUtils.HotfoldersRoot();
        var byFolder = new List<object>();
        int totalActive = 0;

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                if (string.Equals(folderName, "quote_pdf", StringComparison.OrdinalIgnoreCase)) continue;
                var count = Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly).Length;
                if (count > 0)
                    byFolder.Add(new { folder = folderName, count });
                totalActive += count;
            }
        }

        // ──────────────────────────────────────────────
        // 2. Agrégats depuis MongoDB fabrications
        // ──────────────────────────────────────────────
        var fabCol = MongoDbHelper.GetFabricationsCollection();
        // Only active (non-excluded) fabrications with an active PDF file
        var activeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                if (string.Equals(folderName, "quote_pdf", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly))
                        activeFileNames.Add(Path.GetFileName(f));
                }
                catch { }
            }
        }

        var allFabs = fabCol.Find(
            Builders<BsonDocument>.Filter.Ne("excludeFromPlanning", true)
        ).ToList();

        // Filter to only active files
        var activeFabs = allFabs.Where(doc =>
        {
            var fn = doc.Contains("fileName") && doc["fileName"] != BsonNull.Value ? doc["fileName"].AsString : "";
            return !string.IsNullOrWhiteSpace(fn) && activeFileNames.Contains(fn);
        }).ToList();

        // Helpers
        static string Str(BsonDocument doc, string key)
        {
            if (!doc.Contains(key) || doc[key] == BsonNull.Value) return "";
            try { return doc[key].AsString ?? ""; } catch { return ""; }
        }
        static int IntVal(BsonDocument doc, string key)
        {
            if (!doc.Contains(key) || doc[key] == BsonNull.Value) return 0;
            try { return doc[key].ToInt32(); } catch { return 0; }
        }

        // 2a. By moteur d'impression
        var byMoteurDict = new Dictionary<string, (int count, int feuilles)>(StringComparer.OrdinalIgnoreCase);
        // 2b. By type de travail
        var byTypeTravailDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 2c. By process (Numérique / Offset)
        var byProcessDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 2d. Paper consumption (media1 as main paper)
        var paperDict = new Dictionary<string, (int jobs, int feuilles)>(StringComparer.OrdinalIgnoreCase);
        // 2e. By operator
        var byOperateurDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 2f. Quantité totale (copies)
        int totalQuantite = 0;
        int totalFeuilles = 0;

        foreach (var doc in activeFabs)
        {
            var moteur = Str(doc, "moteurImpression");
            if (string.IsNullOrWhiteSpace(moteur)) moteur = "— Non défini —";
            var feuilles = IntVal(doc, "nombreFeuilles");

            if (!byMoteurDict.ContainsKey(moteur)) byMoteurDict[moteur] = (0, 0);
            byMoteurDict[moteur] = (byMoteurDict[moteur].count + 1, byMoteurDict[moteur].feuilles + feuilles);

            var type = Str(doc, "typeTravail");
            if (string.IsNullOrWhiteSpace(type)) type = "— Non défini —";
            byTypeTravailDict.TryGetValue(type, out var tc);
            byTypeTravailDict[type] = tc + 1;

            var process = Str(doc, "process");
            if (string.IsNullOrWhiteSpace(process)) process = "— Non défini —";
            byProcessDict.TryGetValue(process, out var pc);
            byProcessDict[process] = pc + 1;

            // Paper: Media1 as the primary paper (fallback to papier)
            var paper = Str(doc, "media1");
            if (string.IsNullOrWhiteSpace(paper)) paper = Str(doc, "papier");
            if (string.IsNullOrWhiteSpace(paper)) paper = "— Non défini —";
            if (!paperDict.ContainsKey(paper)) paperDict[paper] = (0, 0);
            paperDict[paper] = (paperDict[paper].jobs + 1, paperDict[paper].feuilles + feuilles);

            var op = Str(doc, "operateur");
            if (string.IsNullOrWhiteSpace(op)) op = "— Non assigné —";
            byOperateurDict.TryGetValue(op, out var oc);
            byOperateurDict[op] = oc + 1;

            totalQuantite += IntVal(doc, "quantite");
            totalFeuilles += feuilles;
        }

        var byMoteur = byMoteurDict
            .OrderByDescending(kv => kv.Value.count)
            .Select(kv => new { moteur = kv.Key, count = kv.Value.count, totalFeuilles = kv.Value.feuilles })
            .ToList<object>();

        var byTypeTravail = byTypeTravailDict
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new { type = kv.Key, count = kv.Value })
            .ToList<object>();

        var byProcess = byProcessDict
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new { process = kv.Key, count = kv.Value })
            .ToList<object>();

        var paperConsumption = paperDict
            .OrderByDescending(kv => kv.Value.feuilles)
            .Select(kv => new { papier = kv.Key, jobCount = kv.Value.jobs, totalFeuilles = kv.Value.feuilles })
            .ToList<object>();

        var byOperateur = byOperateurDict
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new { operateur = kv.Key, count = kv.Value })
            .ToList<object>();

        // ──────────────────────────────────────────────
        // 3. Planning + reporting finitions + temps
        // ──────────────────────────────────────────────
        var today = DateTime.UtcNow.Date;
        var endOfWeek = today.AddDays(7);
        var endOfMonth = today.AddDays(30);

        int retardsCount = 0;
        int plannedImpression7 = 0;
        int plannedImpression30 = 0;
        int plannedFinitions7 = 0;
        int plannedFinitions30 = 0;
        int plannedLivraisons7 = 0;
        int plannedLivraisons30 = 0;

        var byEnnoblissementDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byFaconnageBindingDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byPlisDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bySortieDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int byRainageCount = 0;

        int totalTempsMinutes = 0;
        int totalTempsCount = 0;

        DateTime? DateVal(BsonDocument doc, string key)
        {
            if (!doc.Contains(key) || doc[key] == BsonNull.Value) return null;
            try { return doc[key].ToUniversalTime().Date; } catch { return null; }
        }

        foreach (var doc in activeFabs)
        {
            var fn = doc.Contains("fileName") && doc["fileName"] != BsonNull.Value ? doc["fileName"].AsString : "";
            if (!activeFileNames.Contains(fn)) continue;
            if (doc.Contains("locked") && doc["locked"] != BsonNull.Value
                && doc["locked"].BsonType == BsonType.Boolean && doc["locked"].AsBoolean) continue;
            if (doc.Contains("statutProduction") && doc["statutProduction"] != BsonNull.Value)
            {
                var statut = doc["statutProduction"].AsString;
                if (string.Equals(statut, "Fin de production", StringComparison.OrdinalIgnoreCase)) continue;
            }

            var dateImp = DateVal(doc, "dateImpression");
            var dateFinitions = DateVal(doc, "dateProductionFinitions");
            var dateLivraison = DateVal(doc, "dateLivraison");

            if (dateImp.HasValue)
            {
                if (dateImp.Value < today) retardsCount++;
                if (dateImp.Value >= today && dateImp.Value < endOfWeek) plannedImpression7++;
                if (dateImp.Value >= today && dateImp.Value < endOfMonth) plannedImpression30++;
            }
            if (dateFinitions.HasValue)
            {
                if (dateFinitions.Value >= today && dateFinitions.Value < endOfWeek) plannedFinitions7++;
                if (dateFinitions.Value >= today && dateFinitions.Value < endOfMonth) plannedFinitions30++;
            }
            if (dateLivraison.HasValue)
            {
                if (dateLivraison.Value >= today && dateLivraison.Value < endOfWeek) plannedLivraisons7++;
                if (dateLivraison.Value >= today && dateLivraison.Value < endOfMonth) plannedLivraisons30++;
            }

            if (doc.Contains("ennoblissement") && doc["ennoblissement"].BsonType == BsonType.Array)
            {
                foreach (var v in doc["ennoblissement"].AsBsonArray.Select(x => x?.ToString()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    byEnnoblissementDict.TryGetValue(v!, out var c);
                    byEnnoblissementDict[v!] = c + 1;
                }
            }

            var binding = Str(doc, "faconnageBinding").Trim();
            if (!string.IsNullOrWhiteSpace(binding))
            {
                byFaconnageBindingDict.TryGetValue(binding, out var c);
                byFaconnageBindingDict[binding] = c + 1;
            }

            if (doc.Contains("rainage") && doc["rainage"] != BsonNull.Value && doc["rainage"].IsBoolean && doc["rainage"].AsBoolean)
                byRainageCount++;

            var plis = Str(doc, "plis").Trim();
            if (!string.IsNullOrWhiteSpace(plis))
            {
                byPlisDict.TryGetValue(plis, out var c);
                byPlisDict[plis] = c + 1;
            }

            var sortie = Str(doc, "sortie").Trim();
            if (!string.IsNullOrWhiteSpace(sortie))
            {
                bySortieDict.TryGetValue(sortie, out var c);
                bySortieDict[sortie] = c + 1;
            }

            var temps = IntVal(doc, "tempsProduitMinutes");
            if (temps > 0)
            {
                totalTempsMinutes += temps;
                totalTempsCount++;
            }
        }

        var byEnnoblissement = byEnnoblissementDict.OrderByDescending(x => x.Value)
            .Select(x => new { value = x.Key, count = x.Value }).ToList<object>();
        var byFaconnageBinding = byFaconnageBindingDict.OrderByDescending(x => x.Value)
            .Select(x => new { value = x.Key, count = x.Value }).ToList<object>();
        var byPlis = byPlisDict.OrderByDescending(x => x.Value)
            .Select(x => new { value = x.Key, count = x.Value }).ToList<object>();
        var bySortie = bySortieDict.OrderByDescending(x => x.Value)
            .Select(x => new { value = x.Key, count = x.Value }).ToList<object>();
        var byRainage = new List<object> { new { value = "Oui", count = byRainageCount } };

        // ──────────────────────────────────────────────
        // 4. 5 Most recently modified jobs
        // ──────────────────────────────────────────────
        var recentJobs = new List<object>();
        if (Directory.Exists(root))
        {
            var allPdfs = new List<(string path, DateTime modified)>();
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                if (string.Equals(folderName, "quote_pdf", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly))
                        allPdfs.Add((f, File.GetLastWriteTimeUtc(f)));
                }
                catch { }
            }

            foreach (var (path, modified) in allPdfs.OrderByDescending(x => x.modified).Take(5))
            {
                var fn = Path.GetFileName(path);
                var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
                // Try to get fabrication data
                var fabDoc = activeFabs.FirstOrDefault(d => string.Equals(Str(d, "fileName"), fn, StringComparison.OrdinalIgnoreCase));
                var numeroDossier = fabDoc != null ? Str(fabDoc, "numeroDossier") : "";
                var client = fabDoc != null ? Str(fabDoc, "client") : "";
                var tempsProduitMinutes = fabDoc != null ? IntVal(fabDoc, "tempsProduitMinutes") : 0;
                recentJobs.Add(new { fileName = fn, folder, modified = modified.ToString("yyyy-MM-ddTHH:mm:ss"), numeroDossier, client, tempsProduitMinutes });
            }
        }

        var jobsWithTemps = activeFabs
            .Select(doc => new
            {
                numeroDossier = Str(doc, "numeroDossier"),
                client = Str(doc, "client"),
                fileName = Str(doc, "fileName"),
                moteurImpression = Str(doc, "moteurImpression"),
                tempsProduitMinutes = IntVal(doc, "tempsProduitMinutes")
            })
            .Where(j => j.tempsProduitMinutes > 0)
            .OrderByDescending(j => j.tempsProduitMinutes)
            .Take(20)
            .ToList<object>();

        var avgTempsMinutes = totalTempsCount > 0 ? (int)Math.Round((double)totalTempsMinutes / totalTempsCount) : 0;

        return Results.Json(new
        {
            ok = true,
            generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            summary = new
            {
                totalActive,
                totalQuantite,
                totalFeuilles,
                retardsCount,
                plannedImpression7,
                plannedImpression30,
                plannedFinitions7,
                plannedFinitions30,
                plannedLivraisons7,
                plannedLivraisons30,
                jobsWithFiche = activeFabs.Count
            },
            byFolder,
            byMoteur,
            byTypeTravail,
            byProcess,
            paperConsumption,
            byOperateur,
            recentJobs,
            byEnnoblissement,
            byFaconnageBinding,
            byRainage,
            byPlis,
            bySortie,
            totalTempsMinutes,
            avgTempsMinutes,
            jobsWithTemps
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// GET /api/dashboard/stats/export-csv
// Exports the production stats as a CSV file download.
// ======================================================
app.MapGet("/api/dashboard/stats/export-csv", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3) return Results.Json(new { ok = false, error = "Token invalide" });
            var userId = parts[0];
            var users = BackendUtils.LoadUsers();
            if (!users.Any(u => u.Id == userId)) return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });
        }
        catch { return Results.Json(new { ok = false, error = "Token invalide" }); }

        var root = BackendUtils.HotfoldersRoot();
        var activeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                if (string.Equals(folderName, "quote_pdf", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly))
                        activeFileNames.Add(Path.GetFileName(f));
                }
                catch { }
            }
        }

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var activeFabs = fabCol.Find(
            Builders<BsonDocument>.Filter.Ne("excludeFromPlanning", true)
        ).ToList().Where(doc =>
        {
            var fn = doc.Contains("fileName") && doc["fileName"] != BsonNull.Value ? doc["fileName"].AsString : "";
            return !string.IsNullOrWhiteSpace(fn) && activeFileNames.Contains(fn);
        }).ToList();

        DateTime? fromDate = null;
        DateTime? toDate = null;
        if (ctx.Request.Query.TryGetValue("from", out var fromRaw) && DateTime.TryParse(fromRaw.ToString(), out var fromParsed))
            fromDate = fromParsed.Date;
        if (ctx.Request.Query.TryGetValue("to", out var toRaw) && DateTime.TryParse(toRaw.ToString(), out var toParsed))
            toDate = toParsed.Date;

        if (fromDate.HasValue || toDate.HasValue)
        {
            DateTime? ReadDate(BsonDocument doc, string key)
            {
                if (!doc.Contains(key) || doc[key] == BsonNull.Value) return null;
                try { return doc[key].ToUniversalTime().Date; } catch { return null; }
            }

            activeFabs = activeFabs.Where(doc =>
            {
                var date = ReadDate(doc, "dateImpression") ?? ReadDate(doc, "dateReception");
                if (!date.HasValue) return false;
                if (fromDate.HasValue && date.Value < fromDate.Value) return false;
                if (toDate.HasValue && date.Value > toDate.Value) return false;
                return true;
            }).ToList();
        }

        static string Str(BsonDocument doc, string key)
        {
            if (!doc.Contains(key) || doc[key] == BsonNull.Value) return "";
            try { return doc[key].AsString ?? ""; } catch { return ""; }
        }
        static int IntVal(BsonDocument doc, string key)
        {
            if (!doc.Contains(key) || doc[key] == BsonNull.Value) return 0;
            try { return doc[key].ToInt32(); } catch { return 0; }
        }
        static string CsvEsc(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        var sb = new StringBuilder();
        // BOM for Excel compatibility
        sb.Append('\uFEFF');
        sb.AppendLine("Fichier,Dossier N°,Client,Moteur,Type de travail,Process,Opérateur,Média 1,Média 2,Quantité,Nb feuilles,Statut,Date impression");

        foreach (var doc in activeFabs.OrderBy(d => Str(d, "numeroDossier")))
        {
            var fileName = Str(doc, "fileName");
            var numeroDossier = Str(doc, "numeroDossier");
            var client = Str(doc, "client");
            if (string.IsNullOrWhiteSpace(client)) client = Str(doc, "nomClient");
            var moteur = Str(doc, "moteurImpression");
            var typeTravail = Str(doc, "typeTravail");
            var process = Str(doc, "process");
            var operateur = Str(doc, "operateur");
            var media1 = Str(doc, "media1");
            if (string.IsNullOrWhiteSpace(media1)) media1 = Str(doc, "papier");
            var media2 = Str(doc, "media2");
            var quantite = IntVal(doc, "quantite");
            var feuilles = IntVal(doc, "nombreFeuilles");
            var statut = Str(doc, "statutProduction");
            var dateImp = "";
            if (doc.Contains("dateImpression") && doc["dateImpression"] != BsonNull.Value)
            {
                try { dateImp = doc["dateImpression"].ToUniversalTime().ToString("yyyy-MM-dd"); } catch { }
            }

            sb.AppendLine(string.Join(",", new[]
            {
                CsvEsc(fileName), CsvEsc(numeroDossier), CsvEsc(client),
                CsvEsc(moteur), CsvEsc(typeTravail), CsvEsc(process),
                CsvEsc(operateur), CsvEsc(media1), CsvEsc(media2),
                quantite.ToString(), feuilles.ToString(),
                CsvEsc(statut), CsvEsc(dateImp)
            }));
        }

        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"stats-production-{date}.csv\"";
        ctx.Response.ContentType = "text/csv; charset=utf-8";
        return Results.Bytes(bytes, "text/csv; charset=utf-8");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

    } // MapDashboardEndpoints
} // class
