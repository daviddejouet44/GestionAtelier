export async function initLicense() {
  attachLicenseEvents();
  await loadLicenseStatus();
}

export async function loadLicenseStatus() {
  try {
    const res = await fetch('/api/license/status');
    const data = await res.json();
    applyLicenseUI(data);
    return data;
  } catch {
    applyLicenseUI({ isValid: false, level: 0, reason: 'Impossible de contacter le serveur' });
    return null;
  }
}

function applyLicenseUI(data) {
  const level = data.level || 0;
  window._licenseLevel = level;

  const badge = document.getElementById('license-badge');
  if (badge) {
    badge.textContent = data.isValid
      ? `🔓 ${data.version} — expire le ${data.expireOn}`
      : '⚠️ Licence invalide';
    badge.className = 'license-badge ' + (data.isValid ? 'valid' : 'invalid');
  }

  const statusEl = document.getElementById('license-status-text');
  if (statusEl) {
    statusEl.innerHTML = data.isValid
      ? `<strong style="color:#15803d;">✅ ${data.version}</strong> — Client : ${data.client} — Expire le <strong>${data.expireOn}</strong>`
      : `<strong style="color:#dc2626;">❌ ${data.reason || 'Aucune licence valide'}</strong>`;
  }

  setVisible('btnViewSubmission', level >= 1);
  setVisible('btnViewKanban', level >= 1);
  setVisible('btnViewBat', level >= 1);
  setVisible('btnRemoteManager', level >= 1);
  setVisible('btnPrismalytics', level >= 1);
  setVisible('btnViewRapport', level >= 1);
  setVisible('btnViewDossiers', level >= 1);
  setVisible('btnViewRecycle', level >= 1);
  setVisible('btnViewCalendar', level >= 2);
  setVisible('btnViewDashboard', level >= 2);
  setVisible('btn-envoyer-devis', level >= 3);
  setVisible('btnViewGlobalProd', level >= 3);

  const sidebarRetard = document.getElementById('kanban-sidebar-sec-retard');
  const sidebarMachine = document.getElementById('kanban-sidebar-sec-machine');
  if (level < 2) {
    if (sidebarRetard) sidebarRetard.style.display = 'none';
    if (sidebarMachine) sidebarMachine.style.display = 'none';
  } else {
    if (sidebarRetard) sidebarRetard.style.display = '';
    if (sidebarMachine) sidebarMachine.style.display = '';
  }

  const submissionSection = document.querySelector('.submission-section');
  if (submissionSection) submissionSection.style.display = level >= 2 ? '' : 'none';

  ['btn-help', 'help-btn', 'help-panel', 'help-toggle'].forEach(id => {
    const el = document.getElementById(id);
    if (el) el.style.display = level >= 2 ? '' : 'none';
  });
  document.querySelectorAll('.help-btn, .help-toggle').forEach(el => {
    el.style.display = level >= 2 ? '' : 'none';
  });

  if (!data.isValid) {
    const modal = document.getElementById('license-modal');
    if (modal && modal.classList.contains('hidden')) {
      openLicenseModal();
    }
  }
}

function setVisible(id, visible) {
  const el = document.getElementById(id);
  if (el) el.style.display = visible ? '' : 'none';
}

export function openLicenseModal() {
  const modal = document.getElementById('license-modal');
  if (!modal) return;
  if (!modal.classList.contains('hidden')) return;
  modal.classList.remove('hidden');
  loadMachineToken();
}

export function closeLicenseModal() {
  document.getElementById('license-modal')?.classList.add('hidden');
}

async function loadMachineToken() {
  const tokenEl = document.getElementById('license-machine-token');
  if (!tokenEl) return;
  tokenEl.textContent = 'Chargement…';
  try {
    const res = await fetch('/api/license/token');
    const data = await res.json();
    tokenEl.textContent = data.token;
  } catch {
    tokenEl.textContent = 'Erreur de chargement';
  }
}

let _licenseEventsAttached = false;

function attachLicenseEvents() {
  if (_licenseEventsAttached) return;
  _licenseEventsAttached = true;

  document.getElementById('license-copy-token')?.addEventListener('click', () => {
    const tok = document.getElementById('license-machine-token')?.textContent;
    if (tok && tok !== 'Chargement…') {
      navigator.clipboard.writeText(tok).then(() => showLicenseMsg('Token copié ✓', 'success'));
    }
  });

  document.getElementById('license-file-input')?.addEventListener('change', (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    document.getElementById('license-file-name').textContent = file.name;
    document.getElementById('license-file-preview').style.display = 'flex';
    showLicenseMsg('', '');
  });

  const uploadZone = document.getElementById('license-upload-zone');
  if (uploadZone) {
    uploadZone.addEventListener('dragover', (e) => { e.preventDefault(); uploadZone.style.borderColor = '#1d4ed8'; });
    uploadZone.addEventListener('dragleave', () => { uploadZone.style.borderColor = '#d1d5db'; });
    uploadZone.addEventListener('drop', (e) => {
      e.preventDefault();
      uploadZone.style.borderColor = '#d1d5db';
      const file = e.dataTransfer?.files?.[0];
      if (file?.name.endsWith('.lic')) {
        const dt = new DataTransfer();
        dt.items.add(file);
        document.getElementById('license-file-input').files = dt.files;
        document.getElementById('license-file-name').textContent = file.name;
        document.getElementById('license-file-preview').style.display = 'flex';
      }
    });
  }

  document.getElementById('license-activate-btn')?.addEventListener('click', async () => {
    const file = document.getElementById('license-file-input')?.files?.[0];
    if (!file) { showLicenseMsg('Veuillez sélectionner un fichier .lic', 'error'); return; }

    const btn = document.getElementById('license-activate-btn');
    btn.disabled = true; btn.textContent = 'Activation…';

    const fd = new FormData();
    fd.append('licfile', file);
    try {
      const res = await fetch('/api/license/activate', { method: 'POST', body: fd });
      const data = await res.json();
      if (res.ok) {
        showLicenseMsg(`✅ ${data.message}`, 'success');
        setTimeout(async () => { closeLicenseModal(); await loadLicenseStatus(); }, 2000);
      } else {
        showLicenseMsg(`❌ ${data.error}`, 'error');
      }
    } catch { showLicenseMsg('❌ Erreur réseau', 'error'); }
    finally { btn.disabled = false; btn.textContent = '🔓 Activer la licence'; }
  });

  document.getElementById('license-modal-close')?.addEventListener('click', closeLicenseModal);
}

document.addEventListener('DOMContentLoaded', attachLicenseEvents);

function showLicenseMsg(msg, type) {
  const el = document.getElementById('license-msg');
  if (!el) return;
  el.textContent = msg;
  el.className = 'license-msg ' + type;
  el.style.display = msg ? 'block' : 'none';
}
