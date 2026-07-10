using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

// ======================================================
// Config des priorités (planification intelligente — point 2)
// GET /api/config/priority
// PUT /api/config/priority  (admin)
// ======================================================
public static class PriorityConfigEndpoints
{
    public static void MapPriorityConfigEndpoints(this WebApplication app, string recyclePath)
    {
        app.MapGet("/api/config/priority", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var cfg = MongoDbHelper.GetSettings<PriorityConfig>("priorityConfig") ?? new PriorityConfig();
                return Results.Json(new { ok = true, config = cfg });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/config/priority", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var cfg = await ctx.Request.ReadFromJsonAsync<PriorityConfig>();
                if (cfg == null)
                    return Results.Json(new { ok = false, error = "Configuration invalide" });

                cfg.WeightUrgent     = Math.Max(0, cfg.WeightUrgent);
                cfg.WeightVip        = Math.Max(0, cfg.WeightVip);
                cfg.WeightRetard     = Math.Max(0, cfg.WeightRetard);
                cfg.WeightModif      = Math.Max(0, cfg.WeightModif);
                cfg.ModifWindowHours = Math.Clamp(cfg.ModifWindowHours, 0, 720);
                cfg.VipClients ??= new();

                MongoDbHelper.UpsertSettings("priorityConfig", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
