using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

// ======================================================
// Config des coûts de calage (planification intelligente)
// GET  /api/config/changeover-costs
// PUT  /api/config/changeover-costs  (admin)
// ======================================================
public static class ChangeoverConfigEndpoints
{
    public static void MapChangeoverConfigEndpoints(this WebApplication app, string recyclePath)
    {
        app.MapGet("/api/config/changeover-costs", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var cfg = MongoDbHelper.GetSettings<ChangeoverCostSettings>("changeoverCosts")
                          ?? new ChangeoverCostSettings();
                return Results.Json(new { ok = true, config = cfg });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/config/changeover-costs", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var cfg = await ctx.Request.ReadFromJsonAsync<ChangeoverCostSettings>();
                if (cfg == null)
                    return Results.Json(new { ok = false, error = "Configuration invalide" });

                // Garde-fous : pas de valeurs négatives
                cfg.CalageBaseMinutes       = Math.Max(0, cfg.CalageBaseMinutes);
                cfg.ChangementPapierMinutes = Math.Max(0, cfg.ChangementPapierMinutes);
                cfg.ChangementFormatMinutes = Math.Max(0, cfg.ChangementFormatMinutes);
                cfg.Engines ??= new();
                foreach (var e in cfg.Engines)
                {
                    if (e.CalageBaseMinutes.HasValue)       e.CalageBaseMinutes       = Math.Max(0, e.CalageBaseMinutes.Value);
                    if (e.ChangementPapierMinutes.HasValue) e.ChangementPapierMinutes = Math.Max(0, e.ChangementPapierMinutes.Value);
                    if (e.ChangementFormatMinutes.HasValue) e.ChangementFormatMinutes = Math.Max(0, e.ChangementFormatMinutes.Value);
                }

                MongoDbHelper.UpsertSettings("changeoverCosts", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
