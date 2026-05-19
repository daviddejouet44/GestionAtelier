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
    const systemKeys = new Set(SYSTEM_FIELDS.map(f => f.key));
    const configFields = r.fields.map(f => ({ key: f.id || '', label: f.label || f.id || '' }))
                                  .filter(f => f.key && !systemKeys.has(f.key));
    return [...SYSTEM_FIELDS, ...configFields];
  } catch {
    return FALLBACK_FIELDS;
  }
}

/**
 * Converts a field key to a safe CSS/HTML identifier
 */
function safeDomId(key) {
  return (key || '').replace(/[^A-Za-z0-9_-]/g, '_');
}

export async function renderSettingsIntegrations(panel) {
  panel.innerHTML = `
    <h3>Intégrations — Import &amp; Export</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Configurez les sources d'import automatique de la fiche de production (XML, ERP, Web-to-Print)
      et les destinations d'export des commandes.
    </p>

    <!-- ▶ Fournisseur actif — mutuellement exclusif ─────────────────────── -->
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
    if (cfg.erp?.enabled) activeProvider = 'erp';
    else if (cfg.pressero?.enabled) activeProvider = 'pressero';
    else if (cfg.mdsf?.enabled) activeProvider = 'mdsf';
  }

  const setProviderRadio = (val) => {
    panel.querySelectorAll('input[name="active-provider"]').forEach(r => {
      r.checked = r.value === val;
    });
  };
  setProviderRadio(activeProvider);

  panel.querySelectorAll('input[name="active-provider"]').forEach(radio => {
    radio.onchange = async () => {
      const prev = activeProvider;
      activeProvider = radio.value;
      const msgEl = panel.querySelector('#active-provider-msg');
      msgEl.style.color = '#6b7280'; msgEl.textContent = '⏳ Enregistrement…';
      try {
        const r = await fetch(API.config, {
          method: 'PUT', headers: authJsonH(),
          body: JSON.stringify({ section: 'activeProvider', data: { provider: activeProvider } })
        }).then(r => r.json());
        if (r.ok) {
          cfg.activeProvider = activeProvider;
          msgEl.style.color = '#16a34a';
          msgEl.textContent = activeProvider === 'none'
            ? '✅ Aucun fournisseur actif'
            : `✅ Fournisseur actif : ${activeProvider.toUpperCase()}`;
          const activeTab = panel.querySelector('.settings-tab.active[data-itab]');
          if (activeTab && activeTab.dataset.itab) showIntegTab(activeTab.dataset.itab);
        } else {
          msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur');
          activeProvider = prev;
          setProviderRadio(prev);
        }
      } catch {
        msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau';
        activeProvider = prev;
        setProviderRadio(prev);
      }
    };
  });

  const integPanel = panel.querySelector('#integ-panel');

  function showIntegTab(tabId) {
    panel.querySelectorAll('.settings-tab[data-itab]').forEach(t => {
      t.classList.toggle('active', t.dataset.itab === tabId);
    });
    switch(tabId) {
      case 'xml-import':    renderXmlImportTab(integPanel, cfg); break;
      case 'erp-import':    renderErpImportTab(integPanel, cfg, activeProvider); break;
      case 'pressero':      renderPresseroTab(integPanel, cfg, activeProvider); break;
      case 'mdsf':          renderMdsfTab(integPanel, cfg, activeProvider); break;
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
// ERP IMPORT (STUB)
// ======================================================
function renderErpImportTab(panel, cfg, activeProvider = 'none') {
  const erpCfg = cfg.erp || {};
  const isActive = activeProvider === 'erp';
  const inactiveNote = !isActive
    ? `<div style="background:#fef9c3;border:1px solid #fde68a;border-radius:6px;padding:8px 12px;margin-bottom:14px;font-size:13px;color:#92400e;">
        ⚠️ L'ERP n'est pas le fournisseur actif. Sélectionnez "<strong>ERP / Import auto</strong>" dans la section ci-dessus pour l'activer.
       </div>`
    : '';
  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Connexion ERP / Source externe</h4>
      ${inactiveNote}
      <p style="color:#6b7280;font-size:13px;margin-bottom:14px;">
        Configurez la connexion à votre ERP ou logiciel tiers pour importer automatiquement les commandes.
      </p>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px 20px;max-width:700px;">
        <div>
          <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">URL de l'endpoint</label>
          <input type="url" id="erp-url" placeholder="https://erp.example.com/api/orders" class="settings-input" style="width:100%;" value="${esc(erpCfg.url||'')}" />
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
}

function renderPresseroTab(panel, cfg, activeProvider = 'none') {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Configuration Pressero (à configurer)</p></div>';
}

function renderMdsfTab(panel, cfg, activeProvider = 'none') {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Configuration MDSF (à configurer)</p></div>';
}

async function renderExportTab(panel, cfg) {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Configuration Export (à configurer)</p></div>';
}

async function renderImportLogTab(panel) {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Journal des imports (à configurer)</p></div>';
}

async function renderExportLogTab(panel) {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Journal des exports (à configurer)</p></div>';
}

async function renderSubmissionXmlCouplingTab(panel) {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Soumission XML (à configurer)</p></div>';
}

async function renderSubmissionErpLookupTab(panel) {
  panel.innerHTML = '<div class="settings-section-card"><p style="color:#6b7280;">Soumission ERP (à configurer)</p></div>';
}
