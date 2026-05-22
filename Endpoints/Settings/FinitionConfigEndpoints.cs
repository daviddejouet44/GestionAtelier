using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

public static class FinitionConfigEndpoints
{
    public static void MapFinitionConfigEndpoints(this WebApplication app)
    {
        // ── Finition time rules ─────────────────────────────────────────────────

        app.MapGet("/api/settings/finition-time-rules", (HttpContext ctx) =>
        {
            try
            {
                var cfg = MongoDbHelper.GetSettings<FinitionTimeConfig>("finitionTimeRules")
                    ?? new FinitionTimeConfig();
                return Results.Json(cfg);
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        app.MapPut("/api/settings/finition-time-rules", async (HttpContext ctx) =>
        {
            try
            {
                if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
                var cfg = await ctx.Request.ReadFromJsonAsync<FinitionTimeConfig>();
                if (cfg == null) return Results.Json(new { ok = false, error = "Payload invalide" });
                MongoDbHelper.UpsertSettings("finitionTimeRules", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── Finition sheet formulas ─────────────────────────────────────────────

        app.MapGet("/api/settings/finition-sheet-formulas", (HttpContext ctx) =>
        {
            try
            {
                var cfg = MongoDbHelper.GetSettings<FinitionSheetFormulaConfig>("finitionSheetFormulas")
                    ?? new FinitionSheetFormulaConfig();
                return Results.Json(cfg);
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        app.MapPut("/api/settings/finition-sheet-formulas", async (HttpContext ctx) =>
        {
            try
            {
                if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
                var cfg = await ctx.Request.ReadFromJsonAsync<FinitionSheetFormulaConfig>();
                if (cfg == null) return Results.Json(new { ok = false, error = "Payload invalide" });
                MongoDbHelper.UpsertSettings("finitionSheetFormulas", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── Rainage options ─────────────────────────────────────────────────────

        app.MapGet("/api/settings/rainage-options", (HttpContext ctx) =>
        {
            try
            {
                var cfg = MongoDbHelper.GetSettings<RainageOptionsConfig>("rainageOptions")
                    ?? new RainageOptionsConfig();
                return Results.Json(cfg);
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        app.MapPut("/api/settings/rainage-options", async (HttpContext ctx) =>
        {
            try
            {
                if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin uniquement" });
                var cfg = await ctx.Request.ReadFromJsonAsync<RainageOptionsConfig>();
                if (cfg == null) return Results.Json(new { ok = false, error = "Payload invalide" });
                MongoDbHelper.UpsertSettings("rainageOptions", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }

    private static bool IsAdmin(HttpContext ctx)
    {
        try
        {
            var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            return parts.Length >= 3 && parts[2] == "3";
        }
        catch { return false; }
    }
}
