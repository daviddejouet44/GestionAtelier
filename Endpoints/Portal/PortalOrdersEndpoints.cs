using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

public static class PortalOrdersEndpoints
{
    // Mapper -----------------------------------------------------------------
    private static object OrderToDto(ClientOrder o, bool includeFiles = true) => new
    {
        id = o.Id,
        orderNumber = o.OrderNumber,
        title = o.Title,
        quantity = o.Quantity,
        format = o.Format,
        paper = o.Paper,
        recto = o.Recto,
        finitions = o.Finitions,
        desiredDeliveryDate = o.DesiredDeliveryDate,
        deliveryMode = o.DeliveryMode,
        deliveryAddress = o.DeliveryAddress,
        comments = o.Comments,
        status = o.Status,
        createdAt = o.CreatedAt,
        updatedAt = o.UpdatedAt,
        statusHistory = o.StatusHistory,
        files = includeFiles ? o.Files.Select(f => new { fileName = f.FileName, uploadedAt = f.UploadedAt, size = f.Size }).ToList() : null,
    };

    private static ClientOrder DocToOrder(BsonDocument d)
    {
        var finitions = new List<string>();
        if (d.Contains("finitions") && d["finitions"].IsBsonArray)
            finitions = d["finitions"].AsBsonArray.Select(v => v.AsString).ToList();

        var files = new List<ClientOrderFile>();
        if (d.Contains("files") && d["files"].IsBsonArray)
        {
            foreach (var f in d["files"].AsBsonArray)
            {
                if (f.IsBsonDocument)
                {
                    var fd = f.AsBsonDocument;
                    files.Add(new ClientOrderFile
                    {
                        FileName = fd.Contains("fileName") ? fd["fileName"].AsString : "",
                        StoredPath = fd.Contains("storedPath") ? fd["storedPath"].AsString : "",
                        UploadedAt = fd.Contains("uploadedAt") ? fd["uploadedAt"].ToUniversalTime() : DateTime.UtcNow,
                        Size = fd.Contains("size") ? fd["size"].ToInt64() : 0
                    });
                }
            }
        }

        var history = new List<ClientOrderStatusEntry>();
        if (d.Contains("statusHistory") && d["statusHistory"].IsBsonArray)
        {
            foreach (var h in d["statusHistory"].AsBsonArray)
            {
                if (h.IsBsonDocument)
                {
                    var hd = h.AsBsonDocument;
                    history.Add(new ClientOrderStatusEntry
                    {
                        Status = hd.Contains("status") ? hd["status"].AsString : "",
                        Timestamp = hd.Contains("timestamp") ? hd["timestamp"].ToUniversalTime() : DateTime.UtcNow,
                        Comment = hd.Contains("comment") ? hd["comment"].AsString : ""
                    });
                }
            }
        }

        return new ClientOrder
        {
            Id = d.Contains("id") ? d["id"].AsString : "",
            ClientAccountId = d.Contains("clientAccountId") ? d["clientAccountId"].AsString : "",
            OrderNumber = d.Contains("orderNumber") ? d["orderNumber"].AsString : "",
            Title = d.Contains("title") ? d["title"].AsString : "",
            Quantity = d.Contains("quantity") ? d["quantity"].AsInt32 : 0,
            Format = d.Contains("format") ? d["format"].AsString : "",
            Paper = d.Contains("paper") ? d["paper"].AsString : "",
            Recto = d.Contains("recto") ? d["recto"].AsString : "recto",
            Finitions = finitions,
            DesiredDeliveryDate = d.Contains("desiredDeliveryDate") && !d["desiredDeliveryDate"].IsBsonNull ? d["desiredDeliveryDate"].ToUniversalTime() : null,
            DeliveryMode = d.Contains("deliveryMode") ? d["deliveryMode"].AsString : "retrait",
            DeliveryAddress = d.Contains("deliveryAddress") ? d["deliveryAddress"].AsString : "",
            Comments = d.Contains("comments") ? d["comments"].AsString : "",
            Status = d.Contains("status") ? d["status"].AsString : "draft",
            AtelierJobPath = d.Contains("atelierJobPath") ? d["atelierJobPath"].AsString : "",
            CreatedAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : DateTime.UtcNow,
            UpdatedAt = d.Contains("updatedAt") ? d["updatedAt"].ToUniversalTime() : DateTime.UtcNow,
            Files = files,
            StatusHistory = history
        };
    }

