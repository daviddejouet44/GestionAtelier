import { useApp } from "../context/useApp";
import { BarChart3, TrendingUp, FileText, Package } from "lucide-react";

function SectionTitle({ icon, children }) {
  const Icon = icon;
  return (
    <h2 className="section-title">
      <Icon size={18} /> {children}
    </h2>
  );
}

export default function Rapports() {
  const { state } = useApp();
  const { clients, machines, travaux, inventaire } = state;

  // CA par client
  const caParClient = clients
    .map((c) => {
      const ca = travaux
        .filter((t) => t.clientId === c.id && t.statut !== "annule")
        .reduce((s, t) => s + (t.montantTotal || 0), 0);
      const nb = travaux.filter((t) => t.clientId === c.id).length;
      return { ...c, ca, nb };
    })
    .filter((c) => c.nb > 0)
    .sort((a, b) => b.ca - a.ca);

  const caTotal = caParClient.reduce((s, c) => s + c.ca, 0);

  // Travaux par machine
  const travauxParMachine = machines
    .map((m) => {
      const nb = travaux.filter((t) => t.machineId === m.id).length;
      const ca = travaux
        .filter((t) => t.machineId === m.id && t.statut !== "annule")
        .reduce((s, t) => s + (t.montantTotal || 0), 0);
      return { ...m, nb, ca };
    })
    .sort((a, b) => b.nb - a.nb);

  // Travaux par type
  const travauxParType = {};
  travaux.forEach((t) => {
    travauxParType[t.type] = (travauxParType[t.type] || 0) + 1;
  });

  const typeLabels = {
    brochure: "Brochure",
    affiche: "Affiche",
    catalogue: "Catalogue",
    carte_visite: "Carte de visite",
    flyer: "Flyer",
    etiquette: "Étiquette",
    livre: "Livre",
    calendrier: "Calendrier",
    autre: "Autre",
  };

  // Stock valeur
  const valeurStock = inventaire.reduce(
    (s, a) => s + a.stock * a.prixUnitaire,
    0
  );

  return (
    <div className="page">
      <h1 className="page-title">Rapports</h1>

      <div className="rapports-grid">
        {/* Résumé global */}
        <div className="card">
          <SectionTitle icon={TrendingUp}>Résumé global</SectionTitle>
          <div className="rapport-stats">
            <div className="rapport-stat">
              <div className="rapport-stat-value">
                {caTotal.toLocaleString("fr-FR")} €
              </div>
              <div className="rapport-stat-label">CA prévisionnel total</div>
            </div>
            <div className="rapport-stat">
              <div className="rapport-stat-value">{travaux.length}</div>
              <div className="rapport-stat-label">Travaux enregistrés</div>
            </div>
            <div className="rapport-stat">
              <div className="rapport-stat-value">{clients.length}</div>
              <div className="rapport-stat-label">Clients</div>
            </div>
            <div className="rapport-stat">
              <div className="rapport-stat-value">
                {valeurStock.toLocaleString("fr-FR", {
                  minimumFractionDigits: 2,
                })}{" "}
                €
              </div>
              <div className="rapport-stat-label">Valeur stock</div>
            </div>
          </div>
        </div>

        {/* CA par client */}
        <div className="card">
          <SectionTitle icon={BarChart3}>CA par client</SectionTitle>
          {caParClient.length === 0 && (
            <p className="text-muted">Aucune donnée</p>
          )}
          <div className="rapport-bars">
            {caParClient.map((c) => {
              const pct = caTotal > 0 ? (c.ca / caTotal) * 100 : 0;
              return (
                <div key={c.id} className="rapport-bar-row">
                  <span className="rapport-bar-label">{c.nom}</span>
                  <div className="rapport-bar-wrap">
                    <div
                      className="rapport-bar"
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                  <span className="rapport-bar-val">
                    {c.ca.toLocaleString("fr-FR")} €
                  </span>
                </div>
              );
            })}
          </div>
        </div>

        {/* Travaux par machine */}
        <div className="card">
          <SectionTitle icon={FileText}>Charge par machine</SectionTitle>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Machine</th>
                  <th>Type</th>
                  <th>Nb travaux</th>
                  <th>CA</th>
                </tr>
              </thead>
              <tbody>
                {travauxParMachine.map((m) => (
                  <tr key={m.id}>
                    <td>{m.nom}</td>
                    <td>
                      <span
                        className={`badge badge-${
                          m.type === "offset" ? "primary" : "purple"
                        }`}
                      >
                        {m.type === "offset" ? "Offset" : "Numérique"}
                      </span>
                    </td>
                    <td>{m.nb}</td>
                    <td>{m.ca.toLocaleString("fr-FR")} €</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        {/* Travaux par type */}
        <div className="card">
          <SectionTitle icon={BarChart3}>Répartition des travaux</SectionTitle>
          <div className="rapport-bars">
            {Object.entries(travauxParType)
              .sort((a, b) => b[1] - a[1])
              .map(([type, count]) => {
                const pct = travaux.length > 0 ? (count / travaux.length) * 100 : 0;
                return (
                  <div key={type} className="rapport-bar-row">
                    <span className="rapport-bar-label">
                      {typeLabels[type] || type}
                    </span>
                    <div className="rapport-bar-wrap">
                      <div
                        className="rapport-bar rapport-bar-alt"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                    <span className="rapport-bar-val">{count}</span>
                  </div>
                );
              })}
          </div>
        </div>

        {/* Stock */}
        <div className="card">
          <SectionTitle icon={Package}>État du stock</SectionTitle>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Article</th>
                  <th>Catégorie</th>
                  <th>Stock</th>
                  <th>Valeur</th>
                  <th>Alerte</th>
                </tr>
              </thead>
              <tbody>
                {inventaire.map((a) => (
                  <tr key={a.id}>
                    <td>{a.nom}</td>
                    <td>{a.categorie}</td>
                    <td>
                      {a.stock.toLocaleString("fr-FR")} {a.unite}
                    </td>
                    <td>
                      {(a.stock * a.prixUnitaire).toLocaleString("fr-FR", {
                        minimumFractionDigits: 2,
                      })}{" "}
                      €
                    </td>
                    <td>
                      {a.stock <= a.stockMin ? (
                        <span className="badge badge-warning">Stock bas</span>
                      ) : (
                        <span className="badge badge-success">OK</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
