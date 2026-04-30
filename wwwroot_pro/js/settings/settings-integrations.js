// settings-integrations.js — Paramétrages → Intégrations
// Gestion des imports automatiques (XML, ERP, Pressero, MDSF) et exports (XML, CSV, ERP, Pressero, MDSF)
import { authToken, showNotification, esc } from '../core.js';
import { renderSettingsOrderSources } from './settings-order-sources.js';

const API = {
  config:    '/api/settings/integrations-config',
  testConn:  '/api/settings/integrations/test-connection',
  importXml: '/api/integrations/import-xml',
  importLog: '/api/integrations/import-log',
  exportLog: '/api/integrations/export-log',
  exportCmd: '/api/integrations/export',
};

/** Simple auth header helper */
function authH() { return { 'Authorization': `Bearer ${authToken}` }; }
function authJsonH() { return { ...authH(), 'Content-Type': 'application/json' }; }

/**
 * Loads the list of production-sheet fields from /api/settings/form-config
 * and returns them as an array of { key, label } objects.
 * System anchor fields (numeroDossier, referenceCommande) are always prepended.
 * Falls back to the hardcoded list on any error so existing mappings are preserved.
 * @returns {Promise<Array<{key: string, label: string}>>}
 */
async function loadFicheFields() {
  const SYSTEM_FIELDS = [
    { key: 'numeroDossier',    label: 'Numéro de dossier' },
    { key: 'referenceCommande', label: 'Référence commande' },
  ];
  const FALLBACK_FIELDS = [
    { key: 'numeroDossier',            label: 'Numéro de dossier' },
    { key: 'client',                   label: 'Client' },
    { key: 'nomClient',                label: 'Nom client' },
    { key: 'typeTravail',              label: 'Type de travail' },
    { key: 'quantite',                 label: 'Quantité' },
    { key: 'formatFini',               label: 'Format fini' },
    { key: 'moteurImpression',         label: 'Moteur d\'impression' },
    { key: 'operateur',                label: 'Opérateur' },
    { key: 'dateReceptionSouhaitee',   label: 'Date réception souhaitée' },
    { key: 'dateLivraisonSouhaitee',   label: 'Date livraison souhaitée' },
    { key: 'retraitLivraison',         label: 'Retrait / Livraison' },
    { key: 'commentaire',              label: 'Commentaire' },
    { key: 'referenceCommande',        label: 'Référence commande' },
  ];
  try {
    const r = await fetch('/api/settings/form-config', { headers: authH() }).then(r => r.json());
    if (!r || !r.fields || !r.fields.length) return FALLBACK_FIELDS;
    // Build deduplicated list: system anchors first, then all config fields
    const systemKeys = new Set(SYSTEM_FIELDS.map(f => f.key));
    const configFields = r.fields.map(f => ({ key: f.id || '', label: f.label || f.id || '' }))
                                  .filter(f => f.key && !systemKeys.has(f.key));
    return [...SYSTEM_FIELDS, ...configFields];
  } catch {
    return FALLBACK_FIELDS;
  }
}

/**
 * Converts a field key to a safe CSS/HTML identifier by replacing any
 * non-alphanumeric characters with underscores.  Custom field keys could
 * contain spaces or other special chars that would break querySelector.
 * @param {string} key
 * @returns {string}
 */
function safeDomId(key) {
  return (key || '').replace(/[^A-Za-z0-9_-]/g, '_');
}

export async function renderSettingsIntegrations(panel) {
  panel.innerHTML = `
    <h3>Intégrations — Import &amp; Export</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:20px;">
      Configurez les sources d'import automatique de la fiche de production (XML, ERP, Web-to-Print)
      et les destinations d'export des commandes.
    </p>
    <div class="settings-tabs" id="integ-tabs" style="margin-bottom:20px;">
      <button class="settings-tab active" data-itab="xml-import">📥 Import XML</button>
      <button class="settings-tab" data-itab="erp-import">🔗 ERP / Import auto</button>
      <button class="settings-tab" data-itab="pressero">🌐 Pressero</button>
      <button class="settings-tab" data-itab="mdsf">🌐 MDSF</button>
      <button class="settings-tab" data-itab="export">📤 Export commandes</button>
      <button class="settings-tab" data-itab="import-log">📋 Journal imports</button>
      <button class="settings-tab" data-itab="export-log">📋 Journal exports</button>
      <button class="settings-tab" data-itab="order-sources">📡 Sources de commandes</button>
      <button class="settings-tab" data-itab="submission-xml">📎 Soumission XML couplé</button>
      <button class="settings-tab" data-itab="submission-erp">🔗 PDF + ERP/W2P</button>
    </div>
    <div id="integ-panel"></div>
  `;

  // Load config
  let cfg = {};
  try {
    const r = await fetch(API.config, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok && r.config) cfg = r.config;
  } catch(e) { /* use defaults */ }

  const integPanel = panel.querySelector('#integ-panel');

  function showIntegTab(tabId) {
    panel.querySelectorAll('.settings-tab[data-itab]').forEach(t => {
      t.classList.toggle('active', t.dataset.itab === tabId);
    });
    switch(tabId) {
      case 'xml-import':    renderXmlImportTab(integPanel, cfg); break;
      case 'erp-import':    renderErpImportTab(integPanel, cfg); break;
      case 'pressero':      renderPresseroTab(integPanel, cfg); break;
      case 'mdsf':          renderMdsfTab(integPanel, cfg); break;
      case 'export':        renderExportTab(integPanel, cfg); break;
      case 'import-log':    renderImportLogTab(integPanel); break;
      case 'export-log':    renderExportLogTab(integPanel); break;
      case 'order-sources': renderSettingsOrderSources(integPanel); break;
      case 'submission-xml': renderSubmissionXmlCouplingTab(integPanel); break;
      case 'submission-erp': renderSubmissionErpLookupTab(integPanel); break;
    }
  }

  panel.querySelectorAll('.settings-tab[data-itab]').forEach(btn => {
    btn.onclick = () => showIntegTab(btn.dataset.itab);
  });

  showIntegTab('xml-import');
}

