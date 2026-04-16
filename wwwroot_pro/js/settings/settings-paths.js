import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPaths(panel) {
  panel.innerHTML = `<h3>Chemins d'accès</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { recycleBinPath: "", acrobatExePath: "", fieryPaths: [] };
  try {
    const resp = await fetch("/api/config/paths", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  const fieryPaths = Array.isArray(cfg.fieryPaths) ? cfg.fieryPaths : [];

  panel.innerHTML = `
    <h3>Chemins d'accès</h3>

    <div class="settings-form-group">
      <label>Chemin corbeille</label>
      <input type="text" id="paths-recycle" value="${esc(cfg.recycleBinPath || '')}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Corbeille" />
    </div>
    <div class="settings-form-group">
      <label>Chemin Adobe Acrobat Pro (Acrobat.exe)</label>
      <input type="text" id="paths-acrobat" value="${esc(cfg.acrobatExePath || '')}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Program Files\\Adobe\\Acrobat DC\\Acrobat\\Acrobat.exe" />
      <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour l'action BAT Simple (ouvrir dans Acrobat Pro).</p>
    </div>

    <div class="settings-form-group" style="margin-top:20px;">
      <label style="font-size:14px;font-weight:600;color:#374151;">Chemins Fiery / systèmes d'impression</label>
      <p style="font-size:13px;color:#6b7280;margin-bottom:10px;">Ajoutez un ou plusieurs chemins vers les dossiers Fiery ou autres systèmes d'impression.</p>
      <div id="fiery-paths-list" style="display:flex;flex-direction:column;gap:6px;max-width:600px;margin-bottom:10px;">
      </div>
      <button id="fiery-add-btn" class="btn btn-sm" style="margin-top:4px;">＋ Ajouter un chemin Fiery</button>
    </div>

    <button id="paths-save" class="btn btn-primary" style="margin-top:20px;">Enregistrer les chemins</button>
    <div id="paths-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  const listEl = panel.querySelector("#fiery-paths-list");

  function addFieryPathRow(value) {
    const row = document.createElement("div");
    row.style.cssText = "display:flex;gap:8px;align-items:center;";
    const input = document.createElement("input");
    input.type = "text";
    input.value = value || "";
    input.className = "settings-input fiery-path-input";
    input.placeholder = "Ex: C:\\Fiery\\Hotfolders";
    input.style.cssText = "flex:1;";
    const removeBtn = document.createElement("button");
    removeBtn.className = "btn btn-sm";
    removeBtn.textContent = "✕";
    removeBtn.style.cssText = "color:#ef4444;border-color:#ef4444;flex-shrink:0;";
    removeBtn.onclick = () => row.remove();
    row.appendChild(input);
    row.appendChild(removeBtn);
    listEl.appendChild(row);
  }

  fieryPaths.forEach(p => addFieryPathRow(p));
  if (fieryPaths.length === 0) addFieryPathRow("");

  panel.querySelector("#fiery-add-btn").onclick = () => addFieryPathRow("");

  panel.querySelector("#paths-save").onclick = async () => {
    const msgEl = panel.querySelector("#paths-msg");
    const recycleBinPath = panel.querySelector("#paths-recycle").value.trim();
    const acrobatExePath = panel.querySelector("#paths-acrobat").value.trim();
    const fieryPathsNew = Array.from(panel.querySelectorAll(".fiery-path-input"))
      .map(i => i.value.trim()).filter(v => v.length > 0);
    const r = await fetch("/api/config/paths", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ recycleBinPath, acrobatExePath, fieryPaths: fieryPathsNew })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Chemins enregistrés", "success");
      if (msgEl) { msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Chemins enregistrés"; }
      panel._loaded = false;
    } else {
      if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
    }
  };
}
