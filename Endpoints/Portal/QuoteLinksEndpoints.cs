using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MongoDB.Driver;
using GestionAtelier.Models;
using GestionAtelier.Services;

namespace GestionAtelier.Endpoints.Portal;

/// <summary>
/// Quote-link bridge: staff sends a formatted email to a client with a unique URL.
/// The client clicks the link, reviews the quote recap, uploads their production PDF,
/// confirms and submits — without needing a portal account.
///
/// Staff endpoints  (Bearer staff token required):
///   POST   /api/pro/quotes/send          — create link + send email
///   GET    /api/pro/quotes               — list all quote links
///   DELETE /api/pro/quotes/{id}          — revoke a link
///
/// Public endpoints (token in query string):
///   GET    /api/portal/quote             — fetch quote data (?token=)
///   GET    /api/portal/quote/pdf         — download the ERP quote PDF (?token=)
///   POST   /api/portal/quote/submit      — upload production file + create order (?token=)
/// </summary>
public static class QuoteLinksEndpoints
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsStaffAuth(HttpContext ctx, out string login)
    {
        login = "";
        try
        {
            var raw = ctx.Request.Headers["Authorization"].ToString().Replace("Bearer ", "").Trim();
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var parts = decoded.Split(':');
            if (parts.Length < 3) return false;
            var profile = parts[2];
            if (profile != "2" && profile != "3") return false;
            login = parts.Length >= 2 ? parts[1] : "";
            return true;
        }
        catch { return false; }
    }

    private static QuoteLink? TokenToLink(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var col = MongoDbHelper.GetCollection<BsonDocument>("quote_links");
        var doc = col.Find(Builders<BsonDocument>.Filter.Eq("token", token)).FirstOrDefault();
        return doc == null ? null : DocToLink(doc);
    }

    private static QuoteLink DocToLink(BsonDocument d)
    {
        var finitions = new List<string>();
        if (d.Contains("finitions") && d["finitions"].IsBsonArray)
            finitions = d["finitions"].AsBsonArray.Select(v => v.AsString).ToList();

        return new QuoteLink
        {
            Id = d.Contains("id") ? d["id"].AsString : "",
            Token = d.Contains("token") ? d["token"].AsString : "",
            DevisNumber = d.Contains("devisNumber") ? d["devisNumber"].AsString : "",
            ClientName = d.Contains("clientName") ? d["clientName"].AsString : "",
            ClientEmail = d.Contains("clientEmail") ? d["clientEmail"].AsString : "",
            Title = d.Contains("title") ? d["title"].AsString : "",
            Format = d.Contains("format") ? d["format"].AsString : "",
            Paper = d.Contains("paper") ? d["paper"].AsString : "",
            Encres = d.Contains("encres") ? d["encres"].AsString : "",
            Quantity = d.Contains("quantity") ? d["quantity"].AsInt32 : 0,
            Finitions = finitions,
            Pagination = d.Contains("pagination") && !d["pagination"].IsBsonNull ? (int?)d["pagination"].AsInt32 : null,
            Recto = d.Contains("recto") ? d["recto"].AsString : "recto",
            Notes = d.Contains("notes") && !d["notes"].IsBsonNull ? d["notes"].AsString : null,
            QuotePdfFileName = d.Contains("quotePdfFileName") && !d["quotePdfFileName"].IsBsonNull ? d["quotePdfFileName"].AsString : null,
            QuotePdfStoredPath = d.Contains("quotePdfStoredPath") && !d["quotePdfStoredPath"].IsBsonNull ? d["quotePdfStoredPath"].AsString : null,
            FichePath = d.Contains("fichePath") && !d["fichePath"].IsBsonNull ? d["fichePath"].AsString : null,
            Status = d.Contains("status") ? d["status"].AsString : "pending",
            CreatedAt = d.Contains("createdAt") ? d["createdAt"].ToUniversalTime() : DateTime.UtcNow,
            ExpiresAt = d.Contains("expiresAt") && !d["expiresAt"].IsBsonNull ? (DateTime?)d["expiresAt"].ToUniversalTime() : null,
            UsedAt = d.Contains("usedAt") && !d["usedAt"].IsBsonNull ? (DateTime?)d["usedAt"].ToUniversalTime() : null,
            ResultOrderId = d.Contains("resultOrderId") && !d["resultOrderId"].IsBsonNull ? d["resultOrderId"].AsString : null,
            ResultOrderNumber = d.Contains("resultOrderNumber") && !d["resultOrderNumber"].IsBsonNull ? d["resultOrderNumber"].AsString : null,
            CreatedByLogin = d.Contains("createdByLogin") && !d["createdByLogin"].IsBsonNull ? d["createdByLogin"].AsString : null,
        };
    }

    private static BsonDocument LinkToDoc(QuoteLink l)
    {
        var doc = new BsonDocument
        {
            ["id"] = l.Id,
            ["token"] = l.Token,
            ["devisNumber"] = l.DevisNumber,
            ["clientName"] = l.ClientName,
            ["clientEmail"] = l.ClientEmail,
            ["title"] = l.Title,
            ["format"] = l.Format,
            ["paper"] = l.Paper,
            ["encres"] = l.Encres,
            ["quantity"] = l.Quantity,
            ["finitions"] = new BsonArray(l.Finitions.Select(f => (BsonValue)f)),
            ["recto"] = l.Recto,
            ["status"] = l.Status,
            ["createdAt"] = l.CreatedAt,
        };
        if (l.Pagination.HasValue) doc["pagination"] = l.Pagination.Value; else doc["pagination"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.Notes)) doc["notes"] = l.Notes; else doc["notes"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.QuotePdfFileName)) doc["quotePdfFileName"] = l.QuotePdfFileName; else doc["quotePdfFileName"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.QuotePdfStoredPath)) doc["quotePdfStoredPath"] = l.QuotePdfStoredPath; else doc["quotePdfStoredPath"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.FichePath)) doc["fichePath"] = l.FichePath; else doc["fichePath"] = BsonNull.Value;
        if (l.ExpiresAt.HasValue) doc["expiresAt"] = l.ExpiresAt.Value; else doc["expiresAt"] = BsonNull.Value;
        if (l.UsedAt.HasValue) doc["usedAt"] = l.UsedAt.Value; else doc["usedAt"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.ResultOrderId)) doc["resultOrderId"] = l.ResultOrderId; else doc["resultOrderId"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.ResultOrderNumber)) doc["resultOrderNumber"] = l.ResultOrderNumber; else doc["resultOrderNumber"] = BsonNull.Value;
        if (!string.IsNullOrWhiteSpace(l.CreatedByLogin)) doc["createdByLogin"] = l.CreatedByLogin; else doc["createdByLogin"] = BsonNull.Value;
        return doc;
    }

    private static string SanitizeForFs(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "fichier";
        var safe = System.Text.RegularExpressions.Regex.Replace(s, @"[^\w\.\-]", "_");
        return safe.Length > 60 ? safe[..60] : safe;
    }

    // ── Email builder ─────────────────────────────────────────────────────────

    private static string BuildQuoteEmailHtml(QuoteLink link, string quoteUrl, string companyName)
    {
        static string Row(string label, string value) =>
            $"<tr><td style=\"padding:6px 12px;color:#6b7280;font-size:13px;white-space:nowrap;\">{System.Net.WebUtility.HtmlEncode(label)}&nbsp;:</td>" +
            $"<td style=\"padding:6px 12px;font-size:13px;font-weight:600;color:#111827;\">{System.Net.WebUtility.HtmlEncode(value)}</td></tr>";

        var finitionsText = link.Finitions.Count > 0 ? string.Join(", ", link.Finitions) : "";
        var rows = new StringBuilder();
        rows.Append(Row("Client", link.ClientName));
        if (!string.IsNullOrWhiteSpace(link.Title)) rows.Append(Row("Produit", link.Title));
        if (!string.IsNullOrWhiteSpace(link.Format)) rows.Append(Row("Format", link.Format));
        if (!string.IsNullOrWhiteSpace(link.Paper)) rows.Append(Row("Support", link.Paper));
        if (!string.IsNullOrWhiteSpace(link.Encres)) rows.Append(Row("Couleurs", link.Encres));
        if (link.Quantity > 0) rows.Append(Row("Quantité", link.Quantity.ToString("N0") + " ex."));
        if (link.Pagination.HasValue && link.Pagination.Value > 0) rows.Append(Row("Pagination", link.Pagination.Value + " pages"));
        if (!string.IsNullOrWhiteSpace(link.Recto) && link.Recto != "recto") rows.Append(Row("Impression", link.Recto == "recto-verso" ? "Recto / Verso" : link.Recto));
        if (!string.IsNullOrWhiteSpace(finitionsText)) rows.Append(Row("Finitions", finitionsText));
        if (!string.IsNullOrWhiteSpace(link.Notes)) rows.Append(Row("Remarques", link.Notes));

        var hasPdf = !string.IsNullOrWhiteSpace(link.QuotePdfFileName);
        var pdfLine = hasPdf
            ? $"<p style=\"font-size:13px;color:#374151;margin:0 0 20px;\">📎 Le PDF de votre devis est joint à cet email.</p>"
            : "";

        return $@"<!DOCTYPE html>
<html lang=""fr"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Devis {System.Net.WebUtility.HtmlEncode(link.DevisNumber)}</title></head>
<body style=""margin:0;padding:0;background:#f3f4f6;font-family:system-ui,-apple-system,sans-serif;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f3f4f6;padding:32px 16px;"">
<tr><td align=""center"">
  <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0"" style=""background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 1px 6px rgba(0,0,0,.08);"">

    <!-- Header -->
    <tr><td style=""background:#1d4ed8;padding:28px 32px;"">
      <p style=""margin:0;color:rgba(255,255,255,.7);font-size:12px;letter-spacing:.08em;text-transform:uppercase;"">COMMANDE</p>
      <h1 style=""margin:6px 0 0;color:#fff;font-size:22px;font-weight:700;"">Devis {System.Net.WebUtility.HtmlEncode(link.DevisNumber)}</h1>
      <p style=""margin:4px 0 0;color:rgba(255,255,255,.8);font-size:13px;"">{System.Net.WebUtility.HtmlEncode(companyName)}</p>
    </td></tr>

    <!-- Body -->
    <tr><td style=""padding:28px 32px;"">
      <p style=""margin:0 0 20px;font-size:15px;color:#374151;"">Bonjour,</p>
      <p style=""margin:0 0 24px;font-size:14px;color:#374151;"">Veuillez trouver ci-dessous le récapitulatif de votre devis. Pour confirmer votre commande et transmettre votre fichier de production, cliquez sur le bouton ci-dessous.</p>

      <!-- Recap table -->
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0""
             style=""background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;margin:0 0 24px;border-collapse:collapse;"">
        <thead><tr><td colspan=""2"" style=""padding:10px 12px;background:#f3f4f6;border-bottom:1px solid #e5e7eb;font-size:12px;font-weight:600;color:#6b7280;letter-spacing:.05em;text-transform:uppercase;"">Récapitulatif du devis</td></tr></thead>
        <tbody>{rows}</tbody>
      </table>

      {pdfLine}

      <!-- Upload zone CTA -->
      <div style=""background:#eff6ff;border:2px dashed #93c5fd;border-radius:10px;padding:24px;text-align:center;margin:0 0 24px;"">
        <p style=""margin:0 0 8px;font-size:14px;color:#1e40af;font-weight:600;"">📂 Transmettez votre fichier de production</p>
        <p style=""margin:0 0 16px;font-size:13px;color:#3b82f6;"">Glissez votre PDF ici ou cliquez pour sélectionner</p>
        <a href=""{System.Net.WebUtility.HtmlEncode(quoteUrl)}""
           style=""display:inline-block;background:#1d4ed8;color:#fff;text-decoration:none;font-weight:600;font-size:14px;padding:12px 28px;border-radius:8px;"">
          Accéder au formulaire de commande →
        </a>
      </div>

      <p style=""font-size:12px;color:#9ca3af;margin:0;"">Ce lien est personnel et valable 30 jours. En cas de problème, contactez-nous.</p>
    </td></tr>

    <!-- Footer -->
    <tr><td style=""background:#f9fafb;border-top:1px solid #e5e7eb;padding:16px 32px;text-align:center;"">
      <p style=""margin:0;font-size:12px;color:#9ca3af;"">{System.Net.WebUtility.HtmlEncode(companyName)}</p>
    </td></tr>

  </table>
</td></tr>
</table>
</body></html>";
    }

    private static string BuildQuoteEmailPlainText(QuoteLink link, string quoteUrl, string companyName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"COMMANDE — Devis {link.DevisNumber}");
        sb.AppendLine(new string('─', 50));
        sb.AppendLine();
        sb.AppendLine("Récapitulatif de votre devis");
        sb.AppendLine(new string('─', 30));
        sb.AppendLine($"Client    : {link.ClientName}");
        if (!string.IsNullOrWhiteSpace(link.Title)) sb.AppendLine($"Produit   : {link.Title}");
        if (!string.IsNullOrWhiteSpace(link.Format)) sb.AppendLine($"Format    : {link.Format}");
        if (!string.IsNullOrWhiteSpace(link.Paper)) sb.AppendLine($"Support   : {link.Paper}");
        if (!string.IsNullOrWhiteSpace(link.Encres)) sb.AppendLine($"Couleurs  : {link.Encres}");
        if (link.Quantity > 0) sb.AppendLine($"Quantité  : {link.Quantity:N0} ex.");
        if (link.Pagination.HasValue) sb.AppendLine($"Pagination: {link.Pagination.Value} pages");
        if (link.Finitions.Count > 0) sb.AppendLine($"Finitions : {string.Join(", ", link.Finitions)}");
        if (!string.IsNullOrWhiteSpace(link.Notes)) sb.AppendLine($"Remarques : {link.Notes}");
        sb.AppendLine();
        sb.AppendLine("── Transmettez votre fichier de production ──");
        sb.AppendLine(quoteUrl);
        sb.AppendLine();
        sb.AppendLine(companyName);
        return sb.ToString();
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public static void MapQuoteLinksEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        // ── STAFF: POST /api/pro/quotes/send ─────────────────────────────────
        // Multipart form: quote fields + optional quotePdf file
        app.MapPost("/api/pro/quotes/send", async (HttpContext ctx) =>
        {
            try
            {
                if (!IsStaffAuth(ctx, out var login))
                    return Results.Json(new { ok = false, error = "Non autorisé" });

                var form = await ctx.Request.ReadFormAsync();

                var devisNumber = form["devisNumber"].ToString().Trim();
                if (string.IsNullOrWhiteSpace(devisNumber))
                    return Results.Json(new { ok = false, error = "Le numéro de devis est obligatoire" });

                var clientEmail = form["clientEmail"].ToString().Trim();
                if (string.IsNullOrWhiteSpace(clientEmail))
                    return Results.Json(new { ok = false, error = "L'adresse email du client est obligatoire" });

                var clientName = form["clientName"].ToString().Trim();
                var title = form["title"].ToString().Trim();
                var format = form["format"].ToString().Trim();
                var paper = form["paper"].ToString().Trim();
                var encres = form["encres"].ToString().Trim();
                int.TryParse(form["quantity"].ToString(), out var quantity);
                var finitionsRaw = form["finitions"].ToString().Trim();
                var finitions = finitionsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                int? pagination = null;
                if (int.TryParse(form["pagination"].ToString(), out var pgI) && pgI > 0) pagination = pgI;
                var recto = form["recto"].ToString().Trim();
                if (string.IsNullOrWhiteSpace(recto)) recto = "recto";
                var notes = form["notes"].ToString().Trim();
                var fichePath = form["fichePath"].ToString().Trim();

                // Save quote PDF if provided
                string? pdfFileName = null;
                string? pdfStoredPath = null;
                var pdfFile = form.Files.GetFile("quotePdf");
                if (pdfFile != null && pdfFile.Length > 0)
                {
                    var ext = Path.GetExtension(pdfFile.FileName).ToLowerInvariant();
                    if (ext != ".pdf")
                        return Results.Json(new { ok = false, error = "Le fichier devis doit être un PDF" });

                    // Validate magic bytes
                    byte[] header = new byte[4];
                    using (var stream = pdfFile.OpenReadStream())
                    {
                        int read = await stream.ReadAsync(header, 0, 4);
                        if (read < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46)
                            return Results.Json(new { ok = false, error = "Le fichier PDF du devis est invalide" });
                    }

                    var quotePdfDir = Path.Combine(BackendUtils.HotfoldersRoot(), "quote_pdfs");
                    Directory.CreateDirectory(quotePdfDir);
                    var safeOrigName = SanitizeForFs(Path.GetFileNameWithoutExtension(pdfFile.FileName)) + ".pdf";
                    var num = MongoDbHelper.GetNextFileNumber();
                    pdfFileName = $"{num:D5}_{safeOrigName}";
                    pdfStoredPath = Path.Combine(quotePdfDir, pdfFileName);
                    using (var fs = File.Create(pdfStoredPath))
                        await pdfFile.CopyToAsync(fs);
                }

                // Generate unique token
                var linkId = Guid.NewGuid().ToString("N");
                var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

                var link = new QuoteLink
                {
                    Id = linkId,
                    Token = token,
                    DevisNumber = devisNumber,
                    ClientName = clientName,
                    ClientEmail = clientEmail,
                    Title = title,
                    Format = format,
                    Paper = paper,
                    Encres = encres,
                    Quantity = quantity,
                    Finitions = finitions,
                    Pagination = pagination,
                    Recto = recto,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    QuotePdfFileName = pdfFileName,
                    QuotePdfStoredPath = pdfStoredPath,
                    FichePath = string.IsNullOrWhiteSpace(fichePath) ? null : fichePath,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                    CreatedByLogin = login
                };

                var col = MongoDbHelper.GetCollection<BsonDocument>("quote_links");
                col.InsertOne(LinkToDoc(link));

                // Build quote URL
                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                var portalBase = PortalEmailHelper.SanitizePortalBaseUrl(settings.PortalUrl);
                // Fallback: use request origin if no portal URL configured
                if (string.IsNullOrWhiteSpace(portalBase))
                    portalBase = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                var quoteUrl = $"{portalBase}/portal/quote.html?token={token}";

                // Build and send email
                var theme = MongoDbHelper.GetSettings<PortalThemeConfig>("portalTheme") ?? new PortalThemeConfig();
                var companyName = string.IsNullOrWhiteSpace(theme.CompanyName) ? "Gestion d'Atelier" : theme.CompanyName;

                var subject = $"COMMANDE — Devis {devisNumber}";
                var htmlBody = BuildQuoteEmailHtml(link, quoteUrl, companyName);
                var plainBody = BuildQuoteEmailPlainText(link, quoteUrl, companyName);

                PortalEmailHelper.SendHtmlEmail(
                    clientEmail,
                    subject,
                    htmlBody,
                    plainBody,
                    pdfStoredPath,
                    pdfFileName);

                return Results.Json(new { ok = true, quoteLinkId = linkId, quoteUrl, token });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/pro/quotes/send: {ex.Message}");
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // ── STAFF: GET /api/pro/quotes ────────────────────────────────────────
        app.MapGet("/api/pro/quotes", (HttpContext ctx) =>
        {
            if (!IsStaffAuth(ctx, out _))
                return Results.Json(new { ok = false, error = "Non autorisé" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("quote_links");
            var docs = col.Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
                .Limit(200)
                .ToList();

            var links = docs.Select(d =>
            {
                var l = DocToLink(d);
                return new
                {
                    id = l.Id,
                    devisNumber = l.DevisNumber,
                    clientName = l.ClientName,
                    clientEmail = l.ClientEmail,
                    title = l.Title,
                    status = l.Status,
                    createdAt = l.CreatedAt,
                    expiresAt = l.ExpiresAt,
                    usedAt = l.UsedAt,
                    resultOrderNumber = l.ResultOrderNumber,
                    createdByLogin = l.CreatedByLogin,
                    hasQuotePdf = !string.IsNullOrWhiteSpace(l.QuotePdfFileName)
                };
            }).ToList();

            return Results.Json(new { ok = true, links });
        });

        // ── STAFF: DELETE /api/pro/quotes/{id} ───────────────────────────────
        app.MapDelete("/api/pro/quotes/{id}", (HttpContext ctx, string id) =>
        {
            if (!IsStaffAuth(ctx, out _))
                return Results.Json(new { ok = false, error = "Non autorisé" });

            var col = MongoDbHelper.GetCollection<BsonDocument>("quote_links");
            var doc = col.Find(Builders<BsonDocument>.Filter.Eq("id", id)).FirstOrDefault();
            if (doc == null) return Results.Json(new { ok = false, error = "Lien non trouvé" });

            col.UpdateOne(
                Builders<BsonDocument>.Filter.Eq("id", id),
                Builders<BsonDocument>.Update.Set("status", "revoked"));

            return Results.Json(new { ok = true });
        });

        // ── PUBLIC: GET /api/portal/quote?token= ─────────────────────────────
        app.MapGet("/api/portal/quote", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            var link = TokenToLink(token);

            if (link == null)
                return Results.Json(new { ok = false, error = "Lien invalide ou introuvable" });

            if (link.Status == "revoked")
                return Results.Json(new { ok = false, error = "Ce lien a été révoqué" });

            if (link.Status == "used")
                return Results.Json(new { ok = true, alreadyUsed = true,
                    resultOrderNumber = link.ResultOrderNumber,
                    devisNumber = link.DevisNumber });

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
                return Results.Json(new { ok = false, error = "Ce lien a expiré" });

            return Results.Json(new
            {
                ok = true,
                alreadyUsed = false,
                id = link.Id,
                devisNumber = link.DevisNumber,
                clientName = link.ClientName,
                title = link.Title,
                format = link.Format,
                paper = link.Paper,
                encres = link.Encres,
                quantity = link.Quantity,
                finitions = link.Finitions,
                pagination = link.Pagination,
                recto = link.Recto,
                notes = link.Notes,
                hasQuotePdf = !string.IsNullOrWhiteSpace(link.QuotePdfFileName),
                expiresAt = link.ExpiresAt
            });
        });

        // ── PUBLIC: GET /api/portal/quote/pdf?token= ─────────────────────────
        app.MapGet("/api/portal/quote/pdf", (HttpContext ctx) =>
        {
            var token = ctx.Request.Query["token"].ToString();
            var link = TokenToLink(token);

            if (link == null || link.Status == "revoked")
                return Results.Json(new { ok = false, error = "Lien invalide" });

            if (string.IsNullOrWhiteSpace(link.QuotePdfStoredPath) || !File.Exists(link.QuotePdfStoredPath))
                return Results.Json(new { ok = false, error = "PDF non disponible" });

            var fileName = link.QuotePdfFileName ?? $"Devis-{link.DevisNumber}.pdf";
            ctx.Response.Headers["Content-Disposition"] = $"inline; filename=\"{SanitizeForFs(fileName)}\"";
            return Results.File(link.QuotePdfStoredPath, "application/pdf");
        });

        // ── PUBLIC: POST /api/portal/quote/submit?token= ─────────────────────
        // Multipart: production PDF file(s) + optional JSON fields (donneurOrdreNom, etc.)
        app.MapPost("/api/portal/quote/submit", async (HttpContext ctx) =>
        {
            try
            {
                var token = ctx.Request.Query["token"].ToString();
                if (string.IsNullOrWhiteSpace(token))
                    return Results.Json(new { ok = false, error = "Token manquant" });

                var col = MongoDbHelper.GetCollection<BsonDocument>("quote_links");
                var doc = col.Find(Builders<BsonDocument>.Filter.Eq("token", token)).FirstOrDefault();
                if (doc == null)
                    return Results.Json(new { ok = false, error = "Lien invalide ou introuvable" });

                var link = DocToLink(doc);

                if (link.Status == "revoked")
                    return Results.Json(new { ok = false, error = "Ce lien a été révoqué" });

                if (link.Status == "used")
                    return Results.Json(new { ok = true, alreadyUsed = true, resultOrderNumber = link.ResultOrderNumber });

                if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
                    return Results.Json(new { ok = false, error = "Ce lien a expiré" });

                var form = await ctx.Request.ReadFormAsync();

                // Optional donneur d'ordre overrides
                var donneurNom = form["donneurNom"].ToString().Trim();
                var donneurPrenom = form["donneurPrenom"].ToString().Trim();
                var donneurEmail = form["donneurEmail"].ToString().Trim();
                var donneurTel = form["donneurTel"].ToString().Trim();
                var clientComments = form["comments"].ToString().Trim();

                var settings = MongoDbHelper.GetSettings<PortalSettings>("portalSettings") ?? new PortalSettings();
                var hotRoot = BackendUtils.HotfoldersRoot();
                var webFolder = Path.Combine(hotRoot, settings.WebOrderKanbanFolder ?? "Commandes web");
                Directory.CreateDirectory(webFolder);

                // Generate order
                var counter = MongoDbHelper.GetNextClientOrderNumber();
                var orderNumber = $"DEVIS-{DateTime.Now:yyyyMMdd}-{counter:D4}";
                var orderId = Guid.NewGuid().ToString("N");
                var now = DateTime.UtcNow;

                // Save uploaded production file(s) directly to kanban folder
                var savedFiles = new List<ClientOrderFile>();
                var titlePart = System.Text.RegularExpressions.Regex.Replace(link.DevisNumber, @"[^\w\-]", "_");

                foreach (var file in form.Files)
                {
                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (ext != ".pdf") continue;

                    // Validate magic bytes
                    byte[] header = new byte[4];
                    using (var stream = file.OpenReadStream())
                    {
                        int read = await stream.ReadAsync(header, 0, 4);
                        if (read < 4 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44 || header[3] != 0x46) continue;
                    }

                    long maxBytes = (settings.MaxUploadSizeMb > 0 ? settings.MaxUploadSizeMb : 500) * 1024L * 1024L;
                    if (file.Length > maxBytes) continue;

                    var num = MongoDbHelper.GetNextFileNumber();
                    var safeName = SanitizeForFs(Path.GetFileNameWithoutExtension(file.FileName)) + ".pdf";
                    var destName = $"{orderNumber}__{titlePart}__{num:D5}_{safeName}";
                    var destPath = Path.Combine(webFolder, destName);

                    using (var fs = File.Create(destPath))
                        await file.CopyToAsync(fs);

                    savedFiles.Add(new ClientOrderFile
                    {
                        FileName = destName,
                        StoredPath = destPath,
                        UploadedAt = now,
                        Size = file.Length
                    });
                }

                var filesArray = new BsonArray(savedFiles.Select(f => new BsonDocument
                {
                    ["fileName"] = f.FileName,
                    ["storedPath"] = f.StoredPath,
                    ["uploadedAt"] = f.UploadedAt,
                    ["size"] = f.Size
                }));

                var historyArray = new BsonArray(new[]
                {
                    new BsonDocument { ["status"] = "submitted", ["timestamp"] = now, ["comment"] = $"Commande soumise via lien devis {link.DevisNumber}" }
                });

                // Build the order document
                var orderDoc = new BsonDocument
                {
                    ["id"] = orderId,
                    ["clientAccountId"] = "",   // no portal account required
                    ["quoteToken"] = token,
                    ["quoteLinkId"] = link.Id,
                    ["orderNumber"] = orderNumber,
                    ["title"] = string.IsNullOrWhiteSpace(link.Title) ? link.DevisNumber : link.Title,
                    ["quantity"] = link.Quantity,
                    ["format"] = link.Format,
                    ["paper"] = link.Paper,
                    ["encres"] = link.Encres,
                    ["recto"] = link.Recto,
                    ["finitions"] = new BsonArray(link.Finitions.Select(f => (BsonValue)f)),
                    ["deliveryMode"] = "retrait",
                    ["deliveryAddress"] = "",
                    ["comments"] = string.IsNullOrWhiteSpace(clientComments) ? "" : clientComments,
                    ["status"] = "submitted",
                    ["atelierJobPath"] = "",
                    ["createdAt"] = now,
                    ["updatedAt"] = now,
                    ["files"] = filesArray,
                    ["statusHistory"] = historyArray,
                    ["donneurOrdreNom"] = string.IsNullOrWhiteSpace(donneurNom) ? (BsonValue)link.ClientName : donneurNom,
                    ["donneurOrdrePrenom"] = string.IsNullOrWhiteSpace(donneurPrenom) ? BsonNull.Value : (BsonValue)donneurPrenom,
                    ["donneurOrdreEmail"] = string.IsNullOrWhiteSpace(donneurEmail) ? (BsonValue)link.ClientEmail : donneurEmail,
                    ["donneurOrdreTelephone"] = string.IsNullOrWhiteSpace(donneurTel) ? BsonNull.Value : (BsonValue)donneurTel,
                    ["donneurOrdreSociete"] = (BsonValue)link.ClientName,
                    ["devisNumber"] = link.DevisNumber,
                };

                if (link.Pagination.HasValue) orderDoc["pagination"] = link.Pagination.Value;
                if (!string.IsNullOrWhiteSpace(link.Notes)) orderDoc["notes"] = link.Notes;

                var orderCol = MongoDbHelper.GetCollection<BsonDocument>("client_orders");
                orderCol.InsertOne(orderDoc);

                // Mark link as used
                col.UpdateOne(
                    Builders<BsonDocument>.Filter.Eq("token", token),
                    Builders<BsonDocument>.Update
                        .Set("status", "used")
                        .Set("usedAt", now)
                        .Set("resultOrderId", orderId)
                        .Set("resultOrderNumber", orderNumber));

                // Notify atelier staff
                try
                {
                    var notifCol = MongoDbHelper.GetCollection<BsonDocument>("notifications");
                    var users = BackendUtils.LoadUsers();
                    foreach (var u in users.Where(u => u.Profile == 2 || u.Profile == 3))
                    {
                        notifCol.InsertOne(new BsonDocument
                        {
                            ["type"] = "new_web_order",
                            ["recipientLogin"] = u.Login,
                            ["message"] = $"📦 Nouvelle commande devis : {orderNumber} — {link.Title} (Client : {link.ClientName}) [Devis {link.DevisNumber}]",
                            ["read"] = false,
                            ["timestamp"] = now
                        });
                    }
                }
                catch { /* non-blocking */ }

                // Send confirmation email to client
                try
                {
                    var portalBase = PortalEmailHelper.SanitizePortalBaseUrl(settings.PortalUrl);
                    if (string.IsNullOrWhiteSpace(portalBase))
                        portalBase = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
                    var theme = MongoDbHelper.GetSettings<PortalThemeConfig>("portalTheme") ?? new PortalThemeConfig();
                    var companyName = string.IsNullOrWhiteSpace(theme.CompanyName) ? "Gestion d'Atelier" : theme.CompanyName;
                    var confirmSubject = $"Commande confirmée — {orderNumber}";
                    var confirmHtml = $@"<!DOCTYPE html><html lang=""fr""><head><meta charset=""utf-8""></head>
<body style=""font-family:system-ui,sans-serif;background:#f3f4f6;padding:32px 16px;"">
<div style=""max-width:520px;margin:0 auto;background:#fff;border-radius:12px;overflow:hidden;"">
  <div style=""background:#16a34a;padding:24px 32px;""><h1 style=""margin:0;color:#fff;font-size:20px;"">✅ Commande confirmée</h1></div>
  <div style=""padding:24px 32px;"">
    <p style=""font-size:14px;color:#374151;"">Votre commande <strong>{System.Net.WebUtility.HtmlEncode(orderNumber)}</strong> liée au devis <strong>{System.Net.WebUtility.HtmlEncode(link.DevisNumber)}</strong> a bien été reçue.</p>
    <p style=""font-size:14px;color:#374151;"">Notre équipe va prendre en charge votre dossier dans les meilleurs délais.</p>
    <p style=""font-size:12px;color:#9ca3af;margin-top:24px;"">{System.Net.WebUtility.HtmlEncode(companyName)}</p>
  </div>
</div>
</body></html>";
                    PortalEmailHelper.SendHtmlEmail(link.ClientEmail, confirmSubject, confirmHtml);
                }
                catch (Exception ex) { Console.WriteLine($"[WARN] Quote confirm email: {ex.Message}"); }

                return Results.Json(new { ok = true, orderNumber, orderId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/portal/quote/submit: {ex.Message}");
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });
    }
}
