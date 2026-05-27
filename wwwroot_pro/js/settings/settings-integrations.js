// settings-integrations.js — Paramétrages → Intégrations
// Gestion des imports automatiques (XML, ERP, Pressero, MDSF) et exports (XML, CSV, ERP, Pressero, MDSF)
import { authToken, showNotification, esc } from '../core.js';
import { renderSettingsOrderSources } from './settings-order-sources.js';
import { renderXmlMappingBuilder } from './xml-mapping-builder.js';

const API = {
  config:    '/api/settings/integrations-config',
  testConn:  '/api/settings/integrations/test-connection',
  importXml: '/api/integrations/import-xml',
  importLog: '/api/integrations/import-log',
  exportLog: '/api/integrations/export-log',
  exportCmd: '/api/integrations/export',
};

function authH()     { return { 'Authorization': `Bearer ${authToken}` }; }
function authJsonH() { return { ...authH(), 'Content-Type': 'application/json' }; }

/** Converts a field key to a safe CSS/HTML identifier */
function safeDomId(key) {
  return (key || '').replace(/[^A-Za-z0-9_-]/g, '_');
}

/** Charge les champs fiche depuis l'API (avec fallback sur liste statique) */
async function loadFicheFields() {
  try {
    const r = await fetch('/api/settings/form-config', { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok && Array.isArray(r.fields) && r.fields.length > 0) {
      return r.fields.map(f => ({ key: f.key || f.id, label: f.label || f.key || f.id }));
    }
  } catch(e) { /* fallback */ }
  return [
    { key: 'numeroDossier',            label: 'N° Dossier' },
    { key: 'client',                   label: 'Client' },
    { key: 'nomClient',                label: 'Nom client' },
    { key: 'typeTravail',              label: 'Type de travail' },
    { key: 'quantite',                 label: 'Quantité' },
    { key: 'formatFini',               label: 'Format fini' },
    { key: 'moteurImpression',         label: 'Moteur impression' },
    { key: 'operateur',                label: 'Opérateur' },
    { key: 'dateReceptionSouhaitee',   label: 'Date réception souhaitée' },
    { key: 'dateLivraisonSouhaitee',   label: 'Date livraison souhaitée' },
    { key: 'retraitLivraison',         label: 'Retrait / Livraison' },
    { key: 'commentaire',              label: 'Commentaire' },
    { key: 'referenceCommande',        label: 'Référence commande' },
  ];
}

// ======================================================
// EXPORT PRINCIPAL
// ======================================================
export async function renderSettingsIntegrations(panel) {
  panel.innerHTML = `
    <h3>Intégrations — Import &amp; Export</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Configurez les sources d'import automatique de la fiche de production (XML, ERP, Web-to-Print)
      et les destinations d'export des commandes.
    </p>

    <!-- ▶ Fournisseur actif — mutuellement exclusif -->
    <div class="settings-section-card" style="margin-bottom:16px;background:#fafafa;">
      <h4 style="margin-top:0;">🔌 Fournisseur d'intégration externe actif</h4>
      <p style="font-size:13px;color:#6b7280;margin-bottom:12px;">
        Vous ne pouvez activer qu'un seul fournisseur à la fois (ERP, Pressero ou MDSF).
        Sélectionnez celui qui est utilisé dans votre atelier.
      </p>
      <div style="display:flex;gap:20px;flex-wrap:wrap;" id="active-provider-radios">
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;font-size:14px;">
          <input type="radio" name="active-provider" value="none" /> Aucun
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;font-size:14px;">
          <input type="radio" name="active-provider" value="erp" /> 🔗 ERP / Import auto
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;font-size:14px;">
          <input type="radio" name="active-provider" value="pressero" /> 🌐 Pressero
        </label>
        <label style="display:flex;align-items:center;gap:6px;cursor:pointer;font-size:14px;">
          <input type="radio" name="active-provider" value="mdsf" /> 🌐 MDSF
        </label>
      </div>
      <div id="active-provider-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

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

  let cfg = {};
  try {
    const r = await fetch(API.config, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok && r.config) cfg = r.config;
  } catch(e) { }

  let activeProvider = cfg.activeProvider || 'none';
  if (activeProvider === 'none') {
    if (cfg.erp?.enabled)      activeProvider = 'erp';
    else if (cfg.pressero?.enabled) activeProvider = 'pressero';
    else if (cfg.mdsf?.enabled)     activeProvider = 'mdsf';
  }

  const setProviderRadio = (val) => {
    panel.querySelectorAll('input[name="active-provider"]').forEach(r => {
      r.checked = r.value === val;
    });
  };
  setProviderRadio(activeProvider);

  panel.querySelectorAll('input[name="active-provider"]').forEach(radio => {
    radio.onchange = async () => {
      activeProvider = radio.value;
      const msgEl = panel.querySelector('#active-provider-msg');
      try {
        const r = await fetch(API.config, {
          method: 'PUT',
          headers: authJsonH(),
          body: JSON.stringify({ activeProvider })
        }).then(r => r.json());
        if (r.ok) {
          msgEl.style.color = '#16a34a';
          msgEl.textContent = '✅ Fournisseur actif enregistré';
        } else {
          msgEl.style.color = '#ef4444';
          msgEl.textContent = '❌ ' + (r.error || 'Erreur');
        }
      } catch(e) {
        msgEl.style.color = '#ef4444';
        msgEl.textContent = '❌ Erreur réseau';
      }
    };
  });

  const integPanel = panel.querySelector('#integ-panel');
  function showIntegTab(tabId) {
    panel.querySelectorAll('.settings-tab[data-itab]').forEach(t => {
      t.classList.toggle('active', t.dataset.itab === tabId);
    });
    switch(tabId) {
      case 'xml-import':     renderXmlImportTab(integPanel, cfg); break;
      case 'erp-import':     renderErpImportTab(integPanel, cfg, activeProvider); break;
      case 'pressero':       renderPresseroTab(integPanel, cfg, activeProvider); break;
      case 'mdsf':           renderMdsfTab(integPanel, cfg, activeProvider); break;
      case 'export':         renderExportTab(integPanel, cfg); break;
      case 'import-log':     renderImportLogTab(integPanel); break;
      case 'export-log':     renderExportLogTab(integPanel); break;
      case 'order-sources':  renderSettingsOrderSources(integPanel); break;
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

    <div id="xml-builder-container"></div>

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

  const builderContainer = panel.querySelector('#xml-builder-container');
  await renderXmlMappingBuilder(builderContainer, cfg, ficheFields);

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
function renderErpImportTab(panel, cfg, activeProvider = 'none') {
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
            <span style="font-size:13px;">Activer l'import ERP automatique</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL de l'endpoint</label>
          <input type="url" id="erp-url" placeholder="[erp.example.com](https://erp.example.com/api/orders)" class="settings-input" style="width:100%;" value="${esc(erpCfg.url||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API / Token</label>
          <input type="password" id="erp-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(erpCfg.apiKey||'')}" autocomplete="new-password" />
        </div>
      </div>
      <button id="erp-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="erp-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;
  panel.querySelector('#erp-save').onclick = async () => {
    const msgEl = panel.querySelector('#erp-msg');
    const data = {
      erp: {
        enabled: panel.querySelector('#erp-enabled').checked,
        url:     panel.querySelector('#erp-url').value.trim(),
        apiKey:  panel.querySelector('#erp-apikey').value,
      }
    };
    try {
      const r = await fetch(API.config, { method: 'PUT', headers: authJsonH(), body: JSON.stringify(data) }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration ERP enregistrée'; }
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}

// ======================================================
// PRESSERO
// ======================================================
function renderPresseroTab(panel, cfg, activeProvider = 'none') {
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
            <input type="checkbox" id="pressero-enabled" ${pCfg.enabled ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Activer l'intégration Pressero</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL de base</label>
          <input type="url" id="pressero-url" placeholder="[monsite.pressero.com](https://monsite.pressero.com)" class="settings-input" style="width:100%;" value="${esc(pCfg.url||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API</label>
          <input type="password" id="pressero-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(pCfg.apiKey||'')}" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Webhook secret</label>
          <input type="password" id="pressero-webhook" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(pCfg.webhookSecret||'')}" autocomplete="new-password" />
        </div>
      </div>
      <button id="pressero-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="pressero-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;
  panel.querySelector('#pressero-save').onclick = async () => {
    const msgEl = panel.querySelector('#pressero-msg');
    const data = {
      pressero: {
        enabled:       panel.querySelector('#pressero-enabled').checked,
        url:           panel.querySelector('#pressero-url').value.trim(),
        apiKey:        panel.querySelector('#pressero-apikey').value,
        webhookSecret: panel.querySelector('#pressero-webhook').value,
      }
    };
    try {
      const r = await fetch(API.config, { method: 'PUT', headers: authJsonH(), body: JSON.stringify(data) }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration Pressero enregistrée'; }
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}

// ======================================================
// MDSF (Market Direct Store Front)
// ======================================================
function renderMdsfTab(panel, cfg, activeProvider = 'none') {
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
            <input type="checkbox" id="mdsf-enabled" ${mCfg.enabled ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Activer l'intégration MDSF</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL de base</label>
          <input type="url" id="mdsf-url" placeholder="[monsite.mdsf.com](https://monsite.mdsf.com)" class="settings-input" style="width:100%;" value="${esc(mCfg.url||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Clé API</label>
          <input type="password" id="mdsf-apikey" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(mCfg.apiKey||'')}" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Webhook secret</label>
          <input type="password" id="mdsf-webhook" placeholder="••••••••" class="settings-input" style="width:100%;" value="${esc(mCfg.webhookSecret||'')}" autocomplete="new-password" />
        </div>
      </div>
      <button id="mdsf-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="mdsf-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;
  panel.querySelector('#mdsf-save').onclick = async () => {
    const msgEl = panel.querySelector('#mdsf-msg');
    const data = {
      mdsf: {
        enabled:       panel.querySelector('#mdsf-enabled').checked,
        url:           panel.querySelector('#mdsf-url').value.trim(),
        apiKey:        panel.querySelector('#mdsf-apikey').value,
        webhookSecret: panel.querySelector('#mdsf-webhook').value,
      }
    };
    try {
      const r = await fetch(API.config, { method: 'PUT', headers: authJsonH(), body: JSON.stringify(data) }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration MDSF enregistrée'; }
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}

// ======================================================
// EXPORT COMMANDES
// ======================================================
function renderExportTab(panel, cfg) {
  const expCfg = cfg.export || {};
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Export des commandes</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez les formats et destinations d'export des informations de commandes.
      </p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 20px;max-width:700px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Format d'export</label>
          <select id="export-format" class="settings-input" style="width:100%;">
            <option value="xml"  ${expCfg.format==='xml'  ? 'selected':''}>XML</option>
            <option value="csv"  ${expCfg.format==='csv'  ? 'selected':''}>CSV</option>
            <option value="json" ${expCfg.format==='json' ? 'selected':''}>JSON</option>
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Destination</label>
          <select id="export-dest" class="settings-input" style="width:100%;">
            <option value="download" ${expCfg.destination==='download' ? 'selected':''}>Téléchargement</option>
            <option value="erp"      ${expCfg.destination==='erp'      ? 'selected':''}>ERP (push)</option>
            <option value="pressero" ${expCfg.destination==='pressero' ? 'selected':''}>Pressero</option>
            <option value="mdsf"     ${expCfg.destination==='mdsf'     ? 'selected':''}>MDSF</option>
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL destination (si push)</label>
          <input type="url" id="export-url" placeholder="[erp.example.com](https://erp.example.com/api/receive)" class="settings-input" style="width:100%;" value="${esc(expCfg.url||'')}" />
        </div>
      </div>
      <button id="export-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <button id="export-now" class="btn btn-secondary" style="margin-top:16px;margin-left:8px;">📤 Exporter maintenant</button>
      <div id="export-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;
  panel.querySelector('#export-save').onclick = async () => {
    const msgEl = panel.querySelector('#export-msg');
    const data = {
      export: {
        format:      panel.querySelector('#export-format').value,
        destination: panel.querySelector('#export-dest').value,
        url:         panel.querySelector('#export-url').value.trim(),
      }
    };
    try {
      const r = await fetch(API.config, { method: 'PUT', headers: authJsonH(), body: JSON.stringify(data) }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Configuration export enregistrée'; }
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
  panel.querySelector('#export-now').onclick = async () => {
    const msgEl = panel.querySelector('#export-msg');
    try {
      const r = await fetch(API.exportCmd, { method: 'POST', headers: authJsonH() }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Export lancé — ' + (r.message || ''); }
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
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
      <tr>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.date||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.source||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.file||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">
          <span style="color:${l.status==='ok'?'#16a34a':l.status==='doublon'?'#d97706':'#ef4444'};">
            ${esc(l.status||'')}
          </span>
        </td>
        <td style="padding:8px 10px;font-size:12px;">${l.ficheId ? `<a href="/fiche/${esc(l.ficheId)}">#${esc(l.ficheId)}</a>` : '—'}</td>
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${esc(l.message||'')}</td>
      </tr>
    `).join('');
    panel.innerHTML = `
      <div class="settings-section-card">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
          <h4 style="margin:0;">Journal des imports (50 derniers)</h4>
          <button id="import-log-refresh" class="btn btn-secondary" style="font-size:12px;">🔄 Actualiser</button>
        </div>
        <div style="overflow-x:auto;">
          <table style="width:100%;border-collapse:collapse;">
            <thead>
              <tr style="background:#f3f4f6;">
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Date</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Source</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Fichier</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Statut</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Fiche</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Message</th>
              </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
          </table>
        </div>
      </div>
    `;
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
      <tr>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.date||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.destination||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">${esc(l.format||'')}</td>
        <td style="padding:8px 10px;font-size:12px;">
          <span style="color:${l.status==='ok'?'#16a34a':'#ef4444'};">
            ${esc(l.status||'')}
          </span>
        </td>
        <td style="padding:8px 10px;font-size:12px;color:#6b7280;">${esc(l.message||'')}</td>
      </tr>
    `).join('');
    panel.innerHTML = `
      <div class="settings-section-card">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;">
          <h4 style="margin:0;">Journal des exports (50 derniers)</h4>
          <button id="export-log-refresh" class="btn btn-secondary" style="font-size:12px;">🔄 Actualiser</button>
        </div>
        <div style="overflow-x:auto;">
          <table style="width:100%;border-collapse:collapse;">
            <thead>
              <tr style="background:#f3f4f6;">
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Date</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Destination</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Format</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Statut</th>
                <th style="padding:8px 10px;font-size:12px;font-weight:700;color:#374151;text-align:left;">Message</th>
              </tr>
            </thead>
            <tbody>${rowsHtml}</tbody>
          </table>
        </div>
      </div>
    `;
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
  } catch(e) { }
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>📎 Soumission PDF + XML couplés</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Lorsque cette option est activée, l'onglet <strong>Soumission</strong> accepte de déposer simultanément
        un <strong>PDF</strong> et un <strong>XML de métadonnées</strong>. Les données XML pré-remplissent
        automatiquement la fiche en utilisant le mapping configuré dans l'onglet <em>Import XML</em>.
      </p>
      <div style="display:flex;flex-direction:column;gap:16px;max-width:600px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:6px;">Activé</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="sxml-enabled" ${cfg.enabled !== false ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Activer la soumission PDF + XML couplés</span>
          </label>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:6px;">Comportement</label>
          <select id="sxml-mode" class="settings-input" style="min-width:260px;">
            <option value="form"   ${cfg.mode==='form'   ? 'selected':''}>Formulaire pré-rempli (recommandé)</option>
            <option value="direct" ${cfg.mode==='direct' ? 'selected':''}>Création directe sans formulaire</option>
          </select>
        </div>
      </div>
      <button id="sxml-save" class="btn btn-primary" style="margin-top:20px;">💾 Enregistrer</button>
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
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
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
  } catch(e) { }

  const sources = cfg.erpSources || [];
  const sourcesHtml = sources.map((s, i) => `
    <div class="erp-src-card" style="border:1px solid #e5e7eb;border-radius:8px;padding:14px;margin-bottom:12px;">
      <input type="hidden" class="erp-src-id" value="${esc(s.id||String(i))}" />
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 16px;">
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Nom</label>
          <input type="text" class="erp-src-name settings-input" style="width:100%;" value="${esc(s.name||'')}" placeholder="Mon ERP" />
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">URL (utilisez {ref})</label>
          <input type="url" class="erp-src-url settings-input" style="width:100%;" value="${esc(s.url||'')}" placeholder="[erp.example.com](https://erp.example.com/api/orders/{ref})" />
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Auth</label>
          <select class="erp-src-authtype settings-input" style="width:100%;">
            <option value="none"   ${s.authType==='none'   ?'selected':''}>Aucune</option>
            <option value="basic"  ${s.authType==='basic'  ?'selected':''}>Basic Auth</option>
            <option value="bearer" ${s.authType==='bearer' ?'selected':''}>Bearer Token</option>
            <option value="apikey" ${s.authType==='apikey' ?'selected':''}>API Key (header)</option>
          </select>
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">User / Token / Key</label>
          <input type="text" class="erp-src-authuser settings-input" style="width:100%;" value="${esc(s.authUser||'')}" />
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Password / Secret</label>
          <input type="password" class="erp-src-authpwd settings-input" style="width:100%;" value="${esc(s.authPassword||'')}" autocomplete="new-password" />
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Nom du header (si API Key)</label>
          <input type="text" class="erp-src-authheader settings-input" style="width:100%;" value="${esc(s.authHeader||'')}" placeholder="X-API-Key" />
        </div>
        <div>
          <label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Format réponse</label>
          <select class="erp-src-format settings-input" style="width:100%;">
            <option value="json" ${s.responseFormat==='json'?'selected':''}>JSON</option>
            <option value="xml"  ${s.responseFormat==='xml' ?'selected':''}>XML</option>
          </select>
        </div>
      </div>
    </div>
  `).join('');

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
            <option value="mdsf"     ${cfg.defaultSource==='mdsf'    ?'selected':''}>MDSF</option>
            ${sources.map(s => `<option value="${esc(s.id)}" ${cfg.defaultSource===s.id?'selected':''}>${esc(s.name)}</option>`).join('')}
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Regex de détection (nom fichier)</label>
          <input type="text" id="serp-regex" class="settings-input" style="width:100%;" placeholder="CMD-(\d+)" value="${esc(cfg.refDetectionRegex||'')}" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Auto-lookup</label>
          <label style="display:flex;align-items:center;gap:8px;cursor:pointer;">
            <input type="checkbox" id="serp-auto" ${cfg.autoLookup ? 'checked' : ''} style="width:16px;height:16px;" />
            <span style="font-size:13px;">Lancer automatiquement au dépôt</span>
          </label>
        </div>
      </div>
      <button id="serp-save-global" class="btn btn-primary">💾 Enregistrer la config globale</button>
      <div id="serp-msg-global" style="margin-top:8px;font-size:13px;margin-bottom:24px;"></div>

      <h4>Sources ERP génériques</h4>
      <div id="serp-sources-list">${sourcesHtml}</div>
      <button id="serp-add-source" class="btn btn-secondary" style="margin-bottom:16px;">➕ Ajouter une source ERP</button>
      <button id="serp-save-sources" class="btn btn-primary">💾 Enregistrer les sources</button>
      <div id="serp-msg-sources" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  // Sauvegarder config globale
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
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };

  // Ajouter une source ERP
  panel.querySelector('#serp-add-source').onclick = () => {
    const list = panel.querySelector('#serp-sources-list');
    const idx = list.querySelectorAll('.erp-src-card').length;
    const div = document.createElement('div');
    div.className = 'erp-src-card';
    div.style.cssText = 'border:1px solid #e5e7eb;border-radius:8px;padding:14px;margin-bottom:12px;';
    div.innerHTML = `
      <input type="hidden" class="erp-src-id" value="erp-${Date.now()}" />
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 16px;">
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Nom</label>
          <input type="text" class="erp-src-name settings-input" style="width:100%;" placeholder="Mon ERP" /></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">URL (utilisez {ref})</label>
          <input type="url" class="erp-src-url settings-input" style="width:100%;" placeholder="[erp.example.com](https://erp.example.com/api/orders/{ref})" /></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Auth</label>
          <select class="erp-src-authtype settings-input" style="width:100%;">
            <option value="none">Aucune</option><option value="basic">Basic Auth</option>
            <option value="bearer">Bearer Token</option><option value="apikey">API Key (header)</option>
          </select></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">User / Token / Key</label>
          <input type="text" class="erp-src-authuser settings-input" style="width:100%;" /></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Password / Secret</label>
          <input type="password" class="erp-src-authpwd settings-input" style="width:100%;" autocomplete="new-password" /></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Nom du header (si API Key)</label>
          <input type="text" class="erp-src-authheader settings-input" style="width:100%;" placeholder="X-API-Key" /></div>
        <div><label style="font-size:11px;font-weight:600;color:#6b7280;display:block;margin-bottom:3px;">Format réponse</label>
          <select class="erp-src-format settings-input" style="width:100%;">
            <option value="json">JSON</option><option value="xml">XML</option>
          </select></div>
      </div>
    `;
    list.appendChild(div);
  };

  // Sauvegarder les sources ERP
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
      else       { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}
