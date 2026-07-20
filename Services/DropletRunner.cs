using System.Diagnostics;

namespace GestionAtelier.Services;

/// <summary>Résultat d'une exécution de droplet Acrobat.</summary>
public sealed class DropletRunResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Service réutilisable d'exécution d'un droplet Acrobat sur un fichier PDF.
/// <para>
/// Extrait du mécanisme d'exécution présent dans <c>/api/acrobat/preflight</c> afin de
/// le partager avec <see cref="PreflightChainRunner"/> sans dupliquer la logique.
/// </para>
/// </summary>
public static class DropletRunner
{
    /// <summary>Délai de grâce après la sortie du droplet pour laisser Acrobat fermer le fichier.</summary>
    public static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(5);

    /// <summary>Délai d'attente maximum avant d'interrompre le droplet.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Lance le droplet Acrobat sur le <paramref name="pdfPath"/> indiqué, attend sa fin
    /// (5 min max) et laisse le temps à Acrobat de fermer le fichier (5 s).
    /// <para>
    /// Ne déplace ni ne supprime aucun fichier — seule la logique de lancement/attente est ici.
    /// C'est l'appelant qui gère la suite (déplacement vers « Prêt pour impression », etc.).
    /// </para>
    /// </summary>
    /// <param name="dropletExe">Chemin complet du droplet Acrobat à exécuter.</param>
    /// <param name="pdfPath">Chemin complet du fichier PDF à traiter.</param>
    /// <param name="ct">Jeton d'annulation optionnel.</param>
    /// <returns>Un <see cref="DropletRunResult"/> indiquant le succès ou l'erreur.</returns>
    public static async Task<DropletRunResult> RunAsync(string dropletExe, string pdfPath, CancellationToken ct = default)
    {
        if (!File.Exists(dropletExe))
            return new DropletRunResult { Ok = false, Error = $"Droplet introuvable : {dropletExe}." };

        var psi = new ProcessStartInfo(dropletExe, $"\"{pdfPath}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new DropletRunResult { Ok = false, Error = $"Impossible de démarrer le droplet : {ex.Message}" };
        }

        if (process == null)
            return new DropletRunResult { Ok = false, Error = "Impossible de démarrer le droplet Preflight." };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { }
            if (ct.IsCancellationRequested)
                return new DropletRunResult { Ok = false, Error = "Opération annulée." };
            return new DropletRunResult { Ok = false, Error = "Le droplet Preflight a dépassé le délai d'attente (5 min)." };
        }

        // Laisse Acrobat fermer/flush le fichier après la sortie du processus.
        await Task.Delay(FlushDelay, CancellationToken.None);

        return new DropletRunResult { Ok = true };
    }
}
