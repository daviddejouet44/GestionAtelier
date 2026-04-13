import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPrintEngines(panel) {
  panel.innerHTML = `<h3>Moteurs d'impression</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshPrintEnginesPanel(panel);
}

export function extractEngineName(e) {
  return (typeof e === "object" && e !== null) ? (e.name || "") : String(e || "");
}

export async function refreshPrintEnginesPanel(panel) {
  let engines = [];
  try {
    const resp = await fetch("/api/config/print-engines").then(r => r.json());
    engines = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Moteurs d'impression</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des moteurs d'impression disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="pe-new-name" placeholder="Nom du moteur" class="settings-input" style="max-width:200px;" />
      <input type="text" id="pe-new-ip" placeholder="IP / URL (optionnel)" class="settings-input" style="max-width:250px;" />
      <button id="pe-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        Importer CSV
        <input type="file" id="pe-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="pe-list">
      ${engines.length === 0
        ? '<p style="color:#9ca3af;">Aucun moteur configuré</p>'
        : engines.map(e => {
            const name = extractEngineName(e);
            const ip = (typeof e === "object" && e !== null) ? (e.ip || "") : "";
            const safeName = name.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `
          <div style="display: flex; align-items: center; gap: 10px; padding: 8px 12px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 6px;">
            <span style="flex: 1; font-size: 13px;">${name}</span>
            ${ip ? `<span style="font-size: 12px; color: #6b7280; font-family: monospace;">${ip}</span>` : ""}
            <button class="btn btn-sm pe-delete" data-name="${safeName}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
          </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("pe-add").onclick = async () => {
    const name = document.getElementById("pe-new-name").value.trim();
    const ip   = document.getElementById("pe-new-ip").value.trim();
    if (!name) { alert("Entrez un nom"); return; }
    const r = await fetch("/api/config/print-engines", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ name, ip })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("Moteur ajouté", "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("pe-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const text = await file.text();
    const lines = text.split(/[\r\n]+/).filter(Boolean);
    const enginesList = [];
    for (const line of lines) {
      const parts = line.split(";");
      const name = (parts[0] || "").trim();
      const ip   = (parts[1] || "").trim();
      const knownHeaders = ["presse", "nom", "name", "moteur", "engine"];
      if (!name || knownHeaders.includes(name.toLowerCase())) continue;
      enginesList.push({ name, ip });
    }
    if (enginesList.length === 0) { alert("Aucun moteur trouvé dans le fichier"); return; }
    const r = await fetch("/api/config/print-engines/import", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ engines: enginesList })
    }).then(r => r.json());
    if (r.ok) {
      showNotification(`${r.count || enginesList.length} moteurs importés`, "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
    e.target.value = "";
  };

  panel.querySelectorAll(".pe-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/print-engines/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("Moteur supprimé", "success");
        panel._loaded = false;
        await refreshPrintEnginesPanel(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}
