using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

public static class PortalAccountEndpoints
{
    public static void MapPortalAccountEndpoints(this WebApplication app)
    {
        // GET /api/portal/me
        app.MapGet("/api/portal/me", (HttpContext ctx) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });
            return Results.Json(new
            {
                ok = true,
                client = new
                {
                    id = client.Id,
                    email = client.Email,
                    displayName = client.DisplayName,
                    companyName = client.CompanyName,
                    contactPhone = client.ContactPhone,
                    defaultDeliveryAddress = client.DefaultDeliveryAddress
                }
            });
        });

        // PUT /api/portal/me
        app.MapPut("/api/portal/me", async (HttpContext ctx) =>
        {
            try
            {
                var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
                if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");

                var updateBuilder = Builders<BsonDocument>.Update;
                var updates = new List<UpdateDefinition<BsonDocument>>();

                if (json.TryGetProperty("displayName", out var dnEl))
                    updates.Add(updateBuilder.Set("displayName", dnEl.GetString() ?? ""));
                if (json.TryGetProperty("companyName", out var cnEl))
                    updates.Add(updateBuilder.Set("companyName", cnEl.GetString() ?? ""));
                if (json.TryGetProperty("contactPhone", out var cpEl))
                    updates.Add(updateBuilder.Set("contactPhone", cpEl.GetString() ?? ""));
                if (json.TryGetProperty("defaultDeliveryAddress", out var ddaEl))
                    updates.Add(updateBuilder.Set("defaultDeliveryAddress", ddaEl.GetString() ?? ""));

                // Password change
                if (json.TryGetProperty("currentPassword", out var cpwdEl) && json.TryGetProperty("newPassword", out var npwdEl))
                {
                    var currentPwd = cpwdEl.GetString() ?? "";
                    var newPwd = npwdEl.GetString() ?? "";

                    if (!BCrypt.Net.BCrypt.Verify(currentPwd, client.PasswordHash))
                        return Results.Json(new { ok = false, error = "Mot de passe actuel incorrect" });

                    if (newPwd.Length < 8)
                        return Results.Json(new { ok = false, error = "Le nouveau mot de passe doit contenir au moins 8 caractères" });

                    updates.Add(updateBuilder.Set("passwordHash", BCrypt.Net.BCrypt.HashPassword(newPwd)));
                }

                if (updates.Count > 0)
                {
                    col.UpdateOne(
                        Builders<BsonDocument>.Filter.Eq("id", client.Id),
                        updateBuilder.Combine(updates));
                }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });
    }
}
