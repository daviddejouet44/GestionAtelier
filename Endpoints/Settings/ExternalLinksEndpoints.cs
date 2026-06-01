using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Settings;

public static class ExternalLinksEndpoints
{
    public static void MapExternalLinksEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/external-links", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3)
                    return Results.Json(new { ok = false, error = "Token invalide" });

                var users = BackendUtils.LoadUsers();
                if (!users.Any(u => u.Id == parts[0]))
                    return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

                var cfg = MongoDbHelper.GetSettings<ExternalLinksSettings>("externalLinks") ?? new ExternalLinksSettings();
                return Results.Json(new
                {
                    ok = true,
                    remoteManagerUrl = cfg.RemoteManagerUrl ?? "",
                    primalyticsUrl = cfg.PrismalyticsUrl ?? ""
                });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        app.MapPut("/api/settings/external-links", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3 || parts[2] != "3")
                    return Results.Json(new { ok = false, error = "Admin only" });

                var users = BackendUtils.LoadUsers();
                if (!users.Any(u => u.Id == parts[0]))
                    return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg = MongoDbHelper.GetSettings<ExternalLinksSettings>("externalLinks") ?? new ExternalLinksSettings();

                if (json.TryGetProperty("remoteManagerUrl", out var remoteEl))
                    cfg.RemoteManagerUrl = remoteEl.GetString()?.Trim() ?? "";
                if (json.TryGetProperty("primalyticsUrl", out var prismaEl))
                    cfg.PrismalyticsUrl = prismaEl.GetString()?.Trim() ?? "";

                MongoDbHelper.UpsertSettings("externalLinks", cfg);

                return Results.Json(new
                {
                    ok = true,
                    remoteManagerUrl = cfg.RemoteManagerUrl ?? "",
                    primalyticsUrl = cfg.PrismalyticsUrl ?? ""
                });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }
}
