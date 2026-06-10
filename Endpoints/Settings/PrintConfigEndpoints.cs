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

namespace GestionAtelier.Endpoints.Settings;

public static class PrintConfigEndpoints
{
    public static void MapPrintConfigEndpoints(this WebApplication app, string recyclePath)
    {
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
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return Results.Json(new { ok = false, error = "name requis" });

        var ip = json.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "" : "";
        MongoDbHelper.AddPrintEngineWithIp(nameEl.GetString()!, ip);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapPost("/api/config/print-engines/import", async (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
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
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapDelete("/api/config/print-engines/{name}", (HttpContext ctx, string name) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        MongoDbHelper.RemovePrintEngine(name);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

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
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapDelete("/api/config/work-types/{name}", (string name) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
        col.DeleteMany(Builders<BsonDocument>.Filter.Eq("name", Uri.UnescapeDataString(name)));
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

app.MapGet("/api/config/paper-catalog", () =>
{
    try
    {
        var names = new List<string>();

        // Load from XML catalog
        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "Paper Catalog.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "Paper Catalog.xml"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Paper Catalog.xml"),
            Path.Combine(Directory.GetCurrentDirectory(), "Paper Catalog.xml"),
            Path.Combine(BackendUtils.HotfoldersRoot(), "..", "Paper Catalog.xml"),
            "Paper Catalog.xml"
        };

        string? xmlPath = searchPaths.FirstOrDefault(p => File.Exists(p));
        if (xmlPath != null)
        {
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

            var xmlNames = doc.Descendants()
                .Where(el => el.Name.LocalName == "Media")
                .Select(el => (string?)(el.Attribute("DescriptiveName") ?? el.Attribute("descriptiveName")))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .ToList();

            if (!xmlNames.Any())
            {
                xmlNames = doc.Descendants()
                    .Where(el => el.Name.LocalName == "CatalogEntry" || el.Name.LocalName == "Paper" || el.Name.LocalName == "Entry")
                    .Select(el => (string?)(el.Attribute("Name") ?? el.Attribute("name") ?? el.Attribute("mediaName") ?? el.Attribute("MediaName")))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .ToList();
            }

            names.AddRange(xmlNames);
        }

        // Merge with custom MongoDB papers
        var customCatalog = MongoDbHelper.GetSettings<CustomPaperCatalog>("customPaperCatalog");
        if (customCatalog?.Papers != null)
            names.AddRange(customCatalog.Papers.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)));

        return Results.Json(names.Distinct().OrderBy(n => n).ToList());
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[WARN] Paper catalog parse error: {ex.Message}");
        return Results.Json(new string[0]);
    }
});

// GET /api/config/paper-catalog/custom — list only custom (MongoDB) papers
app.MapGet("/api/config/paper-catalog/custom", (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
        var catalog = MongoDbHelper.GetSettings<CustomPaperCatalog>("customPaperCatalog")
            ?? new CustomPaperCatalog();
        return Results.Json(new { ok = true, papers = catalog.Papers });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

// POST /api/config/paper-catalog/add — add a paper manually
app.MapPost("/api/config/paper-catalog/add", async (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
        var entry = await ctx.Request.ReadFromJsonAsync<CustomPaperEntry>();
        if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            return Results.Json(new { ok = false, error = "Nom de papier requis" });

        var catalog = MongoDbHelper.GetSettings<CustomPaperCatalog>("customPaperCatalog")
            ?? new CustomPaperCatalog();

        if (catalog.Papers.Any(p => p.Name == entry.Name))
            return Results.Json(new { ok = false, error = "Ce papier existe déjà dans le catalogue personnalisé" });

        catalog.Papers.Add(entry);
        MongoDbHelper.UpsertSettings("customPaperCatalog", catalog);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

// POST /api/config/paper-catalog/import-csv — import papers from CSV
app.MapPost("/api/config/paper-catalog/import-csv", async (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null) return Results.Json(new { ok = false, error = "Fichier manquant" });

        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var catalog = MongoDbHelper.GetSettings<CustomPaperCatalog>("customPaperCatalog")
            ?? new CustomPaperCatalog();

        int added = 0, skipped = 0;
        // CSV format: Name[;Grammage[;Format[;Fabricant[;Notes]]]]
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            // Support both comma and semicolon as separator
            var sep = line.Contains(';') ? ';' : ',';
            var cols = line.Split(sep).Select(c => c.Trim().Trim('"')).ToArray();
            var name = cols.Length > 0 ? cols[0] : "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (catalog.Papers.Any(p => p.Name == name)) { skipped++; continue; }

            catalog.Papers.Add(new CustomPaperEntry
            {
                Name      = name,
                Grammage  = cols.Length > 1 ? cols[1] : null,
                Format    = cols.Length > 2 ? cols[2] : null,
                Fabricant = cols.Length > 3 ? cols[3] : null,
                Notes     = cols.Length > 4 ? cols[4] : null
            });
            added++;
        }

        MongoDbHelper.UpsertSettings("customPaperCatalog", catalog);
        return Results.Json(new { ok = true, added, skipped });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

// DELETE /api/config/paper-catalog/custom/{name} — remove a custom paper
app.MapDelete("/api/config/paper-catalog/custom/{name}", (HttpContext ctx, string name) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
        var decoded = Uri.UnescapeDataString(name);
        var catalog = MongoDbHelper.GetSettings<CustomPaperCatalog>("customPaperCatalog")
            ?? new CustomPaperCatalog();
        var before = catalog.Papers.Count;
        catalog.Papers.RemoveAll(p => p.Name == decoded);
        MongoDbHelper.UpsertSettings("customPaperCatalog", catalog);
        return Results.Json(new { ok = true, removed = before - catalog.Papers.Count });
    }
    catch (Exception ex) { return ErrorHelper.HandleException(ex); }
});

    }

}
