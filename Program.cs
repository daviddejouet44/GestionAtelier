// ======================================================
// Program.cs — DEV final (.NET 8)
// Port = 5080 — Frontend sous /pro
// ======================================================

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Routing;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using MongoDB.Driver;
using MongoDB.Bson;

// ======================================================
// HOST — DEV (5080)
// ======================================================

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(k =>
{
    k.ListenAnyIP(5080, o => o.Protocols = HttpProtocols.Http1AndHttp2);
});

// ======================================================
// CREATION DE LA CORBEILLE
// ======================================================
var recycleEnabled = builder.Configuration["RecycleBin:Enabled"] == "true";
var hotfoldersRootForRecycle = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT") is { Length: > 0 } env ? Path.GetFullPath(env) : @"C:\Flux";
var recyclePath    = builder.Configuration["RecycleBin:Path"] ?? Path.Combine(hotfoldersRootForRecycle, "Corbeille");
var recycleDays    = int.TryParse(builder.Configuration["RecycleBin:DaysToKeep"], out var d) ? d : 7;
Directory.CreateDirectory(recyclePath);

var app = builder.Build();

Console.WriteLine("[INFO] ContentRoot = " + app.Environment.ContentRootPath);

// ======================================================
// CREATION DES DOSSIERS HOTFOLDERS AU DÉMARRAGE
// ======================================================
{
    var hotRoot = BackendUtils.HotfoldersRoot();
    var hotFolders = new[]
    {
        "Soumission", "Début de production", "Corrections", "Corrections et fond perdu",
        "Rapport", "Prêt pour impression", "BAT", "Impression en cours",
        "PrismaPrepare", "Fiery", "Façonnage", "Fin de production", "Corbeille",
        "DossiersProduction"
    };
    foreach (var f in hotFolders)
    {
        try { Directory.CreateDirectory(Path.Combine(hotRoot, f)); } catch { }
    }
    Console.WriteLine("[INFO] Hotfolders initialized in " + hotRoot);
}

// ======================================================
// Logging (console + MongoDB)
// ======================================================

app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Method} {ctx.Request.Path}");
    await next();
    try
    {
        MongoDbHelper.InsertLog(new LogEntry
        {
            Timestamp  = DateTime.Now,
            Method     = ctx.Request.Method,
            Path       = ctx.Request.Path.Value ?? "",
            StatusCode = ctx.Response.StatusCode
        });
    }
    catch (Exception logEx) { Console.WriteLine($"[WARN] MongoDB log failed: {logEx.Message}"); }
});

// ======================================================
// FRONTEND — /pro (static files + fallback SPA)
// ======================================================

var proPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
Console.WriteLine("[INFO] Expected /pro at " + proPath);

if (Directory.Exists(proPath))
{
    var provider = new PhysicalFileProvider(proPath);

    // 1) Static files AVANT le routage
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider     = provider,
        RequestPath      = "/pro",
        DefaultFileNames = new List<string> { "index.html", "index.htm" }
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider        = provider,
        RequestPath         = "/pro",
        ContentTypeProvider = new FileExtensionContentTypeProvider()
    });
}
else
{
    Console.WriteLine("[WARN] wwwroot_pro NOT FOUND at " + proPath);
}

// 2) Routage APRÈS static files, AVANT les Map…
app.UseRouting();

// 3) /pro → /pro/index.html
app.MapGet("/pro", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/pro/index.html");
    return Task.CompletedTask;
});

// 4) Fallback SPA (ne capture pas les fichiers)
app.MapFallback("/pro/{*path}", async (HttpContext ctx) =>
{
    if (Path.HasExtension(ctx.Request.Path))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(Path.Combine(proPath, "index.html"));
});

// ======================================================
// AUTHENTIFICATION — Users Management
// ======================================================

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
        var list = users.Select(u => new
        {
            id = u.Id,
            login = u.Login,
            profile = u.Profile,
            name = u.Name
        });

        return Results.Json(new { ok = true, users = list });
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

// ======================================================
// CORBEILLE — API
// ======================================================

