// ======================================================
// Program.cs — entry point (refactored modular architecture)
// ======================================================

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
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

// ── Upload size limit ────────────────────────────────────────────────────────
var maxUploadMb = int.TryParse(Environment.GetEnvironmentVariable("MAX_UPLOAD_MB"), out var mb) ? mb : 500;
var maxBytes = (long)maxUploadMb * 1024 * 1024;

builder.WebHost.UseKestrel(k =>
{
    k.ListenAnyIP(5080, o => o.Protocols = HttpProtocols.Http1AndHttp2);
    k.Limits.MaxRequestBodySize = maxBytes;
});

var recycleEnabled = builder.Configuration["RecycleBin:Enabled"] == "true";
var hotfoldersRootForRecycle = Environment.GetEnvironmentVariable("GA_HOTFOLDERS_ROOT") is { Length: > 0 } env ? Path.GetFullPath(env) : @"C:\Flux";
var recyclePath    = builder.Configuration["RecycleBin:Path"] ?? Path.Combine(hotfoldersRootForRecycle, "Corbeille");
var recycleDays    = int.TryParse(builder.Configuration["RecycleBin:DaysToKeep"], out var d) ? d : 7;
Directory.CreateDirectory(recyclePath);

// Remove form body size limit so large PDF uploads in coupled submission are not rejected
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxBytes;
    o.ValueLengthLimit         = int.MaxValue;
    o.ValueCountLimit          = int.MaxValue;
});

// ── JWT Authentication ───────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = AuthHelper.GetSigningKey(),
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ClockSkew                = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddHostedService<GestionAtelier.Services.DailyReportService>();
builder.Services.AddHostedService<GestionAtelier.Services.MachineTelemetryPollingService>();
builder.Services.AddSingleton<GestionAtelier.Services.OrderSourcePollingService>();
// Temporarily disable OrderSourcePollingService for demo
//builder.Services.AddHostedService(sp => sp.GetRequiredService<GestionAtelier.Services.OrderSourcePollingService>());

// Point 12: Rate limiter to protect /api/auth/login against brute-force
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Point 14: HSTS configuration for production
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Point 15: IHttpClientFactory to avoid socket exhaustion
builder.Services.AddHttpClient("external", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Point 14: Force HTTPS in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// Point 11: HTTP security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    await next();
});

// Point 12: Apply rate limiter middleware
app.UseRateLimiter();

QuestPDF.Settings.License = LicenseType.Community;

Console.WriteLine("[INFO] ContentRoot = " + app.Environment.ContentRootPath);

// Warn if license public key is not configured
try
{
    var _ = LicenseService.GetCurrent(); // triggers key loading; logs warning if missing
}
catch (Exception licEx)
{
    Console.WriteLine($"[WARN] License configuration: {licEx.Message}");
}

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

    app.MapGet("/bat-review.html", async (HttpContext ctx) =>
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(Path.Combine(proPath, "bat-review.html"));
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
app.UseAuthentication();
app.UseAuthorization();

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

// One-time migration: copy legacy PortalClientStep.EmailTemplateKey → KanbanColumnConfig.EmailTemplateKeys
try
{
    var stepsCfg  = MongoDbHelper.GetSettings<PortalClientStepsConfig>("portalClientSteps");
    var kanbanCfg = MongoDbHelper.GetSettings<KanbanSettings>("kanbanColumns");
    if (stepsCfg != null && kanbanCfg != null)
    {
        bool kanbanDirty = false;
        bool stepsDirty  = false;
        foreach (var step in stepsCfg.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.EmailTemplateKey)) continue;
            var col = kanbanCfg.Columns.FirstOrDefault(c =>
                string.Equals(c.Folder, step.KanbanFolder, StringComparison.OrdinalIgnoreCase));
            if (col != null)
            {
                col.EmailTemplateKeys ??= new List<string>();
                if (!col.EmailTemplateKeys.Contains(step.EmailTemplateKey))
                {
                    col.EmailTemplateKeys.Add(step.EmailTemplateKey);
                    kanbanDirty = true;
                    Console.WriteLine($"[MIGRATION] Copied emailTemplateKey '{step.EmailTemplateKey}' from step '{step.KanbanFolder}' to KanbanColumnConfig.");
                }
            }
            step.EmailTemplateKey = "";
            stepsDirty = true;
        }
        if (kanbanDirty) MongoDbHelper.UpsertSettings("kanbanColumns", kanbanCfg);
        if (stepsDirty)  MongoDbHelper.UpsertSettings("portalClientSteps", stepsCfg);
    }
}
catch (Exception migEx) { Console.WriteLine($"[WARN] EmailTemplateKey migration failed: {migEx.Message}"); }

// 4. Register all endpoint groups
app.MapAuthEndpoints();
app.MapDashboardEndpoints();
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
app.MapLicenseEndpoints();
app.MapSearchEndpoints();
app.MapPlanningEndpoints();
app.MapMachineStatusEndpoints();
app.MapMachinePilotageEndpoints();
app.MapStockEndpoints();
app.MapKpiEndpoints();

// Portal API endpoints
app.MapPortalAuthEndpoints();
app.MapPortalOrdersEndpoints();
app.MapPortalBatEndpoints();
app.MapPortalAccountEndpoints();
app.MapQuoteLinksEndpoints();

// Submission XML coupling + ERP/W2P lookup
app.MapSubmissionXmlEndpoints();
app.MapExternalLookupEndpoints();

// "Fiche sans PDF" process: substitution PDF, blank sheet creation, final PDF replacement
app.MapSubmissionBlankEndpoints();

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

// Point 16: Ensure MongoDB indexes exist
try { MongoDbIndexes.EnsureIndexes(); }
catch (Exception ex) { Console.WriteLine($"[WARN] Index creation: {ex.Message}"); }

// 8. Run en dernier
app.Run();
