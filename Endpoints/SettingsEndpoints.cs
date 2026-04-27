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

public static class SettingsEndpointsExtensions
{
    public static void MapSettingsEndpoints(this WebApplication app, string recyclePath)
    {
app.MapGet("/api/config/schedule", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };
        return Results.Json(new { ok = true, config = new { workStart = cfg.WorkStart, workEnd = cfg.WorkEnd, holidays = cfg.Holidays } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/schedule", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        if (json.TryGetProperty("workStart", out var wsEl)) existing.WorkStart = wsEl.GetString() ?? existing.WorkStart;
        if (json.TryGetProperty("workEnd", out var weEl)) existing.WorkEnd = weEl.GetString() ?? existing.WorkEnd;

        MongoDbHelper.UpsertSettings("schedule", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/config/schedule/holidays", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("date", out var dateEl))
            return Results.Json(new { ok = false, error = "date requis" });

        var dateStr = dateEl.GetString() ?? "";
        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        if (!existing.Holidays.Contains(dateStr))
        {
            existing.Holidays.Add(dateStr);
            existing.Holidays.Sort();
            MongoDbHelper.UpsertSettings("schedule", existing);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/schedule/holidays", (HttpContext ctx, string date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var existing = MongoDbHelper.GetSettings<ScheduleSettings>("schedule")
            ?? new ScheduleSettings { WorkStart = "08:00", WorkEnd = "18:00", Holidays = new List<string>() };

        existing.Holidays.Remove(date);
        MongoDbHelper.UpsertSettings("schedule", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Paths (chemins d'accès)
// ======================================================

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
        return Results.Json(new { ok = true, config = new { hotfoldersRoot = cfg.HotfoldersRoot, recycleBinPath = cfg.RecycleBinPath, acrobatExePath = cfg.AcrobatExePath } });
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

        MongoDbHelper.UpsertSettings("paths", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Fabrication Imports
// ======================================================

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

// ======================================================
// SETTINGS — Façonnage options (CSV import)
// ======================================================

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
            // Only the first comma-separated column is used as the option label
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


// ======================================================
// CONFIG — Moteurs d'impression (CRUD + MongoDB)
// ======================================================

app.MapGet("/api/config/print-engines", () =>
{
    try
    {
        var engines = MongoDbHelper.GetPrintEnginesWithIp();
        if (engines.Count == 0)
        {
            // Return default list if none configured
            return Results.Json(new[] {
                new { name = "Offset", ip = "" }, new { name = "Numérique", ip = "" },
                new { name = "Jet d'encre", ip = "" }, new { name = "Sérigraphie", ip = "" },
                new { name = "Flexographie", ip = "" }, new { name = "Héliogravure", ip = "" },
                new { name = "Tampographie", ip = "" }, new { name = "Laser", ip = "" }
            });
        }
        return Results.Json(engines);
    }
    catch (Exception)
    {
        return Results.Json(new object[0]);
    }
});

app.MapPost("/api/config/print-engines", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return Results.Json(new { ok = false, error = "name requis" });

        var ip = json.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "" : "";
        MongoDbHelper.AddPrintEngineWithIp(nameEl.GetString()!, ip);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/config/print-engines/import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("engines", out var enginesEl))
            return Results.Json(new { ok = false, error = "engines requis" });

        int count = 0;
        foreach (var e in enginesEl.EnumerateArray())
        {
            string name = "", ip = "";
            if (e.ValueKind == JsonValueKind.Object)
            {
                name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                ip   = e.TryGetProperty("ip",   out var i) ? i.GetString() ?? "" : "";
            }
            else
            {
                name = e.GetString() ?? "";
            }
            if (!string.IsNullOrWhiteSpace(name)) { MongoDbHelper.AddPrintEngineWithIp(name, ip); count++; }
        }

        return Results.Json(new { ok = true, count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/print-engines/{name}", (HttpContext ctx, string name) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        MongoDbHelper.RemovePrintEngine(name);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Work Types
// ======================================================

app.MapGet("/api/config/work-types", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        var types = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => d.Contains("name") ? d["name"].AsString : "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .OrderBy(s => s)
            .ToList();
        return Results.Json(types);
    }
    catch (Exception) { return Results.Json(new string[0]); }
});

app.MapPost("/api/config/work-types/import", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null) return Results.Json(new { ok = false, error = "Fichier manquant" });

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        int count = 0;
        foreach (var line in lines)
        {
            var name = line.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(name)) continue;
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var existing = col.Find(filter).FirstOrDefault();
            if (existing == null)
            {
                col.InsertOne(new BsonDocument { ["name"] = name });
                count++;
            }
        }
        return Results.Json(new { ok = true, count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/work-types/{name}", (string name) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("name", Uri.UnescapeDataString(name)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Hotfolder Routing (type de travail → chemin hotfolder PrismaPrepare)
// ======================================================

app.MapGet("/api/config/hotfolder-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] hotfolder-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/hotfolder-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/hotfolder-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("hotfolderRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", Uri.UnescapeDataString(typeTravail)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Fiery Routing (type de travail → hotfolder Fiery)
// ======================================================

app.MapGet("/api/config/fiery-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] fiery-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/fiery-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/fiery-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("fieryRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", Uri.UnescapeDataString(typeTravail)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — PrismaSync Routing (presse/moteur → workflow)
// ======================================================

app.MapGet("/api/config/prismasync-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                _id = d["_id"].ToString(),
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                moteurImpression = d.Contains("moteurImpression") ? d["moteurImpression"].AsString : "",
                media1 = d.Contains("media1") ? d["media1"].AsString : "",
                media2 = d.Contains("media2") ? d["media2"].AsString : "",
                media3 = d.Contains("media3") ? d["media3"].AsString : "",
                media4 = d.Contains("media4") ? d["media4"].AsString : "",
                prismaSyncPath = d.Contains("prismaSyncPath") ? d["prismaSyncPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail) && !string.IsNullOrEmpty(r.moteurImpression))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] prismasync-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/prismasync-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var id = json.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : "";
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var moteurImpression = json.TryGetProperty("moteurImpression", out var mi) ? mi.GetString() ?? "" : "";
        var media1 = json.TryGetProperty("media1", out var m1) ? m1.GetString() ?? "" : "";
        var media2 = json.TryGetProperty("media2", out var m2) ? m2.GetString() ?? "" : "";
        var media3 = json.TryGetProperty("media3", out var m3) ? m3.GetString() ?? "" : "";
        var media4 = json.TryGetProperty("media4", out var m4) ? m4.GetString() ?? "" : "";
        var prismaSyncPath = json.TryGetProperty("prismaSyncPath", out var psp) ? psp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail) && string.IsNullOrEmpty(moteurImpression))
            return Results.Json(new { ok = false, error = "typeTravail ou moteurImpression manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var doc = new BsonDocument
        {
            ["typeTravail"] = typeTravail,
            ["moteurImpression"] = moteurImpression,
            ["media1"] = media1,
            ["media2"] = media2,
            ["media3"] = media3,
            ["media4"] = media4,
            ["prismaSyncPath"] = prismaSyncPath
        };
        if (!string.IsNullOrEmpty(id) && MongoDB.Bson.ObjectId.TryParse(id, out var oid))
        {
            var filter = Builders<BsonDocument>.Filter.Eq("_id", oid);
            col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = false });
        }
        else
        {
            col.InsertOne(doc);
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/prismasync-routing/{id}", (string id) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaSyncRouting");
        var decodedId = Uri.UnescapeDataString(id);
        if (MongoDB.Bson.ObjectId.TryParse(decodedId, out var oid))
        {
            col.DeleteMany(Builders<BsonDocument>.Filter.Eq("_id", oid));
        }
        else
        {
            // Fallback: legacy delete by printEngine field
            col.DeleteMany(Builders<BsonDocument>.Filter.Eq("printEngine", decodedId));
        }
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Direct Print Routing (type de travail + moteur → hotfolder)
// ======================================================

app.MapGet("/api/config/direct-print-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var docs = col.Find(FilterDefinition<BsonDocument>.Empty).ToList()
            .Select(d => new
            {
                typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
                printEngine = d.Contains("printEngine") ? d["printEngine"].AsString : "",
                hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
            })
            .Where(r => !string.IsNullOrEmpty(r.typeTravail))
            .ToList();
        return Results.Json(docs);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] direct-print-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/direct-print-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var printEngine = json.TryGetProperty("printEngine", out var pe) ? pe.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(typeTravail))
            return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
            Builders<BsonDocument>.Filter.Eq("printEngine", printEngine));
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["printEngine"] = printEngine, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/direct-print-routing", async (HttpContext ctx) =>
{
    try
    {
        var typeTravail = ctx.Request.Query["typeTravail"].ToString();
        var printEngine = ctx.Request.Query["printEngine"].ToString();
        var col = MongoDbHelper.GetCollection<BsonDocument>("directPrintRouting");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail),
            Builders<BsonDocument>.Filter.Eq("printEngine", printEngine));
        col.DeleteMany(filter);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ROUTAGE PRISMA PREPARE (action "Ouvrir dans PrismaPrepare")
// ======================================================

app.MapGet("/api/config/prisma-prepare-routing", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        var docs = col.Find(new BsonDocument()).ToList();
        var result = docs.Select(d => new {
            typeTravail = d.Contains("typeTravail") ? d["typeTravail"].AsString : "",
            hotfolderPath = d.Contains("hotfolderPath") ? d["hotfolderPath"].AsString : ""
        });
        return Results.Json(result);
    }
    catch (Exception ex) { Console.WriteLine($"[ERR] prisma-prepare-routing GET: {ex.Message}"); return Results.Json(new object[0]); }
});

app.MapPut("/api/config/prisma-prepare-routing", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var typeTravail = json.TryGetProperty("typeTravail", out var tt) ? tt.GetString() ?? "" : "";
        var hotfolderPath = json.TryGetProperty("hotfolderPath", out var hp) ? hp.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(typeTravail)) return Results.Json(new { ok = false, error = "typeTravail manquant" });
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        var filter = Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail);
        var doc = new BsonDocument { ["typeTravail"] = typeTravail, ["hotfolderPath"] = hotfolderPath };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/prisma-prepare-routing/{typeTravail}", (string typeTravail) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("prismaPrepareRouting");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("typeTravail", typeTravail));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// PRINT — Send to print (Fiery / PrismaSync / Direct)
// ======================================================


app.MapGet("/api/config/paper-catalog", () =>
{
    try
    {
        // Look for Paper Catalog.xml in app directory or common locations
        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Paper Catalog.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Paper Catalog.xml"),
            Path.Combine(BackendUtils.HotfoldersRoot(), "..", "Paper Catalog.xml"),
            "Paper Catalog.xml"
        };

        string? xmlPath = searchPaths.FirstOrDefault(p => File.Exists(p));
        if (xmlPath == null)
            return Results.Json(new string[0]);

        // Load XML with secure settings to prevent XXE attacks
        var xmlSettings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null
        };
        XDocument doc;
        using (var xmlReader = System.Xml.XmlReader.Create(xmlPath, xmlSettings))
        {
            doc = XDocument.Load(xmlReader);
        }

        // JDF format (Fiery/EFI Paper Catalog): <Media DescriptiveName="..." />
        var names = doc.Descendants()
            .Where(el => el.Name.LocalName == "Media")
            .Select(el => (string?)(el.Attribute("DescriptiveName") ?? el.Attribute("descriptiveName")))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (!names.Any())
        {
            // Fallback: try CatalogEntry/Paper/Entry elements with Name/name attribute
            names = doc.Descendants()
                .Where(el => el.Name.LocalName == "CatalogEntry" || el.Name.LocalName == "Paper" || el.Name.LocalName == "Entry")
                .Select(el => (string?)(el.Attribute("Name") ?? el.Attribute("name") ?? el.Attribute("mediaName") ?? el.Attribute("MediaName")))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        if (!names.Any())
        {
            // Last resort: all leaf text content
            names = doc.Descendants()
                .Where(el => !el.HasElements && !string.IsNullOrWhiteSpace(el.Value))
                .Select(el => el.Value.Trim())
                .Where(n => n.Length > 0 && n.Length < 200)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        return Results.Json(names);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] Paper catalog parse error: {ex.Message}");
        return Results.Json(new string[0]);
    }
});

// ======================================================
// ADMIN — Activity Logs
// ======================================================


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

// ======================================================
// ADMIN — Logs
// ======================================================

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

// ======================================================
// ADMIN — Stats (Dashboard)
// ======================================================

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

// ======================================================
// ACROBAT — Complete processing
// ======================================================

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

// ======================================================
// COMMANDS — BAT, Print, Send etc.
// ======================================================

app.MapGet("/api/config/action-buttons", () =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("actionButtonsConfig");
    var doc = col.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefault();
    var defaults = new {
        controller = @"C:\Program Files\Canon\PRISMACore\PrismaSync.exe",
        prismaPrepare = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        print = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        modification = @"C:\Program Files\Canon\PRISMACore\PRISMAprepare.exe",
        fiery = @"C:\FieryHotfolder"
    };
    if (doc == null) return Results.Json(new { ok = true, buttons = defaults });
    return Results.Json(new {
        ok = true,
        buttons = new {
            controller = doc.Contains("controller") ? doc["controller"].AsString : defaults.controller,
            prismaPrepare = doc.Contains("prismaPrepare") ? doc["prismaPrepare"].AsString : defaults.prismaPrepare,
            print = doc.Contains("print") ? doc["print"].AsString : defaults.print,
            modification = doc.Contains("modification") ? doc["modification"].AsString : defaults.modification,
            fiery = doc.Contains("fiery") ? doc["fiery"].AsString : defaults.fiery
        }
    });
});

app.MapPut("/api/config/action-buttons", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var buttons = json.TryGetProperty("buttons", out var b) ? b : default;
    var doc = new BsonDocument();
    if (buttons.ValueKind == JsonValueKind.Object)
    {
        if (buttons.TryGetProperty("controller", out var v1)) doc["controller"] = v1.GetString() ?? "";
        if (buttons.TryGetProperty("prismaPrepare", out var v2)) doc["prismaPrepare"] = v2.GetString() ?? "";
        if (buttons.TryGetProperty("print", out var v3)) doc["print"] = v3.GetString() ?? "";
        if (buttons.TryGetProperty("modification", out var v4)) doc["modification"] = v4.GetString() ?? "";
        if (buttons.TryGetProperty("fiery", out var v5)) doc["fiery"] = v5.GetString() ?? "";
    }
    var col = MongoDbHelper.GetCollection<BsonDocument>("actionButtonsConfig");
    col.ReplaceOne(Builders<BsonDocument>.Filter.Empty, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// DELETE PRODUCTION FOLDER
// ======================================================
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

// ======================================================
// NOTIFICATIONS

// ======================================================
// PREFLIGHT SETTINGS
// ======================================================
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
                var path = d.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                existing.Droplets.Add(new DropletConfig { Name = name, Path = path });
            }
        }

        MongoDbHelper.UpsertSettings("preflight", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// PREFLIGHT — liste des droplets (accessible à tous)
// ======================================================
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
// KANBAN COLUMNS CONFIG
// ======================================================
static List<KanbanColumnConfig> GetDefaultKanbanColumns() => new()
{
    new KanbanColumnConfig { Folder = "Début de production", Label = "Jobs à traiter",           Color = "#5fa8c4", Visible = true, Order = 0 },
    new KanbanColumnConfig { Folder = "Corrections",         Label = "Preflight",                Color = "#e0e0e0", Visible = true, Order = 1 },
    new KanbanColumnConfig { Folder = "Corrections et fond perdu", Label = "Preflight avec fond perdu", Color = "#e0e0e0", Visible = true, Order = 2 },
    new KanbanColumnConfig { Folder = "Prêt pour impression", Label = "En attente",              Color = "#b8b8b8", Visible = true, Order = 3 },
    new KanbanColumnConfig { Folder = "PrismaPrepare",        Label = "PrismaPrepare",            Color = "#8f8f8f", Visible = true, Order = 4 },
    new KanbanColumnConfig { Folder = "Fiery",                Label = "Fiery",                    Color = "#8f8f8f", Visible = true, Order = 5 },
    new KanbanColumnConfig { Folder = "Impression en cours",  Label = "Impression en cours",      Color = "#7a7a7a", Visible = true, Order = 6 },
    new KanbanColumnConfig { Folder = "Façonnage",            Label = "Façonnage",                Color = "#666666", Visible = true, Order = 7 },
    new KanbanColumnConfig { Folder = "Fin de production",    Label = "Fin de production",        Color = "#22c55e", Visible = true, Order = 8 },
};

app.MapGet("/api/config/kanban-columns", () =>
{
    try
    {
        var cfg = MongoDbHelper.GetSettings<KanbanSettings>("kanbanColumns");
        if (cfg == null || cfg.Columns == null || cfg.Columns.Count == 0)
            return Results.Json(new { ok = true, columns = GetDefaultKanbanColumns() });
        return Results.Json(new { ok = true, columns = cfg.Columns });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/kanban-columns", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("columns", out var colsEl) || colsEl.ValueKind != JsonValueKind.Array)
            return Results.Json(new { ok = false, error = "columns manquant" });

        var columns = new List<KanbanColumnConfig>();
        foreach (var c in colsEl.EnumerateArray())
        {
            columns.Add(new KanbanColumnConfig
            {
                Folder  = c.TryGetProperty("folder",  out var f)   ? f.GetString()  ?? "" : "",
                Label   = c.TryGetProperty("label",   out var l)   ? l.GetString()  ?? "" : "",
                Color   = c.TryGetProperty("color",   out var col) ? col.GetString() ?? "#8f8f8f" : "#8f8f8f",
                Visible = c.TryGetProperty("visible", out var v)   ? v.GetBoolean() : true,
                Order   = c.TryGetProperty("order",   out var o)   ? o.GetInt32()   : 0,
            });
        }

        MongoDbHelper.UpsertSettings("kanbanColumns", new KanbanSettings { Columns = columns });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// KEY DATES OFFSETS (Retrait/Livraison)
// ======================================================

app.MapGet("/api/config/key-dates-offsets", () =>
{
    try
    {
        var config = MongoDbHelper.GetSettings<KeyDatesOffsetsSettings>("keyDatesOffsets");
        if (config == null)
        {
            config = new KeyDatesOffsetsSettings(); // valeurs par défaut
        }
        return Results.Json(new { ok = true, config });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/key-dates-offsets", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var settings = await ctx.Request.ReadFromJsonAsync<KeyDatesOffsetsSettings>();
        if (settings == null) return Results.BadRequest(new { ok = false, error = "Settings invalid." });
        MongoDbHelper.UpsertSettings("keyDatesOffsets", settings);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

    }
}
