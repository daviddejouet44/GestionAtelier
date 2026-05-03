using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;
using GestionAtelier.Endpoints.Portal;

namespace GestionAtelier.Endpoints.Admin;

public static class PortalAdminEndpoints
{
    // Validate admin token (profile == 3 = admin) ----------------------------
    private static bool IsAdmin(HttpContext ctx)
    {
        try
        {
            var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            return parts.Length >= 3 && parts[2] == "3";
        }
        catch { return false; }
    }

    public static void MapPortalAdminEndpoints(this WebApplication app)
    {
        // =====================================================================
        // Portal settings
        // =====================================================================

        // GET /api/admin/portal/settings
        app.MapGet("/api/admin/portal/settings", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
            var smtp = MongoDbHelper.GetSettings<PortalSmtpSettings>("portalSmtp") ?? new PortalSmtpSettings();
            return Results.Json(new { ok = true, settings, smtp = new { smtp.Host, smtp.Port, smtp.UseSsl, smtp.Username, smtp.FromAddress, smtp.FromName, smtp.AtelierNotifyEmail } });
        });

        // PUT /api/admin/portal/settings
        app.MapPut("/api/admin/portal/settings", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var existing = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();

                if (json.TryGetProperty("enabled", out var eEl) && eEl.ValueKind != JsonValueKind.Null) existing.Enabled = eEl.GetBoolean();
                if (json.TryGetProperty("portalUrl", out var puEl)) existing.PortalUrl = puEl.GetString() ?? "";
                if (json.TryGetProperty("welcomeText", out var wtEl)) existing.WelcomeText = wtEl.GetString() ?? "";
                if (json.TryGetProperty("maxUploadSizeMb", out var musEl) && musEl.TryGetInt32(out var mus)) existing.MaxUploadSizeMb = mus;
                if (json.TryGetProperty("maxFilesPerOrder", out var mfEl) && mfEl.TryGetInt32(out var mf)) existing.MaxFilesPerOrder = mf;
                if (json.TryGetProperty("maxLoginAttempts", out var mlaEl) && mlaEl.TryGetInt32(out var mla)) existing.MaxLoginAttempts = mla;
                if (json.TryGetProperty("lockDurationMinutes", out var ldmEl) && ldmEl.TryGetInt32(out var ldm)) existing.LockDurationMinutes = ldm;
                if (json.TryGetProperty("webOrderKanbanFolder", out var wokfEl)) existing.WebOrderKanbanFolder = wokfEl.GetString() ?? "Commandes web";
                if (json.TryGetProperty("availableFormats", out var afEl) && afEl.ValueKind == JsonValueKind.Array)
                    existing.AvailableFormats = afEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (json.TryGetProperty("availablePapers", out var apEl) && apEl.ValueKind == JsonValueKind.Array)
                    existing.AvailablePapers = apEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                if (json.TryGetProperty("availableFinitions", out var afEl2) && afEl2.ValueKind == JsonValueKind.Array)
                    existing.AvailableFinitions = afEl2.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                MongoDbHelper.UpsertSettings("portalSettings", existing);

                // Sync "Commandes web" tile to kanbanColumns if not already present
                try
                {
                    var folderName = existing.WebOrderKanbanFolder;
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        var kanbanCfg = MongoDbHelper.GetSettings<KanbanSettings>("kanbanColumns");
                        if (kanbanCfg == null || kanbanCfg.Columns == null || kanbanCfg.Columns.Count == 0)
                        {
                            // Initialise with default columns from the client-side defaults
                            kanbanCfg = new KanbanSettings
                            {
                                Columns = new List<KanbanColumnConfig>
                                {
                                    new() { Folder = "Début de production", Label = "Jobs à traiter",                Color = "#5fa8c4", Visible = true, Order = 0 },
                                    new() { Folder = "Corrections",         Label = "Preflight",                     Color = "#e0e0e0", Visible = true, Order = 1 },
                                    new() { Folder = "Corrections et fond perdu", Label = "Preflight avec fond perdu", Color = "#e0e0e0", Visible = true, Order = 2 },
                                    new() { Folder = "Prêt pour impression", Label = "En attente",                   Color = "#b8b8b8", Visible = true, Order = 3 },
                                    new() { Folder = "PrismaPrepare",        Label = "PrismaPrepare",                Color = "#8f8f8f", Visible = true, Order = 4 },
                                    new() { Folder = "Fiery",                Label = "Fiery",                        Color = "#8f8f8f", Visible = true, Order = 5 },
                                    new() { Folder = "Impression en cours",  Label = "Impression en cours",          Color = "#7a7a7a", Visible = true, Order = 6 },
                                    new() { Folder = "Façonnage",            Label = "Finitions",                    Color = "#666666", Visible = true, Order = 7 },
                                    new() { Folder = "Fin de production",    Label = "Fin de production",            Color = "#22c55e", Visible = true, Order = 8 },
                                }
                            };
                        }
                        if (kanbanCfg.Columns != null && !kanbanCfg.Columns.Any(c => c.Folder == folderName))
                        {
                            // Insert portal tile at the top (order -1), then re-number
                            kanbanCfg.Columns.Insert(0, new KanbanColumnConfig
                            {
                                Folder = folderName,
                                Label = "Commandes web",
                                Color = "#4f46e5",
                                Visible = true,
                                Order = 0
                            });
                            for (int i = 0; i < kanbanCfg.Columns.Count; i++)
                                kanbanCfg.Columns[i].Order = i;
                            MongoDbHelper.UpsertSettings("kanbanColumns", kanbanCfg);
                        }
                    }
                }
                catch (Exception ex2) { Console.WriteLine($"[WARN] Kanban tile sync failed: {ex2.Message}"); }

