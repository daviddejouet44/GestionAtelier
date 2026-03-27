export default function Badge({ children, variant = "default" }) {
  return <span className={`badge badge-${variant}`}>{children}</span>;
}

export function StatutBadge({ statut }) {
  const map = {
    planifie: { label: "Planifié", variant: "info" },
    en_cours: { label: "En cours", variant: "warning" },
    termine: { label: "Terminé", variant: "success" },
    annule: { label: "Annulé", variant: "danger" },
    en_attente: { label: "En attente", variant: "default" },
  };
  const s = map[statut] || { label: statut, variant: "default" };
  return <span className={`badge badge-${s.variant}`}>{s.label}</span>;
}

export function EtatMachineBadge({ etat }) {
  const map = {
    operationnelle: { label: "Opérationnelle", variant: "success" },
    maintenance: { label: "Maintenance", variant: "warning" },
    panne: { label: "En panne", variant: "danger" },
    arret: { label: "Arrêt", variant: "default" },
  };
  const e = map[etat] || { label: etat, variant: "default" };
  return <span className={`badge badge-${e.variant}`}>{e.label}</span>;
}

export function TypeMachineBadge({ type }) {
  const map = {
    offset: { label: "Offset", variant: "primary" },
    numerique: { label: "Numérique", variant: "purple" },
  };
  const t = map[type] || { label: type, variant: "default" };
  return <span className={`badge badge-${t.variant}`}>{t.label}</span>;
}

export function StockAlertBadge({ stock, stockMin }) {
  if (stock <= 0) return <span className="badge badge-danger">Rupture</span>;
  if (stock <= stockMin) return <span className="badge badge-warning">Stock bas</span>;
  return <span className="badge badge-success">OK</span>;
}
