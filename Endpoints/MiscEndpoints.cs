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
    // Helper to delete files matching a base name (no extension) in a directory
    private static void DeleteImageFiles(string dir, string baseName)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, baseName + ".*"))
            if (Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                File.Delete(f);
    }

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

        // Priority 1: look up currentStage from productionFolders MongoDB (set on every file move)
        try
        {
            var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
            var pfDoc = pfCol.Find(
                Builders<BsonDocument>.Filter.Regex("fileName",
                    new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(safeFileName) + "$", "i"))
            ).SortByDescending(x => x["createdAt"]).FirstOrDefault();

            if (pfDoc != null && pfDoc.Contains("currentStage") && pfDoc["currentStage"] != BsonNull.Value
                && pfDoc["currentStage"].BsonType == BsonType.String)
            {
                var stage = pfDoc["currentStage"].AsString;
                var currentPath = pfDoc.Contains("currentFilePath") && pfDoc["currentFilePath"] != BsonNull.Value
                    ? pfDoc["currentFilePath"].AsString : (string?)null;
                return Results.Json(new { ok = true, folder = stage, fullPath = currentPath });
            }
        }
        catch (Exception exPf) { Console.WriteLine($"[WARN] file-stage productionFolders lookup: {exPf.Message}"); }

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

        // Physical scan for the file itself, most advanced folder first
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
        DeleteImageFiles(logoDir, "logo");

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
        DeleteImageFiles(dir, "logo");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// LOGO CONNEXION — Upload et affichage (logo dédié à la page de connexion)
// ======================================================
app.MapGet("/api/logo-login", (HttpContext ctx) =>
{
    var logoDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    string? found = null;
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
    {
        var candidate = Path.Combine(logoDir, "logo-login" + ext);
        if (File.Exists(candidate)) { found = candidate; break; }
    }
    if (found == null)
        return Results.NotFound();
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(found, out var ct)) ct = "image/png";
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
    return Results.File(File.OpenRead(found), ct);
});

app.MapPost("/api/logo-login", async (HttpContext ctx) =>
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
        Directory.CreateDirectory(logoDir);
        DeleteImageFiles(logoDir, "logo-login");

        var logoPath = Path.Combine(logoDir, "logo-login" + ext);
        using var stream = File.Create(logoPath);
        await file.CopyToAsync(stream);

        if (ext != ".png")
        {
            var pngPath = Path.Combine(logoDir, "logo-login.png");
            File.Copy(logoPath, pngPath, overwrite: true);
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] POST /api/logo-login: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/logo-login", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "logo-login");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// IMAGE DE FOND CONNEXION
// ======================================================
app.MapGet("/api/background-login", (HttpContext ctx) =>
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    string? found = null;
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
    {
        var candidate = Path.Combine(dir, "background-login" + ext);
        if (File.Exists(candidate)) { found = candidate; break; }
    }
    if (found == null) return Results.NotFound();
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(found, out var ct)) ct = "image/jpeg";
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
    return Results.File(File.OpenRead(found), ct);
});

app.MapPost("/api/background-login", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0) return Results.Json(new { ok = false, error = "Fichier manquant" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif" && ext != ".webp")
            return Results.Json(new { ok = false, error = "Format non supporté" });
        var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
        Directory.CreateDirectory(dir);
        DeleteImageFiles(dir, "background-login");
        var path = Path.Combine(dir, "background-login" + ext);
        using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/background-login", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "background-login");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// IMAGE DE BANDEAU HEADER
// ======================================================
app.MapGet("/api/header-banner", (HttpContext ctx) =>
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    string? found = null;
    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
    {
        var candidate = Path.Combine(dir, "header-banner" + ext);
        if (File.Exists(candidate)) { found = candidate; break; }
    }
    if (found == null) return Results.NotFound();
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(found, out var ct)) ct = "image/jpeg";
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
    return Results.File(File.OpenRead(found), ct);
});

app.MapPost("/api/header-banner", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0) return Results.Json(new { ok = false, error = "Fichier manquant" });
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".gif" && ext != ".webp")
            return Results.Json(new { ok = false, error = "Format non supporté" });
        var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
        Directory.CreateDirectory(dir);
        DeleteImageFiles(dir, "header-banner");
        var path = Path.Combine(dir, "header-banner" + ext);
        using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/header-banner", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "header-banner");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});
    }
}
