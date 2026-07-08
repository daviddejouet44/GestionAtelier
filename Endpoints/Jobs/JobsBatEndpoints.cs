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

public static class JobsBatEndpoints
{
    public static void MapJobsBatEndpoints(this WebApplication app, string recyclePath)
    {
app.MapPost("/api/bat/execute", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var fullPath = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
        var xmlPath  = json.TryGetProperty("xmlPath",  out var xp) ? xp.GetString() ?? "" : "";

        // Security: validate that paths are within hotfolders
        if (!AuthHelper.IsPathSafe(fullPath))
            return Results.Json(new { ok = false, error = "Chemin non autorisé" });
        if (!string.IsNullOrWhiteSpace(xmlPath) && !AuthHelper.IsPathSafe(xmlPath))
            return Results.Json(new { ok = false, error = "Chemin XML non autorisé" });

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
        ProcessHelper.StartShellCommand(cmd);

        return Results.Json(new { ok = true, command = cmd });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

// ======================================================
// BAT PLANNING — mini-planning « BAT à envoyer » (numérique / papier)
// Alimenté par le champ dateEnvoiBat de la fiche de fabrication.
// ======================================================
app.MapGet("/api/bat/planning", (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAuthenticated(ctx))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        // Load config (planning days + alert threshold hours)
        var cfgCol = MongoDbHelper.GetCollection<BsonDocument>("batCommandConfig");
        var cfgDoc = cfgCol.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
        var planningDays = cfgDoc != null && cfgDoc.Contains("batPlanningDays") ? cfgDoc["batPlanningDays"].AsInt32 : 5;
        var alertHours = cfgDoc != null && cfgDoc.Contains("batPlanningAlertHours") ? cfgDoc["batPlanningAlertHours"].AsInt32 : 24;
        if (planningDays < 1) planningDays = 5;
        if (planningDays > 14) planningDays = 14;

        var now = DateTime.UtcNow;
        var today = now.Date;
        var windowEnd = today.AddDays(planningDays); // exclusive upper bound

        // Files still physically present in the kanban hotfolders (orphan detection)
        var hotRoot = BackendUtils.HotfoldersRoot();
        var activeFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(hotRoot))
        {
            foreach (var dir in Directory.GetDirectories(hotRoot))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly))
                        activeFileNames.Add(Path.GetFileName(file));
                }
                catch { }
            }
        }

        // BATs already sent/validated should not appear in the "to send" planning.
        var alreadyHandled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var batStatusCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
            foreach (var s in batStatusCol.Find(new BsonDocument()).ToList())
            {
                var status = s.Contains("status") && s["status"] != BsonNull.Value ? s["status"].AsString : "";
                var validated = s.Contains("validatedAt") && s["validatedAt"] != BsonNull.Value;
                if (!validated
                    && !string.Equals(status, "sent", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(status, "validated", StringComparison.OrdinalIgnoreCase))
                    continue;
                var sp = s.Contains("fullPath") && s["fullPath"] != BsonNull.Value ? s["fullPath"].AsString : "";
                var fn = Path.GetFileName(sp)?.ToLowerInvariant() ?? "";
                if (fn.StartsWith("bat_")) fn = fn.Substring(4);
                if (!string.IsNullOrEmpty(fn)) alreadyHandled.Add(fn);
            }
        }
        catch { }

        var fabCol = MongoDbHelper.GetFabricationsCollection();
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Exists("dateEnvoiBat"),
            Builders<BsonDocument>.Filter.Ne("dateEnvoiBat", BsonNull.Value),
            Builders<BsonDocument>.Filter.Lt("dateEnvoiBat", new BsonDateTime(windowEnd))
        );
        var docs = fabCol.Find(filter).ToList();

        var entries = new List<dynamic>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            // Skip explicitly excluded / locked jobs
            if (doc.Contains("excludeFromPlanning") && doc["excludeFromPlanning"].BsonType == BsonType.Boolean && doc["excludeFromPlanning"].AsBoolean)
                continue;
            if (doc.Contains("locked") && doc["locked"] != BsonNull.Value && doc["locked"].BsonType == BsonType.Boolean && doc["locked"].AsBoolean)
                continue;

            // Only Numérique / Papier BATs are relevant to the "à envoyer" planning
            var bat = doc.Contains("bat") && doc["bat"] != BsonNull.Value ? doc["bat"].AsString : "";
            string batType;
            if (string.Equals(bat, "Numérique", StringComparison.OrdinalIgnoreCase)) batType = "numerique";
            else if (string.Equals(bat, "Papier", StringComparison.OrdinalIgnoreCase)) batType = "papier";
            else continue;

            var fileName = doc.Contains("fileName") && doc["fileName"] != BsonNull.Value ? doc["fileName"].AsString : "";
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            // Skip orphan records: file no longer present in any active kanban folder
            if (!activeFileNames.Contains(fileName) && !activeFileNames.Contains(fileName.ToLowerInvariant()))
                continue;

            // Skip BATs already sent/validated
            var fnNorm = fileName.ToLowerInvariant();
            if (fnNorm.StartsWith("bat_")) fnNorm = fnNorm.Substring(4);
            if (alreadyHandled.Contains(fnNorm)) continue;

            // De-duplicate by (fileName + type)
            if (!seen.Add(fnNorm + "|" + batType)) continue;

            DateTime dEnvoi;
            try { dEnvoi = doc["dateEnvoiBat"].ToUniversalTime().Date; }
            catch { continue; }

            var numeroDossier = doc.Contains("numeroDossier") && doc["numeroDossier"] != BsonNull.Value ? doc["numeroDossier"].AsString : "";
            var client = doc.Contains("client") && doc["client"] != BsonNull.Value ? doc["client"].AsString : "";

            var offset = (int)(dEnvoi - today).TotalDays; // <0 = en retard
            var hoursUntil = (int)Math.Floor((dEnvoi - now).TotalHours);
            var isAlert = hoursUntil <= alertHours; // dans la fenêtre d'alerte (inclut le retard)

            entries.Add(new
            {
                fileName,
                numeroDossier,
                client,
                batType,
                dateEnvoiBat = dEnvoi.ToString("yyyy-MM-dd"),
                offset,
                hoursUntil,
                overdue = offset < 0,
                alert = isAlert
            });
        }

        // Build day buckets (today .. today+planningDays-1)
        var days = new List<object>();
        for (int i = 0; i < planningDays; i++)
        {
            var date = today.AddDays(i);
            var dstr = date.ToString("yyyy-MM-dd");
            days.Add(new
            {
                date = dstr,
                offset = i,
                numerique = entries.Where(e => (string)e.batType == "numerique" && (int)e.offset == i).ToList(),
                papier = entries.Where(e => (string)e.batType == "papier" && (int)e.offset == i).ToList()
            });
        }

        var overdue = new
        {
            numerique = entries.Where(e => (bool)e.overdue && (string)e.batType == "numerique").OrderBy(e => (string)e.dateEnvoiBat).ToList(),
            papier = entries.Where(e => (bool)e.overdue && (string)e.batType == "papier").OrderBy(e => (string)e.dateEnvoiBat).ToList()
        };

        var alertCount = entries.Count(e => (bool)e.alert);

        return Results.Json(new
        {
            ok = true,
            today = today.ToString("yyyy-MM-dd"),
            planningDays,
            alertHours,
            alertCount,
            overdue,
            days
        });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

app.MapGet("/api/assignment", (HttpContext ctx, string fullPath) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
        return Results.Json(new { ok = false, error = "Non authentifié" });
    var a = BackendUtils.FindAssignment(fullPath);
    if (a != null)
        return Results.Json(new { ok = true, assignment = new { fullPath = a.FullPath, operatorId = a.OperatorId, operatorName = a.OperatorName, assignedAt = a.AssignedAt, assignedBy = a.AssignedBy } });
    return Results.Json(new { ok = false, error = "Aucune affectation." });
});

app.MapGet("/api/assignments", (HttpContext ctx) =>
{
    if (!AuthHelper.IsAuthenticated(ctx))
        return Results.Json(new { ok = false, error = "Non authentifié" });
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
        if (!AuthHelper.IsAuthenticated(ctx))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        // Extract caller identity from token
        var callerName = AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "Système";

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
        var operator2 = users2.FirstOrDefault(u => u.Id == operatorId && (u.Profile == 2 || u.Profile == 4 || u.Profile == 6));
        if (operator2 == null)
            return Results.Json(new { ok = false, error = "Utilisateur introuvable ou profil non autorisé." });

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
        return ErrorHelper.HandleException(ex);
    }
});

    }
}
