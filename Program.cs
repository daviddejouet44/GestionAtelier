// ======================================================
// Program.cs — entry point (refactored modular architecture)
// ======================================================

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;
using GestionAtelier.Endpoints;
using GestionAtelier.Endpoints.Portal;
using GestionAtelier.Watchers;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseKestrel(k =>
{
    k.ListenAnyIP(5080, o => o.Protocols = HttpProtocols.Http1AndHttp2);
    k.Limits.MaxRequestBodySize = null; // No size limit for large PDF uploads
});

var recycleEnabled = builder.Configuration["RecycleBin:Enabled"] == "true";
var hotfoldersRootForRecycle = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT") is { Length: > 0 } env ? Path.GetFullPath(env) : @"C:\Flux";
var recyclePath    = builder.Configuration["RecycleBin:Path"] ?? Path.Combine(hotfoldersRootForRecycle, "Corbeille");
var recycleDays    = int.TryParse(builder.Configuration["RecycleBin:DaysToKeep"], out var d) ? d : 7;
Directory.CreateDirectory(recyclePath);

builder.Services.AddHostedService<GestionAtelier.Services.DailyReportService>();
builder.Services.AddSingleton<GestionAtelier.Services.OrderSourcePollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GestionAtelier.Services.OrderSourcePollingService>());

var app = builder.Build();

QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("[INFO] ContentRoot = " + app.Environment.ContentRootPath);

// Initialize hotfolders
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

// Watchers
app.UseHotfolderWatcher();
var tempCopyWatcher = app.UsePrismaOutputWatcher();

// 1. Fichiers statiques AVANT le routing (ils n'ont pas besoin du routing)
var proPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot_pro");
Console.WriteLine("[INFO] Expected /pro at " + proPath);

if (Directory.Exists(proPath))
{
    var provider = new PhysicalFileProvider(proPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider     = provider,
        RequestPath      = "/pro",
        DefaultFileNames = new List<string> { "index.html", "index.htm" }
    });
    var proContentTypes = new FileExtensionContentTypeProvider();
    proContentTypes.Mappings[".md"] = "text/markdown; charset=utf-8";
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider        = provider,
        RequestPath         = "/pro",
        ContentTypeProvider = proContentTypes
    });
}
else
{
    Console.WriteLine("[WARN] wwwroot_pro NOT FOUND at " + proPath);
}

// Portal static files
var portalPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot_portal");
Console.WriteLine("[INFO] Expected /portal at " + portalPath);
if (Directory.Exists(portalPath))
{
    var portalProvider = new PhysicalFileProvider(portalPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider     = portalProvider,
        RequestPath      = "/portal",
        DefaultFileNames = new List<string> { "login.html" }
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider        = portalProvider,
        RequestPath         = "/portal",
        ContentTypeProvider = new FileExtensionContentTypeProvider()
    });
}
else
{
    Console.WriteLine("[WARN] wwwroot_portal NOT FOUND at " + portalPath);
}

// 2. Routing APRÈS les fichiers statiques
app.UseRouting();

// 3. Logging middleware
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

// 4. Register all endpoint groups
app.MapAuthEndpoints();
app.MapRecycleEndpoints(recyclePath, recycleDays);
app.MapMiscEndpoints();
app.MapJobsEndpoints(recyclePath);
app.MapDeliveryEndpoints();
app.MapFabricationEndpoints();
app.MapNotificationEndpoints();
app.MapDossiersEndpoints();
app.MapSettingsEndpoints(recyclePath);
app.MapReportsEndpoints();
app.MapMailImportEndpoints();

// Portal API endpoints
app.MapPortalAuthEndpoints();
app.MapPortalOrdersEndpoints();
app.MapPortalBatEndpoints();
app.MapPortalAccountEndpoints();

// 5. Routes /pro
app.MapGet("/pro", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/pro/index.html");
    return Task.CompletedTask;
});

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

// 5b. Routes /portal
app.MapGet("/portal", (HttpContext ctx) =>
{
    ctx.Response.Redirect("/portal/login.html");
    return Task.CompletedTask;
});

if (Directory.Exists(portalPath))
{
    app.MapFallback("/portal/{*path}", async (HttpContext ctx) =>
    {
        if (Path.HasExtension(ctx.Request.Path))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(Path.Combine(portalPath, "login.html"));
    });
}

// 6. Debug endpoint listing
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

// 7. GC.KeepAlive AVANT app.Run()
GC.KeepAlive(tempCopyWatcher);

// 8. Run en dernier
app.Run();
