using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

public static class PortalBatEndpoints
{
    private static string NormalizePath(string? path) =>
        string.IsNullOrEmpty(path) ? "" : path.Replace('\\', System.IO.Path.DirectorySeparatorChar);

    private static void SyncBatStatusValidated(string? batFileName, DateTime now)
    {
        if (string.IsNullOrEmpty(batFileName)) return;
        try
        {
            var batStatusCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
            var hotRoot = BackendUtils.HotfoldersRoot();
            var paths = new List<string> { System.IO.Path.Combine(hotRoot, "BAT", batFileName) };
            if (!batFileName.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase))
                paths.Add(System.IO.Path.Combine(hotRoot, "BAT", "BAT_" + batFileName));
            foreach (var sp in paths)
            {
                batStatusCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("fullPath", sp),
                    Builders<BsonDocument>.Update.Set("status", "validated").Set("validatedAt", now),
                    new UpdateOptions { IsUpsert = true });
            }
            Console.WriteLine($"[BAT] Synced batStatus → validated for {batFileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] BAT status sync failed: {ex.Message}");
        }
    }

    private static void SyncBatStatusByPath(string? fullPath, string statusValue, DateTime now)
    {
        if (string.IsNullOrEmpty(fullPath)) return;
        try
        {
            var batStatusCol = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
            var update = statusValue == "validated"
                ? Builders<BsonDocument>.Update.Set("status", "validated").Set("validatedAt", now)
                : Builders<BsonDocument>.Update.Set("status", "refused").Set("rejectedAt", now);
            batStatusCol.UpdateOne(
                Builders<BsonDocument>.Filter.Eq("fullPath", fullPath),
                update,
                new UpdateOptions { IsUpsert = true });
            Console.WriteLine($"[BAT] Synced batStatus → {statusValue} for path: {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] BAT status sync by path failed: {ex.Message}");
        }
    }

    public static void MapPortalBatEndpoints(this WebApplication app)
    {
        // ── Helper: send client confirmation email ───────────────────────────
        static void SendClientBatConfirmation(string action, string motif,
            ClientAccount client, string orderNumber, string orderTitle, string batFileName)
        {
            try
            {
                if (action == "validated")
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"]  = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"]  = orderTitle,
                        ["{batFileName}"] = batFileName
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("client_bat_validated_confirmation",
                        "Votre BAT a bien été validé — {orderNumber}",
                        "Bonjour {clientName},\n\nVous avez validé le BAT « {batFileName} » pour votre commande {orderNumber} — {orderTitle}.\n\nNous allons maintenant procéder à la mise en production.\n\nMerci pour votre confirmation.\n\nCordialement,",
                        vars);
                    PortalEmailHelper.SendEmail(client.Email, subj, body);
                }
                else if (action == "refused")
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"]  = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"]  = orderTitle,
                        ["{batFileName}"] = batFileName,
                        ["{motif}"]       = motif
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("client_bat_refused_confirmation",
                        "Votre refus du BAT a bien été enregistré — {orderNumber}",
                        "Bonjour {clientName},\n\nVotre refus du BAT « {batFileName} » pour la commande {orderNumber} — {orderTitle} a bien été enregistré.\n\nMotif indiqué : {motif}\n\nNous allons traiter vos corrections et vous soumettre un nouveau BAT.\n\nCordialement,",
                        vars);
                    PortalEmailHelper.SendEmail(client.Email, subj, body);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[WARN] Client BAT confirmation email failed: {ex.Message}"); }
        }

        // ── Token-based endpoints (no auth required — for direct email links) ──
        // (These must be registered before the authenticated order-specific endpoints)

        // GET /api/portal/bat/view?token={token}
        app.MapGet("/api/portal/bat/view", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
                return Results.Json(new { ok = false, error = "Token manquant" });

            var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
            var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("batToken", token)).FirstOrDefault();
            if (bat == null)
                return Results.Json(new { ok = false, error = "Lien invalide ou expiré" });

            var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", bat["orderId"].AsString)).FirstOrDefault();

            return Results.Json(new
            {
                ok = true,
                bat = new
                {
                    id = bat["id"].AsString,
                    orderId = bat["orderId"].AsString,
                    batFileName = bat.Contains("batFileName") ? bat["batFileName"].AsString : "",
                    action = bat.Contains("action") ? bat["action"].AsString : "pending",
                    motif = bat.Contains("motif") ? bat["motif"].AsString : "",
                    sentAt = bat.Contains("sentAt") ? bat["sentAt"].ToUniversalTime() : DateTime.UtcNow,
                    performedAt = bat.Contains("performedAt") && !bat["performedAt"].IsBsonNull
                        ? (DateTime?)bat["performedAt"].ToUniversalTime() : null
                },
                order = orderDoc == null ? null : new
                {
                    orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : "",
                    title = orderDoc.Contains("title") ? orderDoc["title"].AsString : "",
                    status = orderDoc.Contains("status") ? orderDoc["status"].AsString : ""
                }
            });
        });

        // GET /api/portal/bat/file?token={token}  — serve PDF inline (no download)
        app.MapGet("/api/portal/bat/file", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Query["token"].ToString();
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Token manquant" });

                var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
                var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("batToken", token)).FirstOrDefault();
                if (bat == null)
                    return Results.Json(new { ok = false, error = "Lien invalide" });

                var filePath = bat.Contains("batFileRef") ? bat["batFileRef"].AsString : "";
                if (string.IsNullOrWhiteSpace(filePath))
                    return Results.Json(new { ok = false, error = "Référence fichier BAT manquante" });

                Console.WriteLine($"[BAT] Serving file: {filePath}");

                // Try the stored path first, then search in BAT folder by filename
                if (!File.Exists(filePath))
                {
                    var batFolder = Path.Combine(BackendUtils.HotfoldersRoot(), "BAT");
                    var justName = Path.GetFileName(filePath);
                    var altPath = Path.Combine(batFolder, justName);
                    if (File.Exists(altPath))
                        filePath = altPath;
                }

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[BAT] File not found: {filePath}");
                    return Results.Json(new { ok = false, error = "Fichier BAT non trouvé sur le serveur" });
                }

                // Stream the file directly using FileStream (handles Unicode paths on Windows)
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ctx.Response.Headers["Content-Disposition"] = "inline; filename=\"BAT.pdf\"";
                return Results.File(stream, "application/pdf");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BAT] Error: {ex.Message}");
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // GET /api/portal/orders/by-bat-token?token={token}  — anonymous, no login required
        // Returns the order + pending BAT so order.html can load in guest/token mode.
        app.MapGet("/api/portal/orders/by-bat-token", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token))
                return Results.Json(new { ok = false, error = "Token manquant" });

            var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
            var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("batToken", token)).FirstOrDefault();
            if (bat == null)
                return Results.Json(new { ok = false, error = "Lien invalide ou expiré" });

            var orderId = bat["orderId"].AsString;
            var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", orderId)).FirstOrDefault();
            if (orderDoc == null)
                return Results.Json(new { ok = false, error = "Commande non trouvée" });

            return Results.Json(new
            {
                ok = true,
                order = new
                {
                    id = orderDoc.Contains("id") ? orderDoc["id"].AsString : "",
                    orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : "",
                    title = orderDoc.Contains("title") ? orderDoc["title"].AsString : "",
                    status = orderDoc.Contains("status") ? orderDoc["status"].AsString : ""
                },
                batPending = new
                {
                    id = bat["id"].AsString,
                    orderId,
                    batFileName = bat.Contains("batFileName") ? bat["batFileName"].AsString : "",
                    action = bat.Contains("action") ? bat["action"].AsString : "pending",
                    motif = bat.Contains("motif") ? bat["motif"].AsString : "",
                    sentAt = bat.Contains("sentAt") ? bat["sentAt"].ToUniversalTime() : DateTime.UtcNow,
                    performedAt = bat.Contains("performedAt") && !bat["performedAt"].IsBsonNull
                        ? (DateTime?)bat["performedAt"].ToUniversalTime() : null
                }
            });
        });

        // POST /api/portal/bat/decide?token={token}
        // Client validates or refuses the BAT via the token link (no login required)
        app.MapPost("/api/portal/bat/decide", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Query["token"].ToString();
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Token manquant" });

                string action, motif = "";
                if (ctx.Request.ContentType?.Contains("application/json") == true)
                {
                    var jsonBody = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    action = jsonBody.TryGetProperty("action", out var aEl) ? aEl.GetString() ?? "" : "";
                    motif  = jsonBody.TryGetProperty("motif",  out var mEl) ? mEl.GetString() ?? "" : "";
                }
                else
                {
                    var form = await ctx.Request.ReadFormAsync();
                    action = form.TryGetValue("action", out var aVal) ? aVal.ToString() : "";
                    motif  = form.TryGetValue("motif",  out var mVal) ? mVal.ToString().Trim() : "";
                }

                if (action != "validated" && action != "refused")
                    return Results.Json(new { ok = false, error = "Action invalide (validated | refused)" });
                if (action == "refused" && string.IsNullOrWhiteSpace(motif))
                    return Results.Json(new { ok = false, error = "Le motif de refus est obligatoire" });

                var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
                var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("batToken", token)).FirstOrDefault();
                if (bat == null)
                    return Results.Json(new { ok = false, error = "Lien invalide ou expiré" });
                if (bat["action"].AsString != "pending")
                    return Results.Json(new { ok = false, error = "Ce BAT a déjà été traité" });

                var batId       = bat["id"].AsString;
                var orderId     = bat["orderId"].AsString;
                var batFileName = bat.Contains("batFileName") ? bat["batFileName"].AsString : "";
                var now = DateTime.UtcNow;
                var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";

                batCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", batId),
                    Builders<BsonDocument>.Update
                        .Set("action", action)
                        .Set("motif", motif)
                        .Set("performedAt", now)
                        .Set("performedByClientId", "token:" + clientIp)
                        .Set("operatorNotificationAcknowledged", false));

                var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var orderDoc  = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", orderId)).FirstOrDefault();
                var newStatus = action == "validated" ? "in_production" : "bat_refused";
                var comment   = action == "validated"
                    ? "BAT validé par le client"
                    : $"BAT refusé par le client. Motif : {motif}";

                ordersCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", orderId),
                    Builders<BsonDocument>.Update
                        .Set("status", newStatus)
                        .Set("updatedAt", now)
                        .Push("statusHistory", new BsonDocument
                        {
                            ["status"] = action == "validated" ? "bat_validated" : "bat_refused",
                            ["timestamp"] = now,
                            ["comment"] = comment
                        }));

                var clientId    = orderDoc?.Contains("clientAccountId") == true ? orderDoc["clientAccountId"].AsString : "";
                var orderNumber = orderDoc?.Contains("orderNumber") == true ? orderDoc["orderNumber"].AsString : orderId;
                var orderTitle  = orderDoc?.Contains("title") == true ? orderDoc["title"].AsString : "";

                var clientsCol  = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var clientDoc   = string.IsNullOrEmpty(clientId) ? null
                    : clientsCol.Find(Builders<BsonDocument>.Filter.Eq("id", clientId)).FirstOrDefault();
                ClientAccount? client = clientDoc != null ? PortalAuthEndpoints.DocToClient(clientDoc) : null;

                // Sync operator BAT tab for validated decisions
                if (action == "validated")
                    SyncBatStatusValidated(batFileName, now);

                // Confirmation email to client
                if (client != null)
                    SendClientBatConfirmation(action, motif, client, orderNumber, orderTitle, batFileName);

                // Notify atelier
                try
                {
                    var atelierVars = new Dictionary<string, string>
                    {
                        ["{clientName}"]  = client?.DisplayName ?? "Client",
                        ["{companyName}"] = client?.CompanyName ?? "",
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"]  = orderTitle,
                        ["{motif}"]       = motif
                    };
                    if (action == "validated")
                    {
                        var (subj, body) = PortalEmailHelper.RenderTemplate("atelier_client_bat_validated",
                            "BAT validé par le client — {orderNumber}",
                            "Le client {clientName} ({companyName}) a validé le BAT pour la commande {orderNumber} — {orderTitle}.",
                            atelierVars);
                        PortalEmailHelper.SendAtelierNotification(subj, body);
                    }
                    else
                    {
                        var (subj, body) = PortalEmailHelper.RenderTemplate("atelier_client_bat_refused",
                            "BAT refusé par le client — {orderNumber}",
                            "Le client {clientName} ({companyName}) a refusé le BAT pour la commande {orderNumber} — {orderTitle}.\n\nMotif : {motif}",
                            atelierVars);
                        PortalEmailHelper.SendAtelierNotification(subj, body);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] Atelier BAT email failed: {ex.Message}"); }

                return Results.Json(new { ok = true, action });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // GET /api/portal/orders/{id}/bat
        app.MapGet("/api/portal/orders/{id}/bat", (HttpContext ctx, string id) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

            // Verify order ownership
            var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
            if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });
            var clientAccountId = orderDoc.Contains("clientAccountId") ? orderDoc["clientAccountId"].AsString : "";
            if (clientAccountId != client.Id) return Results.Json(new { ok = false, error = "Accès refusé" });

            var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
            var bats = batCol.Find(Builders<BsonDocument>.Filter.Eq("orderId", id))
                .Sort(Builders<BsonDocument>.Sort.Descending("sentAt"))
                .ToList()
                .Select(b => new
                {
                    id = b.Contains("id") ? b["id"].AsString : "",
                    batFileName = b.Contains("batFileName") ? b["batFileName"].AsString : "",
                    batFileRef = b.Contains("batFileRef") ? b["batFileRef"].AsString : "",
                    action = b.Contains("action") ? b["action"].AsString : "pending",
                    motif = b.Contains("motif") ? b["motif"].AsString : "",
                    attachmentName = b.Contains("attachmentName") ? b["attachmentName"].AsString : "",
                    performedAt = b.Contains("performedAt") && !b["performedAt"].IsBsonNull ? (DateTime?)b["performedAt"].ToUniversalTime() : null,
                    sentAt = b.Contains("sentAt") ? b["sentAt"].ToUniversalTime() : DateTime.UtcNow
                }).ToList();

            return Results.Json(new { ok = true, bats });
        });

        // GET /api/portal/orders/{id}/bat/{batId}/file  — stream the BAT PDF for viewing
        app.MapGet("/api/portal/orders/{id}/bat/{batId}/file", (HttpContext ctx, string id, string batId) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

            // Ownership check
            var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
            if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });
            if (orderDoc["clientAccountId"].AsString != client.Id) return Results.Json(new { ok = false, error = "Accès refusé" });

            var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
            var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("id", batId)).FirstOrDefault();
            if (bat == null) return Results.Json(new { ok = false, error = "BAT non trouvé" });
            if (bat["orderId"].AsString != id) return Results.Json(new { ok = false, error = "BAT non trouvé" });

            var filePath = bat.Contains("batFileRef") ? bat["batFileRef"].AsString : "";
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Results.Json(new { ok = false, error = "Fichier BAT non trouvé" });

            // Serve inline so the browser displays it rather than downloading
            var fileName = Path.GetFileName(filePath);
            ctx.Response.Headers["Content-Disposition"] = $"inline; filename=\"{fileName}\"";
            return Results.File(filePath, "application/pdf");
        });

        // POST /api/portal/orders/{id}/bat/{batId}/validate
        app.MapPost("/api/portal/orders/{id}/bat/{batId}/validate", async (HttpContext ctx, string id, string batId) =>
        {
            try
            {
                var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
                if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

                var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });
                if (orderDoc["clientAccountId"].AsString != client.Id) return Results.Json(new { ok = false, error = "Accès refusé" });

                var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
                var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("id", batId)).FirstOrDefault();
                if (bat == null) return Results.Json(new { ok = false, error = "BAT non trouvé" });
                if (bat["orderId"].AsString != id) return Results.Json(new { ok = false, error = "BAT non trouvé" });
                if (bat["action"].AsString != "pending") return Results.Json(new { ok = false, error = "Ce BAT a déjà été traité" });

                var now = DateTime.UtcNow;
                var batFileName = bat.Contains("batFileName") ? bat["batFileName"].AsString : "";
                batCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", batId),
                    Builders<BsonDocument>.Update
                        .Set("action", "validated")
                        .Set("performedAt", now)
                        .Set("performedByClientId", client.Id)
                        .Set("operatorNotificationAcknowledged", false));

                // Update order status
                ordersCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("status", "in_production")
                        .Set("updatedAt", now)
                        .Push("statusHistory", new BsonDocument
                        {
                            ["status"] = "bat_validated",
                            ["timestamp"] = now,
                            ["comment"] = "BAT validé par le client"
                        }));

                var orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : id;
                var orderTitle = orderDoc.Contains("title") ? orderDoc["title"].AsString : "";

                // Confirmation email to client
                SendClientBatConfirmation("validated", "", client, orderNumber, orderTitle, batFileName);

                // Auto-validate internal BAT status: mark pending BAT decisions as validated
                try
                {
                    var pendingFilter = Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq("orderId", id),
                        Builders<BsonDocument>.Filter.Eq("action", "pending"));
                    var autoResult = batCol.UpdateMany(pendingFilter, Builders<BsonDocument>.Update
                        .Set("action", "validated")
                        .Set("performedAt", now)
                        .Set("performedByClientId", client.Id)
                        .Set("operatorNotificationAcknowledged", false));
                    if (autoResult.MatchedCount > 0)
                        Console.WriteLine($"[BAT] Auto-validated {autoResult.ModifiedCount}/{autoResult.MatchedCount} pending BATs for order {id}");
                }
                catch (Exception exAuto)
                {
                    Console.WriteLine($"[WARN] BAT auto-validate failed for order {id}: {exAuto.Message}");
                }

                // Sync operator BAT tab: update batStatus so the operator sees "Validé"
                SyncBatStatusValidated(batFileName, now);

                // Notify atelier
                try
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"] = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"] = orderTitle,
                        ["{companyName}"] = client.CompanyName
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("atelier_client_bat_validated",
                        "BAT validé par le client — {orderNumber}",
                        "Le client {clientName} ({companyName}) a validé le BAT pour la commande {orderNumber} — {orderTitle}.",
                        vars);
                    PortalEmailHelper.SendAtelierNotification(subj, body);
                }
                catch { /* non-blocking */ }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // POST /api/portal/orders/{id}/bat/{batId}/refuse
        app.MapPost("/api/portal/orders/{id}/bat/{batId}/refuse", async (HttpContext ctx, string id, string batId) =>
        {
            try
            {
                var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
                if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

                var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });
                if (orderDoc["clientAccountId"].AsString != client.Id) return Results.Json(new { ok = false, error = "Accès refusé" });

                var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
                var bat = batCol.Find(Builders<BsonDocument>.Filter.Eq("id", batId)).FirstOrDefault();
                if (bat == null) return Results.Json(new { ok = false, error = "BAT non trouvé" });
                if (bat["orderId"].AsString != id) return Results.Json(new { ok = false, error = "BAT non trouvé" });
                if (bat["action"].AsString != "pending") return Results.Json(new { ok = false, error = "Ce BAT a déjà été traité" });

                var form = await ctx.Request.ReadFormAsync();
                var motif = form.TryGetValue("motif", out var mVal) ? mVal.ToString().Trim() : "";
                if (string.IsNullOrWhiteSpace(motif))
                    return Results.Json(new { ok = false, error = "Le motif de refus est obligatoire" });

                var now = DateTime.UtcNow;
                var updateDef = Builders<BsonDocument>.Update
                    .Set("action", "refused")
                    .Set("motif", motif)
                    .Set("performedAt", now)
                    .Set("performedByClientId", client.Id);

                // Handle optional attachment
                string attachmentName = "";
                if (form.Files.Any())
                {
                    var file = form.Files.First();
                    var safeName = Path.GetFileName(file.FileName ?? "attachment");
                    safeName = System.Text.RegularExpressions.Regex.Replace(safeName, @"[^\w\.\-]", "_");
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "attachment";

                    var settings2 = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                    var hotRoot = BackendUtils.HotfoldersRoot();
                    var attachDir = Path.Combine(hotRoot, settings2.WebOrderKanbanFolder ?? "Commandes web", id, "bat-refus");
                    Directory.CreateDirectory(attachDir);

                    var destPath = Path.Combine(attachDir, $"{batId}_{safeName}");
                    using (var fs = File.Create(destPath))
                        await file.CopyToAsync(fs);

                    attachmentName = safeName;
                    updateDef = updateDef
                        .Set("attachmentRef", destPath)
                        .Set("attachmentName", attachmentName);
                }

                batCol.UpdateOne(Builders<BsonDocument>.Filter.Eq("id", batId), updateDef);

                // Mark for operator notification
                batCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", batId),
                    Builders<BsonDocument>.Update.Set("operatorNotificationAcknowledged", false));

                // Update order status
                ordersCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("status", "bat_refused")
                        .Set("updatedAt", now)
                        .Push("statusHistory", new BsonDocument
                        {
                            ["status"] = "bat_refused",
                            ["timestamp"] = now,
                            ["comment"] = $"BAT refusé par le client. Motif : {motif}"
                        }));

                var orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : id;
                var orderTitle = orderDoc.Contains("title") ? orderDoc["title"].AsString : "";
                var batFileName = bat.Contains("batFileName") ? bat["batFileName"].AsString : "";

                // Confirmation email to client
                SendClientBatConfirmation("refused", motif, client, orderNumber, orderTitle, batFileName);

                // Notify atelier
                try
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"] = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"] = orderTitle,
                        ["{companyName}"] = client.CompanyName,
                        ["{motif}"] = motif
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("atelier_client_bat_refused",
                        "BAT refusé par le client — {orderNumber}",
                        "Le client {clientName} ({companyName}) a refusé le BAT pour la commande {orderNumber} — {orderTitle}.\n\nMotif : {motif}",
                        vars);
                    PortalEmailHelper.SendAtelierNotification(subj, body);
                }
                catch { /* non-blocking */ }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── STAFF: POST /api/pro/bat/external-link ────────────────────────────
        // Creates an external BAT validation link (no portal account required).
        // Body: { fileName, fullPath, clientEmail, clientName? }
        // Returns: { ok, token, batUrl }
        app.MapPost("/api/pro/bat/external-link", async (HttpContext ctx) =>
        {
            try
            {
                var raw = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "").Trim();
                if (string.IsNullOrWhiteSpace(raw)) return Results.Json(new { ok = false, error = "Non autorisé" });

                var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
                if (body == null) return Results.Json(new { ok = false, error = "Payload invalide" });

                var fileName = body.RootElement.TryGetProperty("fileName", out var fnEl) ? fnEl.GetString() ?? "" : "";
                var fullPath = body.RootElement.TryGetProperty("fullPath", out var fpEl) ? fpEl.GetString() ?? "" : "";
                var clientEmail = body.RootElement.TryGetProperty("clientEmail", out var ceEl) ? ceEl.GetString() ?? "" : "";
                var clientName = body.RootElement.TryGetProperty("clientName", out var cnEl) ? cnEl.GetString() ?? "" : clientEmail;
                var numeroDossier = body.RootElement.TryGetProperty("numeroDossier", out var ndEl) ? ndEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(clientEmail))
                    return Results.Json(new { ok = false, error = "Email client requis" });
                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.Json(new { ok = false, error = "Nom de fichier BAT requis" });

                // Generate token and expiry
                var tokenBytes = new byte[24];
                System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
                var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
                var expiresAt = DateTime.UtcNow.AddDays(30);

                // Determine BAT file path for viewing
                // Normalize path separators coming from the JS client (which uses backslashes)
                var osFullPath = NormalizePath(fullPath);
                var hotRoot = BackendUtils.HotfoldersRoot();
                var batDir = System.IO.Path.Combine(hotRoot, "BAT");
                string? batFilePath = null;
                if (!string.IsNullOrWhiteSpace(osFullPath) && System.IO.File.Exists(osFullPath))
                    batFilePath = osFullPath;
                else if (System.IO.Directory.Exists(batDir))
                {
                    // Case-insensitive file search in the BAT folder
                    var justName = System.IO.Path.GetFileName(
                        !string.IsNullOrWhiteSpace(osFullPath) ? osFullPath : fileName);
                    if (!string.IsNullOrWhiteSpace(justName))
                    {
                        var found = System.IO.Directory.GetFiles(batDir)
                            .FirstOrDefault(f => string.Equals(
                                System.IO.Path.GetFileName(f), justName,
                                StringComparison.OrdinalIgnoreCase));
                        if (found != null)
                            batFilePath = found;
                    }
                    // If still not found and name already has BAT_ prefix, try without; otherwise try with
                    if (batFilePath == null && !string.IsNullOrWhiteSpace(justName))
                    {
                        var altName = justName.StartsWith("BAT_", StringComparison.OrdinalIgnoreCase)
                            ? justName.Substring(4)
                            : "BAT_" + justName;
                        var found = System.IO.Directory.GetFiles(batDir)
                            .FirstOrDefault(f => string.Equals(
                                System.IO.Path.GetFileName(f), altName,
                                StringComparison.OrdinalIgnoreCase));
                        if (found != null)
                            batFilePath = found;
                    }
                }

                // Save to bat_external_links collection
                var col = MongoDbHelper.GetCollection<BsonDocument>("bat_external_links");
                var doc = new BsonDocument
                {
                    ["token"] = token,
                    ["fileName"] = fileName,
                    ["fullPath"] = batFilePath ?? fullPath,
                    ["clientEmail"] = clientEmail,
                    ["clientName"] = clientName,
                    ["numeroDossier"] = numeroDossier,
                    ["status"] = "pending",
                    ["clientDecision"] = BsonNull.Value,
                    ["createdAt"] = DateTime.UtcNow,
                    ["expiresAt"] = expiresAt
                };
                col.InsertOne(doc);

                // Build the URL
                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                var portalBase = PortalEmailHelper.SanitizePortalBaseUrl(settings.PortalUrl);
                if (string.IsNullOrWhiteSpace(portalBase))
                    portalBase = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                var batUrl = $"{portalBase}/portal/bat-external.html?token={token}";

                return Results.Json(new { ok = true, token, batUrl, clientEmail, expiresAt });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── PUBLIC: GET /api/portal/bat-external?token= ─────────────────────
        // Returns BAT metadata for an external BAT link
        app.MapGet("/api/portal/bat-external", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token)) return Results.Json(new { ok = false, error = "Token manquant" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("bat_external_links");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("token", token)).FirstOrDefault();
            if (doc == null) return Results.Json(new { ok = false, error = "Lien invalide ou introuvable" });

            if (doc["status"].AsString == "revoked") return Results.Json(new { ok = false, error = "Ce lien a été révoqué" });

            var expiresAt = doc.Contains("expiresAt") && doc["expiresAt"] != BsonNull.Value
                ? (DateTime?)doc["expiresAt"].ToUniversalTime() : null;
            if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
                return Results.Json(new { ok = false, error = "Ce lien a expiré" });

            var clientDecision = (doc.Contains("clientDecision") && doc["clientDecision"] != BsonNull.Value)
                ? doc["clientDecision"].AsString : null;

            var storedPath = NormalizePath(doc.Contains("fullPath") ? doc["fullPath"].AsString : "");
            return Results.Json(new
            {
                ok = true,
                fileName = doc["fileName"].AsString,
                clientName = doc.Contains("clientName") ? doc["clientName"].AsString : "",
                numeroDossier = doc.Contains("numeroDossier") ? doc["numeroDossier"].AsString : "",
                hasFile = !string.IsNullOrWhiteSpace(storedPath) && System.IO.File.Exists(storedPath),
                clientDecision,
                expiresAt
            });
        });

        // ── PUBLIC: GET /api/portal/bat-external/file?token= ────────────────
        // Serves the BAT PDF file for an external BAT link
        app.MapGet("/api/portal/bat-external/file", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            if (string.IsNullOrWhiteSpace(token)) return Results.Json(new { ok = false, error = "Token manquant" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("bat_external_links");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("token", token)).FirstOrDefault();
            if (doc == null || doc["status"].AsString == "revoked")
                return Results.Json(new { ok = false, error = "Lien invalide" });

            var filePath = NormalizePath(doc["fullPath"].AsString);
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return Results.NotFound();

            var fn = System.IO.Path.GetFileName(filePath);
            ctx.Response.Headers["Content-Disposition"] = "inline; filename=\"BAT.pdf\"";
            return Results.File(System.IO.File.OpenRead(filePath), "application/pdf");
        });

        // ── PUBLIC: POST /api/portal/bat-external/decide?token= ─────────────
        // Records the client's BAT decision (accepted/refused)
        app.MapPost("/api/portal/bat-external/decide", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Query["token"].ToString();
                if (string.IsNullOrWhiteSpace(token)) return Results.Json(new { ok = false, error = "Token manquant" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("bat_external_links");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("token", token)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Lien invalide ou introuvable" });
                if (doc["status"].AsString == "revoked") return Results.Json(new { ok = false, error = "Lien révoqué" });

                // Prevent double-submission: check if decision was already recorded
                var existingDecision = (doc.Contains("clientDecision") && doc["clientDecision"] != BsonNull.Value)
                    ? doc["clientDecision"].AsString : null;
                if (!string.IsNullOrEmpty(existingDecision))
                    return Results.Json(new { ok = false, error = "Votre décision a déjà été enregistrée." });

                // Check expiry
                var expiresAt = doc.Contains("expiresAt") && doc["expiresAt"] != BsonNull.Value
                    ? (DateTime?)doc["expiresAt"].ToUniversalTime() : null;
                if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow)
                    return Results.Json(new { ok = false, error = "Ce lien a expiré." });

                var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
                var decision = body?.RootElement.TryGetProperty("decision", out var decEl) == true ? decEl.GetString() ?? "" : "";
                if (decision != "accepted" && decision != "refused")
                    return Results.Json(new { ok = false, error = "Décision invalide." });

                var motif = body?.RootElement.TryGetProperty("motif", out var motifEl) == true ? motifEl.GetString() ?? "" : "";
                var now = DateTime.UtcNow;

                // Update the link record
                var filter = Builders<BsonDocument>.Filter.Eq("token", token);
                var update = Builders<BsonDocument>.Update
                    .Set("clientDecision", decision)
                    .Set("clientDecisionAt", now)
                    .Set("clientDecisionMotif", motif)
                    .Set("status", decision == "accepted" ? "validated" : "refused");
                col.UpdateOne(filter, update);

                // Sync batStatus using the stored file path (normalized for OS)
                var storedFilePath = NormalizePath(doc.Contains("fullPath") ? doc["fullPath"].AsString : "");
                if (decision == "accepted")
                {
                    // Use stored path directly for exact match; fall back to filename-based sync
                    if (!string.IsNullOrWhiteSpace(storedFilePath))
                        SyncBatStatusByPath(storedFilePath, "validated", now);
                    else
                        SyncBatStatusValidated(doc["fileName"].AsString, now);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(storedFilePath))
                        SyncBatStatusByPath(storedFilePath, "refused", now);
                    else
                    {
                        // Fallback: reconstruct paths from filename
                        var hotRoot = BackendUtils.HotfoldersRoot();
                        var batStatusCol2 = MongoDbHelper.GetCollection<BsonDocument>("batStatus");
                        var fn = doc["fileName"].AsString;
                        var paths = new List<string>
                        {
                            System.IO.Path.Combine(hotRoot, "BAT", fn),
                            System.IO.Path.Combine(hotRoot, "BAT", "BAT_" + fn)
                        };
                        foreach (var sp in paths)
                        {
                            batStatusCol2.UpdateOne(
                                Builders<BsonDocument>.Filter.Eq("fullPath", sp),
                                Builders<BsonDocument>.Update.Set("status", "refused").Set("rejectedAt", now),
                                new UpdateOptions { IsUpsert = true });
                        }
                    }
                }

                // Create a staff notification per operator/admin so it appears in each user's bell
                var emoji = decision == "accepted" ? "✅" : "❌";
                var clientName = doc.Contains("clientName") ? doc["clientName"].AsString : "";
                var numDossier = doc.Contains("numeroDossier") ? doc["numeroDossier"].AsString : "";
                var actionLabel = decision == "accepted" ? "a VALIDÉ" : "a REFUSÉ";
                var notifMessage = $"{emoji} BAT {actionLabel} par {clientName} — dossier {numDossier}";
                var notifType = decision == "accepted" ? "bat_validated" : "bat_refused";
                var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
                try
                {
                    var usersCol = MongoDbHelper.GetCollection<BsonDocument>("users");
                    var staffUsers = usersCol.Find(
                        Builders<BsonDocument>.Filter.In("profile", new[] { 2, 3 })).ToList();
                    foreach (var staffUser in staffUsers)
                    {
                        var recipientLogin = staffUser.Contains("login") ? staffUser["login"].AsString : "";
                        if (string.IsNullOrEmpty(recipientLogin)) continue;
                        notifCol.InsertOne(new BsonDocument
                        {
                            ["type"] = notifType,
                            ["recipientLogin"] = recipientLogin,
                            ["message"] = notifMessage,
                            ["fileName"] = doc["fileName"].AsString,
                            ["read"] = false,
                            ["timestamp"] = now
                        });
                    }
                }
                catch (Exception exNotif)
                {
                    Console.WriteLine($"[WARN] External BAT notification failed: {exNotif.Message}");
                }

                return Results.Json(new { ok = true, decision });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── STAFF: GET /api/pro/bat/external-link-enabled ────────────────────
        // Returns whether external BAT links are enabled (admin toggle)
        app.MapGet("/api/pro/bat/external-link-enabled", () =>
        {
            try
            {
                var col = MongoDbHelper.GetSettingsCollection();
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("_id", "productionSettings")).FirstOrDefault();
                bool enabled = doc != null
                    && doc.Contains("enableExternalBatLinks")
                    && doc["enableExternalBatLinks"] != BsonNull.Value
                    && doc["enableExternalBatLinks"].ToBoolean();
                return Results.Json(new { ok = true, enabled });
            }
            catch { return Results.Json(new { ok = true, enabled = false }); }
        });
    }
}
