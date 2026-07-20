using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

/// <summary>
/// Calcule le TAC (Total Area Coverage / taux d'encrage total) et la couverture par plaque
/// en utilisant le device <c>inkcov</c> de Ghostscript.
/// </summary>
/// <remarks>
/// Le device <c>inkcov</c> fournit les fractions d'encrage (0–1) pour chaque plaque CMYK
/// par page, mais ne donne aucune information sur le mode de surimpression (overprint).
/// La détection de la surimpression est assurée par <see cref="PdfAnalyzer"/> via l'inspection
/// des ressources ExtGState du PDF (propriété <c>HasOverprint</c> de <see cref="PdfAnalysisReport"/>).
/// </remarks>
public static class GhostscriptInkAnalyzer
{
    private const int DefaultTimeoutSeconds = 60;

    // Ligne de sortie inkcov :   0.22479  0.00000  0.06773  0.00780  CMYK OK
    private static readonly Regex CmykLineRegex = new(
        @"^\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+CMYK\s+OK",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Enrichit <paramref name="report"/> avec <c>TotalInkCoveragePercent</c> et
    /// <c>PlateCoveragePercent</c> calculés via Ghostscript.
    /// En cas d'erreur ou d'absence de Ghostscript, les champs restent <c>null</c>
    /// (dégradation gracieuse — les Étapes 1 à 4 ne sont pas affectées).
    /// </summary>
    /// <param name="report">Rapport à enrichir (modifié en place).</param>
    /// <param name="pdfPath">Chemin absolu vers le fichier PDF à analyser.</param>
    /// <param name="ghostscriptExePath">
    /// Chemin vers l'exécutable Ghostscript. Vide ou null = analyse ignorée.
    /// </param>
    /// <param name="timeoutSeconds">Timeout en secondes (défaut : 60).</param>
    public static void Enrich(
        PdfAnalysisReport report,
        string pdfPath,
        string? ghostscriptExePath,
        int timeoutSeconds = DefaultTimeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(ghostscriptExePath))
            return; // Ghostscript non configuré — comportement opt-in

        if (!File.Exists(ghostscriptExePath))
        {
            Console.WriteLine($"[WARN] GhostscriptInkAnalyzer: exécutable introuvable : {ghostscriptExePath}");
            return;
        }

        if (!File.Exists(pdfPath))
            return;

        try
        {
            var psi = new ProcessStartInfo(ghostscriptExePath,
                $"-dBATCH -dNOPAUSE -dQUIET -sDEVICE=inkcov -sOutputFile=- \"{pdfPath}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var process = new Process { StartInfo = psi };

            var outputSb = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            // Drainer stderr pour éviter tout deadlock (non utilisé pour le parsing)
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutSeconds * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignoré */ }
                Console.WriteLine($"[WARN] GhostscriptInkAnalyzer: timeout ({timeoutSeconds}s) sur {pdfPath}");
                return;
            }

            ParseAndEnrich(report, outputSb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] GhostscriptInkAnalyzer: erreur lors de l'analyse de {pdfPath} : {ex.Message}");
        }
    }

    /// <summary>
    /// Parse la sortie Ghostscript inkcov et renseigne les champs TAC du rapport.
    /// </summary>
    private static void ParseAndEnrich(PdfAnalysisReport report, string output)
    {
        var matches = CmykLineRegex.Matches(output);
        if (matches.Count == 0)
            return;

        double maxTac = 0.0;
        double sumC = 0.0, sumM = 0.0, sumY = 0.0, sumK = 0.0;

        foreach (Match m in matches)
        {
            double c = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double mg = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            double k = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);

            double pageTac = (c + mg + y + k) * 100.0;
            if (pageTac > maxTac)
                maxTac = pageTac;

            sumC += c;
            sumM += mg;
            sumY += y;
            sumK += k;
        }

        int pageCount = matches.Count;

        // TAC maximum sur l'ensemble des pages
        report.TotalInkCoveragePercent = Math.Round(maxTac, 2);

        // Couverture moyenne par plaque (en %)
        report.PlateCoveragePercent = new Dictionary<string, double>
        {
            ["C"] = Math.Round(sumC / pageCount * 100.0, 2),
            ["M"] = Math.Round(sumM / pageCount * 100.0, 2),
            ["Y"] = Math.Round(sumY / pageCount * 100.0, 2),
            ["K"] = Math.Round(sumK / pageCount * 100.0, 2),
        };
    }
}
