using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

// ── OrderSource entity ──────────────────────────────────────────────────────
public class OrderSource
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Type de source : "sftp" | "dropbox"</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Configuration JSON chiffrée (credentials). Stockée chiffrée via AES en base.</summary>
    [JsonPropertyName("configEncrypted")]
    public string ConfigEncrypted { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("pollingIntervalMinutes")]
    public int PollingIntervalMinutes { get; set; } = 5;

    [JsonPropertyName("defaultQuantity")]
    public int DefaultQuantity { get; set; } = 1;

    [JsonPropertyName("defaultFormat")]
    public string DefaultFormat { get; set; } = "";

    /// <summary>JSON: { "dossierName": "clientId" }</summary>
    [JsonPropertyName("clientMapping")]
    public Dictionary<string, string> ClientMapping { get; set; } = new();

    [JsonPropertyName("maxFileSizeMb")]
    public int MaxFileSizeMb { get; set; } = 200;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("lastPollAt")]
    public string? LastPollAt { get; set; }

    [JsonPropertyName("lastPollStatus")]
    public string LastPollStatus { get; set; } = "never";
}

// ── OrderSourceImport log entry ─────────────────────────────────────────────
public class OrderSourceImport
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = "";

    [JsonPropertyName("sourceName")]
    public string SourceName { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("fileHash")]
    public string FileHash { get; set; } = "";

    /// <summary>success | error | duplicate</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("jobId")]
    public string? JobId { get; set; }

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("processedAt")]
    public string ProcessedAt { get; set; } = "";
}

// ── SFTP config (stored encrypted) ──────────────────────────────────────────
public class SftpSourceConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 22;

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("privateKey")]
    public string PrivateKey { get; set; } = "";

    [JsonPropertyName("privateKeyPassphrase")]
    public string PrivateKeyPassphrase { get; set; } = "";

    [JsonPropertyName("baseDir")]
    public string BaseDir { get; set; } = "/";

    [JsonPropertyName("hostFingerprint")]
    public string HostFingerprint { get; set; } = "";
}

// ── Dropbox config (stored encrypted) ───────────────────────────────────────
public class DropboxSourceConfig
{
    [JsonPropertyName("appKey")]
    public string AppKey { get; set; } = "";

    [JsonPropertyName("appSecret")]
    public string AppSecret { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("folderPath")]
    public string FolderPath { get; set; } = "/GestionAtelier";
}

// ── Global Dropbox OAuth app config ─────────────────────────────────────────
public class DropboxGlobalConfig
{
    [JsonPropertyName("appKey")]
    public string AppKey { get; set; } = "";

    [JsonPropertyName("appSecret")]
    public string AppSecret { get; set; } = "";

    [JsonPropertyName("callbackUrl")]
    public string CallbackUrl { get; set; } = "http://localhost:5080/api/integrations/dropbox/callback";
}
