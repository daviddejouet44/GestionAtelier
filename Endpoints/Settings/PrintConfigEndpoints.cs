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

app.MapGet("/api/config/paper-catalog", () =>
{
    try
    {
        // Look for Paper Catalog.xml in app directory or common locations
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

    }
}
