// settings-order-sources.js — Paramétrages → Intégrations → Sources de commandes
import { authToken, showNotification, esc } from '../core.js';

const API = {
  sources:  '/api/integrations/order-sources',
  logs:     '/api/integrations/order-sources/logs',
  dropboxGlobalConfig:    '/api/integrations/dropbox/global-config',
  dropboxAuthorize:       '/api/integrations/dropbox/authorize',
  googleDriveGlobalConfig: '/api/integrations/google-drive/global-config',
  googleDriveAuthorize:   '/api/integrations/google-drive/authorize',
  boxGlobalConfig:        '/api/integrations/box/global-config',
  boxAuthorize:           '/api/integrations/box/authorize',
  oneDriveGlobalConfig:   '/api/integrations/onedrive/global-config',
  oneDriveAuthorize:      '/api/integrations/onedrive/authorize',
};

function authH() { return { 'Authorization': `Bearer ${authToken}` }; }
function authJsonH() { return { ...authH(), 'Content-Type': 'application/json' }; }

// ── Status badge helpers ─────────────────────────────────────────────────────
function statusBadge(s) {
  if (!s || s === 'never') return `<span style="background:#e5e7eb;color:#6b7280;padding:2px 8px;border-radius:10px;font-size:11px;">Jamais</span>`;
  if (s === 'ok')          return `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:10px;font-size:11px;">✅ OK</span>`;
  if (s === 'error')       return `<span style="background:#fee2e2;color:#991b1b;padding:2px 8px;border-radius:10px;font-size:11px;">❌ Erreur</span>`;
  return `<span style="background:#fef9c3;color:#854d0e;padding:2px 8px;border-radius:10px;font-size:11px;">${esc(s)}</span>`;
}

function importStatusBadge(s) {
  if (s === 'success')   return `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:10px;font-size:11px;">✅ Succès</span>`;
  if (s === 'error')     return `<span style="background:#fee2e2;color:#991b1b;padding:2px 8px;border-radius:10px;font-size:11px;">❌ Erreur</span>`;
  if (s === 'duplicate') return `<span style="background:#fef9c3;color:#854d0e;padding:2px 8px;border-radius:10px;font-size:11px;">⚠️ Doublon</span>`;
  return `<span style="background:#e5e7eb;color:#6b7280;padding:2px 8px;border-radius:10px;font-size:11px;">${esc(s)}</span>`;
}

function fmtDate(s) {
  if (!s) return '—';
  try { return new Date(s).toLocaleString('fr-FR'); } catch { return s; }
}

function nextPollStr(lastPollAt, intervalMin) {
  if (!lastPollAt) return 'Immédiat';
  try {
    const next = new Date(new Date(lastPollAt).getTime() + intervalMin * 60000);
    const diff = Math.round((next - Date.now()) / 60000);
    if (diff <= 0) return 'Immédiat';
    return `Dans ${diff} min`;
  } catch { return '—'; }
}

// ── Main render ──────────────────────────────────────────────────────────────
export async function renderSettingsOrderSources(panel) {
  panel.innerHTML = `
    <h3>Sources de commandes automatiques</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:20px;">
      Configurez des sources distantes (SFTP, Dropbox) pour récupérer automatiquement des commandes PDF
      et pré-remplir les fiches de production.
    </p>
    <div class="settings-tabs" id="os-tabs" style="margin-bottom:20px;">
      <button class="settings-tab active" data-ostab="sources">🔗 Sources configurées</button>
      <button class="settings-tab" data-ostab="dropbox-global">☁️ Config Dropbox globale</button>
      <button class="settings-tab" data-ostab="googledrive-global">☁️ Config Google Drive globale</button>
      <button class="settings-tab" data-ostab="box-global">☁️ Config Box globale</button>
      <button class="settings-tab" data-ostab="onedrive-global">☁️ Config OneDrive globale</button>
      <button class="settings-tab" data-ostab="imports-log">📋 Journal des imports</button>
    </div>
    <div id="os-panel"></div>
  `;

  const osPanel = panel.querySelector('#os-panel');

  function showOsTab(tabId) {
    panel.querySelectorAll('.settings-tab[data-ostab]').forEach(t => {
      t.classList.toggle('active', t.dataset.ostab === tabId);
    });
    switch (tabId) {
      case 'sources':          renderSourcesList(osPanel); break;
      case 'dropbox-global':   renderDropboxGlobalConfig(osPanel); break;
      case 'googledrive-global': renderGoogleDriveGlobalConfig(osPanel); break;
      case 'box-global':       renderBoxGlobalConfig(osPanel); break;
      case 'onedrive-global':  renderOneDriveGlobalConfig(osPanel); break;
      case 'imports-log':      renderImportsLog(osPanel); break;
    }
  }

  panel.querySelectorAll('.settings-tab[data-ostab]').forEach(btn => {
    btn.onclick = () => showOsTab(btn.dataset.ostab);
  });

  // Handle OAuth callback hash parameters
  const hash = window.location.hash;
  if (hash.includes('dropbox_ok=1')) {
    showNotification('✅ Dropbox connecté avec succès !', 'success');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('dropbox_error=')) {
    const errMatch = hash.match(/dropbox_error=([^&]+)/);
    const errMsg = errMatch ? decodeURIComponent(errMatch[1]) : 'Erreur inconnue';
    showNotification('❌ Erreur Dropbox : ' + errMsg, 'error');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('googledrive_ok=1')) {
    showNotification('✅ Google Drive connecté avec succès !', 'success');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('googledrive_error=')) {
    const errMatch = hash.match(/googledrive_error=([^&]+)/);
    const errMsg = errMatch ? decodeURIComponent(errMatch[1]) : 'Erreur inconnue';
    showNotification('❌ Erreur Google Drive : ' + errMsg, 'error');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('box_ok=1')) {
    showNotification('✅ Box connecté avec succès !', 'success');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('box_error=')) {
    const errMatch = hash.match(/box_error=([^&]+)/);
    const errMsg = errMatch ? decodeURIComponent(errMatch[1]) : 'Erreur inconnue';
    showNotification('❌ Erreur Box : ' + errMsg, 'error');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('onedrive_ok=1')) {
    showNotification('✅ OneDrive connecté avec succès !', 'success');
    window.location.hash = '#settings/integrations';
  } else if (hash.includes('onedrive_error=')) {
    const errMatch = hash.match(/onedrive_error=([^&]+)/);
    const errMsg = errMatch ? decodeURIComponent(errMatch[1]) : 'Erreur inconnue';
    showNotification('❌ Erreur OneDrive : ' + errMsg, 'error');
    window.location.hash = '#settings/integrations';
  }

  showOsTab('sources');
}

