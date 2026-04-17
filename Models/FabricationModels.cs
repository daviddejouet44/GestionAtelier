using System.Text.Json.Serialization;

namespace GestionAtelier.Models;

public record FabricationHistory
{
    public DateTime Date { get; init; }
    public string User { get; init; } = "David";
    public string Action { get; init; } = "";
}

public record RepartitionItem
{
    public int? Quantite { get; init; }
    public string? Adresse { get; init; }
}

public record FabricationSheet
{
    public string FullPath { get; init; } = default!;
    public string FileName { get; set; } = default!;
    public string? Machine { get; init; }
    public string? MoteurImpression { get; init; }
    public string? Operateur { get; init; }
    public int? Quantite { get; init; }
    public string? TypeTravail { get; init; }
    public string? Format { get; init; }
    public string? Papier { get; init; }
    public string? RectoVerso { get; init; }
    public string? FormeDecoupe { get; init; }
    public string? Encres { get; init; }
    public string? Client { get; init; }
    public string? NumeroAffaire { get; init; }
    public string? NumeroDossier { get; init; }
    public string? Notes { get; init; }
    public DateTime? Delai { get; init; }
    public string? Media1 { get; init; }
    public string? Media2 { get; init; }
    public string? Media3 { get; init; }
    public string? Media4 { get; init; }
    public string? TypeDocument { get; init; }
    public int? NombreFeuilles { get; init; }
    public List<string>? Faconnage { get; init; }
    public string? Livraison { get; init; }
    public string? Bat { get; init; }
    public string? RetraitLivraison { get; init; }
    public string? AdresseLivraison { get; init; }
    public List<FabricationHistory> History { get; init; } = new();

    // ── Donneur d'ordre ──────────────────────────────────
    public string? DonneurOrdreNom { get; init; }
    public string? DonneurOrdrePrenom { get; init; }
    public string? DonneurOrdreTelephone { get; init; }
    public string? DonneurOrdreEmail { get; init; }

    // ── Pagination ───────────────────────────────────────
    public string? Pagination { get; init; }

    // ── Format feuille en machine ────────────────────────
    public string? FormatFeuille { get; init; }

    // ── Fabricants des médias ────────────────────────────
    public string? Media1Fabricant { get; init; }
    public string? Media2Fabricant { get; init; }
    public string? Media3Fabricant { get; init; }
    public string? Media4Fabricant { get; init; }

    // ── Couverture ───────────────────────────────────────
    public string? MediaCouverture { get; init; }
    public string? MediaCouvertureFabricant { get; init; }

    // ── Rainage ──────────────────────────────────────────
    public bool? Rainage { get; init; }

    // ── Ennoblissement ───────────────────────────────────
    public List<string>? Ennoblissement { get; init; }

    // ── Façonnage (sélection unique, type de reliure) ────
    public string? FaconnageBinding { get; init; }

    // ── Plis ─────────────────────────────────────────────
    public string? Plis { get; init; }

    // ── Sortie ───────────────────────────────────────────
    public string? Sortie { get; init; }

    // ── Mails importés ───────────────────────────────────
    public string? MailDevisFileName { get; init; }
    public string? MailBatFileName { get; init; }

    // ── Dates ────────────────────────────────────────────
    public DateTime? DateDepart { get; init; }
    public DateTime? DateLivraison { get; init; }
    public DateTime? PlanningMachine { get; init; }

    // ── Dates clés (calculées depuis DateReception) ──────
    public DateTime? DateReception { get; init; }
    public DateTime? DateEnvoi { get; init; }
    public DateTime? DateProductionFinitions { get; init; }
    public DateTime? DateImpression { get; init; }

    // ── Temps théorique de production (minutes) ──────────
    public int? TempsProduitMinutes { get; init; }

    // ── Justifs clients ──────────────────────────────────
    public int? JustifsClientsQuantite { get; init; }
    public string? JustifsClientsAdresse { get; init; }

    // ── Répartitions ─────────────────────────────────────
    public List<RepartitionItem>? Repartitions { get; init; }

    // ── Étapes finitions ─────────────────────────────────
    public FinitionSteps? FinitionSteps { get; init; }

    // ── Statut de production (validé/refusé par opérateur) ──
    public string? StatutProduction { get; init; }
}

