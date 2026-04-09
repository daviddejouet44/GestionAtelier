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

namespace GestionAtelier.Endpoints;

public static class FabricationEndpointsExtensions
{
    public static void MapFabricationEndpoints(this WebApplication app)
    {
app.MapGet("/api/fabrication", (string? fullPath, string? fileName) =>
{
    try
    {
    FabricationSheet? sheet = null;
    bool locked = false;

    var fabCol = MongoDbHelper.GetFabricationsCollection();
    BsonDocument? rawDoc = null;

    if (!string.IsNullOrWhiteSpace(fullPath))
    {
        rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fullPath", fullPath)).FirstOrDefault();
        if (rawDoc == null)
        {
            var fn = Path.GetFileName(fullPath)?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(fn))
                rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", fn)).SortByDescending(x => x["_id"]).FirstOrDefault();
        }
        if (rawDoc != null) sheet = BackendUtils.BsonDocToFabricationSheet(rawDoc);
    }
    if (sheet == null && !string.IsNullOrWhiteSpace(fileName))
    {
        var lf = (fileName ?? "").ToLowerInvariant();
        rawDoc = fabCol.Find(Builders<BsonDocument>.Filter.Eq("fileName", lf)).SortByDescending(x => x["_id"]).FirstOrDefault();
        if (rawDoc != null) sheet = BackendUtils.BsonDocToFabricationSheet(rawDoc);
    }

    if (sheet != null)
    {
        locked = rawDoc != null && rawDoc.Contains("locked") && rawDoc["locked"] != BsonNull.Value
            && rawDoc["locked"].BsonType == BsonType.Boolean && rawDoc["locked"].AsBoolean;
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var json = System.Text.Json.JsonSerializer.Serialize(sheet, opts);
        using var doc2 = System.Text.Json.JsonDocument.Parse(json);
        var root2 = doc2.RootElement;
        var merged = new Dictionary<string, object?>();
        foreach (var prop in root2.EnumerateObject())
            merged[prop.Name] = (object?)prop.Value;
        merged["locked"] = locked;
        return Results.Json(merged);
    }

    return Results.Json(new { ok = false, error = "Aucune fiche de fabrication." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERR] GET /api/fabrication: {ex.Message}");
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.MapPut("/api/fabrication", async (HttpContext ctx) =>
{
    try
    {
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
            catch { }
        }

        var input = await ctx.Request.ReadFromJsonAsync<FabricationInput>();
        if (input == null)
            return Results.Json(new { ok = false, error = "JSON vide." });

        if (string.IsNullOrWhiteSpace(input.FullPath) && string.IsNullOrWhiteSpace(input.FileName))
            return Results.Json(new { ok = false, error = "FullPath ou FileName requis." });

        if (!string.IsNullOrWhiteSpace(input.FullPath) && !File.Exists(input.FullPath))
            Console.WriteLine($"[WARN] PUT /api/fabrication: File not found at {input.FullPath}, saving anyway (may have been moved).);

        if (string.IsNullOrWhiteSpace(input.FullPath) && !string.IsNullOrWhiteSpace(input.FileName))
        {
            var existing = BackendUtils.FindFabricationByName(input.FileName);
            input.FullPath = existing?.FullPath is { Length: > 0 } ? existing.FullPath : (input.FileName ?? "");
        }

        var old = BackendUtils.FindFabrication(input.FullPath);
        if (old == null && !string.IsNullOrWhiteSpace(input.FileName))
            old = BackendUtils.FindFabricationByName(input.FileName);

        var isAdmin = (userProfile == 3);

        // For fields always sent by the frontend, use input value directly (even if null/empty)
        // so that clearing a field actually persists as empty in the database.
        // Only use ?? old?.Value for fields that are NOT always sent by the frontend.
        var sheet = new FabricationSheet
        {
            FullPath = input.FullPath,
            FileName = string.IsNullOrWhiteSpace(input.FileName)
                ? Path.GetFileName(input.FullPath)
                : input.FileName,

            // Fields always sent by the JS frontend — use input directly
            MoteurImpression = input.MoteurImpression,
            Machine          = input.MoteurImpression ?? input.Machine,
            Operateur        = input.Operateur,
            Quantite         = input.Quantite,
            TypeTravail      = input.TypeTravail,
            Format           = input.Format,
            RectoVerso       = input.RectoVerso,
            Client           = input.Client,
            NumeroDossier    = input.NumeroDossier,
            Notes            = input.Notes,
            Faconnage        = input.Faconnage,
            Delai            = input.Delai,
            Media1           = input.Media1,
            Media2           = input.Media2,
            Media3           = input.Media3,
            Media4           = input.Media4,

            // Fields NOT sent by the JS frontend — keep old value as fallback
            Papier           = input.Papier           ?? old?.Papier,
            Encres           = input.Encres           ?? old?.Encres,
            NumeroAffaire    = input.NumeroAffaire    ?? old?.NumeroAffaire,
            Livraison        = input.Livraison        ?? old?.Livraison,

            // Admin-only fields — only profile 3 can update
            TypeDocument   = isAdmin ? input.TypeDocument   : old?.TypeDocument,
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

        if (!string.IsNullOrWhiteSpace(sheet.NumeroDossier))
        {
            try
            {
                var pfCol = MongoDbHelper.GetCollection<BsonDocument>("productionFolders");
                var pfFilter = Builders<BsonDocument>.Filter.Or(
                    Builders<BsonDocument>.Filter.Eq("originalFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("currentFilePath", sheet.FullPath),
                    Builders<BsonDocument>.Filter.Eq("fileName", sheet.FileName)
                );
                var pfDoc = pfCol.Find(pfFilter).FirstOrDefault();
                if (pfDoc != null)
                {
                    var oldNumeroDossier = pfDoc.Contains("numeroDossier") && pfDoc["numeroDossier"] != BsonNull.Value
                        ? pfDoc["numeroDossier"].AsString : null;

                    var pfUpdate = Builders<BsonDocument>.Update.Set("numeroDossier", sheet.NumeroDossier);
                    pfCol.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", pfDoc["_id"]), pfUpdate);

                    if (oldNumeroDossier != sheet.NumeroDossier)
                    {
                        var existingFolderPath = pfDoc.Contains("folderPath") ? pfDoc["folderPath"].AsString : "";
                        if (!string.IsNullOrEmpty(existingFolderPath) && Directory.Exists(existingFolderPath))
                        {
                            var safeName = BackendUtils.SafeNameRegex.Replace(Path.GetFileNameWithoutExtension(sheet.FileName ?? ""), "_");
                            var newFolderName = $"{sheet.NumeroDossier}_{safeName}";
                            var parentDir = Path.GetDirectoryName(existingFolderPath) ?? "";
                            var newFolderPath = Path.Combine(parentDir, newFolderName);
                            if (!string.Equals(existingFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase)
                                && !Directory.Exists(newFolderPath))
                            {
                                Directory.Move(existingFolderPath, newFolderPath);
                                pfCol.UpdateOne(
                                    Builders<BsonDocument>.Filter.Eq("_id", pfDoc["_id"]),
                                    Builders<BsonDocument>.Update.Set("folderPath", newFolderPath));
                            }
                        }
                    }
                }
            }
            catch (Exception exPf)
            {
                Console.WriteLine($"[WARN] SyncNumeroDossierToProductionFolder failed: {exPf.Message}");
            }
        }

        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

    }
}