using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using MongoDB.Bson;
using GestionAtelier.Models;
using GestionAtelier.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GestionAtelier.Endpoints;

/// <summary>
/// Endpoints supporting the "fiche sans PDF" submission process:
///  - a customizable substitution PDF (admin, configured from the "Fiche de production" tab)
///  - creating a blank production sheet without importing a real PDF
///  - replacing the substitution PDF with the final PDF once it is available
/// </summary>
public static class SubmissionBlankEndpoints
{
    private const string SettingsKey = "substitutionPdf";
    private const string SoumissionFolder = "Soumission";

    private static string SubstitutionDir() =>
        Path.Combine(BackendUtils.HotfoldersRoot(), "_substitution");

    private static string DefaultSubstitutionPath() =>
        Path.Combine(SubstitutionDir(), "substitution.pdf");

    /// <summary>Returns the path to the substitution PDF, generating a built-in default if none is configured.</summary>
    private static string GetOrCreateSubstitutionPdfPath()
    {
        var cfg = MongoDbHelper.GetSettings<SubstitutionPdfSettings>(SettingsKey);
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Path) && File.Exists(cfg.Path))
            return cfg.Path!;

        // Generate a default placeholder PDF.
        var dir = SubstitutionDir();
        Directory.CreateDirectory(dir);
        var path = DefaultSubstitutionPath();
        GenerateDefaultSubstitutionPdf(path);

        MongoDbHelper.UpsertSettings(SettingsKey, new SubstitutionPdfSettings
        {
            FileName = "substitution-par-defaut.pdf",
            Path = path,
            UpdatedAt = DateTime.UtcNow
        });
        return path;
    }

    private static void GenerateDefaultSubstitutionPdf(string path)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.Content().AlignCenter().AlignMiddle().Column(col =>
                {
                    col.Spacing(14);
                    col.Item().AlignCenter().Text("PDF de substitution").FontSize(28).Bold().FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text("Fiche créée sans PDF").FontSize(16).FontColor(Colors.Grey.Medium);
                    col.Item().AlignCenter().Text("En attente du fichier final").FontSize(14).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf(path);
    }

    public static void MapSubmissionBlankEndpoints(this WebApplication app)
    {
        // ── GET substitution PDF config ───────────────────────────────────────
        app.MapGet("/api/settings/substitution-pdf", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var cfg = MongoDbHelper.GetSettings<SubstitutionPdfSettings>(SettingsKey);
                var configured = cfg != null && !string.IsNullOrWhiteSpace(cfg.Path) && File.Exists(cfg.Path);
                return Results.Json(new
                {
                    ok = true,
                    configured,
                    fileName = cfg?.FileName,
                    path = configured ? cfg!.Path : null,
                    updatedAt = cfg?.UpdatedAt
                });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Upload / replace substitution PDF (admin only) ────────────────────
        app.MapPost("/api/settings/substitution-pdf", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null)
                    return Results.Json(new { ok = false, error = "Aucun fichier reçu" });
                if (!file.FileName.ToLowerInvariant().EndsWith(".pdf"))
                    return Results.Json(new { ok = false, error = "Seuls les PDF sont acceptés" });

                // Validate magic bytes (%PDF)
                byte[] header = new byte[4];
                using (var s = file.OpenReadStream())
                {
                    int read = await s.ReadAsync(header, 0, 4);
                    if (read < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46)
                        return Results.Json(new { ok = false, error = "Le fichier n'est pas un PDF valide" });
                }

                var dir = SubstitutionDir();
                Directory.CreateDirectory(dir);
                var path = DefaultSubstitutionPath();
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(fs);

                MongoDbHelper.UpsertSettings(SettingsKey, new SubstitutionPdfSettings
                {
                    FileName = Path.GetFileName(file.FileName),
                    Path = path,
                    UpdatedAt = DateTime.UtcNow
                });

                return Results.Json(new { ok = true, fileName = Path.GetFileName(file.FileName) });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Reset substitution PDF to built-in default (admin only) ───────────
        app.MapDelete("/api/settings/substitution-pdf", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                MongoDbHelper.DeleteSettings(SettingsKey);
                try { if (File.Exists(DefaultSubstitutionPath())) File.Delete(DefaultSubstitutionPath()); } catch { }
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Create a blank production sheet (no PDF import) ────────────────────
        app.MapPost("/api/soumission/create-blank", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var substitutionPath = GetOrCreateSubstitutionPdfPath();
                if (!File.Exists(substitutionPath))
                    return Results.Json(new { ok = false, error = "PDF de substitution introuvable" });

                var root = BackendUtils.HotfoldersRoot();
                var destDir = Path.Combine(root, SoumissionFolder);
                Directory.CreateDirectory(destDir);

                long numero = MongoDbHelper.GetNextFileNumber();
                var numeroStr = numero.ToString("D5");
                var fileName = $"{numeroStr}_fiche-sans-pdf.pdf";
                var destPath = Path.Combine(destDir, fileName);
                File.Copy(substitutionPath, destPath, overwrite: true);

                // Seed a fabrication record flagged as a placeholder ("sans PDF").
                var userName = AuthHelper.GetClaim(ctx, "name") ?? "Système";
                var sheet = new FabricationSheet
                {
                    FullPath = destPath,
                    FileName = fileName,
                    NumeroDossier = numeroStr,
                    History = new List<FabricationHistory>
                    {
                        new() { Date = DateTime.Now, User = userName, Action = "Création fiche (sans PDF)" }
                    }
                };
                BackendUtils.UpsertFabrication(sheet);

                // Flag the record so the UI knows this is a placeholder awaiting the final PDF.
                var fabCol = MongoDbHelper.GetFabricationsCollection();
                fabCol.UpdateMany(
                    Builders<BsonDocument>.Filter.Eq("fileName", fileName.ToLowerInvariant()),
                    Builders<BsonDocument>.Update.Set("sansPdf", true).Set("substitutionPdf", true));

                return Results.Json(new { ok = true, fullPath = destPath, fileName });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Replace the substitution PDF with the real final PDF ──────────────
        app.MapPost("/api/soumission/replace-pdf", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" });

                var form = await ctx.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null)
                    return Results.Json(new { ok = false, error = "Aucun fichier reçu" });
                if (!file.FileName.ToLowerInvariant().EndsWith(".pdf"))
                    return Results.Json(new { ok = false, error = "Seuls les PDF sont acceptés" });

                var fullPath = form["fullPath"].ToString();
                var fileNameKey = form["fileName"].ToString();

                // Resolve the current on-disk path (fullPath may be stale after a Kanban move).
                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                {
                    var lookupName = !string.IsNullOrWhiteSpace(fileNameKey)
                        ? fileNameKey
                        : (string.IsNullOrWhiteSpace(fullPath) ? "" : Path.GetFileName(fullPath));
                    if (!string.IsNullOrWhiteSpace(lookupName))
                    {
                        var found = Directory.GetFiles(BackendUtils.HotfoldersRoot(), lookupName, SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null) fullPath = found;
                    }
                }

                if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                    return Results.Json(new { ok = false, error = "Fiche introuvable sur le disque" });

                // Validate magic bytes (%PDF)
                byte[] header = new byte[4];
                using (var s = file.OpenReadStream())
                {
                    int read = await s.ReadAsync(header, 0, 4);
                    if (read < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46)
                        return Results.Json(new { ok = false, error = "Le fichier n'est pas un PDF valide" });
                }

                // Overwrite the placeholder file, keeping the same name so the fabrication stays linked.
                using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await file.CopyToAsync(fs);

                var name = Path.GetFileName(fullPath).ToLowerInvariant();
                var fabCol = MongoDbHelper.GetFabricationsCollection();
                fabCol.UpdateMany(
                    Builders<BsonDocument>.Filter.Or(
                        Builders<BsonDocument>.Filter.Eq("fileName", name),
                        Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)),
                    Builders<BsonDocument>.Update.Set("sansPdf", false).Set("substitutionPdf", false));

                return Results.Json(new { ok = true, fullPath, fileName = Path.GetFileName(fullPath) });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
