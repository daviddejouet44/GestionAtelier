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
    private static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

    // Helper to delete files matching a base name (no extension) in a directory
    private static void DeleteImageFiles(string dir, string baseName)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.GetFiles(dir, baseName + ".*"))
            if (Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                File.Delete(f);
    }

    private static string? FindImageFile(string dir, string baseName)
    {
        foreach (var ext in SupportedImageExtensions)
        {
            var candidate = Path.Combine(dir, baseName + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static IResult ServeOptionalImage(HttpContext ctx, string dir, string baseName, string defaultContentType)
    {
        ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";

        var found = FindImageFile(dir, baseName);
        if (found == null)
        {
            // These images are optional: 204 avoids noisy 404 logs while <img onerror> still hides the missing asset.
            return Results.NoContent();
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(found, out var ct)) ct = defaultContentType;
        return Results.File(File.OpenRead(found), ct);
    }

    public static void MapMiscEndpoints(this WebApplication app)
    {
app.MapGet("/api/ping", () => "pong");

app.MapGet("/favicon.ico", () => Results.NoContent());

app.MapGet("/api/file-stage", (HttpContext ctx, string fileName) =>
{
    try
    {
        if (!AuthHelper.IsAuthenticated(ctx))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        // Sanitize: only allow the base filename, no path traversal
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            return Results.Json(new { ok = false, folder = (string?)null, fullPath = (string?)null, batStatus = (string?)null });

        // Helper: resolve BAT sub-status from batStatus collection
        string? ResolveBatStatus(string fn)
        {
            try
            {
                // batStatus documents use fullPath (e.g., ".../BAT/BAT_myfile.pdf"), not fileName
                var batStatusCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
                var allDocs = batStatusCol.Find(new BsonDocument()).ToList();
                var batDoc = allDocs
                    .Where(d => {
                        if (!d.Contains("fullPath") || d["fullPath"] == BsonNull.Value) return false;
                        var docFn = Path.GetFileName(d["fullPath"].AsString);
                        if (docFn.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase)) docFn = docFn.Substring(4);
                        return string.Equals(docFn, fn, StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderByDescending(d => d["_id"].AsObjectId.CreationTime)
                    .FirstOrDefault();
                if (batDoc == null) return null;
                if (batDoc.Contains("rejectedAt") && batDoc["rejectedAt"] != BsonNull.Value) return "refuse";
                if (batDoc.Contains("validatedAt") && batDoc["validatedAt"] != BsonNull.Value) return "valide";
                if (batDoc.Contains("sentAt") && batDoc["sentAt"] != BsonNull.Value) return "envoye";
            }
            catch { /* ignore */ }
            return null;
        }

        // Priority 1: physical file scan (always accurate, reflects actual current tile)
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
            {
                var batStatus = (folder == "BAT") ? ResolveBatStatus(safeFileName) : null;
                // Also update DB for consistency (non-blocking)
                try
                {
                    var pfCol2 = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                    pfCol2.UpdateMany(
                        Builders<BsonDocument>.Filter.Eq("fileName", safeFileName),
                        Builders<BsonDocument>.Update.Set("currentStage", folder).Set("currentFilePath", path));
                }
                catch { /* non-blocking */ }
                return Results.Json(new { ok = true, folder, fullPath = path, batStatus });
            }
        }

        // Priority 2: look up currentStage from productionFolders MongoDB (fallback when file not in standard hotfolders)
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
                var batStatus = (stage == "BAT") ? ResolveBatStatus(safeFileName) : null;
                return Results.Json(new { ok = true, folder = stage, fullPath = currentPath, batStatus });
            }
        }
        catch (Exception exPf) { Console.WriteLine($"[WARN] file-stage productionFolders lookup: {exPf.Message}"); }

        return Results.Json(new { ok = false, folder = (string?)null, fullPath = (string?)null, batStatus = (string?)null });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

app.MapGet("/api/folders", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        if (decoded.Split(':').Length < 3)
            return Results.Json(new { ok = false, error = "Non authentifié" });
    }
    catch { return Results.Json(new { ok = false, error = "Non authentifié" }); }

    var clean = BackendUtils.Hotfolders()
        .Select(n => n.Replace("\u00A0", " ").Trim())
        .ToArray();
    return Results.Json(clean);
});

// ======================================================
// API — FILE
// ======================================================

app.MapGet("/api/file", (HttpContext ctx, string path, bool? download) =>
{
    // Authentication: accept header auth OR ?token= query param (needed for window.open / new tab)
    bool authenticated = AuthHelper.IsAuthenticated(ctx);
    if (!authenticated)
    {
        var queryToken = ctx.Request.Query["token"].ToString();
        if (!string.IsNullOrWhiteSpace(queryToken))
        {
            try
            {
                var key = AuthHelper.GetSigningKey();
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var validationParams = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                };
                handler.ValidateToken(queryToken, validationParams, out _);
                authenticated = true;
            }
            catch (Microsoft.IdentityModel.Tokens.SecurityTokenException) { }
            catch (ArgumentException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] /api/file token validation error: {ex.Message}");
            }
        }
    }
    if (!authenticated)
        return Results.Unauthorized();

    // 2. Restrict strictly to hotfolders to prevent path traversal
    var root = Path.GetFullPath(BackendUtils.HotfoldersRoot());
    string full;
    try
    {
        full = Path.GetFullPath(path);
    }
    catch
    {
        return Results.BadRequest();
    }
    if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    if (!File.Exists(full))
        return Results.NotFound();

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(full, out var ct))
        ct = "application/octet-stream";

    if (download == true)
        return Results.File(File.OpenRead(full), ct, Path.GetFileName(full), enableRangeProcessing: true);
    return Results.File(File.OpenRead(full), ct, enableRangeProcessing: true);
});

// ======================================================
// DELIVERY (planning)
// ======================================================

app.MapGet("/api/tools/prismasync", (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAuthenticated(ctx))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        var cfg = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig")
            .Find(new BsonDocument()).FirstOrDefault();
        var url = cfg?.Contains("prismaSyncUrl") == true
            ? cfg["prismaSyncUrl"].AsString
            : null;

        if (string.IsNullOrWhiteSpace(url))
            return Results.Json(new { ok = false, error = "URL PrismaSync non configurée. Veuillez la définir dans les paramètres (champ prismaSyncUrl)." });

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
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
    return ServeOptionalImage(ctx, logoDir, "logo", "image/png");
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
        return ErrorHelper.HandleException(ex);
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
        return ErrorHelper.HandleException(ex);
    }
});

