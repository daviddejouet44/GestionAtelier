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
var recyclePath    = builder.Configuration["RecycleBin:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "Corbeille");
var recycleDays    = int.TryParse(builder.Configuration["RecycleBin:DaysToKeep"], out var d) ? d : 7;
Directory.CreateDirectory(recyclePath);

var app = builder.Build();

Console.WriteLine("[INFO] ContentRoot = " + app.Environment.ContentRootPath);

// ======================================================
// Logging (console)
// ======================================================

app.Use(async (ctx, next) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Method} {ctx.Request.Path}");
    await next();
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

        var newId = (users.Count + 1).ToString("D3");
        var newUser = new UserItem
        {
            Id = newId,
            Login = loginEl.GetString() ?? "",
            Password = pwdEl.GetString() ?? "",
            Profile = profileEl.GetInt32(),
            Name = nameEl.GetString() ?? ""
        };

        users.Add(newUser);
        BackendUtils.SaveUsers(users);

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

        var users = BackendUtils.LoadUsers();
        var toRemove = users.FirstOrDefault(u => u.Id == userId);

        if (toRemove == null)
            return Results.Json(new { ok = false, error = "Utilisateur non trouvé" });

        users.Remove(toRemove);
        BackendUtils.SaveUsers(users);

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

        string trashFolder = Path.Combine(Path.GetDirectoryName(fullPath)!, ".Trash");
        Directory.CreateDirectory(trashFolder);

        string fileName = Path.GetFileName(fullPath);
        string trashPath = Path.Combine(trashFolder, fileName);

        int counter = 1;
        while (File.Exists(trashPath))
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            trashPath = Path.Combine(trashFolder, $"{fileNameWithoutExt}_{counter}{ext}");
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
    
    var toRemove = map.Where(kvp => !File.Exists(kvp.Key)).Select(kvp => kvp.Key).ToList();
    if (toRemove.Count > 0) {
        foreach (var key in toRemove) {
            map.Remove(key);
        }
        BackendUtils.SaveDeliveries(map);
        Console.WriteLine($"[INFO] Nettoyage: {toRemove.Count} fichier(s) supprimé(s) du planning");
    }
    
    var data = map.Values.Select(v => new
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
        if (!File.Exists(full))
            return Results.BadRequest("Fichier introuvable.");

        var folder = new DirectoryInfo(Path.GetDirectoryName(full)!).Name;
        if (!string.Equals(folder, "1.Reception", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(folder, "2.Analyse", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(folder, "8. Fin de production", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { ok = false, error = "Seuls les fichiers de 1.Reception, 2.Analyse et 8. Fin de production peuvent être planifiés." });

        var time = "09:00";
        if (json.TryGetProperty("time", out var tEl) && tEl.ValueKind != JsonValueKind.Null)
        {
            time = tEl.GetString() ?? "09:00";
        }

        var map = BackendUtils.LoadDeliveries();
        map[full] = new DeliveryItem
        {
            FullPath = full,
            FileName = Path.GetFileName(full),
            Date     = dt.Date,
            Time     = time
        };
        BackendUtils.SaveDeliveries(map);

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
        var map = BackendUtils.LoadDeliveries();
        if (map.Remove(fullPath))
        {
            BackendUtils.SaveDeliveries(map);
            return Results.Json(new { ok = true });
        }
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
    var map = BackendUtils.LoadFabrications();
    if (map.TryGetValue(fullPath, out var sheet))
        return Results.Json(sheet);

    return Results.Json(new { ok = false, error = "Aucune fiche de fabrication." });
});

app.MapPut("/api/fabrication", async (HttpContext ctx) =>
{
    try
    {
        var input = await ctx.Request.ReadFromJsonAsync<FabricationInput>();
        if (input == null)
            return Results.Json(new { ok = false, error = "JSON vide." });

        if (string.IsNullOrWhiteSpace(input.FullPath))
            return Results.Json(new { ok = false, error = "FullPath absent." });

        if (!File.Exists(input.FullPath))
            return Results.Json(new { ok = false, error = "Fichier introuvable." });

        var map = BackendUtils.LoadFabrications();
        map.TryGetValue(input.FullPath, out var old);

        var sheet = new FabricationSheet
        {
            FullPath = input.FullPath,
            FileName = string.IsNullOrWhiteSpace(input.FileName)
                ? Path.GetFileName(input.FullPath)
                : input.FileName,

            Machine       = input.Machine,
            Operateur     = input.Operateur,
            Quantite      = input.Quantite,
            TypeTravail   = input.TypeTravail,
            Format        = input.Format,
            Papier        = input.Papier,
            RectoVerso    = input.RectoVerso,
            Encres        = input.Encres,
            Client        = input.Client,
            NumeroAffaire = input.NumeroAffaire,
            Notes         = input.Notes,
            History       = old?.History ?? new List<FabricationHistory>()
        };

        sheet.History.Add(new FabricationHistory
        {
            Date   = DateTime.Now,
            User   = "David",
            Action = (old == null ? "Création fiche" : "Modification fiche")
        });

        map[input.FullPath] = sheet;
        BackendUtils.SaveFabrications(map);

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
        var all = BackendUtils.LoadFabrications();
        if (!all.TryGetValue(fullPath, out var sheet))
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
            folder = "1.Reception";

        if (!file.FileName.ToLower().EndsWith(".pdf"))
            return Results.Json(new { ok = false, error = "Seuls les PDF sont acceptés" });

        var root    = BackendUtils.HotfoldersRoot();
        var destDir = Path.Combine(root, folder);
        Directory.CreateDirectory(destDir);

        // --- AJOUT : récupère le numéro SI c'est un dépôt "soumission"
        string destFileName = Path.GetFileName(file.FileName);

        // On détermine le profil: (adaptation selon ton code, ici profil = form["profile"])
        var profile = form["profile"].ToString().Trim();

        if (profile == "soumission")
        {
            long numero = MongoDbHelper.GetNextFileNumber();
            destFileName = $"{numero:D5}_{destFileName}";
        }

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
            var update = Builders<BsonDocument>.Update.Inc("value", 1);
            var options = new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After, IsUpsert = true };

            var result = countersCol.FindOneAndUpdate(filter, update, options);
            return result["value"].AsInt64;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] MongoDB counter error: {ex.Message}");
            return 1;
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
    public List<FabricationHistory> History { get; init; } = new();
}

file class FabricationInput
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Machine { get; set; }
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
}

// ======================================================
// BackendUtils
// ======================================================

file static class BackendUtils
{
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

    public static string UsersDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FluxAtelier");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "users.json");
    }

    public static List<UserItem> LoadUsers()
    {
        var path = UsersDbPath();
        Console.WriteLine($"[DEBUG] Loading users from: {path}");
        Console.WriteLine($"[DEBUG] File exists: {File.Exists(path)}");
        
        if (!File.Exists(path))
        {
            Console.WriteLine($"[DEBUG] File not found!");
            return new();
        }

        try
        {
            var json = File.ReadAllText(path);
            Console.WriteLine($"[DEBUG] JSON content: {json}");
            
            var result = JsonSerializer.Deserialize<List<UserItem>>(json);
            Console.WriteLine($"[DEBUG] Deserialized: {result?.Count ?? 0} users");
            
            if (result != null)
            {
                foreach (var u in result)
                {
                    Console.WriteLine($"[DEBUG] User: Id={u.Id}, Login={u.Login}, Pwd={u.Password}, Profile={u.Profile}, Name={u.Name}");
                }
            }
            
            return result ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception in LoadUsers: {ex.Message}");
            return new();
        }
    }

    public static void SaveUsers(List<UserItem> users)
    {
        var path = UsersDbPath();
        var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string DeliveryDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FluxAtelier");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "deliveries.json");
    }

    public static Dictionary<string, DeliveryItem> LoadDeliveries()
    {
        var path = DeliveryDbPath();
        if (!File.Exists(path))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<DeliveryItem>>(json) ?? new();
            return list.ToDictionary(x => x.FullPath, x => x, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveDeliveries(Dictionary<string, DeliveryItem> map)
    {
        var path = DeliveryDbPath();
        var list = map.Values.OrderBy(v => v.Date).ThenBy(v => v.FileName).ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string FabricationDbPath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FluxAtelier");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "fabrications.json");
    }

    public static Dictionary<string, FabricationSheet> LoadFabrications()
    {
        var path = FabricationDbPath();
        if (!File.Exists(path))
            return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<FabricationSheet>>(json) ?? new();
            return list.ToDictionary(k => k.FullPath, v => v, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static void SaveFabrications(Dictionary<string, FabricationSheet> map)
    {
        var path = FabricationDbPath();
        var list = map.Values.OrderBy(v => v.FileName).ToList();
        var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
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

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#cccccc");

                    col.Item().Text("Données Atelier").FontSize(14).SemiBold();
                    col.Item().Text($"Machine : {s.Machine}");
                    col.Item().Text($"Opérateur : {s.Operateur}");
                    col.Item().Text($"Quantité : {s.Quantite}");
                    col.Item().Text($"Type travail : {s.TypeTravail}");
                    col.Item().Text($"Format : {s.Format}");
                    col.Item().Text($"Papier : {s.Papier}");
                    col.Item().Text($"Recto/Verso : {s.RectoVerso}");
                    col.Item().Text($"Encres : {s.Encres}");
                    col.Item().Text($"Client : {s.Client}");
                    col.Item().Text($"N° affaire : {s.NumeroAffaire}");
                    col.Item().Text($"Notes : {s.Notes}");

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#cccccc");

                    col.Item().Text("Historique").FontSize(14).SemiBold();
                    foreach (var h in s.History.OrderBy(h => h.Date))
                        col.Item().Text($"{h.Date:dd/MM/yyyy HH:mm} — {h.User} — {h.Action}");
                });
            });
        });
    }
}