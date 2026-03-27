import { useState } from "react";
import { useApp } from "../context/useApp";
import { Plus, Edit, Trash2, Wrench } from "lucide-react";
import Modal from "../components/Modal";
import SearchBar from "../components/SearchBar";
import { EtatMachineBadge, TypeMachineBadge } from "../components/Badge";

const emptyMachine = {
  nom: "",
  type: "offset",
  marque: "",
  modele: "",
  annee: new Date().getFullYear(),
  etat: "operationnelle",
  formatMax: "",
  couleurs: 4,
  vitesseMax: 0,
  derniereMaintenance: "",
  prochaineMaintenance: "",
  notes: "",
};

function MachineForm({ initial, onSave, onCancel }) {
  const [form, setForm] = useState(initial);
  function set(k, v) {
    setForm((f) => ({ ...f, [k]: v }));
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
          <label>Nom *</label>
          <input
            required
            value={form.nom}
            onChange={(e) => set("nom", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Type *</label>
          <select value={form.type} onChange={(e) => set("type", e.target.value)}>
            <option value="offset">Offset</option>
            <option value="numerique">Numérique</option>
          </select>
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Marque</label>
          <input
            value={form.marque}
            onChange={(e) => set("marque", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Modèle</label>
          <input
            value={form.modele}
            onChange={(e) => set("modele", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Année</label>
          <input
            type="number"
            value={form.annee}
            onChange={(e) => set("annee", Number(e.target.value))}
          />
        </div>
        <div className="form-group">
          <label>État</label>
          <select value={form.etat} onChange={(e) => set("etat", e.target.value)}>
            <option value="operationnelle">Opérationnelle</option>
            <option value="maintenance">Maintenance</option>
            <option value="panne">En panne</option>
            <option value="arret">Arrêt</option>
          </select>
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Format max.</label>
          <input
            value={form.formatMax}
            onChange={(e) => set("formatMax", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Nb couleurs</label>
          <input
            type="number"
            min={1}
            value={form.couleurs}
            onChange={(e) => set("couleurs", Number(e.target.value))}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Dernière maintenance</label>
          <input
            type="date"
            value={form.derniereMaintenance}
            onChange={(e) => set("derniereMaintenance", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Prochaine maintenance</label>
          <input
            type="date"
            value={form.prochaineMaintenance}
            onChange={(e) => set("prochaineMaintenance", e.target.value)}
          />
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

export default function Machines() {
  const { state, dispatch } = useApp();
  const [search, setSearch] = useState("");
  const [filterType, setFilterType] = useState("tous");
  const [modal, setModal] = useState(null);

  const filtered = state.machines.filter((m) => {
    const matchSearch =
      m.nom.toLowerCase().includes(search.toLowerCase()) ||
      m.marque.toLowerCase().includes(search.toLowerCase());
    const matchType = filterType === "tous" || m.type === filterType;
    return matchSearch && matchType;
  });

  function handleSave(machine) {
    if (modal.mode === "add") {
      dispatch({ type: "ADD_MACHINE", payload: machine });
    } else {
      dispatch({ type: "UPDATE_MACHINE", payload: machine });
    }
    setModal(null);
  }

  function handleDelete(id) {
    if (window.confirm("Supprimer cette machine ?")) {
      dispatch({ type: "DELETE_MACHINE", payload: id });
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Machines</h1>
        <button
          className="btn btn-primary"
          onClick={() => setModal({ mode: "add", machine: emptyMachine })}
        >
          <Plus size={16} /> Nouvelle machine
        </button>
      </div>

      <div className="toolbar">
        <SearchBar
          value={search}
          onChange={setSearch}
          placeholder="Rechercher une machine…"
        />
        <div className="filter-tabs">
          {["tous", "offset", "numerique"].map((t) => (
            <button
              key={t}
              className={`tab-btn${filterType === t ? " active" : ""}`}
              onClick={() => setFilterType(t)}
            >
              {t === "tous" ? "Tous" : t === "offset" ? "Offset" : "Numérique"}
            </button>
          ))}
        </div>
        <span className="count">{filtered.length} machine(s)</span>
      </div>

      <div className="cards-grid">
        {filtered.length === 0 && (
          <p className="text-muted">Aucune machine trouvée</p>
        )}
        {filtered.map((m) => (
          <div key={m.id} className="machine-card card">
            <div className="machine-card-header">
              <div>
                <div className="machine-card-title">{m.nom}</div>
                <div className="machine-card-sub">
                  {m.marque} {m.modele}
                </div>
              </div>
              <div className="machine-card-badges">
                <TypeMachineBadge type={m.type} />
                <EtatMachineBadge etat={m.etat} />
              </div>
            </div>
            <div className="machine-card-body">
              <div className="machine-detail">
                <span className="detail-label">Format max.</span>
                <span>{m.formatMax}</span>
              </div>
              <div className="machine-detail">
                <span className="detail-label">Couleurs</span>
                <span>{m.couleurs}</span>
              </div>
              <div className="machine-detail">
                <span className="detail-label">Année</span>
                <span>{m.annee}</span>
              </div>
              <div className="machine-detail">
                <span className="detail-label">
                  <Wrench size={13} /> Maintenance
                </span>
                <span>
                  {m.prochaineMaintenance
                    ? new Date(m.prochaineMaintenance).toLocaleDateString("fr-FR")
                    : "–"}
                </span>
              </div>
            </div>
            {m.notes && <p className="machine-notes">{m.notes}</p>}
            <div className="card-actions">
              <button
                className="btn btn-secondary btn-sm"
                onClick={() => setModal({ mode: "edit", machine: m })}
              >
                <Edit size={14} /> Modifier
              </button>
              <button
                className="btn btn-danger btn-sm"
                onClick={() => handleDelete(m.id)}
              >
                <Trash2 size={14} /> Supprimer
              </button>
            </div>
          </div>
        ))}
      </div>

      {modal && (
        <Modal
          title={
            modal.mode === "add" ? "Nouvelle machine" : "Modifier la machine"
          }
          onClose={() => setModal(null)}
        >
          <MachineForm
            initial={modal.machine}
            onSave={handleSave}
            onCancel={() => setModal(null)}
          />
        </Modal>
      )}
    </div>
  );
}