// ======================================================
// XML IMPORT
// ======================================================
async function renderXmlImportTab(panel, cfg) {
  const xmlCfg = cfg.xmlImport || {};
  const mapping = xmlCfg.mapping || {};

  // Load dynamic fields from form-config (falls back to hardcoded list on error)
  const ficheFields = await loadFicheFields();

  panel.innerHTML = `
    <div class="settings-section-card" style="background:#f0f9ff;border:1px solid #bae6fd;margin-bottom:16px;">
      <p style="margin:0;font-size:13px;color:#0369a1;">
        💡 Les champs disponibles correspondent à ceux configurés dans
        <strong>Paramétrages → Fiche de production</strong>.
        Ajoutez-y vos champs personnalisés pour pouvoir les mapper ici.
      </p>
    </div>

    <div class="settings-section-card">
      <h4>Import XML manuel</h4>
      <p style="color:#6b7280;font-size:13px;">Importez un fichier XML pour pré-remplir automatiquement une fiche de production.</p>
      <div style="margin-bottom:16px;">
        <label style="font-size:13px;font-weight:600;color:#374151;display:block;margin-bottom:6px;">Fichier XML</label>
        <input type="file" id="xml-import-file" accept=".xml" class="settings-input" style="margin-bottom:8px;" />
        <button id="xml-import-btn" class="btn btn-primary">📥 Importer</button>
        <div id="xml-import-msg" style="margin-top:8px;font-size:13px;"></div>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Mapping des champs XML → Fiche</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Associez les balises XML de votre export ERP/W2P aux champs de la fiche.<br>
        Laissez vide pour ignorer un champ.
      </p>
      <div id="xml-mapping-rows" style="display:grid;grid-template-columns:1fr 1fr;gap:10px 20px;max-width:700px;"></div>
      <button id="xml-mapping-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer le mapping</button>
      <div id="xml-mapping-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

    <div class="settings-section-card">
      <h4>Clé de déduplication</h4>
      <p style="color:#6b7280;font-size:13px;">Champ utilisé pour éviter les doublons (mise à jour si la clé existe déjà).</p>
      <select id="xml-dedup-key" class="settings-input" style="min-width:200px;">
        ${ficheFields.map(f => `<option value="${esc(f.key)}" ${(xmlCfg.dedupKey||'referenceCommande')===f.key?'selected':''}>${esc(f.key)} — ${esc(f.label)}</option>`).join('')}
      </select>
      <button id="xml-dedup-save" class="btn btn-primary" style="margin-left:10px;">Enregistrer</button>
      <div id="xml-dedup-msg" style="font-size:13px;margin-top:6px;"></div>
    </div>
  `;

  // Build mapping rows
  const rowsEl = panel.querySelector('#xml-mapping-rows');
  ficheFields.forEach(f => {
    const sid = safeDomId(f.key);
    rowsEl.innerHTML += `
      <div style="display:flex;flex-direction:column;gap:4px;">
        <label style="font-size:12px;font-weight:600;color:#374151;">${esc(f.key)}<span style="font-weight:400;color:#6b7280;margin-left:4px;">— ${esc(f.label)}</span></label>
        <input type="text" id="xml-map-${sid}" placeholder="Balise XML source" class="settings-input"
          value="${esc(mapping[f.key] || '')}" style="font-size:12px;padding:5px 8px;" />
      </div>`;
  });

  // Save mapping
  panel.querySelector('#xml-mapping-save').onclick = async () => {
    const newMapping = {};
    ficheFields.forEach(f => {
      const v = panel.querySelector(`#xml-map-${safeDomId(f.key)}`)?.value?.trim();
      if (v) newMapping[f.key] = v;
    });
    const msgEl = panel.querySelector('#xml-mapping-msg');
    try {
      const r = await fetch(API.config, {
        method: 'PUT',
        headers: authJsonH(),
        body: JSON.stringify({ section: 'xmlImport', data: { ...cfg.xmlImport, mapping: newMapping } })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Mapping enregistré';
        cfg.xmlImport = { ...cfg.xmlImport, mapping: newMapping };
      } else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };

  // Save dedup key
  panel.querySelector('#xml-dedup-save').onclick = async () => {
    const key = panel.querySelector('#xml-dedup-key').value;
    const msgEl = panel.querySelector('#xml-dedup-msg');
    try {
      const r = await fetch(API.config, {
        method: 'PUT',
        headers: authJsonH(),
        body: JSON.stringify({ section: 'xmlImport', data: { ...cfg.xmlImport, dedupKey: key } })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Clé enregistrée';
        cfg.xmlImport = { ...cfg.xmlImport, dedupKey: key };
      } else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };

  // XML import button
  panel.querySelector('#xml-import-btn').onclick = async () => {
    const fileInput = panel.querySelector('#xml-import-file');
    const msgEl = panel.querySelector('#xml-import-msg');
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = '#ef4444'; msgEl.textContent = 'Sélectionnez un fichier XML'; return;
    }
    const formData = new FormData();
    formData.append('file', fileInput.files[0]);
    msgEl.style.color = '#6b7280'; msgEl.textContent = '⏳ Import en cours…';
    try {
      const r = await fetch(API.importXml, {
        method: 'POST',
        headers: authH(),
        body: formData
      }).then(r => r.json()).catch(() => ({ ok: false, error: 'Erreur réseau' }));
      if (r.ok) {
        msgEl.style.color = '#16a34a';
        msgEl.textContent = `✅ ${r.imported || 0} fiche(s) importée(s)${r.updated ? ', ' + r.updated + ' mise(s) à jour' : ''}${r.duplicates ? ', ' + r.duplicates + ' doublon(s) ignoré(s)' : ''}`;
      } else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}

// ======================================================
// ERP IMPORT
// ======================================================
function renderErpImportTab(panel, cfg) {
  const erpCfg = cfg.erp || {};
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Connexion ERP / Source externe</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez la connexion à votre ERP ou logiciel tiers pour importer automatiquement les commandes.
      </p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 20px;max-width:700px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="erp-enabled" ${erpCfg.enabled ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Activer l'import ERP</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Format de données</label>
          <select id="erp-format" class="settings-input" style="width:100%;">
            <option value="xml" ${erpCfg.format==='xml'?'selected':''}>XML</option>
            <option value="json" ${erpCfg.format==='json'?'selected':''}>JSON</option>
            <option value="csv" ${erpCfg.format==='csv'?'selected':''}>CSV</option>
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL de l'endpoint</label>
          <input type="url" id="erp-url" placeholder="https://erp.example.com/api/orders" class="settings-input" style="width:100%;" value="${esc(erpCfg.url||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API / Token</label>
          <input type="password" id="erp-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(erpCfg.apiKey||'')}" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Login (si Basic Auth)</label>
          <input type="text" id="erp-login" class="settings-input" style="width:100%;" value="${esc(erpCfg.login||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Mot de passe (si Basic Auth)</label>
          <input type="password" id="erp-password" placeholder="••••••••" class="settings-input" style="width:100%;" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Fréquence de polling (minutes)</label>
          <input type="number" id="erp-interval" min="1" max="1440" class="settings-input" style="width:120px;" value="${erpCfg.intervalMinutes||60}" />
        </div>
      </div>
      <div style="display:flex;gap:10px;margin-top:16px;flex-wrap:wrap;">
        <button id="erp-save" class="btn btn-primary">💾 Enregistrer</button>
        <button id="erp-test" class="btn">🔌 Tester la connexion</button>
      </div>
      <div id="erp-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#erp-save').onclick = async () => {
    const msgEl = panel.querySelector('#erp-msg');
    const data = {
      enabled: panel.querySelector('#erp-enabled').checked,
      format:  panel.querySelector('#erp-format').value,
      url:     panel.querySelector('#erp-url').value.trim(),
      apiKey:  panel.querySelector('#erp-apikey').value,
      login:   panel.querySelector('#erp-login').value.trim(),
      password: panel.querySelector('#erp-password').value || erpCfg.password || '',
      intervalMinutes: parseInt(panel.querySelector('#erp-interval').value) || 60,
    };
    try {
      const r = await fetch(API.config, {
        method: 'PUT', headers: authJsonH(),
        body: JSON.stringify({ section: 'erp', data })
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Configuration ERP enregistrée'; cfg.erp = data; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Erreur'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };

  panel.querySelector('#erp-test').onclick = async () => {
    const msgEl = panel.querySelector('#erp-msg');
    msgEl.style.color='#6b7280'; msgEl.textContent='⏳ Test de connexion…';
    try {
      const r = await fetch(API.testConn, {
        method: 'POST', headers: authJsonH(),
        body: JSON.stringify({ source: 'erp', url: panel.querySelector('#erp-url').value.trim(), apiKey: panel.querySelector('#erp-apikey').value, format: panel.querySelector('#erp-format').value })
      }).then(r => r.json()).catch(() => ({ ok: false, error: 'Erreur réseau' }));
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Connexion réussie'+(r.message?': '+r.message:''); }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Connexion échouée'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };
}

// ======================================================
// PRESSERO
// ======================================================
function renderPresseroTab(panel, cfg) {
  const pCfg = cfg.pressero || {};
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Pressero (Web-to-Print)</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez la connexion à Pressero pour importer les commandes W2P et/ou renvoyer les statuts.
      </p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 20px;max-width:700px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="pressero-enabled" ${pCfg.enabled ? 'checked' : ''} />
            <span style="font-size:13px;">Activer l'intégration Pressero</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL API Pressero</label>
          <input type="url" id="pressero-url" placeholder="https://api.pressero.com/v1" class="settings-input" style="width:100%;" value="${esc(pCfg.apiUrl||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API</label>
          <input type="password" id="pressero-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Secret API</label>
          <input type="password" id="pressero-secret" placeholder="••••••••" class="settings-input" style="width:100%;" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Webhook (URL de réception Pressero → GestionAtelier)</label>
          <input type="url" id="pressero-webhook" placeholder="https://gestionatelier.example.com/api/webhooks/pressero" class="settings-input" style="width:100%;" value="${esc(pCfg.webhookUrl||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Import automatique des commandes</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="pressero-auto-import" ${pCfg.autoImport ? 'checked' : ''} />
            <span style="font-size:13px;">Importer automatiquement</span>
          </label>
        </div>
      </div>
      <div style="display:flex;gap:10px;margin-top:16px;flex-wrap:wrap;">
        <button id="pressero-save" class="btn btn-primary">💾 Enregistrer</button>
        <button id="pressero-test" class="btn">🔌 Tester la connexion</button>
      </div>
      <div id="pressero-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#pressero-save').onclick = async () => {
    const msgEl = panel.querySelector('#pressero-msg');
    const data = {
      enabled: panel.querySelector('#pressero-enabled').checked,
      apiUrl: panel.querySelector('#pressero-url').value.trim(),
      apiKey: panel.querySelector('#pressero-apikey').value || pCfg.apiKey || '',
      apiSecret: panel.querySelector('#pressero-secret').value || pCfg.apiSecret || '',
      webhookUrl: panel.querySelector('#pressero-webhook').value.trim(),
      autoImport: panel.querySelector('#pressero-auto-import').checked,
    };
    try {
      const r = await fetch(API.config, { method:'PUT', headers: authJsonH(), body: JSON.stringify({ section:'pressero', data }) }).then(r=>r.json());
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Configuration Pressero enregistrée'; cfg.pressero=data; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Erreur'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };

  panel.querySelector('#pressero-test').onclick = async () => {
    const msgEl = panel.querySelector('#pressero-msg');
    msgEl.style.color='#6b7280'; msgEl.textContent='⏳ Test de connexion Pressero…';
    try {
      const r = await fetch(API.testConn, { method:'POST', headers: authJsonH(), body: JSON.stringify({ source:'pressero', url: panel.querySelector('#pressero-url').value.trim(), apiKey: panel.querySelector('#pressero-apikey').value || pCfg.apiKey || '' }) }).then(r=>r.json()).catch(()=>({ ok:false, error:'Erreur réseau' }));
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Connexion réussie'; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Connexion échouée'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };
}

// ======================================================
// MDSF (Market Direct Store Front)
// ======================================================
function renderMdsfTab(panel, cfg) {
  const mCfg = cfg.mdsf || {};
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Market Direct StoreFront (MDSF)</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez la connexion à Market Direct StoreFront pour synchroniser les commandes.
      </p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 20px;max-width:700px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="mdsf-enabled" ${mCfg.enabled ? 'checked' : ''} />
            <span style="font-size:13px;">Activer l'intégration MDSF</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL API MDSF</label>
          <input type="url" id="mdsf-url" placeholder="https://mdsf.example.com/api" class="settings-input" style="width:100%;" value="${esc(mCfg.apiUrl||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API / Token</label>
          <input type="password" id="mdsf-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Site ID / Store ID</label>
          <input type="text" id="mdsf-storeid" class="settings-input" style="width:100%;" value="${esc(mCfg.storeId||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Import automatique</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="mdsf-auto-import" ${mCfg.autoImport ? 'checked' : ''} />
            <span style="font-size:13px;">Importer automatiquement les commandes</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Fréquence (minutes)</label>
          <input type="number" id="mdsf-interval" min="1" max="1440" class="settings-input" style="width:120px;" value="${mCfg.intervalMinutes||30}" />
        </div>
      </div>
      <div style="display:flex;gap:10px;margin-top:16px;flex-wrap:wrap;">
        <button id="mdsf-save" class="btn btn-primary">💾 Enregistrer</button>
        <button id="mdsf-test" class="btn">🔌 Tester la connexion</button>
      </div>
      <div id="mdsf-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#mdsf-save').onclick = async () => {
    const msgEl = panel.querySelector('#mdsf-msg');
    const data = {
      enabled: panel.querySelector('#mdsf-enabled').checked,
      apiUrl: panel.querySelector('#mdsf-url').value.trim(),
      apiKey: panel.querySelector('#mdsf-apikey').value || mCfg.apiKey || '',
      storeId: panel.querySelector('#mdsf-storeid').value.trim(),
      autoImport: panel.querySelector('#mdsf-auto-import').checked,
      intervalMinutes: parseInt(panel.querySelector('#mdsf-interval').value)||30,
    };
    try {
      const r = await fetch(API.config, { method:'PUT', headers: authJsonH(), body: JSON.stringify({ section:'mdsf', data }) }).then(r=>r.json());
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Configuration MDSF enregistrée'; cfg.mdsf=data; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Erreur'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };

  panel.querySelector('#mdsf-test').onclick = async () => {
    const msgEl = panel.querySelector('#mdsf-msg');
    msgEl.style.color='#6b7280'; msgEl.textContent='⏳ Test de connexion MDSF…';
    try {
      const r = await fetch(API.testConn, { method:'POST', headers: authJsonH(), body: JSON.stringify({ source:'mdsf', url: panel.querySelector('#mdsf-url').value.trim(), apiKey: panel.querySelector('#mdsf-apikey').value || mCfg.apiKey||'' }) }).then(r=>r.json()).catch(()=>({ ok:false, error:'Erreur réseau' }));
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Connexion réussie'; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Connexion échouée'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };
}

// ======================================================
// EXPORT COMMANDES
// ======================================================
async function renderExportTab(panel, cfg) {
  const expCfg = cfg.export || {};

  // Load dynamic fields from form-config (falls back to hardcoded list on error)
  const ficheFields = await loadFicheFields();

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Export des commandes</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez les formats et destinations d'export des informations de commandes.
      </p>

      <h5 style="margin-bottom:8px;font-size:13px;color:#374151;">Formats disponibles</h5>
      <div style="display:flex;flex-wrap:wrap;gap:10px;margin-bottom:20px;">
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;">
          <input type="checkbox" id="exp-xml" ${expCfg.enableXml !== false ? 'checked' : ''} />
          <span style="font-size:13px;font-weight:600;">XML</span>
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;">
          <input type="checkbox" id="exp-csv" ${expCfg.enableCsv !== false ? 'checked' : ''} />
          <span style="font-size:13px;font-weight:600;">CSV</span>
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;">
          <input type="checkbox" id="exp-erp" ${expCfg.enableErp ? 'checked' : ''} />
          <span style="font-size:13px;font-weight:600;">Envoi vers ERP</span>
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;">
          <input type="checkbox" id="exp-pressero" ${expCfg.enablePressero ? 'checked' : ''} />
          <span style="font-size:13px;font-weight:600;">Renvoi vers Pressero</span>
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;">
          <input type="checkbox" id="exp-mdsf" ${expCfg.enableMdsf ? 'checked' : ''} />
          <span style="font-size:13px;font-weight:600;">Renvoi vers MDSF</span>
        </label>
      </div>

      <h5 style="margin-bottom:8px;font-size:13px;color:#374151;">Séparateur CSV</h5>
      <select id="exp-csv-sep" class="settings-input" style="min-width:100px;margin-bottom:16px;">
        <option value="," ${(expCfg.csvSeparator||';')===','?'selected':''}>Virgule (,)</option>
        <option value=";" ${(expCfg.csvSeparator||';')===';'?'selected':''}>Point-virgule (;)</option>
        <option value="\\t" ${expCfg.csvSeparator==='\\t'?'selected':''}>Tabulation</option>
      </select>

      <h5 style="margin-bottom:8px;font-size:13px;color:#374151;">Mapping des champs (export)</h5>
      <p style="color:#6b7280;font-size:12px;margin-bottom:10px;">Renommez les champs dans le fichier d'export. Laissez vide pour utiliser le nom interne.</p>
      <div id="exp-mapping-rows" style="display:grid;grid-template-columns:1fr 1fr;gap:8px 20px;max-width:700px;margin-bottom:16px;"></div>

      <button id="exp-save" class="btn btn-primary">💾 Enregistrer la configuration d'export</button>
      <div id="exp-msg" style="margin-top:8px;font-size:13px;"></div>

      <hr style="margin:20px 0;border:none;border-top:1px solid #e5e7eb;" />

      <h5 style="margin-bottom:8px;font-size:13px;color:#374151;">Test d'export (commandes récentes)</h5>
      <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;">
        <select id="exp-test-format" class="settings-input" style="min-width:140px;">
          <option value="xml">XML</option>
          <option value="csv">CSV</option>
        </select>
        <button id="exp-test-btn" class="btn btn-primary">📤 Télécharger un export test</button>
      </div>
      <div id="exp-test-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  const expMapping = expCfg.mapping || {};
  const rowsEl = panel.querySelector('#exp-mapping-rows');
  ficheFields.forEach(f => {
    const sid = safeDomId(f.key);
    rowsEl.innerHTML += `
      <div style="display:flex;flex-direction:column;gap:4px;">
        <label style="font-size:12px;font-weight:600;color:#374151;">${esc(f.key)}<span style="font-weight:400;color:#6b7280;margin-left:4px;">— ${esc(f.label)}</span></label>
        <input type="text" id="exp-map-${sid}" placeholder="${esc(f.key)}" class="settings-input"
          value="${esc(expMapping[f.key]||'')}" style="font-size:12px;padding:5px 8px;" />
      </div>`;
  });

  panel.querySelector('#exp-save').onclick = async () => {
    const msgEl = panel.querySelector('#exp-msg');
    const mapping = {};
    ficheFields.forEach(f => { const v=panel.querySelector(`#exp-map-${safeDomId(f.key)}`)?.value?.trim(); if(v) mapping[f.key]=v; });
    const data = {
      enableXml: panel.querySelector('#exp-xml').checked,
      enableCsv: panel.querySelector('#exp-csv').checked,
      enableErp: panel.querySelector('#exp-erp').checked,
      enablePressero: panel.querySelector('#exp-pressero').checked,
      enableMdsf: panel.querySelector('#exp-mdsf').checked,
      csvSeparator: panel.querySelector('#exp-csv-sep').value,
      mapping,
    };
    try {
      const r = await fetch(API.config, { method:'PUT', headers: authJsonH(), body: JSON.stringify({ section:'export', data }) }).then(r=>r.json());
      if (r.ok) { msgEl.style.color='#16a34a'; msgEl.textContent='✅ Configuration d\'export enregistrée'; cfg.export=data; }
      else { msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(r.error||'Erreur'); }
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };

  panel.querySelector('#exp-test-btn').onclick = async () => {
    const fmt = panel.querySelector('#exp-test-format').value;
    const msgEl = panel.querySelector('#exp-test-msg');
    msgEl.style.color='#6b7280'; msgEl.textContent='⏳ Génération de l\'export…';
    try {
      const resp = await fetch(`${API.exportCmd}?format=${fmt}&limit=10`, { headers: authH() });
      if (!resp.ok) {
        const err = await resp.json().catch(()=>({ error: 'Erreur' }));
        msgEl.style.color='#ef4444'; msgEl.textContent='❌ '+(err.error||'Erreur');
        return;
      }
      const blob = await resp.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `export-commandes.${fmt}`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      msgEl.style.color='#16a34a'; msgEl.textContent='✅ Export téléchargé';
    } catch(e) { msgEl.style.color='#ef4444'; msgEl.textContent='❌ Erreur réseau'; }
  };
}

// ======================================================
// JOURNAL IMPORTS
// ======================================================
async function renderImportLogTab(panel) {
  panel.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement du journal…</div>';
  try {
    const r = await fetch(API.importLog + '?limit=50', { headers: authH() }).then(r => r.json()).catch(() => ({ ok: false, logs: [] }));
    const logs = (r.ok && Array.isArray(r.logs)) ? r.logs : [];
    if (logs.length === 0) {
      panel.innerHTML = '<div class="settings-section-card"><p style="color:#9ca3af;">Aucun import enregistré.</p></div>';
      return;
    }
    const rowsHtml = logs.map(l => `
      <tr style="border-bottom:1px solid #f3f4f6;">
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${new Date(l.timestamp).toLocaleString('fr-FR')}</td>
        <td style="padding:8px 10px;font-size:12px;font-weight:600;">${esc(l.source||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.fileName||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${l.status==='ok'?'✅ Succès':l.status==='update'?'🔄 MàJ':'❌ Erreur'}</td>
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${esc(l.message||'')}</td>
      </tr>`).join('');
    panel.innerHTML = `
      <div class="settings-section-card">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
          <h4>Journal des imports (${logs.length} dernières entrées)</h4>
          <button id="import-log-refresh" class="btn btn-sm btn-primary">Rafraîchir</button>
        </div>
        <div style="overflow-x:auto;">
          <table style="width:100%;border-collapse:collapse;font-size:12px;">
            <thead>
              <tr style="background:#f9fafb;text-align:left;">
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Date</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Source</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Fichier</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Statut</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Message</th>
              </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
          </table>
        </div>
      </div>`;
    panel.querySelector('#import-log-refresh').onclick = () => renderImportLogTab(panel);
  } catch(e) {
    panel.innerHTML = '<div class="settings-section-card"><p style="color:#ef4444;">Erreur lors du chargement du journal.</p></div>';
  }
}

// ======================================================
// JOURNAL EXPORTS
// ======================================================
async function renderExportLogTab(panel) {
  panel.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement du journal…</div>';
  try {
    const r = await fetch(API.exportLog + '?limit=50', { headers: authH() }).then(r => r.json()).catch(() => ({ ok: false, logs: [] }));
    const logs = (r.ok && Array.isArray(r.logs)) ? r.logs : [];
    if (logs.length === 0) {
      panel.innerHTML = '<div class="settings-section-card"><p style="color:#9ca3af;">Aucun export enregistré.</p></div>';
      return;
    }
    const rowsHtml = logs.map(l => `
      <tr style="border-bottom:1px solid #f3f4f6;">
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${new Date(l.timestamp).toLocaleString('fr-FR')}</td>
        <td style="padding:8px 10px;font-size:12px;font-weight:600;">${esc(l.format||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.destination||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${l.status==='ok'?'✅ Succès':'❌ Erreur'}</td>
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${esc(l.message||'')}</td>
      </tr>`).join('');
    panel.innerHTML = `
      <div class="settings-section-card">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
          <h4>Journal des exports (${logs.length} dernières entrées)</h4>
          <button id="export-log-refresh" class="btn btn-sm btn-primary">Rafraîchir</button>
        </div>
        <div style="overflow-x:auto;">
          <table style="width:100%;border-collapse:collapse;font-size:12px;">
            <thead>
              <tr style="background:#f9fafb;text-align:left;">
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Date</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Format</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Destination</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Statut</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;">Message</th>
              </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
          </table>
        </div>
      </div>`;
    panel.querySelector('#export-log-refresh').onclick = () => renderExportLogTab(panel);
  } catch(e) {
    panel.innerHTML = '<div class="settings-section-card"><p style="color:#ef4444;">Erreur lors du chargement du journal.</p></div>';
  }
}

// ======================================================
// SOUMISSION XML COUPLÉ
// ======================================================
async function renderSubmissionXmlCouplingTab(panel) {
  panel.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement…</div>';
  let cfg = {};
  try {
    const r = await fetch('/api/settings/submission-xml-coupling', { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok && r.config) cfg = r.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>📎 Soumission PDF + XML couplés</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Lorsque cette option est activée, l'onglet <strong>Soumission</strong> accepte de déposer simultanément
        un <strong>PDF</strong> et un <strong>XML de métadonnées</strong>. Les données XML pré-remplissent
        automatiquement la fiche en utilisant le mapping configuré dans l'onglet <em>Import XML</em>.
      </p>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:14px 20px;max-width:600px;margin-bottom:20px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="sxml-enabled" ${cfg.enabled !== false ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Activer la détection PDF+XML en Soumission</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Comportement après détection</label>
          <select id="sxml-mode" class="settings-input" style="width:100%;">
            <option value="prefill" ${(cfg.mode||'prefill')==='prefill'?'selected':''}>Ouvrir le formulaire pré-rempli (recommandé)</option>
            <option value="create" ${cfg.mode==='create'?'selected':''}>Créer la fiche directement (sans formulaire)</option>
          </select>
        </div>
      </div>

      <div class="settings-section-card" style="background:#f8fafc;border:1px solid #e2e8f0;margin-bottom:18px;">
        <h5 style="margin:0 0 10px;font-size:13px;color:#374151;">Règles de couplage PDF ↔ XML</h5>
        <ul style="margin:0;padding-left:18px;font-size:12px;color:#6b7280;line-height:1.8;">
          <li><strong>1 PDF + 1 XML</strong> → couplage automatique (même nom de base recommandé).</li>
          <li><strong>N PDF + 1 XML</strong> → tous les PDF rattachés à la même fiche avec les métadonnées du XML.</li>
          <li><strong>N PDF + N XML</strong> → appariement par nom de base (ex : <code>commande01.pdf</code> + <code>commande01.xml</code>).</li>
          <li><strong>PDF seul</strong> → comportement habituel (upload simple).</li>
          <li><strong>XML seul</strong> → création de la fiche sans PDF (le PDF peut être ajouté ensuite).</li>
        </ul>
      </div>

      <p style="color:#6b7280;font-size:12px;margin-bottom:12px;">
        Le <strong>mapping XML → champs fiche</strong> et la <strong>clé de déduplication</strong> sont définis
        dans l'onglet <em>📥 Import XML</em> et sont réutilisés ici.
      </p>

      <button id="sxml-save" class="btn btn-primary">💾 Enregistrer</button>
      <div id="sxml-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#sxml-save').onclick = async () => {
    const msgEl = panel.querySelector('#sxml-msg');
    const data = {
      enabled: panel.querySelector('#sxml-enabled').checked,
      mode:    panel.querySelector('#sxml-mode').value,
    };
    try {
      const r = await fetch('/api/settings/submission-xml-coupling', {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(data)
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration enregistrée'; }
      else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}

// ======================================================
// SOUMISSION PDF + ERP / W2P LOOKUP
// ======================================================
async function renderSubmissionErpLookupTab(panel) {
  panel.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement…</div>';
  let cfg = { enabled: true, defaultSource: '', refDetectionRegex: '', autoLookup: false, erpSources: [] };
  try {
    const r = await fetch('/api/settings/submission-erp-lookup', { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok && r.config) cfg = { ...cfg, ...r.config };
  } catch(e) { /* use defaults */ }

  function buildErpSourceForm(src = {}) {
    const id = src.id || ('src_' + Date.now());
    return `
      <div class="erp-src-card" data-id="${id}" style="border:1px solid #e5e7eb;border-radius:10px;padding:14px;margin-bottom:12px;background:#fff;">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;">
          <strong style="font-size:13px;">${esc(src.name || 'Nouvelle source ERP')}</strong>
          <button class="btn btn-sm erp-src-remove" style="color:#ef4444;padding:2px 8px;">✕ Supprimer</button>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 16px;">
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Nom</label>
            <input type="text" class="settings-input erp-src-name" value="${esc(src.name||'')}" placeholder="Mon ERP" style="width:100%;" />
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">URL (avec {ref} si nécessaire)</label>
            <input type="url" class="settings-input erp-src-url" value="${esc(src.url||'')}" placeholder="https://erp.example.com/api/orders/{ref}" style="width:100%;" />
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Authentification</label>
            <select class="settings-input erp-src-authtype" style="width:100%;">
              <option value="none" ${(src.authType||'none')==='none'?'selected':''}>Aucune</option>
              <option value="basic" ${src.authType==='basic'?'selected':''}>Basic Auth</option>
              <option value="bearer" ${src.authType==='bearer'?'selected':''}>Bearer Token</option>
              <option value="apikey" ${src.authType==='apikey'?'selected':''}>API Key (header)</option>
            </select>
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Login (Basic Auth)</label>
            <input type="text" class="settings-input erp-src-authuser" value="${esc(src.authUser||'')}" placeholder="login" style="width:100%;" />
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Mot de passe / Token</label>
            <input type="password" class="settings-input erp-src-authpwd" placeholder="••••••••" autocomplete="new-password" style="width:100%;" />
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Nom du header (API Key)</label>
            <input type="text" class="settings-input erp-src-authheader" value="${esc(src.authHeader||'X-Api-Key')}" style="width:100%;" />
          </div>
          <div>
            <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Format réponse</label>
            <select class="settings-input erp-src-format" style="width:100%;">
              <option value="json" ${(src.responseFormat||'json')==='json'?'selected':''}>JSON</option>
              <option value="xml" ${src.responseFormat==='xml'?'selected':''}>XML</option>
            </select>
          </div>
        </div>
        <input type="hidden" class="erp-src-id" value="${id}" />
      </div>`;
  }

  const sources = cfg.erpSources || [];

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>🔗 Import PDF + ERP / W2P</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Permet, lors d'un dépôt de PDF dans Soumission, de récupérer les métadonnées de la commande
        depuis un <strong>ERP</strong> ou depuis un <strong>W2P</strong> (Pressero, MDSF) en saisissant
        ou en détectant automatiquement le n° de commande.
      </p>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:14px 20px;max-width:700px;margin-bottom:20px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="serp-enabled" ${cfg.enabled !== false ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Afficher le bouton "🔗 ERP/W2P" en Soumission</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Source par défaut</label>
          <select id="serp-default-source" class="settings-input" style="width:100%;">
            <option value="">— Aucune —</option>
            <option value="pressero" ${cfg.defaultSource==='pressero'?'selected':''}>Pressero</option>
            <option value="mdsf" ${cfg.defaultSource==='mdsf'?'selected':''}>MDSF</option>
            ${sources.map(s => `<option value="${esc(s.id||s.name)}" ${cfg.defaultSource===(s.id||s.name)?'selected':''}>${esc(s.name)}</option>`).join('')}
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Regex de détection de référence</label>
          <input type="text" id="serp-regex" class="settings-input" style="width:100%;"
            value="${esc(cfg.refDetectionRegex||'')}" placeholder="ex: ^([A-Z0-9-]+)_.*\\.pdf$" />
          <div style="font-size:11px;color:#9ca3af;margin-top:3px;">Appliquée au nom du fichier PDF. Le 1er groupe capturant est utilisé comme référence.</div>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Auto-lookup au drop</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="serp-auto" ${cfg.autoLookup ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Lancer la recherche automatiquement si une référence est détectée</span>
          </label>
        </div>
      </div>

      <button id="serp-save-global" class="btn btn-primary" style="margin-bottom:20px;">💾 Enregistrer la configuration globale</button>
      <div id="serp-msg-global" style="margin-bottom:16px;font-size:13px;"></div>

      <hr style="border:none;border-top:1px solid #e5e7eb;margin:16px 0;" />
      <h5 style="font-size:13px;color:#374151;margin-bottom:12px;">Sources ERP génériques</h5>
      <div id="serp-sources-list">
        ${sources.map(s => buildErpSourceForm(s)).join('')}
        ${sources.length === 0 ? '<p style="color:#9ca3af;font-size:13px;">Aucune source ERP configurée.</p>' : ''}
      </div>
      <button id="serp-add-source" class="btn" style="margin-bottom:16px;">+ Ajouter une source ERP</button>
      <br/>
      <button id="serp-save-sources" class="btn btn-primary">💾 Enregistrer les sources ERP</button>
      <div id="serp-msg-sources" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  // Add source
  panel.querySelector('#serp-add-source').onclick = () => {
    const listEl = panel.querySelector('#serp-sources-list');
    const noSrc = listEl.querySelector('p');
    if (noSrc) noSrc.remove();
    listEl.insertAdjacentHTML('beforeend', buildErpSourceForm({}));
    listEl.querySelectorAll('.erp-src-remove').forEach(btn => {
      btn.onclick = () => btn.closest('.erp-src-card').remove();
    });
  };

  // Remove source buttons
  panel.querySelectorAll('.erp-src-remove').forEach(btn => {
    btn.onclick = () => btn.closest('.erp-src-card').remove();
  });

  // Save global config
  panel.querySelector('#serp-save-global').onclick = async () => {
    const msgEl = panel.querySelector('#serp-msg-global');
    const data = {
      enabled:           panel.querySelector('#serp-enabled').checked,
      defaultSource:     panel.querySelector('#serp-default-source').value,
      refDetectionRegex: panel.querySelector('#serp-regex').value.trim(),
      autoLookup:        panel.querySelector('#serp-auto').checked,
    };
    try {
      const r = await fetch('/api/settings/submission-erp-lookup', {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(data)
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration globale enregistrée'; }
      else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };

  // Save sources
  panel.querySelector('#serp-save-sources').onclick = async () => {
    const msgEl = panel.querySelector('#serp-msg-sources');
    const cards = panel.querySelectorAll('.erp-src-card');
    const erpSources = Array.from(cards).map(card => ({
      id:             card.querySelector('.erp-src-id').value,
      name:           card.querySelector('.erp-src-name').value.trim(),
      url:            card.querySelector('.erp-src-url').value.trim(),
      authType:       card.querySelector('.erp-src-authtype').value,
      authUser:       card.querySelector('.erp-src-authuser').value.trim(),
      authPassword:   card.querySelector('.erp-src-authpwd').value,
      authHeader:     card.querySelector('.erp-src-authheader').value.trim(),
      responseFormat: card.querySelector('.erp-src-format').value,
    }));
    try {
      const r = await fetch('/api/settings/submission-erp-lookup', {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify({ erpSources })
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Sources ERP enregistrées'; }
      else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}
