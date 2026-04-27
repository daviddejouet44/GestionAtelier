using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

public static class ReportsEndpoints
{
    public static void MapReportsEndpoints(this WebApplication app)
    {
// ======================================================
// GET /api/settings/report-config
// ======================================================
app.MapGet("/api/settings/report-config", () =>
{
    try
    {
        var config = DailyReportService.LoadConfig();
        return Results.Json(new { ok = true, config });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// PUT /api/settings/report-config
// ======================================================
app.MapPut("/api/settings/report-config", async (HttpContext ctx) =>
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

        var cfg = await ctx.Request.ReadFromJsonAsync<DailyReportConfig>() ?? new DailyReportConfig();
        MongoDbHelper.UpsertSettings("dailyReportConfig", cfg);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// POST /api/reports/generate-now
// Force immediate report generation
// ======================================================
app.MapPost("/api/reports/generate-now", async (HttpContext ctx) =>
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

        await DailyReportService.GenerateReportsAsync();
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// GET /api/reports/history
// Returns the last generated reports
// ======================================================
app.MapGet("/api/reports/history", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("dailyReports");
        var docs = col.Find(new BsonDocument())
            .SortByDescending(x => x["generatedAt"])
            .Limit(30)
            .ToList();

        var items = docs.Select(d => new
        {
            generatedAt     = d.Contains("generatedAt") ? d["generatedAt"].ToUniversalTime() : (DateTime?)null,
            machinesReport  = d.Contains("machinesReport") ? d["machinesReport"].AsString : "",
            finitionsReport = d.Contains("finitionsReport") ? d["finitionsReport"].AsString : ""
        }).ToArray();

        return Results.Json(new { ok = true, items });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});
    }
}
