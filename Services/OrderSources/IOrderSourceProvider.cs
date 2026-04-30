using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GestionAtelier.Services.OrderSources;

/// <summary>
/// Représente un fichier découvert sur une source distante.
/// </summary>
public class RemoteFile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Abstraction commune à tous les connecteurs de sources de commandes.
/// </summary>
public interface IOrderSourceProvider : IDisposable
{
    /// <summary>Établit la connexion au serveur distant.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Liste les fichiers dans le dossier d'entrée du client donné.</summary>
    Task<List<RemoteFile>> ListFilesAsync(string clientFolder, CancellationToken ct = default);

    /// <summary>Télécharge un fichier vers un flux en mémoire.</summary>
    Task<byte[]> DownloadFileAsync(string remotePath, CancellationToken ct = default);

    /// <summary>Déplace un fichier vers /processed/ (avec horodatage).</summary>
    Task MoveToProcessedAsync(string remotePath, string clientFolder, CancellationToken ct = default);

    /// <summary>Déplace un fichier vers /error/ et dépose un fichier .error.txt.</summary>
    Task MoveToErrorAsync(string remotePath, string clientFolder, string errorMessage, CancellationToken ct = default);

    /// <summary>Ferme proprement la connexion.</summary>
    Task DisconnectAsync(CancellationToken ct = default);
}
