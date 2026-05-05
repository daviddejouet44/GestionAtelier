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
        typeTravail = o.TypeTravail,
        pagination = o.Pagination,
        encres = o.Encres,
        formatFeuille = o.FormatFeuille,
        formeDecoupe = o.FormeDecoupe,
        faconnageBinding = o.FaconnageBinding,
        numeroDossier = o.NumeroDossier,
        numeroAffaire = o.NumeroAffaire,
        notes = o.Notes,
        bat = o.Bat,
        quantiteJustifs = o.QuantiteJustifs,
        adresseJustifs = o.AdresseJustifs,
        media1 = o.Media1,
        media2 = o.Media2,
        media3 = o.Media3,
        media4 = o.Media4,
        mediaCouverture = o.MediaCouverture,
        formatFini = o.FormatFini,
        donneurOrdreNom = o.DonneurOrdreNom,
        donneurOrdrePrenom = o.DonneurOrdrePrenom,
        donneurOrdreTelephone = o.DonneurOrdreTelephone,
        donneurOrdreEmail = o.DonneurOrdreEmail,
        donneurOrdreSociete = o.DonneurOrdreSociete,
        rainage = o.Rainage,
        vernisSelectif = o.VernisSelectif,
        dorureAChaud = o.DorureAChaud,
        pelliculage = o.Pelliculage,
        plis = o.Plis,
        deliveryPoints = o.DeliveryPoints,
        dateEnvoi = o.DateEnvoi,
        dateImpression = o.DateImpression,
        dateProductionFinitions = o.DateProductionFinitions,
        status = o.Status,
        createdAt = o.CreatedAt,
        updatedAt = o.UpdatedAt,
        statusHistory = o.StatusHistory,
        files = includeFiles ? o.Files.Select(f => new { fileName = f.FileName, uploadedAt = f.UploadedAt, size = f.Size }).ToList() : null,
    };

    internal static ClientOrder DocToOrder(BsonDocument d)
    {
        var finitions = new List<string>();
        if (d.Contains("finitions") && d["finitions"].IsBsonArray)
            finitions = d["finitions"].AsBsonArray.Select(v => v.AsString).ToList();

        var pelliculage = new List<string>();
        if (d.Contains("pelliculage") && d["pelliculage"].IsBsonArray)
            pelliculage = d["pelliculage"].AsBsonArray.Select(v => v.AsString).ToList();

        var deliveryPoints = new List<DeliveryPoint>();
        if (d.Contains("deliveryPoints") && d["deliveryPoints"].IsBsonArray)
        {
            foreach (var p in d["deliveryPoints"].AsBsonArray)
            {
                if (p.IsBsonDocument)
                {
                    var pd = p.AsBsonDocument;
                    deliveryPoints.Add(new DeliveryPoint
                    {
                        Id = pd.Contains("id") ? pd["id"].AsString : Guid.NewGuid().ToString("N"),
                        Address = pd.Contains("address") ? pd["address"].AsString : "",
                        Quantity = pd.Contains("quantity") ? pd["quantity"].AsInt32 : 0,
                        Notes = pd.Contains("notes") && !pd["notes"].IsBsonNull ? pd["notes"].AsString : null
                    });
                }
            }
        }

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
            TypeTravail = d.Contains("typeTravail") && !d["typeTravail"].IsBsonNull ? d["typeTravail"].AsString : null,
            Pagination = d.Contains("pagination") && !d["pagination"].IsBsonNull ? d["pagination"].AsInt32 : null,
            Encres = d.Contains("encres") && !d["encres"].IsBsonNull ? d["encres"].AsString : null,
            FormatFeuille = d.Contains("formatFeuille") && !d["formatFeuille"].IsBsonNull ? d["formatFeuille"].AsString : null,
            FormeDecoupe = d.Contains("formeDecoupe") && !d["formeDecoupe"].IsBsonNull ? d["formeDecoupe"].AsString : null,
            FaconnageBinding = d.Contains("faconnageBinding") && !d["faconnageBinding"].IsBsonNull ? d["faconnageBinding"].AsString : null,
            NumeroDossier = d.Contains("numeroDossier") && !d["numeroDossier"].IsBsonNull ? d["numeroDossier"].AsString : null,
            NumeroAffaire = d.Contains("numeroAffaire") && !d["numeroAffaire"].IsBsonNull ? d["numeroAffaire"].AsString : null,
            Notes = d.Contains("notes") && !d["notes"].IsBsonNull ? d["notes"].AsString : null,
            Bat = d.Contains("bat") && !d["bat"].IsBsonNull ? d["bat"].AsString : null,
            QuantiteJustifs = d.Contains("quantiteJustifs") && !d["quantiteJustifs"].IsBsonNull ? d["quantiteJustifs"].AsInt32 : null,
            AdresseJustifs = d.Contains("adresseJustifs") && !d["adresseJustifs"].IsBsonNull ? d["adresseJustifs"].AsString : null,
            Media1 = d.Contains("media1") && !d["media1"].IsBsonNull ? d["media1"].AsString : null,
            Media2 = d.Contains("media2") && !d["media2"].IsBsonNull ? d["media2"].AsString : null,
            Media3 = d.Contains("media3") && !d["media3"].IsBsonNull ? d["media3"].AsString : null,
            Media4 = d.Contains("media4") && !d["media4"].IsBsonNull ? d["media4"].AsString : null,
            MediaCouverture = d.Contains("mediaCouverture") && !d["mediaCouverture"].IsBsonNull ? d["mediaCouverture"].AsString : null,
            FormatFini = d.Contains("formatFini") && !d["formatFini"].IsBsonNull ? d["formatFini"].AsString : null,
            DonneurOrdreNom = d.Contains("donneurOrdreNom") && !d["donneurOrdreNom"].IsBsonNull ? d["donneurOrdreNom"].AsString : null,
            DonneurOrdrePrenom = d.Contains("donneurOrdrePrenom") && !d["donneurOrdrePrenom"].IsBsonNull ? d["donneurOrdrePrenom"].AsString : null,
            DonneurOrdreTelephone = d.Contains("donneurOrdreTelephone") && !d["donneurOrdreTelephone"].IsBsonNull ? d["donneurOrdreTelephone"].AsString : null,
            DonneurOrdreEmail = d.Contains("donneurOrdreEmail") && !d["donneurOrdreEmail"].IsBsonNull ? d["donneurOrdreEmail"].AsString : null,
            DonneurOrdreSociete = d.Contains("donneurOrdreSociete") && !d["donneurOrdreSociete"].IsBsonNull ? d["donneurOrdreSociete"].AsString : null,
            Rainage = d.Contains("rainage") && !d["rainage"].IsBsonNull ? (bool?)d["rainage"].AsBoolean : null,
            VernisSelectif = d.Contains("vernisSelectif") && !d["vernisSelectif"].IsBsonNull ? (bool?)d["vernisSelectif"].AsBoolean : null,
            DorureAChaud = d.Contains("dorureAChaud") && !d["dorureAChaud"].IsBsonNull ? d["dorureAChaud"].AsString : null,
            Pelliculage = pelliculage,
            Plis = d.Contains("plis") && !d["plis"].IsBsonNull ? d["plis"].AsString : null,
            DeliveryPoints = deliveryPoints,
            DateEnvoi = d.Contains("dateEnvoi") && !d["dateEnvoi"].IsBsonNull ? d["dateEnvoi"].ToUniversalTime() : null,
            DateImpression = d.Contains("dateImpression") && !d["dateImpression"].IsBsonNull ? d["dateImpression"].ToUniversalTime() : null,
            DateProductionFinitions = d.Contains("dateProductionFinitions") && !d["dateProductionFinitions"].IsBsonNull ? d["dateProductionFinitions"].ToUniversalTime() : null,
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
                    if (DateTime.TryParse(ddEl.GetString(), out var ddParsed)) desiredDate = ddParsed;

                var finitions = new List<string>();
                if (json.TryGetProperty("finitions", out var finEl) && finEl.ValueKind == JsonValueKind.Array)
                    finitions = finEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

                // Extended fields
                string? typeTravail = json.TryGetProperty("typeTravail", out var ttEl) ? ttEl.GetString() : null;
                int? pagination = null;
                if (json.TryGetProperty("pagination", out var pgEl) && pgEl.TryGetInt32(out var pgI)) pagination = pgI;
                string? encres = json.TryGetProperty("encres", out var encEl) ? encEl.GetString() : null;
                string? formatFeuille = json.TryGetProperty("formatFeuille", out var ffEl) ? ffEl.GetString() : null;
                string? formeDecoupe = json.TryGetProperty("formeDecoupe", out var fdEl) ? fdEl.GetString() : null;
                string? faconnageBinding = json.TryGetProperty("faconnageBinding", out var fbEl) ? fbEl.GetString() : null;
                string? numeroDossier = json.TryGetProperty("numeroDossier", out var ndEl) ? ndEl.GetString() : null;
                string? numeroAffaire = json.TryGetProperty("numeroAffaire", out var naEl) ? naEl.GetString() : null;
                string? notes = json.TryGetProperty("notes", out var notEl) ? notEl.GetString() : null;
                string? bat = json.TryGetProperty("bat", out var batEl) ? batEl.GetString() : null;
                int? quantiteJustifs = null;
                if (json.TryGetProperty("quantiteJustifs", out var qjEl) && qjEl.TryGetInt32(out var qjI)) quantiteJustifs = qjI;
                string? adresseJustifs = json.TryGetProperty("adresseJustifs", out var ajEl) ? ajEl.GetString() : null;
                string? media1 = json.TryGetProperty("media1", out var m1El) ? m1El.GetString() : null;
                string? media2 = json.TryGetProperty("media2", out var m2El) ? m2El.GetString() : null;
                string? media3 = json.TryGetProperty("media3", out var m3El) ? m3El.GetString() : null;
                string? media4 = json.TryGetProperty("media4", out var m4El) ? m4El.GetString() : null;
                string? mediaCouverture = json.TryGetProperty("mediaCouverture", out var mcEl) ? mcEl.GetString() : null;
                string? formatFini = json.TryGetProperty("formatFini", out var fnEl) ? fnEl.GetString() : null;

                // Donneur d'ordre
                string? donneurOrdreNom     = json.TryGetProperty("donneurOrdreNom",     out var donNomEl)    ? donNomEl.GetString()    : null;
                string? donneurOrdrePrenom  = json.TryGetProperty("donneurOrdrePrenom",  out var donPrenEl)   ? donPrenEl.GetString()   : null;
                string? donneurOrdreTel     = json.TryGetProperty("donneurOrdreTelephone",out var donTelEl)   ? donTelEl.GetString()   : null;
                string? donneurOrdreEmail   = json.TryGetProperty("donneurOrdreEmail",   out var donEmailEl)  ? donEmailEl.GetString()  : null;
                string? donneurOrdreSociete = json.TryGetProperty("donneurOrdreSociete", out var donSocEl)    ? donSocEl.GetString()    : null;

                // Finitions étendues
                bool? rainage = null;
                if (json.TryGetProperty("rainage", out var rainEl) && (rainEl.ValueKind == JsonValueKind.True || rainEl.ValueKind == JsonValueKind.False)) rainage = rainEl.GetBoolean();
                bool? vernisSelectif = null;
                if (json.TryGetProperty("vernisSelectif", out var vsEl) && (vsEl.ValueKind == JsonValueKind.True || vsEl.ValueKind == JsonValueKind.False)) vernisSelectif = vsEl.GetBoolean();
                string? dorureAChaud = json.TryGetProperty("dorureAChaud", out var dorureEl) ? dorureEl.GetString() : null;
                var pelliculage = new List<string>();
                if (json.TryGetProperty("pelliculage", out var pelEl) && pelEl.ValueKind == JsonValueKind.Array)
                    pelliculage = pelEl.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                string? plis = json.TryGetProperty("plis", out var plisEl) ? plisEl.GetString() : null;

                // Multi-points de livraison
                var deliveryPoints = new List<DeliveryPoint>();
                if (json.TryGetProperty("deliveryPoints", out var dpEl) && dpEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pt in dpEl.EnumerateArray())
                    {
                        var addr = pt.TryGetProperty("address", out var addrEl) ? addrEl.GetString() ?? "" : "";
                        int ptQty = 0;
                        if (pt.TryGetProperty("quantity", out var ptQEl)) ptQEl.TryGetInt32(out ptQty);
                        var ptNotes = pt.TryGetProperty("notes", out var ptNEl) ? ptNEl.GetString() : null;
                        deliveryPoints.Add(new DeliveryPoint { Address = addr, Quantity = ptQty, Notes = ptNotes });
                    }
                }

                // Indicative dates
                DateTime? dateEnvoi = null;
                if (json.TryGetProperty("dateEnvoi", out var deEl) && deEl.ValueKind == JsonValueKind.String && DateTime.TryParse(deEl.GetString(), out var deParsed)) dateEnvoi = DateTime.SpecifyKind(deParsed, DateTimeKind.Utc);
                DateTime? dateImpression = null;
                if (json.TryGetProperty("dateImpression", out var diEl) && diEl.ValueKind == JsonValueKind.String && DateTime.TryParse(diEl.GetString(), out var diParsed)) dateImpression = DateTime.SpecifyKind(diParsed, DateTimeKind.Utc);
                DateTime? dateProductionFinitions = null;
                if (json.TryGetProperty("dateProductionFinitions", out var dpfEl) && dpfEl.ValueKind == JsonValueKind.String && DateTime.TryParse(dpfEl.GetString(), out var dpfParsed)) dateProductionFinitions = DateTime.SpecifyKind(dpfParsed, DateTimeKind.Utc);
                if (deliveryMode == "livraison" && string.IsNullOrWhiteSpace(deliveryAddress) && deliveryPoints.Count == 0)
                    return Results.Json(new { ok = false, error = "Pour le mode Livraison, veuillez renseigner une adresse de livraison ou au moins un point de livraison" });

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
                    TypeTravail = typeTravail,
                    Pagination = pagination,
                    Encres = encres,
                    FormatFeuille = formatFeuille,
                    FormeDecoupe = formeDecoupe,
                    FaconnageBinding = faconnageBinding,
                    NumeroDossier = numeroDossier,
                    NumeroAffaire = numeroAffaire,
                    Notes = notes,
                    Bat = bat,
                    QuantiteJustifs = quantiteJustifs,
                    AdresseJustifs = adresseJustifs,
                    Media1 = media1,
                    Media2 = media2,
                    Media3 = media3,
                    Media4 = media4,
                    MediaCouverture = mediaCouverture,
                    FormatFini = formatFini,
                    DonneurOrdreNom = donneurOrdreNom,
                    DonneurOrdrePrenom = donneurOrdrePrenom,
                    DonneurOrdreTelephone = donneurOrdreTel,
                    DonneurOrdreEmail = donneurOrdreEmail,
                    DonneurOrdreSociete = donneurOrdreSociete,
                    Rainage = rainage,
                    VernisSelectif = vernisSelectif,
                    DorureAChaud = dorureAChaud,
                    Pelliculage = pelliculage,
                    Plis = plis,
                    DeliveryPoints = deliveryPoints,
                    DateEnvoi = dateEnvoi,
                    DateImpression = dateImpression,
                    DateProductionFinitions = dateProductionFinitions,
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
                    var portalUrl = PortalEmailHelper.SanitizePortalBaseUrl(settings.PortalUrl);
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

        // POST /api/portal/orders/{id}/submit  — move files to kanban root and mark as submitted
        app.MapPost("/api/portal/orders/{id}/submit", async (HttpContext ctx, string id) =>
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

                if (order.Status != "draft")
                    return Results.Json(new { ok = false, error = "La commande a déjà été soumise" });

                var hotRoot = BackendUtils.HotfoldersRoot();
                var webFolder = Path.Combine(hotRoot, settings.WebOrderKanbanFolder ?? "Commandes web");
                Directory.CreateDirectory(webFolder);

                var titlePart = SanitizeForFs(order.Title);

                // Move each file from the draft subfolder to the kanban root folder
                for (int i = 0; i < order.Files.Count; i++)
                {
                    var f = order.Files[i];
                    var newName = $"{order.OrderNumber}__{titlePart}__{Path.GetFileName(f.StoredPath)}";
                    var newPath = Path.Combine(webFolder, newName);

                    // Avoid collisions
                    if (File.Exists(newPath))
                    {
                        var stem = Path.GetFileNameWithoutExtension(newName);
                        var ext2 = Path.GetExtension(newName);
                        newName = $"{stem}_{i}{ext2}";
                        newPath = Path.Combine(webFolder, newName);
                    }

                    if (File.Exists(f.StoredPath))
                        File.Move(f.StoredPath, newPath);

                    order.Files[i] = new ClientOrderFile
                    {
                        FileName = newName,
                        StoredPath = newPath,
                        UploadedAt = f.UploadedAt,
                        Size = f.Size
                    };
                }

                // Try to remove now-empty draft subfolder
                try
                {
                    var draftDir = Path.Combine(hotRoot, settings.WebOrderKanbanFolder ?? "Commandes web", id);
                    if (Directory.Exists(draftDir) && !Directory.EnumerateFileSystemEntries(draftDir).Any())
                        Directory.Delete(draftDir);
                }
                catch { /* non-blocking */ }

                var now = DateTime.UtcNow;
                order.Status = "submitted";
                order.UpdatedAt = now;
                order.StatusHistory.Add(new ClientOrderStatusEntry
                {
                    Status = "submitted",
                    Timestamp = now,
                    Comment = "Commande soumise par le client"
                });

                var filesArray = new BsonArray(order.Files.Select(f => new BsonDocument
                {
                    ["fileName"] = f.FileName,
                    ["storedPath"] = f.StoredPath,
                    ["uploadedAt"] = f.UploadedAt,
                    ["size"] = f.Size
                }));

                var historyArray = new BsonArray(order.StatusHistory.Select(h => new BsonDocument
                {
                    ["status"] = h.Status,
                    ["timestamp"] = h.Timestamp,
                    ["comment"] = h.Comment
                }));

                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("id", id),
                    Builders<BsonDocument>.Update
                        .Set("status", "submitted")
                        .Set("updatedAt", now)
                        .Set("files", filesArray)
                        .Set("statusHistory", historyArray));

                return Results.Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // GET /api/portal/orders/{id}/files/{fileName}  — download a file
        app.MapGet("/api/portal/orders/{id}/files/{fileName}", (HttpContext ctx, string id, string fileName) =>        {
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
            // Load work types from the same source used by the fabrication form
            List<string> typesTravail = new();
            try
            {
                var wtCol = MongoDbHelper.GetCollection<BsonDocument>("workTypes");
                typesTravail = wtCol.Find(FilterDefinition<BsonDocument>.Empty).ToList()
                    .Select(d => d.Contains("name") ? d["name"].AsString : "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .OrderBy(s => s)
                    .ToList();
            }
            catch { /* ignore — fall back to empty list */ }

            // Load all finitions (faconnage options) and filter out "Sortie"
            List<string> finitionsAll = settings.AvailableFinitions;
            if (!finitionsAll.Any())
            {
                try
                {
                    var facCol = MongoDbHelper.GetCollection<BsonDocument>("faconnageOptions");
                    finitionsAll = facCol.Find(FilterDefinition<BsonDocument>.Empty).ToList()
                        .Select(d => d.Contains("label") ? d["label"].AsString : "")
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                }
                catch { /* ignore */ }
            }
            // Remove "Sortie" from client-facing finitions list
            var finitions = finitionsAll.Where(f => !string.Equals(f, "Sortie", StringComparison.OrdinalIgnoreCase)).ToList();

            return Results.Json(new
            {
                ok = true,
                formats = settings.AvailableFormats,
                papers = settings.AvailablePapers,
                finitions,
                typesTravail,
                maxUploadSizeMb = settings.MaxUploadSizeMb,
                maxFilesPerOrder = settings.MaxFilesPerOrder,
                welcomeText = settings.WelcomeText
            });
        });

        // GET /api/portal/medias  — shared paper/media list (same source as fabrication form)
        app.MapGet("/api/portal/medias", () =>
        {
            // Try to load from the same Paper Catalog XML used by the fabrication form
            try
            {
                var searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "Paper Catalog.xml"),
                    Path.Combine(Directory.GetCurrentDirectory(), "data", "Paper Catalog.xml"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Paper Catalog.xml"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Paper Catalog.xml"),
                    Path.Combine(BackendUtils.HotfoldersRoot(), "..", "Paper Catalog.xml"),
                    "Paper Catalog.xml"
                };
                var xmlPath = searchPaths.FirstOrDefault(p => File.Exists(p));
                if (xmlPath != null)
                {
                    var xmlSettings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                    System.Xml.Linq.XDocument xdoc;
                    using (var xmlReader = System.Xml.XmlReader.Create(xmlPath, xmlSettings))
                        xdoc = System.Xml.Linq.XDocument.Load(xmlReader);

                    var names = xdoc.Descendants()
                        .Where(el => el.Name.LocalName == "Media")
                        .Select(el => (string?)(el.Attribute("DescriptiveName") ?? el.Attribute("descriptiveName")))
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .Distinct().OrderBy(n => n).ToList();

                    if (!names.Any())
                        names = xdoc.Descendants()
                            .Where(el => el.Name.LocalName is "CatalogEntry" or "Paper" or "Entry")
                            .Select(el => (string?)(el.Attribute("Name") ?? el.Attribute("name") ?? el.Attribute("mediaName") ?? el.Attribute("MediaName")))
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Select(n => n!)
                            .Distinct().OrderBy(n => n).ToList();

                    if (names.Any())
                        return Results.Json(new { ok = true, medias = names });
                }
            }
            catch { /* fall through to portal settings */ }

            // Fall back to portal settings AvailablePapers
            var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
            return Results.Json(new { ok = true, medias = settings.AvailablePapers });
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

        // Extended fields — stored only when not null/empty
        if (!string.IsNullOrWhiteSpace(o.TypeTravail)) doc["typeTravail"] = o.TypeTravail;
        if (o.Pagination.HasValue) doc["pagination"] = o.Pagination.Value;
        if (!string.IsNullOrWhiteSpace(o.Encres)) doc["encres"] = o.Encres;
        if (!string.IsNullOrWhiteSpace(o.FormatFeuille)) doc["formatFeuille"] = o.FormatFeuille;
        if (!string.IsNullOrWhiteSpace(o.FormeDecoupe)) doc["formeDecoupe"] = o.FormeDecoupe;
        if (!string.IsNullOrWhiteSpace(o.FaconnageBinding)) doc["faconnageBinding"] = o.FaconnageBinding;
        if (!string.IsNullOrWhiteSpace(o.NumeroDossier)) doc["numeroDossier"] = o.NumeroDossier;
        if (!string.IsNullOrWhiteSpace(o.NumeroAffaire)) doc["numeroAffaire"] = o.NumeroAffaire;
        if (!string.IsNullOrWhiteSpace(o.Notes)) doc["notes"] = o.Notes;
        if (!string.IsNullOrWhiteSpace(o.Bat)) doc["bat"] = o.Bat;
        if (o.QuantiteJustifs.HasValue) doc["quantiteJustifs"] = o.QuantiteJustifs.Value;
        if (!string.IsNullOrWhiteSpace(o.AdresseJustifs)) doc["adresseJustifs"] = o.AdresseJustifs;
        if (!string.IsNullOrWhiteSpace(o.Media1)) doc["media1"] = o.Media1;
        if (!string.IsNullOrWhiteSpace(o.Media2)) doc["media2"] = o.Media2;
        if (!string.IsNullOrWhiteSpace(o.Media3)) doc["media3"] = o.Media3;
        if (!string.IsNullOrWhiteSpace(o.Media4)) doc["media4"] = o.Media4;
        if (!string.IsNullOrWhiteSpace(o.MediaCouverture)) doc["mediaCouverture"] = o.MediaCouverture;
        if (!string.IsNullOrWhiteSpace(o.FormatFini)) doc["formatFini"] = o.FormatFini;
        if (!string.IsNullOrWhiteSpace(o.DonneurOrdreNom)) doc["donneurOrdreNom"] = o.DonneurOrdreNom;
        if (!string.IsNullOrWhiteSpace(o.DonneurOrdrePrenom)) doc["donneurOrdrePrenom"] = o.DonneurOrdrePrenom;
        if (!string.IsNullOrWhiteSpace(o.DonneurOrdreTelephone)) doc["donneurOrdreTelephone"] = o.DonneurOrdreTelephone;
        if (!string.IsNullOrWhiteSpace(o.DonneurOrdreEmail)) doc["donneurOrdreEmail"] = o.DonneurOrdreEmail;
        if (!string.IsNullOrWhiteSpace(o.DonneurOrdreSociete)) doc["donneurOrdreSociete"] = o.DonneurOrdreSociete;
        if (o.Rainage.HasValue) doc["rainage"] = o.Rainage.Value;
        if (o.VernisSelectif.HasValue) doc["vernisSelectif"] = o.VernisSelectif.Value;
        if (!string.IsNullOrWhiteSpace(o.DorureAChaud)) doc["dorureAChaud"] = o.DorureAChaud;
        if (o.Pelliculage.Count > 0) doc["pelliculage"] = new BsonArray(o.Pelliculage.Select(p => (BsonValue)p));
        if (!string.IsNullOrWhiteSpace(o.Plis)) doc["plis"] = o.Plis;
        if (o.DeliveryPoints.Count > 0)
        {
            doc["deliveryPoints"] = new BsonArray(o.DeliveryPoints.Select(pt => new BsonDocument
            {
                ["id"] = pt.Id,
                ["address"] = pt.Address,
                ["quantity"] = pt.Quantity,
                ["notes"] = pt.Notes != null ? (BsonValue)pt.Notes : BsonNull.Value
            }));
        }
        if (o.DateEnvoi.HasValue) doc["dateEnvoi"] = o.DateEnvoi.Value;
        if (o.DateImpression.HasValue) doc["dateImpression"] = o.DateImpression.Value;
        if (o.DateProductionFinitions.HasValue) doc["dateProductionFinitions"] = o.DateProductionFinitions.Value;

        return doc;
    }

    // ── File-system name sanitizer ────────────────────────────────────────────
    private static string SanitizeForFs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "sans-titre";
        var safe = System.Text.RegularExpressions.Regex.Replace(s, @"[^\w\-]", "_");
        if (safe.Length > 40) safe = safe[..40];
        return string.IsNullOrWhiteSpace(safe) ? "sans-titre" : safe;
    }
}
