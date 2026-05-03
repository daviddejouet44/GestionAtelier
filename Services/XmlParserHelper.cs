using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GestionAtelier.Services;

/// <summary>
/// Helpers for XML parsing: encoding detection (BOM, ISO-8859-1) and
/// MasterPrint proprietary format recognition + field mapping.
/// </summary>
public static class XmlParserHelper
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads an XDocument from a stream, robustly handling UTF-8 BOM,
    /// UTF-16 BOM, and XML declarations that declare ISO-8859-1 / Windows-1252
    /// encoding (common in MasterPrint exports).
    /// </summary>
    public static XDocument LoadSafely(Stream stream)
    {
        // Buffer everything so we can retry with a different encoding if needed.
        byte[] raw;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            raw = ms.ToArray();
        }

        // First attempt: let .NET auto-detect (handles UTF-8 BOM and declared encoding).
        try
        {
            using var ms = new MemoryStream(raw);
            return XDocument.Load(ms);
        }
        catch (XmlException)
        {
            // Second attempt: strip a UTF-8 BOM if present and try again.
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
            {
                try
                {
                    using var ms = new MemoryStream(raw, 3, raw.Length - 3);
                    return XDocument.Load(ms);
                }
                catch (XmlException) { /* fall through */ }
            }

            // Third attempt: read as Windows-1252 / ISO-8859-1 (common in French ERP exports).
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                var enc = Encoding.GetEncoding(1252);
                var xmlString = enc.GetString(raw);
                // Strip any BOM-like character that may have been mis-decoded.
                if (xmlString.Length > 0 && xmlString[0] == '\uFEFF')
                    xmlString = xmlString.Substring(1);
                return XDocument.Parse(xmlString);
            }
            catch (XmlException)
            {
                // Last attempt: read as Latin-1.
                try
                {
                    var enc = Encoding.Latin1;
                    var xmlString = enc.GetString(raw);
                    return XDocument.Parse(xmlString);
                }
                catch (XmlException lastEx)
                {
                    throw new XmlException(
                        $"Impossible de parser le XML (UTF-8, Windows-1252, Latin-1 tous échoués). " +
                        $"Vérifiez que le fichier est bien un XML valide. Dernière erreur : {lastEx.Message}",
                        lastEx);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if the document looks like a MasterPrint XML export
    /// (root element <c>Infos_Commande_MasterPrint</c>).
    /// </summary>
    public static bool IsMasterPrint(XDocument doc) =>
        doc.Root?.Name.LocalName == "Infos_Commande_MasterPrint";

    /// <summary>
    /// Extracts standard fiche fields from a MasterPrint &lt;Commande&gt; element.
    /// Returns a dictionary keyed by the internal fiche field names.
    /// </summary>
    public static Dictionary<string, string> ParseMasterPrintCommande(XElement commande)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── Core job fields ──────────────────────────────────────────────────
        MapSimple(commande, "NomClient",    "client",            d);
        MapSimple(commande, "CodeClient",   "codeClient",        d);
        MapSimple(commande, "Reference",    "referenceCommande", d);
        MapSimple(commande, "Codeproduit",  "codeproduit",       d);
        MapSimple(commande, "Quantite",     "quantite",          d);
        MapSimple(commande, "Devis",        "devis",             d);
        MapSimple(commande, "Dossier",      "dossier",           d);
        MapSimple(commande, "OF",           "of",                d);

        // ── Delivery date: JJ/MM/AAAA → YYYY-MM-DD ──────────────────────────
        var dateEl = commande.Element("Datedelivraison");
        if (dateEl != null && !string.IsNullOrWhiteSpace(dateEl.Value))
        {
            var raw = dateEl.Value.Trim();
            if (DateTime.TryParseExact(raw, "dd/MM/yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                d["dateReceptionSouhaitee"] = dt.ToString("yyyy-MM-dd");
            else
                d["dateReceptionSouhaitee"] = raw; // keep as-is if format unknown
        }

        // ── Text blocks ──────────────────────────────────────────────────────
        var designation = JoinLignes(commande, "Designation");
        if (!string.IsNullOrWhiteSpace(designation)) d["designation"] = designation;

        var observations = JoinLignes(commande, "Observations");
        if (!string.IsNullOrWhiteSpace(observations)) d["observations"] = observations;

        // ── Contact info ─────────────────────────────────────────────────────
        var contact = commande.Element("Contact");
        if (contact != null)
        {
            var nom    = contact.Element("Nom")?.Value?.Trim() ?? "";
            var prenom = contact.Element("Prenom")?.Value?.Trim() ?? "";
            var full   = (nom + " " + prenom).Trim();
            if (!string.IsNullOrWhiteSpace(full))   d["contactNom"]       = full;
            var email  = contact.Element("Email")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(email))  d["contactEmail"]     = email!;
            var tel    = contact.Element("Telephone")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(tel))    d["contactTelephone"] = tel!;
        }

        // Fabricant email / phone as secondary contact
        var fab = commande.Element("Fabricant");
        if (fab != null)
        {
            var email = fab.Element("Email")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(email) && !d.ContainsKey("contactEmail"))
                d["fabricantEmail"] = email!;
        }

        // ── First component ──────────────────────────────────────────────────
        var composants = commande.Element("Composants");
        var firstItem  = composants?.Elements("Item").FirstOrDefault();
        if (firstItem != null)
        {
            MapSimple(firstItem, "Machine",   "machine",    d);
            MapSimple(firstItem, "Impression","impression", d);
            MapSimple(firstItem, "Process",   "process",    d);
            MapSimple(firstItem, "Pagination","pagination", d);
            MapSimple(firstItem, "Bascule",   "bascule",    d);
            MapSimple(firstItem, "PliJDF",    "pliJDF",     d);

            // Format fini: WidthxHeight mm
            var ff = firstItem.Element("FormatFini");
            if (ff != null)
            {
                var larg = ff.Element("Largeur")?.Value?.Trim() ?? "";
                var haut = ff.Element("Hauteur")?.Value?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(larg) && !string.IsNullOrWhiteSpace(haut))
                    d["format"] = $"{larg}x{haut}";
            }

            // Papier
            var papier = firstItem.Element("Papier");
            if (papier != null)
            {
                var nom       = papier.Element("Nom")?.Value?.Trim() ?? "";
                var famille   = papier.Element("Famille")?.Value?.Trim() ?? "";
                var grammage  = papier.Element("Grammage")?.Value?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(nom))     d["papier"]   = nom;
                if (!string.IsNullOrWhiteSpace(famille)) d["famillePapier"] = famille;
                if (!string.IsNullOrWhiteSpace(grammage)) d["grammage"] = grammage;
            }
        }

        return d;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void MapSimple(XElement parent, string xmlTag, string ficheKey, Dictionary<string, string> d)
    {
        var el = parent.Element(xmlTag);
        if (el != null && !string.IsNullOrWhiteSpace(el.Value))
            d[ficheKey] = el.Value.Trim();
    }

    private static string JoinLignes(XElement parent, string blockTag)
    {
        var block = parent.Element(blockTag);
        if (block == null) return "";
        var lines = block.Elements("Ligne").Select(l => l.Value.Trim()).Where(s => !string.IsNullOrWhiteSpace(s));
        return string.Join("\n", lines);
    }
}