// ======================================================
// LOGO CONNEXION — Upload et affichage (logo dédié à la page de connexion)
// ======================================================
app.MapGet("/api/logo-login", (HttpContext ctx) =>
{
    var logoDir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    return ServeOptionalImage(ctx, logoDir, "logo-login", "image/png");
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
        return ErrorHelper.HandleException(ex);
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
        return ErrorHelper.HandleException(ex);
    }
});

// ======================================================
// IMAGE DE FOND CONNEXION
// ======================================================
app.MapGet("/api/background-login", (HttpContext ctx) =>
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    return ServeOptionalImage(ctx, dir, "background-login", "image/jpeg");
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
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapDelete("/api/background-login", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "background-login");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

// ======================================================
// IMAGE DE BANDEAU HEADER
// ======================================================
app.MapGet("/api/header-banner", (HttpContext ctx) =>
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    return ServeOptionalImage(ctx, dir, "header-banner", "image/jpeg");
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
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapDelete("/api/header-banner", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "header-banner");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

// ======================================================
// IMAGE DASHBOARD
// ======================================================
IResult DashboardImageHandler(HttpContext ctx)
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    var found = FindImageFile(dir, "dashboard-image");
    ctx.Response.Headers["Cache-Control"] = "no-cache, no-store";
    if (found == null) return Results.NoContent();
    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(found, out var ct)) ct = "image/jpeg";
    if (ctx.Request.Method == "HEAD")
    {
        ctx.Response.ContentType = ct;
        return Results.Ok();
    }
    return Results.File(File.OpenRead(found), ct);
}
app.MapGet("/api/dashboard-image", DashboardImageHandler);
app.MapMethods("/api/dashboard-image", new[] { "HEAD" }, DashboardImageHandler);

app.MapPost("/api/dashboard-image", async (HttpContext ctx) =>
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
        DeleteImageFiles(dir, "dashboard-image");
        var path = Path.Combine(dir, "dashboard-image" + ext);
        using var stream = File.Create(path);
        await file.CopyToAsync(stream);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapDelete("/api/dashboard-image", (HttpContext ctx) =>
{
    try
    {
        DeleteImageFiles(Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro"), "dashboard-image");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});
    }
}
