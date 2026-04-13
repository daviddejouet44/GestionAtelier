import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsKanbanColumns(panel) {
  panel.innerHTML = `<h3>Tuiles Kanban</h3><p style="color:#6b7280;">Chargement...</p>`;

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
    <h3>Tuiles Kanban</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:20px;">
      Configurez les tuiles affichées dans le Kanban : chemin complet du dossier physique, label affiché, couleur, visibilité et ordre.
    </p>
    <div id="kanban-cols-list" style="display:flex;flex-direction:column;gap:8px;max-width:1000px;margin-bottom:16px;">
      <div style="display:grid;grid-template-columns:1fr 200px 80px 70px 80px;gap:8px;font-size:12px;font-weight:600;color:#6b7280;padding:0 4px;">
        <span>Chemin complet du dossier</span>
        <span>Label affiché</span>
        <span>Couleur</span>
        <span>Visible</span>
        <span>Ordre</span>
      </div>
    </div>
    <button id="kanban-cols-save" class="btn btn-primary">Enregistrer</button>
    <button id="kanban-cols-reset" class="btn btn-sm" style="margin-left:8px;">Réinitialiser par défaut</button>
  `;

  const listEl = panel.querySelector("#kanban-cols-list");

  function renderRow(col) {
    const row = document.createElement("div");
    row.className = "kanban-cfg-row";
    row.style.cssText = "display:grid;grid-template-columns:1fr 200px 80px 70px 80px;gap:8px;align-items:center;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;padding:6px 8px;";
    row.innerHTML = `
      <input type="text" class="settings-input kcol-folder" value="${esc(col.folder)}" placeholder="Chemin complet du dossier" style="font-size:12px;" />
      <input type="text" class="settings-input kcol-label" value="${esc(col.label)}" placeholder="Label affiché" style="font-size:12px;" />
      <input type="color" class="kcol-color" value="${esc(col.color || '#8f8f8f')}" style="width:60px;height:32px;padding:2px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;" />
      <label style="display:flex;align-items:center;gap:4px;font-size:12px;"><input type="checkbox" class="kcol-visible" ${col.visible !== false ? "checked" : ""} /> Visible</label>
      <div style="display:flex;gap:4px;">
        <button class="btn btn-sm kcol-up" title="Monter" style="padding:6px 12px;font-size:18px;">↑</button>
        <button class="btn btn-sm kcol-down" title="Descendre" style="padding:6px 12px;font-size:18px;">↓</button>
      </div>
    `;
    row.querySelector(".kcol-up").onclick = () => {
      const prev = row.previousElementSibling;
      if (prev && prev.classList.contains("kanban-cfg-row")) listEl.insertBefore(row, prev);
    };
    row.querySelector(".kcol-down").onclick = () => {
      const next = row.nextElementSibling;
      if (next && next.classList.contains("kanban-cfg-row")) listEl.insertBefore(next, row);
    };
    listEl.appendChild(row);
  }

  columns.forEach(c => renderRow(c));

  const collectColumns = () => {
    return Array.from(listEl.querySelectorAll(".kanban-cfg-row")).map((row, i) => ({
      folder:  row.querySelector(".kcol-folder")?.value.trim() || "",
      label:   row.querySelector(".kcol-label")?.value.trim() || "",
      color:   row.querySelector(".kcol-color")?.value || "#8f8f8f",
      visible: row.querySelector(".kcol-visible")?.checked ?? true,
      order:   i
    }));
  };

  panel.querySelector("#kanban-cols-save").onclick = async () => {
    const cols = collectColumns();
    const r = await fetch("/api/config/kanban-columns", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ columns: cols })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Configuration Kanban enregistrée", "success");
    else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };

  panel.querySelector("#kanban-cols-reset").onclick = async () => {
    if (!confirm("Réinitialiser la configuration des tuiles Kanban aux valeurs par défaut ?")) return;
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
