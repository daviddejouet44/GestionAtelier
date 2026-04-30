using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;
using Microsoft.Extensions.Logging;
using GestionAtelier.Models;

namespace GestionAtelier.Services.OrderSources;

/// <summary>
/// Connecteur Dropbox via le SDK officiel .NET (Dropbox.Api).
/// Utilise OAuth2 avec stockage du refresh token.
/// </summary>
public sealed class DropboxOrderSourceProvider : IOrderSourceProvider
{
    private readonly DropboxSourceConfig _cfg;
    private readonly ILogger _logger;
    private DropboxClient? _client;

    public DropboxOrderSourceProvider(DropboxSourceConfig cfg, ILogger logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.RefreshToken))
            throw new InvalidOperationException("Dropbox refresh token manquant. Veuillez autoriser l'application via OAuth2.");

        // Exchange refresh token for access token using Dropbox SDK
        _client = new DropboxClient(_cfg.RefreshToken,
            _cfg.AppKey,
            _cfg.AppSecret,
            new DropboxClientConfig("GestionAtelier/1.0"));

        // Validate connection by calling an API
        var account = await _client.Users.GetCurrentAccountAsync();
        _logger.LogInformation("[Dropbox] Connected as {Email}", account.Email);
    }

    public async Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var folderPath = BuildPath(clientFolder, "in");

        // Ensure folder exists (create if needed)
        await EnsureFolderExistsAsync(folderPath);

        var result = new List<RemoteFile>();
        ListFolderResult listing;
        try
        {
            listing = await _client!.Files.ListFolderAsync(folderPath);
        }
        catch (ApiException<ListFolderError> ex) when (ex.ErrorResponse.IsPath)
        {
            _logger.LogWarning("[Dropbox] Folder not found: {Path}", folderPath);
            return result;
        }

        do
        {
            foreach (var entry in listing.Entries)
            {
                if (entry.IsFile)
                {
                    var f = entry.AsFile;
                    result.Add(new RemoteFile
                    {
                        Name = f.Name,
                        Path = f.PathLower ?? f.PathDisplay ?? "",
                        Size = (long)f.Size,
                        LastModified = f.ServerModified
                    });
                }
            }

            if (!listing.HasMore) break;
            listing = await _client!.Files.ListFolderContinueAsync(listing.Cursor);
        }
        while (true);

        return result;
    }

    public async Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        using var response = await _client!.Files.DownloadAsync(remotePath);
        return await response.GetContentAsByteArrayAsync();
    }

    public async Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var fileName = Path.GetFileName(remotePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var destDir = BuildPath(clientFolder, "processed");
        await EnsureFolderExistsAsync(destDir);
        var dest = $"{destDir}/{timestamp}_{fileName}";

        await _client!.Files.MoveV2Async(new RelocationArg(remotePath, dest));
        _logger.LogInformation("[Dropbox] Moved {Src} → {Dest}", remotePath, dest);
    }

    public async Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var fileName = Path.GetFileName(remotePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var destDir = BuildPath(clientFolder, "error");
        await EnsureFolderExistsAsync(destDir);
        var dest = $"{destDir}/{timestamp}_{fileName}";

        await _client!.Files.MoveV2Async(new RelocationArg(remotePath, dest));

        // Upload .error.txt file
        var errorFilePath = $"{destDir}/{timestamp}_{fileName}.error.txt";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(errorMessage));
        await _client!.Files.UploadAsync(errorFilePath, body: ms);

        _logger.LogWarning("[Dropbox] Moved {Src} to error: {Error}", remotePath, errorMessage);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string BuildPath(string clientFolder, string subfolder)
    {
        var baseFolder = (_cfg.FolderPath ?? "/GestionAtelier").TrimEnd('/');
        return $"{baseFolder}/clients/{clientFolder}/{subfolder}".ToLowerInvariant();
    }

    private async Task EnsureFolderExistsAsync(string path)
    {
        try
        {
            await _client!.Files.CreateFolderV2Async(path);
        }
        catch (ApiException<CreateFolderError>)
        {
            // Folder already exists — that's fine
        }
    }

    private void EnsureConnected()
    {
        if (_client == null)
            throw new InvalidOperationException("Dropbox client is not connected. Call ConnectAsync() first.");
    }

    /// <summary>
    /// Génère l'URL d'autorisation OAuth2 Dropbox.
    /// </summary>
    public static string GetAuthorizationUrl(string appKey, string callbackUrl, string state)
    {
        // Dropbox OAuth2 PKCE flow URL
        return $"https://www.dropbox.com/oauth2/authorize" +
               $"?client_id={Uri.EscapeDataString(appKey)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&response_type=code" +
               $"&token_access_type=offline" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>
    /// Échange un code d'autorisation contre un refresh token.
    /// </summary>
    public static async Task<string> ExchangeCodeForRefreshTokenAsync(
        string appKey, string appSecret, string code, string callbackUrl)
    {
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", appKey),
            new KeyValuePair<string, string>("client_secret", appSecret),
            new KeyValuePair<string, string>("redirect_uri", callbackUrl),
        });

        var response = await http.PostAsync("https://api.dropbox.com/oauth2/token", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
            return rt.GetString() ?? "";
        throw new Exception("refresh_token absent de la réponse Dropbox");
    }
}
