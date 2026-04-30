using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Extensions.Logging;
using GestionAtelier.Models;

namespace GestionAtelier.Services.OrderSources;

public sealed class OneDriveOrderSourceProvider : IOrderSourceProvider
{
    private readonly OneDriveSourceConfig _cfg;
    private readonly ILogger _logger;
    private readonly Func<OneDriveSourceConfig, Task>? _onTokensRefreshed;
    private GraphServiceClient? _graphClient;
    private string _currentAccessToken = "";
    private string _effectiveDriveId = "";

    public OneDriveOrderSourceProvider(OneDriveSourceConfig cfg, ILogger logger, Func<OneDriveSourceConfig, Task>? onTokensRefreshed = null)
    {
        _cfg = cfg;
        _logger = logger;
        _onTokensRefreshed = onTokensRefreshed;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.RefreshToken))
            throw new InvalidOperationException("OneDrive refresh token manquant. Veuillez autoriser l'application via OAuth2.");

        var (accessToken, newRefreshToken) = await RefreshAccessTokenInternalAsync(ct);
        _currentAccessToken = accessToken;
        _cfg.RefreshToken = newRefreshToken;

        if (_onTokensRefreshed != null)
            await _onTokensRefreshed(_cfg);

        var authProvider = new SimpleBearerTokenAuthProvider(accessToken);
        _graphClient = new GraphServiceClient(authProvider);

        // Resolve effective drive ID — all item operations require graphClient.Drives["driveId"]
        if (_cfg.DriveType == "business" || _cfg.DriveType == "sharepoint")
        {
            if (string.IsNullOrEmpty(_cfg.DriveId))
                throw new InvalidOperationException($"OneDrive driveType='{_cfg.DriveType}' requires a DriveId.");
            _effectiveDriveId = _cfg.DriveId;
        }
        else
        {
            // personal: resolve driveId from /me/drive
            var myDrive = await _graphClient.Me.Drive.GetAsync(cancellationToken: ct);
            _effectiveDriveId = myDrive?.Id ?? throw new InvalidOperationException("Impossible de résoudre l'ID du drive personnel OneDrive.");
        }

        try
        {
            var me = await _graphClient.Me.GetAsync(cancellationToken: ct);
            _logger.LogInformation("[OneDrive] Connected as {DisplayName} ({Mail}), driveId={DriveId}", me?.DisplayName, me?.Mail ?? me?.UserPrincipalName, _effectiveDriveId);
        }
        catch
        {
            _logger.LogInformation("[OneDrive] Connected (driveType={DriveType}, driveId={DriveId})", _cfg.DriveType, _effectiveDriveId);
        }
    }

    public async Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var inFolderId = await GetOrCreateFolderPathAsync(new[] { "clients", clientFolder, "in" }, ct);

        var result = new List<RemoteFile>();
        var children = await GetDriveItemChildrenAsync(inFolderId, ct);

        foreach (var item in children)
        {
            if (item.File != null) // is a file (not a folder)
            {
                result.Add(new RemoteFile
                {
                    Name = item.Name ?? "",
                    Path = item.Id ?? "",
                    Size = item.Size ?? 0,
                    LastModified = item.LastModifiedDateTime?.UtcDateTime ?? DateTime.UtcNow
                });
            }
        }
        return result;
    }

    public async Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        var stream = await _graphClient!.Drives[_effectiveDriveId].Items[remotePath].Content.GetAsync(cancellationToken: ct);
        if (stream == null) return Array.Empty<byte>();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(new[] { "clients", clientFolder, "processed" }, ct);

        var updateBody = new DriveItem
        {
            ParentReference = new ItemReference { Id = destFolderId }
        };
        await UpdateDriveItemAsync(remotePath, updateBody, ct);
        _logger.LogInformation("[OneDrive] Moved item {Id} to processed", remotePath);
    }

    public async Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(new[] { "clients", clientFolder, "error" }, ct);

        var item = await GetDriveItemAsync(remotePath, ct);
        var fileName = item?.Name ?? remotePath;

        var updateBody = new DriveItem
        {
            ParentReference = new ItemReference { Id = destFolderId }
        };
        await UpdateDriveItemAsync(remotePath, updateBody, ct);

        var errorContent = Encoding.UTF8.GetBytes(errorMessage);
        await UploadFileToDriveAsync(destFolderId, $"{fileName}.error.txt", errorContent, ct);

        _logger.LogWarning("[OneDrive] Moved item {Id} to error: {Error}", remotePath, errorMessage);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _graphClient = null;
        _currentAccessToken = "";
        _effectiveDriveId = "";
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _graphClient = null;
        _currentAccessToken = "";
        _effectiveDriveId = "";
    }

    private void EnsureConnected()
    {
        if (_graphClient == null || string.IsNullOrEmpty(_effectiveDriveId))
            throw new InvalidOperationException("OneDrive not connected. Call ConnectAsync() first.");
    }

    private async Task<List<DriveItem>> GetDriveItemChildrenAsync(string itemId, CancellationToken ct)
    {
        var resp = await _graphClient!.Drives[_effectiveDriveId].Items[itemId].Children.GetAsync(cancellationToken: ct);
        return resp?.Value ?? new List<DriveItem>();
    }

    private Task<DriveItem?> GetDriveItemAsync(string itemId, CancellationToken ct)
        => _graphClient!.Drives[_effectiveDriveId].Items[itemId].GetAsync(cancellationToken: ct);

    private Task UpdateDriveItemAsync(string itemId, DriveItem body, CancellationToken ct)
        => _graphClient!.Drives[_effectiveDriveId].Items[itemId].PatchAsync(body, cancellationToken: ct);

    private async Task<string> GetOrCreateFolderPathAsync(string[] pathParts, CancellationToken ct)
    {
        var currentId = string.IsNullOrEmpty(_cfg.FolderItemId) ? "root" : _cfg.FolderItemId;

        foreach (var part in pathParts)
        {
            var children = await GetDriveItemChildrenAsync(currentId, ct);
            string? foundId = null;
            foreach (var child in children)
            {
                if (child.Folder != null && child.Name == part)
                {
                    foundId = child.Id;
                    break;
                }
            }

            if (foundId != null)
            {
                currentId = foundId;
            }
            else
            {
                var newFolder = new DriveItem
                {
                    Name = part,
                    Folder = new Folder(),
                    AdditionalData = new Dictionary<string, object> { { "@microsoft.graph.conflictBehavior", "fail" } }
                };
                var created = await _graphClient!.Drives[_effectiveDriveId].Items[currentId].Children.PostAsync(newFolder, cancellationToken: ct);
                currentId = created?.Id ?? throw new Exception($"Failed to create folder '{part}'");
            }
        }
        return currentId;
    }

    private async Task UploadFileToDriveAsync(string parentFolderId, string fileName, byte[] content, CancellationToken ct)
    {
        using var ms = new MemoryStream(content);
        await _graphClient!.Drives[_effectiveDriveId].Items[parentFolderId].ItemWithPath(fileName).Content.PutAsync(ms, cancellationToken: ct);
    }

    private async Task<(string accessToken, string refreshToken)> RefreshAccessTokenInternalAsync(CancellationToken ct)
    {
        var tenant = string.IsNullOrEmpty(_cfg.TenantId) ? "common" : _cfg.TenantId;
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", _cfg.RefreshToken),
            new KeyValuePair<string, string>("client_id", _cfg.AppClientId),
            new KeyValuePair<string, string>("client_secret", _cfg.AppClientSecret),
            new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/Files.ReadWrite https://graph.microsoft.com/Sites.ReadWrite.All offline_access"),
        });

        var response = await http.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", body, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("access_token").GetString() ?? "",
            doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? _cfg.RefreshToken : _cfg.RefreshToken
        );
    }

    /// <summary>Génère l'URL d'autorisation OAuth2 Microsoft (OneDrive/SharePoint).</summary>
    public static string GetAuthorizationUrl(string clientId, string tenantId, string callbackUrl, string state, string driveType)
    {
        var tenant = string.IsNullOrEmpty(tenantId) ? "common" : tenantId;
        var scopes = driveType == "sharepoint"
            ? Uri.EscapeDataString("https://graph.microsoft.com/Files.ReadWrite https://graph.microsoft.com/Sites.ReadWrite.All offline_access")
            : Uri.EscapeDataString("https://graph.microsoft.com/Files.ReadWrite offline_access");
        return $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&response_type=code" +
               $"&scope={scopes}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>Échange un code d'autorisation contre les tokens Microsoft.</summary>
    public static async Task<(string accessToken, string refreshToken)> ExchangeCodeForTokensAsync(
        string clientId, string clientSecret, string tenantId, string code, string callbackUrl)
    {
        var tenant = string.IsNullOrEmpty(tenantId) ? "common" : tenantId;
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", callbackUrl),
            new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/Files.ReadWrite https://graph.microsoft.com/Sites.ReadWrite.All offline_access"),
        });

        var response = await http.PostAsync($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return (
            doc.RootElement.GetProperty("access_token").GetString() ?? "",
            doc.RootElement.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : ""
        );
    }
}

/// <summary>Simple bearer token authentication provider for Microsoft Graph SDK.</summary>
internal sealed class SimpleBearerTokenAuthProvider : IAuthenticationProvider
{
    private readonly string _accessToken;

    public SimpleBearerTokenAuthProvider(string accessToken) => _accessToken = accessToken;

    public Task AuthenticateRequestAsync(RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");
        return Task.CompletedTask;
    }
}
