import { authToken, showNotification, esc } from '../core.js';

// Variables for atelier internal templates
function _mailVarsHtml() {
  return `<div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:12px;margin-top:12px;">
    <h5 style="margin:0 0 10px;font-size:13px;color:#374151;">Variables disponibles :</h5>
    ${[
      ['{{numeroDossier}}', 'Numéro du dossier'],
      ['{{fileName}}', 'Nom du fichier'],
      ['{{client}}', 'Nom du client'],
      ['{{companyName}}', 'Nom de l\'entreprise du client'],
      ['{{operatorName}}', 'Nom de l\'opérateur affecté']
    ].map(([k, l]) => `<div style="display:flex;gap:6px;margin-bottom:4px;">
      <code style="flex:0 0 150px;font-size:11px;color:#7c3aed;background:#f5f3ff;padding:2px 6px;border-radius:4px;">${esc(k)}</code>
      <span style="color:#6b7280;margin-left:8px;">${l}</span>
    </div>`).join('')}
  </div>`;
}

// Variables for portal templates
function _portalVarsHtml() {
  return `<div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:12px;margin-top:12px;">
    <h5 style="margin:0 0 10px;font-size:13px;color:#374151;">Variables disponibles :</h5>
    ${[
      ['{clientName}', 'Nom du client'],
      ['{email}', 'Email du client'],
      ['{orderNumber}', 'Numéro de commande'],
      ['{orderTitle}', 'Titre/description de la commande'],
      ['{portalLink}', 'Lien vers le portail client'],
      ['{companyName}', 'Nom de l\'entreprise'],
      ['{motif}', 'Motif du refus (BAT refusé)']
    ].map(([k, l]) => `<div style="display:flex;gap:6px;margin-bottom:4px;">
      <code style="flex:0 0 150px;font-size:11px;color:#7c3aed;background:#f5f3ff;padding:2px 6px;border-radius:4px;">${esc(k)}</code>
      <span style="color:#6b7280;margin-left:8px;">${l}</span>
    </div>`).join('')}
  </div>`;
}

function _varsSidebar() {
  return `<div style="flex:1;min-width:280px;">
    ${_mailVarsHtml()}
  </div>`;
}

function _portalVarsSidebar() {
  return `<div style="flex:1;min-width:280px;">
    ${_portalVarsHtml()}
  </div>`;
}

function _templateSection(id, title, desc, tmpl) {
  return `<div class="settings-section-card">
    <h4>${title}</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">${desc}</p>
    <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;">
      <div style="flex:1;min-width:300px;">
        <div class="settings-form-group"><label>Destinataire (To)</label>
          <input type="text" id="et-${id}-to" value="${esc(tmpl.to||'')}" class="settings-input settings-input-wide" placeholder="client@example.com" /></div>
        <div class="settings-form-group" style="margin-top:12px;"><label>Objet du mail</label>
          <input type="text" id="et-${id}-subject" value="${esc(tmpl.subject||'')}" class="settings-input settings-input-wide" /></div>
        <div class="settings-form-group" style="margin-top:12px;"><label>Corps du mail</label>
          <textarea id="et-${id}-body" class="settings-input settings-input-wide" rows="8" style="font-family:monospace;font-size:12px;">${esc(tmpl.body||'')}</textarea></div>
        <div style="display:flex;align-items:center;gap:8px;margin-top:10px;">
          <button id="et-${id}-save" class="btn btn-primary">Enregistrer</button>
          <span id="et-${id}-msg" style="font-size:13px;"></span>
        </div>
      </div>
      ${_varsSidebar()}
    </div>
  </div>`;
}

