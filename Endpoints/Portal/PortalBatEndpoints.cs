using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

public static class PortalBatEndpoints
{
    public static void MapPortalBatEndpoints(this WebApplication app)
    {
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

            return Results.File(filePath, "application/pdf", Path.GetFileName(filePath));
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
                batCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", batId),
                    Builders<BsonDocument>.Update
                        .Set("action", "validated")
                        .Set("performedAt", now)
                        .Set("performedByClientId", client.Id));

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

                // Notify atelier
                try
                {
                    var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
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
    }
}
