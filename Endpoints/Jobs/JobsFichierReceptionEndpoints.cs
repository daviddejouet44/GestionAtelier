using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Jobs;

// ======================================================
// Alertes « Date de réception du fichier » (dateReceptionFichier)
// Analogue au planning BAT : signale les fiches dont la date d'échéance
// est dans le futur proche ou déjà dépassée.
//
// GET  /api/fichier-reception/planning  — liste des fiches à surveiller
// GET  /api/config/fichier-reception    — lire la configuration (seuil en heures)
// PUT  /api/config/fichier-reception    — écrire la configuration
// ======================================================
public static class JobsFichierReceptionEndpoints
{
    private const string CONFIG_KEY = "fichierReceptionConfig";

    private static FichierReceptionConfig LoadConfig()
    {
        try
        {
            var saved = MongoDbHelper.GetSettings<FichierReceptionConfig>(CONFIG_KEY);
            if (saved != null) return saved;
        }
        catch { }
        return new FichierReceptionConfig { AlertHours = 24 };
    }

    public static void MapJobsFichierReceptionEndpoints(this WebApplication app)
    {
        // ── Configuration ────────────────────────────────────────────────────────
        app.MapGet("/api/config/fichier-reception", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);
                var cfg = LoadConfig();
                return Results.Json(new { ok = true, config = cfg });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/config/fichier-reception", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });
                var input = await ctx.Request.ReadFromJsonAsync<FichierReceptionConfig>();
                if (input == null) return Results.Json(new { ok = false, error = "Corps requis" });
                var hours = input.AlertHours < 1 ? 24 : input.AlertHours;
                MongoDbHelper.UpsertSettings(CONFIG_KEY, new FichierReceptionConfig { AlertHours = hours });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Planning des alertes ─────────────────────────────────────────────────
        app.MapGet("/api/fichier-reception/planning", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var cfg = LoadConfig();
                var alertHours = cfg.AlertHours < 1 ? 24 : cfg.AlertHours;
                var now = DateTime.UtcNow;
                var threshold = now.AddHours(alertHours);

                var fabCol = MongoDbHelper.GetFabricationsCollection();

                // Chercher les fiches ayant une dateReceptionFichier renseignée
                var filter = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Exists("dateReceptionFichier"),
                    Builders<BsonDocument>.Filter.Ne("dateReceptionFichier", BsonNull.Value)
                );
                var docs = fabCol.Find(filter).ToList();

                var alerts = new List<object>();
                foreach (var doc in docs)
                {
                    DateTime? dateRec = null;
                    try
                    {
                        if (doc.Contains("dateReceptionFichier") && doc["dateReceptionFichier"] != BsonNull.Value
                            && doc["dateReceptionFichier"].BsonType == BsonType.DateTime)
                            dateRec = doc["dateReceptionFichier"].ToUniversalTime();
                    }
                    catch { }

                    if (dateRec == null) continue;

                    // Signaler uniquement si dans la fenêtre d'alerte ou dépassé
                    if (dateRec.Value > threshold) continue;

                    string G(string f) => doc.Contains(f) && doc[f] != BsonNull.Value && doc[f].IsString ? doc[f].AsString : "";
                    var diffHours = (dateRec.Value - now).TotalHours;
                    var overdue = diffHours < 0;

                    alerts.Add(new
                    {
                        fileName        = G("fileName"),
                        numeroDossier   = G("numeroDossier"),
                        nomClient       = G("nomClient"),
                        dateReceptionFichier = dateRec.Value,
                        overdue,
                        hoursRemaining  = Math.Round(diffHours, 1)
                    });
                }

                // Trier : d'abord les dépassés (plus ancien en tête), puis les imminents
                alerts = alerts.OrderBy(a => ((dynamic)a).dateReceptionFichier).ToList<object>();

                return Results.Json(new { ok = true, alerts, count = alerts.Count, alertHours });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}

/// <summary>Configuration du seuil d'alerte « date de réception du fichier ».</summary>
public class FichierReceptionConfig
{
    [System.Text.Json.Serialization.JsonPropertyName("alertHours")]
    public int AlertHours { get; set; } = 24;
}
