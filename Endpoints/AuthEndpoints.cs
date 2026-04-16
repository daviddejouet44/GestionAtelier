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

namespace GestionAtelier.Endpoints;

public static class AuthEndpointsExtensions
{
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

        Console.WriteLine($"[DEBUG] Login attempt: {login} / {pwd}");

        var users = BackendUtils.LoadUsers();
        Console.WriteLine($"[DEBUG] Users loaded: {users.Count}");
        foreach (var u in users)
        {
            Console.WriteLine($"[DEBUG]   - {u.Login} / {u.Password}");
        }

        var user = users.FirstOrDefault(u => u.Login == login && u.Password == pwd);

        if (user == null)
        {
            Console.WriteLine($"[DEBUG] User not found or password mismatch");
            return Results.Json(new { ok = false, error = "Identifiants invalides" });
        }

        // Générer un token simple
        var token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user.Id}:{user.Login}:{user.Profile}"));

        Console.WriteLine($"[DEBUG] Login successful for {user.Login}");

        // Log login activity
        MongoDbHelper.InsertActivityLog(new ActivityLogEntry
        {
            Timestamp = DateTime.Now,
            UserLogin = user.Login,
            UserName = user.Name,
            Action = "LOGIN",
            Details = $"Connexion profil {user.Profile}"
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
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/auth/me", (HttpContext ctx) =>
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
        var user = users.FirstOrDefault(u => u.Id == parts[0]);

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
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/auth/users", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var users = BackendUtils.LoadUsers();
        var now = DateTime.Now;
        var list = users.Select(u => new
        {
            id = u.Id,
            login = u.Login,
            profile = u.Profile,
            name = u.Name,
            lastActivityAt = u.LastActivityAt,
            online = u.LastActivityAt.HasValue && (now - u.LastActivityAt.Value).TotalMinutes < 5
        });

        return Results.Json(new { ok = true, users = list });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/auth/heartbeat", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token)) return Results.Json(new { ok = false });
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 2) return Results.Json(new { ok = false });
        BackendUtils.UpdateUserActivity(parts[1]);
        return Results.Json(new { ok = true });
    }
    catch { return Results.Json(new { ok = false }); }
});

app.MapPut("/api/auth/users/{userId}", async (HttpContext ctx, string userId) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
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
        return Results.Json(new { ok = false, error = ex.Message });
    }
});


app.MapPost("/api/auth/register", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
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
        var creatorLogin = parts.Length >= 2 ? parts[1] : "?";
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
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/auth/users/{userId}", (HttpContext ctx, string userId) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');

        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        if (!BackendUtils.DeleteUser(userId))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        // Log account deletion
        var delCreatorLogin = parts.Length >= 2 ? parts[1] : "?";
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
        return Results.Json(new { ok = false, error = ex.Message });
    }
});


    }
}
