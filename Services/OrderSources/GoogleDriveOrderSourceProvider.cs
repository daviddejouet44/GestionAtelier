using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using GestionAtelier.Models;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace GestionAtelier.Services.OrderSources;

public sealed class GoogleDriveOrderSourceProvider : IOrderSourceProvider
{
    private readonly GoogleDriveSourceConfig _cfg;
    private readonly ILogger _logger;
    private DriveService? _service;
    private readonly Dictionary<string, string> _folderIdCache = new();

    public GoogleDriveOrderSourceProvider(GoogleDriveSourceConfig cfg, ILogger logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_cfg.RefreshToken))
            throw new InvalidOperationException("Google Drive refresh token manquant. Veuillez autoriser l'application via OAuth2.");

        var secrets = new ClientSecrets { ClientId = _cfg.AppClientId, ClientSecret = _cfg.AppClientSecret };
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = new[] { DriveService.Scope.Drive }
        });
        var tokenResponse = new TokenResponse { RefreshToken = _cfg.RefreshToken };
        var credential = new UserCredential(flow, "gestion-atelier-user", tokenResponse);

        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "GestionAtelier"
        });

        var aboutRequest = _service.About.Get();
        aboutRequest.Fields = "user";
        var about = await aboutRequest.ExecuteAsync(ct);
        _logger.LogInformation("[GoogleDrive] Connected as {Email}", about.User?.EmailAddress);
    }

    public async Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var inFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "in" }, ct);

        var result = new List<RemoteFile>();
        var request = _service!.Files.List();
        request.Q = $"'{inFolderId}' in parents and trashed=false and mimeType!='application/vnd.google-apps.folder'";
        request.Fields = "files(id,name,size,modifiedTime)";
        request.PageSize = 100;

        var listing = await request.ExecuteAsync(ct);
        foreach (var file in listing.Files ?? new List<DriveFile>())
        {
            result.Add(new RemoteFile
            {
                Name = file.Name,
                Path = file.Id,
                Size = file.Size ?? 0,
                LastModified = file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow
            });
        }
        return result;
    }

    public async Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        var getRequest = _service!.Files.Get(remotePath);
        using var ms = new MemoryStream();
        await getRequest.DownloadAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "processed" }, ct);

        var getReq = _service!.Files.Get(remotePath);
        getReq.Fields = "id,name,parents";
        var fileData = await getReq.ExecuteAsync(ct);

        var updateRequest = _service.Files.Update(new DriveFile(), remotePath);
        updateRequest.AddParents = destFolderId;
        if (fileData.Parents != null && fileData.Parents.Count > 0)
            updateRequest.RemoveParents = string.Join(",", fileData.Parents);

        await updateRequest.ExecuteAsync(ct);
        _logger.LogInformation("[GoogleDrive] Moved {Id} to processed", remotePath);
    }

    public async Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var destFolderId = await GetOrCreateFolderPathAsync(_cfg.FolderId, new[] { "clients", clientFolder, "error" }, ct);

        var getReq = _service!.Files.Get(remotePath);
        getReq.Fields = "id,name,parents";
        var fileData = await getReq.ExecuteAsync(ct);

        var updateRequest = _service.Files.Update(new DriveFile(), remotePath);
        updateRequest.AddParents = destFolderId;
        if (fileData.Parents != null && fileData.Parents.Count > 0)
            updateRequest.RemoveParents = string.Join(",", fileData.Parents);
        await updateRequest.ExecuteAsync(ct);

        // Upload .error.txt
        var errorContent = Encoding.UTF8.GetBytes(errorMessage);
        var errorFileMeta = new DriveFile
        {
            Name = $"{fileData.Name}.error.txt",
            Parents = new List<string> { destFolderId }
        };
        using var ms = new MemoryStream(errorContent);
        var uploadRequest = _service.Files.Create(errorFileMeta, ms, "text/plain");
        await uploadRequest.UploadAsync(ct);

        _logger.LogWarning("[GoogleDrive] Moved {Id} to error: {Error}", remotePath, errorMessage);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _service?.Dispose();
        _service = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _service?.Dispose();
        _service = null;
    }

    private void EnsureConnected()
    {
        if (_service == null)
            throw new InvalidOperationException("Google Drive not connected. Call ConnectAsync() first.");
    }

    private async Task<string> GetOrCreateFolderPathAsync(string rootFolderId, string[] pathParts, CancellationToken ct)
    {
        var currentId = rootFolderId;
        foreach (var part in pathParts)
        {
            var cacheKey = $"{currentId}:{part}";
            if (_folderIdCache.TryGetValue(cacheKey, out var cachedId))
            {
                currentId = cachedId;
                continue;
            }

            // Escape single quotes in folder name
            var escapedPart = part.Replace("'", "\\'");
            var listReq = _service!.Files.List();
            listReq.Q = $"'{currentId}' in parents and name='{escapedPart}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
            listReq.Fields = "files(id)";
            listReq.PageSize = 1;
            var listing = await listReq.ExecuteAsync(ct);

            if (listing.Files?.Count > 0)
            {
                currentId = listing.Files[0].Id;
            }
            else
            {
                var folderMeta = new DriveFile
                {
                    Name = part,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new List<string> { currentId }
                };
                var createReq = _service.Files.Create(folderMeta);
                createReq.Fields = "id";
                var created = await createReq.ExecuteAsync(ct);
                currentId = created.Id;
            }

            _folderIdCache[cacheKey] = currentId;
        }
        return currentId;
    }

    /// <summary>Génère l'URL d'autorisation OAuth2 Google Drive.</summary>
    public static string GetAuthorizationUrl(string clientId, string callbackUrl, string state)
    {
        var scope = Uri.EscapeDataString("https://www.googleapis.com/auth/drive");
        return $"https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={Uri.EscapeDataString(clientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&response_type=code" +
               $"&scope={scope}" +
               $"&access_type=offline" +
               $"&prompt=consent" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>Échange un code d'autorisation contre un refresh token.</summary>
    public static async Task<string> ExchangeCodeForRefreshTokenAsync(
        string clientId, string clientSecret, string code, string callbackUrl)
    {
        using var http = new HttpClient();
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("client_secret", clientSecret),
            new KeyValuePair<string, string>("redirect_uri", callbackUrl),
        });

        var response = await http.PostAsync("https://oauth2.googleapis.com/token", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("refresh_token", out var rt) && !string.IsNullOrEmpty(rt.GetString()))
            return rt.GetString()!;
        throw new Exception("refresh_token absent de la réponse Google. Assurez-vous que 'access_type=offline' et 'prompt=consent' sont dans l'URL d'autorisation.");
    }
}
