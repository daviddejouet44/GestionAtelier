import { useState } from "react";
import { useApp } from "../context/useApp";
import { Plus, Edit, Trash2 } from "lucide-react";
import Modal from "../components/Modal";
import SearchBar from "../components/SearchBar";
import { StatutBadge } from "../components/Badge";

const typesTravail = {
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

function newRef(travaux) {
  const year = new Date().getFullYear();
  const max =
    travaux
      .map((t) => {
        const m = t.reference.match(/TRV-\d{4}-(\d+)/);
        return m ? parseInt(m[1]) : 0;
      })
      .reduce((a, b) => Math.max(a, b), 0) + 1;
  return `TRV-${year}-${String(max).padStart(3, "0")}`;
}

const emptyTravail = {
  reference: "",
  titre: "",
  clientId: "",
  machineId: "",
  type: "brochure",
  statut: "planifie",
  priorite: "normale",
  dateCreation: new Date().toISOString().slice(0, 10),
  dateLivraison: "",
  quantite: 1,
  format: "A4",
  couleurs: "4+4",
  support: "",
  finition: "",
  prixUnitaire: 0,
  montantTotal: 0,
  notes: "",
  pages: 1,
};

function TravailForm({ initial, clients, machines, onSave, onCancel }) {
  const [form, setForm] = useState(initial);
  function set(k, v) {
    setForm((f) => {
      const updated = { ...f, [k]: v };
      if (k === "quantite" || k === "prixUnitaire") {
        updated.montantTotal =
          Number(updated.quantite) * Number(updated.prixUnitaire);
      }
      return updated;
    });
  }
  return (
    <form
      className="form"
      onSubmit={(e) => {
        e.preventDefault();
        onSave(form);
      }}
    >
      <div className="form-row">
        <div className="form-group">
          <label>Référence *</label>
          <input
            required
            value={form.reference}
            onChange={(e) => set("reference", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Titre *</label>
          <input
            required
            value={form.titre}
            onChange={(e) => set("titre", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Client *</label>
          <select
            required
            value={form.clientId}
            onChange={(e) => set("clientId", Number(e.target.value))}
          >
            <option value="">-- Sélectionner --</option>
            {clients.map((c) => (
              <option key={c.id} value={c.id}>
                {c.nom}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label>Machine</label>
          <select
            value={form.machineId}
            onChange={(e) => set("machineId", Number(e.target.value))}
          >
            <option value="">-- Sélectionner --</option>
            {machines.map((m) => (
              <option key={m.id} value={m.id}>
                {m.nom}
              </option>
            ))}
          </select>
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Type</label>
          <select value={form.type} onChange={(e) => set("type", e.target.value)}>
            {Object.entries(typesTravail).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label>Statut</label>
          <select
            value={form.statut}
            onChange={(e) => set("statut", e.target.value)}
          >
            <option value="planifie">Planifié</option>
            <option value="en_cours">En cours</option>
            <option value="termine">Terminé</option>
            <option value="annule">Annulé</option>
            <option value="en_attente">En attente</option>
          </select>
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Priorité</label>
          <select
            value={form.priorite}
            onChange={(e) => set("priorite", e.target.value)}
          >
            <option value="basse">Basse</option>
            <option value="normale">Normale</option>
            <option value="haute">Haute</option>
            <option value="urgente">Urgente</option>
          </select>
        </div>
        <div className="form-group">
          <label>Format</label>
          <input
            value={form.format}
            onChange={(e) => set("format", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Date création</label>
          <input
            type="date"
            value={form.dateCreation}
            onChange={(e) => set("dateCreation", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Date livraison *</label>
          <input
            required
            type="date"
            value={form.dateLivraison}
            onChange={(e) => set("dateLivraison", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Support</label>
          <input
            value={form.support}
            onChange={(e) => set("support", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Finition</label>
          <input
            value={form.finition}
            onChange={(e) => set("finition", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Couleurs (ex: 4+4)</label>
          <input
            value={form.couleurs}
            onChange={(e) => set("couleurs", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Nb de pages</label>
          <input
            type="number"
            min={1}
            value={form.pages}
            onChange={(e) => set("pages", Number(e.target.value))}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Quantité</label>
          <input
            type="number"
            min={1}
            value={form.quantite}
            onChange={(e) => set("quantite", Number(e.target.value))}
          />
        </div>
        <div className="form-group">
          <label>Prix unitaire (€)</label>
          <input
            type="number"
            step="0.01"
            min={0}
            value={form.prixUnitaire}
            onChange={(e) => set("prixUnitaire", Number(e.target.value))}
          />
        </div>
        <div className="form-group">
          <label>Montant total (€)</label>
          <input readOnly value={form.montantTotal.toFixed(2)} />
        </div>
      </div>
      <div className="form-group">
        <label>Notes</label>
        <textarea
          rows={3}
          value={form.notes}
          onChange={(e) => set("notes", e.target.value)}
        />
      </div>
      <div className="form-actions">
        <button type="button" className="btn btn-secondary" onClick={onCancel}>
          Annuler
        </button>
        <button type="submit" className="btn btn-primary">
          Enregistrer
        </button>
      </div>
    </form>
  );
}

export default function Travaux() {
  const { state, dispatch } = useApp();
  const [search, setSearch] = useState("");
  const [filterStatut, setFilterStatut] = useState("tous");
  const [modal, setModal] = useState(null);

  const filtered = state.travaux.filter((t) => {
    const client = state.clients.find((c) => c.id === t.clientId);
    const matchSearch =
      t.reference.toLowerCase().includes(search.toLowerCase()) ||
      t.titre.toLowerCase().includes(search.toLowerCase()) ||
      (client?.nom || "").toLowerCase().includes(search.toLowerCase());
    const matchStatut = filterStatut === "tous" || t.statut === filterStatut;
    return matchSearch && matchStatut;
  });

  function clientNom(id) {
    return state.clients.find((c) => c.id === id)?.nom || "–";
  }

  function machineNom(id) {
    return state.machines.find((m) => m.id === id)?.nom || "–";
  }

  function handleAdd() {
    setModal({
      mode: "add",
      travail: { ...emptyTravail, reference: newRef(state.travaux) },
    });
  }

  function handleSave(travail) {
    if (modal.mode === "add") {
      dispatch({ type: "ADD_TRAVAIL", payload: travail });
    } else {
      dispatch({ type: "UPDATE_TRAVAIL", payload: travail });
    }
    setModal(null);
  }

  function handleDelete(id) {
    if (window.confirm("Supprimer ce travail ?")) {
      dispatch({ type: "DELETE_TRAVAIL", payload: id });
    }
  }

  const statuts = ["tous", "planifie", "en_cours", "termine", "annule"];
  const statutLabels = {
    tous: "Tous",
    planifie: "Planifiés",
    en_cours: "En cours",
    termine: "Terminés",
    annule: "Annulés",
  };

  const prioriteColors = {
    basse: "default",
    normale: "info",
    haute: "warning",
    urgente: "danger",
  };

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Travaux</h1>
        <button className="btn btn-primary" onClick={handleAdd}>
          <Plus size={16} /> Nouveau travail
        </button>
      </div>

      <div className="toolbar">
        <SearchBar
          value={search}
          onChange={setSearch}
          placeholder="Rechercher un travail…"
        />
        <div className="filter-tabs">
          {statuts.map((s) => (
            <button
              key={s}
              className={`tab-btn${filterStatut === s ? " active" : ""}`}
              onClick={() => setFilterStatut(s)}
            >
              {statutLabels[s]}
            </button>
          ))}
        </div>
        <span className="count">{filtered.length} travail(s)</span>
      </div>

      <div className="table-wrap card">
        <table className="table">
          <thead>
            <tr>
              <th>Référence</th>
              <th>Titre</th>
              <th>Client</th>
              <th>Machine</th>
              <th>Type</th>
              <th>Statut</th>
              <th>Priorité</th>
              <th>Livraison</th>
              <th>Montant</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && (
              <tr>
                <td colSpan={10} className="text-center text-muted">
                  Aucun travail trouvé
                </td>
              </tr>
            )}
            {filtered.map((t) => (
              <tr key={t.id}>
                <td>
                  <code>{t.reference}</code>
                </td>
                <td>{t.titre}</td>
                <td>{clientNom(t.clientId)}</td>
                <td className="text-small">{machineNom(t.machineId)}</td>
                <td>{typesTravail[t.type] || t.type}</td>
                <td><StatutBadge statut={t.statut} /></td>
                <td>
                  <span
                    className={`badge badge-${prioriteColors[t.priorite] || "default"}`}
                  >
                    {t.priorite.charAt(0).toUpperCase() + t.priorite.slice(1)}
                  </span>
                </td>
                <td>
                  {t.dateLivraison
                    ? new Date(t.dateLivraison).toLocaleDateString("fr-FR")
                    : "–"}
                </td>
                <td>
                  {t.montantTotal
                    ? `${t.montantTotal.toLocaleString("fr-FR")} €`
                    : "–"}
                </td>
                <td>
                  <div className="actions">
                    <button
                      className="btn-icon"
                      title="Modifier"
                      onClick={() => setModal({ mode: "edit", travail: t })}
                    >
                      <Edit size={16} />
                    </button>
                    <button
                      className="btn-icon btn-icon-danger"
                      title="Supprimer"
                      onClick={() => handleDelete(t.id)}
                    >
                      <Trash2 size={16} />
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {modal && (
        <Modal
          title={
            modal.mode === "add" ? "Nouveau travail" : "Modifier le travail"
          }
          onClose={() => setModal(null)}
        >
          <TravailForm
            initial={modal.travail}
            clients={state.clients}
            machines={state.machines}
            onSave={handleSave}
            onCancel={() => setModal(null)}
          />
        </Modal>
      )}
    </div>
  );
}