// Portal template editor (no "to" field — address is determined by the event context)
function _portalTemplateSection(key, title, desc, triggerLabel, tmpl) {
  const triggerHtml = triggerLabel
    ? `<div style="display:inline-flex;align-items:center;gap:6px;background:#f0fdf4;border:1px solid #86efac;border-radius:6px;padding:4px 10px;font-size:12px;color:#166534;margin-bottom:12px;">
        <span>⚡ Déclenché par :</span><strong>${esc(triggerLabel)}</strong>
      </div>`
    : '';
  return `<div class="settings-section-card">
    <div style="display:flex;align-items:flex-start;justify-content:space-between;flex-wrap:wrap;gap:8px;">
      <div>
        <h4 style="margin-bottom:4px;">${title}</h4>
        <code style="font-size:11px;color:#7c3aed;background:#f5f3ff;padding:2px 6px;border-radius:4px;">${esc(key)}</code>
      </div>
    </div>
    <p style="color:#6b7280;font-size:13px;margin:10px 0 6px;">${desc}</p>
    ${triggerHtml}
    <div style="display:flex;gap:24px;align-items:flex-start;flex-wrap:wrap;margin-top:8px;">
      <div style="flex:1;min-width:300px;">
        <div class="settings-form-group"><label>Objet du mail</label>
          <input type="text" id="ptet-${key}-subject" value="${esc(tmpl.subject||'')}" class="settings-input settings-input-wide" /></div>
        <div class="settings-form-group" style="margin-top:12px;"><label>Corps du mail</label>
          <textarea id="ptet-${key}-body" class="settings-input settings-input-wide" rows="8" style="font-family:monospace;font-size:12px;">${esc(tmpl.body||'')}</textarea></div>
        <div style="display:flex;align-items:center;gap:8px;margin-top:10px;">
          <button id="ptet-${key}-save" class="btn btn-primary">Enregistrer</button>
          <span id="ptet-${key}-msg" style="font-size:13px;"></span>
        </div>
      </div>
      ${_portalVarsSidebar()}
    </div>
  </div>`;
}

