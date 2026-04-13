using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class PdfUtils
{
    public static Document CreateFabricationPdf(FabricationSheet s)
    {
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
                        // Barcode / identifiant visuel
                        row.ConstantItem(120).AlignRight().Column(bc =>
                        {
                            if (!string.IsNullOrWhiteSpace(s.NumeroDossier))
                            {
                                bc.Item().Border(2).BorderColor("#1a1a2e").Padding(6).AlignCenter()
                                    .Text(s.NumeroDossier).FontSize(10).SemiBold().FontFamily("Courier New");
                                bc.Item().AlignCenter().Text("◼◻◼◻◼◻◼◻◼◻◼◻◼◻◼◻◼◻◼◻").FontSize(6).FontColor("#1a1a2e");
                            }
                        });
                    });
                    if (!string.IsNullOrWhiteSpace(s.NumeroDossier))
                        hdr.Item().AlignCenter().Text(s.NumeroDossier).FontSize(16).SemiBold();
                    hdr.Item().PaddingVertical(6).LineHorizontal(2).LineColor("#1a1a2e");
                });

                page.Content().Column(col =>
                {
                    // ── Informations générales ──────────────────────────
                    col.Item().PaddingBottom(8).Text("Informations générales").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val, bool bold = false)
                        {
                            table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                            var v = table.Cell().PaddingBottom(4).Text(val ?? "—");
                            if (bold) v.SemiBold();
                        }
                        Row("Client", s.Client);
                        Row("Date de création", creationDate.ToString("dd/MM/yyyy"));
                        Row("N° de dossier", s.NumeroDossier, bold: true);
                        Row("Délai", s.Delai.HasValue ? s.Delai.Value.ToString("dd/MM/yyyy") : "—");
                        Row("Nom du fichier", s.FileName);
                        Row("Opérateur", s.Operateur);
                    });

                    // ── Donneur d'ordre ──────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.DonneurOrdreNom) || !string.IsNullOrWhiteSpace(s.DonneurOrdrePrenom)
                        || !string.IsNullOrWhiteSpace(s.DonneurOrdreTelephone) || !string.IsNullOrWhiteSpace(s.DonneurOrdreEmail))
                    {
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                        col.Item().PaddingBottom(8).Text("Donneur d'ordre").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val)
                            {
                                table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                                table.Cell().PaddingBottom(4).Text(val ?? "—");
                            }
                            Row("Nom", s.DonneurOrdreNom);
                            Row("Prénom", s.DonneurOrdrePrenom);
                            Row("Téléphone", s.DonneurOrdreTelephone);
                            Row("Email", s.DonneurOrdreEmail);
                        });
                    }

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Données de production ──────────────────────────
                    col.Item().PaddingBottom(8).Text("Données de production").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val)
                        {
                            table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10);
                            table.Cell().PaddingBottom(4).Text(val ?? "—");
                        }
                        Row("Moteur d'impression", s.MoteurImpression ?? s.Machine);
                        Row("Quantité", s.Quantite.HasValue ? s.Quantite.Value.ToString("N0") : "—");
                        Row("Type de travail", s.TypeTravail);
                        Row("Format fini", s.Format);
                        Row("Recto/Verso", s.RectoVerso);
                        Row("Type document", s.TypeDocument);
                        Row("Nombre de feuilles", s.NombreFeuilles.HasValue ? s.NombreFeuilles.Value.ToString() : "—");
                        Row("N° affaire", s.NumeroAffaire);
                        if (!string.IsNullOrWhiteSpace(s.Pagination)) Row("Pagination", s.Pagination);
                        if (!string.IsNullOrWhiteSpace(s.FormatFeuille)) Row("Format feuille en machine", s.FormatFeuille);
                        if (s.DateDepart.HasValue) Row("Date de départ", s.DateDepart.Value.ToString("dd/MM/yyyy"));
                        if (s.DateLivraison.HasValue) Row("Date de livraison", s.DateLivraison.Value.ToString("dd/MM/yyyy"));
                        if (s.PlanningMachine.HasValue) Row("Planning machine", s.PlanningMachine.Value.ToString("dd/MM/yyyy HH:mm"));
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── BAT ──────────────────────────────────────────────
                    col.Item().PaddingBottom(8).Text("BAT").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                        Row("BAT", s.Bat);
                        if (!string.IsNullOrWhiteSpace(s.MailBatFileName)) Row("Mail validation BAT", s.MailBatFileName);
                        if (!string.IsNullOrWhiteSpace(s.MailDevisFileName)) Row("Mail validation devis", s.MailDevisFileName);
                    });

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Médias ──────────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.Media1) || !string.IsNullOrWhiteSpace(s.Media2) ||
                        !string.IsNullOrWhiteSpace(s.Media3) || !string.IsNullOrWhiteSpace(s.Media4))
                    {
                        col.Item().PaddingBottom(8).Text("Médias / Support").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                            if (!string.IsNullOrWhiteSpace(s.Media1)) { Row("Média 1", s.Media1); Row("Fabricant 1", s.Media1Fabricant); }
                            if (!string.IsNullOrWhiteSpace(s.Media2)) { Row("Média 2", s.Media2); Row("Fabricant 2", s.Media2Fabricant); }
                            if (!string.IsNullOrWhiteSpace(s.Media3)) { Row("Média 3", s.Media3); Row("Fabricant 3", s.Media3Fabricant); }
                            if (!string.IsNullOrWhiteSpace(s.Media4)) { Row("Média 4", s.Media4); Row("Fabricant 4", s.Media4Fabricant); }
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Couverture ──────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.MediaCouverture))
                    {
                        col.Item().PaddingBottom(8).Text("Couverture").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                            Row("Média couverture", s.MediaCouverture);
                            Row("Fabricant couverture", s.MediaCouvertureFabricant);
                            Row("Rainage", s.Rainage == true ? "Oui" : (s.Rainage == false ? "Non" : "—"));
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }
                    else if (s.Rainage.HasValue)
                    {
                        col.Item().PaddingBottom(4).Text("Rainage").FontSize(13).SemiBold();
                        col.Item().PaddingBottom(8).Text(s.Rainage == true ? "Oui" : "Non");
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Ennoblissement ───────────────────────────────────
                    if (s.Ennoblissement != null && s.Ennoblissement.Count > 0)
                    {
                        col.Item().PaddingBottom(4).Text("Ennoblissement").FontSize(13).SemiBold();
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            foreach (var e in s.Ennoblissement)
                            {
                                row.AutoItem().Border(1).BorderColor("#d1d5db").Padding(4).Text("✓ " + e).FontSize(10);
                                row.AutoItem().Width(6);
                            }
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Façonnage (options multi) ─────────────────────────
                    col.Item().PaddingBottom(4).Text("Façonnage").FontSize(13).SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                        void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                        if (!string.IsNullOrWhiteSpace(s.FaconnageBinding)) Row("Type de reliure", s.FaconnageBinding);
                        if (!string.IsNullOrWhiteSpace(s.Plis)) Row("Plis", s.Plis);
                        if (!string.IsNullOrWhiteSpace(s.Sortie)) Row("Sortie", s.Sortie);
                    });
                    if (s.Faconnage != null && s.Faconnage.Count > 0)
                    {
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            foreach (var f in s.Faconnage)
                            {
                                row.AutoItem().Border(1).BorderColor("#d1d5db").Padding(4).Text("✓ " + f).FontSize(10);
                                row.AutoItem().Width(6);
                            }
                        });
                    }
                    else
                    {
                        col.Item().PaddingBottom(8).Text("Aucun façonnage sélectionné").FontColor("#9ca3af").FontSize(10);
                    }

                    col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");

                    // ── Livraison ────────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.RetraitLivraison) || !string.IsNullOrWhiteSpace(s.AdresseLivraison))
                    {
                        col.Item().PaddingBottom(8).Text("Livraison").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                            Row("Mode", s.RetraitLivraison);
                            Row("Adresse", s.AdresseLivraison);
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Justifs clients ──────────────────────────────────
                    if (s.JustifsClientsQuantite.HasValue || !string.IsNullOrWhiteSpace(s.JustifsClientsAdresse))
                    {
                        col.Item().PaddingBottom(8).Text("Justificatifs clients").FontSize(13).SemiBold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2); });
                            void Row(string lbl, string? val) { table.Cell().PaddingBottom(4).Text(lbl + " :").FontColor("#6b7280").FontSize(10); table.Cell().PaddingBottom(4).Text(val ?? "—"); }
                            Row("Quantité", s.JustifsClientsQuantite.HasValue ? s.JustifsClientsQuantite.Value.ToString() : null);
                            Row("Adresse", s.JustifsClientsAdresse);
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Répartitions ─────────────────────────────────────
                    if (s.Repartitions != null && s.Repartitions.Count > 0)
                    {
                        col.Item().PaddingBottom(8).Text("Répartitions et quantités").FontSize(13).SemiBold();
                        col.Item().Table(table =>
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
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Observations ────────────────────────────────────
                    if (!string.IsNullOrWhiteSpace(s.Notes))
                    {
                        col.Item().PaddingBottom(4).Text("Observations / Notes").FontSize(13).SemiBold();
                        col.Item().PaddingBottom(8).Border(1).BorderColor("#e5e7eb").Padding(8).Text(s.Notes).FontSize(10);
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Historique ───────────────────────────────────────
                    if (historyOrdered.Count > 0)
                    {
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
}
