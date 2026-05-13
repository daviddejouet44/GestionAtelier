using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class PortalEmailHelper
{
    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var at = email.IndexOf('@');
        if (at <= 1) return "***@" + (at >= 0 ? email[(at + 1)..] : "***");
        return email[0] + new string('*', at - 1) + email[at..];
    }

    /// <summary>Sends an email (HTML with plain-text fallback) using SMTP settings stored in MongoDB.</summary>
    public static void SendEmail(string toAddress, string subject, string body)
    {
        var smtp = MongoDbHelper.GetSettings<PortalSmtpSettings>("portalSmtp");
        if (smtp == null || string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            Console.WriteLine($"[WARN] Portal SMTP not configured — email not sent to {MaskEmail(toAddress)}");
            return;
        }

        try
        {
            Console.WriteLine($"[EMAIL] Sending to {MaskEmail(toAddress)} via {smtp.Host}:{(smtp.Port > 0 ? smtp.Port : 587)} (SSL={smtp.UseSsl})");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtp.FromName ?? "Portail Client", smtp.FromAddress));
            message.To.Add(MailboxAddress.Parse(toAddress));
            message.Subject = subject;

            // Build safe HTML: encode body then convert newlines to <br/>
            var encodedBody = System.Net.WebUtility.HtmlEncode(body).Replace("&#10;", "<br/>").Replace("\n", "<br/>");
            var multipart = new MimeKit.Multipart("alternative");
            multipart.Add(new TextPart("plain") { Text = body });
            multipart.Add(new TextPart("html") { Text = encodedBody });
            message.Body = multipart;

            using var client = new SmtpClient();
            var port = smtp.Port > 0 ? smtp.Port : 587;
            // Port 465 = SSL direct, Port 587 = STARTTLS, other = auto-detect
            var secureOption = port == 465 ? SecureSocketOptions.SslOnConnect
                             : port == 587 ? SecureSocketOptions.StartTls
                             : (smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
            client.Connect(smtp.Host, port, secureOption);
            if (!string.IsNullOrWhiteSpace(smtp.Username))
                client.Authenticate(smtp.Username, smtp.Password ?? "");
            client.Send(message);
            client.Disconnect(true);

            Console.WriteLine($"[EMAIL] Successfully sent to {MaskEmail(toAddress)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Email send failed to {MaskEmail(toAddress)}: {ex.Message}");
        }
    }

    /// <summary>Sends an email to the atelier notification address.</summary>
    public static void SendAtelierNotification(string subject, string body)
    {
        var smtp = MongoDbHelper.GetSettings<PortalSmtpSettings>("portalSmtp");
        if (smtp == null || string.IsNullOrWhiteSpace(smtp.AtelierNotifyEmail)) return;
        SendEmail(smtp.AtelierNotifyEmail, subject, body);
    }

    /// <summary>
    /// Sanitizes a portal base URL stored in settings.
    /// Strips any trailing .html page path (e.g. if the operator saved the login page URL instead of the base URL).
    /// Input:  "https://portail.example.com/portal/login.html"
    /// Output: "https://portail.example.com"
    /// </summary>
    public static string SanitizePortalBaseUrl(string? rawUrl)
    {
        var trimmed = (rawUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed)) return trimmed;
        // Strip any trailing .html file path — common misconfiguration where the operator pastes
        // the login page URL into the PortalUrl setting instead of the bare origin.
        if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(trimmed);
                // Return just scheme://host[:port] (strip all path components)
                return uri.GetLeftPart(UriPartial.Authority);
            }
            catch
            {
                // Fallback: strip the last /xxx.html segment
                var lastSlash = trimmed.LastIndexOf('/');
                if (lastSlash > 0)
                    trimmed = trimmed.Substring(0, lastSlash).TrimEnd('/');
            }
        }
        return trimmed;
    }

    /// <summary>Resolves and renders a portal email template, replacing variables.</summary>
    public static (string subject, string body) RenderTemplate(
        string templateKey,
        string defaultSubject,
        string defaultBody,
        Dictionary<string, string> vars)
    {
        var tpl = MongoDbHelper.GetSettings<PortalEmailTemplate>($"portalEmailTemplate_{templateKey}");
        var subject = tpl?.Subject ?? defaultSubject;
        var body = tpl?.Body ?? defaultBody;

        foreach (var kv in vars)
        {
            subject = subject.Replace(kv.Key, kv.Value);
            body = body.Replace(kv.Key, kv.Value);
        }
        return (subject, body);
    }
}
