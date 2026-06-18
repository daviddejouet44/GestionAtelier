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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

public static class AuthEndpointsExtensions
{
    // A user is considered "online" if they sent a heartbeat within this window
    private const int OnlineThresholdMinutes = 5;

    public static void MapAuthEndpoints(this WebApplication app)
    {
app.MapPost("/api/auth/login", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("login", out var loginEl) ||
            !json.TryGetProperty("password", out var pwdEl))
            return Results.Json(new { ok = false, error = "login et password requis" });

        var login = loginEl.GetString() ?? "";
        var pwd = pwdEl.GetString() ?? "";

        var users = BackendUtils.LoadUsers();
        var user = users.FirstOrDefault(u => u.Login == login && u.Password == pwd);

        if (user == null)
        {
            // Point 19: Log failed login attempt
            MongoDbHelper.InsertActivityLog(new ActivityLogEntry
            {
                Timestamp = DateTime.Now,
                UserLogin = login,
                UserName = "?",
                Action = "LOGIN_FAILED",
                Details = $"Tentative de connexion échouée depuis {ctx.Connection.RemoteIpAddress}"
            });
            return Results.Json(new { ok = false, error = "Identifiants invalides" });
        }

        // Check if account is already in use (active session within last 5 minutes)
        // All profiles are subject to single-session enforcement.
        // The "force" flag (passed explicitly) allows taking over an active session.
        var forceLogin = json.TryGetProperty("force", out var forceEl) && forceEl.GetBoolean();
        if (!forceLogin)
        {
            try
            {
                var usersCol = MongoDbHelper.GetUsersCollection();
                var userDoc = usersCol.Find(Builders<BsonDocument>.Filter.Eq("id", user.Id)).FirstOrDefault();
                if (userDoc != null && userDoc.Contains("activeSessionId") && userDoc["activeSessionId"] != BsonNull.Value
                    && userDoc.Contains("lastActivityAt") && userDoc["lastActivityAt"] != BsonNull.Value)
                {
                    var lastActivity = userDoc["lastActivityAt"].ToUniversalTime();
                    if ((DateTime.UtcNow - lastActivity).TotalMinutes < OnlineThresholdMinutes)
                    {
                        var lastActivityStr = lastActivity.ToLocalTime().ToString("HH:mm:ss");
                        return Results.Json(new
                        {
                            ok = false,
                            error = "session_active",
                            message = $"Ce compte est déjà utilisé sur un autre poste (dernière activité à {lastActivityStr}). Forcez la connexion pour déconnecter l'autre session.",
                            lastActivityAt = lastActivity.ToString("o")
                        });
                    }
                }
            }
            catch { /* non-fatal — allow login if check fails */ }
        }

        // Générer un token JWT signé avec les claims utilisateur
        var sessionId = Guid.NewGuid().ToString("N")[..16];
        string token;
        try
        {
            var key = AuthHelper.GetSigningKey();
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new[]
            {
                new Claim("userId",    user.Id),
                new Claim("login",     user.Login),
                new Claim("profile",   user.Profile.ToString()),
                new Claim("name",      user.Name),
                new Claim("sessionId", sessionId)
            };
            var jwtToken = new JwtSecurityToken(
                claims:    claims,
                expires:   DateTime.UtcNow.AddHours(8),
                signingCredentials: credentials);
            token = new JwtSecurityTokenHandler().WriteToken(jwtToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] JWT generation failed: {ex.Message}");
            return Results.Json(new { ok = false, error = "Erreur de génération du token. Vérifiez la configuration JWT_SECRET." });
        }

        // Store active session ID on the user record
        try
        {
            var usersCol = MongoDbHelper.GetUsersCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("id", user.Id);
            var update = Builders<BsonDocument>.Update
                .Set("activeSessionId", sessionId)
                .Set("lastActivityAt", DateTime.UtcNow);
            usersCol.UpdateOne(filter, update);
        }
        catch { /* non-fatal */ }

        Console.WriteLine($"[INFO] Login successful for {user.Login}");

        // Point 19: Log successful login with IP
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = user.Login,
            UserName = user.Name,
            Action = "LOGIN_SUCCESS",
            Details = $"Connexion réussie depuis {ctx.Connection.RemoteIpAddress} (profil {user.Profile})"
        });

        return Results.Json(new
        {
            ok = true,
            token,
            user = new
            {
                id = user.Id,
                login = user.Login,
                profile = user.Profile,
                name = user.Name
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] Exception: {ex.Message}");
        return ErrorHelper.HandleException(ex, "/api/auth/login");
    }
}).RequireRateLimiting("login");

