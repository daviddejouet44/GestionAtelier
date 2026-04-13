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

    }
}
