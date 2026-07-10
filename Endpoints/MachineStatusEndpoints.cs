using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// Suivi temps réel des machines (point 3)
// GET /api/machines/status  — état de toutes les machines (catalogue fusionné)
// PUT /api/machines/status  — met à jour l'état d'une machine
// ======================================================
public static class MachineStatusEndpoints
{
    public static void MapMachineStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/api/machines/status", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                // Catalogue des moteurs (avec repli sur les valeurs par défaut).
                var engines = MongoDbHelper.GetPrintEngines();
                if (engines.Count == 0)
                    engines = new List<string> { "Offset", "Numérique" };

                var statusCol = MongoDbHelper.GetCollection<BsonDocument>("machineStatus");
                var statusDocs = statusCol.Find(Builders<BsonDocument>.Filter.Empty).ToList()
                    .Where(d => d.Contains("moteur") && d["moteur"] != BsonNull.Value)
                    .ToDictionary(d => d["moteur"].AsString, d => d, StringComparer.OrdinalIgnoreCase);

                // Fusionne catalogue + statuts persistés ; les moteurs sans doc apparaissent "Disponible".
                var known = new HashSet<string>(engines, StringComparer.OrdinalIgnoreCase);
                foreach (var extra in statusDocs.Keys) known.Add(extra);

                var list = known.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).Select(moteur =>
                {
                    statusDocs.TryGetValue(moteur, out var d);
                    string S(string f) => d != null && d.Contains(f) && d[f] != BsonNull.Value && d[f].IsString ? d[f].AsString : "";
                    long L(string f) { try { return d != null && d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToInt64() : 0L; } catch { return 0L; } }
                    int I(string f) { try { return d != null && d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToInt32() : 0; } catch { return 0; } }
                    DateTime? T(string f) { try { return d != null && d.Contains(f) && d[f] != BsonNull.Value ? d[f].ToUniversalTime() : (DateTime?)null; } catch { return null; } }

                    var statut = S("statut");
                    if (!MachineStatuses.IsValid(statut)) statut = "Disponible";

                    return new
                    {
                        moteur,
                        statut,
                        papierCharge = S("papierCharge"),
                        compteurFeuilles = L("compteurFeuilles"),
                        ofEnCours = S("ofEnCours"),
                        ofEnCoursDossier = S("ofEnCoursDossier"),
                        tempsRestantMinutes = I("tempsRestantMinutes"),
                        note = S("note"),
                        updatedAt = T("updatedAt"),
                        updatedBy = S("updatedBy")
                    };
                }).ToList();

                return Results.Json(new { ok = true, statuses = MachineStatuses.All, machines = list });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/machines/status", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (json.ValueKind != JsonValueKind.Object)
                    return Results.Json(new { ok = false, error = "Corps JSON requis" });

                var moteur = json.TryGetProperty("moteur", out var mEl) ? (mEl.GetString() ?? "").Trim() : "";
                if (string.IsNullOrWhiteSpace(moteur))
                    return Results.Json(new { ok = false, error = "moteur requis" });

                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("moteur", moteur),
                    Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow),
                    Builders<BsonDocument>.Update.Set("updatedBy",
                        AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "")
                };

                if (json.TryGetProperty("statut", out var stEl))
                {
                    var st = stEl.GetString() ?? "";
                    if (!MachineStatuses.IsValid(st))
                        return Results.Json(new { ok = false, error = $"Statut invalide : {st}" });
                    // Normalise la casse sur la valeur canonique.
                    var canonical = MachineStatuses.All.First(x => string.Equals(x, st, StringComparison.OrdinalIgnoreCase));
                    updates.Add(Builders<BsonDocument>.Update.Set("statut", canonical));
                }
                if (json.TryGetProperty("papierCharge", out var pEl))
                    updates.Add(Builders<BsonDocument>.Update.Set("papierCharge", pEl.GetString() ?? ""));
                if (json.TryGetProperty("compteurFeuilles", out var cEl) && cEl.TryGetInt64(out var cVal))
                    updates.Add(Builders<BsonDocument>.Update.Set("compteurFeuilles", Math.Max(0, cVal)));
                if (json.TryGetProperty("note", out var nEl))
                    updates.Add(Builders<BsonDocument>.Update.Set("note", nEl.GetString() ?? ""));

                // OF en cours : renseigne le dossier + le temps restant depuis la fiche si possible.
                if (json.TryGetProperty("ofEnCours", out var ofEl))
                {
                    var ofFile = (ofEl.GetString() ?? "").Trim();
                    updates.Add(Builders<BsonDocument>.Update.Set("ofEnCours", ofFile));
                    string dossier = "";
                    int temps = 0;
                    if (!string.IsNullOrWhiteSpace(ofFile))
                    {
                        var fabCol = MongoDbHelper.GetFabricationsCollection();
                        var doc = fabCol.Find(GestionAtelier.Endpoints.Fabrication.FabricationCrudEndpoints.BuildFileNameFilter(ofFile))
                            .SortByDescending(x => x["_id"]).FirstOrDefault();
                        if (doc != null)
                        {
                            if (doc.Contains("numeroDossier") && doc["numeroDossier"] != BsonNull.Value && doc["numeroDossier"].IsString)
                                dossier = doc["numeroDossier"].AsString;
                            try { if (doc.Contains("tempsProduitMinutes") && doc["tempsProduitMinutes"] != BsonNull.Value) temps = doc["tempsProduitMinutes"].ToInt32(); } catch { }
                        }
                    }
                    updates.Add(Builders<BsonDocument>.Update.Set("ofEnCoursDossier", dossier));
                    // tempsRestant explicite prioritaire, sinon dérivé de la fiche.
                    if (json.TryGetProperty("tempsRestantMinutes", out var trEl) && trEl.TryGetInt32(out var trVal))
                        updates.Add(Builders<BsonDocument>.Update.Set("tempsRestantMinutes", Math.Max(0, trVal)));
                    else
                        updates.Add(Builders<BsonDocument>.Update.Set("tempsRestantMinutes", temps));
                }
                else if (json.TryGetProperty("tempsRestantMinutes", out var trEl) && trEl.TryGetInt32(out var trVal))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("tempsRestantMinutes", Math.Max(0, trVal)));
                }

                var col = MongoDbHelper.GetCollection<BsonDocument>("machineStatus");
                var filter = Builders<BsonDocument>.Filter.Eq("moteur", moteur);
                await col.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Combine(updates), new UpdateOptions { IsUpsert = true });

                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
