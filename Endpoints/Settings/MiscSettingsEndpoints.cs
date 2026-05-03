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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

public static class MiscSettingsEndpoints
{
    public static void MapMiscSettingsEndpoints(this WebApplication app, string recyclePath)
    {
app.MapGet("/api/config/paths", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };
        return Results.Json(new { ok = true, config = new { hotfoldersRoot = cfg.HotfoldersRoot, recycleBinPath = cfg.RecycleBinPath, acrobatExePath = cfg.AcrobatExePath, fieryPaths = cfg.FieryPaths } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/paths", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };

        if (json.TryGetProperty("hotfoldersRoot", out var hrEl) && !string.IsNullOrWhiteSpace(hrEl.GetString()))
            existing.HotfoldersRoot = hrEl.GetString()!;
        if (json.TryGetProperty("recycleBinPath", out var rbEl))
            existing.RecycleBinPath = rbEl.GetString() ?? existing.RecycleBinPath;
        if (json.TryGetProperty("acrobatExePath", out var aeEl))
            existing.AcrobatExePath = aeEl.GetString() ?? existing.AcrobatExePath;
        if (json.TryGetProperty("fieryPaths", out var fpEl) && fpEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            existing.FieryPaths = fpEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        MongoDbHelper.UpsertSettings("paths", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/config/fabrication-imports", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();
        return Results.Json(new { ok = true, config = new {
            media1Path = cfg.Media1Path, media2Path = cfg.Media2Path,
            media3Path = cfg.Media3Path, media4Path = cfg.Media4Path,
            typeDocumentPath = cfg.TypeDocumentPath
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/fabrication-imports", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();

        if (json.TryGetProperty("media1Path", out var m1)) existing.Media1Path = m1.GetString() ?? "";
        if (json.TryGetProperty("media2Path", out var m2)) existing.Media2Path = m2.GetString() ?? "";
        if (json.TryGetProperty("media3Path", out var m3)) existing.Media3Path = m3.GetString() ?? "";
        if (json.TryGetProperty("media4Path", out var m4)) existing.Media4Path = m4.GetString() ?? "";
        if (json.TryGetProperty("typeDocumentPath", out var td)) existing.TypeDocumentPath = td.GetString() ?? "";

        MongoDbHelper.UpsertSettings("fabrication_imports", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/settings/faconnage-options", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
        var docs = col.Find(new BsonDocument()).ToList();
        var labels = docs.Select(d => d.Contains("label") ? d["label"].AsString : "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        return Results.Json(labels);
    }
    catch { return Results.Json(new List<string>()); }
});

app.MapPost("/api/settings/faconnage-import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        if (!ctx.Request.HasFormContentType)
            return Results.Json(new { ok = false, error = "Form data required" });

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null)
            return Results.Json(new { ok = false, error = "Fichier CSV requis" });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var labels = content
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(',').First().Trim().Trim('"'))
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct()
            .ToList();

        if (labels.Count == 0)
            return Results.Json(new { ok = false, error = "Aucune option trouvée dans le CSV" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
        col.DeleteMany(new BsonDocument());
        var docs = labels.Select(l => new BsonDocument { ["label"] = l }).ToList();
        col.InsertMany(docs);

        return Results.Json(new { ok = true, count = labels.Count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// PUT /api/settings/faconnage-options — save option list directly (admin only)
app.MapPut("/api/settings/faconnage-options", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "options[] requis" });

        var labels = optEl.EnumerateArray()
            .Select(e => e.GetString()?.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct()
            .ToList();

        var col = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
        col.DeleteMany(new BsonDocument());
        if (labels.Count > 0)
        {
            var docs = labels.Select(l => new BsonDocument { ["label"] = l! }).ToList();
            col.InsertMany(docs);
        }

        return Results.Json(new { ok = true, count = labels.Count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── GET /api/settings/binding-options ────────────────────────────────────
app.MapGet("/api/settings/binding-options", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<SimpleStringListSettings>("bindingOptions");
        var list = cfg?.Items ?? new List<string> { "2 piques métal","2 piques à plat","2 piques booklet","Dos carré collé","Dos carré piqué","2 piques calendrier (à l'italienne)","Wire'O" };
        return Results.Json(new { ok = true, options = list });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── PUT /api/settings/binding-options ────────────────────────────────────
app.MapPut("/api/settings/binding-options", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "options[] requis" });

        var items = optEl.EnumerateArray().Select(e => e.GetString()?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        MongoDbHelper.UpsertSettings("bindingOptions", new SimpleStringListSettings { Items = items! });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── GET /api/settings/folds-options ──────────────────────────────────────
app.MapGet("/api/settings/folds-options", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<SimpleStringListSettings>("foldsOptions");
        var list = cfg?.Items ?? new List<string> { "Pli accordéon","Pli roulé","Pli fenêtre" };
        return Results.Json(new { ok = true, options = list });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── PUT /api/settings/folds-options ──────────────────────────────────────
app.MapPut("/api/settings/folds-options", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "options[] requis" });

        var items = optEl.EnumerateArray().Select(e => e.GetString()?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        MongoDbHelper.UpsertSettings("foldsOptions", new SimpleStringListSettings { Items = items! });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── GET /api/settings/output-options ─────────────────────────────────────
app.MapGet("/api/settings/output-options", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<SimpleStringListSettings>("outputOptions");
        var list = cfg?.Items ?? new List<string> { "À plat","Assemblée" };
        return Results.Json(new { ok = true, options = list });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── PUT /api/settings/output-options ─────────────────────────────────────
app.MapPut("/api/settings/output-options", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "options[] requis" });

        var items = optEl.EnumerateArray().Select(e => e.GetString()?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        MongoDbHelper.UpsertSettings("outputOptions", new SimpleStringListSettings { Items = items! });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/config/integrations", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();
        return Results.Json(new { ok = true, config = new { preparePath = cfg.PreparePath, fieryPath = cfg.FieryPath, tempCopyPath = cfg.TempCopyPath, prismaPrepareExePath = cfg.PrismaPrepareExePath, prismaPrepareOutputPath = cfg.PrismaPrepareOutputPath } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/integrations", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();

        if (json.TryGetProperty("preparePath", out var pp)) existing.PreparePath = pp.GetString() ?? "";
        if (json.TryGetProperty("fieryPath", out var fp)) existing.FieryPath = fp.GetString() ?? "";
        if (json.TryGetProperty("tempCopyPath", out var tcp)) existing.TempCopyPath = tcp.GetString() ?? "";
        if (json.TryGetProperty("prismaPrepareExePath", out var ppe)) existing.PrismaPrepareExePath = ppe.GetString() ?? "";
        if (json.TryGetProperty("prismaPrepareOutputPath", out var ppop)) existing.PrismaPrepareOutputPath = ppop.GetString() ?? IntegrationsSettings.DefaultPrismaPrepareOutputPath;

        MongoDbHelper.UpsertSettings("integrations", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/admin/activity-logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetActivityLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/admin/logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetRecentLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/admin/stats", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3)
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var statsUsers = BackendUtils.LoadUsers();
        if (!statsUsers.Any(u => u.Id == parts[0]))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        var root = BackendUtils.HotfoldersRoot();
        var filesByFolder = new Dictionary<string, int>();
        int totalFiles = 0;

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                var count = Directory.GetFiles(dir).Length;
                filesByFolder[folderName] = count;
                totalFiles += count;
            }
        }

        // Scheduled this week
        var now = DateTime.Now;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);
        var deliveries = BackendUtils.LoadDeliveries();
        var scheduledThisWeek = deliveries.Values.Count(d => d.Date >= startOfWeek && d.Date < endOfWeek);

        // Active assignments
        var assignments = BackendUtils.LoadAssignments();
        var activeAssignments = assignments.Count;

        return Results.Json(new { ok = true, stats = new {
            totalFiles,
            filesByFolder,
            scheduledThisWeek,
            activeAssignments
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/admin/migrate-to-mongo", () =>
{
    var results = new List<string>();
    var appData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FluxAtelier");

    // ---- Users ----
    try
    {
        var usersPath = Path.Combine(appData, "users.json");
        if (File.Exists(usersPath))
        {
            var json = File.ReadAllText(usersPath);
            var users = JsonSerializer.Deserialize<List<UserItem>>(json) ?? new();
            var col = MongoDbHelper.GetUsersCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var u in users)
                BackendUtils.InsertUser(u);
            File.Move(usersPath, usersPath + ".bak", overwrite: true);
            results.Add($"Users: {users.Count} migrated");
        }
        else
        {
            results.Add("Users: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Users error: {ex.Message}"); }

    // ---- Deliveries ----
    try
    {
        var deliveriesPath = Path.Combine(appData, "deliveries.json");
        if (File.Exists(deliveriesPath))
        {
            var json = File.ReadAllText(deliveriesPath);
            var list = JsonSerializer.Deserialize<List<DeliveryItem>>(json) ?? new();
            var col = MongoDbHelper.GetDeliveriesCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var item in list)
                BackendUtils.UpsertDelivery(item);
            File.Move(deliveriesPath, deliveriesPath + ".bak", overwrite: true);
            results.Add($"Deliveries: {list.Count} migrated");
        }
        else
        {
            results.Add("Deliveries: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Deliveries error: {ex.Message}"); }

    // ---- Fabrications ----
    try
    {
        var fabricationsPath = Path.Combine(appData, "fabrications.json");
        if (File.Exists(fabricationsPath))
        {
            var json = File.ReadAllText(fabricationsPath);
            var list = JsonSerializer.Deserialize<List<FabricationSheet>>(json) ?? new();
            var col = MongoDbHelper.GetFabricationsCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var sheet in list)
                BackendUtils.UpsertFabrication(sheet);
            File.Move(fabricationsPath, fabricationsPath + ".bak", overwrite: true);
            results.Add($"Fabrications: {list.Count} migrated");
        }
        else
        {
            results.Add("Fabrications: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Fabrications error: {ex.Message}"); }

    return Results.Json(new { ok = true, results });
});

app.MapGet("/api/config/commands", (HttpContext ctx) =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = col.Find(new BsonDocument()).FirstOrDefault();
    if (doc == null)
        return Results.Json(new { ok = true, config = new {
            batCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /T \"{typeWork}\" /SP /C {quantity}",
            prismaPrepareCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            printCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /SP /C {quantity}",
            modifyCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            fieryHotfolderBase = "C:\\Fiery\\Hotfolders",
            controllerPath = "C:\\PrismaSync\\Controller"
        }});
    return Results.Json(new { ok = true, config = new {
        batCommand = doc.Contains("batCommand") ? doc["batCommand"].AsString : "",
        prismaPrepareCommand = doc.Contains("prismaPrepareCommand") ? doc["prismaPrepareCommand"].AsString : "",
        prismaCommand = doc.Contains("prismaCommand") ? doc["prismaCommand"].AsString : "",
        printCommand = doc.Contains("printCommand") ? doc["printCommand"].AsString : "",
        modifyCommand = doc.Contains("modifyCommand") ? doc["modifyCommand"].AsString : "",
        fieryHotfolderBase = doc.Contains("fieryHotfolderBase") ? doc["fieryHotfolderBase"].AsString : "",
        controllerPath = doc.Contains("controllerPath") ? doc["controllerPath"].AsString : ""
    }});
});

app.MapPut("/api/config/commands", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = new BsonDocument();
    foreach (var prop in json.EnumerateObject())
        doc[prop.Name] = prop.Value.GetString() ?? "";
    col.ReplaceOne(new BsonDocument(), doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

app.MapDelete("/api/production-folder", async (string path) =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var prodRoot = Path.GetFullPath(Path.Combine(root, "DossiersProduction"));
        var fullPath = Path.GetFullPath(path);
        // Security: ensure path is within production folders root using canonical paths
        var relative = Path.GetRelativePath(prodRoot, fullPath);
        if (relative.StartsWith("..") || Path.IsPathRooted(relative) ||
            !fullPath.StartsWith(prodRoot, StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { ok = false, error = "Chemin non autorisé" });
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, true);
        // Remove MongoDB entry
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        col.DeleteMany(Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("path", fullPath),
            Builders<BsonDocument>.Filter.Eq("folderPath", fullPath)
        ));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/config/preflight", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<PreflightSettings>("preflight") ?? new PreflightSettings();
        return Results.Json(new { ok = true, config = new { dropletStandard = cfg.DropletStandard, dropletFondPerdu = cfg.DropletFondPerdu, droplets = cfg.Droplets } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/preflight", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<PreflightSettings>("preflight") ?? new PreflightSettings();

        if (json.TryGetProperty("dropletStandard", out var ds)) existing.DropletStandard = ds.GetString() ?? existing.DropletStandard;
        if (json.TryGetProperty("dropletFondPerdu", out var df)) existing.DropletFondPerdu = df.GetString() ?? existing.DropletFondPerdu;
        if (json.TryGetProperty("droplets", out var dropletsEl) && dropletsEl.ValueKind == JsonValueKind.Array)
        {
            existing.Droplets = new List<DropletConfig>();
            foreach (var d in dropletsEl.EnumerateArray())
            {
                var name = d.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var pathVal = d.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                existing.Droplets.Add(new DropletConfig { Name = name, Path = pathVal });
            }
        }

        MongoDbHelper.UpsertSettings("preflight", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/config/preflight/droplets", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<PreflightSettings>("preflight") ?? new PreflightSettings();
        return Results.Json(new { ok = true, droplets = cfg.Droplets });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — FORMATS FEUILLE EN MACHINE
// ======================================================

app.MapGet("/api/settings/sheet-formats", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("sheetFormats");
        var docs = col.Find(new BsonDocument()).ToList();
        var formats = docs.Select(d => d.Contains("label") ? d["label"].AsString : "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        return Results.Json(formats);
    }
    catch { return Results.Json(new List<string>()); }
});

app.MapPost("/api/settings/sheet-formats/import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        if (!ctx.Request.HasFormContentType)
            return Results.Json(new { ok = false, error = "Form data required" });

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null)
            return Results.Json(new { ok = false, error = "Fichier CSV requis" });

        using var reader = new System.IO.StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var labels = content
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(',').First().Trim().Trim('"'))
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct()
            .ToList();

        if (labels.Count == 0)
            return Results.Json(new { ok = false, error = "Aucun format trouvé dans le CSV" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("sheetFormats");
        col.DeleteMany(new BsonDocument());
        col.InsertMany(labels.Select(l => new BsonDocument { ["label"] = l }).ToList());

        return Results.Json(new { ok = true, count = labels.Count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — PRODUITS NÉCESSITANT UNE COUVERTURE
// ======================================================

app.MapGet("/api/settings/cover-products", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<CoverProductsSettings>("coverProducts") ?? new CoverProductsSettings();
        return Results.Json(cfg.Products ?? new List<string>());
    }
    catch { return Results.Json(new List<string>()); }
});

app.MapPost("/api/settings/cover-products", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var products = new List<string>();
        if (body.TryGetProperty("products", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            products = arr.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

        MongoDbHelper.UpsertSettings("coverProducts", new CoverProductsSettings { Products = products });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — RÈGLES DE CALCUL NOMBRE DE FEUILLES
// ======================================================

app.MapGet("/api/settings/sheet-calculation-rules", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<SheetCalculationSettings>("sheetCalculationRules") ?? new SheetCalculationSettings();
        return Results.Json(new { ok = true, rules = cfg.Rules ?? new Dictionary<string, int>() });
    }
    catch { return Results.Json(new { ok = true, rules = new Dictionary<string, int>() }); }
});

app.MapPost("/api/settings/sheet-calculation-rules", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var rules = new Dictionary<string, int>();
        if (body.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in rulesEl.EnumerateObject())
            {
                if (prop.Value.TryGetInt32(out int divisor))
                    rules[prop.Name] = divisor;
            }
        }

        MongoDbHelper.UpsertSettings("sheetCalculationRules", new SheetCalculationSettings { Rules = rules });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — DÉLAI DE LIVRAISON
// ======================================================

app.MapGet("/api/settings/delivery-delay", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<DeliveryDelaySettings>("deliveryDelay") ?? new DeliveryDelaySettings();
        return Results.Json(new { ok = true, delayHours = cfg.DelayHours });
    }
    catch { return Results.Json(new { ok = true, delayHours = 48 }); }
});

app.MapPost("/api/settings/delivery-delay", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        int delayHours = 48;
        if (body.TryGetProperty("delayHours", out var d)) d.TryGetInt32(out delayHours);
        MongoDbHelper.UpsertSettings("deliveryDelay", new DeliveryDelaySettings { DelayHours = delayHours });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — DATES CLÉS
// ======================================================

app.MapGet("/api/settings/key-dates", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<KeyDatesSettings>("keyDates") ?? new KeyDatesSettings();
        return Results.Json(new { ok = true, sendOffsetHours = cfg.SendOffsetHours, finitionsOffsetHours = cfg.FinitionsOffsetHours, impressionOffsetHours = cfg.ImpressionOffsetHours });
    }
    catch { return Results.Json(new { ok = true, sendOffsetHours = 48, finitionsOffsetHours = 72, impressionOffsetHours = 96 }); }
});

app.MapPut("/api/settings/key-dates", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        int sendH = 48, finH = 72, impH = 96;
        if (body.TryGetProperty("sendOffsetHours", out var se)) se.TryGetInt32(out sendH);
        if (body.TryGetProperty("finitionsOffsetHours", out var fi)) fi.TryGetInt32(out finH);
        if (body.TryGetProperty("impressionOffsetHours", out var im)) im.TryGetInt32(out impH);
        MongoDbHelper.UpsertSettings("keyDates", new KeyDatesSettings { SendOffsetHours = sendH, FinitionsOffsetHours = finH, ImpressionOffsetHours = impH });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — PALLIER TEMPS PAR GRAMMAGE
// ======================================================

app.MapGet("/api/settings/grammage-time-config", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<GrammageTimeConfig>("grammageTimeConfig") ?? new GrammageTimeConfig();
        return Results.Json(new { ok = true, rules = cfg.Rules });
    }
    catch { return Results.Json(new { ok = true, rules = new object[0] }); }
});

app.MapPut("/api/settings/grammage-time-config", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var rules = new List<GrammageTimeRule>();
        if (body.TryGetProperty("rules", out var rulesEl) && rulesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var r in rulesEl.EnumerateArray())
            {
                var rule = new GrammageTimeRule();
                if (r.TryGetProperty("engineName", out var en)) rule.EngineName = en.GetString() ?? "";
                if (r.TryGetProperty("grammageMin", out var gmr)) { gmr.TryGetInt32(out int gmrV); rule.GrammageMin = gmrV; }
                if (r.TryGetProperty("grammageMax", out var gmar)) { gmar.TryGetInt32(out int gmarV); rule.GrammageMax = gmarV; }
                if (r.TryGetProperty("timePerSheetSeconds", out var tpsr)) { tpsr.TryGetInt32(out int tpsrV); rule.TimePerSheetSeconds = tpsrV; }
                rules.Add(rule);
            }
        }
        MongoDbHelper.UpsertSettings("grammageTimeConfig", new GrammageTimeConfig { Rules = rules });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — CONFIG JDF
// ======================================================

app.MapGet("/api/settings/jdf-config", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<JdfConfig>("jdfConfig") ?? new JdfConfig();
        return Results.Json(new { ok = true, enabled = cfg.Enabled, fields = cfg.Fields });
    }
    catch { return Results.Json(new { ok = true, enabled = false, fields = new object[0] }); }
});

app.MapPut("/api/settings/jdf-config", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        bool enabled = false;
        if (body.TryGetProperty("enabled", out var en)) enabled = en.GetBoolean();
        var fields = new List<JdfFieldConfig>();
        if (body.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var f in fieldsEl.EnumerateArray())
            {
                var fc = new JdfFieldConfig();
                if (f.TryGetProperty("fieldId", out var fid)) fc.FieldId = fid.GetString() ?? "";
                if (f.TryGetProperty("label", out var lbl)) fc.Label = lbl.GetString() ?? "";
                if (f.TryGetProperty("included", out var inc)) fc.Included = inc.GetBoolean();
                fields.Add(fc);
            }
        }
        MongoDbHelper.UpsertSettings("jdfConfig", new JdfConfig { Enabled = enabled, Fields = fields });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — PASSES (feuilles supplémentaires)
// ======================================================

app.MapGet("/api/settings/passes-config", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<PassesConfig>("passesConfig") ?? new PassesConfig();
        return Results.Json(new { ok = true, config = cfg });
    }
    catch { return Results.Json(new { ok = true, config = new PassesConfig() }); }
});

app.MapPost("/api/settings/passes-config", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var cfg = new PassesConfig();
        if (json.TryGetProperty("faconnage", out var f)) cfg.Faconnage = f.GetInt32();
        if (json.TryGetProperty("pelliculageRecto", out var pr)) cfg.PelliculageRecto = pr.GetInt32();
        if (json.TryGetProperty("pelliculageRectoVerso", out var prv)) cfg.PelliculageRectoVerso = prv.GetInt32();
        if (json.TryGetProperty("rainage", out var r)) cfg.Rainage = r.GetInt32();
        if (json.TryGetProperty("dorure", out var dv)) cfg.Dorure = dv.GetInt32();
        if (json.TryGetProperty("dosCarreColle", out var dcc)) cfg.DosCarreColle = dcc.GetInt32();
        MongoDbHelper.UpsertSettings("passesConfig", cfg);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/settings/passes-config", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var cfg = new PassesConfig();
        if (json.TryGetProperty("faconnage", out var f)) cfg.Faconnage = f.GetInt32();
        if (json.TryGetProperty("pelliculageRecto", out var pr)) cfg.PelliculageRecto = pr.GetInt32();
        if (json.TryGetProperty("pelliculageRectoVerso", out var prv)) cfg.PelliculageRectoVerso = prv.GetInt32();
        if (json.TryGetProperty("rainage", out var r)) cfg.Rainage = r.GetInt32();
        if (json.TryGetProperty("dorure", out var dv)) cfg.Dorure = dv.GetInt32();
        if (json.TryGetProperty("dosCarreColle", out var dcc)) cfg.DosCarreColle = dcc.GetInt32();
        MongoDbHelper.UpsertSettings("passesConfig", cfg);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// SETTINGS — ICÔNES FINITIONS
// ======================================================

// ======================================================
// SETTINGS — BAT PAPIER CONFIG
// ======================================================

app.MapGet("/api/settings/bat-papier-config", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<BatPapierConfig>("batPapierConfig") ?? new BatPapierConfig();
        return Results.Json(new { ok = true, config = cfg });
    }
    catch { return Results.Json(new { ok = true, config = new BatPapierConfig() }); }
});

app.MapPut("/api/settings/bat-papier-config", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var cfg = MongoDbHelper.GetSettings<BatPapierConfig>("batPapierConfig") ?? new BatPapierConfig();
        if (json.TryGetProperty("enabled", out var en)) cfg.Enabled = en.GetBoolean();
        if (json.TryGetProperty("hotfolder", out var hf)) cfg.Hotfolder = hf.GetString() ?? "";
        MongoDbHelper.UpsertSettings("batPapierConfig", cfg);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapGet("/api/settings/finition-icons", (HttpContext ctx) =>
{
    try
    {
        var iconsDir = Path.Combine(ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "wwwroot_pro", "images", "finitions");
        if (!Directory.Exists(iconsDir))
            return Results.Json(new { ok = true, icons = new object[0] });

        var icons = Directory.GetFiles(iconsDir)
            .Select(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var ext  = Path.GetExtension(f);
                return new { type = name, url = $"/pro/images/finitions/{Path.GetFileName(f)}", ext };
            }).ToArray();
        return Results.Json(new { ok = true, icons });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/settings/finition-icons", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }

        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var finitionType = form["type"].ToString();

        if (file == null || file.Length == 0)
            return Results.Json(new { ok = false, error = "Fichier manquant" });
        if (string.IsNullOrWhiteSpace(finitionType))
            return Results.Json(new { ok = false, error = "Type de finition manquant" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowed = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };
        if (!allowed.Contains(ext))
            return Results.Json(new { ok = false, error = "Format non supporté" });

        var iconsDir = Path.Combine(ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "wwwroot_pro", "images", "finitions");
        Directory.CreateDirectory(iconsDir);

        // Remove any existing icon for this type
        foreach (var old in Directory.GetFiles(iconsDir, finitionType + ".*"))
            File.Delete(old);

        var dest = Path.Combine(iconsDir, finitionType + ext);
        using var stream = File.Create(dest);
        await file.CopyToAsync(stream);

        return Results.Json(new { ok = true, url = $"/pro/images/finitions/{finitionType}{ext}" });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// GET /api/settings/imap — Récupère la config IMAP
app.MapGet("/api/settings/imap", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<ImapSettings>("imapSettings");
        return Results.Json(new { ok = true, settings = cfg ?? new ImapSettings() });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// PUT /api/settings/imap — Enregistre la config IMAP (admin only)
app.MapPut("/api/settings/imap", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (!string.IsNullOrEmpty(token))
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[2] != "3")
                return Results.Json(new { ok = false, error = "Admin only" });
        }
        var cfg = await ctx.Request.ReadFromJsonAsync<ImapSettings>();
        if (cfg == null) return Results.Json(new { ok = false, error = "Payload invalide" });
        MongoDbHelper.UpsertSettings("imapSettings", cfg);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// Planning colors configuration (admin only)
// GET  /api/settings/planning-colors  → { ok, colors: { engines: {name: hex}, finitions: {name: hex} } }
// PUT  /api/settings/planning-colors  → save colors
// ======================================================
app.MapGet("/api/settings/planning-colors", (HttpContext ctx) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("planningColors");
        var doc = col.Find(new BsonDocument()).FirstOrDefault();
        var engines = new Dictionary<string, string>();
        var finitions = new Dictionary<string, string>();
        if (doc != null)
        {
            if (doc.Contains("engines") && doc["engines"].IsBsonDocument)
                foreach (var e in doc["engines"].AsBsonDocument)
                    engines[e.Name] = e.Value.IsString ? e.Value.AsString : "#8b5cf6";
            if (doc.Contains("finitions") && doc["finitions"].IsBsonDocument)
                foreach (var f in doc["finitions"].AsBsonDocument)
                    finitions[f.Name] = f.Value.IsString ? f.Value.AsString : "#f59e0b";
        }
        return Results.Json(new { ok = true, colors = new { engines, finitions } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/settings/planning-colors", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var enginesDoc = new BsonDocument();
        var finitionsDoc = new BsonDocument();
        if (json.TryGetProperty("engines", out var engProp) && engProp.ValueKind == JsonValueKind.Object)
            foreach (var p in engProp.EnumerateObject())
                enginesDoc[p.Name] = p.Value.GetString() ?? "#8b5cf6";
        if (json.TryGetProperty("finitions", out var finProp) && finProp.ValueKind == JsonValueKind.Object)
            foreach (var p in finProp.EnumerateObject())
                finitionsDoc[p.Name] = p.Value.GetString() ?? "#f59e0b";

        var col = MongoDbHelper.GetCollection<BsonDocument>("planningColors");
        col.DeleteMany(new BsonDocument());
        col.InsertOne(new BsonDocument { ["engines"] = enginesDoc, ["finitions"] = finitionsDoc });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── GET /api/settings/actions-config  ─────────────────────────────────────────
app.MapGet("/api/settings/actions-config", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts   = decoded.Split(':');
        if (parts.Length < 3) return Results.Json(new { ok = false, error = "Auth requise" });

        var saved = MongoDbHelper.GetSettings<KanbanActionsConfig>("kanbanActionsConfig");
        var actions = saved?.Actions.Count > 0
            ? saved.Actions
            : DefaultKanbanActions();
        return Results.Json(new { ok = true, actions });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ── PUT /api/settings/actions-config  ─────────────────────────────────────────
app.MapPut("/api/settings/actions-config", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts   = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("actions", out var actEl) || actEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "actions manquant" });

        var actions = new List<KanbanAction>();
        foreach (var a in actEl.EnumerateArray())
        {
            var id    = a.TryGetProperty("id",      out var idEl)  ? idEl.GetString()    ?? "" : "";
            var label = a.TryGetProperty("label",   out var lEl)   ? lEl.GetString()     ?? "" : "";
            var enab  = a.TryGetProperty("enabled", out var enEl)  ? enEl.GetBoolean()        : true;
            if (!string.IsNullOrWhiteSpace(id))
                actions.Add(new KanbanAction { Id = id, Label = label, Enabled = enab });
        }

        // If empty list submitted, reset to defaults
        if (actions.Count == 0)
            actions = DefaultKanbanActions();

        MongoDbHelper.UpsertSettings("kanbanActionsConfig", new KanbanActionsConfig { Actions = actions });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

    }

    private static List<KanbanAction> DefaultKanbanActions() => new()
    {
        new() { Id = "prismasync",    Label = "Envoyer vers PrismaSync",   Enabled = true },
        new() { Id = "prisma-prepare",Label = "Ouvrir dans PrismaPrepare", Enabled = true },
        new() { Id = "direct-print",  Label = "Impression directe",        Enabled = true },
        new() { Id = "fiery",         Label = "Envoyer dans Fiery",        Enabled = true },
    };

    private static async Task<object> SavePassesConfigAsync(HttpContext ctx)
    {
        try
        {
            var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
            var cfg = new PassesConfig();
            if (json.TryGetProperty("faconnage", out var f)) cfg.Faconnage = f.GetInt32();
            if (json.TryGetProperty("pelliculageRecto", out var pr)) cfg.PelliculageRecto = pr.GetInt32();
            if (json.TryGetProperty("pelliculageRectoVerso", out var prv)) cfg.PelliculageRectoVerso = prv.GetInt32();
            if (json.TryGetProperty("rainage", out var r)) cfg.Rainage = r.GetInt32();
            if (json.TryGetProperty("dorure", out var d)) cfg.Dorure = d.GetInt32();
            if (json.TryGetProperty("dosCarreColle", out var dcc)) cfg.DosCarreColle = dcc.GetInt32();
            MongoDbHelper.UpsertSettings("passesConfig", cfg);
            return new { ok = true };
        }
        catch (Exception ex) { return new { ok = false, error = ex.Message }; }
    }
}

public class ImapSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string Email { get; set; } = "";
    public bool UseSsl { get; set; } = true;
}
