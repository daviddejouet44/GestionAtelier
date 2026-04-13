import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>Gestion des imports — Fiche de fabrication</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  try {
    const resp = await fetch("/api/config/fabrication-imports", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Gestion des imports — Fiche de fabrication</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Configurez les chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.</p>
    <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${cfg.media1Path || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
    <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${cfg.media2Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${cfg.media3Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${cfg.media4Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${cfg.typeDocumentPath || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <button id="fi-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("fi-save").onclick = async () => {
    const r = await fetch("/api/config/fabrication-imports", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({
        media1Path: document.getElementById("fi-media1").value.trim(),
        media2Path: document.getElementById("fi-media2").value.trim(),
        media3Path: document.getElementById("fi-media3").value.trim(),
        media4Path: document.getElementById("fi-media4").value.trim(),
        typeDocumentPath: document.getElementById("fi-typedoc").value.trim()
      })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins d'import enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}
