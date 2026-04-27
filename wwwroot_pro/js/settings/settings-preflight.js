import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPreflight(panel) {
  panel.innerHTML = `<h3>Preflight — Droplets Acrobat</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { dropletStandard: "", dropletFondPerdu: "", droplets: [] };
  try {
    const resp = await fetch("/api/config/preflight", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) {
      cfg = {
        dropletStandard: resp.config.dropletStandard || "",
        dropletFondPerdu: resp.config.dropletFondPerdu || "",
        droplets: Array.isArray(resp.config.droplets) ? resp.config.droplets : []
      };
    }
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Preflight — Droplets Acrobat</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:20px;">
      Configurez les chemins vers les droplets Acrobat (.exe) utilisés pour le Preflight automatique.
      Les droplets sont lancés avec le fichier PDF en argument.
    </p>
    <div class="settings-form-group">
      <label>Droplet Preflight standard (colonne "Preflight")</label>
      <input type="text" id="preflight-standard" value="${(cfg.dropletStandard || '').replace(/"/g,'&quot;')}" class="settings-input" style="width: 100%; max-width: 600px;" placeholder="Ex: C:\\Droplets\\Preflight_Standard.exe" />
      <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour les fichiers dans la colonne "Corrections" (Preflight).</p>
    </div>
    <div class="settings-form-group">
      <label>Droplet Preflight avec fond perdu (colonne "Preflight avec fond perdu")</label>
      <input type="text" id="preflight-fondperdu" value="${(cfg.dropletFondPerdu || '').replace(/"/g,'&quot;')}" class="settings-input" style="width: 100%; max-width: 600px;" placeholder="Ex: C:\\Droplets\\Preflight_FondPerdu.exe" />
      <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour les fichiers dans la colonne "Corrections et fond perdu" (Preflight avec fond perdu).</p>
    </div>

    <hr style="margin:24px 0;border:none;border-top:1px solid #e5e7eb;" />
    <h4 style="margin-bottom:8px;">Droplets supplémentaires</h4>
    <p style="font-size:13px;color:#6b7280;margin-bottom:12px;">
      Ces droplets sont affichés dans le bouton "▶ Preflight ▾" de la tuile "Début de production"
      quand les tuiles Preflight sont masquées (configurable dans <em>Tuiles Kanban</em>).
    </p>
    <div id="preflight-droplets-list" style="display:flex;flex-direction:column;gap:8px;max-width:700px;margin-bottom:12px;"></div>
    <button id="preflight-droplet-add" class="btn btn-sm" style="margin-bottom:16px;">+ Ajouter un droplet</button>

    <button id="preflight-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer</button>
  `;

  const listEl = panel.querySelector("#preflight-droplets-list");

  function renderDropletRow(name, path) {
    const row = document.createElement("div");
    row.style.cssText = "display:flex;gap:8px;align-items:center;";
    row.innerHTML = `
      <input type="text" class="settings-input droplet-name" value="${esc(name)}" placeholder="Nom affiché (ex: Preflight Standard)" style="flex:1;" />
      <input type="text" class="settings-input droplet-path" value="${esc(path)}" placeholder="Chemin .exe" style="flex:2;" />
      <button class="btn btn-sm btn-droplet-delete" title="Supprimer" style="flex-shrink:0;">🗑</button>
    `;
    row.querySelector(".btn-droplet-delete").onclick = () => row.remove();
    listEl.appendChild(row);
  }

  cfg.droplets.forEach(d => renderDropletRow(d.name || "", d.path || ""));

  panel.querySelector("#preflight-droplet-add").onclick = () => renderDropletRow("", "");

  panel.querySelector("#preflight-save").onclick = async () => {
    const dropletStandard = panel.querySelector("#preflight-standard").value.trim();
    const dropletFondPerdu = panel.querySelector("#preflight-fondperdu").value.trim();
    const droplets = Array.from(listEl.querySelectorAll("div")).map(row => ({
      name: row.querySelector(".droplet-name")?.value.trim() || "",
      path: row.querySelector(".droplet-path")?.value.trim() || ""
    })).filter(d => d.path);
    const r = await fetch("/api/config/preflight", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ dropletStandard, dropletFondPerdu, droplets })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Configuration Preflight enregistrée", "success");
    else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };
}