                // SMTP settings (separate)
                if (json.TryGetProperty("smtp", out var smtpEl))
                {
                    var smtpSettings = MongoDbHelper.GetSettings<PortalSmtpSettings>("portalSmtp") ?? new PortalSmtpSettings();
                    if (smtpEl.TryGetProperty("host", out var hEl)) smtpSettings.Host = hEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var portVal)) smtpSettings.Port = portVal;
                    if (smtpEl.TryGetProperty("useSsl", out var sslEl) && sslEl.ValueKind != JsonValueKind.Null) smtpSettings.UseSsl = sslEl.GetBoolean();
                    if (smtpEl.TryGetProperty("username", out var uEl)) smtpSettings.Username = uEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("password", out var pwEl) && !string.IsNullOrWhiteSpace(pwEl.GetString())) smtpSettings.Password = pwEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("fromAddress", out var faEl)) smtpSettings.FromAddress = faEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("fromName", out var fnEl)) smtpSettings.FromName = fnEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("atelierNotifyEmail", out var aneEl)) smtpSettings.AtelierNotifyEmail = aneEl.GetString() ?? "";
                    MongoDbHelper.UpsertSettings("portalSmtp", smtpSettings);
                }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // =====================================================================
        // Client account management
        // =====================================================================

        // GET /api/admin/portal/clients
        app.MapGet("/api/admin/portal/clients", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
            var docs = col.Find(new BsonDocument()).Sort(Builders<BsonDocument>.Sort.Ascending("email")).ToList();
            var clients = docs.Select(d => new
            {
                id = d.Contains("id") ? d["id"].AsString : "",
                email = d.Contains("email") ? d["email"].AsString : "",
                displayName = d.Contains("displayName") ? d["displayName"].AsString : "",
                companyName = d.Contains("companyName") ? d["companyName"].AsString : "",
                contactPhone = d.Contains("contactPhone") ? d["contactPhone"].AsString : "",
                enabled = !d.Contains("enabled") || d["enabled"].AsBoolean,
                createdAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : (DateTime?)null,
                lastLoginAt = d.Contains("lastLoginAt") && !d["lastLoginAt"].IsBsonNull ? d["lastLoginAt"].ToUniversalTime() : (DateTime?)null
            }).ToList();
            return Results.Json(new { ok = true, clients });
        });

        // POST /api/admin/portal/clients
        app.MapPost("/api/admin/portal/clients", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var email = json.TryGetProperty("email", out var eEl) ? eEl.GetString()?.Trim().ToLowerInvariant() ?? "" : "";
                var pwd = json.TryGetProperty("password", out var pEl) ? pEl.GetString() ?? "" : "";
                var displayName = json.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() ?? "" : "";
                var companyName = json.TryGetProperty("companyName", out var cnEl) ? cnEl.GetString() ?? "" : "";
                var contactPhone = json.TryGetProperty("contactPhone", out var cpEl) ? cpEl.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(email)) return Results.Json(new { ok = false, error = "Email requis" });
                if (string.IsNullOrWhiteSpace(pwd)) return Results.Json(new { ok = false, error = "Mot de passe requis" });
                if (pwd.Length < 8) return Results.Json(new { ok = false, error = "Le mot de passe doit contenir au moins 8 caractères" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var existing = col.Find(Builders<BsonDocument>.Filter.Eq("email", email)).FirstOrDefault();
                if (existing != null) return Results.Json(new { ok = false, error = "Un compte avec cet email existe déjà" });

                var id = Guid.NewGuid().ToString("N");
                var hash = BCrypt.Net.BCrypt.HashPassword(pwd);
                var now = DateTime.UtcNow;

                col.InsertOne(new BsonDocument
                {
                    ["id"] = id,
                    ["email"] = email,
                    ["passwordHash"] = hash,
                    ["displayName"] = displayName,
                    ["companyName"] = companyName,
                    ["contactPhone"] = contactPhone,
                    ["defaultDeliveryAddress"] = "",
                    ["enabled"] = true,
                    ["createdAt"] = now,
                    ["lastLoginAt"] = BsonNull.Value,
                    ["failedLoginAttempts"] = 0,
                    ["lockedUntil"] = BsonNull.Value
                });

                // Send welcome email
                try
                {
                    var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                    var portalUrl = (settings.PortalUrl ?? "").TrimEnd('/');
                    var vars = new Dictionary<string, string>
                    {
                        ["{clientName}"] = displayName,
                        ["{email}"] = email,
                        ["{portalLink}"] = $"{portalUrl}/portal/login.html"
                    };
                    var (subj, body) = PortalEmailHelper.RenderTemplate("client_welcome",
                        "Bienvenue sur votre espace client",
                        "Bonjour {clientName},\n\nVotre espace client a été créé.\n\nConnectez-vous ici : {portalLink}\nEmail : {email}\n\nCordialement,",
                        vars);
                    PortalEmailHelper.SendEmail(email, subj, body);
                }
                catch { /* non-blocking */ }

                return Results.Json(new { ok = true, id });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // PUT /api/admin/portal/clients/{id}
        app.MapPut("/api/admin/portal/clients/{id}", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Client non trouvé" });

                var updates = new List<UpdateDefinition<BsonDocument>>();
                var ub = Builders<BsonDocument>.Update;
                if (json.TryGetProperty("email", out var eEl)) updates.Add(ub.Set("email", eEl.GetString()?.ToLowerInvariant() ?? ""));
                if (json.TryGetProperty("displayName", out var dnEl)) updates.Add(ub.Set("displayName", dnEl.GetString() ?? ""));
                if (json.TryGetProperty("companyName", out var cnEl)) updates.Add(ub.Set("companyName", cnEl.GetString() ?? ""));
                if (json.TryGetProperty("contactPhone", out var cpEl)) updates.Add(ub.Set("contactPhone", cpEl.GetString() ?? ""));
                if (json.TryGetProperty("enabled", out var enEl)) updates.Add(ub.Set("enabled", enEl.GetBoolean()));

                if (updates.Count > 0)
                    col.UpdateOne(Builders<BsonDocument>.Filter.Eq("id", id), ub.Combine(updates));

                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // DELETE /api/admin/portal/clients/{id}  (disables account)
        app.MapDelete("/api/admin/portal/clients/{id}", (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
            col.UpdateOne(
                Builders<BsonDocument>.Filter.Eq("id", id),
                Builders<BsonDocument>.Update.Set("enabled", false));
            return Results.Json(new { ok = true });
        });

        // POST /api/admin/portal/clients/{id}/reset-password
        app.MapPost("/api/admin/portal/clients/{id}/reset-password", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var newPwd = json.TryGetProperty("password", out var pEl) ? pEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(newPwd)) return Results.Json(new { ok = false, error = "Mot de passe requis" });
                if (newPwd.Length < 8) return Results.Json(new { ok = false, error = "Minimum 8 caractères" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Client non trouvé" });

                var hash = BCrypt.Net.BCrypt.HashPassword(newPwd);
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("passwordHash", hash)
                        .Set("failedLoginAttempts", 0)
                        .Set("lockedUntil", BsonNull.Value));

                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // =====================================================================
        // BAT sending from atelier to client
        // =====================================================================

        // POST /api/admin/portal/orders/{orderId}/send-bat
        // Called by the atelier when sending a BAT to the client
        app.MapPost("/api/admin/portal/orders/{orderId}/send-bat", async (HttpContext ctx, string orderId) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var ordersCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var orderDoc = ordersCol.Find(Builders<BsonDocument>.Filter.Eq("id", orderId)).FirstOrDefault();
                if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });

                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var batFilePath = json.TryGetProperty("batFilePath", out var bfpEl) ? bfpEl.GetString() ?? "" : "";
                var batFileName = json.TryGetProperty("batFileName", out var bfnEl) ? bfnEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(batFileName)) batFileName = Path.GetFileName(batFilePath);

                var batId = Guid.NewGuid().ToString("N");
                var now = DateTime.UtcNow;

                var batCol = MongoDbHelper.GetCollection<BsonDocument>("client_bat_actions");
                batCol.InsertOne(new BsonDocument
                {
                    ["id"] = batId,
                    ["orderId"] = orderId,
                    ["batFileRef"] = batFilePath,
                    ["batFileName"] = batFileName,
                    ["action"] = "pending",
                    ["motif"] = "",
                    ["attachmentRef"] = "",
                    ["attachmentName"] = "",
                    ["performedAt"] = BsonNull.Value,
                    ["performedByClientId"] = "",
                    ["sentAt"] = now,
                    ["notificationEmailSent"] = false
                });

                // Update order status
                ordersCol.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", orderId),
                    Builders<BsonDocument>.Update
                        .Set("status", "bat_pending")
                        .Set("updatedAt", now)
                        .Push("statusHistory", new BsonDocument
                        {
                            ["status"] = "bat_pending",
                            ["timestamp"] = now,
                            ["comment"] = "BAT envoyé au client"
                        }));

                // Notify client
                try
                {
                    var clientId = orderDoc["clientAccountId"].AsString;
                    var clientCol = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                    var clientDoc = clientCol.Find(Builders<BsonDocument>.Filter.Eq("id", clientId)).FirstOrDefault();
                    if (clientDoc != null)
                    {
                        var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                        var portalUrl = (settings.PortalUrl ?? "").TrimEnd('/');
                        var clientEmail = clientDoc["email"].AsString;
                        var clientName = clientDoc.Contains("displayName") ? clientDoc["displayName"].AsString : clientEmail;
                        var orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : orderId;
                        var orderTitle = orderDoc.Contains("title") ? orderDoc["title"].AsString : "";
                        var batLink = $"{portalUrl}/portal/order.html?id={orderId}";

                        var vars = new Dictionary<string, string>
                        {
                            ["{clientName}"] = clientName,
                            ["{orderNumber}"] = orderNumber,
                            ["{orderTitle}"] = orderTitle,
                            ["{batLink}"] = batLink,
                            ["{portalLink}"] = portalUrl
                        };
                        var (subj, body) = PortalEmailHelper.RenderTemplate("client_bat_available",
                            "Un BAT est disponible — {orderNumber}",
                            "Bonjour {clientName},\n\nUn BAT est disponible pour votre commande {orderNumber} — {orderTitle}.\n\nConnectez-vous pour le consulter et le valider ou refuser :\n{batLink}\n\nCordialement,",
                            vars);
                        PortalEmailHelper.SendEmail(clientEmail, subj, body);

                        batCol.UpdateOne(
                            Builders<BsonDocument>.Filter.Eq("id", batId),
                            Builders<BsonDocument>.Update.Set("notificationEmailSent", true));
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] BAT notification email failed: {ex.Message}"); }

                return Results.Json(new { ok = true, batId });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // GET /api/admin/portal/orders — list all client portal orders (for kanban)
        app.MapGet("/api/admin/portal/orders", (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':');
                if (parts.Length < 3) return Results.Json(new { ok = false, error = "Non authentifié" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var docs = col.Find(new BsonDocument()).Sort(Builders<BsonDocument>.Sort.Descending("createdAt")).ToList();
                var orders = docs.Select(d => new
                {
                    id = d.Contains("id") ? d["id"].AsString : "",
                    clientAccountId = d.Contains("clientAccountId") ? d["clientAccountId"].AsString : "",
                    orderNumber = d.Contains("orderNumber") ? d["orderNumber"].AsString : "",
                    title = d.Contains("title") ? d["title"].AsString : "",
                    quantity = d.Contains("quantity") ? d["quantity"].AsInt32 : 0,
                    status = d.Contains("status") ? d["status"].AsString : "draft",
                    createdAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : (DateTime?)null,
                    updatedAt = d.Contains("updatedAt") ? d["updatedAt"].ToUniversalTime() : (DateTime?)null,
                    filesCount = d.Contains("files") && d["files"].IsBsonArray ? d["files"].AsBsonArray.Count : 0
                }).ToList();
                return Results.Json(new { ok = true, orders });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // PUT /api/admin/portal/orders/{orderId}/status  — update order status from atelier
        app.MapPut("/api/admin/portal/orders/{orderId}/status", async (HttpContext ctx, string orderId) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var status = json.TryGetProperty("status", out var sEl) ? sEl.GetString() ?? "" : "";
                var comment = json.TryGetProperty("comment", out var cEl) ? cEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(status)) return Results.Json(new { ok = false, error = "Status requis" });

                var now = DateTime.UtcNow;
                var col = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                var orderDoc = col.Find(Builders<BsonDocument>.Filter.Eq("id", orderId)).FirstOrDefault();
                if (orderDoc == null) return Results.Json(new { ok = false, error = "Commande non trouvée" });

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", orderId),
                    Builders<BsonDocument>.Update
                        .Set("status", status)
                        .Set("updatedAt", now)
                        .Push("statusHistory", new BsonDocument
                        {
                            ["status"] = status,
                            ["timestamp"] = now,
                            ["comment"] = comment
                        }));

                // Notify client by email
                try
                {
                    var clientId = orderDoc["clientAccountId"].AsString;
                    var clientCol = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                    var clientDoc = clientCol.Find(Builders<BsonDocument>.Filter.Eq("id", clientId)).FirstOrDefault();
                    if (clientDoc != null)
                    {
                        var portalSettings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                        var portalUrl = (portalSettings.PortalUrl ?? "").TrimEnd('/');
                        var clientEmail = clientDoc["email"].AsString;
                        var clientName = clientDoc.Contains("displayName") ? clientDoc["displayName"].AsString : clientEmail;
                        var orderNumber = orderDoc.Contains("orderNumber") ? orderDoc["orderNumber"].AsString : orderId;
                        var orderTitle = orderDoc.Contains("title") ? orderDoc["title"].AsString : "";

                        var vars = new Dictionary<string, string>
                        {
                            ["{clientName}"]  = clientName,
                            ["{orderNumber}"] = orderNumber,
                            ["{orderTitle}"]  = orderTitle,
                            ["{status}"]      = status,
                            ["{portalLink}"]  = $"{portalUrl}/portal/order.html?id={orderId}"
                        };

                        // Check if a step-specific email template is configured for this status
                        var stepsCfg = MongoDbHelper.GetSettings<PortalClientStepsConfig>("portalClientSteps");
                        var stepsMap = stepsCfg?.Steps.ToDictionary(
                            s => s.KanbanFolder.ToLowerInvariant(),
                            s => s.EmailTemplateKey) ?? new Dictionary<string, string>();
                        stepsMap.TryGetValue(status.ToLowerInvariant(), out var mappedTemplate);
                        var templateKey = !string.IsNullOrWhiteSpace(mappedTemplate)
                            ? mappedTemplate
                            : "client_order_status_changed";

                        var (subj, body) = PortalEmailHelper.RenderTemplate(templateKey,
                            "Mise à jour de votre commande — {orderNumber}",
                            "Bonjour {clientName},\n\nLe statut de votre commande {orderNumber} — {orderTitle} a été mis à jour.\n\nConsultez votre espace client : {portalLink}\n\nCordialement,",
                            vars);
                        PortalEmailHelper.SendEmail(clientEmail, subj, body);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] Status change email failed: {ex.Message}"); }

                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // =====================================================================
        // Portal email templates
        // =====================================================================
        var portalTemplateKeys = new[] {
            "client_welcome", "client_invitation", "client_password_reset", "client_order_received",
            "client_bat_available", "client_order_status_changed",
            "atelier_client_bat_validated", "atelier_client_bat_refused", "atelier_new_client_order"
        };

        app.MapGet("/api/admin/portal/email-templates", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var templates = new Dictionary<string, object>();
            foreach (var key in portalTemplateKeys)
            {
                var tpl = MongoDbHelper.GetSettings<PortalEmailTemplate>($"portalEmailTemplate_{key}");
                templates[key] = new { subject = tpl?.Subject ?? "", body = tpl?.Body ?? "" };
            }
            return Results.Json(new { ok = true, templates });
        });

        app.MapPut("/api/admin/portal/email-templates/{key}", async (HttpContext ctx, string key) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            if (!portalTemplateKeys.Contains(key)) return Results.Json(new { ok = false, error = "Template inconnu" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var subject = json.TryGetProperty("subject", out var sEl) ? sEl.GetString() ?? "" : "";
                var body = json.TryGetProperty("body", out var bEl) ? bEl.GetString() ?? "" : "";
                MongoDbHelper.UpsertSettings($"portalEmailTemplate_{key}", new PortalEmailTemplate { Subject = subject, Body = body });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // =====================================================================
        // Portal theme configuration
        // =====================================================================

        // GET /api/admin/portal/theme
        app.MapGet("/api/admin/portal/theme", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var theme = MongoDbHelper.GetSettings<PortalThemeConfig>("portalTheme") ?? new PortalThemeConfig();
            return Results.Json(new { ok = true, theme });
        });

        // PUT /api/admin/portal/theme
        app.MapPut("/api/admin/portal/theme", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var theme = MongoDbHelper.GetSettings<PortalThemeConfig>("portalTheme") ?? new PortalThemeConfig();

                if (json.TryGetProperty("primaryColor",      out var pc))  theme.PrimaryColor      = pc.GetString()  ?? theme.PrimaryColor;
                if (json.TryGetProperty("primaryDarkColor",  out var pdc)) theme.PrimaryDarkColor  = pdc.GetString() ?? theme.PrimaryDarkColor;
                if (json.TryGetProperty("primaryLightColor", out var plc)) theme.PrimaryLightColor = plc.GetString() ?? theme.PrimaryLightColor;
                if (json.TryGetProperty("backgroundColor",   out var bg))  theme.BackgroundColor   = bg.GetString()  ?? theme.BackgroundColor;
                if (json.TryGetProperty("textColor",         out var tc))  theme.TextColor         = tc.GetString()  ?? theme.TextColor;
                if (json.TryGetProperty("fontFamily",        out var ff))  theme.FontFamily        = ff.GetString()  ?? theme.FontFamily;
                if (json.TryGetProperty("companyName",       out var cn))  theme.CompanyName       = cn.GetString()  ?? theme.CompanyName;
                if (json.TryGetProperty("tagline",           out var tg))  theme.Tagline           = tg.GetString()  ?? "";
                if (json.TryGetProperty("contactLink",       out var cl))  theme.ContactLink       = cl.GetString()  ?? "";
                if (json.TryGetProperty("footerText",        out var ft))  theme.FooterText        = ft.GetString()  ?? "";
                if (json.TryGetProperty("loginBackground",   out var lb))  theme.LoginBackground   = lb.GetString()  ?? "";
                if (json.TryGetProperty("ordersPageText",    out var opt)) theme.OrdersPageText    = opt.GetString() ?? "";
                if (json.TryGetProperty("customCss",         out var css)) theme.CustomCss         = css.GetString() ?? "";
                if (json.TryGetProperty("usePortalSpecificLogo", out var upl)) theme.UsePortalSpecificLogo = upl.GetBoolean();
                if (json.TryGetProperty("portalLogoPath",    out var plp)) theme.PortalLogoPath    = plp.GetString() ?? "";

                MongoDbHelper.UpsertSettings("portalTheme", theme);
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // GET /api/portal/config/theme  (public — used by portal pages)
        app.MapGet("/api/portal/config/theme", () =>
        {
            var theme = MongoDbHelper.GetSettings<PortalThemeConfig>("portalTheme") ?? new PortalThemeConfig();
            return Results.Json(new { ok = true, theme });
        });

        // =====================================================================
        // Portal form fields configuration
        // =====================================================================

        static List<PortalFormFieldConfig> DefaultPortalFormFields() => new()
        {
            // ── Champs critiques (toujours présents) ──────────────────────────
            new() { Id = "title",            Label = "Intitulé du job",          Type = "text",     Visible = true,  Required = true,  Critical = true,  Order = 0 },
            new() { Id = "quantity",         Label = "Quantité",                 Type = "number",   Visible = true,  Required = true,  Critical = true,  Order = 1 },
            new() { Id = "delivery-mode",    Label = "Mode de livraison",        Type = "radio",    Visible = true,  Required = true,  Critical = true,  Order = 2 },
            // ── Caractéristiques du job ────────────────────────────────────────
            new() { Id = "format",           Label = "Format",                   Type = "select",   Visible = true,  Required = false, Critical = false, Order = 3 },
            new() { Id = "paper",            Label = "Support / Papier",         Type = "select",   Visible = true,  Required = false, Critical = false, Order = 4 },
            new() { Id = "recto",            Label = "Impression",               Type = "radio",    Visible = true,  Required = false, Critical = false, Order = 5 },
            new() { Id = "finitions",        Label = "Finitions souhaitées",     Type = "checkbox", Visible = true,  Required = false, Critical = false, Order = 6 },
            new() { Id = "type-travail",     Label = "Type de travail",          Type = "select",   Visible = false, Required = false, Critical = false, Order = 7 },
            new() { Id = "pagination",       Label = "Pagination (nombre pages)",Type = "number",   Visible = false, Required = false, Critical = false, Order = 8 },
            new() { Id = "encres",           Label = "Encres",                   Type = "text",     Visible = false, Required = false, Critical = false, Order = 9 },
            new() { Id = "format-feuille",   Label = "Format feuille machine",   Type = "text",     Visible = false, Required = false, Critical = false, Order = 10 },
            // ── Façonnage ──────────────────────────────────────────────────────
            new() { Id = "forme-decoupe",    Label = "Forme de découpe",         Type = "text",     Visible = false, Required = false, Critical = false, Order = 11 },
            new() { Id = "faconnage-binding",Label = "Type de reliure",          Type = "select",   Visible = false, Required = false, Critical = false, Order = 12 },
            // ── Planification / livraison ──────────────────────────────────────
            new() { Id = "delivery-date",    Label = "Date de réception souhaitée", Type = "date",  Visible = true,  Required = false, Critical = false, Order = 13 },
            new() { Id = "delivery-address", Label = "Adresse de livraison",     Type = "textarea", Visible = true,  Required = false, Critical = false, Order = 14 },
            // ── Contacts ───────────────────────────────────────────────────────
            new() { Id = "numero-dossier",   Label = "Référence / N° dossier",   Type = "text",     Visible = false, Required = false, Critical = false, Order = 15 },
            new() { Id = "numero-affaire",   Label = "N° d'affaire",             Type = "text",     Visible = false, Required = false, Critical = false, Order = 16 },
            // ── Informations complémentaires ───────────────────────────────────
            new() { Id = "notes",            Label = "Notes internes",           Type = "textarea", Visible = false, Required = false, Critical = false, Order = 17 },
            new() { Id = "comments",         Label = "Commentaires (client)",    Type = "textarea", Visible = true,  Required = false, Critical = false, Order = 18 },
        };

        // GET /api/admin/portal/form-fields
        app.MapGet("/api/admin/portal/form-fields", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var cfg = MongoDbHelper.GetSettings<PortalFormFieldsConfig>("portalFormFields");
            var fields = (cfg == null || cfg.Fields.Count == 0) ? DefaultPortalFormFields() : cfg.Fields;
            return Results.Json(new { ok = true, fields });
        });

        // PUT /api/admin/portal/form-fields
        app.MapPut("/api/admin/portal/form-fields", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (!json.TryGetProperty("fields", out var fieldsEl) || fieldsEl.ValueKind != JsonValueKind.Array)
                    return Results.Json(new { ok = false, error = "fields manquant" });

                var fields = new List<PortalFormFieldConfig>();
                foreach (var f in fieldsEl.EnumerateArray())
                {
                    var id = f.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var allowedValues = new List<string>();
                    if (f.TryGetProperty("allowedValues", out var avEl) && avEl.ValueKind == JsonValueKind.Array)
                        allowedValues = avEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                    fields.Add(new PortalFormFieldConfig
                    {
                        Id           = id,
                        Label        = f.TryGetProperty("label",        out var lEl)  ? lEl.GetString()  ?? "" : "",
                        CustomLabel  = f.TryGetProperty("customLabel",  out var clEl) ? clEl.GetString() ?? "" : "",
                        Placeholder  = f.TryGetProperty("placeholder",  out var phEl) ? phEl.GetString() ?? "" : "",
                        Type         = f.TryGetProperty("type",         out var tEl)  ? tEl.GetString()  ?? "text" : "text",
                        Visible      = f.TryGetProperty("visible",      out var vEl)  ? vEl.GetBoolean()  : true,
                        Required     = f.TryGetProperty("required",     out var rEl)  ? rEl.GetBoolean()  : false,
                        Critical     = f.TryGetProperty("critical",     out var crEl) ? crEl.GetBoolean() : false,
                        Order        = f.TryGetProperty("order",        out var oEl)  ? oEl.GetInt32()    : 0,
                        DefaultValue = f.TryGetProperty("defaultValue", out var dvEl) ? dvEl.GetString() ?? "" : "",
                        AllowedValues = allowedValues,
                    });
                }

                // Ensure critical fields are visible and required
                foreach (var f in fields.Where(f => f.Critical)) { f.Visible = true; f.Required = true; }

                MongoDbHelper.UpsertSettings("portalFormFields", new PortalFormFieldsConfig { Fields = fields });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // GET /api/portal/config/form-fields  (public — used by order-new.html)
        app.MapGet("/api/portal/config/form-fields", () =>
        {
            var cfg = MongoDbHelper.GetSettings<PortalFormFieldsConfig>("portalFormFields");
            var fields = (cfg == null || cfg.Fields.Count == 0) ? DefaultPortalFormFields() : cfg.Fields;
            // Only return visible fields to clients, sorted by order
            var visible = fields.Where(f => f.Visible).OrderBy(f => f.Order).Select(f => new
            {
                id           = f.Id,
                label        = string.IsNullOrWhiteSpace(f.CustomLabel) ? f.Label : f.CustomLabel,
                placeholder  = f.Placeholder,
                type         = f.Type,
                visible      = true, // always true here (we already filtered to Visible:true above)
                required     = f.Required,
                critical     = f.Critical,
                defaultValue = f.DefaultValue,
                allowedValues = f.AllowedValues
            }).ToList();
            return Results.Json(new { ok = true, fields = visible });
        });

        // =====================================================================
        // Portal client steps (Kanban tile → client-facing stage mapping)
        // =====================================================================

        // GET /api/admin/portal/client-steps
        app.MapGet("/api/admin/portal/client-steps", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            var saved = MongoDbHelper.GetSettings<PortalClientStepsConfig>("portalClientSteps") ?? new PortalClientStepsConfig();
            var kanbanCfg = MongoDbHelper.GetSettings<KanbanSettings>("kanbanColumns") ?? new KanbanSettings();

            // Merge: for each Kanban column, pick the saved step or build a default
            var savedMap = saved.Steps.ToDictionary(s => s.KanbanFolder, s => s);
            var steps = kanbanCfg.Columns
                .OrderBy(c => c.Order)
                .Select((c, i) =>
                {
                    var sv = savedMap.TryGetValue(c.Folder, out var s) ? s : null;
                    return new PortalClientStep
                    {
                        KanbanFolder     = c.Folder,
                        ClientLabel      = sv?.ClientLabel ?? c.Label,
                        Visible          = sv?.Visible ?? false,
                        Order            = sv?.Order ?? i,
                        EmailTemplateKey = sv?.EmailTemplateKey ?? ""
                    };
                })
                .OrderBy(s => s.Order)
                .ToList();

            return Results.Json(new { ok = true, steps });
        });

        // PUT /api/admin/portal/client-steps
        app.MapPut("/api/admin/portal/client-steps", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                if (!json.TryGetProperty("steps", out var stepsEl) || stepsEl.ValueKind != JsonValueKind.Array)
                    return Results.Json(new { ok = false, error = "steps manquant" });

                var steps = new List<PortalClientStep>();
                int idx = 0;
                foreach (var s in stepsEl.EnumerateArray())
                {
                    var folder = s.TryGetProperty("kanbanFolder", out var fEl) ? fEl.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(folder)) continue;
                    steps.Add(new PortalClientStep
                    {
                        KanbanFolder     = folder,
                        ClientLabel      = s.TryGetProperty("clientLabel",      out var lEl)  ? lEl.GetString()  ?? "" : "",
                        Visible          = s.TryGetProperty("visible",          out var vEl)  ? vEl.GetBoolean() : false,
                        Order            = s.TryGetProperty("order",            out var oEl)  ? oEl.GetInt32()   : idx,
                        EmailTemplateKey = s.TryGetProperty("emailTemplateKey", out var etEl) ? etEl.GetString() ?? "" : ""
                    });
                    idx++;
                }

                MongoDbHelper.UpsertSettings("portalClientSteps", new PortalClientStepsConfig { Steps = steps });
                return Results.Json(new { ok = true });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // GET /api/portal/config/client-steps  (public — used by client portal pages)
        app.MapGet("/api/portal/config/client-steps", () =>
        {
            var cfg = MongoDbHelper.GetSettings<PortalClientStepsConfig>("portalClientSteps") ?? new PortalClientStepsConfig();
            var visibleSteps = cfg.Steps
                .Where(s => s.Visible)
                .OrderBy(s => s.Order)
                .Select(s => new { kanbanFolder = s.KanbanFolder, clientLabel = string.IsNullOrWhiteSpace(s.ClientLabel) ? s.KanbanFolder : s.ClientLabel })
                .ToList();
            return Results.Json(new { ok = true, steps = visibleSteps });
        });

        // =====================================================================
        // Client invitation (sends activation link by email)
        // =====================================================================

        // POST /api/admin/portal/clients/{id}/invite
        app.MapPost("/api/admin/portal/clients/{id}/invite", async (HttpContext ctx, string id) =>
        {
            if (!IsAdmin(ctx)) return Results.Json(new { ok = false, error = "Admin only" });
            try
            {
                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
                if (doc == null) return Results.Json(new { ok = false, error = "Client non trouvé" });

                var client = PortalAuthEndpoints.DocToClient(doc);

                // Generate invitation token (48h validity) with high-entropy random bytes
                var tokenBytes = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(tokenBytes);
                var token  = Convert.ToHexString(tokenBytes).ToLowerInvariant();
                var expiry = DateTime.UtcNow.AddHours(48);

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("inviteToken",  token)
                        .Set("inviteExpiry", expiry)
                        .Set("enabled",      true));

                var settings   = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                var portalUrl  = (settings.PortalUrl ?? "").TrimEnd('/');
                var activateLink = $"{portalUrl}/portal/activate.html?token={token}";

                var vars = new Dictionary<string, string>
                {
                    ["{clientName}"]   = client.DisplayName.Length > 0 ? client.DisplayName : client.Email,
                    ["{email}"]        = client.Email,
                    ["{activateLink}"] = activateLink,
                    ["{portalLink}"]   = portalUrl
                };
                var (subj, body) = PortalEmailHelper.RenderTemplate(
                    "client_invitation",
                    "Invitation à votre espace client",
                    "Bonjour {clientName},\n\nVous avez été invité à accéder à votre espace client.\n\nCliquez sur le lien ci-dessous pour activer votre accès et définir votre mot de passe (lien valable 48h) :\n{activateLink}\n\nEmail de connexion : {email}\n\nCordialement,",
                    vars);

                PortalEmailHelper.SendEmail(client.Email, subj, body);
                return Results.Json(new { ok = true, email = client.Email });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });

        // POST /api/portal/auth/activate  (public — client sets password via invite token)
        app.MapPost("/api/portal/auth/activate", async (HttpContext ctx) =>
        {
            try
            {
                var json = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                var token  = json.TryGetProperty("token",    out var tEl) ? tEl.GetString()  ?? "" : "";
                var newPwd = json.TryGetProperty("password", out var pEl) ? pEl.GetString()  ?? "" : "";

                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Token manquant" });
                if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 8)
                    return Results.Json(new { ok = false, error = "Le mot de passe doit contenir au moins 8 caractères" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("client_accounts");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("inviteToken", token)).FirstOrDefault();
                if (doc == null)
                    return Results.Json(new { ok = false, error = "Lien invalide ou déjà utilisé" });

                var expiry = doc.Contains("inviteExpiry") && !doc["inviteExpiry"].IsBsonNull
                    ? doc["inviteExpiry"].ToUniversalTime()
                    : (DateTime?)null;
                if (!expiry.HasValue || expiry.Value < DateTime.UtcNow)
                    return Results.Json(new { ok = false, error = "Ce lien d'invitation a expiré" });

                var hash     = BCrypt.Net.BCrypt.HashPassword(newPwd);
                var clientId = doc["id"].AsString;

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", clientId),
                    Builders<BsonDocument>.Update
                        .Set("passwordHash",          hash)
                        .Set("inviteToken",           BsonNull.Value)
                        .Set("inviteExpiry",          BsonNull.Value)
                        .Set("failedLoginAttempts",   0)
                        .Set("lockedUntil",           BsonNull.Value));

                var email = doc["email"].AsString;
                var portalToken = PortalAuthEndpoints.MakePortalToken(clientId, email);
                return Results.Json(new { ok = true, token = portalToken, email });
            }
            catch (Exception ex) { return Results.Json(new { ok = false, error = ex.Message }); }
        });
    }
}