export async function renderSettingsEmailTemplates(panel) {
  let batComplet = { to: '', subject: 'Épreuve BAT complète — {{numeroDossier}}', body: 'Bonjour,\n\nL\'épreuve BAT du dossier {{numeroDossier}} / {{fileName}} est prête.\n\nCordialement,' };
  let batPapier  = { to: '', subject: 'Épreuve BAT papier — {{numeroDossier}}', body: 'Bonjour,\n\nL\'épreuve BAT papier du dossier {{numeroDossier}} / {{fileName}} est prête.\n\nCordialement,' };
  let prodStart  = { to: '', subject: 'Début de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} a commencé.\n\nCordialement,' };
  let prodEnd    = { to: '', subject: 'Fin de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} est terminée.\n\nCordialement,' };
  let kanbanColumns = []; // KanbanColumnConfig list for multi-template mapping
  let portalTplMap  = {}; // templateKey → [tileLabel, ...]
  let portalTpls    = {}; // templateKey → { subject, body }

  // Portal template metadata (trigger labels for system templates)
  const PORTAL_TEMPLATE_META = {
    'client_welcome':             { title: 'Bienvenue client',         desc: 'Envoyé lors de la création d\'un compte client (inscription manuelle par l\'admin ou auto-inscription).', trigger: null },
    'client_password_reset':      { title: 'Réinitialisation mot de passe', desc: 'Envoyé lors d\'une demande de réinitialisation de mot de passe.', trigger: null },
    'client_order_received':      { title: 'Commande reçue',           desc: 'Envoyé au client après la soumission d\'une nouvelle commande.', trigger: 'Soumission d\'une commande web (portail)' },
    'client_bat_available':       { title: 'BAT disponible',           desc: 'Envoyé au client lorsqu\'un BAT est prêt pour validation.', trigger: 'Envoi de BAT à l\'atelier' },
    'client_bat_validated_confirmation': { title: 'BAT validé',        desc: 'Envoyé au client en confirmation de la validation du BAT.', trigger: 'Validation BAT par le client (portail)' },
    'client_bat_refused_confirmation': { title: 'BAT refusé',          desc: 'Envoyé au client en confirmation du refus du BAT (avec motif).', trigger: 'Refus BAT par le client (portail)' },
    'client_production_started':  { title: 'Début de production',      desc: 'Envoyé au client lorsque sa commande entre en production (déclenché manuellement).', trigger: 'Début de production (mail manuel depuis tuile)' },
    'client_production_completed': { title: 'Fin de production',        desc: 'Envoyé au client lorsque sa commande est terminée (déclenché manuellement).', trigger: 'Fin de production (mail manuel depuis tuile)' },
    'atelier_client_bat_refused': { title: 'BAT refusé — notification atelier',  desc: 'Envoyé à l\'atelier lorsqu\'un client refuse un BAT depuis son espace client (avec motif).', trigger: 'Refus BAT par le client (portail)' },
    'atelier_new_client_order':   { title: 'Nouvelle commande web — atelier',    desc: 'Envoyé à l\'atelier lorsqu\'une nouvelle commande arrive depuis le portail client.', trigger: 'Soumission d\'une commande web (portail)' },
  };

  // Default bodies for portal templates
  const PORTAL_TEMPLATE_DEFAULTS = {
    'client_welcome':              { subject: 'Bienvenue sur votre espace client', body: 'Bonjour {clientName},\n\nVotre espace client a été créé.\n\nConnectez-vous ici : {portalLink}\nEmail : {email}\n\nCordialement,' },
    'client_password_reset':       { subject: 'Réinitialisation de mot de passe', body: 'Bonjour {clientName},\n\nClic ici pour réinitialiser votre mot de passe : {resetLink}\n\nCordialement,' },
    'client_order_received':       { subject: 'Commande reçue — {orderNumber}', body: 'Bonjour {clientName},\n\nVotre commande {orderNumber} \"{orderTitle}\" a bien été reçue.\n\nConsultez votre espace client : {portalLink}\n\nCordialement,' },
    'client_bat_available':        { subject: 'BAT disponible — {orderNumber}', body: 'Bonjour {clientName},\n\nL\'épreuve BAT pour votre commande {orderNumber} est prête à la validation.\n\nConsultez votre espace client : {portalLink}\n\nCordialement,' },
    'client_bat_validated_confirmation': { subject: 'BAT validé — {orderNumber}', body: 'Bonjour {clientName},\n\nVotre validation du BAT pour la commande {orderNumber} a bien été enregistrée.\n\nCordialement,' },
    'client_bat_refused_confirmation': { subject: 'BAT refusé — {orderNumber}', body: 'Bonjour {clientName},\n\nVotre refus du BAT pour la commande {orderNumber} a bien été enregistré.\n\nMotif : {motif}\n\nCordialement,' },
    'client_production_started':   { subject: 'Début de production — {orderNumber}', body: 'Bonjour {clientName},\n\nVotre commande {orderNumber} — {orderTitle} est entrée en production.\n\nCordialement,' },
    'client_production_completed': { subject: 'Fin de production — {orderNumber}', body: 'Bonjour {clientName},\n\nVotre commande {orderNumber} — {orderTitle} est terminée.\n\nCordialement,' },
    'atelier_client_bat_refused': { subject: 'BAT refusé — {orderNumber}', body: 'BAT refusé par {clientName} ({companyName})\nCommande : {orderNumber}\nMotif : {motif}\n\nCordialement,' },
    'atelier_new_client_order':   { subject: 'Nouvelle commande client — {orderNumber}', body: 'Nouvelle commande web.\n\nClient : {clientName} ({companyName})\nCommande : {orderNumber} — {orderTitle}\n\nCordialement,' },
    'atelier_password_reset_reply': { subject: 'Réinitialisation de votre mot de passe', body: 'Bonjour {clientName},\n\nSuite à votre demande, votre mot de passe a été réinitialisé.\n\nVotre nouveau mot de passe : {newPassword}\n\nConnectez-vous ici : {portalLink}\n\nCordialement,' },
  };

  // Fetch all templates
  try {
    const [r1, r2, r3, r4, rKanban, rTpl] = await Promise.all([
      fetch('/api/config/mail-template-bat-complet',      { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-bat-papier',       { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-production-start', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-production-end', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/kanban-columns',        { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/email-templates', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
    ]);
    if (r1.ok && r1.template) batComplet = r1.template;
    if (r2.ok && r2.template) batPapier  = r2.template;
    if (r3.ok && r3.template) prodStart  = r3.template;
    if (r4.ok && r4.template) prodEnd    = r4.template;
    if (rKanban.ok) kanbanColumns = rKanban.columns || [];
    if (rTpl.ok) {
      // Build portalTplMap from KanbanColumnConfig.emailTemplateKeys (multi-template per tile)
      const allTplKeys = Object.keys(rTpl.templates || {});
      allTplKeys.forEach(k => { portalTplMap[k] = []; });
      kanbanColumns.forEach(col => {
        if (Array.isArray(col.emailTemplateKeys)) {
          col.emailTemplateKeys.forEach(key => {
            if (portalTplMap[key] !== undefined) {
              portalTplMap[key].push(col.label || col.folder);
            }
          });
        }
      });
      // Merge saved templates with defaults
      Object.keys(PORTAL_TEMPLATE_DEFAULTS).forEach(k => {
        const saved = rTpl.templates?.[k] || {};
        const def   = PORTAL_TEMPLATE_DEFAULTS[k];
        portalTpls[k] = { subject: saved.subject || def.subject, body: saved.body || def.body };
      });
    } else {
      // Fallback to defaults
      Object.keys(PORTAL_TEMPLATE_DEFAULTS).forEach(k => { portalTpls[k] = { ...PORTAL_TEMPLATE_DEFAULTS[k] }; });
    }
  } catch(e) { /* use defaults */
    Object.keys(PORTAL_TEMPLATE_DEFAULTS).forEach(k => { portalTpls[k] = { ...PORTAL_TEMPLATE_DEFAULTS[k] }; });
  }

  // Build portal step mapping section HTML
  const _portalTplMappingHtml = () => {
    const tplKeys = Object.keys(portalTplMap).filter(k => portalTplMap[k].length > 0);
    if (!tplKeys.length) {
      return `<p style="color:#9ca3af;font-size:13px;">Aucun template portail associé à une tuile. Configurez les associations dans <strong>Paramétrage → Tuiles</strong>.</p>`;
    }

    return tplKeys.sort().map(key => {
      const tiles = portalTplMap[key];
      const meta  = PORTAL_TEMPLATE_META[key];
      const tilesHtml = tiles.length
        ? tiles.map(l => `<span style="background:#dbeafe;color:#1e40af;border-radius:4px;padding:2px 8px;font-size:12px;">${esc(l)}</span>`).join(' ')
        : meta?.trigger
          ? `<span style="background:#f0fdf4;color:#166534;border-radius:4px;padding:2px 8px;font-size:12px;">⚡ ${esc(meta.trigger)}</span>`
          : `<span style="color:#9ca3af;font-size:12px;">— aucune tuile liée —</span>`;
      return `<div style="display:flex;align-items:center;gap:12px;padding:8px 12px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;">
        <code style="flex:0 0 200px;font-size:12px;color:#1d4ed8;">${esc(key)}</code>
        <div style="display:flex;flex-wrap:wrap;gap:4px;">${tilesHtml}</div>
      </div>`;
    }).join('');
  };

  // Build portal template editors HTML
  const _portalTemplateEditorsHtml = () => {
    return Object.keys(PORTAL_TEMPLATE_META).map(key => {
      const meta = PORTAL_TEMPLATE_META[key];
      const tpl  = portalTpls[key] || PORTAL_TEMPLATE_DEFAULTS[key];
      return _portalTemplateSection(key, meta.title, meta.desc, meta.trigger, tpl);
    }).join('');
  };

  panel.innerHTML = `
    <h3>📧 Templates email</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">Personnalisez les modèles d'email utilisés dans les différents workflows. Utilisez les variables pour insérer des données dynamiques.</p>

    <!-- Portal tile ↔ template mapping (inverse view) -->
    <div class="settings-section-card" style="margin-bottom:24px;">
      <h4>🔗 Mapping Tuiles Kanban ↔ Templates portail</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Vue inverse : pour chaque template portail, les tuiles Kanban qui l'utilisent comme bouton email. Pour modifier les associations, allez dans <strong>Paramétrage → Tuiles</strong>.</p>
      <div style="display:flex;flex-direction:column;gap:6px;" id="et-portal-mapping">
        ${_portalTplMappingHtml()}
      </div>
    </div>

    <!-- Portal template editors -->
    <div class="settings-section-card" style="margin-bottom:24px;border-left:4px solid #7c3aed;">
      <h4 style="color:#7c3aed;">✉️ Templates portail client</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Personnalisez les emails envoyés aux clients depuis le portail. Les variables entre accolades <code style="color:#7c3aed;">{variable}</code> sont remplacées dynamiquement.</p>
      <div id="et-portal-editors" style="display:flex;flex-direction:column;gap:16px;">
        ${_portalTemplateEditorsHtml()}
      </div>
    </div>

    <!-- Atelier internal templates -->
    <div class="settings-section-card" style="margin-bottom:24px;">
      <h4 style="margin-bottom:16px;color:#374151;">📋 Templates internes atelier</h4>
      ${_templateSection('bat-complet', 'Email BAT complet', 'Envoyé lors de la validation d\'un BAT complet.', batComplet)}
      ${_templateSection('bat-papier',  'Email BAT papier',  'Envoyé lors d\'un BAT papier.', batPapier)}
      ${_templateSection('prod-start',  'Email début de production', 'Envoyé au démarrage de la production (bouton "Mail début" sur les cartes Kanban).', prodStart)}
      ${_templateSection('prod-end',    'Email fin de production',   'Envoyé à la fin de la production (bouton "Mail fin" sur les cartes Kanban).', prodEnd)}
    </div>
  `;

  // Save handler for portal templates
  const _savePortalTemplate = async (key) => {
    const subjEl = panel.querySelector(`#ptet-${key}-subject`);
    const bodyEl = panel.querySelector(`#ptet-${key}-body`);
    const msgEl  = panel.querySelector(`#ptet-${key}-msg`);
    if (!subjEl || !bodyEl || !msgEl) return;
    try {
      const r = await fetch(`/api/admin/portal/email-templates/${encodeURIComponent(key)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ subject: subjEl.value, body: bodyEl.value })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Enregistré';
        setTimeout(() => { msgEl.textContent = ''; }, 3000);
      } else {
        msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur');
      }
    } catch(e) {
      msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau';
    }
  };

  // Wire up portal template save buttons
  Object.keys(PORTAL_TEMPLATE_META).forEach(key => {
    const btn = panel.querySelector(`#ptet-${key}-save`);
    if (btn) btn.onclick = () => _savePortalTemplate(key);
  });

  const _saveTemplate = async (id, endpoint, method = 'PUT') => {
    const to      = panel.querySelector(`#et-${id}-to`).value.trim();
    const subject = panel.querySelector(`#et-${id}-subject`).value;
    const body    = panel.querySelector(`#et-${id}-body`).value;
    const msgEl   = panel.querySelector(`#et-${id}-msg`);
    try {
      const r = await fetch(endpoint, {
        method: method,
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ to, subject, body })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Enregistré';
        setTimeout(() => { msgEl.textContent = ''; }, 3000);
      } else {
        msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur');
      }
    } catch(e) {
      msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau';
    }
  };

  panel.querySelector('#et-bat-complet-save').onclick = () => _saveTemplate('bat-complet', '/api/config/bat-mail-template');
  panel.querySelector('#et-bat-papier-save').onclick  = () => _saveTemplate('bat-papier',  '/api/config/mail-template-bat-papier');
  panel.querySelector('#et-prod-start-save').onclick  = () => _saveTemplate('prod-start',  '/api/config/mail-template-production-start');
  panel.querySelector('#et-prod-end-save').onclick    = () => _saveTemplate('prod-end',    '/api/config/mail-template-production-end');
}
