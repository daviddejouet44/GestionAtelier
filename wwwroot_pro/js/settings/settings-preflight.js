import { authToken, showNotification, esc } from '../core.js';

// Identifiants canoniques des règles preflight (miroir de PreflightRuleIds côté C#)
const RULE_DEFINITIONS = [
  { id: "rgb_to_cmyk",             label: "Conversion RVB \u2192 CMJN",        description: "D\u00e9clenche une correction si le PDF contient des espaces couleur RVB." },
  { id: "bleed_insufficient",      label: "Fond perdu insuffisant",              description: "D\u00e9clenche une correction si le fond perdu est inf\u00e9rieur au seuil configur\u00e9." },
  { id: "spot_color_conservation", label: "Tons directs / Pantone",              description: "D\u00e9clenche une correction si des tons directs (Pantone\u2026) sont d\u00e9tect\u00e9s." },
  { id: "tac_reduction",           label: "R\u00e9duction TAC (taux d'\u00e9ncrage)", description: "D\u00e9clenche une correction si le TAC d\u00e9passe le seuil configur\u00e9 (actif apr\u00e8s \u00c9tape 5)." },
  { id: "trim_box_missing",        label: "TrimBox absente",                     description: "D\u00e9clenche une correction si la TrimBox (zone de rognage) est absente du PDF." },
  { id: "low_image_dpi",           label: "Images basse r\u00e9solution",         description: "D\u00e9clenche une correction si la r\u00e9solution d'image est inf\u00e9rieure au seuil configur\u00e9." },
];

