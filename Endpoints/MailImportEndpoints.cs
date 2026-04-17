using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GestionAtelier.Services;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace GestionAtelier.Endpoints;

public static class MailImportEndpoints
{
    public static void MapMailImportEndpoints(this WebApplication app)
    {
        // POST /api/submission/list-mail-attachments
        // Body: { host, port, email, password, useSsl }
        app.MapPost("/api/submission/list-mail-attachments", async (HttpContext ctx) =>
        {
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<MailSearchRequest>();
                if (body == null || string.IsNullOrWhiteSpace(body.Host) || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
                    return Results.Json(new { ok = false, error = "Paramètres IMAP incomplets" });

                var results = new List<MailAttachmentInfo>();

                using var client = new ImapClient();
                await client.ConnectAsync(body.Host, body.Port > 0 ? body.Port : (body.UseSsl ? 993 : 143), body.UseSsl, ctx.RequestAborted);
                await client.AuthenticateAsync(body.Email, body.Password, ctx.RequestAborted);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ctx.RequestAborted);

                // Search mails from the last 48 hours
                var since = DateTime.UtcNow.AddHours(-48);
                var uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(since), ctx.RequestAborted);

                // Limit to last 50 messages
                var recentUids = uids.Reverse().Take(50).ToList();

                foreach (var uid in recentUids)
                {
                    var msg = await inbox.GetMessageAsync(uid, ctx.RequestAborted);

                    foreach (var part in msg.BodyParts.OfType<MimePart>())
                    {
                        if (string.IsNullOrWhiteSpace(part.FileName)) continue;
                        if (!part.FileName.ToLowerInvariant().EndsWith(".pdf")) continue;

                        results.Add(new MailAttachmentInfo
                        {
                            MessageId = uid.ToString(),
                            Subject = msg.Subject ?? "(Sans objet)",
                            From = msg.From?.ToString() ?? "",
                            Date = msg.Date.UtcDateTime,
                            AttachmentName = part.FileName,
                            ContentId = part.ContentId ?? Guid.NewGuid().ToString("N")
                        });
                    }
                }

                await client.DisconnectAsync(true, ctx.RequestAborted);

                return Results.Json(new { ok = true, attachments = results });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });

        // POST /api/submission/import-mail-attachment
        // Body: { host, port, email, password, useSsl, messageId, attachmentName, destinationFolder }
        app.MapPost("/api/submission/import-mail-attachment", async (HttpContext ctx) =>
        {
            try
            {
                var body = await ctx.Request.ReadFromJsonAsync<MailImportRequest>();
                if (body == null || string.IsNullOrWhiteSpace(body.Host) || string.IsNullOrWhiteSpace(body.Email)
                    || string.IsNullOrWhiteSpace(body.Password) || string.IsNullOrWhiteSpace(body.MessageId)
                    || string.IsNullOrWhiteSpace(body.AttachmentName))
                    return Results.Json(new { ok = false, error = "Paramètres incomplets" });

                var targetFolder = string.IsNullOrWhiteSpace(body.DestinationFolder) ? "Soumission" : body.DestinationFolder;

                // Find the physical folder for the destination
                var root = BackendUtils.HotfoldersRoot();
                if (string.IsNullOrWhiteSpace(root))
                    return Results.Json(new { ok = false, error = "Répertoire de travail non configuré" });

                var destPath = Path.Combine(root, targetFolder);
                if (!Directory.Exists(destPath))
                    Directory.CreateDirectory(destPath);

                byte[]? pdfBytes = null;
                string? savedName = null;

                using var client = new ImapClient();
                await client.ConnectAsync(body.Host, body.Port > 0 ? body.Port : (body.UseSsl ? 993 : 143), body.UseSsl, ctx.RequestAborted);
                await client.AuthenticateAsync(body.Email, body.Password, ctx.RequestAborted);

                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ctx.RequestAborted);

                if (!UniqueId.TryParse(body.MessageId, out var uid))
                    return Results.Json(new { ok = false, error = "MessageId invalide" });

                var msg = await inbox.GetMessageAsync(uid, ctx.RequestAborted);

                foreach (var part in msg.BodyParts.OfType<MimePart>())
                {
                    if (string.IsNullOrWhiteSpace(part.FileName)) continue;
                    if (!part.FileName.Equals(body.AttachmentName, StringComparison.OrdinalIgnoreCase)) continue;

                    using var ms = new MemoryStream();
                    await part.Content.DecodeToAsync(ms, ctx.RequestAborted);
                    pdfBytes = ms.ToArray();
                    savedName = part.FileName;
                    break;
                }

                await client.DisconnectAsync(true, ctx.RequestAborted);

                if (pdfBytes == null || savedName == null)
                    return Results.Json(new { ok = false, error = "Pièce jointe introuvable" });

                // Save file (avoid name collisions, add number prefix)
                var safeFileName = Path.GetFileName(savedName);
                long numero = MongoDbHelper.GetNextFileNumber();
                safeFileName = $"{numero:D5}_{safeFileName}";
                var destFilePath = Path.Combine(destPath, safeFileName);

                await File.WriteAllBytesAsync(destFilePath, pdfBytes, ctx.RequestAborted);

                return Results.Json(new { ok = true, fileName = Path.GetFileName(destFilePath), path = destFilePath });
            }
            catch (Exception ex)
            {
                return Results.Json(new { ok = false, error = ex.Message });
            }
        });
    }
}

public class MailSearchRequest
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseSsl { get; set; } = true;
}

public class MailImportRequest : MailSearchRequest
{
    public string MessageId { get; set; } = "";
    public string AttachmentName { get; set; } = "";
    public string DestinationFolder { get; set; } = "Soumission";
}

public class MailAttachmentInfo
{
    public string MessageId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public DateTime Date { get; set; }
    public string AttachmentName { get; set; } = "";
    public string ContentId { get; set; } = "";
}