    // Registration -----------------------------------------------------------
    public static void MapPortalOrdersEndpoints(this WebApplication app)
    {
        // GET /api/portal/orders
        app.MapGet("/api/portal/orders", (HttpContext ctx) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var docs = col.Find(Builders<BsonDocument>.Filter.Eq("clientAccountId", client.Id))
                .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
                .ToList();

            var orders = docs.Select(DocToOrder).Select(o => OrderToDto(o, false)).ToList();
            return Results.Json(new { ok = true, orders });
        });

        // GET /api/portal/orders/{id}
        app.MapGet("/api/portal/orders/{id}", (HttpContext ctx, string id) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
            if (doc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });

            var order = DocToOrder(doc);
            // Strict ownership check
            if (order.ClientAccountId != client.Id)
                return Results.Json(new { ok = false, error = "Accès refusé" });

            // Fetch BAT actions for this order
            var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
            var batDocs = batCol.Find(Builders<BsonDocument>.Filter.Eq("orderId", id))
                .Sort(Builders<BsonDocument>.Sort.Descending("sentAt"))
                .ToList();
            var bats = batDocs.Select(b => new
            {
                id = b.Contains("id") ? b["id"].AsString : "",
                batFileName = b.Contains("batFileName") ? b["batFileName"].AsString : "",
                action = b.Contains("action") ? b["action"].AsString : "pending",
                motif = b.Contains("motif") ? b["motif"].AsString : "",
                attachmentName = b.Contains("attachmentName") ? b["attachmentName"].AsString : "",
                performedAt = b.Contains("performedAt") && !b["performedAt"].IsBsonNull ? (DateTime?)b["performedAt"].ToUniversalTime() : null,
                sentAt = b.Contains("sentAt") ? b["sentAt"].ToUniversalTime() : DateTime.UtcNow
            }).ToList();

