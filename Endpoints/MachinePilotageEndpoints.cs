using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints;

// ======================================================
// Pilotage & remontée temps réel des machines (point 8)
// POST /api/machines/telemetry            — ingestion (agent/passerelle push)
// GET  /api/config/machine-connections    — connexions par machine
// PUT  /api/config/machine-connections    — configurer une connexion (admin)
// GET  /api/config/machine-token          — token d'ingestion (admin)
// POST /api/config/machine-token/rotate   — régénérer le token (admin)
// ======================================================
public static class MachinePilotageEndpoints
{
    internal static IMongoCollection<BsonDocument> Connections() =>
        MongoDbHelper.GetCollection<BsonDocument>("machineConnections");

    internal static string GetOrCreateToken()
    {
        var cfg = MongoDbHelper.GetSettings<MachineTokenConfig>("machineApiToken");
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Token)) return cfg.Token;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        MongoDbHelper.UpsertSettings("machineApiToken", new MachineTokenConfig { Token = token });
        return token;
    }

    public static void MapMachinePilotageEndpoints(this WebApplication app)
    {
        // ── Ingestion de télémétrie (push) ────────────────────────────────
        app.MapPost("/api/machines/telemetry", async (HttpContext ctx) =>
        {
            try
            {
                // Auth : token machine (agents headless) OU JWT (personnel).
                var provided = ctx.Request.Headers["X-Machine-Token"].ToString();
                bool tokenOk = !string.IsNullOrWhiteSpace(provided) &&
                    string.Equals(provided, GetOrCreateToken(), StringComparison.Ordinal);
                if (!tokenOk && !AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié (token machine ou session requis)" }, statusCode: 401);

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (json.ValueKind != JsonValueKind.Object)
                    return Results.Json(new { ok = false, error = "Corps JSON requis" });

                var moteur = json.TryGetProperty("moteur", out var mEl) ? (mEl.GetString() ?? "").Trim() : "";
                string? statut = json.TryGetProperty("statut", out var sEl) ? sEl.GetString() : null;
                long? compteur = json.TryGetProperty("compteurFeuilles", out var cEl) && cEl.TryGetInt64(out var cv) ? cv : (long?)null;
                string? ofEnCours = json.TryGetProperty("ofEnCours", out var oEl) ? oEl.GetString() : null;
                int? tempsRestant = json.TryGetProperty("tempsRestantMinutes", out var tEl) && tEl.TryGetInt32(out var tv) ? tv : (int?)null;
                string? note = json.TryGetProperty("note", out var nEl) ? nEl.GetString() : null;

                var by = AuthHelper.GetClaim(ctx, "name") ?? AuthHelper.GetClaim(ctx, "login") ?? "agent";
                var (ok, error) = MachineTelemetryService.Ingest(moteur, statut, compteur, ofEnCours, tempsRestant, note,
                    source: tokenOk ? "machine" : "api", by: by);
                if (!ok) return Results.Json(new { ok = false, error });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Connexions par machine ────────────────────────────────────────
        app.MapGet("/api/config/machine-connections", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAuthenticated(ctx))
                    return Results.Json(new { ok = false, error = "Non authentifié" }, statusCode: 401);

                var engines = MongoDbHelper.GetPrintEngines();
                var conns = Connections().Find(Builders<BsonDocument>.Filter.Empty).ToList()
                    .Where(d => d.Contains("moteur") && d["moteur"].IsString)
                    .ToDictionary(d => d["moteur"].AsString, d => d, StringComparer.OrdinalIgnoreCase);

                var known = new HashSet<string>(engines, StringComparer.OrdinalIgnoreCase);
                foreach (var k in conns.Keys) known.Add(k);

                var list = known.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).Select(m =>
                {
                    conns.TryGetValue(m, out var d);
                    return new
                    {
                        moteur = m,
                        protocol = d != null && d.Contains("protocol") && d["protocol"].IsString ? d["protocol"].AsString : "manual",
                        address = d != null && d.Contains("address") && d["address"].IsString ? d["address"].AsString : "",
                        pollIntervalSec = d != null && d.Contains("pollIntervalSec") && d["pollIntervalSec"] != BsonNull.Value ? d["pollIntervalSec"].ToInt32() : 30,
                        enabled = d != null && d.Contains("enabled") && d["enabled"].IsBoolean && d["enabled"].AsBoolean
                    };
                }).ToList();

                return Results.Json(new { ok = true, protocols = MachineProtocols.All, connections = list });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPut("/api/config/machine-connections", async (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });

                var input = await ctx.Request.ReadFromJsonAsync<MachineConnectionInput>();
                if (input == null || string.IsNullOrWhiteSpace(input.Moteur))
                    return Results.Json(new { ok = false, error = "moteur requis" });
                var protocol = MachineProtocols.IsValid(input.Protocol) ? MachineProtocols.Canonical(input.Protocol!) : "manual";
                var interval = Math.Clamp(input.PollIntervalSec ?? 30, 5, 3600);

                var filter = Builders<BsonDocument>.Filter.Eq("moteur", input.Moteur.Trim());
                var update = Builders<BsonDocument>.Update
                    .Set("moteur", input.Moteur.Trim())
                    .Set("protocol", protocol)
                    .Set("address", input.Address ?? "")
                    .Set("pollIntervalSec", interval)
                    .Set("enabled", input.Enabled ?? false)
                    .Set("updatedAt", DateTime.UtcNow);
                await Connections().UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        // ── Token d'ingestion machine ─────────────────────────────────────
        app.MapGet("/api/config/machine-token", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });
                return Results.Json(new { ok = true, token = GetOrCreateToken() });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });

        app.MapPost("/api/config/machine-token/rotate", (HttpContext ctx) =>
        {
            try
            {
                if (!AuthHelper.IsAdmin(ctx))
                    return Results.Json(new { ok = false, error = "Admin uniquement" });
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                MongoDbHelper.UpsertSettings("machineApiToken", new MachineTokenConfig { Token = token });
                return Results.Json(new { ok = true, token });
            }
            catch (Exception ex) { return ErrorHelper.HandleException(ex); }
        });
    }
}