public class FabricationInput
{
    public string FullPath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? Machine { get; set; }
    public string? MoteurImpression { get; set; }
    public string? Operateur { get; set; }
    public int? Quantite { get; set; }
    public string? TypeTravail { get; set; }
    public string? Format { get; set; }
    public string? Papier { get; set; }
    public string? RectoVerso { get; set; }
    public string? FormeDecoupe { get; set; }
    public string? Encres { get; set; }
    public string? Client { get; set; }
    public string? NumeroAffaire { get; set; }
    public string? NumeroDossier { get; set; }
    public string? Notes { get; set; }
    public DateTime? Delai { get; set; }
    public string? Media1 { get; set; }
    public string? Media2 { get; set; }
    public string? Media3 { get; set; }
    public string? Media4 { get; set; }
    public string? TypeDocument { get; set; }
    public int? NombreFeuilles { get; set; }
    public List<string>? Faconnage { get; set; }
    public string? Livraison { get; set; }
    public string? Bat { get; set; }
    public string? RetraitLivraison { get; set; }
    public string? AdresseLivraison { get; set; }

    // ── Donneur d'ordre ──────────────────────────────────
    public string? DonneurOrdreNom { get; set; }
    public string? DonneurOrdrePrenom { get; set; }
    public string? DonneurOrdreTelephone { get; set; }
    public string? DonneurOrdreEmail { get; set; }

    // ── Pagination ───────────────────────────────────────
    public string? Pagination { get; set; }

    // ── Format feuille en machine ────────────────────────
    public string? FormatFeuille { get; set; }

    // ── Fabricants des médias ────────────────────────────
    public string? Media1Fabricant { get; set; }
    public string? Media2Fabricant { get; set; }
    public string? Media3Fabricant { get; set; }
    public string? Media4Fabricant { get; set; }

    // ── Couverture ───────────────────────────────────────
    public string? MediaCouverture { get; set; }
    public string? MediaCouvertureFabricant { get; set; }

    // ── Rainage ──────────────────────────────────────────
    public bool? Rainage { get; set; }

    // ── Ennoblissement ───────────────────────────────────
    public List<string>? Ennoblissement { get; set; }

    // ── Façonnage (sélection unique, type de reliure) ────
    public string? FaconnageBinding { get; set; }

    // ── Plis ─────────────────────────────────────────────
    public string? Plis { get; set; }

    // ── Sortie ───────────────────────────────────────────
    public string? Sortie { get; set; }

    // ── Mails importés ───────────────────────────────────
    public string? MailDevisFileName { get; set; }
    public string? MailBatFileName { get; set; }

    // ── Dates ────────────────────────────────────────────
    public DateTime? DateDepart { get; set; }
    public DateTime? DateLivraison { get; set; }
    public DateTime? PlanningMachine { get; set; }

    // ── Dates clés (calculées depuis DateReception) ──────
    public DateTime? DateReception { get; set; }
    public DateTime? DateEnvoi { get; set; }
    public DateTime? DateProductionFinitions { get; set; }
    public DateTime? DateImpression { get; set; }

    // ── Temps théorique de production (minutes) ──────────
    public int? TempsProduitMinutes { get; set; }

    // ── Justifs clients ──────────────────────────────────
    public int? JustifsClientsQuantite { get; set; }
    public string? JustifsClientsAdresse { get; set; }

    // ── Répartitions ─────────────────────────────────────
    public List<RepartitionItem>? Repartitions { get; set; }
}

// ======================================================
// Finition Steps
// ======================================================

public class FinitionStep
{
    public bool Done { get; set; } = false;
    public DateTime? DoneAt { get; set; }
    public string? DoneBy { get; set; }
    public string? Conditionnement { get; set; }  // for emballage
    public string? Tracking { get; set; }          // for livraison
}

public class FinitionSteps
{
    public FinitionStep Embellissement { get; set; } = new();
    public FinitionStep Rainage { get; set; } = new();
    public FinitionStep Pliage { get; set; } = new();
    public FinitionStep Faconnage { get; set; } = new();
    public FinitionStep Coupe { get; set; } = new();
    public FinitionStep Emballage { get; set; } = new();
    public FinitionStep Depart { get; set; } = new();
    public FinitionStep Livraison { get; set; } = new();
}

// ======================================================
// Settings / Log types
// ======================================================

