import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsBatConfig(panel) {
  panel.innerHTML = `
    <h3>Configuration BAT</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">Paramétrez l'ensemble du workflow BAT : chemins de travail, routage hotfolder et commandes.</p>
    <div class="settings-section-card">
      <h4>Workflow BAT</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">Chargement…</p>
    </div>
    <div class="settings-section-card">
      <h4>Routage Hotfolder BAT PrismaPrepare</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">Chargement…</p>
    </div>
    <div class="settings-section-card">
      <h4>Commande BAT</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">Chargement…</p>
    </div>
  `;

  // Load all data in parallel
  let intCfg = { tempCopyPath: "", prismaPrepareOutputPath: "" };
  let batCmd = "";
  let batAlertDelayHours = 48;
  let batPlanningAlertHours = 24;
  let batPlanningDays = 5;
  let batSimpleDropletPath = "";
  let routings = [];
  let types = [];
  let batPapierCfg = { enabled: false, hotfolder: "" };
  let batValidationLinkCfg = {
    enabled: false,
    tokenExpiryHours: 72,
    subjectTemplate: "Validation BAT — {{fileName}}",
    bodyTemplate: "Bonjour,\n\nVeuillez consulter votre BAT et nous indiquer votre décision via ce lien :\n\n{{batLink}}\n\nCe lien est valable {{expiryHours}}h.\n\nCordialement"
  };
  try {
    const [r1, r2, r3, r4, r9, r10] = await Promise.all([
      fetch("/api/config/integrations", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch("/api/config/bat-command").then(r => r.json()).catch(() => ({})),
      fetch("/api/config/hotfolder-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => []),
      fetch("/api/settings/bat-papier-config", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch("/api/settings/bat-validation-link", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ ok: false, config: {} }))
    ]);
    if (r1.ok && r1.config) intCfg = r1.config;
    if (r2.ok) { batCmd = r2.command || ""; batAlertDelayHours = r2.batAlertDelayHours ?? 48; batSimpleDropletPath = r2.batSimpleDropletPath || ""; batPlanningAlertHours = r2.batPlanningAlertHours ?? 24; batPlanningDays = r2.batPlanningDays ?? 5; }
    if (Array.isArray(r3)) routings = r3;
    if (Array.isArray(r4)) types = r4;
    if (r9.ok && r9.config) batPapierCfg = r9.config;
    if (r10.ok && r10.config) batValidationLinkCfg = { ...batValidationLinkCfg, ...r10.config };
  } catch(e) { /* use defaults */ }

  const typeOptions = types.map(t => `<option value="${t.replace(/"/g,'&quot;')}">${t}</option>`).join("");
  const routingsHtml = routings.length === 0
    ? '<p style="color:#9ca3af;margin:0;">Aucun routage configuré</p>'
    : routings.map(r => `
        <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;">
          <div style="flex:0 0 200px;"><strong style="font-size:13px;color:#111827;">${r.typeTravail}</strong></div>
          <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.hotfolderPath || '—'}</div>
          <button class="btn btn-sm hfr-edit" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-path="${(r.hotfolderPath||'').replace(/"/g,'&quot;')}">Modifier</button>
          <button class="btn btn-sm hfr-delete" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
        </div>
      `).join("");

  panel.innerHTML = `
    <h3>Configuration BAT</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">Paramétrez l'ensemble du workflow BAT : chemins de travail, routage hotfolder et commandes.</p>

    <div class="settings-section-card">
      <h4>Lien de validation BAT (clients non-web)</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Activez l'envoi d'un lien public de validation BAT pour les clients hors portail.</p>
      <div style="display:flex;align-items:center;gap:10px;margin-bottom:16px;">
        <input type="checkbox" id="bat-validation-link-enabled" ${batValidationLinkCfg.enabled ? 'checked' : ''} />
        <label for="bat-validation-link-enabled" style="font-size:14px;font-weight:500;color:#374151;">Activer l'envoi de lien de validation BAT</label>
      </div>
      <div class="settings-form-group" style="margin-top:10px;margin-bottom:12px;">
        <label>URL publique du serveur (ex: https://portail.daviddejouet.com)</label>
        <input type="text" id="bat-validation-link-base-url" value="${esc(batValidationLinkCfg.publicBaseUrl || '')}" class="settings-input" style="width:100%;max-width:480px;" placeholder="https://portail.daviddejouet.com" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">URL utilisée pour générer le lien envoyé au client. Si vide, utilise l'adresse locale du serveur (déconseillé si tunnel Cloudflare).</p>
      </div>
      <div class="settings-form-group" style="margin-bottom:12px;">
        <label>Durée de validité du lien (heures)</label>
        <input type="number" id="bat-validation-link-expiry" min="1" class="settings-input" style="width:160px;" value="${batValidationLinkCfg.tokenExpiryHours || 72}" />
      </div>
      <div class="settings-form-group" style="margin-bottom:12px;">
        <label>Objet du mail</label>
        <input type="text" id="bat-validation-link-subject" class="settings-input settings-input-wide" value="${esc(batValidationLinkCfg.subjectTemplate || '')}" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Variables : <code>{{fileName}}</code>, <code>{{numeroDossier}}</code></p>
      </div>
      <div class="settings-form-group" style="margin-bottom:12px;">
        <label>Corps du mail</label>
        <textarea id="bat-validation-link-body" class="settings-input settings-input-wide" rows="7">${esc(batValidationLinkCfg.bodyTemplate || '')}</textarea>
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Variables : <code>{{batLink}}</code>, <code>{{expiryHours}}</code>, <code>{{fileName}}</code>, <code>{{numeroDossier}}</code></p>
      </div>
      <button id="bat-validation-link-save" class="btn btn-primary">Enregistrer</button>
      <span id="bat-validation-link-saved" style="display:none;color:#16a34a;font-size:13px;margin-left:10px;">✅ Enregistré</span>
    </div>

    <div class="settings-section-card">
      <h4>BAT Papier</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Activez et configurez le workflow BAT Papier (impression physique d'un justificatif).</p>
      <div style="display:flex;align-items:center;gap:10px;margin-bottom:16px;">
        <input type="checkbox" id="bat-papier-enabled" ${batPapierCfg.enabled ? 'checked' : ''} />
        <label for="bat-papier-enabled" style="font-size:14px;font-weight:500;color:#374151;">Activer le BAT Papier</label>
      </div>
      <div class="settings-form-group" style="margin-bottom:16px;">
        <label>Hotfolder BAT Papier</label>
        <input type="text" id="bat-papier-hotfolder" value="${esc(batPapierCfg.hotfolder || '')}" class="settings-input settings-input-wide" placeholder="Ex: C:\\FluxAtelier\\BATPapier" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Dossier dans lequel le PDF est déposé pour impression BAT Papier.</p>
      </div>
      <button id="bat-papier-cfg-save" class="btn btn-primary">Enregistrer la configuration BAT Papier</button>
    </div>

    <div class="settings-section-card">
      <h4>Workflow BAT</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Configurez les dossiers de travail utilisés par le workflow BAT.</p>
      <div class="settings-form-group">
        <label>Chemin TEMP_COPY</label>
        <input type="text" id="int-temp-copy" value="${(intCfg.tempCopyPath || '').replace(/"/g,'&quot;')}" class="settings-input settings-input-wide" placeholder="Ex: C:\\FluxAtelier\\Base\\TEMP_COPY" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Dossier dans lequel le bouton BAT copie le PDF source pour conserver son nom d'origine.</p>
      </div>
      <div class="settings-form-group">
        <label>Chemin sortie PrismaPrepare</label>
        <input type="text" id="int-prisma-output" value="${(intCfg.prismaPrepareOutputPath || '').replace(/"/g,'&quot;')}" class="settings-input settings-input-wide" placeholder="Ex: C:\\FluxAtelier\\Base\\Sortie" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Dossier dans lequel PrismaPrepare dépose le fichier <code>Epreuve.pdf</code>.</p>
      </div>
      <button id="int-save" class="btn btn-primary">Enregistrer le workflow</button>
    </div>

    <div class="settings-section-card">
      <h4>Routage Hotfolder BAT PrismaPrepare</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Configurez le chemin du hotfolder PrismaPrepare pour chaque type de travail.</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:16px;">
        <div>
          <label style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:0.05em;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="hfr-type" class="settings-input" style="min-width:200px;">
            <option value="">— Sélectionner —</option>
            ${typeOptions}
          </select>
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:0.05em;color:#6b7280;display:block;margin-bottom:4px;">Chemin hotfolder PrismaPrepare</label>
          <input type="text" id="hfr-path" placeholder="Ex: C:\\Flux\\PrismaPrepare\\Brochures" class="settings-input settings-input-wide" />
        </div>
        <button id="hfr-save" class="btn btn-primary">Enregistrer</button>
      </div>
      <div id="hfr-list">${routingsHtml}</div>
    </div>

    <div class="settings-section-card">
      <h4>BAT Simple</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Configuration du bouton "BAT Simple" (ouvrir dans un droplet).</p>
      <div class="settings-form-group">
        <label>Chemin du droplet BAT Simple</label>
        <input type="text" id="bat-simple-droplet-input" value="${(batSimpleDropletPath || '').replace(/"/g,'&quot;')}" class="settings-input settings-input-wide" placeholder="Ex: C:\\Droplets\\MonDroplet.exe" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Exécutable lancé par le bouton "BAT Simple" avec le fichier en paramètre.</p>
      </div>
      <div class="settings-form-group" style="margin-top:16px;">
        <label>Délai alerte BAT sans réponse (heures)</label>
        <input type="number" id="bat-alert-delay-input" value="${batAlertDelayHours}" min="1" class="settings-input" style="width:120px;" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Affiche une alerte si un BAT reste sans validation/refus après ce délai. Défaut : 48h.</p>
      </div>
      <h4 style="margin-top:20px;">Planning des BAT à envoyer</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Mini-planning affiché dans l'onglet BAT, alimenté par la « Date d'envoi du BAT au client » de chaque fiche.</p>
      <div class="settings-form-group">
        <label>Alerte avant envoi du BAT (heures)</label>
        <input type="number" id="bat-planning-alert-input" value="${batPlanningAlertHours}" min="1" class="settings-input" style="width:120px;" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Signale un dossier lorsque l'échéance d'envoi du BAT est à H-<em>x</em> ou dépassée. Défaut : 24h.</p>
      </div>
      <div class="settings-form-group" style="margin-top:12px;">
        <label>Nombre de jours affichés</label>
        <input type="number" id="bat-planning-days-input" value="${batPlanningDays}" min="1" max="14" class="settings-input" style="width:120px;" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">Nombre de jours du planning à partir d'aujourd'hui. Défaut : 5 jours.</p>
      </div>
      <button id="bat-cmd-save" class="btn btn-primary">Enregistrer</button>
    </div>

    <div class="settings-section-card" style="background:#eff6ff;border-color:#bfdbfe;">
      <p style="font-size:13px;color:#1e40af;margin:0;">📧 Les <strong>templates email BAT</strong>, <strong>début de production</strong> et <strong>fin de production</strong> ont été déplacés dans l'onglet <strong>"Templates email"</strong>.</p>
    </div>
  `;

  // Lien de validation BAT save
  panel.querySelector("#bat-validation-link-save").onclick = async () => {
    const enabled = panel.querySelector("#bat-validation-link-enabled").checked;
    const tokenExpiryHours = Math.max(1, parseInt(panel.querySelector("#bat-validation-link-expiry").value || "72", 10) || 72);
    const publicBaseUrl = (panel.querySelector("#bat-validation-link-base-url")?.value || "").trim();
    const subjectTemplate = panel.querySelector("#bat-validation-link-subject").value || "";
    const bodyTemplate = panel.querySelector("#bat-validation-link-body").value || "";
    const r = await fetch("/api/settings/bat-validation-link", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": "Bearer " + authToken },
      body: JSON.stringify({ enabled, tokenExpiryHours, publicBaseUrl, subjectTemplate, bodyTemplate })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      const savedEl = panel.querySelector("#bat-validation-link-saved");
      if (savedEl) { savedEl.style.display = ''; setTimeout(() => { savedEl.style.display = 'none'; }, 2500); }
      showNotification("✅ Paramètre lien de validation BAT enregistré", "success");
    } else showNotification("❌ " + (r.error || "Erreur"), "error");
  };

  panel.querySelector("#bat-papier-cfg-save").onclick = async () => {
    const enabled = panel.querySelector("#bat-papier-enabled").checked;
    const hotfolder = panel.querySelector("#bat-papier-hotfolder").value.trim();
    const r = await fetch("/api/settings/bat-papier-config", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ enabled, hotfolder })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) showNotification("✅ Configuration BAT Papier enregistrée", "success");
    else showNotification("❌ " + (r.error || "Erreur"), "error");
  };

  // Workflow BAT save
  panel.querySelector("#int-save").onclick = async () => {
    const tempCopyPath = panel.querySelector("#int-temp-copy").value.trim();
    const prismaPrepareOutputPath = panel.querySelector("#int-prisma-output").value.trim();
    const r = await fetch("/api/config/integrations", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ tempCopyPath, prismaPrepareOutputPath })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Workflow BAT enregistré", "success");
    else alert("Erreur : " + (r.error || ""));
  };

  // Hotfolder routing save
  panel.querySelector("#hfr-save").onclick = async () => {
    const typeTravail = panel.querySelector("#hfr-type").value;
    const hotfolderPath = panel.querySelector("#hfr-path").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!hotfolderPath) { alert("Entrez un chemin hotfolder"); return; }
    const r = await fetch("/api/config/hotfolder-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ typeTravail, hotfolderPath })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Routage enregistré", "success");
      panel._loaded = false;
      await renderSettingsBatConfig(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };

  panel.querySelectorAll(".hfr-edit").forEach(btn => {
    btn.onclick = () => {
      panel.querySelector("#hfr-type").value = btn.dataset.type;
      panel.querySelector("#hfr-path").value = btn.dataset.path;
    };
  });

  panel.querySelectorAll(".hfr-delete").forEach(btn => {
    btn.onclick = async () => {
      const typeTravail = btn.dataset.type;
      if (!confirm(`Supprimer le routage pour "${typeTravail}" ?`)) return;
      const r = await fetch(`/api/config/hotfolder-routing/${encodeURIComponent(typeTravail)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("Routage supprimé", "success");
        panel._loaded = false;
        await renderSettingsBatConfig(panel);
      } else { alert("Erreur : " + (r.error || "")); }
    };
  });

  // BAT simple/delay save
  panel.querySelector("#bat-cmd-save").onclick = async () => {
    const batSimpleDropletPathNew = panel.querySelector("#bat-simple-droplet-input").value.trim();
    const rawDelay = parseInt(panel.querySelector("#bat-alert-delay-input").value);
    const batAlertDelayHoursNew = (rawDelay > 0) ? rawDelay : 48;
    const rawPlanAlert = parseInt(panel.querySelector("#bat-planning-alert-input").value);
    const batPlanningAlertHoursNew = (rawPlanAlert > 0) ? rawPlanAlert : 24;
    const rawPlanDays = parseInt(panel.querySelector("#bat-planning-days-input").value);
    const batPlanningDaysNew = (rawPlanDays > 0) ? Math.min(rawPlanDays, 14) : 5;
    const r = await fetch("/api/config/bat-command", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ command: batCmd, batAlertDelayHours: batAlertDelayHoursNew, batSimpleDropletPath: batSimpleDropletPathNew, batPlanningAlertHours: batPlanningAlertHoursNew, batPlanningDays: batPlanningDaysNew })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Configuration BAT Simple enregistrée", "success");
    else alert("Erreur");
  };
}

// ======================================================
// WORKFLOW BAT (kept for backward compat)
// ======================================================
export async function renderSettingsIntegrations(panel) {
  return renderSettingsBatConfig(panel);
}

export async function renderSettingsBatCommand(panel) {
  let cmd = "";
  let alertDelayHours = 48;
  try {
    const r = await fetch("/api/config/bat-command").then(r => r.json());
    if (r.ok) { cmd = r.command || ""; alertDelayHours = r.batAlertDelayHours ?? 48; }
  } catch(e) { /* use default */ }
  panel.innerHTML = `
    <h3>Commande BAT</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Utilisez <code>{filePath}</code>, <code>{type}</code> et <code>{qty}</code> comme variables.</p>
    <div class="settings-form-group">
      <label>Commande</label>
      <input type="text" id="bat-cmd-input" value="${(cmd || '').replace(/"/g,'&quot;')}" class="settings-input" style="width:100%;max-width:600px;" />
    </div>
    <div class="settings-form-group" style="margin-top:16px;">
      <label>Délai alerte BAT sans réponse (heures)</label>
      <input type="number" id="bat-alert-delay-input" value="${alertDelayHours}" min="1" class="settings-input" style="width:120px;" />
      <p style="color:#6b7280;font-size:12px;margin-top:4px;">Affiche une alerte dans le bandeau si un BAT reste sans validation/refus après ce délai. Défaut : 48h.</p>
    </div>
    <button id="bat-cmd-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("bat-cmd-save").onclick = async () => {
    const command = document.getElementById("bat-cmd-input").value;
    const rawDelay = parseInt(document.getElementById("bat-alert-delay-input").value);
    const batAlertDelayHours = (rawDelay > 0) ? rawDelay : 48;
    const r = await fetch("/api/config/bat-command", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ command, batAlertDelayHours })
    }).then(r => r.json());
    if (r.ok) showNotification("Commande BAT enregistrée", "success");
    else alert("Erreur");
  };
}

