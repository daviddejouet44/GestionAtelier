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

public static class KanbanConfigEndpoints
{
    private static List<KanbanColumnConfig> GetDefaultKanbanColumns() => new()
    {
        new KanbanColumnConfig { Folder = "Début de production", Label = "Jobs à traiter",           Color = "#5fa8c4", Visible = true, Order = 0 },
        new KanbanColumnConfig { Folder = "Corrections",         Label = "Preflight",                Color = "#e0e0e0", Visible = true, Order = 1 },
        new KanbanColumnConfig { Folder = "Corrections et fond perdu", Label = "Preflight avec fond perdu", Color = "#e0e0e0", Visible = true, Order = 2 },
        new KanbanColumnConfig { Folder = "Prêt pour impression", Label = "En attente",              Color = "#b8b8b8", Visible = true, Order = 3 },
        new KanbanColumnConfig { Folder = "PrismaPrepare",        Label = "PrismaPrepare",            Color = "#8f8f8f", Visible = true, Order = 4 },
        new KanbanColumnConfig { Folder = "Fiery",                Label = "Fiery",                    Color = "#8f8f8f", Visible = true, Order = 5 },
        new KanbanColumnConfig { Folder = "Impression en cours",  Label = "Impression en cours",      Color = "#7a7a7a", Visible = true, Order = 6 },
        new KanbanColumnConfig { Folder = "Façonnage",            Label = "Finitions",                Color = "#666666", Visible = true, Order = 7 },
        new KanbanColumnConfig { Folder = "Fin de production",    Label = "Fin de production",        Color = "#22c55e", Visible = true, Order = 8 },
    };

    public static void MapKanbanConfigEndpoints(this WebApplication app, string recyclePath)
    {
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
            var fp = c.TryGetProperty("folderPath", out var fpEl) ? fpEl.GetString() : null;
            List<string>? visibleActions = null;
            if (c.TryGetProperty("visibleActions", out var vaEl) && vaEl.ValueKind == JsonValueKind.Array)
            {
                visibleActions = new List<string>();
                foreach (var a in vaEl.EnumerateArray())
                {
                    var s = a.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) visibleActions.Add(s!);
                }
            }
            List<string>? emailTemplateKeys = null;
            if (c.TryGetProperty("emailTemplateKeys", out var etEl) && etEl.ValueKind == JsonValueKind.Array)
            {
                emailTemplateKeys = new List<string>();
                foreach (var a in etEl.EnumerateArray())
                {
                    var s = a.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) emailTemplateKeys.Add(s!);
                }
            }
            columns.Add(new KanbanColumnConfig
            {
                Folder          = c.TryGetProperty("folder",  out var f)   ? f.GetString()  ?? "" : "",
                FolderPath      = string.IsNullOrWhiteSpace(fp) ? null : fp,
                Label           = c.TryGetProperty("label",   out var l)   ? l.GetString()  ?? "" : "",
                Color           = c.TryGetProperty("color",   out var col) ? col.GetString() ?? "#8f8f8f" : "#8f8f8f",
                Visible         = c.TryGetProperty("visible", out var v)   ? v.GetBoolean() : true,
                Order           = c.TryGetProperty("order",   out var o)   ? o.GetInt32()   : 0,
                VisibleActions  = visibleActions,
                EmailTemplateKeys = emailTemplateKeys,
            });
        }

        MongoDbHelper.UpsertSettings("kanbanColumns", new KanbanSettings { Columns = columns });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

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

    }
}
