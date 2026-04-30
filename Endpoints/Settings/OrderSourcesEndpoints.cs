using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;
using GestionAtelier.Services.OrderSources;

namespace GestionAtelier.Endpoints.Settings;

public static class OrderSourcesEndpoints
{
    public static void MapOrderSourcesEndpoints(this WebApplication app)
    {
        // ── Auth helpers ──────────────────────────────────────────────────────
        static bool IsAdmin(HttpContext ctx)
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                return parts.Length >= 3 && parts[2] == "3";
            }
            catch { return false; }
        }

        const string CollectionName = "order_sources";
        const string ImportsCollection = "order_source_imports";

        // ── GET /api/integrations/order-sources ───────────────────────────────
        app.MapGet("/api/integrations/order-sources", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>(CollectionName);
                var docs = col.Find(new BsonDocument()).ToList();
                var sources = docs.Select(d => SafeSourceView(d)).ToList();
                return Results.Json(new { ok = true, sources });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── POST /api/integrations/order-sources ──────────────────────────────
        app.MapPost("/api/integrations/order-sources", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var source = ParseSourceFromBody(body, null);
                source.CreatedAt = DateTime.UtcNow.ToString("O");
                source.UpdatedAt = DateTime.UtcNow.ToString("O");

                var col = MongoDbHelper.GetCollection<BsonDocument>(CollectionName);
                var doc = SourceToBsonDocument(source);
                col.InsertOne(doc);

                return Results.Json(new { ok = true, id = source.Id });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/integrations/order-sources/{id} ──────────────────────────
        app.MapPut("/api/integrations/order-sources/{id}", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>(CollectionName);
                var existing = col.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefault();
                if (existing == null) return Results.Json(new { ok = false, error = "Source introuvable" });

                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                // Preserve old encrypted config if no new credentials provided
                var oldConfigEncrypted = existing.Contains("configEncrypted") ? existing["configEncrypted"].AsString : "";
                var source = ParseSourceFromBody(body, oldConfigEncrypted);
                source.Id = id;
                source.UpdatedAt = DateTime.UtcNow.ToString("O");
                source.CreatedAt = existing.Contains("createdAt") ? existing["createdAt"].AsString : DateTime.UtcNow.ToString("O");

                var doc = SourceToBsonDocument(source);
                col.ReplaceOne(Builders<BsonDocument>.Filter.Eq("_id", id), doc);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── DELETE /api/integrations/order-sources/{id} ───────────────────────
        app.MapDelete("/api/integrations/order-sources/{id}", (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>(CollectionName);
                col.DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", id));
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── POST /api/integrations/order-sources/{id}/test ───────────────────
        app.MapPost("/api/integrations/order-sources/{id}/test", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>(CollectionName);
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Source introuvable" });

                var source = DeserializeSource(doc);
                if (source == null) return Results.Json(new { ok = false, error = "Impossible de désérialiser la source" });

                var configJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
                if (string.IsNullOrEmpty(configJson))
                    return Results.Json(new { ok = false, error = "Configuration manquante" });

                var logger = app.Services.GetRequiredService<ILogger<OrderSourcePollingService>>();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                IOrderSourceProvider provider = source.Type.ToLower() switch
                {
                    "sftp" => new SftpOrderSourceProvider(
                        JsonSerializer.Deserialize<SftpSourceConfig>(configJson)!, logger),
                    "dropbox" => new DropboxOrderSourceProvider(
                        JsonSerializer.Deserialize<DropboxSourceConfig>(configJson)!, logger),
                    "googledrive" => new GoogleDriveOrderSourceProvider(
                        JsonSerializer.Deserialize<GoogleDriveSourceConfig>(configJson)!, logger),
                    "box" => new BoxOrderSourceProvider(
                        JsonSerializer.Deserialize<BoxSourceConfig>(configJson)!, logger, null),
                    "onedrive" => new OneDriveOrderSourceProvider(
                        JsonSerializer.Deserialize<OneDriveSourceConfig>(configJson)!, logger, null),
                    _ => throw new NotSupportedException($"Type non supporté : {source.Type}")
                };

                using (provider)
                {
                    await provider.ConnectAsync(cts.Token);
                    await provider.DisconnectAsync(cts.Token);
                }

                return Results.Json(new { ok = true, message = "Connexion réussie ✅" });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── POST /api/integrations/order-sources/{id}/run ────────────────────
        app.MapPost("/api/integrations/order-sources/{id}/run", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var pollingService = app.Services.GetService<OrderSourcePollingService>();
                if (pollingService == null)
                    return Results.Json(new { ok = false, error = "Service de polling non disponible" });

                // Run in background (don't wait for completion)
                _ = Task.Run(async () =>
                {
                    try { await pollingService.RunSourceNowAsync(id); }
                    catch (Exception ex)
                    {
                        var logger = app.Services.GetRequiredService<ILogger<OrderSourcePollingService>>();
                        logger.LogError(ex, "[OrderSources] Manual run error for {Id}", id);
                    }
                });

                return Results.Json(new { ok = true, message = "Cycle lancé en arrière-plan" });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/order-sources/{id}/logs ────────────────────
        app.MapGet("/api/integrations/order-sources/{id}/logs", (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var limitStr = ctx.Request.Query["limit"].ToString();
                var limit = int.TryParse(limitStr, out var l) ? l : 50;

                var col = MongoDbHelper.GetCollection<BsonDocument>(ImportsCollection);
                var logs = col.Find(Builders<BsonDocument>.Filter.Eq("sourceId", id))
                    .SortByDescending(d => d["_id"])
                    .Limit(limit)
                    .ToList()
                    .Select(SafeLogView)
                    .ToList();

                return Results.Json(new { ok = true, logs });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/order-sources/logs ─────────────────────────
        app.MapGet("/api/integrations/order-sources/logs", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var limitStr = ctx.Request.Query["limit"].ToString();
                var limit = int.TryParse(limitStr, out var l) ? l : 100;
                var sourceIdFilter = ctx.Request.Query["sourceId"].ToString();
                var statusFilter = ctx.Request.Query["status"].ToString();

                var col = MongoDbHelper.GetCollection<BsonDocument>(ImportsCollection);
                var filter = Builders<BsonDocument>.Filter.Empty;
                if (!string.IsNullOrEmpty(sourceIdFilter))
                    filter = Builders<BsonDocument>.Filter.And(filter,
                        Builders<BsonDocument>.Filter.Eq("sourceId", sourceIdFilter));
                if (!string.IsNullOrEmpty(statusFilter))
                    filter = Builders<BsonDocument>.Filter.And(filter,
                        Builders<BsonDocument>.Filter.Eq("status", statusFilter));

                var logs = col.Find(filter)
                    .SortByDescending(d => d["_id"])
                    .Limit(limit)
                    .ToList()
                    .Select(SafeLogView)
                    .ToList();

                return Results.Json(new { ok = true, logs });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/dropbox/authorize ───────────────────────────
        app.MapGet("/api/integrations/dropbox/authorize", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var sourceId = ctx.Request.Query["sourceId"].ToString();
                var dropboxCfg = MongoDbHelper.GetSettings<DropboxGlobalConfig>("dropboxGlobalConfig")
                                 ?? new DropboxGlobalConfig();
                if (string.IsNullOrEmpty(dropboxCfg.AppKey))
                    return Results.Json(new { ok = false, error = "App Key Dropbox non configurée dans les paramètres globaux" });

                var callbackUrl = dropboxCfg.CallbackUrl;
                if (string.IsNullOrEmpty(callbackUrl))
                    callbackUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/dropbox/callback";

                var state = Uri.EscapeDataString($"{sourceId}|{Guid.NewGuid():N}");
                var url = DropboxOrderSourceProvider.GetAuthorizationUrl(dropboxCfg.AppKey, callbackUrl, state);
                return Results.Json(new { ok = true, url });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/dropbox/callback ────────────────────────────
        app.MapGet("/api/integrations/dropbox/callback", async (HttpContext ctx) =>
        {
            try
            {
                var code = ctx.Request.Query["code"].ToString();
                var state = ctx.Request.Query["state"].ToString();
                var error = ctx.Request.Query["error"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    ctx.Response.Redirect($"/pro/index.html#settings/integrations?dropbox_error={Uri.EscapeDataString(error)}");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ctx.Response.Redirect("/pro/index.html#settings/integrations?dropbox_error=code_missing");
                    return;
                }

                // Parse state to get sourceId
                var stateDecoded = Uri.UnescapeDataString(state);
                var sourceId = stateDecoded.Contains('|') ? stateDecoded.Split('|')[0] : "";

                var dropboxCfg = MongoDbHelper.GetSettings<DropboxGlobalConfig>("dropboxGlobalConfig")
                                 ?? new DropboxGlobalConfig();
                var callbackUrl = string.IsNullOrEmpty(dropboxCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/dropbox/callback"
                    : dropboxCfg.CallbackUrl;

                var refreshToken = await DropboxOrderSourceProvider.ExchangeCodeForRefreshTokenAsync(
                    dropboxCfg.AppKey, dropboxCfg.AppSecret, code, callbackUrl);

                if (!string.IsNullOrEmpty(sourceId))
                {
                    // Update the source with the new refresh token
                    var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                    var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", sourceId)).FirstOrDefault();
                    if (doc != null)
                    {
                        var source = DeserializeSource(doc);
                        if (source != null)
                        {
                            var existingCfgJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
                            var dropboxSourceCfg = string.IsNullOrEmpty(existingCfgJson)
                                ? new DropboxSourceConfig()
                                : JsonSerializer.Deserialize<DropboxSourceConfig>(existingCfgJson) ?? new DropboxSourceConfig();

                            dropboxSourceCfg.AppKey = dropboxCfg.AppKey;
                            dropboxSourceCfg.AppSecret = dropboxCfg.AppSecret;
                            dropboxSourceCfg.RefreshToken = refreshToken;

                            var newConfigJson = JsonSerializer.Serialize(dropboxSourceCfg);
                            var update = Builders<BsonDocument>.Update
                                .Set("configEncrypted", CredentialCrypto.Encrypt(newConfigJson))
                                .Set("updatedAt", DateTime.UtcNow.ToString("O"));
                            col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", sourceId), update);
                        }
                    }
                }

                ctx.Response.Redirect($"/pro/index.html#settings/integrations?dropbox_ok=1&sourceId={Uri.EscapeDataString(sourceId)}");
            }
            catch (Exception ex)
            {
                ctx.Response.Redirect($"/pro/index.html#settings/integrations?dropbox_error={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // ── PUT /api/integrations/dropbox/global-config ───────────────────────
        app.MapPut("/api/integrations/dropbox/global-config", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg = MongoDbHelper.GetSettings<DropboxGlobalConfig>("dropboxGlobalConfig")
                          ?? new DropboxGlobalConfig();
                if (body.TryGetProperty("appKey", out var ak) && !string.IsNullOrEmpty(ak.GetString()))
                    cfg.AppKey = ak.GetString()!;
                if (body.TryGetProperty("appSecret", out var asec) && !string.IsNullOrEmpty(asec.GetString()))
                    cfg.AppSecret = asec.GetString()!;
                if (body.TryGetProperty("callbackUrl", out var cb)) cfg.CallbackUrl = cb.GetString() ?? "";
                MongoDbHelper.UpsertSettings("dropboxGlobalConfig", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/dropbox/global-config ───────────────────────
        app.MapGet("/api/integrations/dropbox/global-config", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<DropboxGlobalConfig>("dropboxGlobalConfig")
                          ?? new DropboxGlobalConfig();
                // Never expose the app secret
                return Results.Json(new { ok = true, appKey = cfg.AppKey, callbackUrl = cfg.CallbackUrl,
                    hasAppSecret = !string.IsNullOrEmpty(cfg.AppSecret) });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/google-drive/authorize ──────────────────────
        app.MapGet("/api/integrations/google-drive/authorize", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var sourceId = ctx.Request.Query["sourceId"].ToString();
                var gdCfg = MongoDbHelper.GetSettings<GoogleDriveGlobalConfig>("googleDriveGlobalConfig")
                            ?? new GoogleDriveGlobalConfig();
                if (string.IsNullOrEmpty(gdCfg.AppClientId))
                    return Results.Json(new { ok = false, error = "Client ID Google Drive non configuré dans les paramètres globaux" });

                var callbackUrl = string.IsNullOrEmpty(gdCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/google-drive/callback"
                    : gdCfg.CallbackUrl;

                var state = Uri.EscapeDataString($"{sourceId}|{Guid.NewGuid():N}");
                var url = GoogleDriveOrderSourceProvider.GetAuthorizationUrl(gdCfg.AppClientId, callbackUrl, state);
                return Results.Json(new { ok = true, url });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/google-drive/callback ───────────────────────
        app.MapGet("/api/integrations/google-drive/callback", async (HttpContext ctx) =>
        {
            try
            {
                var code = ctx.Request.Query["code"].ToString();
                var state = ctx.Request.Query["state"].ToString();
                var error = ctx.Request.Query["error"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    ctx.Response.Redirect($"/pro/index.html#settings/integrations?googledrive_error={Uri.EscapeDataString(error)}");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ctx.Response.Redirect("/pro/index.html#settings/integrations?googledrive_error=code_missing");
                    return;
                }

                var stateDecoded = Uri.UnescapeDataString(state);
                var sourceId = stateDecoded.Contains('|') ? stateDecoded.Split('|')[0] : "";

                var gdCfg = MongoDbHelper.GetSettings<GoogleDriveGlobalConfig>("googleDriveGlobalConfig")
                            ?? new GoogleDriveGlobalConfig();
                var callbackUrl = string.IsNullOrEmpty(gdCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/google-drive/callback"
                    : gdCfg.CallbackUrl;

                var refreshToken = await GoogleDriveOrderSourceProvider.ExchangeCodeForRefreshTokenAsync(
                    gdCfg.AppClientId, gdCfg.AppClientSecret, code, callbackUrl);

                if (!string.IsNullOrEmpty(sourceId))
                {
                    var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                    var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", sourceId)).FirstOrDefault();
                    if (doc != null)
                    {
                        var source = DeserializeSource(doc);
                        if (source != null)
                        {
                            var existingCfgJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
                            var gdSourceCfg = string.IsNullOrEmpty(existingCfgJson)
                                ? new GoogleDriveSourceConfig()
                                : JsonSerializer.Deserialize<GoogleDriveSourceConfig>(existingCfgJson) ?? new GoogleDriveSourceConfig();

                            gdSourceCfg.AppClientId = gdCfg.AppClientId;
                            gdSourceCfg.AppClientSecret = gdCfg.AppClientSecret;
                            gdSourceCfg.RefreshToken = refreshToken;

                            var newConfigJson = JsonSerializer.Serialize(gdSourceCfg);
                            var update = Builders<BsonDocument>.Update
                                .Set("configEncrypted", CredentialCrypto.Encrypt(newConfigJson))
                                .Set("updatedAt", DateTime.UtcNow.ToString("O"));
                            col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", sourceId), update);
                        }
                    }
                }

                ctx.Response.Redirect($"/pro/index.html#settings/integrations?googledrive_ok=1&sourceId={Uri.EscapeDataString(sourceId)}");
            }
            catch (Exception ex)
            {
                ctx.Response.Redirect($"/pro/index.html#settings/integrations?googledrive_error={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // ── GET /api/integrations/google-drive/global-config ─────────────────
        app.MapGet("/api/integrations/google-drive/global-config", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<GoogleDriveGlobalConfig>("googleDriveGlobalConfig")
                          ?? new GoogleDriveGlobalConfig();
                return Results.Json(new { ok = true, appClientId = cfg.AppClientId, callbackUrl = cfg.CallbackUrl,
                    hasAppClientSecret = !string.IsNullOrEmpty(cfg.AppClientSecret) });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/integrations/google-drive/global-config ─────────────────
        app.MapPut("/api/integrations/google-drive/global-config", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg = MongoDbHelper.GetSettings<GoogleDriveGlobalConfig>("googleDriveGlobalConfig")
                          ?? new GoogleDriveGlobalConfig();
                if (body.TryGetProperty("appClientId", out var cid) && !string.IsNullOrEmpty(cid.GetString()))
                    cfg.AppClientId = cid.GetString()!;
                if (body.TryGetProperty("appClientSecret", out var csec) && !string.IsNullOrEmpty(csec.GetString()))
                    cfg.AppClientSecret = csec.GetString()!;
                if (body.TryGetProperty("callbackUrl", out var cb)) cfg.CallbackUrl = cb.GetString() ?? "";
                MongoDbHelper.UpsertSettings("googleDriveGlobalConfig", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/box/authorize ───────────────────────────────
        app.MapGet("/api/integrations/box/authorize", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var sourceId = ctx.Request.Query["sourceId"].ToString();
                var boxCfg = MongoDbHelper.GetSettings<BoxGlobalConfig>("boxGlobalConfig")
                             ?? new BoxGlobalConfig();
                if (string.IsNullOrEmpty(boxCfg.AppClientId))
                    return Results.Json(new { ok = false, error = "Client ID Box non configuré dans les paramètres globaux" });

                var callbackUrl = string.IsNullOrEmpty(boxCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/box/callback"
                    : boxCfg.CallbackUrl;

                var state = Uri.EscapeDataString($"{sourceId}|{Guid.NewGuid():N}");
                var url = BoxOrderSourceProvider.GetAuthorizationUrl(boxCfg.AppClientId, callbackUrl, state);
                return Results.Json(new { ok = true, url });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/box/callback ────────────────────────────────
        app.MapGet("/api/integrations/box/callback", async (HttpContext ctx) =>
        {
            try
            {
                var code = ctx.Request.Query["code"].ToString();
                var state = ctx.Request.Query["state"].ToString();
                var error = ctx.Request.Query["error"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    ctx.Response.Redirect($"/pro/index.html#settings/integrations?box_error={Uri.EscapeDataString(error)}");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ctx.Response.Redirect("/pro/index.html#settings/integrations?box_error=code_missing");
                    return;
                }

                var stateDecoded = Uri.UnescapeDataString(state);
                var sourceId = stateDecoded.Contains('|') ? stateDecoded.Split('|')[0] : "";

                var boxCfg = MongoDbHelper.GetSettings<BoxGlobalConfig>("boxGlobalConfig")
                             ?? new BoxGlobalConfig();
                var callbackUrl = string.IsNullOrEmpty(boxCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/box/callback"
                    : boxCfg.CallbackUrl;

                var (accessToken, refreshToken) = await BoxOrderSourceProvider.ExchangeCodeForTokensAsync(
                    boxCfg.AppClientId, boxCfg.AppClientSecret, code, callbackUrl);

                if (!string.IsNullOrEmpty(sourceId))
                {
                    var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                    var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", sourceId)).FirstOrDefault();
                    if (doc != null)
                    {
                        var source = DeserializeSource(doc);
                        if (source != null)
                        {
                            var existingCfgJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
                            var boxSourceCfg = string.IsNullOrEmpty(existingCfgJson)
                                ? new BoxSourceConfig()
                                : JsonSerializer.Deserialize<BoxSourceConfig>(existingCfgJson) ?? new BoxSourceConfig();

                            boxSourceCfg.AppClientId = boxCfg.AppClientId;
                            boxSourceCfg.AppClientSecret = boxCfg.AppClientSecret;
                            boxSourceCfg.AccessToken = accessToken;
                            boxSourceCfg.RefreshToken = refreshToken;

                            var newConfigJson = JsonSerializer.Serialize(boxSourceCfg);
                            var update = Builders<BsonDocument>.Update
                                .Set("configEncrypted", CredentialCrypto.Encrypt(newConfigJson))
                                .Set("updatedAt", DateTime.UtcNow.ToString("O"));
                            col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", sourceId), update);
                        }
                    }
                }

                ctx.Response.Redirect($"/pro/index.html#settings/integrations?box_ok=1&sourceId={Uri.EscapeDataString(sourceId)}");
            }
            catch (Exception ex)
            {
                ctx.Response.Redirect($"/pro/index.html#settings/integrations?box_error={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // ── GET /api/integrations/box/global-config ───────────────────────────
        app.MapGet("/api/integrations/box/global-config", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<BoxGlobalConfig>("boxGlobalConfig")
                          ?? new BoxGlobalConfig();
                return Results.Json(new { ok = true, appClientId = cfg.AppClientId, callbackUrl = cfg.CallbackUrl,
                    hasAppClientSecret = !string.IsNullOrEmpty(cfg.AppClientSecret) });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/integrations/box/global-config ───────────────────────────
        app.MapPut("/api/integrations/box/global-config", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg = MongoDbHelper.GetSettings<BoxGlobalConfig>("boxGlobalConfig")
                          ?? new BoxGlobalConfig();
                if (body.TryGetProperty("appClientId", out var cid) && !string.IsNullOrEmpty(cid.GetString()))
                    cfg.AppClientId = cid.GetString()!;
                if (body.TryGetProperty("appClientSecret", out var csec) && !string.IsNullOrEmpty(csec.GetString()))
                    cfg.AppClientSecret = csec.GetString()!;
                if (body.TryGetProperty("callbackUrl", out var cb)) cfg.CallbackUrl = cb.GetString() ?? "";
                MongoDbHelper.UpsertSettings("boxGlobalConfig", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/onedrive/authorize ──────────────────────────
        app.MapGet("/api/integrations/onedrive/authorize", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var sourceId = ctx.Request.Query["sourceId"].ToString();
                var driveType = ctx.Request.Query["driveType"].ToString();
                var odCfg = MongoDbHelper.GetSettings<OneDriveGlobalConfig>("oneDriveGlobalConfig")
                            ?? new OneDriveGlobalConfig();
                if (string.IsNullOrEmpty(odCfg.AppClientId))
                    return Results.Json(new { ok = false, error = "Client ID OneDrive non configuré dans les paramètres globaux" });

                var callbackUrl = string.IsNullOrEmpty(odCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/onedrive/callback"
                    : odCfg.CallbackUrl;

                var state = Uri.EscapeDataString($"{sourceId}|{Guid.NewGuid():N}");
                var url = OneDriveOrderSourceProvider.GetAuthorizationUrl(
                    odCfg.AppClientId, odCfg.TenantId, callbackUrl, state, driveType ?? "personal");
                return Results.Json(new { ok = true, url });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── GET /api/integrations/onedrive/callback ───────────────────────────
        app.MapGet("/api/integrations/onedrive/callback", async (HttpContext ctx) =>
        {
            try
            {
                var code = ctx.Request.Query["code"].ToString();
                var state = ctx.Request.Query["state"].ToString();
                var error = ctx.Request.Query["error"].ToString();

                if (!string.IsNullOrEmpty(error))
                {
                    ctx.Response.Redirect($"/pro/index.html#settings/integrations?onedrive_error={Uri.EscapeDataString(error)}");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ctx.Response.Redirect("/pro/index.html#settings/integrations?onedrive_error=code_missing");
                    return;
                }

                var stateDecoded = Uri.UnescapeDataString(state);
                var sourceId = stateDecoded.Contains('|') ? stateDecoded.Split('|')[0] : "";

                var odCfg = MongoDbHelper.GetSettings<OneDriveGlobalConfig>("oneDriveGlobalConfig")
                            ?? new OneDriveGlobalConfig();
                var callbackUrl = string.IsNullOrEmpty(odCfg.CallbackUrl)
                    ? $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/integrations/onedrive/callback"
                    : odCfg.CallbackUrl;

                var (accessToken, refreshToken) = await OneDriveOrderSourceProvider.ExchangeCodeForTokensAsync(
                    odCfg.AppClientId, odCfg.AppClientSecret, odCfg.TenantId, code, callbackUrl);

                if (!string.IsNullOrEmpty(sourceId))
                {
                    var col = MongoDbHelper.GetCollection<BsonDocument>("order_sources");
                    var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", sourceId)).FirstOrDefault();
                    if (doc != null)
                    {
                        var source = DeserializeSource(doc);
                        if (source != null)
                        {
                            var existingCfgJson = CredentialCrypto.Decrypt(source.ConfigEncrypted);
                            var odSourceCfg = string.IsNullOrEmpty(existingCfgJson)
                                ? new OneDriveSourceConfig()
                                : JsonSerializer.Deserialize<OneDriveSourceConfig>(existingCfgJson) ?? new OneDriveSourceConfig();

                            odSourceCfg.AppClientId = odCfg.AppClientId;
                            odSourceCfg.AppClientSecret = odCfg.AppClientSecret;
                            odSourceCfg.TenantId = odCfg.TenantId;
                            odSourceCfg.RefreshToken = refreshToken;

                            var newConfigJson = JsonSerializer.Serialize(odSourceCfg);
                            var update = Builders<BsonDocument>.Update
                                .Set("configEncrypted", CredentialCrypto.Encrypt(newConfigJson))
                                .Set("updatedAt", DateTime.UtcNow.ToString("O"));
                            col.UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", sourceId), update);
                        }
                    }
                }

                ctx.Response.Redirect($"/pro/index.html#settings/integrations?onedrive_ok=1&sourceId={Uri.EscapeDataString(sourceId)}");
            }
            catch (Exception ex)
            {
                ctx.Response.Redirect($"/pro/index.html#settings/integrations?onedrive_error={Uri.EscapeDataString(ex.Message)}");
            }
        });

        // ── GET /api/integrations/onedrive/global-config ──────────────────────
        app.MapGet("/api/integrations/onedrive/global-config", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var cfg = MongoDbHelper.GetSettings<OneDriveGlobalConfig>("oneDriveGlobalConfig")
                          ?? new OneDriveGlobalConfig();
                return Results.Json(new { ok = true, appClientId = cfg.AppClientId, tenantId = cfg.TenantId,
                    callbackUrl = cfg.CallbackUrl, hasAppClientSecret = !string.IsNullOrEmpty(cfg.AppClientSecret) });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // ── PUT /api/integrations/onedrive/global-config ──────────────────────
        app.MapPut("/api/integrations/onedrive/global-config", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var cfg = MongoDbHelper.GetSettings<OneDriveGlobalConfig>("oneDriveGlobalConfig")
                          ?? new OneDriveGlobalConfig();
                if (body.TryGetProperty("appClientId", out var cid) && !string.IsNullOrEmpty(cid.GetString()))
                    cfg.AppClientId = cid.GetString()!;
                if (body.TryGetProperty("appClientSecret", out var csec) && !string.IsNullOrEmpty(csec.GetString()))
                    cfg.AppClientSecret = csec.GetString()!;
                if (body.TryGetProperty("tenantId", out var tid)) cfg.TenantId = tid.GetString() ?? "common";
                if (body.TryGetProperty("callbackUrl", out var cb)) cfg.CallbackUrl = cb.GetString() ?? "";
                MongoDbHelper.UpsertSettings("oneDriveGlobalConfig", cfg);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OrderSource ParseSourceFromBody(JsonElement body, string? existingConfigEncrypted)
    {
        var id = body.TryGetProperty("id", out var idEl) && !string.IsNullOrEmpty(idEl.GetString())
            ? idEl.GetString()!
            : Guid.NewGuid().ToString("N");

        var source = new OrderSource
        {
            Id = id,
            Name = body.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "",
            Type = body.TryGetProperty("type", out var te) ? te.GetString() ?? "" : "",
            Enabled = !body.TryGetProperty("enabled", out var en) || en.GetBoolean(),
            PollingIntervalMinutes = body.TryGetProperty("pollingIntervalMinutes", out var pi) ? Math.Max(1, pi.GetInt32()) : 5,
            DefaultQuantity = body.TryGetProperty("defaultQuantity", out var dq) ? dq.GetInt32() : 1,
            DefaultFormat = body.TryGetProperty("defaultFormat", out var df) ? df.GetString() ?? "" : "",
            MaxFileSizeMb = body.TryGetProperty("maxFileSizeMb", out var mf) ? mf.GetInt32() : 200,
        };

        // Client mapping
        if (body.TryGetProperty("clientMapping", out var cm) && cm.ValueKind == JsonValueKind.Object)
        {
            source.ClientMapping = new Dictionary<string, string>();
            foreach (var prop in cm.EnumerateObject())
                source.ClientMapping[prop.Name] = prop.Value.GetString() ?? "";
        }

        // Config (credentials) — only update if provided
        if (body.TryGetProperty("config", out var cfgEl) && cfgEl.ValueKind == JsonValueKind.Object)
        {
            var configJson = cfgEl.GetRawText();
            source.ConfigEncrypted = CredentialCrypto.Encrypt(configJson);
        }
        else if (!string.IsNullOrEmpty(existingConfigEncrypted))
        {
            source.ConfigEncrypted = existingConfigEncrypted;
        }

        return source;
    }

    private static BsonDocument SourceToBsonDocument(OrderSource source)
    {
        var json = JsonSerializer.Serialize(source);
        var doc = BsonDocument.Parse(json);
        doc["_id"] = source.Id;
        return doc;
    }

    private static OrderSource? DeserializeSource(BsonDocument doc)
    {
        try
        {
            var id = doc["_id"].AsString;
            doc.Remove("_id");
            var json = doc.ToJson();
            var source = JsonSerializer.Deserialize<OrderSource>(json);
            if (source != null) source.Id = id;
            return source;
        }
        catch { return null; }
    }

    private static object SafeSourceView(BsonDocument doc)
    {
        var id = doc.Contains("_id") ? doc["_id"].AsString : "";
        return new
        {
            id,
            name = doc.Contains("name") ? doc["name"].AsString : "",
            type = doc.Contains("type") ? doc["type"].AsString : "",
            enabled = doc.Contains("enabled") && doc["enabled"].AsBoolean,
            pollingIntervalMinutes = doc.Contains("pollingIntervalMinutes") ? doc["pollingIntervalMinutes"].AsInt32 : 5,
            defaultQuantity = doc.Contains("defaultQuantity") ? doc["defaultQuantity"].AsInt32 : 1,
            defaultFormat = doc.Contains("defaultFormat") ? doc["defaultFormat"].AsString : "",
            maxFileSizeMb = doc.Contains("maxFileSizeMb") ? doc["maxFileSizeMb"].AsInt32 : 200,
            clientMapping = doc.Contains("clientMapping")
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(doc["clientMapping"].ToJson())
                : new Dictionary<string, string>(),
            createdAt = doc.Contains("createdAt") ? doc["createdAt"].AsString : "",
            updatedAt = doc.Contains("updatedAt") ? doc["updatedAt"].AsString : "",
            lastPollAt = doc.Contains("lastPollAt") && doc["lastPollAt"] != BsonNull.Value ? doc["lastPollAt"].AsString : null,
            lastPollStatus = doc.Contains("lastPollStatus") ? doc["lastPollStatus"].AsString : "never",
        };
    }

    private static object SafeLogView(BsonDocument d) => new
    {
        id = d["_id"].ToString(),
        sourceId = d.Contains("sourceId") ? d["sourceId"].AsString : "",
        sourceName = d.Contains("sourceName") ? d["sourceName"].AsString : "",
        clientFolder = d.Contains("clientFolder") ? d["clientFolder"].AsString : "",
        fileName = d.Contains("fileName") ? d["fileName"].AsString : "",
        fileHash = d.Contains("fileHash") ? d["fileHash"].AsString : "",
        status = d.Contains("status") ? d["status"].AsString : "",
        jobId = d.Contains("jobId") && d["jobId"] != BsonNull.Value ? d["jobId"].AsString : null,
        errorMessage = d.Contains("errorMessage") && d["errorMessage"] != BsonNull.Value ? d["errorMessage"].AsString : null,
        processedAt = d.Contains("processedAt") ? d["processedAt"].AsString : "",
    };
}
