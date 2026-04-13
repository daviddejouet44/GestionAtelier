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

public static class RoutingEndpoints
{
    public static void MapRoutingEndpoints(this WebApplication app, string recyclePath)
    {
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

    }
}
