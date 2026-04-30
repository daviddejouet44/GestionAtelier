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

                if (json.TryGetProperty("enabled", out var eEl)) existing.Enabled = eEl.GetBoolean();
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

                // SMTP settings (separate)
                if (json.TryGetProperty("smtp", out var smtpEl))
                {
                    var smtpSettings = MongoDbHelper.GetSettings<PortalSmtpSettings>("portalSmtp") ?? new PortalSmtpSettings();
                    if (smtpEl.TryGetProperty("host", out var hEl)) smtpSettings.Host = hEl.GetString() ?? "";
                    if (smtpEl.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out var portVal)) smtpSettings.Port = portVal;
                    if (smtpEl.TryGetProperty("useSsl", out var sslEl)) smtpSettings.UseSsl = sslEl.GetBoolean();
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
                            ["{clientName}"] = clientName,
                            ["{orderNumber}"] = orderNumber,
                            ["{orderTitle}"] = orderTitle,
                            ["{status}"] = status,
                            ["{portalLink}"] = $"{portalUrl}/portal/order.html?id={orderId}"
                        };
                        var (subj, body) = PortalEmailHelper.RenderTemplate("client_order_status_changed",
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
            "client_welcome", "client_password_reset", "client_order_received",
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
    }
}
