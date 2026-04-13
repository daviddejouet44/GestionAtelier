import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsFaconnage(panel) {
  panel.innerHTML = `<h3>Options de façonnage</h3><p style="color:#6b7280;">Chargement...</p>`;

  let options = [];
  try {
    options = await fetch("/api/settings/faconnage-options", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
  } catch(e) { /* ignore */ }

  const listHtml = options.length === 0
    ? '<span style="color:#9ca3af;font-size:13px;">Aucune option définie</span>'
    : options.map(o => `<span style="display:inline-block;background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:13px;margin:3px;">${esc(o)}</span>`).join("");

  panel.innerHTML = `
    <h3>Options de façonnage</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Importez un fichier CSV contenant une option par ligne (ou par colonne). Les options existantes seront remplacées.
    </p>
    <div class="settings-form-group">
      <label>Importer un CSV</label>
      <input type="file" id="faconnage-csv-input" accept=".csv,.txt" class="settings-input" />
      <button id="faconnage-csv-import" class="btn btn-primary" style="margin-top:8px;">Importer</button>
      <div id="faconnage-import-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
    <div style="margin-top:20px;">
      <h4 style="margin-bottom:10px;">Options actuelles (${options.length})</h4>
      <div id="faconnage-options-list">${listHtml}</div>
    </div>
  `;

  panel.querySelector("#faconnage-csv-import").onclick = async () => {
    const fileInput = panel.querySelector("#faconnage-csv-input");
    const msgEl = panel.querySelector("#faconnage-import-msg");
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "Sélectionnez un fichier CSV";
      return;
    }
    const formData = new FormData();
    formData.append("file", fileInput.files[0]);
    try {
      const r = await fetch("/api/settings/faconnage-import", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: formData
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = `✅ ${r.count} option(s) importée(s)`;
        panel._loaded = false;
        await renderSettingsFaconnage(panel);
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}
