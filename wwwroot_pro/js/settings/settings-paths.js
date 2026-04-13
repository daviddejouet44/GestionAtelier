import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPaths(panel) {
  panel.innerHTML = `<h3>Chemins d'accès aux dossiers</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { hotfoldersRoot: "C:\\Flux", recycleBinPath: "", acrobatExePath: "" };
  try {
    const resp = await fetch("/api/config/paths", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Chemins d'accès aux dossiers</h3>
    <div class="settings-form-group">
      <label>Racine des hotfolders (GA_HOTFOLDERS_ROOT)</label>
      <input type="text" id="paths-hotfolders" value="${cfg.hotfoldersRoot || 'C:\\\\Flux'}" class="settings-input" style="width: 100%; max-width: 500px;" />
    </div>
    <div class="settings-form-group">
      <label>Chemin corbeille</label>
      <input type="text" id="paths-recycle" value="${cfg.recycleBinPath || ''}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Corbeille" />
    </div>
    <div class="settings-form-group">
      <label>Chemin Adobe Acrobat Pro (Acrobat.exe)</label>
      <input type="text" id="paths-acrobat" value="${(cfg.acrobatExePath || '').replace(/"/g,'&quot;')}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Program Files\\Adobe\\Acrobat DC\\Acrobat\\Acrobat.exe" />
      <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour l'action BAT Simple (ouvrir dans Acrobat Pro).</p>
    </div>
    <button id="paths-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("paths-save").onclick = async () => {
    const hotfoldersRoot = document.getElementById("paths-hotfolders").value.trim();
    const recycleBinPath = document.getElementById("paths-recycle").value.trim();
    const acrobatExePath = document.getElementById("paths-acrobat").value.trim();
    const r = await fetch("/api/config/paths", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ hotfoldersRoot, recycleBinPath, acrobatExePath })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}