// ── Sources list ─────────────────────────────────────────────────────────────
async function renderSourcesList(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let sources = [];
  try {
    const r = await fetch(API.sources, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok) sources = r.sources || [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <div style="display:flex;justify-content:flex-end;margin-bottom:16px;">
      <button id="os-add-btn" class="btn btn-primary">+ Ajouter une source</button>
    </div>
    <div id="os-sources-list">
      ${sources.length === 0 ? `
        <div style="text-align:center;padding:40px;color:#9ca3af;">
          <div style="font-size:48px;margin-bottom:12px;">📡</div>
          <div style="font-size:16px;font-weight:600;margin-bottom:6px;">Aucune source configurée</div>
          <div style="font-size:13px;">Cliquez sur "Ajouter une source" pour commencer.</div>
        </div>
      ` : sources.map(s => renderSourceCard(s)).join('')}
    </div>
    <div id="os-modal-container"></div>
  `;

  panel.querySelector('#os-add-btn').onclick = () => openSourceModal(panel, null, () => renderSourcesList(panel));

  // Bind action buttons
  panel.querySelectorAll('[data-os-action]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const action = btn.dataset.osAction;
      const id = btn.dataset.osId;
      await handleSourceAction(action, id, panel);
    });
  });
}

function renderSourceCard(s) {
  const typeLabels = {
    sftp: '🖥️ SFTP',
    dropbox: '☁️ Dropbox',
    googledrive: '☁️ Google Drive',
    box: '☁️ Box',
    onedrive: '☁️ OneDrive / Office 365',
  };
  const typeLabel = typeLabels[s.type] || esc(s.type);
  const mapping = s.clientMapping || {};
  const mappingStr = Object.entries(mapping).map(([k, v]) => `${esc(k)} → ${esc(v)}`).join(', ') || '—';
  return `
    <div class="settings-section-card" style="margin-bottom:12px;">
      <div style="display:flex;justify-content:space-between;align-items:flex-start;flex-wrap:wrap;gap:8px;">
        <div>
          <div style="display:flex;align-items:center;gap:10px;margin-bottom:6px;">
            <span style="font-size:15px;font-weight:700;color:#1e293b;">${esc(s.name)}</span>
            <span style="background:#dbeafe;color:#1e40af;padding:2px 8px;border-radius:10px;font-size:11px;">${typeLabel}</span>
            ${s.enabled
              ? `<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:10px;font-size:11px;">Actif</span>`
              : `<span style="background:#fee2e2;color:#991b1b;padding:2px 8px;border-radius:10px;font-size:11px;">Inactif</span>`
            }
          </div>
          <div style="font-size:12px;color:#6b7280;display:flex;gap:20px;flex-wrap:wrap;">
            <span>⏱ Polling : ${s.pollingIntervalMinutes} min</span>
            <span>📦 Quantité par défaut : ${s.defaultQuantity}</span>
            <span>📐 Format par défaut : ${esc(s.defaultFormat || '—')}</span>
            <span>💾 Taille max : ${s.maxFileSizeMb} Mo</span>
          </div>
          <div style="font-size:12px;color:#6b7280;margin-top:4px;">
            Mapping client : <span style="font-family:monospace;">${mappingStr}</span>
          </div>
          <div style="font-size:12px;color:#6b7280;margin-top:4px;display:flex;gap:16px;">
            <span>Dernier polling : ${fmtDate(s.lastPollAt)} ${statusBadge(s.lastPollStatus)}</span>
            <span>Prochain : ${nextPollStr(s.lastPollAt, s.pollingIntervalMinutes)}</span>
          </div>
        </div>
        <div style="display:flex;gap:6px;flex-wrap:wrap;">
          <button class="btn btn-sm" data-os-action="test" data-os-id="${esc(s.id)}">🔌 Tester</button>
          <button class="btn btn-sm btn-primary" data-os-action="run" data-os-id="${esc(s.id)}">▶ Lancer</button>
          <button class="btn btn-sm" data-os-action="edit" data-os-id="${esc(s.id)}">✏️ Modifier</button>
          <button class="btn btn-sm" data-os-action="toggle" data-os-id="${esc(s.id)}" data-os-enabled="${s.enabled}">
            ${s.enabled ? '⏸ Désactiver' : '▶ Activer'}
          </button>
          <button class="btn btn-sm btn-danger" data-os-action="delete" data-os-id="${esc(s.id)}">🗑 Supprimer</button>
        </div>
      </div>
    </div>
  `;
}

async function handleSourceAction(action, id, panel) {
  switch (action) {
    case 'test': {
      showNotification('⏳ Test de connexion en cours…', 'info');
      try {
        const r = await fetch(`${API.sources}/${id}/test`, { method: 'POST', headers: authH() }).then(r => r.json());
        if (r.ok) showNotification('✅ ' + (r.message || 'Connexion réussie'), 'success');
        else showNotification('❌ ' + (r.error || 'Erreur de connexion'), 'error');
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
      break;
    }
    case 'run': {
      showNotification('⏳ Cycle de polling lancé…', 'info');
      try {
        const r = await fetch(`${API.sources}/${id}/run`, { method: 'POST', headers: authH() }).then(r => r.json());
        if (r.ok) showNotification('✅ ' + (r.message || 'Cycle lancé'), 'success');
        else showNotification('❌ ' + (r.error || 'Erreur'), 'error');
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
      break;
    }
    case 'edit': {
      const r = await fetch(API.sources, { headers: authH() }).then(r => r.json()).catch(() => ({}));
      const source = (r.sources || []).find(s => s.id === id);
      if (source) openSourceModal(panel, source, () => renderSourcesList(panel));
      break;
    }
    case 'toggle': {
      const btn = panel.querySelector(`[data-os-action="toggle"][data-os-id="${id}"]`);
      const enabled = btn?.dataset.osEnabled !== 'true';
      try {
        const r = await fetch(`${API.sources}/${id}`, {
          method: 'PUT', headers: authJsonH(),
          body: JSON.stringify({ enabled })
        }).then(r => r.json());
        if (r.ok) { renderSourcesList(panel); showNotification(enabled ? '✅ Source activée' : '⏸ Source désactivée', 'success'); }
        else showNotification('❌ ' + r.error, 'error');
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
      break;
    }
    case 'delete': {
      if (!confirm('Supprimer cette source ?')) break;
      try {
        const r = await fetch(`${API.sources}/${id}`, { method: 'DELETE', headers: authH() }).then(r => r.json());
        if (r.ok) { renderSourcesList(panel); showNotification('🗑 Source supprimée', 'success'); }
        else showNotification('❌ ' + r.error, 'error');
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
      break;
    }
  }
}

// ── Source modal (Create / Edit) ─────────────────────────────────────────────
function openSourceModal(parentPanel, source, onSaved) {
  const isEdit = !!source;
  const s = source || { type: 'sftp', pollingIntervalMinutes: 5, defaultQuantity: 1, maxFileSizeMb: 200, enabled: true };

  const overlay = document.createElement('div');
  overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;z-index:10000;';

  const modal = document.createElement('div');
  modal.style.cssText = 'background:white;border-radius:12px;padding:28px;min-width:500px;max-width:700px;width:94%;box-shadow:0 12px 50px rgba(0,0,0,.3);max-height:90vh;overflow-y:auto;';

  function buildConfigFields(type) {
    if (type === 'sftp') {
      const cfg = {};
      return `
        <div class="settings-section-card" style="margin-top:16px;background:#f8fafc;">
          <h4 style="margin:0 0 12px;font-size:13px;color:#374151;">Configuration SFTP</h4>
          <div style="display:grid;grid-template-columns:1fr 80px;gap:10px;margin-bottom:10px;">
            <div><label class="settings-form-group" style="font-size:12px;">Serveur (host)</label>
              <input type="text" id="sftp-host" class="settings-input settings-input-wide" placeholder="sftp.exemple.com" value="${esc(cfg.host||'')}" /></div>
            <div><label class="settings-form-group" style="font-size:12px;">Port</label>
              <input type="number" id="sftp-port" class="settings-input" value="${cfg.port||22}" style="width:70px;" /></div>
          </div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Utilisateur</label>
            <input type="text" id="sftp-username" class="settings-input settings-input-wide" placeholder="utilisateur" value="${esc(cfg.username||'')}" /></div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Mot de passe (laisser vide pour clé privée)</label>
            <input type="password" id="sftp-password" class="settings-input settings-input-wide" placeholder="••••••••" /></div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Clé privée PEM (optionnel)</label>
            <textarea id="sftp-privatekey" class="settings-input settings-input-wide" rows="3" placeholder="-----BEGIN RSA PRIVATE KEY-----&#10;...&#10;-----END RSA PRIVATE KEY-----" style="font-family:monospace;font-size:11px;"></textarea></div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Passphrase clé privée (optionnel)</label>
            <input type="password" id="sftp-passphrase" class="settings-input settings-input-wide" placeholder="••••••••" /></div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Répertoire de base</label>
            <input type="text" id="sftp-basedir" class="settings-input settings-input-wide" placeholder="/flux/clients" value="${esc(cfg.baseDir||'/')}" /></div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Empreinte hôte (optionnel, vérification)</label>
            <input type="text" id="sftp-fingerprint" class="settings-input settings-input-wide" placeholder="xx:xx:xx:..." value="${esc(cfg.hostFingerprint||'')}" /></div>
        </div>
      `;
    } else if (type === 'dropbox') {
      return `
        <div class="settings-section-card" style="margin-top:16px;background:#f8fafc;">
          <h4 style="margin:0 0 12px;font-size:13px;color:#374151;">Configuration Dropbox</h4>
          <p style="color:#6b7280;font-size:12px;margin-bottom:12px;">
            Les identifiants App Key / App Secret se configurent dans l'onglet <strong>Config Dropbox globale</strong>.
            Cliquez sur "Connecter à Dropbox" après avoir créé/enregistré la source pour démarrer le flux OAuth2.
          </p>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Dossier racine Dropbox</label>
            <input type="text" id="dropbox-folder" class="settings-input settings-input-wide" placeholder="/GestionAtelier" value="${esc('/GestionAtelier')}" /></div>
          <div id="dropbox-oauth-status" style="font-size:12px;color:#6b7280;margin-top:8px;">
            ${isEdit && s.id ? `<span id="dropbox-connect-info">Statut OAuth : vérifiez via "Tester la connexion"</span>` : '<span>Enregistrez la source pour démarrer le flux OAuth2.</span>'}
          </div>
        </div>
      `;
    } else if (type === 'googledrive') {
      return `
        <div class="settings-section-card" style="margin-top:16px;background:#f8fafc;">
          <h4 style="margin:0 0 12px;font-size:13px;color:#374151;">Configuration Google Drive</h4>
          <p style="color:#6b7280;font-size:12px;margin-bottom:12px;">
            Les identifiants OAuth se configurent dans l'onglet <strong>Config Google Drive globale</strong>.
            Cliquez sur "Connecter Google Drive" après avoir enregistré la source.
          </p>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">ID du dossier racine Google Drive</label>
            <input type="text" id="googledrive-folderid" class="settings-input settings-input-wide" placeholder="root" value="${esc('root')}" />
            <span style="font-size:11px;color:#6b7280;">Utilisez "root" pour Mon Drive, ou l'ID d'un dossier spécifique (visible dans l'URL Drive).</span>
          </div>
          <div style="font-size:12px;color:#6b7280;margin-top:8px;">
            ${isEdit && s.id ? 'Statut OAuth : vérifiez via "Tester la connexion"' : 'Enregistrez la source pour démarrer le flux OAuth2.'}
          </div>
        </div>
      `;
    } else if (type === 'box') {
      return `
        <div class="settings-section-card" style="margin-top:16px;background:#f8fafc;">
          <h4 style="margin:0 0 12px;font-size:13px;color:#374151;">Configuration Box</h4>
          <p style="color:#6b7280;font-size:12px;margin-bottom:12px;">
            Les identifiants OAuth se configurent dans l'onglet <strong>Config Box globale</strong>.
            Cliquez sur "Connecter Box" après avoir enregistré la source.
          </p>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">ID du dossier racine Box</label>
            <input type="text" id="box-folderid" class="settings-input settings-input-wide" placeholder="0" value="${esc('0')}" />
            <span style="font-size:11px;color:#6b7280;">Utilisez "0" pour le dossier racine, ou l'ID d'un sous-dossier.</span>
          </div>
          <div style="font-size:12px;color:#6b7280;margin-top:8px;">
            ${isEdit && s.id ? 'Statut OAuth : vérifiez via "Tester la connexion"' : 'Enregistrez la source pour démarrer le flux OAuth2.'}
          </div>
        </div>
      `;
    } else if (type === 'onedrive') {
      return `
        <div class="settings-section-card" style="margin-top:16px;background:#f8fafc;">
          <h4 style="margin:0 0 12px;font-size:13px;color:#374151;">Configuration OneDrive / Office 365</h4>
          <p style="color:#6b7280;font-size:12px;margin-bottom:12px;">
            Les identifiants OAuth se configurent dans l'onglet <strong>Config OneDrive globale</strong>.
            Cliquez sur "Connecter OneDrive" après avoir enregistré la source.
          </p>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">Type de drive</label>
            <select id="onedrive-drivetype" class="settings-input">
              <option value="personal">OneDrive Personnel</option>
              <option value="business">OneDrive Entreprise (Business)</option>
              <option value="sharepoint">SharePoint</option>
            </select>
          </div>
          <div id="onedrive-siteid-row" style="margin-bottom:10px;display:none;">
            <label class="settings-form-group" style="font-size:12px;">Site ID SharePoint</label>
            <input type="text" id="onedrive-siteid" class="settings-input settings-input-wide" placeholder="contoso.sharepoint.com,…" value="" />
          </div>
          <div id="onedrive-driveid-row" style="margin-bottom:10px;display:none;">
            <label class="settings-form-group" style="font-size:12px;">Drive ID (optionnel)</label>
            <input type="text" id="onedrive-driveid" class="settings-input settings-input-wide" placeholder="b!…" value="" />
          </div>
          <div style="margin-bottom:10px;"><label class="settings-form-group" style="font-size:12px;">ID de l'élément dossier racine</label>
            <input type="text" id="onedrive-folderitemid" class="settings-input settings-input-wide" placeholder="root" value="${esc('root')}" />
            <span style="font-size:11px;color:#6b7280;">Utilisez "root" pour la racine, ou l'ID d'un dossier spécifique.</span>
          </div>
          <div style="font-size:12px;color:#6b7280;margin-top:8px;">
            ${isEdit && s.id ? 'Statut OAuth : vérifiez via "Tester la connexion"' : 'Enregistrez la source pour démarrer le flux OAuth2.'}
          </div>
        </div>
      `;
    }
    return '';
  }

  modal.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:20px;">
      <h3 style="margin:0;font-size:18px;">${isEdit ? '✏️ Modifier la source' : '+ Nouvelle source de commandes'}</h3>
      <button id="os-modal-close" style="background:none;border:none;font-size:20px;cursor:pointer;color:#6b7280;">✕</button>
    </div>

    <div class="settings-form-group" style="margin-bottom:14px;">
      <label style="font-size:13px;font-weight:600;display:block;margin-bottom:4px;">Nom de la source <span style="color:red;">*</span></label>
      <input type="text" id="os-name" class="settings-input settings-input-wide" placeholder="Ex: SFTP Client Dupont" value="${esc(s.name||'')}" />
    </div>

    <div class="settings-form-group" style="margin-bottom:14px;">
      <label style="font-size:13px;font-weight:600;display:block;margin-bottom:4px;">Type</label>
      <select id="os-type" class="settings-input" style="min-width:200px;">
        <option value="sftp" ${s.type==='sftp'?'selected':''}>🖥️ SFTP</option>
        <option value="dropbox" ${s.type==='dropbox'?'selected':''}>☁️ Dropbox</option>
        <option value="googledrive" ${s.type==='googledrive'?'selected':''}>☁️ Google Drive</option>
        <option value="box" ${s.type==='box'?'selected':''}>☁️ Box</option>
        <option value="onedrive" ${s.type==='onedrive'?'selected':''}>☁️ OneDrive / Office 365</option>
      </select>
    </div>

    <div id="os-config-fields">${buildConfigFields(s.type || 'sftp')}</div>

    <div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px;margin-top:16px;">
      <div class="settings-form-group">
        <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Intervalle (min)</label>
        <input type="number" id="os-interval" class="settings-input" value="${s.pollingIntervalMinutes||5}" min="1" style="width:80px;" />
      </div>
      <div class="settings-form-group">
        <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Quantité par défaut</label>
        <input type="number" id="os-qty" class="settings-input" value="${s.defaultQuantity||1}" min="1" style="width:80px;" />
      </div>
      <div class="settings-form-group">
        <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Taille max (Mo)</label>
        <input type="number" id="os-maxmb" class="settings-input" value="${s.maxFileSizeMb||200}" min="1" style="width:80px;" />
      </div>
    </div>

    <div class="settings-form-group" style="margin-top:12px;">
      <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Format par défaut</label>
      <input type="text" id="os-format" class="settings-input" value="${esc(s.defaultFormat||'')}" placeholder="Ex: A4" style="width:150px;" />
    </div>

    <div class="settings-form-group" style="margin-top:16px;">
      <label style="font-size:13px;font-weight:600;display:block;margin-bottom:8px;">Mapping dossiers → clients</label>
      <p style="font-size:12px;color:#6b7280;margin-bottom:8px;">
        Convention : <code>/clients/{dossier}/in/</code>. Associez chaque nom de dossier à un client.
      </p>
      <div id="os-mapping-rows"></div>
      <button id="os-add-mapping" class="btn btn-sm" style="margin-top:8px;">+ Ajouter un mapping</button>
    </div>

    <div style="display:flex;align-items:center;gap:8px;margin-top:16px;">
      <input type="checkbox" id="os-enabled" ${s.enabled!==false?'checked':''} />
      <label for="os-enabled" style="font-size:13px;">Source active</label>
    </div>

    <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:24px;padding-top:16px;border-top:1px solid #e5e7eb;">
      <button id="os-modal-cancel" class="btn">Annuler</button>
      ${isEdit ? `
        <button id="os-modal-dropbox-connect" class="btn" style="display:${s.type==='dropbox'?'inline-flex':'none'}">🔗 Connecter Dropbox</button>
        <button id="os-modal-googledrive-connect" class="btn" style="display:${s.type==='googledrive'?'inline-flex':'none'}">🔗 Connecter Google Drive</button>
        <button id="os-modal-box-connect" class="btn" style="display:${s.type==='box'?'inline-flex':'none'}">🔗 Connecter Box</button>
        <button id="os-modal-onedrive-connect" class="btn" style="display:${s.type==='onedrive'?'inline-flex':'none'}">🔗 Connecter OneDrive</button>
      ` : ''}
      <button id="os-modal-save" class="btn btn-primary">💾 Enregistrer</button>
    </div>
  `;

  overlay.appendChild(modal);
  document.body.appendChild(overlay);

  // Handle type change
  const typeSelect = modal.querySelector('#os-type');
  typeSelect.addEventListener('change', () => {
    const t = typeSelect.value;
    modal.querySelector('#os-config-fields').innerHTML = buildConfigFields(t);
    if (isEdit) {
      const oauthTypes = ['dropbox', 'googledrive', 'box', 'onedrive'];
      oauthTypes.forEach(ot => {
        const btn = modal.querySelector(`#os-modal-${ot}-connect`);
        if (btn) btn.style.display = t === ot ? 'inline-flex' : 'none';
      });
    }
    // Wire up OneDrive driveType toggle if applicable
    if (t === 'onedrive') wireOneDriveDriveTypeToggle(modal);
  });

  // Mapping rows
  const mappingContainer = modal.querySelector('#os-mapping-rows');
  const initialMapping = s.clientMapping || {};
  function addMappingRow(folder = '', clientId = '') {
    const row = document.createElement('div');
    row.style.cssText = 'display:flex;gap:8px;align-items:center;margin-bottom:6px;';
    row.innerHTML = `
      <input type="text" class="os-map-folder settings-input" placeholder="nom_dossier" value="${esc(folder)}" style="width:150px;font-family:monospace;font-size:12px;" />
      <span style="color:#6b7280;">→</span>
      <input type="text" class="os-map-client settings-input" placeholder="clientId" value="${esc(clientId)}" style="width:150px;" />
      <button class="btn btn-sm btn-danger" onclick="this.closest('div').remove()">✕</button>
    `;
    mappingContainer.appendChild(row);
  }
  Object.entries(initialMapping).forEach(([f, c]) => addMappingRow(f, c));
  modal.querySelector('#os-add-mapping').onclick = () => addMappingRow();

  function buildClientMapping() {
    const m = {};
    mappingContainer.querySelectorAll('div').forEach(row => {
      const f = row.querySelector('.os-map-folder')?.value.trim();
      const c = row.querySelector('.os-map-client')?.value.trim();
      if (f && c) m[f] = c;
    });
    return m;
  }

  function buildConfig(type) {
    if (type === 'sftp') {
      return {
        host: modal.querySelector('#sftp-host')?.value.trim() || '',
        port: parseInt(modal.querySelector('#sftp-port')?.value || '22'),
        username: modal.querySelector('#sftp-username')?.value.trim() || '',
        password: modal.querySelector('#sftp-password')?.value || '',
        privateKey: modal.querySelector('#sftp-privatekey')?.value.trim() || '',
        privateKeyPassphrase: modal.querySelector('#sftp-passphrase')?.value || '',
        baseDir: modal.querySelector('#sftp-basedir')?.value.trim() || '/',
        hostFingerprint: modal.querySelector('#sftp-fingerprint')?.value.trim() || '',
      };
    } else if (type === 'dropbox') {
      return {
        folderPath: modal.querySelector('#dropbox-folder')?.value.trim() || '/GestionAtelier',
      };
    } else if (type === 'googledrive') {
      return {
        folderId: modal.querySelector('#googledrive-folderid')?.value.trim() || 'root',
      };
    } else if (type === 'box') {
      return {
        folderId: modal.querySelector('#box-folderid')?.value.trim() || '0',
      };
    } else if (type === 'onedrive') {
      return {
        driveType: modal.querySelector('#onedrive-drivetype')?.value || 'personal',
        siteId: modal.querySelector('#onedrive-siteid')?.value.trim() || '',
        driveId: modal.querySelector('#onedrive-driveid')?.value.trim() || '',
        folderItemId: modal.querySelector('#onedrive-folderitemid')?.value.trim() || 'root',
      };
    }
    return {};
  }

  // Save
  modal.querySelector('#os-modal-save').onclick = async () => {
    const type = modal.querySelector('#os-type').value;
    const name = modal.querySelector('#os-name').value.trim();
    if (!name) { showNotification('❌ Le nom est obligatoire', 'error'); return; }

    const body = {
      id: isEdit ? s.id : undefined,
      name,
      type,
      pollingIntervalMinutes: Math.max(1, parseInt(modal.querySelector('#os-interval').value) || 5),
      defaultQuantity: parseInt(modal.querySelector('#os-qty').value) || 1,
      defaultFormat: modal.querySelector('#os-format').value.trim(),
      maxFileSizeMb: parseInt(modal.querySelector('#os-maxmb').value) || 200,
      enabled: modal.querySelector('#os-enabled').checked,
      clientMapping: buildClientMapping(),
      config: buildConfig(type),
    };

    try {
      const url = isEdit ? `${API.sources}/${s.id}` : API.sources;
      const method = isEdit ? 'PUT' : 'POST';
      const r = await fetch(url, { method, headers: authJsonH(), body: JSON.stringify(body) }).then(r => r.json());
      if (r.ok) {
        showNotification(`✅ Source ${isEdit ? 'mise à jour' : 'créée'} !`, 'success');
        overlay.remove();
        onSaved && onSaved(r.id || s.id);
      } else {
        showNotification('❌ ' + (r.error || 'Erreur'), 'error');
      }
    } catch(e) {
      showNotification('❌ Erreur réseau', 'error');
    }
  };

  // Dropbox OAuth
  const dropboxConnectBtn = modal.querySelector('#os-modal-dropbox-connect');
  if (dropboxConnectBtn) {
    dropboxConnectBtn.onclick = async () => {
      if (!s.id) { showNotification('⚠️ Enregistrez d\'abord la source', 'warning'); return; }
      try {
        const r = await fetch(`${API.dropboxAuthorize}?sourceId=${encodeURIComponent(s.id)}`, { headers: authH() }).then(r => r.json());
        if (r.ok && r.url) {
          window.open(r.url, '_blank', 'width=600,height=700,noopener,noreferrer');
        } else {
          showNotification('❌ ' + (r.error || 'Impossible de générer l\'URL OAuth'), 'error');
        }
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
    };
  }

  // Google Drive OAuth
  const googleDriveConnectBtn = modal.querySelector('#os-modal-googledrive-connect');
  if (googleDriveConnectBtn) {
    googleDriveConnectBtn.onclick = async () => {
      if (!s.id) { showNotification('⚠️ Enregistrez d\'abord la source', 'warning'); return; }
      try {
        const r = await fetch(`${API.googleDriveAuthorize}?sourceId=${encodeURIComponent(s.id)}`, { headers: authH() }).then(r => r.json());
        if (r.ok && r.url) {
          window.open(r.url, '_blank', 'width=600,height=700,noopener,noreferrer');
        } else {
          showNotification('❌ ' + (r.error || 'Impossible de générer l\'URL OAuth'), 'error');
        }
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
    };
  }

  // Box OAuth
  const boxConnectBtn = modal.querySelector('#os-modal-box-connect');
  if (boxConnectBtn) {
    boxConnectBtn.onclick = async () => {
      if (!s.id) { showNotification('⚠️ Enregistrez d\'abord la source', 'warning'); return; }
      try {
        const r = await fetch(`${API.boxAuthorize}?sourceId=${encodeURIComponent(s.id)}`, { headers: authH() }).then(r => r.json());
        if (r.ok && r.url) {
          window.open(r.url, '_blank', 'width=600,height=700,noopener,noreferrer');
        } else {
          showNotification('❌ ' + (r.error || 'Impossible de générer l\'URL OAuth'), 'error');
        }
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
    };
  }

  // OneDrive OAuth
  const oneDriveConnectBtn = modal.querySelector('#os-modal-onedrive-connect');
  if (oneDriveConnectBtn) {
    oneDriveConnectBtn.onclick = async () => {
      if (!s.id) { showNotification('⚠️ Enregistrez d\'abord la source', 'warning'); return; }
      const driveType = modal.querySelector('#onedrive-drivetype')?.value || 'personal';
      try {
        const r = await fetch(`${API.oneDriveAuthorize}?sourceId=${encodeURIComponent(s.id)}&driveType=${encodeURIComponent(driveType)}`, { headers: authH() }).then(r => r.json());
        if (r.ok && r.url) {
          window.open(r.url, '_blank', 'width=600,height=700,noopener,noreferrer');
        } else {
          showNotification('❌ ' + (r.error || 'Impossible de générer l\'URL OAuth'), 'error');
        }
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
    };
  }

  // Wire up OneDrive driveType toggle for initial render
  if (s.type === 'onedrive') wireOneDriveDriveTypeToggle(modal);

  modal.querySelector('#os-modal-close').onclick = () => overlay.remove();
  modal.querySelector('#os-modal-cancel').onclick = () => overlay.remove();
  overlay.addEventListener('click', e => { if (e.target === overlay) overlay.remove(); });
}

// ── OneDrive driveType toggle ────────────────────────────────────────────────
function wireOneDriveDriveTypeToggle(modal) {
  const driveTypeSelect = modal.querySelector('#onedrive-drivetype');
  if (!driveTypeSelect) return;
  function updateVisibility() {
    const v = driveTypeSelect.value;
    const siteRow = modal.querySelector('#onedrive-siteid-row');
    const driveRow = modal.querySelector('#onedrive-driveid-row');
    if (siteRow) siteRow.style.display = v === 'sharepoint' ? 'block' : 'none';
    if (driveRow) driveRow.style.display = (v === 'business' || v === 'sharepoint') ? 'block' : 'none';
  }
  driveTypeSelect.addEventListener('change', updateVisibility);
  updateVisibility();
}

// ── Dropbox global config ────────────────────────────────────────────────────
async function renderDropboxGlobalConfig(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let cfg = {};
  try {
    const r = await fetch(API.dropboxGlobalConfig, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok) cfg = r;
  } catch(e) {}

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Configuration globale Dropbox OAuth2</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Créez une application sur
        <a href="https://www.dropbox.com/developers/apps" target="_blank" rel="noopener">developers.dropbox.com</a>
        puis renseignez ici l'App Key et l'App Secret. L'URL de callback à enregistrer dans Dropbox est :
        <code id="dropbox-callback-url">${esc(cfg.callbackUrl || window.location.origin + '/api/integrations/dropbox/callback')}</code>
      </p>
      <div style="display:grid;gap:12px;max-width:500px;">
        <div class="settings-form-group">
          <label>App Key (Client ID)</label>
          <input type="text" id="dbx-appkey" class="settings-input settings-input-wide" value="${esc(cfg.appKey||'')}" placeholder="xxxxxxxxxxxx" />
        </div>
        <div class="settings-form-group">
          <label>App Secret (Client Secret)</label>
          <input type="password" id="dbx-appsecret" class="settings-input settings-input-wide" placeholder="${cfg.hasAppSecret ? '(déjà configuré — laisser vide pour conserver)' : 'xxxxxxxxxxxx'}" />
          ${cfg.hasAppSecret ? '<span style="font-size:11px;color:#16a34a;">✅ App Secret enregistré</span>' : ''}
        </div>
        <div class="settings-form-group">
          <label>URL de callback (doit correspondre à Dropbox)</label>
          <input type="text" id="dbx-callback" class="settings-input settings-input-wide" value="${esc(cfg.callbackUrl||window.location.origin+'/api/integrations/dropbox/callback')}" />
        </div>
      </div>
      <button id="dbx-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="dbx-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#dbx-save').onclick = async () => {
    const appKey = panel.querySelector('#dbx-appkey').value.trim();
    const appSecret = panel.querySelector('#dbx-appsecret').value;
    const callbackUrl = panel.querySelector('#dbx-callback').value.trim();
    const body = { appKey, callbackUrl };
    if (appSecret) body.appSecret = appSecret;
    try {
      const r = await fetch(API.dropboxGlobalConfig, {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) showNotification('✅ Configuration Dropbox enregistrée', 'success');
      else showNotification('❌ ' + (r.error || 'Erreur'), 'error');
    } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
  };
}

// ── Google Drive global config ───────────────────────────────────────────────
async function renderGoogleDriveGlobalConfig(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let cfg = {};
  try {
    const r = await fetch(API.googleDriveGlobalConfig, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok) cfg = r;
  } catch(e) {}

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Configuration globale Google Drive OAuth2</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Créez une application sur
        <a href="https://console.cloud.google.com/apis/credentials" target="_blank" rel="noopener">Google Cloud Console</a>
        (type OAuth 2.0), activez l'API Google Drive, puis renseignez ici le Client ID et le Client Secret.
        L'URL de callback à autoriser dans la Console Google est :
        <code>${esc(cfg.callbackUrl || window.location.origin + '/api/integrations/google-drive/callback')}</code>
      </p>
      <div style="display:grid;gap:12px;max-width:500px;">
        <div class="settings-form-group">
          <label>Client ID</label>
          <input type="text" id="gd-clientid" class="settings-input settings-input-wide" value="${esc(cfg.appClientId||'')}" placeholder="xxx.apps.googleusercontent.com" />
        </div>
        <div class="settings-form-group">
          <label>Client Secret</label>
          <input type="password" id="gd-clientsecret" class="settings-input settings-input-wide" placeholder="${cfg.hasAppClientSecret ? '(déjà configuré — laisser vide pour conserver)' : 'GOCSPX-…'}" />
          ${cfg.hasAppClientSecret ? '<span style="font-size:11px;color:#16a34a;">✅ Client Secret enregistré</span>' : ''}
        </div>
        <div class="settings-form-group">
          <label>URL de callback (doit correspondre à Google Cloud Console)</label>
          <input type="text" id="gd-callback" class="settings-input settings-input-wide" value="${esc(cfg.callbackUrl||window.location.origin+'/api/integrations/google-drive/callback')}" />
        </div>
      </div>
      <button id="gd-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="gd-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#gd-save').onclick = async () => {
    const appClientId = panel.querySelector('#gd-clientid').value.trim();
    const appClientSecret = panel.querySelector('#gd-clientsecret').value;
    const callbackUrl = panel.querySelector('#gd-callback').value.trim();
    const body = { appClientId, callbackUrl };
    if (appClientSecret) body.appClientSecret = appClientSecret;
    try {
      const r = await fetch(API.googleDriveGlobalConfig, {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) showNotification('✅ Configuration Google Drive enregistrée', 'success');
      else showNotification('❌ ' + (r.error || 'Erreur'), 'error');
    } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
  };
}

// ── Box global config ────────────────────────────────────────────────────────
async function renderBoxGlobalConfig(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let cfg = {};
  try {
    const r = await fetch(API.boxGlobalConfig, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok) cfg = r;
  } catch(e) {}

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Configuration globale Box OAuth2</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Créez une application sur
        <a href="https://app.box.com/developers/console" target="_blank" rel="noopener">Box Developer Console</a>
        (type OAuth 2.0), puis renseignez ici le Client ID et le Client Secret.
        L'URL de callback à enregistrer dans Box est :
        <code>${esc(cfg.callbackUrl || window.location.origin + '/api/integrations/box/callback')}</code>
      </p>
      <div style="display:grid;gap:12px;max-width:500px;">
        <div class="settings-form-group">
          <label>Client ID</label>
          <input type="text" id="box-clientid" class="settings-input settings-input-wide" value="${esc(cfg.appClientId||'')}" placeholder="xxxxxxxxxxxxxxxxxxxxxxxxxxxx" />
        </div>
        <div class="settings-form-group">
          <label>Client Secret</label>
          <input type="password" id="box-clientsecret" class="settings-input settings-input-wide" placeholder="${cfg.hasAppClientSecret ? '(déjà configuré — laisser vide pour conserver)' : 'xxxxxxxxxxxxxxxxxxxxxxxxxxxx'}" />
          ${cfg.hasAppClientSecret ? '<span style="font-size:11px;color:#16a34a;">✅ Client Secret enregistré</span>' : ''}
        </div>
        <div class="settings-form-group">
          <label>URL de callback (doit correspondre à Box Developer Console)</label>
          <input type="text" id="box-callback" class="settings-input settings-input-wide" value="${esc(cfg.callbackUrl||window.location.origin+'/api/integrations/box/callback')}" />
        </div>
      </div>
      <button id="box-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="box-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#box-save').onclick = async () => {
    const appClientId = panel.querySelector('#box-clientid').value.trim();
    const appClientSecret = panel.querySelector('#box-clientsecret').value;
    const callbackUrl = panel.querySelector('#box-callback').value.trim();
    const body = { appClientId, callbackUrl };
    if (appClientSecret) body.appClientSecret = appClientSecret;
    try {
      const r = await fetch(API.boxGlobalConfig, {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) showNotification('✅ Configuration Box enregistrée', 'success');
      else showNotification('❌ ' + (r.error || 'Erreur'), 'error');
    } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
  };
}

// ── OneDrive global config ───────────────────────────────────────────────────
async function renderOneDriveGlobalConfig(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let cfg = {};
  try {
    const r = await fetch(API.oneDriveGlobalConfig, { headers: authH() }).then(r => r.json()).catch(() => ({}));
    if (r.ok) cfg = r;
  } catch(e) {}

  panel.innerHTML = `
    <div class="settings-section-card">
      <h4>Configuration globale OneDrive / Office 365 OAuth2</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Créez une application sur
        <a href="https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade" target="_blank" rel="noopener">Azure App Registrations</a>,
        ajoutez les permissions <code>Files.ReadWrite</code> et <code>offline_access</code> (et <code>Sites.ReadWrite.All</code> pour SharePoint).
        L'URL de callback à enregistrer est :
        <code>${esc(cfg.callbackUrl || window.location.origin + '/api/integrations/onedrive/callback')}</code>
      </p>
      <div style="display:grid;gap:12px;max-width:500px;">
        <div class="settings-form-group">
          <label>Application (Client) ID</label>
          <input type="text" id="od-clientid" class="settings-input settings-input-wide" value="${esc(cfg.appClientId||'')}" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" />
        </div>
        <div class="settings-form-group">
          <label>Client Secret</label>
          <input type="password" id="od-clientsecret" class="settings-input settings-input-wide" placeholder="${cfg.hasAppClientSecret ? '(déjà configuré — laisser vide pour conserver)' : 'Valeur du secret'}" />
          ${cfg.hasAppClientSecret ? '<span style="font-size:11px;color:#16a34a;">✅ Client Secret enregistré</span>' : ''}
        </div>
        <div class="settings-form-group">
          <label>Tenant ID (optionnel, "common" pour multi-tenant)</label>
          <input type="text" id="od-tenantid" class="settings-input settings-input-wide" value="${esc(cfg.tenantId||'common')}" placeholder="common" />
        </div>
        <div class="settings-form-group">
          <label>URL de callback (doit correspondre à Azure)</label>
          <input type="text" id="od-callback" class="settings-input settings-input-wide" value="${esc(cfg.callbackUrl||window.location.origin+'/api/integrations/onedrive/callback')}" />
        </div>
      </div>
      <button id="od-save" class="btn btn-primary" style="margin-top:16px;">💾 Enregistrer</button>
      <div id="od-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector('#od-save').onclick = async () => {
    const appClientId = panel.querySelector('#od-clientid').value.trim();
    const appClientSecret = panel.querySelector('#od-clientsecret').value;
    const tenantId = panel.querySelector('#od-tenantid').value.trim() || 'common';
    const callbackUrl = panel.querySelector('#od-callback').value.trim();
    const body = { appClientId, tenantId, callbackUrl };
    if (appClientSecret) body.appClientSecret = appClientSecret;
    try {
      const r = await fetch(API.oneDriveGlobalConfig, {
        method: 'PUT', headers: authJsonH(), body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) showNotification('✅ Configuration OneDrive enregistrée', 'success');
      else showNotification('❌ ' + (r.error || 'Erreur'), 'error');
    } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
  };
}

// ── Imports log ──────────────────────────────────────────────────────────────
async function renderImportsLog(panel) {
  panel.innerHTML = `<div style="text-align:center;padding:20px;color:#6b7280;">Chargement…</div>`;

  let logs = [];
  let sources = [];
  try {
    const [lr, sr] = await Promise.all([
      fetch(API.logs + '?limit=100', { headers: authH() }).then(r => r.json()).catch(() => ({})),
      fetch(API.sources, { headers: authH() }).then(r => r.json()).catch(() => ({})),
    ]);
    if (lr.ok) logs = lr.logs || [];
    if (sr.ok) sources = sr.sources || [];
  } catch(e) {}

  panel.innerHTML = `
    <div style="display:flex;gap:12px;align-items:center;margin-bottom:16px;flex-wrap:wrap;">
      <div class="settings-form-group" style="margin:0;">
        <label style="font-size:12px;">Filtrer par source</label>
        <select id="os-log-source" class="settings-input" style="min-width:200px;">
          <option value="">Toutes les sources</option>
          ${sources.map(s => `<option value="${esc(s.id)}">${esc(s.name)}</option>`).join('')}
        </select>
      </div>
      <div class="settings-form-group" style="margin:0;">
        <label style="font-size:12px;">Filtrer par statut</label>
        <select id="os-log-status" class="settings-input">
          <option value="">Tous</option>
          <option value="success">✅ Succès</option>
          <option value="error">❌ Erreur</option>
          <option value="duplicate">⚠️ Doublon</option>
        </select>
      </div>
      <button id="os-log-refresh" class="btn btn-sm" style="margin-top:16px;">🔄 Actualiser</button>
      <button id="os-log-export" class="btn btn-sm" style="margin-top:16px;">📥 Export CSV</button>
    </div>
    <div id="os-log-table">
      ${renderLogTable(logs)}
    </div>
  `;

  async function refreshLogs() {
    const sourceId = panel.querySelector('#os-log-source').value;
    const status = panel.querySelector('#os-log-status').value;
    let url = API.logs + '?limit=100';
    if (sourceId) url += `&sourceId=${encodeURIComponent(sourceId)}`;
    if (status) url += `&status=${encodeURIComponent(status)}`;
    try {
      const r = await fetch(url, { headers: authH() }).then(r => r.json());
      logs = r.logs || [];
      panel.querySelector('#os-log-table').innerHTML = renderLogTable(logs);
    } catch(e) {}
  }

  panel.querySelector('#os-log-refresh').onclick = refreshLogs;
  panel.querySelector('#os-log-source').onchange = refreshLogs;
  panel.querySelector('#os-log-status').onchange = refreshLogs;

  panel.querySelector('#os-log-export').onclick = () => {
    const header = ['Date/Heure', 'Source', 'Dossier client', 'Fichier', 'Hash SHA-256', 'Statut', 'Fiche créée', 'Erreur'];
    const rows = logs.map(l => [
      fmtDate(l.processedAt), l.sourceName, l.clientFolder, l.fileName,
      l.fileHash, l.status, l.jobId || '', l.errorMessage || ''
    ]);
    const csv = [header, ...rows].map(r => r.map(v => `"${String(v).replace(/"/g,'""')}"`).join(';')).join('\n');
    const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8;' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = `imports-automatiques-${new Date().toISOString().slice(0,10)}.csv`;
    a.click();
  };
}

function renderLogTable(logs) {
  if (logs.length === 0) {
    return `<div style="text-align:center;padding:40px;color:#9ca3af;">Aucun import enregistré.</div>`;
  }
  return `
    <div style="overflow-x:auto;">
      <table style="width:100%;border-collapse:collapse;font-size:12px;">
        <thead>
          <tr style="background:#f1f5f9;text-align:left;">
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Date/Heure</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Source</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Dossier</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Fichier</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Statut</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Fiche</th>
            <th style="padding:8px 10px;border-bottom:2px solid #e2e8f0;">Détails</th>
          </tr>
        </thead>
        <tbody>
          ${logs.map(l => `
            <tr style="border-bottom:1px solid #f1f5f9;">
              <td style="padding:8px 10px;white-space:nowrap;">${fmtDate(l.processedAt)}</td>
              <td style="padding:8px 10px;">${esc(l.sourceName || l.sourceId)}</td>
              <td style="padding:8px 10px;font-family:monospace;">${esc(l.clientFolder)}</td>
              <td style="padding:8px 10px;font-family:monospace;max-width:200px;overflow:hidden;text-overflow:ellipsis;" title="${esc(l.fileName)}">${esc(l.fileName)}</td>
              <td style="padding:8px 10px;">${importStatusBadge(l.status)}</td>
              <td style="padding:8px 10px;">${l.jobId ? `<a href="#dossiers/${l.jobId}" style="color:#3b82f6;text-decoration:none;">${esc(l.jobId)}</a>` : '—'}</td>
              <td style="padding:8px 10px;max-width:200px;overflow:hidden;text-overflow:ellipsis;color:#dc2626;" title="${esc(l.errorMessage||'')}">
                ${l.errorMessage ? esc(l.errorMessage) : ''}
              </td>
            </tr>
          `).join('')}
        </tbody>
      </table>
    </div>
  `;
}
