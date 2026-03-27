import { useState } from "react";
import { useApp } from "../context/useApp";
import { Plus, Edit, Trash2, AlertTriangle } from "lucide-react";
import Modal from "../components/Modal";
import SearchBar from "../components/SearchBar";
import { StockAlertBadge } from "../components/Badge";

const categories = {
  papier: "Papier",
  encre: "Encre",
  consommable: "Consommable",
  autre: "Autre",
};

const emptyArticle = {
  reference: "",
  nom: "",
  categorie: "papier",
  sousCategorie: "",
  unite: "feuilles",
  stock: 0,
  stockMin: 0,
  stockMax: 0,
  prixUnitaire: 0,
  fournisseur: "",
  emplacement: "",
  derniereCommande: "",
};

function ArticleForm({ initial, onSave, onCancel }) {
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
          <label>Référence *</label>
          <input
            required
            value={form.reference}
            onChange={(e) => set("reference", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Nom *</label>
          <input
            required
            value={form.nom}
            onChange={(e) => set("nom", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Catégorie</label>
          <select
            value={form.categorie}
            onChange={(e) => set("categorie", e.target.value)}
          >
            {Object.entries(categories).map(([k, v]) => (
              <option key={k} value={k}>
                {v}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label>Sous-catégorie</label>
          <input
            value={form.sousCategorie}
            onChange={(e) => set("sousCategorie", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Stock actuel</label>
          <input
            type="number"
            min={0}
            value={form.stock}
            onChange={(e) => set("stock", Number(e.target.value))}
          />
        </div>
        <div className="form-group">
          <label>Unité</label>
          <input
            value={form.unite}
            onChange={(e) => set("unite", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Stock min.</label>
          <input
            type="number"
            min={0}
            value={form.stockMin}
            onChange={(e) => set("stockMin", Number(e.target.value))}
          />
        </div>
        <div className="form-group">
          <label>Stock max.</label>
          <input
            type="number"
            min={0}
            value={form.stockMax}
            onChange={(e) => set("stockMax", Number(e.target.value))}
          />
        </div>
      </div>
      <div className="form-row">
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
          <label>Fournisseur</label>
          <input
            value={form.fournisseur}
            onChange={(e) => set("fournisseur", e.target.value)}
          />
        </div>
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Emplacement</label>
          <input
            value={form.emplacement}
            onChange={(e) => set("emplacement", e.target.value)}
          />
        </div>
        <div className="form-group">
          <label>Dernière commande</label>
          <input
            type="date"
            value={form.derniereCommande}
            onChange={(e) => set("derniereCommande", e.target.value)}
          />
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

export default function Inventaire() {
  const { state, dispatch } = useApp();
  const [search, setSearch] = useState("");
  const [filterCat, setFilterCat] = useState("tous");
  const [modal, setModal] = useState(null);

  const filtered = state.inventaire.filter((a) => {
    const matchSearch =
      a.nom.toLowerCase().includes(search.toLowerCase()) ||
      a.reference.toLowerCase().includes(search.toLowerCase()) ||
      a.fournisseur.toLowerCase().includes(search.toLowerCase());
    const matchCat = filterCat === "tous" || a.categorie === filterCat;
    return matchSearch && matchCat;
  });

  const alertes = state.inventaire.filter((a) => a.stock <= a.stockMin);

  function handleSave(article) {
    if (modal.mode === "add") {
      dispatch({ type: "ADD_ARTICLE", payload: article });
    } else {
      dispatch({ type: "UPDATE_ARTICLE", payload: article });
    }
    setModal(null);
  }

  function handleDelete(id) {
    if (window.confirm("Supprimer cet article ?")) {
      dispatch({ type: "DELETE_ARTICLE", payload: id });
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="page-title">Inventaire</h1>
        <button
          className="btn btn-primary"
          onClick={() => setModal({ mode: "add", article: emptyArticle })}
        >
          <Plus size={16} /> Nouvel article
        </button>
      </div>

      {alertes.length > 0 && (
        <div className="alert-banner">
          <AlertTriangle size={18} />
          <span>
            {alertes.length} article(s) en dessous du seuil minimum :{" "}
            {alertes.map((a) => a.nom).join(", ")}
          </span>
        </div>
      )}

      <div className="toolbar">
        <SearchBar
          value={search}
          onChange={setSearch}
          placeholder="Rechercher un article…"
        />
        <div className="filter-tabs">
          {["tous", ...Object.keys(categories)].map((c) => (
            <button
              key={c}
              className={`tab-btn${filterCat === c ? " active" : ""}`}
              onClick={() => setFilterCat(c)}
            >
              {c === "tous" ? "Tous" : categories[c]}
            </button>
          ))}
        </div>
        <span className="count">{filtered.length} article(s)</span>
      </div>

      <div className="table-wrap card">
        <table className="table">
          <thead>
            <tr>
              <th>Référence</th>
              <th>Nom</th>
              <th>Catégorie</th>
              <th>Stock</th>
              <th>Unité</th>
              <th>Seuil min.</th>
              <th>Alerte</th>
              <th>Prix unit.</th>
              <th>Fournisseur</th>
              <th>Emplacement</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && (
              <tr>
                <td colSpan={11} className="text-center text-muted">
                  Aucun article trouvé
                </td>
              </tr>
            )}
            {filtered.map((a) => (
              <tr key={a.id}>
                <td>
                  <code>{a.reference}</code>
                </td>
                <td>{a.nom}</td>
                <td>{categories[a.categorie] || a.categorie}</td>
                <td className={a.stock <= a.stockMin ? "text-danger" : ""}>
                  {a.stock.toLocaleString("fr-FR")}
                </td>
                <td>{a.unite}</td>
                <td>{a.stockMin.toLocaleString("fr-FR")}</td>
                <td><StockAlertBadge stock={a.stock} stockMin={a.stockMin} /></td>
                <td>{a.prixUnitaire.toLocaleString("fr-FR", { minimumFractionDigits: 2 })} €</td>
                <td>{a.fournisseur}</td>
                <td>{a.emplacement}</td>
                <td>
                  <div className="actions">
                    <button
                      className="btn-icon"
                      title="Modifier"
                      onClick={() => setModal({ mode: "edit", article: a })}
                    >
                      <Edit size={16} />
                    </button>
                    <button
                      className="btn-icon btn-icon-danger"
                      title="Supprimer"
                      onClick={() => handleDelete(a.id)}
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
            modal.mode === "add" ? "Nouvel article" : "Modifier l'article"
          }
          onClose={() => setModal(null)}
        >
          <ArticleForm
            initial={modal.article}
            onSave={handleSave}
            onCancel={() => setModal(null)}
          />
        </Modal>
      )}
    </div>
  );
}
