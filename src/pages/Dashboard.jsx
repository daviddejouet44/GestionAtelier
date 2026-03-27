import { useApp } from "../context/useApp";
import {
  Users,
  Printer,
  FileText,
  Package,
  AlertTriangle,
  TrendingUp,
  Clock,
  CheckCircle,
} from "lucide-react";

function KpiCard({ icon, label, value, sub, color }) {
  const Icon = icon;
  return (
    <div className="kpi-card">
      <div className={`kpi-icon kpi-icon-${color}`}>
        <Icon size={24} />
      </div>
      <div className="kpi-body">
        <div className="kpi-value">{value}</div>
        <div className="kpi-label">{label}</div>
        {sub && <div className="kpi-sub">{sub}</div>}
      </div>
    </div>
  );
}

function StatRow({ label, value, total, color }) {
  const pct = total > 0 ? Math.round((value / total) * 100) : 0;
  return (
    <div className="stat-row">
      <span className="stat-label">{label}</span>
      <div className="stat-bar-wrap">
        <div
          className={`stat-bar stat-bar-${color}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="stat-value">{value}</span>
    </div>
  );
}

export default function Dashboard() {
  const { state } = useApp();
  const { clients, machines, travaux, inventaire } = state;

  const travauxEnCours = travaux.filter((t) => t.statut === "en_cours").length;
  const travauxTermines = travaux.filter((t) => t.statut === "termine").length;
  const travauxPlanifies = travaux.filter((t) => t.statut === "planifie").length;

  const machinesOK = machines.filter((m) => m.etat === "operationnelle").length;
  const machinesMaint = machines.filter((m) => m.etat === "maintenance").length;
  const machinesPanne = machines.filter((m) => m.etat === "panne").length;

  const articlesStockBas = inventaire.filter(
    (a) => a.stock <= a.stockMin
  ).length;

  const caTotal = travaux
    .filter((t) => t.statut !== "annule")
    .reduce((s, t) => s + (t.montantTotal || 0), 0);

  const travRecentsTrie = [...travaux]
    .sort((a, b) => new Date(b.dateCreation) - new Date(a.dateCreation))
    .slice(0, 5);

  function clientNom(id) {
    return clients.find((c) => c.id === id)?.nom || "–";
  }

  return (
    <div className="page">
      <h1 className="page-title">Tableau de bord</h1>

      <div className="kpi-grid">
        <KpiCard
          icon={FileText}
          label="Travaux en cours"
          value={travauxEnCours}
          sub={`${travauxPlanifies} planifiés`}
          color="blue"
        />
        <KpiCard
          icon={CheckCircle}
          label="Travaux terminés"
          value={travauxTermines}
          color="green"
        />
        <KpiCard
          icon={Printer}
          label="Machines opérationnelles"
          value={`${machinesOK}/${machines.length}`}
          sub={machinesMaint > 0 ? `${machinesMaint} en maintenance` : undefined}
          color="purple"
        />
        <KpiCard
          icon={Users}
          label="Clients actifs"
          value={clients.filter((c) => c.actif).length}
          color="orange"
        />
        <KpiCard
          icon={Package}
          label="Alertes stock"
          value={articlesStockBas}
          sub="articles sous seuil"
          color={articlesStockBas > 0 ? "red" : "green"}
        />
        <KpiCard
          icon={TrendingUp}
          label="CA prévisionnel"
          value={`${caTotal.toLocaleString("fr-FR")} €`}
          color="teal"
        />
      </div>

      <div className="dashboard-row">
        <div className="card">
          <h2 className="card-title">État des travaux</h2>
          <div className="stat-rows">
            <StatRow
              label="En cours"
              value={travauxEnCours}
              total={travaux.length}
              color="blue"
            />
            <StatRow
              label="Planifiés"
              value={travauxPlanifies}
              total={travaux.length}
              color="orange"
            />
            <StatRow
              label="Terminés"
              value={travauxTermines}
              total={travaux.length}
              color="green"
            />
          </div>
        </div>

        <div className="card">
          <h2 className="card-title">État des machines</h2>
          <div className="stat-rows">
            <StatRow
              label="Opérationnelles"
              value={machinesOK}
              total={machines.length}
              color="green"
            />
            <StatRow
              label="En maintenance"
              value={machinesMaint}
              total={machines.length}
              color="orange"
            />
            <StatRow
              label="En panne"
              value={machinesPanne}
              total={machines.length}
              color="red"
            />
          </div>
        </div>
      </div>

      {articlesStockBas > 0 && (
        <div className="card alert-card">
          <div className="alert-header">
            <AlertTriangle size={20} />
            <h2 className="card-title">Alertes inventaire</h2>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Référence</th>
                  <th>Article</th>
                  <th>Stock actuel</th>
                  <th>Stock min.</th>
                </tr>
              </thead>
              <tbody>
                {inventaire
                  .filter((a) => a.stock <= a.stockMin)
                  .map((a) => (
                    <tr key={a.id}>
                      <td>{a.reference}</td>
                      <td>{a.nom}</td>
                      <td className="text-danger">
                        {a.stock} {a.unite}
                      </td>
                      <td>
                        {a.stockMin} {a.unite}
                      </td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <div className="card">
        <h2 className="card-title">
          <Clock size={18} /> Travaux récents
        </h2>
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Référence</th>
                <th>Titre</th>
                <th>Client</th>
                <th>Statut</th>
                <th>Livraison</th>
                <th>Montant</th>
              </tr>
            </thead>
            <tbody>
              {travRecentsTrie.map((t) => (
                <tr key={t.id}>
                  <td>
                    <code>{t.reference}</code>
                  </td>
                  <td>{t.titre}</td>
                  <td>{clientNom(t.clientId)}</td>
                  <td>
                    <span
                      className={`badge badge-${
                        {
                          planifie: "info",
                          en_cours: "warning",
                          termine: "success",
                          annule: "danger",
                        }[t.statut] || "default"
                      }`}
                    >
                      {
                        {
                          planifie: "Planifié",
                          en_cours: "En cours",
                          termine: "Terminé",
                          annule: "Annulé",
                        }[t.statut]
                      }
                    </span>
                  </td>
                  <td>{new Date(t.dateLivraison).toLocaleDateString("fr-FR")}</td>
                  <td>{t.montantTotal?.toLocaleString("fr-FR")} €</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
