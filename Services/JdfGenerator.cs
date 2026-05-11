using System;
using System.IO;
using System.Xml.Linq;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class JdfGenerator
{
    private static readonly XNamespace Jdf = "http://www.CIP4.org/JDFSchema_1_1";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly XNamespace Efi = "http://www.efi.com/efijdf";

    /// <summary>
    /// Generates a Fiery EFI JDF (Combined Digital Printing ICS) ticket.
    /// </summary>
    public static string GenerateFieryJdf(FabricationSheet sheet, string pdfFileName)
    {
        var jobId = $"GA_{sheet.NumeroDossier ?? "0"}_{DateTime.Now:yyyyMMddHHmmss}";
        var sides = MapSides(sheet.RectoVerso);
        var quantity = sheet.Quantite?.ToString() ?? "1";

        var root = new XElement(Jdf + "JDF",
            new XAttribute("xmlns", "http://www.CIP4.org/JDFSchema_1_1"),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "EFI", Efi.NamespaceName),
            new XAttribute("ID", jobId),
            new XAttribute("JobID", sheet.NumeroDossier ?? ""),
            new XAttribute("JobPartID", "p1"),
            new XAttribute("Activation", "Active"),
            new XAttribute("Status", "Ready"),
            new XAttribute("Type", "Combined"),
            new XAttribute("Types", "LayoutPreparation Imposition Interpreting Rendering DigitalPrinting"),
            new XAttribute("Version", "1.3"),
            new XAttribute("Category", "DigitalPrinting"),
            new XAttribute("DescriptiveName", BuildDescription(sheet)));

        // AuditPool
        root.Add(new XElement(Jdf + "AuditPool",
            new XElement(Jdf + "Created",
                new XAttribute("TimeStamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XAttribute("AgentName", "GestionAtelier"),
                new XAttribute("AgentVersion", "1.0"))));

        // ResourceLinkPool
        var linkPool = new XElement(Jdf + "ResourceLinkPool");
        linkPool.Add(new XElement(Jdf + "LayoutPreparationParamsLink", new XAttribute("rRef", "LPP1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "RunListLink", new XAttribute("rRef", "RL1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "MediaLink", new XAttribute("rRef", "M1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "DigitalPrintingParamsLink", new XAttribute("rRef", "DPP1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "ComponentLink", new XAttribute("rRef", "CO1"), new XAttribute("Usage", "Output"), new XAttribute("Amount", quantity)));

        if (HasBinding(sheet))
            linkPool.Add(new XElement(Jdf + "StitchingParamsLink", new XAttribute("rRef", "STP1"), new XAttribute("Usage", "Input")));
        if (HasFolding(sheet))
            linkPool.Add(new XElement(Jdf + "FoldingParamsLink", new XAttribute("rRef", "FP1"), new XAttribute("Usage", "Input")));

        root.Add(linkPool);

        // ResourcePool
        var resPool = new XElement(Jdf + "ResourcePool");

        // LayoutPreparationParams
        var lpp = new XElement(Jdf + "LayoutPreparationParams",
            new XAttribute("ID", "LPP1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"),
            new XAttribute("Sides", sides));
        resPool.Add(lpp);

        // RunList
        var runList = new XElement(Jdf + "RunList",
            new XAttribute("ID", "RL1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"));
        if (!string.IsNullOrWhiteSpace(sheet.Pagination))
            runList.Add(new XAttribute("NPage", sheet.Pagination));
        runList.Add(new XElement(Jdf + "LayoutElement",
            new XElement(Jdf + "FileSpec", new XAttribute("URL", pdfFileName))));
        resPool.Add(runList);

        // Media (main)
        resPool.Add(BuildMediaElement("M1", sheet.Media1, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media2))
            resPool.Add(BuildMediaElement("M2", sheet.Media2, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media3))
            resPool.Add(BuildMediaElement("M3", sheet.Media3, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media4))
            resPool.Add(BuildMediaElement("M4", sheet.Media4, null));
        if (!string.IsNullOrWhiteSpace(sheet.MediaCouverture))
            resPool.Add(BuildMediaElement("MC1", sheet.MediaCouverture, "Cover"));

        // DigitalPrintingParams
        var dpp = new XElement(Jdf + "DigitalPrintingParams",
            new XAttribute("ID", "DPP1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"),
            new XAttribute("Collate", "Sheet"));
        resPool.Add(dpp);

        // Component (Output)
        resPool.Add(new XElement(Jdf + "Component",
            new XAttribute("ID", "CO1"),
            new XAttribute("Class", "Quantity"),
            new XAttribute("Status", "Available"),
            new XAttribute("ComponentType", "FinalProduct")));

        // StitchingParams (binding)
        if (HasBinding(sheet))
            resPool.Add(BuildStitchingParams(sheet));

        // FoldingParams
        if (HasFolding(sheet))
            resPool.Add(BuildFoldingParams(sheet));

        root.Add(resPool);

        // NodeInfo
        root.Add(new XElement(Jdf + "NodeInfo",
            new XAttribute("JobPriority", "50")));

        var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        return xdoc.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Generates a PrismaSync Canon JDF (standard CIP4 JDF) ticket.
    /// </summary>
    public static string GeneratePrismaSyncJdf(FabricationSheet sheet, string pdfFileName)
    {
        var jobId = $"GA_{sheet.NumeroDossier ?? "0"}_{DateTime.Now:yyyyMMddHHmmss}";
        var sides = MapSides(sheet.RectoVerso);
        var quantity = sheet.Quantite?.ToString() ?? "1";

        var root = new XElement(Jdf + "JDF",
            new XAttribute("xmlns", "http://www.CIP4.org/JDFSchema_1_1"),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi.NamespaceName),
            new XAttribute("ID", jobId),
            new XAttribute("JobID", sheet.NumeroDossier ?? ""),
            new XAttribute("JobPartID", "p1"),
            new XAttribute("Activation", "Active"),
            new XAttribute("Status", "Ready"),
            new XAttribute("Type", "Combined"),
            new XAttribute("Types", "LayoutPreparation Imposition Interpreting Rendering DigitalPrinting"),
            new XAttribute("Version", "1.3"),
            new XAttribute("DescriptiveName", BuildDescription(sheet)));

        // AuditPool
        root.Add(new XElement(Jdf + "AuditPool",
            new XElement(Jdf + "Created",
                new XAttribute("TimeStamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XAttribute("AgentName", "GestionAtelier"),
                new XAttribute("AgentVersion", "1.0"))));

        // ResourceLinkPool
        var linkPool = new XElement(Jdf + "ResourceLinkPool");
        linkPool.Add(new XElement(Jdf + "LayoutPreparationParamsLink", new XAttribute("rRef", "LPP1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "RunListLink", new XAttribute("rRef", "RL1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "MediaLink", new XAttribute("rRef", "M1"), new XAttribute("Usage", "Input")));
        linkPool.Add(new XElement(Jdf + "ComponentLink", new XAttribute("rRef", "CO1"), new XAttribute("Usage", "Output"), new XAttribute("Amount", quantity)));

        if (HasBinding(sheet))
            linkPool.Add(new XElement(Jdf + "StitchingParamsLink", new XAttribute("rRef", "STP1"), new XAttribute("Usage", "Input")));
        if (HasFolding(sheet))
            linkPool.Add(new XElement(Jdf + "FoldingParamsLink", new XAttribute("rRef", "FP1"), new XAttribute("Usage", "Input")));

        root.Add(linkPool);

        // ResourcePool
        var resPool = new XElement(Jdf + "ResourcePool");

        // LayoutPreparationParams
        var lpp = new XElement(Jdf + "LayoutPreparationParams",
            new XAttribute("ID", "LPP1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"),
            new XAttribute("Sides", sides));
        resPool.Add(lpp);

        // RunList
        var runList = new XElement(Jdf + "RunList",
            new XAttribute("ID", "RL1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"));
        if (!string.IsNullOrWhiteSpace(sheet.Pagination))
            runList.Add(new XAttribute("NPage", sheet.Pagination));
        runList.Add(new XElement(Jdf + "LayoutElement",
            new XElement(Jdf + "FileSpec", new XAttribute("URL", pdfFileName))));
        resPool.Add(runList);

        // Media
        resPool.Add(BuildMediaElement("M1", sheet.Media1, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media2))
            resPool.Add(BuildMediaElement("M2", sheet.Media2, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media3))
            resPool.Add(BuildMediaElement("M3", sheet.Media3, null));
        if (!string.IsNullOrWhiteSpace(sheet.Media4))
            resPool.Add(BuildMediaElement("M4", sheet.Media4, null));
        if (!string.IsNullOrWhiteSpace(sheet.MediaCouverture))
            resPool.Add(BuildMediaElement("MC1", sheet.MediaCouverture, "Cover"));

        // Component (Output)
        resPool.Add(new XElement(Jdf + "Component",
            new XAttribute("ID", "CO1"),
            new XAttribute("Class", "Quantity"),
            new XAttribute("Status", "Available"),
            new XAttribute("ComponentType", "FinalProduct")));

        // StitchingParams
        if (HasBinding(sheet))
            resPool.Add(BuildStitchingParams(sheet));

        // FoldingParams
        if (HasFolding(sheet))
            resPool.Add(BuildFoldingParams(sheet));

        // Comments (additional info for PrismaSync)
        if (!string.IsNullOrWhiteSpace(sheet.TypeTravail))
            root.Add(new XElement(Jdf + "Comment", new XAttribute("Name", "TypeTravail"), sheet.TypeTravail));
        if (!string.IsNullOrWhiteSpace(sheet.Client))
            root.Add(new XElement(Jdf + "Comment", new XAttribute("Name", "Client"), sheet.Client));

        root.Add(resPool);

        var xdoc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        return xdoc.ToString(SaveOptions.None);
    }

    private static string BuildDescription(FabricationSheet sheet)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(sheet.NumeroDossier)) parts.Add(sheet.NumeroDossier);
        if (!string.IsNullOrWhiteSpace(sheet.Client)) parts.Add(sheet.Client);
        if (!string.IsNullOrWhiteSpace(sheet.TypeTravail)) parts.Add(sheet.TypeTravail);
        return parts.Count > 0 ? string.Join(" - ", parts) : "GestionAtelier Job";
    }

    private static string MapSides(string? rectoVerso)
    {
        if (string.IsNullOrWhiteSpace(rectoVerso)) return "OneSidedFront";
        var lower = rectoVerso.ToLowerInvariant();
        if (lower.Contains("recto/verso") || lower.Contains("recto verso") || lower == "r/v")
            return "TwoSidedFlipY";
        return "OneSidedFront";
    }

    private static XElement BuildMediaElement(string id, string? mediaName, string? productType)
    {
        var media = new XElement(Jdf + "Media",
            new XAttribute("ID", id),
            new XAttribute("Class", "Consumable"),
            new XAttribute("Status", "Available"));

        if (!string.IsNullOrWhiteSpace(mediaName))
            media.Add(new XAttribute("DescriptiveName", mediaName));

        if (!string.IsNullOrWhiteSpace(productType))
            media.Add(new XAttribute("ProductType", productType));

        // Try to extract grammage from media name (e.g. "Couché 150g" → Weight=150)
        var match = System.Text.RegularExpressions.Regex.Match(mediaName ?? "", @"(\d+)\s*g");
        if (match.Success)
            media.Add(new XAttribute("Weight", match.Groups[1].Value));

        return media;
    }

    private static bool HasBinding(FabricationSheet sheet)
    {
        return !string.IsNullOrWhiteSpace(sheet.FaconnageBinding)
            && !sheet.FaconnageBinding.Equals("Aucune", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFolding(FabricationSheet sheet)
    {
        return !string.IsNullOrWhiteSpace(sheet.Plis);
    }

    private static XElement BuildStitchingParams(FabricationSheet sheet)
    {
        var stp = new XElement(Jdf + "StitchingParams",
            new XAttribute("ID", "STP1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"));

        var binding = (sheet.FaconnageBinding ?? "").ToLowerInvariant();
        if (binding.Contains("piqûre") || binding.Contains("piqure") || binding.Contains("2 points"))
        {
            stp.Add(new XAttribute("StitchType", "Saddle"));
            stp.Add(new XAttribute("NumberOfStitches", "2"));
        }
        else if (binding.Contains("spirale") || binding.Contains("wire"))
        {
            stp.Add(new XAttribute("StitchType", "Side"));
            stp.Add(new XAttribute("NumberOfStitches", "2"));
        }
        else if (binding.Contains("dos carré") || binding.Contains("collé"))
        {
            stp.Add(new XAttribute("NoOp", "true"));
        }

        return stp;
    }

    private static XElement BuildFoldingParams(FabricationSheet sheet)
    {
        var fp = new XElement(Jdf + "FoldingParams",
            new XAttribute("ID", "FP1"),
            new XAttribute("Class", "Parameter"),
            new XAttribute("Status", "Available"));

        var pli = (sheet.Plis ?? "").ToLowerInvariant();
        if (pli.Contains("accordéon") || pli.Contains("accordeon"))
            fp.Add(new XAttribute("FoldCatalog", "F6-1"));
        else if (pli.Contains("roulé") || pli.Contains("roule"))
            fp.Add(new XAttribute("FoldCatalog", "F6-2"));
        else if (pli.Contains("fenêtre") || pli.Contains("fenetre"))
            fp.Add(new XAttribute("FoldCatalog", "F8-4"));
        else
            fp.Add(new XAttribute("FoldCatalog", "F4-1"));

        return fp;
    }
}
