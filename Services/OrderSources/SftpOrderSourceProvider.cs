using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using GestionAtelier.Models;

namespace GestionAtelier.Services.OrderSources;

/// <summary>
/// Connecteur SFTP basé sur SSH.NET (Renci.SshNet).
/// Supporte mot de passe ou clé privée PEM avec passphrase optionnelle.
/// </summary>
public sealed class SftpOrderSourceProvider : IOrderSourceProvider
{
    private readonly SftpSourceConfig _cfg;
    private readonly ILogger _logger;
    private SftpClient? _client;

    public SftpOrderSourceProvider(SftpSourceConfig cfg, ILogger logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _client = BuildClient();
        _client.Connect();
        _logger.LogInformation("[SFTP] Connected to {Host}:{Port}", _cfg.Host, _cfg.Port);
        return Task.CompletedTask;
    }

    public Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var inPath = BuildPath(clientFolder, "in");

        // Create the /in/ folder if it does not exist yet
        if (!_client!.Exists(inPath))
        {
            _logger.LogInformation("[SFTP] Creating missing folder {Path}", inPath);
            EnsureDirExists(inPath);
        }

        var result = new List<RemoteFile>();
        foreach (var entry in _client!.ListDirectory(inPath))
        {
            if (entry.IsRegularFile && !entry.Name.StartsWith("."))
            {
                result.Add(new RemoteFile
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    Size = entry.Attributes.Size,
                    LastModified = entry.LastWriteTime
                });
            }
        }
        return Task.FromResult(result);
    }

    public Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureConnected();
        using var ms = new MemoryStream();
        _client!.DownloadFile(remotePath, ms);
        return Task.FromResult(ms.ToArray());
    }

    public Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default)
    {
        EnsureConnected();
        var fileName = Path.GetFileName(remotePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var destDir = BuildPath(clientFolder, "processed");
        EnsureDirExists(destDir);
        var dest = $"{destDir}/{timestamp}_{fileName}";
        _client!.RenameFile(remotePath, dest);
        _logger.LogInformation("[SFTP] Moved {Src} → {Dest}", remotePath, dest);
        return Task.CompletedTask;
    }

    public Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default)
    {
        EnsureConnected();
        var fileName = Path.GetFileName(remotePath);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var destDir = BuildPath(clientFolder, "error");
        EnsureDirExists(destDir);
        var dest = $"{destDir}/{timestamp}_{fileName}";
        _client!.RenameFile(remotePath, dest);

        // Write .error.txt alongside
        var errorFilePath = $"{destDir}/{timestamp}_{fileName}.error.txt";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(errorMessage));
        _client!.UploadFile(ms, errorFilePath);

        _logger.LogWarning("[SFTP] Moved {Src} to error: {Error}", remotePath, errorMessage);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client?.IsConnected == true)
            _client.Disconnect();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private SftpClient BuildClient()
    {
        var host = _cfg.Host;
        var port = _cfg.Port > 0 ? _cfg.Port : 22;

        Renci.SshNet.ConnectionInfo connInfo;

        if (!string.IsNullOrWhiteSpace(_cfg.PrivateKey))
        {
            // Key-based auth
            PrivateKeyFile keyFile;
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(_cfg.PrivateKey));
            if (!string.IsNullOrEmpty(_cfg.PrivateKeyPassphrase))
                keyFile = new PrivateKeyFile(keyStream, _cfg.PrivateKeyPassphrase);
            else
                keyFile = new PrivateKeyFile(keyStream);

            connInfo = new Renci.SshNet.ConnectionInfo(host, port, _cfg.Username,
                new PrivateKeyAuthenticationMethod(_cfg.Username, keyFile));
        }
        else
        {
            // Password auth
            connInfo = new Renci.SshNet.ConnectionInfo(host, port, _cfg.Username,
                new PasswordAuthenticationMethod(_cfg.Username, _cfg.Password));
        }

        var client = new SftpClient(connInfo);

        // Optional fingerprint verification
        if (!string.IsNullOrWhiteSpace(_cfg.HostFingerprint))
        {
            client.HostKeyReceived += (_, e) =>
            {
                var fp = BitConverter.ToString(e.FingerPrint).Replace("-", ":");
                if (!fp.Equals(_cfg.HostFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    e.CanTrust = false;
                    _logger.LogError("[SFTP] Host fingerprint mismatch! Expected {Expected}, got {Got}",
                        _cfg.HostFingerprint, fp);
                }
                else
                {
                    e.CanTrust = true;
                }
            };
        }

        return client;
    }

    private string BuildPath(string clientFolder, string subfolder)
    {
        var baseDir = _cfg.BaseDir.TrimEnd('/');
        return $"{baseDir}/clients/{clientFolder}/{subfolder}";
    }

    private void EnsureDirExists(string path)
    {
        // Create intermediate directories
        var parts = path.TrimStart('/').Split('/');
        var current = "";
        foreach (var part in parts)
        {
            current += "/" + part;
            if (!string.IsNullOrEmpty(part) && !_client!.Exists(current))
            {
                try { _client!.CreateDirectory(current); }
                catch { /* already exists or permission denied */ }
            }
        }
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected)
            throw new InvalidOperationException("SFTP client is not connected. Call ConnectAsync() first.");
    }
}
