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
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GestionAtelier.Endpoints.Fabrication;

public static class FabricationPdfEndpoints
{
    public static void MapFabricationPdfEndpoints(this WebApplication app)
    {
app.MapGet("/api/fabrication/pdf", (string? fullPath, string? fileName, bool? save, HttpContext ctx) =>
{
    try
    {
        FabricationSheet? sheet = null;
        if (!string.IsNullOrEmpty(fullPath))
            sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null && !string.IsNullOrEmpty(fileName))
            sheet = BackendUtils.FindFabricationByName(fileName);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        var doc = PdfUtils.CreateFabricationPdf(sheet, MongoDbHelper.GetSettings<FabricationFormConfig>("formConfig"), baseUrl);
        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        ms.Position = 0;
        var pdfBytes = ms.ToArray();

        // Optionally save PDF to production folder (dossier de production)
        if (save == true)
        {
            try
            {
                // Look up production folder for this file
                var safeFileName = Path.GetFileName(sheet.FileName ?? "");
                var baseName = Path.GetFileNameWithoutExtension(safeFileName);
                bool savedToProductionFolder = false;

                var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                BsonDocument? pfDoc = null;
                if (!string.IsNullOrEmpty(safeFileName))
                    pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", safeFileName))
                                .SortByDescending(x => x["createdAt"]).FirstOrDefault();
                if (pfDoc == null && !string.IsNullOrEmpty(sheet.NumeroDossier))
                    pfDoc = pfCol.Find(Builders<BsonDocument>.Filter.Eq("numeroDossier", sheet.NumeroDossier))
                                .SortByDescending(x => x["createdAt"]).FirstOrDefault();

                if (pfDoc != null && pfDoc.Contains("folderPath") && !string.IsNullOrEmpty(pfDoc["folderPath"].AsString))
                {
                    var prodFolderPath = pfDoc["folderPath"].AsString;
                    if (Directory.Exists(prodFolderPath))
                    {
                        var pdfPath = Path.Combine(prodFolderPath, $"{baseName}_FicheFabrication.pdf");
                        File.WriteAllBytes(pdfPath, pdfBytes);
                        savedToProductionFolder = true;
                        Console.WriteLine($"[PDF] Fiche enregistrée dans le dossier de production : {pdfPath}");
                    }
                }

                if (!savedToProductionFolder)
                {
                    Console.WriteLine($"[WARN] Dossier de production introuvable pour {safeFileName} — PDF non sauvegardé sur disque");
                }
            }
            catch (Exception saveEx)
            {
                Console.WriteLine($"[WARN] PDF save failed: {saveEx.Message}");
            }
        }

        return Results.File(pdfBytes, "application/pdf", $"FicheFabrication-{sheet.FileName}.pdf");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

// GET /api/admin/jobs/delivery-labels-pdf?fileName=...
// Génère un PDF d'étiquettes de livraison (1 par point de livraison + 1 par adresse justif)
app.MapGet("/api/admin/jobs/delivery-labels-pdf", (string? fileName, string? fullPath, HttpContext ctx) =>
{
    try
    {
        FabricationSheet? sheet = null;
        if (!string.IsNullOrEmpty(fullPath))
            sheet = BackendUtils.FindFabrication(fullPath);
        if (sheet == null && !string.IsNullOrEmpty(fileName))
            sheet = BackendUtils.FindFabricationByName(fileName);
        if (sheet == null)
            return Results.Json(new { ok = false, error = "Fiche introuvable" });

        // Collect delivery addresses
        var labels = new List<(string Adresse, int? Quantite, bool IsJustif)>();

        // Priority 1: Repartitions (multi-delivery points)
        if (sheet.Repartitions != null && sheet.Repartitions.Count > 0)
        {
            foreach (var r in sheet.Repartitions)
                if (!string.IsNullOrWhiteSpace(r.Adresse))
                    labels.Add((r.Adresse, r.Quantite, false));
        }
        else if (!string.IsNullOrWhiteSpace(sheet.AdresseLivraison))
        {
            // Fallback: single delivery address
            labels.Add((sheet.AdresseLivraison, sheet.Quantite, false));
        }

        // Justificatifs address
        if (!string.IsNullOrWhiteSpace(sheet.JustifsClientsAdresse))
            labels.Add((sheet.JustifsClientsAdresse, sheet.JustifsClientsQuantite, true));

        if (labels.Count == 0)
            return Results.Json(new { ok = false, error = "Aucune adresse de livraison trouvée" });

        // Retrieve sender name from portal theme settings (company name)
        var portalTheme = MongoDbHelper.GetSettings<GestionAtelier.Models.PortalThemeConfig>("portalTheme");
        var expediteur = portalTheme?.CompanyName;
        if (string.IsNullOrWhiteSpace(expediteur)) expediteur = "Expéditeur";

        var numeroDossier = sheet.NumeroDossier ?? sheet.FileName ?? "";

        var doc = Document.Create(container =>
        {
            foreach (var (adresse, quantite, isJustif) in labels)
            {
                container.Page(page =>
                {
                    // A6 landscape for label (148mm x 105mm)
                    page.Size(PageSizes.A6.Landscape());
                    page.Margin(12, QuestPDF.Infrastructure.Unit.Millimetre);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Border(2).BorderColor("#111827").Padding(10).Column(col =>
                    {
                        // Header: expediteur + dossier N°
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text(expediteur).FontSize(9).FontColor("#6b7280");
                            row.AutoItem().AlignRight().Text($"N° {numeroDossier}").FontSize(9).FontColor("#6b7280");
                        });

                        col.Item().PaddingVertical(4).LineHorizontal(1).LineColor("#d1d5db");

                        // Destination address (large)
                        col.Item().PaddingTop(6).Text(adresse.Trim()).FontSize(13).SemiBold().LineHeight(1.4f);

                        col.Item().PaddingVertical(4);

                        // Footer row: quantity + justif tag
                        col.Item().Row(row =>
                        {
                            if (quantite.HasValue && quantite.Value > 0)
                                row.RelativeItem().AlignBottom().Text($"Qté : {quantite.Value}").FontSize(10).SemiBold();
                            else
                                row.RelativeItem();
                            if (isJustif)
                                row.AutoItem().AlignRight().AlignBottom().Background("#dc2626").Padding(3)
                                    .Text("JUSTIFICATIF").FontSize(9).FontColor("white");
                        });
                    });
                });
            }
        });

        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        ms.Position = 0;
        var pdfBytes = ms.ToArray();

        ctx.Response.Headers["Content-Disposition"] = $"inline; filename=\"EtiquettesLivraison-{numeroDossier}.pdf\"";
        return Results.File(pdfBytes, "application/pdf");
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

    }
}