            return Results.Json(new { ok = true, order = OrderToDto(order), bats });
        });

        // POST /api/portal/orders  — create new order (draft)
        app.MapPost("/api/portal/orders", async (HttpContext ctx) =>
        {
            try
            {
                var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
                if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();

                var title = json.TryGetProperty("title", out var tEl) ? tEl.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrWhiteSpace(title))
                    return Results.Json(new { ok = false, error = "L'intitulé est obligatoire" });

                int qty = json.TryGetProperty("quantity", out var qEl) ? (qEl.TryGetInt32(out var qi) ? qi : 0) : 0;
                if (qty <= 0)
                    return Results.Json(new { ok = false, error = "La quantité est obligatoire" });

                var format = json.TryGetProperty("format", out var fEl) ? fEl.GetString() ?? "" : "";
                var paper = json.TryGetProperty("paper", out var pEl) ? pEl.GetString() ?? "" : "";
                var recto = json.TryGetProperty("recto", out var rEl) ? rEl.GetString() ?? "recto" : "recto";
                var deliveryMode = json.TryGetProperty("deliveryMode", out var dmEl) ? dmEl.GetString() ?? "retrait" : "retrait";
                var deliveryAddress = json.TryGetProperty("deliveryAddress", out var daEl) ? daEl.GetString() ?? "" : "";
                var comments = json.TryGetProperty("comments", out var cEl) ? cEl.GetString() ?? "" : "";
                DateTime? desiredDate = null;
                if (json.TryGetProperty("desiredDeliveryDate", out var ddEl) && ddEl.ValueKind == JsonValueKind.String)
                    DateTime.TryParse(ddEl.GetString(), out var ddParsed);

                var finitions = new List<string>();
                if (json.TryGetProperty("finitions", out var finEl) && finEl.ValueKind == JsonValueKind.Array)
                    finitions = finEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                // Validate deliveryAddress is required for livraison
                if (deliveryMode == "livraison" && string.IsNullOrWhiteSpace(deliveryAddress))
                    return Results.Json(new { ok = false, error = "L'adresse de livraison est obligatoire pour le mode Livraison" });

                // Generate order number
                var counter = MongoDbHelper.GetNextClientOrderNumber();
                var orderNumber = $"WEB-{DateTime.Now:yyyyMMdd}-{counter:D4}";

                var orderId = Guid.NewGuid().ToString("N");
                var now = DateTime.UtcNow;

                var order = new ClientOrder
                {
                    Id = orderId,
                    ClientAccountId = client.Id,
                    OrderNumber = orderNumber,
                    Title = title,
                    Quantity = qty,
                    Format = format,
                    Paper = paper,
                    Recto = recto == "recto-verso" ? "recto-verso" : "recto",
                    Finitions = finitions,
                    DesiredDeliveryDate = desiredDate,
                    DeliveryMode = deliveryMode == "livraison" ? "livraison" : "retrait",
                    DeliveryAddress = deliveryAddress,
                    Comments = comments,
                    Status = "draft",
                    CreatedAt = now,
                    UpdatedAt = now,
                    StatusHistory = new List<ClientOrderStatusEntry>
                    {
                        new() { Status = "draft", Timestamp = now, Comment = "Commande créée par le client" }
                    }
                };

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var doc = ToBsonDocument(order);
                col.InsertOne(doc);

                // Create "Commandes web" folder in hotfolder if not exists
                try
                {
                    var hotRoot = BackendUtils.HotfoldersRoot();
                    var webFolder = Path.Combine(hotRoot, settings.WebOrderKanbanFolder ?? "Commandes web");
                    Directory.CreateDirectory(webFolder);
                }
                catch { /* non-blocking */ }

                // Send confirmation email to client
                try
                {
                    var portalUrl = (settings.PortalUrl ?? "").TrimEnd('/');
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"] = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"] = title,
                        ["{portalLink}"] = $"{portalUrl}/portal/order.html?id={orderId}"
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("order_received",
                        "Commande reçue — {orderNumber}",
                        "Bonjour {clientName},\n\nVotre commande {orderNumber} \"{orderTitle}\" a bien été reçue.\n\nConsultez votre espace client : {portalLink}\n\nCordialement,",
                        vars);
                    PortalEmailHelper.SendEmail(client.Email, subj, body);
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] Order confirmation email failed: {ex.Message}"); }

                // Notify atelier
                try
                {
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"] = client.DisplayName,
                        ["{orderNumber}"] = orderNumber,
                        ["{orderTitle}"] = title,
                        ["{companyName}"] = client.CompanyName
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("atelier_new_client_order",
                        "Nouvelle commande client — {orderNumber}",
                        "Une nouvelle commande a été déposée sur le portail client.\n\nClient : {clientName} ({companyName})\nCommande : {orderNumber} — {orderTitle}",
                        vars);
                    PortalEmailHelper.SendAtelierNotification(subj, body);
                }
                catch { /* non-blocking */ }

                return Results.Json(new { ok = true, orderId, orderNumber });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // POST /api/portal/orders/{id}/files  — upload PDF(s) to an order
        app.MapPost("/api/portal/orders/{id}/files", async (HttpContext ctx, string id) =>
        {
            try
            {
                var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
                if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });

                var order = DocToOrder(doc);
                if (order.ClientAccountId != client.Id)
                    return Results.Json(new { ok = false, error = "Accès refusé" });

                var form = await ctx.Request.ReadFormAsync();
                if (!form.Files.Any())
                    return Results.Json(new { ok = false, error = "Aucun fichier reçu" });

                // Check max files
                int maxFiles = settings.MaxFilesPerOrder > 0 ? settings.MaxFilesPerOrder : 10;
                if (order.Files.Count + form.Files.Count > maxFiles)
                    return Results.Json(new { ok = false, error = $"Nombre maximum de fichiers ({maxFiles}) atteint" });

                long maxSizeBytes = (settings.MaxUploadSizeMb > 0 ? settings.MaxUploadSizeMb : 500) * 1024L * 1024L;

                var uploadedFiles = new List<string>();
                var hotRoot = BackendUtils.HotfoldersRoot();
                var webOrderDir = Path.Combine(hotRoot, settings.WebOrderKanbanFolder ?? "Commandes web", id);
                Directory.CreateDirectory(webOrderDir);

                foreach (var file in form.Files)
                {
                    // Sanitize filename
                    var safeName = Path.GetFileName(file.FileName);
                    safeName = System.Text.RegularExpressions.Regex.Replace(safeName, @"[^\w\.\-]", "_");
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = "file.pdf";

                    // Validate extension
                    var ext = Path.GetExtension(safeName).ToLowerInvariant();
                    if (ext != ".pdf")
                        return Results.Json(new { ok = false, error = $"{safeName}: seuls les fichiers PDF sont acceptés" });

                    if (file.Length > maxSizeBytes)
                        return Results.Json(new { ok = false, error = $"{safeName}: fichier trop volumineux (max {settings.MaxUploadSizeMb} Mo)" });

                    // Validate magic bytes (PDF signature: %PDF)
                    byte[] header = new byte[4];
                    using (var stream = file.OpenReadStream())
                    {
                        int read = await stream.ReadAsync(header, 0, 4);
                        if (read < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46)
                            return Results.Json(new { ok = false, error = $"{safeName}: le fichier n'est pas un PDF valide" });
                    }

                    // Unique filename
                    var num = MongoDbHelper.GetNextFileNumber();
                    var destName = $"{num:D5}_{safeName}";
                    var destPath = Path.Combine(webOrderDir, destName);

                    using (var fs = File.Create(destPath))
                    {
                        await file.CopyToAsync(fs);
                    }

                    order.Files.Add(new ClientOrderFile
                    {
                        FileName = destName,
                        StoredPath = destPath,
                        UploadedAt = DateTime.UtcNow,
                        Size = file.Length
                    });
                    uploadedFiles.Add(destName);
                }

                order.UpdatedAt = DateTime.UtcNow;

                // Update MongoDB
                var filesArray = new BsonArray(order.Files.Select(f => new BsonDocument
                {
                    ["fileName"] = f.FileName,
                    ["storedPath"] = f.StoredPath,
                    ["uploadedAt"] = f.UploadedAt,
                    ["size"] = f.Size
                }));

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("files", filesArray)
                        .Set("updatedAt", order.UpdatedAt));

                return Results.Json(new { ok = true, uploadedFiles });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // GET /api/portal/orders/{id}/files/{fileName}  — download a file
        app.MapGet("/api/portal/orders/{id}/files/{fileName}", (HttpContext ctx, string id, string fileName) =>
        {
            var client = PortalAuthEndpoints.GetAuthenticatedClient(ctx);
            if (client == null) return Results.Json(new { ok = false, error = "Non authentifié" });

            // Reject path traversal
            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\"))
                return Results.Json(new { ok = false, error = "Nom de fichier non autorisé" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
            if (doc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });

            var order = DocToOrder(doc);
            if (order.ClientAccountId != client.Id)
                return Results.Json(new { ok = false, error = "Accès refusé" });

            var fileEntry = order.Files.FirstOrDefault(f => f.FileName == fileName);
            if (fileEntry == null) return Results.Json(new { ok = false, error = "Fichier non trouvé" });

            if (!File.Exists(fileEntry.StoredPath))
                return Results.Json(new { ok = false, error = "Fichier non trouvé sur le serveur" });

            return Results.File(fileEntry.StoredPath, "application/pdf", fileName);
        });

        // GET /api/portal/config/form-options
        app.MapGet("/api/portal/config/form-options", () =>
        {
            var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
            return Results.Json(new
            {
                ok = true,
                formats = settings.AvailableFormats,
                papers = settings.AvailablePapers,
                finitions = settings.AvailableFinitions,
                maxUploadSizeMb = settings.MaxUploadSizeMb,
                maxFilesPerOrder = settings.MaxFilesPerOrder,
                welcomeText = settings.WelcomeText
            });
        });
    }

    // BsonDocument builder ---------------------------------------------------
    private static BsonDocument ToBsonDocument(ClientOrder o)
    {
        var filesArray = new BsonArray(o.Files.Select(f => new BsonDocument
        {
            ["fileName"] = f.FileName,
            ["storedPath"] = f.StoredPath,
            ["uploadedAt"] = f.UploadedAt,
            ["size"] = f.Size
        }));

        var historyArray = new BsonArray(o.StatusHistory.Select(h => new BsonDocument
        {
            ["status"] = h.Status,
            ["timestamp"] = h.Timestamp,
            ["comment"] = h.Comment
        }));

        var doc = new BsonDocument
        {
            ["id"] = o.Id,
            ["clientAccountId"] = o.ClientAccountId,
            ["orderNumber"] = o.OrderNumber,
            ["title"] = o.Title,
            ["quantity"] = o.Quantity,
            ["format"] = o.Format,
            ["paper"] = o.Paper,
            ["recto"] = o.Recto,
            ["finitions"] = new BsonArray(o.Finitions.Select(f => (BsonValue)f)),
            ["deliveryMode"] = o.DeliveryMode,
            ["deliveryAddress"] = o.DeliveryAddress,
            ["comments"] = o.Comments,
            ["status"] = o.Status,
            ["atelierJobPath"] = o.AtelierJobPath,
            ["createdAt"] = o.CreatedAt,
            ["updatedAt"] = o.UpdatedAt,
            ["files"] = filesArray,
            ["statusHistory"] = historyArray
        };

        if (o.DesiredDeliveryDate.HasValue)
            doc["desiredDeliveryDate"] = o.DesiredDeliveryDate.Value;
        else
            doc["desiredDeliveryDate"] = BsonNull.Value;

        return doc;
    }
}
