using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Box.V2;
using Box.V2.Auth;
using Box.V2.Config;
using Box.V2.Models;
using Microsoft.Extensions.Logging;
using GestionAtelier.Models;

namespace GestionAtelier.Services.OrderSources;

public sealed class BoxOrderSourceProvider : IOrderSourceProvider
{
    private readonly BoxSourceConfig _cfg;
    private readonly ILogger _logger;
    private readonly Func<BoxSourceConfig, Task>? _onTokensRefreshed;
    private BoxClient? _client;

    public BoxOrderSourceProvider(BoxSourceConfig cfg, ILogger logger, Func<BoxSourceConfig, Task>? onTokensRefreshed = null)
    {
        _cfg = cfg;
        _logger = logger;
        _onTokensRefreshed = onTokensRefreshed;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.RefreshToken))
            throw new InvalidOperationException("Box refresh token manquant. Veuillez autoriser l'application via OAuth2.");

        // Always refresh to get a fresh access token and new refresh token
        var (newAccessToken, newRefreshToken) = await RefreshAccessTokenInternalAsync(ct);

        _cfg.AccessToken = newAccessToken;
        _cfg.RefreshToken = newRefreshToken;

        if (_onTokensRefreshed != null)
            await _onTokensRefreshed(_cfg);

        var config = new BoxConfig(_cfg.AppClientId, _cfg.AppClientSecret, new Uri("http://localhost"));
        var session = new OAuthSession(newAccessToken, newRefreshToken, 3600, "bearer");
        _client = new BoxClient(config, session);

        var user = await _client.UsersManager.GetCurrentUserInformationAsync();
        _logger.LogInformation("[Box] Connected as {Name} ({Login})", user.Name, user.Login);
    }

    public async Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var inFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "in" }, ct);

        var result = new List<RemoteFile>();
        var items = await _client!.FoldersManager.GetFolderItemsAsync(inFolderId, 100);
        foreach (var item in items.Entries ?? new List<BoxItem>())
        {
            if (item.Type == "file")
            {
                result.Add(new RemoteFile
                {
                    Name = item.Name,
                    Path = item.Id,
                    Size = 0, // Box doesn't return size in folder listing by default
                    LastModified = DateTime.UtcNow
                });
            }
        }
        return result;
    }

    public async Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        using var stream = await _client!.FilesManager.DownloadAsync(remotePath);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "processed" }, ct);

        var updateInfo = new BoxFileRequest { Parent = new BoxRequestEntity { Id = destFolderId } };
        await _client!.FilesManager.UpdateInformationAsync(updateInfo, remotePath);
        _logger.LogInformation("[Box] Moved file {Id} to processed", remotePath);
    }

    public async Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "error" }, ct);

        // Get file info for name
        var fileInfo = await _client!.FilesManager.GetInformationAsync(remotePath);

        // Move file
        var updateInfo = new BoxFileRequest { Parent = new BoxRequestEntity { Id = destFolderId } };
        await _client.FilesManager.UpdateInformationAsync(updateInfo, remotePath);

        // Upload .error.txt
        var errorContent = Encoding.UTF8.GetBytes(errorMessage);
        using var ms = new MemoryStream(errorContent);
        var uploadRequest = new BoxFileRequest
        {
            Name = $"{fileInfo.Name}.error.txt",
            Parent = new BoxRequestEntity { Id = destFolderId }
        };
        await _client.FilesManager.UploadAsync(uploadRequest, ms);

        _logger.LogWarning("[Box] Moved file {Id} to error: {Error}", remotePath, errorMessage);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _client = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client = null;
    }

    private void EnsureConnected()
    {
        if (_client == null)
            throw new InvalidOperationException("Box client not connected. Call ConnectAsync() first.");
    }

    private async Task<string> GetOrCreateFolderPathAsync(string rootFolderId, string[] pathParts, CancellationToken ct)
    {
        var currentId = rootFolderId;
        foreach (var part in pathParts)
        {
            var items = await _client!.FoldersManager.GetFolderItemsAsync(currentId, 200);
            string? foundId = null;
            foreach (var item in items.Entries ?? new List<BoxItem>())
            {
                if (item.Type == "folder" && item.Name == part)
                {
                    foundId = item.Id;
                    break;
                }
            }

            if (foundId != null)
            {
                currentId = foundId;
            }
            else
            {
                var createReq = new BoxFolderRequest
                {
                    Name = part,
                    Parent = new BoxRequestEntity { Id = currentId }
                };
                var created = await _client.FoldersManager.CreateAsync(createReq);
                currentId = created.Id;
            }
        }
        return currentId;
    }

    private async Task<(string accessToken, string refreshToken)> RefreshAccessTokenInternalAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", _cfg.RefreshToken),
            new KeyValuePair<string, string>("client_id", _cfg.AppClientId),
            new KeyValuePair<string, string>("client_secret", _cfg.AppClientSecret),
        });

        var response = await http.PostAsync("https://api.box.com/oauth2/token", body, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("access_token").GetString() ?? "",
            doc.RootElement.GetProperty("refresh_token").GetString() ?? ""
        );
    }

    /// <summary>Génère l'URL d'autorisation OAuth2 Box.</summary>
    public static string GetAuthorizationUrl(string clientId, string callbackUrl, string state)
    {
        return $"https://account.box.com/api/oauth2/authorize" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&response_type=code" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>Échange un code d'autorisation contre les tokens Box.</summary>
    public static async Task<(string accessToken, string refreshToken)> ExchangeCodeForTokensAsync(
        string clientId, string clientSecret, string code, string callbackUrl)
    {
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", callbackUrl),
        });

        var response = await http.PostAsync("https://api.box.com/oauth2/token", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("access_token").GetString() ?? "",
            doc.RootElement.GetProperty("refresh_token").GetString() ?? ""
        );
    }
}
