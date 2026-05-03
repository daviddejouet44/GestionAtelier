import { authToken, showNotification, esc } from '../core.js';

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

function _varsSidebar() {
  return `<div style="flex:0 0 240px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:16px;align-self:flex-start;">
    <p style="font-size:12px;font-weight:600;color:#374151;margin:0 0 10px;">Variables disponibles</p>
    <p style="font-size:11px;color:#6b7280;margin:0 0 8px;">Copiez-collez dans l'objet ou le corps :</p>
    ${_mailVarsHtml()}
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

export async function renderSettingsEmailTemplates(panel) {
  panel.innerHTML = `<h3>📧 Templates email</h3><p style="color:#6b7280;">Chargement…</p>`;

  let batComplet    = { to: '', subject: 'BAT - Dossier {{numeroDossier}} - {{nomClient}}', body: 'Bonjour,\n\nVeuillez trouver ci-joint le BAT pour le dossier {{numeroDossier}}.\n\nCordialement,' };
  let batPapier     = { to: '', subject: 'BAT Papier — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nVeuillez trouver ci-joint le BAT papier pour le dossier {{numeroDossier}}.\n\nCordialement,' };
  let prodStart     = { to: '', subject: 'Début de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} vient de démarrer.\n\nCordialement,' };
  let prodEnd       = { to: '', subject: 'Fin de production — Dossier {{numeroDossier}}', body: 'Bonjour,\n\nLa production de votre dossier {{numeroDossier}} est terminée.\n\nCordialement,' };
  let portalSteps   = [];
  let portalTplMap  = {}; // templateKey → [stepLabel, ...]

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
      // Build reverse map: templateKey → list of portal step labels that use it
      const allTplKeys = Object.keys(rTpl.templates || {});
      allTplKeys.forEach(k => { portalTplMap[k] = []; });
      portalSteps.forEach(s => {
        if (s.emailTemplateKey && portalTplMap[s.emailTemplateKey] !== undefined) {
          portalTplMap[s.emailTemplateKey].push(s.clientLabel || s.kanbanFolder || s.emailTemplateKey);
        }
      });
    }
  } catch(e) { /* use defaults */ }

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
      const stepsHtml = steps.length
        ? steps.map(l => `<span style="background:#dbeafe;color:#1e40af;border-radius:4px;padding:2px 8px;font-size:12px;">${esc(l)}</span>`).join(' ')
        : `<span style="color:#9ca3af;font-size:12px;">— aucune étape liée —</span>`;
      return `<div style="display:flex;align-items:center;gap:12px;padding:8px 12px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;">
        <code style="flex:0 0 200px;font-size:12px;color:#1d4ed8;">${esc(key)}</code>
        <div style="display:flex;flex-wrap:wrap;gap:4px;">${stepsHtml}</div>
      </div>`;
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

    ${_templateSection('bat-complet', 'Email BAT complet', 'Envoyé lors de la validation d\'un BAT complet.', batComplet)}
    ${_templateSection('bat-papier',  'Email BAT papier',  'Envoyé lors d\'un BAT papier.', batPapier)}
    ${_templateSection('prod-start',  'Email début de production', 'Envoyé au démarrage de la production (bouton "Mail début" sur les cartes Kanban).', prodStart)}
    ${_templateSection('prod-end',    'Email fin de production',   'Envoyé à la fin de la production (bouton "Mail fin" sur les cartes Kanban).', prodEnd)}
  `;

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
