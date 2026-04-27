import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>Import catalogues papiers</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  try {
    const resp = await fetch("/api/config/fabrication-imports", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Import catalogues papiers</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Configurez les chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.</p>
    <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${esc(cfg.media1Path || '')}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
    <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${esc(cfg.media2Path || '')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${esc(cfg.media3Path || '')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${esc(cfg.media4Path || '')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${esc(cfg.typeDocumentPath || '')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <button id="fi-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
    <div id="fi-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  panel.querySelector("#fi-save").onclick = async () => {
    const msgEl = panel.querySelector("#fi-msg");
    const r = await fetch("/api/config/fabrication-imports", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({
        media1Path: panel.querySelector("#fi-media1").value.trim(),
        media2Path: panel.querySelector("#fi-media2").value.trim(),
        media3Path: panel.querySelector("#fi-media3").value.trim(),
        media4Path: panel.querySelector("#fi-media4").value.trim(),
        typeDocumentPath: panel.querySelector("#fi-typedoc").value.trim()
      })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Chemins d'import enregistrés", "success");
      if (msgEl) { msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Chemins enregistrés"; }
      // Re-fetch and repopulate inputs so values are visible after save
      try {
        const resp2 = await fetch("/api/config/fabrication-imports", {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r2 => r2.json());
        if (resp2.ok && resp2.config) {
          const c = resp2.config;
          panel.querySelector("#fi-media1").value = c.media1Path || '';
          panel.querySelector("#fi-media2").value = c.media2Path || '';
          panel.querySelector("#fi-media3").value = c.media3Path || '';
          panel.querySelector("#fi-media4").value = c.media4Path || '';
          panel.querySelector("#fi-typedoc").value = c.typeDocumentPath || '';
        }
      } catch(e) { /* ignore reload errors */ }
      panel._loaded = false;
    } else {
      if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
    }
  };
}
