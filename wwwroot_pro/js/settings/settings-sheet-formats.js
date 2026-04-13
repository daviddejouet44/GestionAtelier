import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsSheetFormats(panel) {
  panel.innerHTML = `<h3>Formats feuille en machine</h3><p style="color:#6b7280;">Chargement...</p>`;

  let formats = [];
  try {
    formats = await fetch("/api/settings/sheet-formats", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
  } catch(e) { /* ignore */ }

  const listHtml = formats.length === 0
    ? '<span style="color:#9ca3af;font-size:13px;">Aucun format défini</span>'
    : formats.map(f => `<span style="display:inline-block;background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:13px;margin:3px;">${esc(f)}</span>`).join("");

  panel.innerHTML = `
    <h3>Formats feuille en machine</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Importez un fichier CSV contenant un format par ligne. Les formats existants seront remplacés.
    </p>
    <div class="settings-form-group">
      <label>Importer un CSV</label>
      <input type="file" id="sheet-formats-csv-input" accept=".csv,.txt" class="settings-input" />
      <button id="sheet-formats-csv-import" class="btn btn-primary" style="margin-top:8px;">Importer</button>
      <div id="sheet-formats-import-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
    <div style="margin-top:20px;">
      <h4 style="margin-bottom:10px;">Formats actuels (${formats.length})</h4>
      <div id="sheet-formats-list">${listHtml}</div>
    </div>
  `;

  panel.querySelector("#sheet-formats-csv-import").onclick = async () => {
    const fileInput = panel.querySelector("#sheet-formats-csv-input");
    const msgEl = panel.querySelector("#sheet-formats-import-msg");
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "Sélectionnez un fichier CSV";
      return;
    }
    const formData = new FormData();
    formData.append("file", fileInput.files[0]);
    try {
      const r = await fetch("/api/settings/sheet-formats/import", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: formData
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = `✅ ${r.count} format(s) importé(s)`;
        panel._loaded = false;
        await renderSettingsSheetFormats(panel);
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
