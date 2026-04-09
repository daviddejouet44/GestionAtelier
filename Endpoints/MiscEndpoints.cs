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

namespace GestionAtelier.Endpoints;

public static class MiscEndpointsExtensions
{
    public static void MapMiscEndpoints(this WebApplication app)
    {
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
    }
}