export async function renderSettingsPreflight(panel) {
  panel.innerHTML = `<h3>Preflight</h3><p style="color:#6b7280;">Chargement...</p>`;

  let cfg      = { dropletStandard: "", dropletFondPerdu: "", droplets: [] };
  let autoCfg  = { enabled: false, maximumTacPercent: null, minimumBleedMm: null, minimumImageDpi: null };
  let rulesCfg = { maximumTacPercent: null, minimumBleedMm: null, minimumImageDpi: null, rules: [] };

  try {
    const [r1, r2, r3] = await Promise.all([
      fetch("/api/config/preflight",       { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()),
      fetch("/api/config/autopreflight",   { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()),
      fetch("/api/config/preflight-rules", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()),
    ]);
    if (r1.ok && r1.config) cfg = {
      dropletStandard:  r1.config.dropletStandard  || "",
      dropletFondPerdu: r1.config.dropletFondPerdu || "",
      droplets:         Array.isArray(r1.config.droplets) ? r1.config.droplets : [],
    };
    if (r2.ok && r2.config) autoCfg  = r2.config;
    if (r3.ok && r3.config) rulesCfg = r3.config;
  } catch(e) { /* use defaults */ }

  const mergedRules = RULE_DEFINITIONS.map(def => {
    const saved = (rulesCfg.rules || []).find(r => r.id === def.id);
    return {
      id: def.id,
      label: saved?.label || def.label,
      description: def.description,
      enabled: saved?.enabled ?? false,
      targetDropletName: saved?.targetDropletName || "",
    };
  });

  panel.innerHTML = `
    <h3>Preflight</h3>

    <!-- Section 1 : Activation & seuils globaux -->
    <div class="settings-section-card" style="max-width:820px;margin-bottom:24px;padding:20px;border:1px solid #e5e7eb;border-radius:10px;background:#f9fafb;">
      <h4 style="margin:0 0 4px;">Analyse PDF automatique</h4>
      <p style="color:#6b7280;font-size:13px;margin:0 0 16px;">
        Activez l'analyse PDF et définissez les seuils globaux. Ces valeurs sont lues par le moteur avant chaque analyse.
        Quand désactivé, le comportement actuel est strictement inchangé.
      </p>
      <div class="settings-form-group" style="display:flex;align-items:center;gap:12px;margin-bottom:16px;">
        <label style="font-weight:600;min-width:240px;">Activer l'analyse PDF automatique</label>
        <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
          <input type="checkbox" id="auto-preflight-enabled" ${autoCfg.enabled ? "checked" : ""} style="width:16px;height:16px;" />
          <span style="font-size:13px;color:#374151;">Activé</span>
        </label>
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:16px;">
        <div class="settings-form-group">
          <label>TAC maximum (%)</label>
          <input type="number" id="auto-tac" min="0" max="400" step="1"
            value="${autoCfg.maximumTacPercent != null ? autoCfg.maximumTacPercent : ''}"
            class="settings-input" placeholder="Ex: 320 (vide = inactif)" />
          <p style="font-size:11px;color:#6b7280;margin-top:4px;">Actif après l'Étape 5 (Ghostscript).</p>
        </div>
        <div class="settings-form-group">
          <label>Fond perdu minimum (mm)</label>
          <input type="number" id="auto-bleed" min="0" max="50" step="0.5"
            value="${autoCfg.minimumBleedMm != null ? autoCfg.minimumBleedMm : ''}"
            class="settings-input" placeholder="Ex: 3 (vide = inactif)" />
        </div>
        <div class="settings-form-group">
          <label>Résolution image minimum (dpi)</label>
          <input type="number" id="auto-dpi" min="0" max="2400" step="1"
            value="${autoCfg.minimumImageDpi != null ? autoCfg.minimumImageDpi : ''}"
            class="settings-input" placeholder="Ex: 200 (vide = inactif)" />
        </div>
      </div>
      <button id="autopreflight-save" class="btn btn-primary" style="margin-top:8px;">Enregistrer activation &amp; seuils</button>
    </div>

    <!-- Section 2 : Règles d'analyse -->
    <div class="settings-section-card" style="max-width:820px;margin-bottom:24px;padding:20px;border:1px solid #e5e7eb;border-radius:10px;background:#f9fafb;">
      <h4 style="margin:0 0 4px;">Règles d'analyse</h4>
      <p style="color:#6b7280;font-size:13px;margin:0 0 4px;">
        Activez ou désactivez chaque règle individuellement. Une règle désactivée n'est jamais déclenchée.
      </p>
      <p style="color:#6b7280;font-size:12px;margin:0 0 16px;">
        Le moteur n'utilise aucun seuil en dur : seules les valeurs saisies ici sont prises en compte.
      </p>
      <div id="preflight-rules-list" style="display:flex;flex-direction:column;gap:8px;"></div>
      <div style="margin-top:16px;padding-top:16px;border-top:1px solid #e5e7eb;">
        <h5 style="margin:0 0 8px;font-size:13px;font-weight:600;color:#374151;">Seuils fins des règles</h5>
        <p style="font-size:12px;color:#6b7280;margin:0 0 12px;">Ces seuils sont prioritaires sur les seuils globaux ci-dessus. Laissez vide pour utiliser les seuils globaux.</p>
        <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:16px;">
          <div class="settings-form-group">
            <label style="font-size:12px;">TAC maximum (%)</label>
            <input type="number" id="rules-tac" min="0" max="400" step="1"
              value="${rulesCfg.maximumTacPercent != null ? rulesCfg.maximumTacPercent : ''}"
              class="settings-input" placeholder="Vide = seuil global" />
          </div>
          <div class="settings-form-group">
            <label style="font-size:12px;">Fond perdu minimum (mm)</label>
            <input type="number" id="rules-bleed" min="0" max="50" step="0.5"
              value="${rulesCfg.minimumBleedMm != null ? rulesCfg.minimumBleedMm : ''}"
              class="settings-input" placeholder="Vide = seuil global" />
          </div>
          <div class="settings-form-group">
            <label style="font-size:12px;">Résolution image (dpi)</label>
            <input type="number" id="rules-dpi" min="0" max="2400" step="1"
              value="${rulesCfg.minimumImageDpi != null ? rulesCfg.minimumImageDpi : ''}"
              class="settings-input" placeholder="Vide = seuil global" />
          </div>
        </div>
      </div>
      <button id="preflight-rules-save" class="btn btn-primary" style="margin-top:8px;">Enregistrer les règles</button>
    </div>

    <!-- Section 3 : Droplets Acrobat -->
    <div class="settings-section-card" style="max-width:820px;padding:20px;border:1px solid #e5e7eb;border-radius:10px;background:#f9fafb;">
      <h4 style="margin:0 0 4px;">Droplets Acrobat</h4>
      <p style="color:#6b7280;font-size:13px;margin:0 0 16px;">
        Configurez les chemins vers les droplets Acrobat (.exe) utilisés pour le Preflight.
        Les droplets sont lancés avec le fichier PDF en argument.
      </p>
      <div class="settings-form-group">
        <label>Droplet Preflight standard (colonne "Preflight")</label>
        <input type="text" id="preflight-standard" value="${(cfg.dropletStandard || '').replace(/"/g,'&quot;')}"
          class="settings-input" style="width: 100%; max-width: 600px;"
          placeholder="Ex: C:\\Droplets\\Preflight_Standard.exe" />
        <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour les fichiers dans la colonne "Corrections" (Preflight).</p>
      </div>
      <div class="settings-form-group">
        <label>Droplet Preflight avec fond perdu (colonne "Preflight avec fond perdu")</label>
        <input type="text" id="preflight-fondperdu" value="${(cfg.dropletFondPerdu || '').replace(/"/g,'&quot;')}"
          class="settings-input" style="width: 100%; max-width: 600px;"
          placeholder="Ex: C:\\Droplets\\Preflight_FondPerdu.exe" />
        <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisé pour les fichiers dans la colonne "Corrections et fond perdu".</p>
      </div>
      <hr style="margin:16px 0;border:none;border-top:1px solid #e5e7eb;" />
      <h5 style="margin-bottom:8px;">Droplets supplémentaires</h5>
      <p style="font-size:13px;color:#6b7280;margin-bottom:12px;">
        Ces droplets sont affichés dans le bouton "▶ Preflight ▾" et dans le panneau d'analyse PDF.
      </p>
      <div id="preflight-droplets-list" style="display:flex;flex-direction:column;gap:8px;max-width:700px;margin-bottom:12px;"></div>
      <button id="preflight-droplet-add" class="btn btn-sm" style="margin-bottom:16px;">+ Ajouter un droplet</button>
      <button id="preflight-save" class="btn btn-primary" style="margin-top:8px;">Enregistrer les droplets</button>
    </div>
  `;

  const rulesListEl = panel.querySelector("#preflight-rules-list");
  mergedRules.forEach(rule => {
    const row = document.createElement("div");
    row.style.cssText = "display:grid;grid-template-columns:280px 1fr;gap:12px;align-items:center;padding:10px 12px;border:1px solid #e5e7eb;border-radius:8px;background:white;";
    row.innerHTML = `
      <label style="display:flex;align-items:center;gap:10px;cursor:pointer;">
        <input type="checkbox" class="rule-enabled" data-rule-id="${esc(rule.id)}"
          ${rule.enabled ? "checked" : ""} style="width:15px;height:15px;flex-shrink:0;" />
        <span style="font-size:13px;font-weight:500;color:#111827;">${esc(rule.label)}</span>
      </label>
      <span style="font-size:12px;color:#6b7280;">${esc(rule.description)}</span>
    `;
    rulesListEl.appendChild(row);
  });

  const listEl = panel.querySelector("#preflight-droplets-list");

  function renderDropletRow(name, path) {
    const row = document.createElement("div");
    row.style.cssText = "display:flex;gap:8px;align-items:center;";
    row.innerHTML = `
      <input type="text" class="settings-input droplet-name" value="${esc(name)}"
        placeholder="Nom affiché (ex: Preflight Standard)" style="flex:1;" />
      <input type="text" class="settings-input droplet-path" value="${esc(path)}"
        placeholder="Chemin .exe" style="flex:2;" />
      <button class="btn btn-sm btn-droplet-delete" title="Supprimer" style="flex-shrink:0;">🗑</button>
    `;
    row.querySelector(".btn-droplet-delete").onclick = () => row.remove();
    listEl.appendChild(row);
  }

  cfg.droplets.forEach(d => renderDropletRow(d.name || "", d.path || ""));
  panel.querySelector("#preflight-droplet-add").onclick = () => renderDropletRow("", "");

  panel.querySelector("#autopreflight-save").onclick = async () => {
    const tacVal   = panel.querySelector("#auto-tac").value.trim();
    const bleedVal = panel.querySelector("#auto-bleed").value.trim();
    const dpiVal   = panel.querySelector("#auto-dpi").value.trim();
    const body = {
      enabled:           panel.querySelector("#auto-preflight-enabled").checked,
      maximumTacPercent: tacVal   !== "" ? parseFloat(tacVal)   : null,
      minimumBleedMm:    bleedVal !== "" ? parseFloat(bleedVal) : null,
      minimumImageDpi:   dpiVal   !== "" ? parseInt(dpiVal, 10) : null,
    };
    const r = await fetch("/api/config/autopreflight", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(body)
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Activation & seuils enregistrés", "success");
    else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };

  panel.querySelector("#preflight-rules-save").onclick = async () => {
    const tacVal   = panel.querySelector("#rules-tac").value.trim();
    const bleedVal = panel.querySelector("#rules-bleed").value.trim();
    const dpiVal   = panel.querySelector("#rules-dpi").value.trim();
    const rules = mergedRules.map(def => {
      const cb = rulesListEl.querySelector(`[data-rule-id="${def.id}"]`);
      return {
        id:                def.id,
        label:             def.label,
        enabled:           cb ? cb.checked : def.enabled,
        targetDropletName: def.targetDropletName || null,
      };
    });
    const body = {
      maximumTacPercent: tacVal   !== "" ? parseFloat(tacVal)   : null,
      minimumBleedMm:    bleedVal !== "" ? parseFloat(bleedVal) : null,
      minimumImageDpi:   dpiVal   !== "" ? parseInt(dpiVal, 10) : null,
      rules,
    };
    const r = await fetch("/api/config/preflight-rules", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(body)
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Règles d'analyse enregistrées", "success");
    else showNotification("❌ Erreur : " + (r.error || ""), "error");
  };

  panel.querySelector("#preflight-save").onclick = async () => {
    const dropletStandard  = panel.querySelector("#preflight-standard").value.trim();
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
