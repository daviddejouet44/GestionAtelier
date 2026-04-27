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

public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this WebApplication app, string recyclePath)
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
// KEY DATES OFFSETS — Offsets pour calcul automatique des dates clés
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
