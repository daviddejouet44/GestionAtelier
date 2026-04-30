using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public class PortalEmailTemplate
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";
}

public class PortalSmtpSettings
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 587;

    [JsonPropertyName("useSsl")]
    public bool UseSsl { get; set; } = true;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("fromAddress")]
    public string FromAddress { get; set; } = "";

    [JsonPropertyName("fromName")]
    public string FromName { get; set; } = "Portail Client";

    [JsonPropertyName("atelierNotifyEmail")]
    public string AtelierNotifyEmail { get; set; } = "";
}
