using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QRCoder;
using GestionAtelier.Models;
using GestionAtelier.Endpoints.Settings;

namespace GestionAtelier.Services;

public static class PdfUtils
{
    public static Document CreateFabricationPdf(FabricationSheet s, FabricationFormConfig? formConfig = null)
    {
        var config = formConfig ?? FormConfigEndpoints.DefaultConfig;

        // Determine creation date from history (first entry) or now
        var historyOrdered = s.History.OrderBy(h => h.Date).ToList();
        var creationDate = historyOrdered.FirstOrDefault()?.Date ?? DateTime.Now;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(hdr =>
                {
                    hdr.Item().Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Text("Fiche de Fabrication").FontSize(24).SemiBold();
                        // QR Code pointing to finitions page
                        row.ConstantItem(80).AlignRight().Column(bc =>
                        {
                            var qrValue = $"/pro/finitions.html?job={Uri.EscapeDataString(s.FileName ?? s.NumeroDossier ?? "")}";
                            try
                            {
                                using var qrGenerator = new QRCodeGenerator();
                                var qrData = qrGenerator.CreateQrCode(qrValue, QRCodeGenerator.ECCLevel.M);
                                using var qrCode = new PngByteQRCode(qrData);
                                var qrBytes = qrCode.GetGraphic(4);
                                bc.Item().Width(70).Height(70).Image(qrBytes);
                            }
                            catch { /* QR code generation failure is non-fatal */ }
                        });
                    });
                    if (!string.IsNullOrWhiteSpace(s.NumeroDossier))
                        hdr.Item().AlignCenter().Text(s.NumeroDossier).FontSize(16).SemiBold();
                    hdr.Item().PaddingVertical(6).LineHorizontal(2).LineColor("#1a1a2e");
                });

                page.Content().Column(col =>
                {
                    // Iterate sections in configured order
                    var sectionOrder = config.Sections.Count > 0 ? config.Sections : new List<string>
                    {
                        "Informations générales","Donneur d'ordre","Impression","Media","BAT",
                        "Finitions","Production","Passes","Livraison","Notes"
                    };

                    foreach (var section in sectionOrder)
                    {
                        var sectionFields = config.Fields
                            .Where(f => f.Section == section && f.Visible)
                            .OrderBy(f => f.Order)
                            .ToList();

                        if (sectionFields.Count == 0) continue;

                        bool sectionHasContent = false;

                        // Check if section has any printable content
                        foreach (var f in sectionFields)
                        {
                            if (GetFieldPdfValue(s, f.Id) != null) { sectionHasContent = true; break; }
                        }
                        if (!sectionHasContent) continue;

                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                        col.Item().PaddingBottom(8).Text(section).FontSize(13).SemiBold();

                        // Special rendering for multiselect / group / calculated/passes
                        if (section == "Finitions")
                        {
                            RenderFinitionsSection(col, s, sectionFields);
                        }
                        else if (section == "Passes")
                        {
                            // Passes are informational — skip in PDF (no stored value)
                        }
                        else if (section == "Livraison")
                        {
                            RenderLivraisonSection(col, s, sectionFields);
                        }
                        else
                        {
                            // Generic two-column table rendering
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(1); c.RelativeColumn(2);
                                    c.RelativeColumn(1); c.RelativeColumn(2);
                                });
                                void Row(string lbl, string? val, bool bold = false)
                                {
                                    table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                                    var v = table.Cell().PaddingBottom(4).Text(val ?? "—");
                                    if (bold) v.SemiBold();
                                }
                                foreach (var f in sectionFields)
                                {
                                    var val = GetFieldPdfValue(s, f.Id);
                                    if (val != null)
                                        Row(f.Label, val, f.Id == "numeroDossier");
                                }
                            });
                        }
                    }

                    // ── Historique — always shown ─────────────────────────
                    if (historyOrdered.Count > 0)
                    {
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                        col.Item().PaddingBottom(4).Text("Historique").FontSize(13).SemiBold();
                        foreach (var h in historyOrdered)
                            col.Item().Text($"{h.Date:dd/MM/yyyy HH:mm} — {h.User} — {h.Action}").FontSize(9).FontColor("#6b7280");
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span($"Généré le {DateTime.Now:dd/MM/yyyy à HH:mm} — ").FontSize(8).FontColor("#9ca3af");
                    t.Span("Gestion d'Atelier").FontSize(8).FontColor("#9ca3af").SemiBold();
                });
            });
        });
    }

    private static void RenderFinitionsSection(ColumnDescriptor col, FabricationSheet s, List<FormFieldConfig> fields)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }

            foreach (var f in fields)
            {
                if (f.Id == "ennoblissement" || f.Id == "faconnage") continue; // rendered separately
                var v = GetFieldPdfValue(s, f.Id);
                if (v != null) Row(f.Label, v);
            }
        });

        if (fields.Any(f => f.Id == "ennoblissement") && s.Ennoblissement != null && s.Ennoblissement.Count > 0)
        {
            col.Item().PaddingTop(4).Row(row =>
            {
                foreach (var e in s.Ennoblissement)
                {
                    row.AutoItem().Border(1).BorderColor("#d1d5db").Padding(4).Text("✓ " + e).FontSize(10);
                    row.AutoItem().Width(6);
                }
            });
        }
    }

    private static void RenderLivraisonSection(ColumnDescriptor col, FabricationSheet s, List<FormFieldConfig> fields)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
            foreach (var f in fields.Where(f => f.Id != "repartitions"))
            {
                var v = GetFieldPdfValue(s, f.Id);
                if (v != null) Row(f.Label, v);
            }
        });

        if (fields.Any(f => f.Id == "repartitions") && s.Repartitions != null && s.Repartitions.Count > 0)
        {
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(3); });
                table.Header(h =>
                {
                    h.Cell().Background("#f3f4f6").Padding(4).Text("Quantité").FontSize(10).SemiBold();
                    h.Cell().Background("#f3f4f6").Padding(4).Text("Adresse").FontSize(10).SemiBold();
                });
                foreach (var r in s.Repartitions)
                {
                    table.Cell().Padding(4).Text(r.Quantite.HasValue ? r.Quantite.Value.ToString() : "—").FontSize(10);
                    table.Cell().Padding(4).Text(r.Adresse ?? "—").FontSize(10);
                }
            });
        }
    }

    /// <summary>Returns a printable string value for the given field ID, or null if empty/not applicable.</summary>
    private static string? GetFieldPdfValue(FabricationSheet s, string fieldId) => fieldId switch
    {
        "numeroDossier"      => s.NumeroDossier,
        "client"             => s.Client,
        "operateur"          => s.Operateur,
        "delai"              => s.Delai.HasValue ? s.Delai.Value.ToString("dd/MM/yyyy") : null,
        "typeTravail"        => s.TypeTravail,
        "formatFini"         => s.Format,
        "quantite"           => s.Quantite.HasValue ? s.Quantite.Value.ToString("N0") : null,
        "moteurImpression"   => s.MoteurImpression ?? s.Machine,
        "donneurOrdreNom"    => s.DonneurOrdreNom,
        "donneurOrdrePrenom" => s.DonneurOrdrePrenom,
        "donneurOrdreTelephone" => s.DonneurOrdreTelephone,
        "donneurOrdreEmail"  => s.DonneurOrdreEmail,
        "rectoVerso"         => s.RectoVerso,
        "formeDecoupe"       => s.FormeDecoupe,
        "pagination"         => s.Pagination,
        "formatFeuilleMachine" => s.FormatFeuille,
        "media1"             => s.Media1,
        "media1Fabricant"    => s.Media1Fabricant,
        "media2"             => s.Media2,
        "media2Fabricant"    => s.Media2Fabricant,
        "media3"             => s.Media3,
        "media3Fabricant"    => s.Media3Fabricant,
        "media4"             => s.Media4,
        "media4Fabricant"    => s.Media4Fabricant,
        "couvertureMedia"    => s.MediaCouverture,
        "couvertureFabricant"=> s.MediaCouvertureFabricant,
        "bat"                => s.Bat,
        "mailValidationBat"  => s.MailBatFileName,
        "mailValidationDevis"=> s.MailDevisFileName,
        "rainage"            => s.Rainage.HasValue ? (s.Rainage.Value ? "Oui" : "Non") : null,
        "ennoblissement"     => s.Ennoblissement != null && s.Ennoblissement.Count > 0 ? string.Join(", ", s.Ennoblissement) : null,
        "faconnageBinding"   => s.FaconnageBinding,
        "plis"               => s.Plis,
        "sortie"             => s.Sortie,
        "nombreFeuilles"     => s.NombreFeuilles.HasValue ? s.NombreFeuilles.Value.ToString() : null,
        "dateDepart"         => s.DateDepart.HasValue ? s.DateDepart.Value.ToString("dd/MM/yyyy") : null,
        "dateLivraison"      => s.DateLivraison.HasValue ? s.DateLivraison.Value.ToString("dd/MM/yyyy") : null,
        "planningMachine"    => s.PlanningMachine.HasValue ? s.PlanningMachine.Value.ToString("dd/MM/yyyy HH:mm") : null,
        "retraitLivraison"   => s.RetraitLivraison,
        "adresseLivraison"   => s.AdresseLivraison,
        "justifsQuantite"    => s.JustifsClientsQuantite.HasValue ? s.JustifsClientsQuantite.Value.ToString() : null,
        "justifsAdresse"     => s.JustifsClientsAdresse,
        "notes"              => s.Notes,
        _                    => null
    };
}
