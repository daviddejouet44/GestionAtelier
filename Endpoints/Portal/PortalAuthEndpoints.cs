using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

public static class PortalAuthEndpoints
{
    // Helpers ----------------------------------------------------------------

    /// <summary>Generates a portal JWT-like token: base64("portal:{clientId}:{email}")</summary>
    public static string MakePortalToken(string clientId, string email)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"portal:{clientId}:{email}"));

    /// <summary>Parses a portal token and returns (clientId, email) or null on failure.</summary>
    public static (string clientId, string email)? ParsePortalToken(string rawToken)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(rawToken));
            var parts = decoded.Split(':');
            if (parts.Length < 3 || parts[0] != "portal") return null;
            return (parts[1], parts[2]);
        }
        catch { return null; }
    }

    /// <summary>Resolves the authenticated client from the Authorization header or returns null.</summary>
    public static ClientAccount? GetAuthenticatedClient(HttpContext ctx)
    {
        var raw = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parsed = ParsePortalToken(raw);
        if (parsed == null) return null;

        var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", parsed.Value.clientId)).FirstOrDefault();
        if (doc == null) return null;

        return DocToClient(doc);
    }

    // Mapper -----------------------------------------------------------------
    public static ClientAccount DocToClient(BsonDocument d) => new()
    {
        Id = d.Contains("id") ? d["id"].AsString : "",
        Email = d.Contains("email") ? d["email"].AsString : "",
        PasswordHash = d.Contains("passwordHash") ? d["passwordHash"].AsString : "",
        DisplayName = d.Contains("displayName") ? d["displayName"].AsString : "",
        CompanyName = d.Contains("companyName") ? d["companyName"].AsString : "",
        ContactPhone = d.Contains("contactPhone") ? d["contactPhone"].AsString : "",
        DefaultDeliveryAddress = d.Contains("defaultDeliveryAddress") ? d["defaultDeliveryAddress"].AsString : "",
        Enabled = !d.Contains("enabled") || d["enabled"].AsBoolean,
        CreatedAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : DateTime.UtcNow,
        LastLoginAt = d.Contains("lastLoginAt") && !d["lastLoginAt"].IsBsonNull ? d["lastLoginAt"].ToUniversalTime() : null,
        FailedLoginAttempts = d.Contains("failedLoginAttempts") ? d["failedLoginAttempts"].AsInt32 : 0,
        LockedUntil = d.Contains("lockedUntil") && !d["lockedUntil"].IsBsonNull ? d["lockedUntil"].ToUniversalTime() : null,
        PasswordResetToken = d.Contains("passwordResetToken") && !d["passwordResetToken"].IsBsonNull ? d["passwordResetToken"].AsString : null,
        PasswordResetExpiry = d.Contains("passwordResetExpiry") && !d["passwordResetExpiry"].IsBsonNull ? d["passwordResetExpiry"].ToUniversalTime() : null,
    };

    // Registration -----------------------------------------------------------
    public static void MapPortalAuthEndpoints(this WebApplication app)
    {
        // POST /api/portal/auth/login
        app.MapPost("/api/portal/auth/login", async (HttpContext ctx) =>
        {
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var email = json.TryGetProperty("email", out var eEl) ? eEl.GetString() ?? "" : "";
                var pwd = json.TryGetProperty("password", out var pEl) ? pEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pwd))
                    return Results.Json(new { ok = false, error = "Email et mot de passe requis" });

                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("email", email.ToLowerInvariant())).FirstOrDefault();
                if (doc == null)
                    return Results.Json(new { ok = false, error = "Identifiants invalides" });

                var client = DocToClient(doc);

                if (!client.Enabled)
                    return Results.Json(new { ok = false, error = "Compte désactivé" });

                // Check lock
                if (client.LockedUntil.HasValue && client.LockedUntil.Value > DateTime.UtcNow)
                    return Results.Json(new { ok = false, error = "Compte temporairement verrouillé. Réessayez plus tard." });

                // Verify password
                bool pwdOk = BCrypt.Net.BCrypt.Verify(pwd, client.PasswordHash);
                if (!pwdOk)
                {
                    // Increment failed attempts
                    int attempts = client.FailedLoginAttempts + 1;
                    DateTime? lockUntil = null;
                    if (attempts >= settings.MaxLoginAttempts)
                    {
                        lockUntil = DateTime.UtcNow.AddMinutes(settings.LockDurationMinutes);
                        attempts = 0;
                    }
                    col.UpdateOne(
                        Builders<BsonDocument>.Filter.Eq("id", client.Id),
                        Builders<BsonDocument>.Update
                            .Set("failedLoginAttempts", attempts)
                            .Set("lockedUntil", lockUntil.HasValue ? (BsonValue)lockUntil.Value : BsonNull.Value));
                    return Results.Json(new { ok = false, error = "Identifiants invalides" });
                }

                // Reset failed attempts, update lastLoginAt
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", client.Id),
                    Builders<BsonDocument>.Update
                        .Set("failedLoginAttempts", 0)
                        .Set("lockedUntil", BsonNull.Value)
                        .Set("lastLoginAt", DateTime.UtcNow));

                var token = MakePortalToken(client.Id, client.Email);
                return Results.Json(new
                {
                    ok = true,
                    token,
                    client = new { id = client.Id, email = client.Email, displayName = client.DisplayName, companyName = client.CompanyName }
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // POST /api/portal/auth/logout  (stateless — client just discards token)
        app.MapPost("/api/portal/auth/logout", () => Results.Json(new { ok = true }));

        // GET /api/portal/auth/me
        app.MapGet("/api/portal/auth/me", (HttpContext ctx) =>
        {
            var client = GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });
            return Results.Json(new
            {
                ok = true,
                client = new { id = client.Id, email = client.Email, displayName = client.DisplayName, companyName = client.CompanyName, contactPhone = client.ContactPhone, defaultDeliveryAddress = client.DefaultDeliveryAddress }
            });
        });

        // POST /api/portal/auth/forgot-password
        app.MapPost("/api/portal/auth/forgot-password", async (HttpContext ctx) =>
        {
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var email = json.TryGetProperty("email", out var eEl) ? eEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(email))
                    return Results.Json(new { ok = false, error = "Email requis" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("email", email.ToLowerInvariant())).FirstOrDefault();
                if (doc == null)
                    // Do not reveal whether the account exists
                    return Results.Json(new { ok = true });

                var client = DocToClient(doc);
                var token = Guid.NewGuid().ToString("N");
                var expiry = DateTime.UtcNow.AddHours(2);

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", client.Id),
                    Builders<BsonDocument>.Update
                        .Set("passwordResetToken", token)
                        .Set("passwordResetExpiry", expiry));

                // Send email
                try
                {
                    var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                    var portalUrl = string.IsNullOrWhiteSpace(settings.PortalUrl) ? "" : settings.PortalUrl.TrimEnd('/');
                    var resetLink = $"{portalUrl}/portal/login.html?token={token}";
                    var tpl = MongoDbHelper.GetSettings<PortalEmailTemplate>("portalEmailTemplate_password_reset");
                    var subject = tpl?.Subject ?? "Réinitialisation de votre mot de passe";
                    var body = (tpl?.Body ?? "Bonjour {clientName},\n\nVoici le lien pour réinitialiser votre mot de passe (valable 2h) :\n{resetLink}\n\nCordialement,")
                        .Replace("{clientName}", client.DisplayName)
                        .Replace("{resetLink}", resetLink)
                        .Replace("{portalLink}", portalUrl);
                    PortalEmailHelper.SendEmail(client.Email, subject, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Portal password reset email failed: {ex.Message}");
                }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // POST /api/portal/auth/reset-password
        app.MapPost("/api/portal/auth/reset-password", async (HttpContext ctx) =>
        {
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var token = json.TryGetProperty("token", out var tEl) ? tEl.GetString() ?? "" : "";
                var newPwd = json.TryGetProperty("password", out var pEl) ? pEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPwd))
                    return Results.Json(new { ok = false, error = "Token et nouveau mot de passe requis" });
                if (newPwd.Length < 8)
                    return Results.Json(new { ok = false, error = "Le mot de passe doit contenir au moins 8 caractères" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("passwordResetToken", token)).FirstOrDefault();
                if (doc == null)
                    return Results.Json(new { ok = false, error = "Token invalide ou expiré" });

                var client = DocToClient(doc);
                if (!client.PasswordResetExpiry.HasValue || client.PasswordResetExpiry.Value < DateTime.UtcNow)
                    return Results.Json(new { ok = false, error = "Token invalide ou expiré" });

                var hash = BCrypt.Net.BCrypt.HashPassword(newPwd);
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", client.Id),
                    Builders<BsonDocument>.Update
                        .Set("passwordHash", hash)
                        .Set("passwordResetToken", BsonNull.Value)
                        .Set("passwordResetExpiry", BsonNull.Value));

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });
    }
}