app.MapGet("/api/auth/me", (HttpContext ctx) =>
{
    try
    {
        var principal = AuthHelper.GetPrincipal(ctx);
        if (principal == null)
            return Results.Json(new { ok = false, error = "Non authentifié" });

        var userId = principal.FindFirstValue("userId") ?? "";

        var users = BackendUtils.LoadUsers();
        var user = users.FirstOrDefault(u => u.Id == userId);

        if (user == null)
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        return Results.Json(new
        {
            ok = true,
            user = new
            {
                id = user.Id,
                login = user.Login,
                profile = user.Profile,
                name = user.Name
            }
        });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

// GET /api/operators — list all user accounts (accessible to all authenticated profiles)
app.MapGet("/api/operators", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });

        var users = BackendUtils.LoadUsers();
        var operatorList = users
            .Select(u => new { name = u.Name ?? "", login = u.Login ?? "", profile = u.Profile })
            .ToList();
        Console.WriteLine($"[DEBUG] /api/operators: {operatorList.Count} users loaded");
        return Results.Json(new { ok = true, operators = operatorList });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] /api/operators: {ex.Message}\n{ex.StackTrace}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/auth/users", (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        var users = BackendUtils.LoadUsers();
        var now = DateTime.UtcNow;
        var list = users.Select(u => new
        {
            id = u.Id,
            login = u.Login,
            profile = u.Profile,
            name = u.Name,
            lastActivityAt = u.LastActivityAt,
            online = u.LastActivityAt.HasValue && (now - u.LastActivityAt.Value).TotalMinutes < OnlineThresholdMinutes
        });

        return Results.Json(new { ok = true, users = list });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

app.MapPost("/api/auth/heartbeat", (HttpContext ctx) =>
{
    try
    {
        var principal = AuthHelper.GetPrincipal(ctx);
        if (principal == null) return Results.Json(new { ok = false });

        var userId    = principal.FindFirstValue("userId") ?? "";
        var login     = principal.FindFirstValue("login")  ?? "";
        var sessionId = principal.FindFirstValue("sessionId") ?? "";

        if (!string.IsNullOrEmpty(sessionId))
        {
            var usersCol = MongoDbHelper.GetUsersCollection();
            var userDoc = usersCol.Find(Builders<BsonDocument>.Filter.Eq("id", userId)).FirstOrDefault();
            if (userDoc != null && userDoc.Contains("activeSessionId") && userDoc["activeSessionId"] != BsonNull.Value)
            {
                var activeSession = userDoc["activeSessionId"].AsString;
                if (activeSession != sessionId)
                    return Results.Json(new { ok = false, error = "session_expired", message = "Votre session a été déconnectée car un autre appareil s'est connecté avec ce compte." });
            }
        }

        BackendUtils.UpdateUserActivity(login);
        return Results.Json(new { ok = true });
    }
    catch { return Results.Json(new { ok = false }); }
});

app.MapPut("/api/auth/users/{userId}", async (HttpContext ctx, string userId) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var users = BackendUtils.LoadUsers();
        var user = users.FirstOrDefault(u => u.Id == userId);
        if (user == null)
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        if (json.TryGetProperty("login", out var loginEl) && !string.IsNullOrWhiteSpace(loginEl.GetString()))
        {
            var newLogin = loginEl.GetString()!;
            if (users.Any(u => u.Login == newLogin && u.Id != userId))
                return Results.Json(new { ok = false, error = "Login déjà utilisé" });
            user.Login = newLogin;
        }
        if (json.TryGetProperty("name", out var nameEl)) user.Name = nameEl.GetString() ?? user.Name;
        if (json.TryGetProperty("profile", out var profileEl)) user.Profile = profileEl.GetInt32();
        if (json.TryGetProperty("password", out var pwdEl) && !string.IsNullOrWhiteSpace(pwdEl.GetString()))
            user.Password = pwdEl.GetString()!;

        BackendUtils.UpdateUser(user);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});


app.MapPost("/api/auth/register", async (HttpContext ctx) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("login", out var loginEl) ||
            !json.TryGetProperty("password", out var pwdEl) ||
            !json.TryGetProperty("profile", out var profileEl) ||
            !json.TryGetProperty("name", out var nameEl))
            return Results.BadRequest("login, password, profile, name requis");

        var users = BackendUtils.LoadUsers();
        if (users.Any(u => u.Login == loginEl.GetString()))
            return Results.Json(new { ok = false, error = "Login déjà existant" });

        var newId = MongoDbHelper.GetNextUserId().ToString("D3");
        var newUser = new UserItem
        {
            Id = newId,
            Login = loginEl.GetString() ?? "",
            Password = pwdEl.GetString() ?? "",
            Profile = profileEl.GetInt32(),
            Name = nameEl.GetString() ?? ""
        };

        BackendUtils.InsertUser(newUser);

        // Log account creation
        var creatorLogin = AuthHelper.GetClaim(ctx, "login") ?? "?";
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = creatorLogin,
            UserName = creatorLogin,
            Action = "CREATE_ACCOUNT",
            Details = $"Compte créé : {newUser.Login} (Profil {newUser.Profile})"
        });

        return Results.Json(new { ok = true, user = new { id = newUser.Id, login = newUser.Login } });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

app.MapDelete("/api/auth/users/{userId}", (HttpContext ctx, string userId) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        if (!BackendUtils.DeleteUser(userId))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        // Log account deletion
        var delCreatorLogin = AuthHelper.GetClaim(ctx, "login") ?? "?";
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = delCreatorLogin,
            UserName = delCreatorLogin,
            Action = "DELETE_ACCOUNT",
            Details = $"Compte supprimé : ID {userId}"
        });

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});

app.MapPost("/api/auth/users/{userId}/force-disconnect", (HttpContext ctx, string userId) =>
{
    try
    {
        if (!AuthHelper.IsAdmin(ctx))
            return Results.Json(new { ok = false, error = "Admin only" });

        var usersCol = MongoDbHelper.GetUsersCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("id", userId);
        var update = Builders<BsonDocument>.Update
            .Unset("activeSessionId")
            .Unset("lastActivityAt");
        var result = usersCol.UpdateOne(filter, update);

        if (result.MatchedCount == 0)
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        return Results.Json(new { ok = true, message = "Session déconnectée" });
    }
    catch (Exception ex)
    {
        return ErrorHelper.HandleException(ex);
    }
});


    }
}
