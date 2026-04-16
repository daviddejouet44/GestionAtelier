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
  let batSimpleDropletPath = "";
  let routings = [];
  let types = [];
  let mailTemplate = { to: "", subject: "BAT - Dossier {{numeroDossier}} - {{nomClient}}", body: "Bonjour,\n\nVeuillez trouver ci-joint le BAT pour le dossier {{numeroDossier}}.\n\nCordialement," };
  let mailTemplateStart = { to: "", subject: "Début de production — Dossier {{numeroDossier}}", body: "Bonjour,\n\nLa production de votre dossier {{numeroDossier}} vient de démarrer.\n\nCordialement," };
  let mailTemplateEnd = { to: "", subject: "Fin de production — Dossier {{numeroDossier}}", body: "Bonjour,\n\nLa production de votre dossier {{numeroDossier}} est terminée.\n\nCordialement," };
  let mailTemplateBatPapier = { to: "", subject: "BAT Papier — Dossier {{numeroDossier}}", body: "Bonjour,\n\nVeuillez trouver ci-joint le BAT papier pour le dossier {{numeroDossier}}.\n\nCordialement," };
  try {
    const [r1, r2, r3, r4, r5, r6, r7, r8] = await Promise.all([
      fetch("/api/config/integrations", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch("/api/config/bat-command").then(r => r.json()).catch(() => ({})),
      fetch("/api/config/hotfolder-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => []),
      fetch("/api/config/bat-mail-template").then(r => r.json()).catch(() => ({})),
      fetch("/api/config/mail-template-production-start").then(r => r.json()).catch(() => ({})),
      fetch("/api/config/mail-template-production-end").then(r => r.json()).catch(() => ({})),
      fetch("/api/config/mail-template-bat-papier").then(r => r.json()).catch(() => ({}))
    ]);
    if (r1.ok && r1.config) intCfg = r1.config;
    if (r2.ok) { batCmd = r2.command || ""; batAlertDelayHours = r2.batAlertDelayHours ?? 48; batSimpleDropletPath = r2.batSimpleDropletPath || ""; }
    if (Array.isArray(r3)) routings = r3;
    if (Array.isArray(r4)) types = r4;
    if (r5.ok && r5.template) mailTemplate = r5.template;
    if (r6.ok && r6.template) mailTemplateStart = r6.template;
    if (r7.ok && r7.template) mailTemplateEnd = r7.template;
    if (r8.ok && r8.template) mailTemplateBatPapier = r8.template;
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
      <button id="bat-cmd-save" class="btn btn-primary">Enregistrer</button>
    </div>

    <div class="settings-section-card">
      <h4>Template email BAT</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Personnalisez le mail ouvert dans le client de messagerie lorsque vous cliquez sur "Envoyé" dans l'onglet BAT.</p>
      <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;">
        <div style="flex:1;min-width:300px;">
          <div class="settings-form-group">
            <label>Destinataire (To)</label>
            <input type="text" id="bat-mail-to" value="${esc(mailTemplate.to || '')}" class="settings-input settings-input-wide" placeholder="client@example.com (laisser vide pour utiliser le mail client)" />
          </div>
          <div class="settings-form-group" style="margin-top:12px;">
            <label>Objet du mail</label>
            <input type="text" id="bat-mail-subject" value="${esc(mailTemplate.subject || '')}" class="settings-input settings-input-wide" placeholder="BAT - Dossier {{numeroDossier}} - {{nomClient}}" />
          </div>
          <div class="settings-form-group" style="margin-top:12px;">
            <label>Corps du mail</label>
            <textarea id="bat-mail-body" class="settings-input settings-input-wide" rows="6" style="font-family:monospace;font-size:12px;">${esc(mailTemplate.body || '')}</textarea>
          </div>
          <button id="bat-mail-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer le template</button>
        </div>
        <div style="flex:0 0 240px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:16px;">
          <p style="font-size:12px;font-weight:600;color:#374151;margin:0 0 10px;">Variables disponibles</p>
          <p style="font-size:11px;color:#6b7280;margin:0 0 8px;">Copiez-collez dans l'objet ou le corps du mail :</p>
          ${[
            ['{{numeroDossier}}', 'N° du dossier'],
            ['{{nomClient}}',     'Nom du client'],
            ['{{nomFichier}}',    'Nom du fichier'],
            ['{{dateCreation}}',  'Date de création'],
            ['{{typeTravail}}',   'Type de travail'],
            ['{{quantite}}',      'Quantité'],
            ['{{operateur}}',     'Opérateur'],
            ['{{dateLivraison}}', 'Date de livraison'],
          ].map(([v, l]) => `<div style="display:flex;justify-content:space-between;padding:4px 0;border-bottom:1px solid #f3f4f6;font-size:11px;">
            <code style="color:#1d4ed8;">${v}</code>
            <span style="color:#6b7280;">${l}</span>
          </div>`).join('')}
        </div>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Template email BAT Papier</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Mail envoyé lors d'un BAT Papier (même process que BAT Complet, template dédié).</p>
      <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;">
        <div style="flex:1;min-width:300px;">
          <div class="settings-form-group"><label>Destinataire (To)</label><input type="text" id="bat-papier-mail-to" value="${esc(mailTemplateBatPapier.to || '')}" class="settings-input settings-input-wide" placeholder="client@example.com" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Objet du mail</label><input type="text" id="bat-papier-mail-subject" value="${esc(mailTemplateBatPapier.subject || '')}" class="settings-input settings-input-wide" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Corps du mail</label><textarea id="bat-papier-mail-body" class="settings-input settings-input-wide" rows="5" style="font-family:monospace;font-size:12px;">${esc(mailTemplateBatPapier.body || '')}</textarea></div>
          <button id="bat-papier-mail-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer le template BAT Papier</button>
        </div>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Template email — Début de production</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Mail envoyé au client quand la production démarre (bouton "Mail début" dans la tuile Impression en cours).</p>
      <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;">
        <div style="flex:1;min-width:300px;">
          <div class="settings-form-group"><label>Destinataire (To)</label><input type="text" id="prod-start-mail-to" value="${esc(mailTemplateStart.to || '')}" class="settings-input settings-input-wide" placeholder="client@example.com" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Objet du mail</label><input type="text" id="prod-start-mail-subject" value="${esc(mailTemplateStart.subject || '')}" class="settings-input settings-input-wide" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Corps du mail</label><textarea id="prod-start-mail-body" class="settings-input settings-input-wide" rows="5" style="font-family:monospace;font-size:12px;">${esc(mailTemplateStart.body || '')}</textarea></div>
          <button id="prod-start-mail-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer le template Début de production</button>
        </div>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Template email — Fin de production</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Mail envoyé au client quand la production est terminée (bouton "Mail fin" dans la tuile Impression en cours).</p>
      <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;">
        <div style="flex:1;min-width:300px;">
          <div class="settings-form-group"><label>Destinataire (To)</label><input type="text" id="prod-end-mail-to" value="${esc(mailTemplateEnd.to || '')}" class="settings-input settings-input-wide" placeholder="client@example.com" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Objet du mail</label><input type="text" id="prod-end-mail-subject" value="${esc(mailTemplateEnd.subject || '')}" class="settings-input settings-input-wide" /></div>
          <div class="settings-form-group" style="margin-top:12px;"><label>Corps du mail</label><textarea id="prod-end-mail-body" class="settings-input settings-input-wide" rows="5" style="font-family:monospace;font-size:12px;">${esc(mailTemplateEnd.body || '')}</textarea></div>
          <button id="prod-end-mail-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer le template Fin de production</button>
        </div>
      </div>
    </div>
  `;

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
    const r = await fetch("/api/config/bat-command", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ command: batCmd, batAlertDelayHours: batAlertDelayHoursNew, batSimpleDropletPath: batSimpleDropletPathNew })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Configuration BAT Simple enregistrée", "success");
    else alert("Erreur");
  };

  // BAT mail template save
  panel.querySelector("#bat-mail-save").onclick = async () => {
    const to = panel.querySelector("#bat-mail-to").value.trim();
    const subject = panel.querySelector("#bat-mail-subject").value;
    const body = panel.querySelector("#bat-mail-body").value;
    const r = await fetch("/api/config/bat-mail-template", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ template: { to, subject, body } })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Template email BAT enregistré", "success");
    else alert("Erreur : " + (r.error || ""));
  };

  // BAT Papier mail template save
  panel.querySelector("#bat-papier-mail-save").onclick = async () => {
    const to = panel.querySelector("#bat-papier-mail-to").value.trim();
    const subject = panel.querySelector("#bat-papier-mail-subject").value;
    const body = panel.querySelector("#bat-papier-mail-body").value;
    const r = await fetch("/api/config/mail-template-bat-papier", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ template: { to, subject, body } })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Template email BAT Papier enregistré", "success");
    else alert("Erreur : " + (r.error || ""));
  };

  // Production start mail template save
  panel.querySelector("#prod-start-mail-save").onclick = async () => {
    const to = panel.querySelector("#prod-start-mail-to").value.trim();
    const subject = panel.querySelector("#prod-start-mail-subject").value;
    const body = panel.querySelector("#prod-start-mail-body").value;
    const r = await fetch("/api/config/mail-template-production-start", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ template: { to, subject, body } })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Template mail début de production enregistré", "success");
    else alert("Erreur : " + (r.error || ""));
  };

  // Production end mail template save
  panel.querySelector("#prod-end-mail-save").onclick = async () => {
    const to = panel.querySelector("#prod-end-mail-to").value.trim();
    const subject = panel.querySelector("#prod-end-mail-subject").value;
    const body = panel.querySelector("#prod-end-mail-body").value;
    const r = await fetch("/api/config/mail-template-production-end", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ template: { to, subject, body } })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Template mail fin de production enregistré", "success");
    else alert("Erreur : " + (r.error || ""));
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