app.MapGet("/api/recycle/list", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var list = Directory.GetFiles(recyclePath)
            .Select(full => new {
                fullPath = full,
                fileName = Path.GetFileName(full),
                deletedAt = File.GetCreationTime(full)
            })
            .OrderByDescending(x => x.deletedAt)
            .ToList();

        return Results.Json(list);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/recycle/restore", (string fullPath, string destinationFolder) =>
{
    try
    {
        var src = Path.GetFullPath(fullPath);
        if (!File.Exists(src))
            return Results.Json(new { ok = false, error = "Fichier introuvable dans la corbeille." });

        var root = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, destinationFolder);
        Directory.CreateDirectory(destDir);

        var dest = Path.Combine(destDir, Path.GetFileName(src));
        File.Move(src, dest);

        return Results.Json(new { ok = true, restoredTo = dest });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/recycle/purge", () =>
{
    try
    {
        Directory.CreateDirectory(recyclePath);
        var count = 0;
        foreach (var f in Directory.GetFiles(recyclePath))
        {
            var age = DateTime.Now - File.GetCreationTime(f);
            if (age.TotalDays >= recycleDays)
            {
                File.Delete(f);
                count++;
            }
        }
        return Results.Json(new { ok = true, purged = count });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// APIs — Ping / Folders / Jobs (listing)
// ======================================================

app.MapGet("/api/ping", () => "pong");

app.MapGet("/api/folders", () =>
{
    var clean = BackendUtils.Hotfolders()
        .Select(n => n.Replace("\u00A0", " ").Trim())
        .ToArray();
    return Results.Json(clean);
});

app.MapGet("/api/jobs", (string folder) =>
{
    try
    {
        var root = BackendUtils.HotfoldersRoot();
        var dir  = Path.Combine(root, folder);
        if (!Directory.Exists(dir))
            return Results.Json(Array.Empty<object>());

        var files = Directory.EnumerateFiles(dir)
            .Select(f =>
            {
                try
                {
                    var fi = new FileInfo(f);
                    return new
                    {
                        name     = fi.Name,
                        fullPath = fi.FullName,
                        modified = fi.LastWriteTime,
                        size     = fi.Length
                    };
                }
                catch { return null; }
            })
            .Where(x => x != null)
            .OrderByDescending(x => ((dynamic)x!).modified)
            .ToList();

        return Results.Json(files);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// MOVE FILES
// ======================================================

app.MapPost("/api/jobs/move", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();

        static string NormalizeFs(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            var s = Uri.UnescapeDataString(p);
            s = s.Replace('/', '\\');
            s = s.Replace("\u00A0", " ");
            try { 
                return Path.GetFullPath(s); 
            }
            catch { 
                return s; 
            }
        }

        static (bool ok, string? moved, string? error) MoveOne(string? src, string folder, bool overwrite)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(src)) return (false, null, "Source vide.");
                
                Console.WriteLine($"[DEBUG] MoveOne: src={src}, folder={folder}");
                
                var srcDir = Path.GetDirectoryName(src);
                if (Directory.Exists(srcDir))
                {
                    Console.WriteLine($"[DEBUG] Fichiers dans {srcDir}:");
                    foreach (var f in Directory.GetFiles(srcDir))
                    {
                        Console.WriteLine($"  - {f}");
                    }
                }
                
                var root    = BackendUtils.HotfoldersRoot();
                var destDir = Path.Combine(root, folder);
                Directory.CreateDirectory(destDir);
                var dst = Path.Combine(destDir, Path.GetFileName(src));
                
                Console.WriteLine($"[DEBUG] File.Exists({src}) = {File.Exists(src)}");
                
                if (!File.Exists(src)) return (false, null, "Fichier introuvable.");
                File.Move(src, dst, overwrite);
                return (true, Path.GetFullPath(dst), null);
            }
            catch (Exception e) { 
                Console.WriteLine($"[DEBUG] MoveOne exception: {e.Message}");
                return (false, null, e.Message); 
            }
        }

        if (json.TryGetProperty("source", out var s) &&
            json.TryGetProperty("destination", out var d))
        {
            var src       = NormalizeFs(s.GetString());
            var dstFolder = d.GetString() ?? "";
            var overwrite = json.TryGetProperty("overwrite", out var ow) && ow.GetBoolean();
            
            Console.WriteLine($"[DEBUG] /api/jobs/move called");
            Console.WriteLine($"[DEBUG] src (raw) = {s.GetString()}");
            Console.WriteLine($"[DEBUG] src (normalized) = {src}");
            Console.WriteLine($"[DEBUG] dstFolder = {dstFolder}");
            
            var (ok, moved, error) = MoveOne(src, dstFolder, overwrite);

            if (ok && moved != null)
            {
                // Update delivery path in MongoDB so planning persists after file move
                try
                {
                    BackendUtils.UpdateDeliveryPath(src, moved);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[WARN] UpdateDeliveryPath failed: {ex2.Message}");
                }

                // Log file move activity
                var token2 = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var userLogin2 = "?";
                var userName2 = "?";
                try {
                    var dec2 = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token2));
                    var p2 = dec2.Split(':');
                    if (p2.Length >= 2) userLogin2 = p2[1];
                    var u2 = BackendUtils.LoadUsers().FirstOrDefault(u => u.Login == userLogin2);
                    if (u2 != null) userName2 = u2.Name;
                } catch { /* ignore */ }
                MongoDbHelper.InsertActivityLog(new ActivityLogEntry
                {
                    Timestamp = DateTime.Now,
                    UserLogin = userLogin2,
                    UserName = userName2,
                    Action = "MOVE_FILE",
                    Details = $"Déplacement : {Path.GetFileName(src)} → {dstFolder}"
                });

                // Create production folder when file moves to "Début de production"
                if (string.Equals(dstFolder.Trim(), "Début de production", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await BackendUtils.EnsureProductionFolderAsync(moved);
                    }
                    catch (Exception ex3)
                    {
                        Console.WriteLine($"[WARN] EnsureProductionFolder failed: {ex3.Message}");
                    }
                }
                else
                {
                    // Copy file to production folder stage sub-folder if applicable
                    try
                    {
                        await BackendUtils.CopyToProductionFolderStageAsync(moved, dstFolder);
                    }
                    catch (Exception ex4)
                    {
                        Console.WriteLine($"[WARN] CopyToProductionFolderStage failed: {ex4.Message}");
                    }
                }

                // Auto-mark BAT status when file moves to BAT folder
                if (string.Equals(dstFolder.Trim(), "BAT", StringComparison.OrdinalIgnoreCase))
                {
                    try {
                        var batCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
                        var batFilter = Builders<BsonDocument>.Filter.Eq("fullPath", moved);
                        var batDoc = new BsonDocument { ["fullPath"] = moved, ["status"] = "sent", ["sentAt"] = DateTime.UtcNow, ["validatedAt"] = BsonNull.Value, ["rejectedAt"] = BsonNull.Value };
                        batCol.ReplaceOne(batFilter, batDoc, new ReplaceOptions { IsUpsert = true });
                    } catch { }
                }
            }

            return Results.Json(new { ok, moved, error });
        }

        return Results.BadRequest("Format JSON invalide.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DEBUG] /api/jobs/move exception: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// DELETE FILE
// ======================================================

app.MapPost("/api/jobs/delete", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();

        if (!json.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        string fullPath = fpEl.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(fullPath))
            return Results.Json(new { ok = false, error = "fullPath vide" });

        fullPath = Path.GetFullPath(fullPath);

        if (!File.Exists(fullPath))
            return Results.Json(new { ok = false, error = "Fichier non trouvé" });

        Directory.CreateDirectory(recyclePath);

        string fileName = Path.GetFileName(fullPath);
        string trashPath = Path.Combine(recyclePath, fileName);

        int counter = 1;
        while (File.Exists(trashPath))
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            trashPath = Path.Combine(recyclePath, $"{fileNameWithoutExt}_{counter}{ext}");
            counter++;
        }

        File.Move(fullPath, trashPath);

        return Results.Json(new { ok = true, message = "Fichier supprimé avec succès" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// API — FILE
// ======================================================

app.MapGet("/api/file", (string path) =>
{
    var full = Path.GetFullPath(path);
    if (!File.Exists(full))
        return Results.NotFound();

    var provider = new FileExtensionContentTypeProvider();
    if (!provider.TryGetContentType(full, out var ct))
        ct = "application/octet-stream";

    return Results.File(File.OpenRead(full), ct);
});

// ======================================================
// DELIVERY (planning)
// ======================================================

app.MapGet("/api/delivery", () =>
{
    var map = BackendUtils.LoadDeliveries();

    var data = map.Values
        .Select(v => new
        {
            fullPath = v.FullPath,
            fileName = v.FileName,
            date     = v.Date.ToString("yyyy-MM-dd"),
            time     = v.Time
        });
    return Results.Json(data);
});

app.MapPut("/api/delivery", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("fullPath", out var fpEl) ||
            !json.TryGetProperty("date", out var dEl))
            return Results.BadRequest("fullPath + date requis.");

        var fullPath = fpEl.GetString()!;
        var dateStr  = dEl.GetString()!;
        if (!DateTime.TryParse(dateStr, out var dt))
            return Results.BadRequest("Format date invalide.");

        var full = Path.GetFullPath(fullPath);

        var time = "09:00";
        if (json.TryGetProperty("time", out var tEl) && tEl.ValueKind != JsonValueKind.Null)
        {
            time = tEl.GetString() ?? "09:00";
        }

        var delivery = new DeliveryItem
        {
            FullPath = full,
            FileName = Path.GetFileName(full),
            Date     = dt.Date,
            Time     = time
        };
        BackendUtils.UpsertDelivery(delivery);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapDelete("/api/delivery", (string fullPath) =>
{
    try
    {
        if (BackendUtils.DeleteDelivery(fullPath))
            return Results.Json(new { ok = true });

        return Results.Json(new { ok = false, error = "Aucune livraison trouvée." });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// FABRICATION
// ======================================================

app.MapGet("/api/fabrication", (string fullPath) =>
{
    var sheet = BackendUtils.FindFabrication(fullPath);
    if (sheet != null)
        return Results.Json(sheet);

    return Results.Json(new { ok = false, error = "Aucune fiche de fabrication." });
});

app.MapPut("/api/fabrication", async (HttpContext ctx) =>
{
    try
    {
        // Extract user from token
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string userName = "Système";
        int userProfile = 0;
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out userProfile);
                    var users = BackendUtils.LoadUsers();
                    var u = users.FirstOrDefault(x => x.Id == parts[0]);
                    if (u != null) userName = u.Name;
                }
            }
            catch { /* ignore token parse errors */ }
        }

        var input = await ctx.Request.ReadFromJsonAsync<FabricationInput>();
        if (input == null)
            return Results.Json(new { ok = false, error = "JSON vide." });

        if (string.IsNullOrWhiteSpace(input.FullPath))
            return Results.Json(new { ok = false, error = "FullPath absent." });

        if (!File.Exists(input.FullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable." });

        var old = BackendUtils.FindFabrication(input.FullPath);

        // Admin-only fields: only profile 3 can update Media1-4, TypeDocument, NombreFeuilles
        var isAdmin = (userProfile == 3);

        var sheet = new FabricationSheet
        {
            FullPath = input.FullPath,
            FileName = string.IsNullOrWhiteSpace(input.FileName)
                ? Path.GetFileName(input.FullPath)
                : input.FileName,

            MoteurImpression = input.MoteurImpression,
            Machine          = input.MoteurImpression ?? input.Machine,
            Operateur        = old?.Operateur,
            Quantite         = input.Quantite,
            TypeTravail      = input.TypeTravail,
            Format           = input.Format,
            Papier           = input.Papier,
            RectoVerso       = input.RectoVerso,
            Encres           = input.Encres,
            Client           = input.Client,
            NumeroAffaire    = input.NumeroAffaire,
            Notes            = input.Notes,
            Faconnage        = input.Faconnage,
            Livraison        = input.Livraison,
            Delai            = input.Delai,

            Media1        = isAdmin ? input.Media1        : old?.Media1,
            Media2        = isAdmin ? input.Media2        : old?.Media2,
            Media3        = isAdmin ? input.Media3        : old?.Media3,
            Media4        = isAdmin ? input.Media4        : old?.Media4,
            TypeDocument  = isAdmin ? input.TypeDocument  : old?.TypeDocument,
            NombreFeuilles = isAdmin ? input.NombreFeuilles : old?.NombreFeuilles,

            History = old?.History ?? new List<FabricationHistory>()
        };

        sheet.History.Add(new FabricationHistory
        {
            Date   = DateTime.Now,
            User   = userName,
            Action = (old == null ? "Création fiche" : "Modification fiche")
        });

        BackendUtils.UpsertFabrication(sheet);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/fabrication/pdf", (string fullPath) =>
{
    try
    {
        var sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        var doc = PdfUtils.CreateFabricationPdf(sheet);
        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        ms.Position = 0;

        return Results.File(ms, "application/pdf", $"FicheFabrication-{sheet.FileName}.pdf");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// OPÉRATEURS — liste des utilisateurs profil 2
// ======================================================

app.MapGet("/api/operators", () =>
{
    try
    {
        var users = BackendUtils.LoadUsers();
        var operators = users.Where(u => u.Profile == 2)
            .Select(u => new { id = u.Id, name = u.Name, login = u.Login });
        return Results.Json(new { ok = true, operators });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ASSIGNMENTS
// ======================================================

app.MapGet("/api/assignment", (string fullPath) =>
{
    var a = BackendUtils.FindAssignment(fullPath);
    if (a != null)
        return Results.Json(new { ok = true, assignment = new { fullPath = a.FullPath, operatorId = a.OperatorId, operatorName = a.OperatorName, assignedAt = a.AssignedAt, assignedBy = a.AssignedBy } });
    return Results.Json(new { ok = false, error = "Aucune affectation." });
});

app.MapGet("/api/assignments", () =>
{
    var list = BackendUtils.LoadAssignments();
    var result = list.Select(a => new { fullPath = a.FullPath, operatorId = a.OperatorId, operatorName = a.OperatorName, assignedAt = a.AssignedAt, assignedBy = a.AssignedBy });
    return Results.Json(result);
});

app.MapPut("/api/assignment", async (HttpContext ctx) =>
{
    try
    {
        // Extract caller identity from token
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        string callerName = "Système";
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length >= 3)
                {
                    var users = BackendUtils.LoadUsers();
                    var u = users.FirstOrDefault(x => x.Id == parts[0]);
                    if (u != null) callerName = u.Name;
                }
            }
            catch { /* ignore */ }
        }

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("fullPath", out var fpEl) ||
            !json.TryGetProperty("operatorId", out var opIdEl))
            return Results.Json(new { ok = false, error = "fullPath et operatorId requis." });

        var fullPath = fpEl.GetString() ?? "";
        var operatorId = opIdEl.GetString() ?? "";

        var users2 = BackendUtils.LoadUsers();
        var operator2 = users2.FirstOrDefault(u => u.Id == operatorId && u.Profile == 2);
        if (operator2 == null)
            return Results.Json(new { ok = false, error = "Opérateur introuvable ou profil invalide." });

        var assignment = new AssignmentItem
        {
            FullPath     = fullPath,
            OperatorId   = operatorId,
            OperatorName = operator2.Name,
            AssignedAt   = DateTime.Now,
            AssignedBy   = callerName
        };
        BackendUtils.UpsertAssignment(assignment);

        // Update fabrication history
        var sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet != null)
        {
            var updatedHistory = sheet.History.ToList();
            updatedHistory.Add(new FabricationHistory
            {
                Date   = DateTime.Now,
                User   = callerName,
                Action = $"Affecté à {operator2.Name}"
            });
            var updatedSheet = sheet with
            {
                Operateur = operator2.Name,
                History   = updatedHistory
            };
            BackendUtils.UpsertFabrication(updatedSheet);
        }

        return Results.Json(new { ok = true, operatorName = operator2.Name });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// UPLOAD
// ======================================================

app.MapPost("/api/upload", async (HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        if (!form.Files.Any())
            return Results.Json(new { ok = false, error = "Aucun fichier reçu" });

        var file   = form.Files.First();
        var folder = form["folder"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(folder))
            folder = "Soumission";

        if (!file.FileName.ToLower().EndsWith(".pdf"))
            return Results.Json(new { ok = false, error = "Seuls les PDF sont acceptés" });

        var root    = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, folder);
        Directory.CreateDirectory(destDir);

        string destFileName = Path.GetFileName(file.FileName);
        long numero = MongoDbHelper.GetNextFileNumber();
        destFileName = $"{numero:D5}_{destFileName}";

        var destPath = Path.Combine(destDir, destFileName);

        using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs);
        }

        return Results.Json(new {
            ok      = true,
            fullPath= destPath,
            fileName= destFileName
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// OUTILS
// ======================================================

app.MapPost("/api/acrobat/open", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("fullPath", out var fpEl))
            return Results.Json(new { ok = false, error = "fullPath manquant" });

        var full = Path.GetFullPath(fpEl.GetString() ?? "");
        if (!File.Exists(full))
            return Results.Json(new { ok = false, error = "Fichier introuvable" });

        var exe = @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";
        if (!File.Exists(exe))
            return Results.Json(new { ok = false, error = "Acrobat.exe introuvable" });

        var psi = new System.Diagnostics.ProcessStartInfo(exe, $"\"{full}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        System.Diagnostics.Process.Start(psi);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// ACROBAT — Ouvrir Acrobat Pro (sans fichier)
// ======================================================

app.MapPost("/api/acrobat", () =>
{
    try
    {
        var exe = @"C:\Program Files\Adobe\Acrobat DC\Acrobat\Acrobat.exe";
        if (!File.Exists(exe))
            return Results.Json(new { ok = false, error = "Acrobat.exe introuvable" });

        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        System.Diagnostics.Process.Start(psi);

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/tools/prismasync", () =>
{
    try
    {
        var url = "http://172.26.197.212/Authentication/";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// Routes racine
// ======================================================

app.MapGet("/", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/pro/index.html");
    return Task.CompletedTask;
});

app.MapGet("/debug/pro", () =>
{
    var path  = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
    var files = Directory.Exists(path)
        ? Directory.GetFiles(path)
                  .Select(f => Path.GetFileName(f))
                  .Where(n => n is not null)
                  .Select(n => n!)
                  .ToArray()
        : Array.Empty<string>();

    return Results.Json(new { expected = path, exists = Directory.Exists(path), files });
});

// ======================================================
// DOSSIERS DE PRODUCTION — API
// ======================================================

app.MapGet("/api/production-folders", () =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var docs = col.Find(new BsonDocument()).SortByDescending(x => x["createdAt"]).ToList();
        var result = docs.Select(d => new
        {
            _id = d["_id"].ToString(),
            number = d.Contains("number") ? d["number"].AsInt32 : 0,
            fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
            folderPath = d.Contains("folderPath") ? d["folderPath"].AsString : "",
            createdAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : DateTime.MinValue,
            currentStage = d.Contains("currentStage") ? d["currentStage"].AsString : "",
            files = d.Contains("files") ? d["files"].AsBsonArray.Count : 0
        }).ToList();
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/production-folders/{id}", (string id) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.Json(new { ok = false, error = "Dossier introuvable" });

        var files = new List<object>();
        if (doc.Contains("files"))
        {
            foreach (BsonDocument f in doc["files"].AsBsonArray)
            {
                files.Add(new
                {
                    stage = f.Contains("stage") ? f["stage"].AsString : "",
                    fileName = f.Contains("fileName") ? f["fileName"].AsString : "",
                    addedAt = f.Contains("addedAt") ? f["addedAt"].ToUniversalTime() : DateTime.MinValue
                });
            }
        }

        var fab = doc.Contains("fabricationSheet") ? doc["fabricationSheet"].AsBsonDocument : new BsonDocument();
        var fabricationSheet = new Dictionary<string, string?>();
        foreach (var el in fab.Elements)
            fabricationSheet[el.Name] = el.Value.ToString();

        return Results.Json(new
        {
            _id = doc["_id"].ToString(),
            number = doc.Contains("number") ? doc["number"].AsInt32 : 0,
            fileName = doc.Contains("fileName") ? doc["fileName"].AsString : "",
            folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "",
            createdAt = doc.Contains("createdAt") ? doc["createdAt"].ToUniversalTime() : DateTime.MinValue,
            currentStage = doc.Contains("currentStage") ? doc["currentStage"].AsString : "",
            fabricationSheet,
            files
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/production-folders/{id}", async (string id, HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));

        var updates = new List<UpdateDefinition<BsonDocument>>();

        if (json.TryGetProperty("currentStage", out var stageEl) && stageEl.ValueKind == JsonValueKind.String)
            updates.Add(Builders<BsonDocument>.Update.Set("currentStage", stageEl.GetString()));

        if (json.TryGetProperty("fabricationSheet", out var fabEl) && fabEl.ValueKind == JsonValueKind.Object)
        {
            var fabDoc = new BsonDocument();
            foreach (var prop in fabEl.EnumerateObject())
                fabDoc[prop.Name] = prop.Value.GetString() ?? "";
            updates.Add(Builders<BsonDocument>.Update.Set("fabricationSheet", fabDoc));
        }

        if (updates.Count > 0)
        {
            var combined = Builders<BsonDocument>.Update.Combine(updates);
            await col.UpdateOneAsync(filter, combined);
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPost("/api/production-folders/{id}/upload", async (string id, HttpContext ctx) =>
{
    try
    {
        var form = await ctx.Request.ReadFormAsync();
        if (!form.Files.Any())
            return Results.Json(new { ok = false, error = "Aucun fichier" });

        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.Json(new { ok = false, error = "Dossier introuvable" });

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        foreach (var file in form.Files)
        {
            var destPath = Path.Combine(folderPath, Path.GetFileName(file.FileName));
            using var fs = new FileStream(destPath, FileMode.Create);
            await file.CopyToAsync(fs);

            var fileEntry = new BsonDocument
            {
                ["stage"] = "Fichier ajouté",
                ["fileName"] = Path.GetFileName(file.FileName),
                ["addedAt"] = DateTime.UtcNow
            };
            await col.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Push("files", fileEntry));
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapGet("/api/production-folders/{id}/files/{filename}", (string id, string filename) =>
{
    try
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(id));
        var doc = col.Find(filter).FirstOrDefault();
        if (doc == null) return Results.NotFound();

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        var safeName = Path.GetFileName(filename);
        var full = Path.Combine(folderPath, safeName);
        if (!File.Exists(full)) return Results.NotFound();

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(full, out var ct)) ct = "application/octet-stream";
        return Results.File(File.OpenRead(full), ct, safeName);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// DEBUG — Endpoints
// ======================================================
var summaries = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
    .OfType<RouteEndpoint>()
    .Where(e => e.RoutePattern.RawText?.StartsWith("/api") ?? false)
    .Select(e => e.RoutePattern.RawText)
    .OrderBy(x => x)
    .ToList();

Console.WriteLine("\n[DEBUG] === ENDPOINTS /api ENREGISTRÉS ===");
foreach (var s in summaries)
    Console.WriteLine($"  {s}");
Console.WriteLine("[DEBUG] === FIN LISTE ===\n");

// ======================================================
// CONFIG — Schedule (plages horaires + jours fériés)
// ======================================================

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
// CONFIG — Paths (chemins d'accès)
// ======================================================

app.MapGet("/api/config/paths", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };
        return Results.Json(new { ok = true, config = new { hotfoldersRoot = cfg.HotfoldersRoot, recycleBinPath = cfg.RecycleBinPath } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/paths", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<PathsSettings>("paths")
            ?? new PathsSettings { HotfoldersRoot = BackendUtils.HotfoldersRoot(), RecycleBinPath = recyclePath };

        if (json.TryGetProperty("hotfoldersRoot", out var hrEl) && !string.IsNullOrWhiteSpace(hrEl.GetString()))
            existing.HotfoldersRoot = hrEl.GetString()!;
        if (json.TryGetProperty("recycleBinPath", out var rbEl))
            existing.RecycleBinPath = rbEl.GetString() ?? existing.RecycleBinPath;

        MongoDbHelper.UpsertSettings("paths", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Fabrication Imports
// ======================================================

app.MapGet("/api/config/fabrication-imports", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();
        return Results.Json(new { ok = true, config = new {
            media1Path = cfg.Media1Path, media2Path = cfg.Media2Path,
            media3Path = cfg.Media3Path, media4Path = cfg.Media4Path,
            typeDocumentPath = cfg.TypeDocumentPath
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/fabrication-imports", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<FabricationImportsSettings>("fabrication_imports")
            ?? new FabricationImportsSettings();

        if (json.TryGetProperty("media1Path", out var m1)) existing.Media1Path = m1.GetString() ?? "";
        if (json.TryGetProperty("media2Path", out var m2)) existing.Media2Path = m2.GetString() ?? "";
        if (json.TryGetProperty("media3Path", out var m3)) existing.Media3Path = m3.GetString() ?? "";
        if (json.TryGetProperty("media4Path", out var m4)) existing.Media4Path = m4.GetString() ?? "";
        if (json.TryGetProperty("typeDocumentPath", out var td)) existing.TypeDocumentPath = td.GetString() ?? "";

        MongoDbHelper.UpsertSettings("fabrication_imports", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Integrations (Prepare / Fiery)
// ======================================================

app.MapGet("/api/config/integrations", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var cfg = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();
        return Results.Json(new { ok = true, config = new { preparePath = cfg.PreparePath, fieryPath = cfg.FieryPath } });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPut("/api/config/integrations", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var existing = MongoDbHelper.GetSettings<IntegrationsSettings>("integrations")
            ?? new IntegrationsSettings();

        if (json.TryGetProperty("preparePath", out var pp)) existing.PreparePath = pp.GetString() ?? "";
        if (json.TryGetProperty("fieryPath", out var fp)) existing.FieryPath = fp.GetString() ?? "";

        MongoDbHelper.UpsertSettings("integrations", existing);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// CONFIG — Moteurs d'impression (CRUD + MongoDB)
// ======================================================

app.MapGet("/api/config/print-engines", () =>
{
    try
    {
        var engines = MongoDbHelper.GetPrintEngines();
        if (engines.Count == 0)
        {
            // Return default list if none configured
            engines = new List<string> { "Offset", "Numérique", "Jet d'encre", "Sérigraphie", "Flexographie", "Héliogravure", "Tampographie", "Laser" };
        }
        return Results.Json(engines);
    }
    catch (Exception)
    {
        return Results.Json(new List<string>());
    }
});

app.MapPost("/api/config/print-engines", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("name", out var nameEl) || string.IsNullOrWhiteSpace(nameEl.GetString()))
            return Results.Json(new { ok = false, error = "name requis" });

        MongoDbHelper.AddPrintEngine(nameEl.GetString()!);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/config/print-engines/import", async (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        if (!json.TryGetProperty("engines", out var enginesEl))
            return Results.Json(new { ok = false, error = "engines requis" });

        var names = enginesEl.EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        foreach (var name in names)
            MongoDbHelper.AddPrintEngine(name);

        return Results.Json(new { ok = true, count = names.Count });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapDelete("/api/config/print-engines/{name}", (HttpContext ctx, string name) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        MongoDbHelper.RemovePrintEngine(name);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ADMIN — Activity Logs
// ======================================================

app.MapGet("/api/admin/activity-logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetActivityLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ADMIN — Logs
// ======================================================

app.MapGet("/api/admin/logs", (HttpContext ctx, string? date) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3 || parts[2] != "3")
            return Results.Json(new { ok = false, error = "Admin only" });

        var logs = MongoDbHelper.GetRecentLogs(date);
        return Results.Json(new { ok = true, logs });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// ADMIN — Stats (Dashboard)
// ======================================================

app.MapGet("/api/admin/stats", (HttpContext ctx) =>
{
    try
    {
        var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        if (string.IsNullOrWhiteSpace(token))
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = decoded.Split(':');
        if (parts.Length < 3)
            return Results.Json(new { ok = false, error = "Non authentifié" });
        var statsUsers = BackendUtils.LoadUsers();
        if (!statsUsers.Any(u => u.Id == parts[0]))
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        var root = BackendUtils.HotfoldersRoot();
        var filesByFolder = new Dictionary<string, int>();
        int totalFiles = 0;

        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                var folderName = Path.GetFileName(dir) ?? "";
                var count = Directory.GetFiles(dir).Length;
                filesByFolder[folderName] = count;
                totalFiles += count;
            }
        }

        // Scheduled this week
        var now = DateTime.Now;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);
        var deliveries = BackendUtils.LoadDeliveries();
        var scheduledThisWeek = deliveries.Values.Count(d => d.Date >= startOfWeek && d.Date < endOfWeek);

        // Active assignments
        var assignments = BackendUtils.LoadAssignments();
        var activeAssignments = assignments.Count;

        return Results.Json(new { ok = true, stats = new {
            totalFiles,
            filesByFolder,
            scheduledThisWeek,
            activeAssignments
        }});
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/admin/migrate-to-mongo", () =>
{
    var results = new List<string>();
    var appData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FluxAtelier");

    // ---- Users ----
    try
    {
        var usersPath = Path.Combine(appData, "users.json");
        if (File.Exists(usersPath))
        {
            var json = File.ReadAllText(usersPath);
            var users = JsonSerializer.Deserialize<List<UserItem>>(json) ?? new();
            var col = MongoDbHelper.GetUsersCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var u in users)
                BackendUtils.InsertUser(u);
            File.Move(usersPath, usersPath + ".bak", overwrite: true);
            results.Add($"Users: {users.Count} migrated");
        }
        else
        {
            results.Add("Users: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Users error: {ex.Message}"); }

    // ---- Deliveries ----
    try
    {
        var deliveriesPath = Path.Combine(appData, "deliveries.json");
        if (File.Exists(deliveriesPath))
        {
            var json = File.ReadAllText(deliveriesPath);
            var list = JsonSerializer.Deserialize<List<DeliveryItem>>(json) ?? new();
            var col = MongoDbHelper.GetDeliveriesCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var item in list)
                BackendUtils.UpsertDelivery(item);
            File.Move(deliveriesPath, deliveriesPath + ".bak", overwrite: true);
            results.Add($"Deliveries: {list.Count} migrated");
        }
        else
        {
            results.Add("Deliveries: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Deliveries error: {ex.Message}"); }

    // ---- Fabrications ----
    try
    {
        var fabricationsPath = Path.Combine(appData, "fabrications.json");
        if (File.Exists(fabricationsPath))
        {
            var json = File.ReadAllText(fabricationsPath);
            var list = JsonSerializer.Deserialize<List<FabricationSheet>>(json) ?? new();
            var col = MongoDbHelper.GetFabricationsCollection();
            col.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            foreach (var sheet in list)
                BackendUtils.UpsertFabrication(sheet);
            File.Move(fabricationsPath, fabricationsPath + ".bak", overwrite: true);
            results.Add($"Fabrications: {list.Count} migrated");
        }
        else
        {
            results.Add("Fabrications: file not found, skipped");
        }
    }
    catch (Exception ex) { results.Add($"Fabrications error: {ex.Message}"); }

    return Results.Json(new { ok = true, results });
});

// ======================================================
// ACROBAT — Complete processing
// ======================================================
app.MapPost("/api/acrobat/complete", async (HttpContext ctx) =>
{
    try
    {
        var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (!doc.RootElement.TryGetProperty("sourcePath", out var spEl))
            return Results.Json(new { ok = false, error = "sourcePath manquant" });

        var sourcePath = Path.GetFullPath(spEl.GetString() ?? "");
        var root = BackendUtils.HotfoldersRoot();
        var fileName = Path.GetFileName(sourcePath);
        var rapportPath = Path.Combine(root, "Rapport", fileName);
        var printPath = Path.Combine(root, "Prêt pour impression", fileName);

        if (!File.Exists(rapportPath))
            return Results.Json(new { ok = false, error = $"Rapport introuvable: {rapportPath}" });
        if (!File.Exists(printPath))
            return Results.Json(new { ok = false, error = $"PDF corrigé introuvable: {printPath}" });

        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
            Console.WriteLine($"[INFO] Acrobat complete: suppression {sourcePath}");
        }

        BackendUtils.UpdateDeliveryPath(sourcePath, printPath);
        Console.WriteLine($"[INFO] Acrobat complete: delivery mis à jour → {printPath}");
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// ======================================================
// COMMANDS CONFIG
// ======================================================
app.MapGet("/api/config/commands", (HttpContext ctx) =>
{
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = col.Find(new BsonDocument()).FirstOrDefault();
    if (doc == null)
        return Results.Json(new { ok = true, config = new {
            batCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /T \"{typeWork}\" /SP /C {quantity}",
            prismaPrepareCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            printCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /SP /C {quantity}",
            modifyCommand = "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"",
            fieryHotfolderBase = "C:\\Fiery\\Hotfolders",
            controllerPath = "C:\\PrismaSync\\Controller"
        }});
    return Results.Json(new { ok = true, config = new {
        batCommand = doc.Contains("batCommand") ? doc["batCommand"].AsString : "",
        prismaPrepareCommand = doc.Contains("prismaPrepareCommand") ? doc["prismaPrepareCommand"].AsString : "",
        printCommand = doc.Contains("printCommand") ? doc["printCommand"].AsString : "",
        modifyCommand = doc.Contains("modifyCommand") ? doc["modifyCommand"].AsString : "",
        fieryHotfolderBase = doc.Contains("fieryHotfolderBase") ? doc["fieryHotfolderBase"].AsString : "",
        controllerPath = doc.Contains("controllerPath") ? doc["controllerPath"].AsString : ""
    }});
});

app.MapPut("/api/config/commands", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
    var doc = new BsonDocument();
    foreach (var prop in json.EnumerateObject())
        doc[prop.Name] = prop.Value.GetString() ?? "";
    col.ReplaceOne(new BsonDocument(), doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// COMMANDS — BAT, Print, Send etc.
// ======================================================
app.MapPost("/api/commands/bat", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var typeWork = json.TryGetProperty("typeWork", out var tw) ? tw.GetString() ?? "" : "";
        var quantity = json.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 1;

        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("batCommand") == true ? cfg["batCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /T \"{typeWork}\" /SP /C {quantity}";

        var cmd = BackendUtils.BuildCommandTemplate(template, filePath, typeWork, quantity);
        Console.WriteLine($"[INFO] BAT command: {cmd}");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        return Results.Json(new { ok = true, command = cmd });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-controller", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var controllerPath = cfg?.Contains("controllerPath") == true ? cfg["controllerPath"].AsString : @"C:\PrismaSync\Controller";
        Directory.CreateDirectory(controllerPath);
        File.Copy(filePath, Path.Combine(controllerPath, Path.GetFileName(filePath)), overwrite: true);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Impression en cours");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-prisma", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("prismaPrepareCommand") == true ? cfg["prismaPrepareCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "PrismaPrepare");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-print", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var quantity = json.TryGetProperty("quantity", out var qty) ? qty.GetInt32() : 1;
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("printCommand") == true ? cfg["printCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\" /SP /C {quantity}";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath, "", quantity);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Impression en cours");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/modify", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var template = cfg?.Contains("modifyCommand") == true ? cfg["modifyCommand"].AsString :
            "\"C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe\" \"{filePath}\"";
        var cmd = BackendUtils.BuildCommandTemplate(template, filePath);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "PrismaPrepare");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

app.MapPost("/api/commands/send-fiery", async (HttpContext ctx) =>
{
    try
    {
        var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
        var filePath = json.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        var col = MongoDbHelper.GetCollection<BsonDocument>("commandsConfig");
        var cfg = col.Find(new BsonDocument()).FirstOrDefault();
        var fieryBase = cfg?.Contains("fieryHotfolderBase") == true ? cfg["fieryHotfolderBase"].AsString : @"C:\Fiery\Hotfolders";
        Directory.CreateDirectory(fieryBase);
        File.Copy(filePath, Path.Combine(fieryBase, Path.GetFileName(filePath)), overwrite: true);
        var (ok, err) = BackendUtils.MoveFileToDestFolder(filePath, "Fiery");
        return Results.Json(new { ok, error = err });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
});

// ======================================================
// BAT TRACKING
// ======================================================
app.MapGet("/api/bat/status", (HttpContext ctx) =>
{
    var path = ctx.Request.Query["path"].ToString();
    if (string.IsNullOrEmpty(path)) return Results.Json(new { ok = false, error = "path manquant" });
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var doc = col.Find(filter).FirstOrDefault();
    if (doc == null) return Results.Json(new { status = "none", sentAt = (object?)null, validatedAt = (object?)null, rejectedAt = (object?)null });
    return Results.Json(new {
        status = doc.Contains("status") ? doc["status"].AsString : "none",
        sentAt = doc.Contains("sentAt") && doc["sentAt"] != BsonNull.Value ? doc["sentAt"].ToUniversalTime() : (DateTime?)null,
        validatedAt = doc.Contains("validatedAt") && doc["validatedAt"] != BsonNull.Value ? doc["validatedAt"].ToUniversalTime() : (DateTime?)null,
        rejectedAt = doc.Contains("rejectedAt") && doc["rejectedAt"] != BsonNull.Value ? doc["rejectedAt"].ToUniversalTime() : (DateTime?)null
    });
});

app.MapPost("/api/bat/send", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var doc = new BsonDocument { ["fullPath"] = path, ["status"] = "sent", ["sentAt"] = DateTime.UtcNow, ["validatedAt"] = BsonNull.Value, ["rejectedAt"] = BsonNull.Value };
    col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/bat/validate", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var update = Builders<BsonDocument>.Update.Set("status", "validated").Set("validatedAt", DateTime.UtcNow);
    col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

app.MapPost("/api/bat/reject", async (HttpContext ctx) =>
{
    var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
    var path = json.TryGetProperty("fullPath", out var fp) ? fp.GetString() ?? "" : "";
    var col = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
    var filter = Builders<BsonDocument>.Filter.Eq("fullPath", path);
    var update = Builders<BsonDocument>.Update.Set("status", "rejected").Set("rejectedAt", DateTime.UtcNow);
    col.UpdateOne(filter, update, new UpdateOptions { IsUpsert = true });
    return Results.Json(new { ok = true });
});

// ======================================================
// RUN
// ======================================================
app.Run();


// ======================================================
// MongoDB Counter Helper
// ======================================================

file static class MongoDbHelper
{
    private static readonly string ConnectionString = "mongodb://localhost:27017";
    private static readonly string DatabaseName = "GestionAtelier";
    private static readonly string CountersCollection = "counters";

    public static IMongoClient GetClient() => new MongoClient(ConnectionString);

    public static IMongoDatabase GetDatabase() => GetClient().GetDatabase(DatabaseName);

    public static long GetNextFileNumber()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "file_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB counter error: {ex.Message}");
            return 1;
        }
    }

    public static long GetNextUserId()
    {
        try
        {
            var db = GetDatabase();
            var countersCol = db.GetCollection<BsonDocument>(CountersCollection);

            var filter = Builders<BsonDocument>.Filter.Eq("_id", "user_counter");
            var update = Builders<BsonDocument>.Update.Inc("value", 1L);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].ToInt64();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB user counter error: {ex.Message}");
            return 1;
        }
    }

    public static IMongoCollection<BsonDocument> GetUsersCollection()
        => GetDatabase().GetCollection<BsonDocument>("users");

    public static IMongoCollection<BsonDocument> GetDeliveriesCollection()
        => GetDatabase().GetCollection<BsonDocument>("deliveries");

    public static IMongoCollection<BsonDocument> GetFabricationsCollection()
        => GetDatabase().GetCollection<BsonDocument>("fabrications");

    public static IMongoCollection<BsonDocument> GetAssignmentsCollection()
        => GetDatabase().GetCollection<BsonDocument>("assignments");

    public static IMongoCollection<BsonDocument> GetSettingsCollection()
        => GetDatabase().GetCollection<BsonDocument>("settings");

    public static IMongoCollection<BsonDocument> GetLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("logs");

    public static IMongoCollection<T> GetCollection<T>(string name)
        => GetDatabase().GetCollection<T>(name);

    public static T? GetSettings<T>(string settingsId) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            var doc = col.Find(filter).FirstOrDefault();
            if (doc == null) return null;
            doc.Remove("_id");
            return JsonSerializer.Deserialize<T>(doc.ToJson());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetSettings({settingsId}) error: {ex.Message}");
            return null;
        }
    }

    public static void UpsertSettings<T>(string settingsId, T value) where T : class
    {
        try
        {
            var col = GetSettingsCollection();
            var json = JsonSerializer.Serialize(value);
            var doc = BsonDocument.Parse(json);
            doc["_id"] = settingsId;
            var filter = Builders<BsonDocument>.Filter.Eq("_id", settingsId);
            col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] UpsertSettings({settingsId}) error: {ex.Message}");
        }
    }

    public static IMongoCollection<BsonDocument> GetActivityLogsCollection()
        => GetDatabase().GetCollection<BsonDocument>("activity_logs");

    public static IMongoCollection<BsonDocument> GetPrintEnginesCollection()
        => GetDatabase().GetCollection<BsonDocument>("print_engines");

    public static void InsertActivityLog(ActivityLogEntry entry)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"] = entry.Timestamp,
                ["userLogin"] = entry.UserLogin,
                ["userName"]  = entry.UserName,
                ["action"]    = entry.Action,
                ["details"]   = entry.Details
            };
            col.InsertOne(doc);
        }
        catch (Exception ex) { Console.WriteLine($"[WARN] Activity log failed: {ex.Message}"); }
    }

    public static List<object> GetActivityLogs(string? dateFilter, int limit = 500)
    {
        try
        {
            var col = GetActivityLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp = d.Contains("timestamp") ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                userLogin = d.Contains("userLogin") ? d["userLogin"].AsString : "",
                userName  = d.Contains("userName")  ? d["userName"].AsString  : "",
                action    = d.Contains("action")    ? d["action"].AsString    : "",
                details   = d.Contains("details")   ? d["details"].AsString   : ""
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetActivityLogs error: {ex.Message}");
            return new();
        }
    }

    public static List<string> GetPrintEngines()
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var docs = col.Find(Builders<BsonDocument>.Filter.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("name"))
                .ToList();
            return docs.Select(d => d.Contains("name") ? d["name"].AsString : "").Where(s => s.Length > 0).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetPrintEngines error: {ex.Message}");
            return new();
        }
    }

    public static void AddPrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("name", name);
            var existing = col.Find(filter).FirstOrDefault();
            if (existing == null)
            {
                col.InsertOne(new BsonDocument { ["name"] = name });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] AddPrintEngine error: {ex.Message}");
        }
    }

    public static void RemovePrintEngine(string name)
    {
        try
        {
            var col = GetPrintEnginesCollection();
            col.DeleteOne(Builders<BsonDocument>.Filter.Eq("name", name));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] RemovePrintEngine error: {ex.Message}");
        }
    }

    public static void InsertLog(LogEntry entry)
    {
        try
        {
            var col = GetLogsCollection();
            var doc = new BsonDocument
            {
                ["timestamp"]  = entry.Timestamp,
                ["method"]     = entry.Method,
                ["path"]       = entry.Path,
                ["statusCode"] = entry.StatusCode
            };
            col.InsertOne(doc);
        }
        catch { /* ignore log errors */ }
    }

    public static List<object> GetRecentLogs(string? dateFilter, int limit = 200)
    {
        try
        {
            var col = GetLogsCollection();
            var filter = Builders<BsonDocument>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(dateFilter) && DateTime.TryParse(dateFilter, out var dt))
            {
                var start = dt.Date;
                var end = start.AddDays(1);
                filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Gte("timestamp", start),
                    Builders<BsonDocument>.Filter.Lt("timestamp", end)
                );
            }
            var docs = col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
                .Limit(limit)
                .ToList();

            return docs.Select(d => (object)new
            {
                timestamp  = d.Contains("timestamp")  ? d["timestamp"].ToLocalTime() : (DateTime?)null,
                method     = d.Contains("method")     ? d["method"].AsString : "",
                path       = d.Contains("path")       ? d["path"].AsString : "",
                statusCode = d.Contains("statusCode") ? d["statusCode"].AsInt32 : 0
            }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetRecentLogs error: {ex.Message}");
            return new();
        }
    }
}


// ======================================================
// TYPES
// ======================================================

file class UserItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
    
    [JsonPropertyName("profile")]
    public int Profile { get; set; } = 1;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

file record DeliveryItem
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; init; } = default!;
    public DateTime Date { get; init; }
    public string Time { get; set; } = "09:00";
}

file record AssignmentItem
{
    public string FullPath     { get; init; } = default!;
    public string OperatorId   { get; init; } = default!;
    public string OperatorName { get; init; } = default!;
    public DateTime AssignedAt { get; init; }
    public string AssignedBy   { get; init; } = "";
}

file record FabricationHistory
{
    public DateTime Date { get; init; }
    public string User { get; init; } = "David";
    public string Action { get; init; } = "";
}

file record FabricationSheet
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; init; } = default!;
    public string? Machine { get; init; }
    public string? MoteurImpression { get; init; }
    public string? Operateur { get; init; }
    public int? Quantite { get; init; }
    public string? TypeTravail { get; init; }
    public string? Format { get; init; }
    public string? Papier { get; init; }
    public string? RectoVerso { get; init; }
    public string? Encres { get; init; }
    public string? Client { get; init; }
    public string? NumeroAffaire { get; init; }
    public string? Notes { get; init; }
    public DateTime? Delai { get; init; }
    public string? Media1 { get; init; }
    public string? Media2 { get; init; }
    public string? Media3 { get; init; }
    public string? Media4 { get; init; }
    public string? TypeDocument { get; init; }
    public int? NombreFeuilles { get; init; }
    public string? Faconnage { get; init; }
    public string? Livraison { get; init; }
    public List<FabricationHistory> History { get; init; } = new();
}

file class FabricationInput
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Machine { get; set; }
    public string? MoteurImpression { get; set; }
    public string? Operateur { get; set; }
    public int? Quantite { get; set; }
    public string? TypeTravail { get; set; }
    public string? Format { get; set; }
    public string? Papier { get; set; }
    public string? RectoVerso { get; set; }
    public string? Encres { get; set; }
    public string? Client { get; set; }
    public string? NumeroAffaire { get; set; }
    public string? Notes { get; set; }
    public DateTime? Delai { get; set; }
    public string? Media1 { get; set; }
    public string? Media2 { get; set; }
    public string? Media3 { get; set; }
    public string? Media4 { get; set; }
    public string? TypeDocument { get; set; }
    public int? NombreFeuilles { get; set; }
    public string? Faconnage { get; set; }
    public string? Livraison { get; set; }
}

// ======================================================
// Settings / Log types
// ======================================================

file class ScheduleSettings
{
    [JsonPropertyName("workStart")]
    public string WorkStart { get; set; } = "08:00";

    [JsonPropertyName("workEnd")]
    public string WorkEnd { get; set; } = "18:00";

    [JsonPropertyName("holidays")]
    public List<string> Holidays { get; set; } = new();
}

file class PathsSettings
{
    [JsonPropertyName("hotfoldersRoot")]
    public string HotfoldersRoot { get; set; } = @"C:\Flux";

    [JsonPropertyName("recycleBinPath")]
    public string RecycleBinPath { get; set; } = "";
}

file class FabricationImportsSettings
{
    [JsonPropertyName("media1Path")]
    public string Media1Path { get; set; } = "";

    [JsonPropertyName("media2Path")]
    public string Media2Path { get; set; } = "";

    [JsonPropertyName("media3Path")]
    public string Media3Path { get; set; } = "";

    [JsonPropertyName("media4Path")]
    public string Media4Path { get; set; } = "";

    [JsonPropertyName("typeDocumentPath")]
    public string TypeDocumentPath { get; set; } = "";
}

file class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
}

file class ActivityLogEntry
{
    public DateTime Timestamp { get; set; }
    public string UserLogin { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Details { get; set; } = "";
}

file class IntegrationsSettings
{
    [JsonPropertyName("preparePath")]
    public string PreparePath { get; set; } = "";

    [JsonPropertyName("fieryPath")]
    public string FieryPath { get; set; } = "";
}

// ======================================================
// BackendUtils
// ======================================================

file static class BackendUtils
{
    private static readonly System.Text.RegularExpressions.Regex SafeNameRegex =
        new(@"[^\w\-]", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static string HotfoldersRoot()
    {
        var env = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        return @"C:\Flux";
    }

    public static string[] Hotfolders()
    {
        try
        {
            var root = HotfoldersRoot();
            if (!Directory.Exists(root)) return Array.Empty<string>();

            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    // ---- Users ----

    public static List<UserItem> LoadUsers()
    {
        try
        {
            var col = MongoDbHelper.GetUsersCollection();
            return col.Find(new BsonDocument()).ToList()
                .Select(d => new UserItem
                {
                    Id       = d.Contains("id")       ? d["id"].AsString       : "",
                    Login    = d.Contains("login")    ? d["login"].AsString    : "",
                    Password = d.Contains("password") ? d["password"].AsString : "",
                    Profile  = d.Contains("profile")  ? d["profile"].AsInt32   : 1,
                    Name     = d.Contains("name")     ? d["name"].AsString     : ""
                }).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadUsers MongoDB error: {ex.Message}");
            return new();
        }
    }

    public static void InsertUser(UserItem user)
    {
        var col = MongoDbHelper.GetUsersCollection();
        var doc = new BsonDocument
        {
            ["id"]       = user.Id,
            ["login"]    = user.Login,
            ["password"] = user.Password,
            ["profile"]  = user.Profile,
            ["name"]     = user.Name
        };
        col.InsertOne(doc);
    }

    public static bool DeleteUser(string userId)
    {
        var col = MongoDbHelper.GetUsersCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("id", userId);
        var result = col.DeleteOne(filter);
        return result.DeletedCount > 0;
    }

    // ---- Deliveries ----

    public static Dictionary<string, DeliveryItem> LoadDeliveries()
    {
        try
        {
            var col = MongoDbHelper.GetDeliveriesCollection();
            var result = new Dictionary<string, DeliveryItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in col.Find(new BsonDocument()).ToList())
            {
                var fullPath = d.Contains("fullPath") ? d["fullPath"].AsString : "";
                if (!string.IsNullOrEmpty(fullPath))
                {
                    result[fullPath] = new DeliveryItem
                    {
                        FullPath = fullPath,
                        FileName = d.Contains("fileName") ? d["fileName"].AsString : (Path.GetFileName(fullPath) ?? ""),
                        Date     = d.Contains("date")     ? d["date"].ToUniversalTime() : DateTime.MinValue,
                        Time     = d.Contains("time")     ? d["time"].AsString : "09:00"
                    };
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadDeliveries MongoDB error: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void UpsertDelivery(DeliveryItem item)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", item.FullPath);
        var doc = new BsonDocument
        {
            ["fullPath"] = item.FullPath,
            ["fileName"] = item.FileName,
            ["date"]     = item.Date,
            ["time"]     = item.Time
        };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    public static bool DeleteDelivery(string fullPath)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
        var result = col.DeleteOne(filter);
        return result.DeletedCount > 0;
    }

    public static void DeleteDeliveries(List<string> fullPaths)
    {
        if (fullPaths.Count == 0) return;
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.In("fullPath", fullPaths);
        col.DeleteMany(filter);
    }

    public static void UpdateDeliveryPath(string oldPath, string newPath)
    {
        var col = MongoDbHelper.GetDeliveriesCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", oldPath);
        var update = Builders<BsonDocument>.Update
            .Set("fullPath", newPath)
            .Set("fileName", Path.GetFileName(newPath));
        col.UpdateMany(filter, update);
    }

    public static async Task EnsureProductionFolderAsync(string movedFilePath)
    {
        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var fileName = Path.GetFileName(movedFilePath);

        // Check if already exists
        var existing = col.Find(Builders<BsonDocument>.Filter.Eq("originalFilePath", movedFilePath)).FirstOrDefault();
        if (existing != null) return;

        // Get next number
        var count = (int)col.CountDocuments(new BsonDocument()) + 1;
        var number = count;
        var safeName = SafeNameRegex.Replace(Path.GetFileNameWithoutExtension(fileName), "_");
        var folderName = $"{number:D3}_{safeName}";
        var root = HotfoldersRoot();
        var dossiersRoot = Path.Combine(root, "DossiersProduction");
        Directory.CreateDirectory(dossiersRoot);
        var folderPath = Path.Combine(dossiersRoot, folderName);
        Directory.CreateDirectory(folderPath);

        // Copy original file to "Original" subfolder
        var originalDir = Path.Combine(folderPath, "Original");
        Directory.CreateDirectory(originalDir);
        if (File.Exists(movedFilePath))
            File.Copy(movedFilePath, Path.Combine(originalDir, fileName), overwrite: true);

        var doc = new BsonDocument
        {
            ["number"] = number,
            ["fileName"] = fileName,
            ["originalFilePath"] = movedFilePath,
            ["folderPath"] = folderPath,
            ["createdAt"] = DateTime.UtcNow,
            ["currentStage"] = "Début de production",
            ["fabricationSheet"] = new BsonDocument(),
            ["files"] = new BsonArray
            {
                new BsonDocument
                {
                    ["stage"] = "Original",
                    ["fileName"] = fileName,
                    ["addedAt"] = DateTime.UtcNow
                }
            }
        };
        await col.InsertOneAsync(doc);
    }

    public static async Task CopyToProductionFolderStageAsync(string filePath, string stage)
    {
        var stageFolderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rapport"] = "Rapport",
            ["Prêt pour impression"] = "PDF_Impression",
            ["BAT"] = "BAT",
            ["PrismaPrepare"] = "PrismaPrepare",
            ["Fin de production"] = "PDF_Imprime"
        };

        if (!stageFolderMap.TryGetValue(stage.Trim(), out var subFolder)) return;

        var col = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
        var fileName = Path.GetFileName(filePath);

        // Find production folder by matching fileName
        var filter = Builders<BsonDocument>.Filter.Eq("fileName", fileName);
        var doc = col.Find(filter).SortByDescending(x => x["createdAt"]).FirstOrDefault();
        if (doc == null) return;

        var folderPath = doc.Contains("folderPath") ? doc["folderPath"].AsString : "";
        if (string.IsNullOrEmpty(folderPath)) return;

        var stageDir = Path.Combine(folderPath, subFolder);
        Directory.CreateDirectory(stageDir);

        if (File.Exists(filePath))
            File.Copy(filePath, Path.Combine(stageDir, fileName), overwrite: true);

        var fileEntry = new BsonDocument
        {
            ["stage"] = subFolder,
            ["fileName"] = fileName,
            ["addedAt"] = DateTime.UtcNow
        };
        var update = Builders<BsonDocument>.Update
            .Push("files", fileEntry)
            .Set("currentStage", stage);
        await col.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]), update);
    }

    // ---- Fabrications ----

    public static Dictionary<string, FabricationSheet> LoadFabrications()
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var result = new Dictionary<string, FabricationSheet>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in col.Find(new BsonDocument()).ToList())
            {
                var sheet = BsonDocToFabricationSheet(d);
                if (sheet != null)
                    result[sheet.FullPath] = sheet;
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadFabrications MongoDB error: {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static FabricationSheet? FindFabrication(string fullPath)
    {
        try
        {
            var col = MongoDbHelper.GetFabricationsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
            var doc = col.Find(filter).FirstOrDefault();
            return doc == null ? null : BsonDocToFabricationSheet(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindFabrication MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static void UpsertFabrication(FabricationSheet sheet)
    {
        var col = MongoDbHelper.GetFabricationsCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", sheet.FullPath);
        var historyArray = new BsonArray(sheet.History.Select(h => new BsonDocument
        {
            ["date"]   = h.Date,
            ["user"]   = h.User,
            ["action"] = h.Action
        }));
        var doc = new BsonDocument
        {
            ["fullPath"]          = sheet.FullPath,
            ["fileName"]          = sheet.FileName,
            ["machine"]           = sheet.Machine           == null ? BsonNull.Value : (BsonValue)sheet.Machine,
            ["moteurImpression"]  = sheet.MoteurImpression  == null ? BsonNull.Value : (BsonValue)sheet.MoteurImpression,
            ["operateur"]         = sheet.Operateur         == null ? BsonNull.Value : (BsonValue)sheet.Operateur,
            ["quantite"]          = sheet.Quantite          == null ? BsonNull.Value : (BsonValue)(int)sheet.Quantite,
            ["typeTravail"]       = sheet.TypeTravail       == null ? BsonNull.Value : (BsonValue)sheet.TypeTravail,
            ["format"]            = sheet.Format            == null ? BsonNull.Value : (BsonValue)sheet.Format,
            ["papier"]            = sheet.Papier            == null ? BsonNull.Value : (BsonValue)sheet.Papier,
            ["rectoVerso"]        = sheet.RectoVerso        == null ? BsonNull.Value : (BsonValue)sheet.RectoVerso,
            ["encres"]            = sheet.Encres            == null ? BsonNull.Value : (BsonValue)sheet.Encres,
            ["client"]            = sheet.Client            == null ? BsonNull.Value : (BsonValue)sheet.Client,
            ["numeroAffaire"]     = sheet.NumeroAffaire     == null ? BsonNull.Value : (BsonValue)sheet.NumeroAffaire,
            ["notes"]             = sheet.Notes             == null ? BsonNull.Value : (BsonValue)sheet.Notes,
            ["delai"]             = sheet.Delai             == null ? BsonNull.Value : (BsonValue)sheet.Delai.Value,
            ["media1"]            = sheet.Media1            == null ? BsonNull.Value : (BsonValue)sheet.Media1,
            ["media2"]            = sheet.Media2            == null ? BsonNull.Value : (BsonValue)sheet.Media2,
            ["media3"]            = sheet.Media3            == null ? BsonNull.Value : (BsonValue)sheet.Media3,
            ["media4"]            = sheet.Media4            == null ? BsonNull.Value : (BsonValue)sheet.Media4,
            ["typeDocument"]      = sheet.TypeDocument      == null ? BsonNull.Value : (BsonValue)sheet.TypeDocument,
            ["nombreFeuilles"]    = sheet.NombreFeuilles    == null ? BsonNull.Value : (BsonValue)(int)sheet.NombreFeuilles,
            ["faconnage"]         = sheet.Faconnage         == null ? BsonNull.Value : (BsonValue)sheet.Faconnage,
            ["livraison"]         = sheet.Livraison         == null ? BsonNull.Value : (BsonValue)sheet.Livraison,
            ["history"]           = historyArray
        };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    private static FabricationSheet? BsonDocToFabricationSheet(BsonDocument d)
    {
        var fullPath = d.Contains("fullPath") ? d["fullPath"].AsString : "";
        if (string.IsNullOrEmpty(fullPath)) return null;

        var history = new List<FabricationHistory>();
        if (d.Contains("history") && d["history"].IsBsonArray)
        {
            foreach (var item in d["history"].AsBsonArray)
            {
                var h = item.AsBsonDocument;
                history.Add(new FabricationHistory
                {
                    Date   = h.Contains("date")   ? h["date"].ToUniversalTime() : DateTime.MinValue,
                    User   = h.Contains("user")   ? h["user"].AsString : "David",
                    Action = h.Contains("action") ? h["action"].AsString : ""
                });
            }
        }

        var machineVal = GetNullableString(d, "machine");
        return new FabricationSheet
        {
            FullPath         = fullPath,
            FileName         = d.Contains("fileName")         ? d["fileName"].AsString      : (Path.GetFileName(fullPath) ?? ""),
            Machine          = machineVal,
            MoteurImpression = GetNullableString(d, "moteurImpression") ?? machineVal,
            Operateur        = GetNullableString(d, "operateur"),
            Quantite         = d.Contains("quantite")         && d["quantite"]         != BsonNull.Value ? (int?)d["quantite"].AsInt32   : null,
            TypeTravail      = GetNullableString(d, "typeTravail"),
            Format           = GetNullableString(d, "format"),
            Papier           = GetNullableString(d, "papier"),
            RectoVerso       = GetNullableString(d, "rectoVerso"),
            Encres           = GetNullableString(d, "encres"),
            Client           = GetNullableString(d, "client"),
            NumeroAffaire    = GetNullableString(d, "numeroAffaire"),
            Notes            = GetNullableString(d, "notes"),
            Delai            = d.Contains("delai")            && d["delai"]            != BsonNull.Value ? (DateTime?)d["delai"].ToLocalTime() : null,
            Media1           = GetNullableString(d, "media1"),
            Media2           = GetNullableString(d, "media2"),
            Media3           = GetNullableString(d, "media3"),
            Media4           = GetNullableString(d, "media4"),
            TypeDocument     = GetNullableString(d, "typeDocument"),
            NombreFeuilles   = d.Contains("nombreFeuilles")   && d["nombreFeuilles"]   != BsonNull.Value ? (int?)d["nombreFeuilles"].AsInt32 : null,
            Faconnage        = GetNullableString(d, "faconnage"),
            Livraison        = GetNullableString(d, "livraison"),
            History          = history
        };
    }

    private static string? GetNullableString(BsonDocument d, string key)
        => d.Contains(key) && d[key] != BsonNull.Value ? d[key].AsString : null;

    // ---- Assignments ----

    public static AssignmentItem? FindAssignment(string fullPath)
    {
        try
        {
            var col = MongoDbHelper.GetAssignmentsCollection();
            var filter = Builders<BsonDocument>.Filter.Eq("fullPath", fullPath);
            var doc = col.Find(filter).FirstOrDefault();
            return doc == null ? null : BsonDocToAssignment(doc);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FindAssignment MongoDB error: {ex.Message}");
            return null;
        }
    }

    public static List<AssignmentItem> LoadAssignments()
    {
        try
        {
            var col = MongoDbHelper.GetAssignmentsCollection();
            return col.Find(new BsonDocument()).ToList()
                .Select(d => BsonDocToAssignment(d))
                .Where(a => a != null)
                .Select(a => a!)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] LoadAssignments MongoDB error: {ex.Message}");
            return new();
        }
    }

    public static void UpsertAssignment(AssignmentItem item)
    {
        var col = MongoDbHelper.GetAssignmentsCollection();
        var filter = Builders<BsonDocument>.Filter.Eq("fullPath", item.FullPath);
        var doc = new BsonDocument
        {
            ["fullPath"]     = item.FullPath,
            ["operatorId"]   = item.OperatorId,
            ["operatorName"] = item.OperatorName,
            ["assignedAt"]   = item.AssignedAt,
            ["assignedBy"]   = item.AssignedBy
        };
        col.ReplaceOne(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    private static AssignmentItem? BsonDocToAssignment(BsonDocument d)
    {
        var fullPath = d.Contains("fullPath") ? d["fullPath"].AsString : "";
        if (string.IsNullOrEmpty(fullPath)) return null;
        return new AssignmentItem
        {
            FullPath     = fullPath,
            OperatorId   = d.Contains("operatorId")   ? d["operatorId"].AsString   : "",
            OperatorName = d.Contains("operatorName") ? d["operatorName"].AsString : "",
            AssignedAt   = d.Contains("assignedAt")   ? d["assignedAt"].ToUniversalTime() : DateTime.MinValue,
            AssignedBy   = d.Contains("assignedBy")   ? d["assignedBy"].AsString   : ""
        };
    }

    public static string BuildCommandTemplate(string template, string filePath, string typeWork = "", int quantity = 1)
    {
        return template
            .Replace("{filePath}", filePath)
            .Replace("{typeWork}", typeWork)
            .Replace("{quantity}", quantity.ToString());
    }

    public static (bool ok, string? error) MoveFileToDestFolder(string filePath, string destFolder)
    {
        try
        {
            var root = HotfoldersRoot();
            var destDir = Path.Combine(root, destFolder);
            Directory.CreateDirectory(destDir);
            var dst = Path.Combine(destDir, Path.GetFileName(filePath));
            File.Move(filePath, dst, true);
            UpdateDeliveryPath(filePath, dst);
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}

// ======================================================
// PdfUtils
// ======================================================

file static class PdfUtils
{
    public static Document CreateFabricationPdf(FabricationSheet s)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header()
                    .AlignCenter()
                    .Text($"FICHE DE FABRICATION — {s.FileName}")
                    .FontSize(20)
                    .SemiBold();

                page.Content().Column(col =>
                {
                    col.Item().Text("Informations générales").FontSize(14).SemiBold();
                    col.Item().Text($"Nom fichier : {s.FileName}");
                    col.Item().Text($"Chemin : {s.FullPath}");
                    col.Item().Text($"Généré : {DateTime.Now:dd/MM/yyyy HH:mm}");
                    if (s.Delai.HasValue)
                        col.Item().Text($"Délai : {s.Delai.Value:dd/MM/yyyy}");

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#cccccc");

                    col.Item().Text("Données Atelier").FontSize(14).SemiBold();
                    col.Item().Text($"Moteur d'impression : {s.MoteurImpression ?? s.Machine}");
                    col.Item().Text($"Opérateur : {s.Operateur}");
                    col.Item().Text($"Quantité : {s.Quantite}");
                    col.Item().Text($"Nombre de feuilles : {s.NombreFeuilles}");
                    col.Item().Text($"Type travail : {s.TypeTravail}");
                    col.Item().Text($"Type document : {s.TypeDocument}");
                    col.Item().Text($"Format : {s.Format}");
                    col.Item().Text($"Papier : {s.Papier}");
                    col.Item().Text($"Recto/Verso : {s.RectoVerso}");
                    col.Item().Text($"Encres : {s.Encres}");
                    col.Item().Text($"Façonnage : {s.Faconnage}");
                    col.Item().Text($"Livraison : {s.Livraison}");
                    col.Item().Text($"Client : {s.Client}");
                    col.Item().Text($"N° affaire : {s.NumeroAffaire}");
                    col.Item().Text($"Notes : {s.Notes}");

                    if (!string.IsNullOrWhiteSpace(s.Media1) || !string.IsNullOrWhiteSpace(s.Media2) ||
                        !string.IsNullOrWhiteSpace(s.Media3) || !string.IsNullOrWhiteSpace(s.Media4))
                    {
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#cccccc");
                        col.Item().Text("Médias").FontSize(14).SemiBold();
                        if (!string.IsNullOrWhiteSpace(s.Media1)) col.Item().Text($"Média 1 : {s.Media1}");
                        if (!string.IsNullOrWhiteSpace(s.Media2)) col.Item().Text($"Média 2 : {s.Media2}");
                        if (!string.IsNullOrWhiteSpace(s.Media3)) col.Item().Text($"Média 3 : {s.Media3}");
                        if (!string.IsNullOrWhiteSpace(s.Media4)) col.Item().Text($"Média 4 : {s.Media4}");
                    }

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#cccccc");

                    col.Item().Text("Historique").FontSize(14).SemiBold();
                    foreach (var h in s.History.OrderBy(h => h.Date))
                        col.Item().Text($"{h.Date:dd/MM/yyyy HH:mm} — {h.User} — {h.Action}");
                });
            });
        });
    }
}