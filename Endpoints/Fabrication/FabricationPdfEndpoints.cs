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

    }
}
