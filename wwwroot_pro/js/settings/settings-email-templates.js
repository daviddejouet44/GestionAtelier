import { authToken, showNotification, esc } from '../core.js';

// Variables for atelier internal templates
function _mailVarsHtml() {
  return [
    ['{{numeroDossier}}',          'N° du dossier'],
    ['{{nomClient}}',              'Nom du client'],
    ['{{nomFichier}}',             'Nom du fichier'],
    ['{{operateur}}',              'Opérateur'],
    ['{{typeTravail}}',            'Type de travail'],
    ['{{quantite}}',               'Quantité'],
    ['{{moteurImpression}}',       'Moteur d\'impression'],
    ['{{dateReception}}',          'Date de réception'],
    ['{{dateEnvoi}}',              'Date d\'envoi'],
    ['{{dateImpression}}',         'Date d\'impression'],
    ['{{dateProductionFinitions}}','Date production finitions'],
  ].map(([v, l]) => `<div style="display:flex;justify-content:space-between;padding:3px 0;border-bottom:1px solid #f3f4f6;font-size:11px;">
    <code style="color:#1d4ed8;">${v}</code>
    <span style="color:#6b7280;margin-left:8px;">${l}</span>
  </div>`).join('');
}

// Variables for portal templates
function _portalVarsHtml() {
  return [
    ['{clientName}',    'Nom du client'],
    ['{email}',         'Email du client'],
    ['{orderNumber}',   'N° de commande'],
    ['{orderTitle}',    'Intitulé commande'],
    ['{batLink}',       'Lien direct BAT'],
    ['{portalLink}',    'URL du portail'],
    ['{activateLink}',  'Lien activation compte'],
    ['{resetLink}',     'Lien réinitialisation MDP'],
    ['{motif}',         'Motif de refus BAT'],
    ['{companyName}',   'Société du client'],
  ].map(([v, l]) => `<div style="display:flex;justify-content:space-between;padding:3px 0;border-bottom:1px solid #f3f4f6;font-size:11px;">
    <code style="color:#7c3aed;">${esc(v)}</code>
    <span style="color:#6b7280;margin-left:8px;">${l}</span>
  </div>`).join('');
}

function _varsSidebar() {
  return `<div style="flex:0 0 240px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:16px;align-self:flex-start;">
    <p style="font-size:12px;font-weight:600;color:#374151;margin:0 0 10px;">Variables disponibles</p>
    <p style="font-size:11px;color:#6b7280;margin:0 0 8px;">Copiez-collez dans l'objet ou le corps :</p>
    ${_mailVarsHtml()}
  </div>`;
}

