import { useState } from "react";
import { useApp } from "../context/useApp";
import { Plus, Edit, Trash2, Mail, Phone } from "lucide-react";
import Modal from "../components/Modal";
import SearchBar from "../components/SearchBar";
import Badge from "../components/Badge";

const emptyClient = {
  nom: "",
  contact: "",
  email: "",
  telephone: "",
  adresse: "",
  type: "professionnel",
  actif: true,
};

const typeLabels = {
  professionnel: "Professionnel",
  collectivite: "Collectivité",
  particulier: "Particulier",
  association: "Association",
};

function ClientForm({ initial, onSave, onCancel }) {
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
          <label>Nom / Raison sociale *</label>
          <input
            required
            value={form.nom}
            onChange={(e) => set("nom", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Contact</label>
          <input
            value={form.contact}
            onChange={(e) => set("contact", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Email</label>
          <input
            type="email"
            value={form.email}
            onChange={(e) => set("email", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Téléphone</label>
          <input
            value={form.telephone}
            onChange={(e) => set("telephone", e.target.value)}
          />
        </div>
      </div>
      <div className="form-group">
        <label>Adresse</label>
        <input
          value={form.adresse}
          onChange={(e) => set("adresse", e.target.value)}
        />
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Type</label>
          <select value={form.type} onChange={(e) => set("type", e.target.value)}>
            {Object.entries(typeLabels).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group form-group-check">
          <label>
            <input
              type="checkbox"
              checked={form.actif}
              onChange={(e) => set("actif", e.target.checked)}
            />
            Client actif
          </label>
        </div>
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

export default function Clients() {
  const { state, dispatch } = useApp();
  const [search, setSearch] = useState("");
  const [modal, setModal] = useState(null); // null | { mode: 'add'|'edit', client? }

  const filtered = state.clients.filter(
    (c) =>
      c.nom.toLowerCase().includes(search.toLowerCase()) ||
      c.contact.toLowerCase().includes(search.toLowerCase()) ||
      c.email.toLowerCase().includes(search.toLowerCase())
  );

  function handleSave(client) {
    if (modal.mode === "add") {
      dispatch({ type: "ADD_CLIENT", payload: client });
    } else {
      dispatch({ type: "UPDATE_CLIENT", payload: client });
    }
    setModal(null);
  }

  function handleDelete(id) {
    if (window.confirm("Supprimer ce client ?")) {
      dispatch({ type: "DELETE_CLIENT", payload: id });
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Clients</h1>
        <button
          className="btn btn-primary"
          onClick={() => setModal({ mode: "add", client: emptyClient })}
        >
          <Plus size={16} /> Nouveau client
        </button>
      </div>

      <div className="toolbar">
        <SearchBar
          value={search}
          onChange={setSearch}
          placeholder="Rechercher un client…"
        />
        <span className="count">{filtered.length} client(s)</span>
      </div>

      <div className="table-wrap card">
        <table className="table">
          <thead>
            <tr>
              <th>Nom</th>
              <th>Contact</th>
              <th>Email</th>
              <th>Téléphone</th>
              <th>Type</th>
              <th>Statut</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && (
              <tr>
                <td colSpan={7} className="text-center text-muted">
                  Aucun client trouvé
                </td>
              </tr>
            )}
            {filtered.map((c) => (
              <tr key={c.id}>
                <td>
                  <strong>{c.nom}</strong>
                </td>
                <td>{c.contact}</td>
                <td>
                  <a href={`mailto:${c.email}`} className="link">
                    <Mail size={13} /> {c.email}
                  </a>
                </td>
                <td>
                  <span className="icon-text">
                    <Phone size={13} /> {c.telephone}
                  </span>
                </td>
                <td>
                  <Badge variant="default">{typeLabels[c.type] || c.type}</Badge>
                </td>
                <td>
                  <Badge variant={c.actif ? "success" : "default"}>
                    {c.actif ? "Actif" : "Inactif"}
                  </Badge>
                </td>
                <td>
                  <div className="actions">
                    <button
                      className="btn-icon"
                      title="Modifier"
                      onClick={() => setModal({ mode: "edit", client: c })}
                    >
                      <Edit size={16} />
                    </button>
                    <button
                      className="btn-icon btn-icon-danger"
                      title="Supprimer"
                      onClick={() => handleDelete(c.id)}
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
          title={modal.mode === "add" ? "Nouveau client" : "Modifier le client"}
          onClose={() => setModal(null)}
        >
          <ClientForm
            initial={modal.client}
            onSave={handleSave}
            onCancel={() => setModal(null)}
          />
        </Modal>
      )}
    </div>
  );
}
