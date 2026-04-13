import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsWorkTypes(panel) {
  panel.innerHTML = `<h3>Types de travail</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshWorkTypesPanel(panel);
}

export async function refreshWorkTypesPanel(panel) {
  let types = [];
  try {
    const resp = await fetch("/api/config/work-types").then(r => r.json());
    types = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Types de travail</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des types de travail disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="wt-new-name" placeholder="Nouveau type" class="settings-input" style="max-width:300px;" />
      <button id="wt-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        📥 Importer CSV
        <input type="file" id="wt-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="wt-list">
      ${types.length === 0
        ? '<p style="color:#9ca3af;">Aucun type configuré</p>'
        : types.map(t => {
            const escapedType = t.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `<div style="display:flex;align-items:center;gap:10px;padding:8px 12px;background:white;border:1px solid #e5e7eb;border-radius:6px;margin-bottom:6px;">
              <span style="flex:1;font-size:13px;">${t}</span>
              <button class="btn btn-sm wt-delete" data-name="${escapedType}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("wt-add").onclick = async () => {
    const name = document.getElementById("wt-new-name").value.trim();
    if (!name) { alert("Entrez un type de travail"); return; }
    const formData = new FormData();
    const blob = new Blob([name], { type: "text/plain" });
    formData.append("file", blob, "type.csv");
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Type ajouté", "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };

  document.getElementById("wt-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${r.count || 0} type(s) importé(s)`, "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
    e.target.value = "";
  };

  panel.querySelectorAll(".wt-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/work-types/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("Type supprimé", "success");
        panel._loaded = false;
        await refreshWorkTypesPanel(panel);
      } else { alert("Erreur : " + (r.error || "")); }
    };
  });
}
