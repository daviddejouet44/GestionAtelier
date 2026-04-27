import { authToken, showNotification, esc } from '../core.js';

// All actions with their IDs and human-readable labels
const ALL_ACTIONS = [
  { id: "ouvrirFichier",        label: "Ouvrir fichier" },
  { id: "fiche",                label: "Fiche" },
  { id: "affecter",             label: "Affecter à" },
  { id: "preflight",            label: "Preflight" },
  { id: "bat",                  label: "→ BAT" },
  { id: "actions",              label: "Actions ▾" },
  { id: "prismaPrepare",        label: "PrismaPrepare" },
  { id: "impressionLancee",     label: "Impression lancée" },
  { id: "fiery",                label: "Fiery" },
  { id: "mailDebutProduction",  label: "Mail début de production" },
  { id: "mailFinProduction",    label: "Mail fin de production" },
  { id: "impressionTerminee",   label: "Impression terminée" },
  { id: "finitions",            label: "Finitions" },
  { id: "faconnageTermine",     label: "Façonnage terminé" },
  { id: "verrouiller",          label: "Verrouiller (Terminé)" },
  { id: "archiver",             label: "Archiver" },
  { id: "supprimer",            label: "Supprimer" },
];

export async function renderSettingsKanbanColumns(panel) {
  panel.innerHTML = `<h3>Tuiles</h3><p style="color:#6b7280;">Chargement...</p>`;

  const DEFAULT_COLUMNS = [
    { folder: "Début de production", label: "Jobs à traiter", color: "#5fa8c4", visible: true, order: 0 },
    { folder: "Corrections", label: "Preflight", color: "#e0e0e0", visible: true, order: 1 },
    { folder: "Corrections et fond perdu", label: "Preflight avec fond perdu", color: "#e0e0e0", visible: true, order: 2 },
    { folder: "Prêt pour impression", label: "En attente", color: "#b8b8b8", visible: true, order: 3 },
    { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f", visible: true, order: 4 },
    { folder: "Fiery", label: "Fiery", color: "#8f8f8f", visible: true, order: 5 },
    { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a", visible: true, order: 6 },
    { folder: "Façonnage", label: "Façonnage", color: "#666666", visible: true, order: 7 },
    { folder: "Fin de production", label: "Fin de production", color: "#22c55e", visible: true, order: 8 }
  ];

  let columns = DEFAULT_COLUMNS;
  try {
    const resp = await fetch("/api/config/kanban-columns").then(r => r.json());
    if (resp.ok && Array.isArray(resp.columns) && resp.columns.length > 0) {
      columns = resp.columns.sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
    }
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Tuiles</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:20px;">
      Configurez les tuiles affichées dans le Kanban : nom de la tuile (interne), chemin physique optionnel, label affiché, couleur, visibilité et ordre.
      Dépliez chaque tuile pour choisir les actions visibles sur les cartes.
    </p>
    <div id="kanban-cols-list" style="display:flex;flex-direction:column;gap:8px;max-width:1200px;margin-bottom:16px;">
      <div style="display:grid;grid-template-columns:160px 1fr 180px 80px 70px 80px 60px;gap:8px;font-size:12px;font-weight:600;color:#6b7280;padding:0 4px;">
        <span>Nom de la tuile</span>
        <span>Chemin physique du dossier (ex: C:\Flux\MonDossier)</span>
        <span>Label affiché</span>
        <span>Couleur</span>
        <span>Visible</span>
        <span>Ordre</span>
        <span>Actions</span>
      </div>
    </div>
    <button id="kanban-cols-save" class="btn btn-primary">Enregistrer</button>
    <button id="kanban-cols-reset" class="btn btn-sm" style="margin-left:8px;">Réinitialiser par défaut</button>
  `;

  const listEl = panel.querySelector("#kanban-cols-list");

  function renderRow(col) {
    const wrapper = document.createElement("div");
    wrapper.className = "kanban-cfg-row";
    wrapper.style.cssText = "background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;overflow:hidden;";

    // Main grid row
    const row = document.createElement("div");
    row.style.cssText = "display:grid;grid-template-columns:160px 1fr 180px 80px 70px 80px 60px;gap:8px;align-items:center;padding:6px 8px;";

    // Determine which actions are currently checked (null = all checked)
    const currentActions = Array.isArray(col.visibleActions) ? col.visibleActions : null;

    row.innerHTML = `
      <input type="text" class="settings-input kcol-folder" value="${esc(col.folder)}" placeholder="Nom de la tuile" style="font-size:12px;" />
      <input type="text" class="settings-input kcol-folderpath" value="${esc(col.folderPath || '')}" placeholder="C:\\Flux\\MonDossier (optionnel)" style="font-size:12px;" />
      <input type="text" class="settings-input kcol-label" value="${esc(col.label)}" placeholder="Label affiché" style="font-size:12px;" />
      <input type="color" class="kcol-color" value="${esc(col.color || '#8f8f8f')}" style="width:60px;height:32px;padding:2px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;" />
      <label style="display:flex;align-items:center;gap:4px;font-size:12px;"><input type="checkbox" class="kcol-visible" ${col.visible !== false ? "checked" : ""} /> Visible</label>
      <div style="display:flex;gap:4px;">
        <button class="btn btn-sm kcol-up" title="Monter" style="padding:6px 12px;font-size:18px;">↑</button>
        <button class="btn btn-sm kcol-down" title="Descendre" style="padding:6px 12px;font-size:18px;">↓</button>
      </div>
      <button class="btn btn-sm kcol-actions-toggle" title="Configurer les actions visibles" style="font-size:11px;padding:4px 8px;">⚙️ Actions</button>
    `;

    // Actions section (collapsible)
    const actionsSection = document.createElement("div");
    actionsSection.className = "kcol-actions-section";
    actionsSection.style.cssText = "display:none;padding:10px 12px;border-top:1px solid #e5e7eb;background:#fff;";

    const actionsLabel = document.createElement("p");
    actionsLabel.style.cssText = "font-size:12px;font-weight:600;color:#374151;margin:0 0 8px;";
    actionsLabel.textContent = "Actions visibles sur les cartes (décochez pour masquer) :";
    actionsSection.appendChild(actionsLabel);

    const actionsGrid = document.createElement("div");
    actionsGrid.style.cssText = "display:flex;flex-wrap:wrap;gap:6px 16px;";

    // "Tout cocher" / "Tout décocher" shortcuts
    const quickBtns = document.createElement("div");
    quickBtns.style.cssText = "display:flex;gap:8px;margin-bottom:8px;";
    const btnAll = document.createElement("button");
    btnAll.className = "btn btn-sm";
    btnAll.textContent = "Tout cocher";
    btnAll.type = "button";
    btnAll.onclick = () => actionsGrid.querySelectorAll("input[type=checkbox]").forEach(cb => cb.checked = true);
    const btnNone = document.createElement("button");
    btnNone.className = "btn btn-sm";
    btnNone.textContent = "Tout décocher";
    btnNone.type = "button";
    btnNone.onclick = () => actionsGrid.querySelectorAll("input[type=checkbox]").forEach(cb => cb.checked = false);
    quickBtns.appendChild(btnAll);
    quickBtns.appendChild(btnNone);
    actionsSection.appendChild(quickBtns);

    ALL_ACTIONS.forEach(action => {
      const lbl = document.createElement("label");
      lbl.style.cssText = "display:flex;align-items:center;gap:4px;font-size:12px;color:#374151;white-space:nowrap;";
      const cb = document.createElement("input");
      cb.type = "checkbox";
      cb.className = "kcol-action-cb";
      cb.dataset.actionId = action.id;
      // Checked if no restriction (null) or if in the list
      cb.checked = (currentActions === null) || currentActions.includes(action.id);
      lbl.appendChild(cb);
      lbl.appendChild(document.createTextNode(action.label));
      actionsGrid.appendChild(lbl);
    });

    actionsSection.appendChild(actionsGrid);
    wrapper.appendChild(row);
    wrapper.appendChild(actionsSection);

    row.querySelector(".kcol-up").onclick = () => {
      const prev = wrapper.previousElementSibling;
      if (prev && prev.classList.contains("kanban-cfg-row")) listEl.insertBefore(wrapper, prev);
    };
    row.querySelector(".kcol-down").onclick = () => {
      const next = wrapper.nextElementSibling;
      if (next && next.classList.contains("kanban-cfg-row")) listEl.insertBefore(next, wrapper);
    };
    row.querySelector(".kcol-actions-toggle").onclick = () => {
      const visible = actionsSection.style.display !== "none";
      actionsSection.style.display = visible ? "none" : "block";
    };

    listEl.appendChild(wrapper);
  }

  columns.forEach(c => renderRow(c));

  const collectColumns = () => {
    return Array.from(listEl.querySelectorAll(".kanban-cfg-row")).map((wrapper, i) => {
      const row = wrapper.querySelector("div");
      const allCbs = Array.from(wrapper.querySelectorAll(".kcol-action-cb"));
      const checkedIds = allCbs.filter(cb => cb.checked).map(cb => cb.dataset.actionId);
      // If all are checked, store null (show all, retrocompat)
      const visibleActions = (checkedIds.length === ALL_ACTIONS.length) ? null : checkedIds;
      return {
        folder:        row.querySelector(".kcol-folder")?.value.trim() || "",
        folderPath:    row.querySelector(".kcol-folderpath")?.value.trim() || "",
        label:         row.querySelector(".kcol-label")?.value.trim() || "",
        color:         row.querySelector(".kcol-color")?.value || "#8f8f8f",
        visible:       row.querySelector(".kcol-visible")?.checked ?? true,
        order:         i,
        visibleActions,
      };
    });
  };

  panel.querySelector("#kanban-cols-save").onclick = async () => {
    const cols = collectColumns();
    const r = await fetch("/api/config/kanban-columns", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ columns: cols })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Configuration Kanban enregistrée", "success");
      // Rebuild kanban to apply new action visibility settings
      if (window._buildKanban) await window._buildKanban();
    } else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };

  panel.querySelector("#kanban-cols-reset").onclick = async () => {
    if (!confirm("Réinitialiser la configuration des tuiles aux valeurs par défaut ?")) return;
    const r = await fetch("/api/config/kanban-columns", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ columns: DEFAULT_COLUMNS })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Configuration réinitialisée", "success");
      panel._loaded = false;
      await renderSettingsKanbanColumns(panel);
    } else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };
}