export async function renderSettingsActionButtons(panel) {
  let buttons = {};
  try {
    const r = await fetch("/api/config/action-buttons").then(r => r.json());
    if (r.ok) buttons = r.buttons || {};
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Boutons d'action</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Configurez les chemins d'exécutables pour chaque bouton d'action.</p>
    <div class="settings-form-group"><label>Contrôleur</label><input type="text" id="ab-controller" value="${esc(buttons.controller)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>PrismaPrepare</label><input type="text" id="ab-prisma" value="${esc(buttons.prismaPrepare)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Impression</label><input type="text" id="ab-print" value="${esc(buttons.print)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Modification</label><input type="text" id="ab-modification" value="${esc(buttons.modification)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Fiery (hotfolder)</label><input type="text" id="ab-fiery" value="${esc(buttons.fiery)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <button id="ab-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("ab-save").onclick = async () => {
    const data = {
      buttons: {
        controller: document.getElementById("ab-controller").value,
        prismaPrepare: document.getElementById("ab-prisma").value,
        print: document.getElementById("ab-print").value,
        modification: document.getElementById("ab-modification").value,
        fiery: document.getElementById("ab-fiery").value
      }
    };
    const r = await fetch("/api/config/action-buttons", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(data)
    }).then(r => r.json());
    if (r.ok) showNotification("Boutons d'action enregistrés", "success");
    else alert("Erreur");
  };
}
