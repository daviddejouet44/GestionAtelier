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
                    hdr.Item().AlignCenter().Text("Fiche de Fabrication").FontSize(24).SemiBold();
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
                        Row("Date de livraison", s.Delai.HasValue ? s.Delai.Value.ToString("dd/MM/yyyy") : "—");
                        Row("Nom du fichier", s.FileName);
                        Row("Opérateur", s.Operateur);
                    });

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
                            if (!string.IsNullOrWhiteSpace(s.Media1)) Row("Média 1", s.Media1);
                            if (!string.IsNullOrWhiteSpace(s.Media2)) Row("Média 2", s.Media2);
                            if (!string.IsNullOrWhiteSpace(s.Media3)) Row("Média 3", s.Media3);
                            if (!string.IsNullOrWhiteSpace(s.Media4)) Row("Média 4", s.Media4);
                        });
                        col.Item().PaddingVertical(10).LineHorizontal(1).LineColor("#d1d5db");
                    }

                    // ── Façonnage ────────────────────────────────────────
                    col.Item().PaddingBottom(4).Text("Façonnage").FontSize(13).SemiBold();
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