function _portalVarsSidebar() {
  return `<div style="flex:0 0 240px;background:#f5f3ff;border:1px solid #ddd6fe;border-radius:8px;padding:16px;align-self:flex-start;">
    <p style="font-size:12px;font-weight:600;color:#374151;margin:0 0 10px;">Variables disponibles</p>
    <p style="font-size:11px;color:#6b7280;margin:0 0 8px;">Copiez-collez dans l'objet ou le corps :</p>
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
        <button id="et-${id}-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
        <span id="et-${id}-msg" style="margin-left:10px;font-size:13px;"></span>
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
  panel.innerHTML = `<h3>📧 Templates email</h3><p style="color:#6b7280;">Chargement…</p>`;

  let batComplet    = { to: '', subject: 'BAT - Dossier {{numeroDossier}} - {{nomClient}}', body: 'Bonjour,\n\nVeuillez trouver ci-joint le BAT pour le dossier {{numeroDossier}}.\n\nCordialement,' };
  let batPapier     = { to: '', subject: 'BAT Papier — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nVeuillez trouver ci-joint le BAT papier pour le dossier {{numeroDossier}}.\n\nCordialement,' };
  let prodStart     = { to: '', subject: 'Début de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} vient de démarrer.\n\nCordialement,' };
  let prodEnd       = { to: '', subject: 'Fin de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} est terminée.\n\nCordialement,' };
  let portalSteps   = [];
  let portalTplMap  = {}; // templateKey → [stepLabel, ...]
  let portalTpls    = {}; // templateKey → { subject, body }

  // Portal template metadata (trigger labels for system templates)
  const PORTAL_TEMPLATE_META = {
    'client_welcome':             { title: 'Bienvenue client',         desc: 'Envoyé lors de la création d\'un compte client (inscription manuelle par l\'admin ou auto-inscription).', trigger: null },
    'client_invitation':          { title: 'Invitation client',         desc: 'Envoyé lors du clic sur le bouton "✉️ Inviter un client" ou sur le bouton invitation par ligne dans la liste des clients.', trigger: 'Invitation manuelle par l\'admin' },
    'client_password_reset':      { title: 'Réinitialisation mot de passe', desc: 'Envoyé lorsqu\'un client demande à réinitialiser son mot de passe depuis la page de connexion.', trigger: 'Demande reset MDP (portail login)' },
    'client_order_received':      { title: 'Confirmation commande',     desc: 'Envoyé au client après réception d\'une nouvelle commande web.', trigger: null },
    'client_bat_available':       { title: 'BAT disponible (client)',    desc: 'Envoyé au client lorsque l\'atelier lui envoie un BAT à valider.', trigger: 'Bouton "📤 Envoyer BAT au client"' },
    'client_order_status_changed':{ title: 'Changement de statut',      desc: 'Envoyé au client lorsque le statut de sa commande change (lié à une étape Kanban).', trigger: null },
    'atelier_client_bat_validated':{ title: 'BAT validé — notification atelier', desc: 'Envoyé à l\'atelier lorsqu\'un client valide un BAT depuis son espace client.', trigger: 'Validation BAT par le client (portail)' },
    'atelier_client_bat_refused': { title: 'BAT refusé — notification atelier',  desc: 'Envoyé à l\'atelier lorsqu\'un client refuse un BAT depuis son espace client (avec motif).', trigger: 'Refus BAT par le client (portail)' },
    'atelier_new_client_order':   { title: 'Nouvelle commande web — atelier',    desc: 'Envoyé à l\'atelier lorsqu\'une nouvelle commande arrive depuis le portail client.', trigger: 'Soumission d\'une commande web (portail)' },
  };

  // Default bodies for portal templates
  const PORTAL_TEMPLATE_DEFAULTS = {
    'client_welcome':              { subject: 'Bienvenue sur votre espace client', body: 'Bonjour {clientName},\n\nVotre espace client a été créé.\n\nConnectez-vous ici : {portalLink}\nEmail : {email}\n\nCordialement,' },
    'client_invitation':           { subject: 'Invitation à votre espace client', body: 'Bonjour {clientName},\n\nVous avez été invité à accéder à votre espace client.\n\nCliquez sur le lien ci-dessous pour activer votre accès et définir votre mot de passe (lien valable 48h) :\n{activateLink}\n\nEmail de connexion : {email}\n\nCordialement,' },
    'client_password_reset':       { subject: 'Réinitialisation de mot de passe', body: 'Bonjour {clientName},\n\nVous avez demandé une réinitialisation de mot de passe.\n\nCliquez ici (valable 1h) :\n{resetLink}\n\nSi vous n\'avez pas fait cette demande, ignorez cet email.\n\nCordialement,' },
    'client_order_received':       { subject: 'Votre commande a bien été reçue — {orderNumber}', body: 'Bonjour {clientName},\n\nNous avons bien reçu votre commande {orderNumber} — {orderTitle}.\n\nNous vous contacterons dès que votre commande sera traitée.\n\nCordialement,' },
    'client_bat_available':        { subject: 'Un BAT est disponible — {orderNumber}', body: 'Bonjour {clientName},\n\nUn BAT est disponible pour votre commande {orderNumber} — {orderTitle}.\n\nConnectez-vous pour le consulter et le valider ou refuser :\n{batLink}\n\nCordialement,' },
    'client_order_status_changed': { subject: 'Mise à jour de votre commande — {orderNumber}', body: 'Bonjour {clientName},\n\nLe statut de votre commande {orderNumber} — {orderTitle} a été mis à jour.\n\nConnectez-vous pour consulter l\'avancement :\n{portalLink}\n\nCordialement,' },
    'atelier_client_bat_validated':{ subject: 'BAT validé par le client — {orderNumber}', body: 'Le client {clientName} ({companyName}) a validé le BAT pour la commande {orderNumber} — {orderTitle}.' },
    'atelier_client_bat_refused':  { subject: 'BAT refusé par le client — {orderNumber}', body: 'Le client {clientName} ({companyName}) a refusé le BAT pour la commande {orderNumber} — {orderTitle}.\n\nMotif : {motif}' },
    'atelier_new_client_order':    { subject: 'Nouvelle commande web — {orderNumber}', body: 'Une nouvelle commande web a été reçue.\n\nClient : {clientName} ({companyName})\nCommande : {orderNumber} — {orderTitle}\n\nConnectez-vous à l\'interface atelier pour la traiter.' },
  };

  try {
    const [r1, r2, r3, r4, rSteps, rTpl] = await Promise.all([
      fetch('/api/config/bat-mail-template', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-bat-papier', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-production-start', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/config/mail-template-production-end', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/client-steps',    { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/email-templates', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
    ]);
    if (r1.ok && r1.template) batComplet = r1.template;
    if (r2.ok && r2.template) batPapier  = r2.template;
    if (r3.ok && r3.template) prodStart  = r3.template;
    if (r4.ok && r4.template) prodEnd    = r4.template;
    if (rSteps.ok) portalSteps = rSteps.steps || [];
    if (rTpl.ok) {
      // Load portal templates — use saved value or fall back to defaults
      const allTplKeys = Object.keys(rTpl.templates || {});
      allTplKeys.forEach(k => { portalTplMap[k] = []; });
      portalSteps.forEach(s => {
        if (s.emailTemplateKey && portalTplMap[s.emailTemplateKey] !== undefined) {
          portalTplMap[s.emailTemplateKey].push(s.clientLabel || s.kanbanFolder || s.emailTemplateKey);
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
    const tplKeys = Object.keys(portalTplMap);
    if (!tplKeys.length && !portalSteps.some(s => s.emailTemplateKey)) {
      return `<p style="color:#9ca3af;font-size:13px;">Aucun template portail configuré. Définissez les mappings dans Paramétrage → Portail client → Étapes client.</p>`;
    }

    // Also include templates used by steps even if not in portalTplMap yet
    const allUsedKeys = new Set(tplKeys);
    portalSteps.forEach(s => { if (s.emailTemplateKey) allUsedKeys.add(s.emailTemplateKey); });

    return Array.from(allUsedKeys).sort().map(key => {
      const steps = portalTplMap[key] || portalSteps.filter(s => s.emailTemplateKey === key).map(s => s.clientLabel || s.kanbanFolder);
      const meta  = PORTAL_TEMPLATE_META[key];
      const stepsHtml = steps.length
        ? steps.map(l => `<span style="background:#dbeafe;color:#1e40af;border-radius:4px;padding:2px 8px;font-size:12px;">${esc(l)}</span>`).join(' ')
        : meta?.trigger
          ? `<span style="background:#f0fdf4;color:#166534;border-radius:4px;padding:2px 8px;font-size:12px;">⚡ ${esc(meta.trigger)}</span>`
          : `<span style="color:#9ca3af;font-size:12px;">— aucune étape liée —</span>`;
      return `<div style="display:flex;align-items:center;gap:12px;padding:8px 12px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;">
        <code style="flex:0 0 200px;font-size:12px;color:#1d4ed8;">${esc(key)}</code>
        <div style="display:flex;flex-wrap:wrap;gap:4px;">${stepsHtml}</div>
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

    <!-- Portal step ↔ template mapping (inverse view) -->
    <div class="settings-section-card" style="margin-bottom:24px;">
      <h4>🔗 Mapping Étapes client ↔ Templates portail</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Vue inverse : pour chaque template portail, les étapes client qui l'utilisent. Pour modifier les associations, allez dans <strong>Portail client → Étapes client</strong>.</p>
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
    <h4 style="margin-bottom:16px;color:#374151;">📋 Templates internes atelier</h4>
    ${_templateSection('bat-complet', 'Email BAT complet', 'Envoyé lors de la validation d\'un BAT complet.', batComplet)}
    ${_templateSection('bat-papier',  'Email BAT papier',  'Envoyé lors d\'un BAT papier.', batPapier)}
    ${_templateSection('prod-start',  'Email début de production', 'Envoyé au démarrage de la production (bouton "Mail début" sur les cartes Kanban).', prodStart)}
    ${_templateSection('prod-end',    'Email fin de production',   'Envoyé à la fin de la production (bouton "Mail fin" sur les cartes Kanban).', prodEnd)}
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
        method,
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ template: { to, subject, body } })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = '#16a34a';
        msgEl.textContent = '✅ Enregistré';
        setTimeout(() => { msgEl.textContent = ''; }, 3000);
      } else {
        msgEl.style.color = '#ef4444';
        msgEl.textContent = '❌ ' + (r.error || 'Erreur');
      }
    } catch(e) {
      msgEl.style.color = '#ef4444';
      msgEl.textContent = '❌ Erreur réseau';
    }
  };

  panel.querySelector('#et-bat-complet-save').onclick = () => _saveTemplate('bat-complet', '/api/config/bat-mail-template');
  panel.querySelector('#et-bat-papier-save').onclick  = () => _saveTemplate('bat-papier',  '/api/config/mail-template-bat-papier');
  panel.querySelector('#et-prod-start-save').onclick  = () => _saveTemplate('prod-start',  '/api/config/mail-template-production-start');
  panel.querySelector('#et-prod-end-save').onclick    = () => _saveTemplate('prod-end',    '/api/config/mail-template-production-end');
}
