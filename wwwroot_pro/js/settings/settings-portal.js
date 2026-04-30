import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPortal(panel) {
  panel.innerHTML = `<h3>🌐 Portail client</h3><p style="color:#6b7280;">Chargement…</p>`;

  let settings = {};
  let smtp = {};
  let clients = [];

  try {
    const r = await fetch('/api/admin/portal/settings', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
    if (r.ok) { settings = r.settings || {}; smtp = r.smtp || {}; }
    const rc = await fetch('/api/admin/portal/clients', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
    if (rc.ok) clients = rc.clients || [];
  } catch (e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>🌐 Portail client</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">Configurez le portail de commande en ligne accessible par vos clients.</p>

    <!-- General settings -->
    <div class="settings-section-card" id="portal-section-general">
      <h4>Activation & URL</h4>
      <div style="display:flex;align-items:center;gap:10px;margin-bottom:16px;">
        <input type="checkbox" id="portal-enabled" ${settings.enabled ? 'checked' : ''} />
        <label for="portal-enabled" style="font-size:14px;color:#374151;font-weight:500;">Portail activé</label>
      </div>
      <div class="settings-form-group">
        <label>URL publique du portail</label>
        <input type="url" id="portal-url" value="${esc(settings.portalUrl || '')}" class="settings-input settings-input-wide" placeholder="https://votredomaine.com" />
        <p style="font-size:12px;color:#6b7280;margin-top:4px;">Utilisée dans les liens des emails. Doit pointer vers la racine de votre serveur.</p>
      </div>
      <div class="settings-form-group" style="margin-top:12px;">
        <label>Message d'accueil (affiché sur la page de connexion)</label>
        <textarea id="portal-welcome" class="settings-input settings-input-wide" rows="3" style="font-family:sans-serif;">${esc(settings.welcomeText || '')}</textarea>
      </div>
      <div class="settings-form-group" style="margin-top:12px;">
        <label>Tuile Kanban "Commandes web" (nom du dossier)</label>
        <input type="text" id="portal-kanban-folder" value="${esc(settings.webOrderKanbanFolder || 'Commandes web')}" class="settings-input" style="max-width:260px;" />
      </div>
    </div>

    <!-- Security -->
    <div class="settings-section-card" style="margin-top:16px;">
      <h4>Sécurité</h4>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;">
        <div class="settings-form-group">
          <label>Tentatives de login max avant blocage</label>
          <input type="number" id="portal-max-attempts" value="${settings.maxLoginAttempts ?? 5}" class="settings-input" style="width:100px;" min="1" max="20" />
        </div>
        <div class="settings-form-group">
          <label>Durée de blocage (minutes)</label>
          <input type="number" id="portal-lock-duration" value="${settings.lockDurationMinutes ?? 30}" class="settings-input" style="width:100px;" min="1" />
        </div>
      </div>
    </div>

    <!-- Upload limits -->
    <div class="settings-section-card" style="margin-top:16px;">
      <h4>Limites d'upload</h4>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;">
        <div class="settings-form-group">
          <label>Taille max par fichier (Mo)</label>
          <input type="number" id="portal-max-size" value="${settings.maxUploadSizeMb ?? 500}" class="settings-input" style="width:120px;" min="1" />
        </div>
        <div class="settings-form-group">
          <label>Nombre max de fichiers par commande</label>
          <input type="number" id="portal-max-files" value="${settings.maxFilesPerOrder ?? 10}" class="settings-input" style="width:80px;" min="1" />
        </div>
      </div>
    </div>

    <!-- Form options -->
    <div class="settings-section-card" style="margin-top:16px;">
      <h4>Options du formulaire de commande</h4>

      <div class="settings-form-group" style="margin-bottom:16px;">
        <label>Formats disponibles (un par ligne)</label>
        <textarea id="portal-formats" class="settings-input settings-input-wide" rows="5">${esc((settings.availableFormats || []).join('\n'))}</textarea>
      </div>
      <div class="settings-form-group" style="margin-bottom:16px;">
        <label>Supports / Papiers disponibles (un par ligne)</label>
        <textarea id="portal-papers" class="settings-input settings-input-wide" rows="6">${esc((settings.availablePapers || []).join('\n'))}</textarea>
      </div>
      <div class="settings-form-group">
        <label>Finitions disponibles (un par ligne)</label>
        <textarea id="portal-finitions" class="settings-input settings-input-wide" rows="5">${esc((settings.availableFinitions || []).join('\n'))}</textarea>
      </div>
    </div>

    <!-- SMTP -->
    <div class="settings-section-card" style="margin-top:16px;">
      <h4>📧 Configuration SMTP (emails portail)</h4>
      <p style="font-size:12px;color:#6b7280;margin-bottom:12px;">Configurez le serveur SMTP pour l'envoi des emails de notification (confirmation, BAT, réinitialisation de mot de passe).</p>
      <div style="display:grid;grid-template-columns:1fr 100px;gap:12px;margin-bottom:12px;">
        <div class="settings-form-group"><label>Serveur SMTP</label><input type="text" id="smtp-host" value="${esc(smtp.host || '')}" class="settings-input settings-input-wide" placeholder="smtp.example.com" /></div>
        <div class="settings-form-group"><label>Port</label><input type="number" id="smtp-port" value="${smtp.port || 587}" class="settings-input" style="width:90px;" /></div>
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px;">
        <div class="settings-form-group"><label>Utilisateur SMTP</label><input type="text" id="smtp-username" value="${esc(smtp.username || '')}" class="settings-input settings-input-wide" placeholder="user@example.com" /></div>
        <div class="settings-form-group"><label>Mot de passe (laisser vide pour ne pas modifier)</label><input type="password" id="smtp-password" value="" class="settings-input settings-input-wide" placeholder="••••••••" /></div>
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px;">
        <div class="settings-form-group"><label>Email expéditeur</label><input type="email" id="smtp-from" value="${esc(smtp.fromAddress || '')}" class="settings-input settings-input-wide" placeholder="portail@votreSoc.fr" /></div>
        <div class="settings-form-group"><label>Nom expéditeur</label><input type="text" id="smtp-from-name" value="${esc(smtp.fromName || 'Portail Client')}" class="settings-input settings-input-wide" /></div>
      </div>
      <div class="settings-form-group" style="margin-bottom:12px;"><label>Email de notification atelier</label><input type="email" id="smtp-atelier-email" value="${esc(smtp.atelierNotifyEmail || '')}" class="settings-input settings-input-wide" placeholder="atelier@votreSoc.fr" /></div>
      <div style="display:flex;align-items:center;gap:8px;margin-bottom:16px;">
        <input type="checkbox" id="smtp-ssl" ${smtp.useSsl !== false ? 'checked' : ''} />
        <label for="smtp-ssl" style="font-size:13px;color:#374151;">Connexion SSL/TLS</label>
      </div>
    </div>

    <button id="portal-save-settings" class="btn btn-primary" style="margin-top:8px;">Enregistrer la configuration</button>
    <span id="portal-settings-msg" style="margin-left:12px;font-size:13px;"></span>

    <!-- Client accounts -->
    <div style="margin-top:32px;">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;">
        <h4 style="margin:0;">Comptes clients</h4>
        <button id="btn-new-client" class="btn btn-primary btn-sm">+ Nouveau client</button>
      </div>

      <div id="clients-list"></div>

      <!-- New/edit client form -->
      <div id="client-form-card" class="settings-section-card hidden" style="margin-top:16px;">
        <h4 id="client-form-title">Nouveau compte client</h4>
        <div id="client-form-error" class="alert alert-error hidden" style="margin-bottom:12px;"></div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;">
          <div class="settings-form-group"><label>Email <span style="color:red">*</span></label><input type="email" id="cf-email" class="settings-input settings-input-wide" /></div>
          <div class="settings-form-group"><label>Mot de passe <span style="color:red">*</span></label><input type="password" id="cf-password" class="settings-input settings-input-wide" placeholder="Minimum 8 caractères" /></div>
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:8px;">
          <div class="settings-form-group"><label>Nom / Prénom</label><input type="text" id="cf-display-name" class="settings-input settings-input-wide" /></div>
          <div class="settings-form-group"><label>Société</label><input type="text" id="cf-company-name" class="settings-input settings-input-wide" /></div>
        </div>
        <div class="settings-form-group" style="margin-top:8px;"><label>Téléphone</label><input type="tel" id="cf-phone" class="settings-input" style="max-width:200px;" /></div>
        <div style="display:flex;gap:8px;margin-top:12px;">
          <button id="btn-client-form-save" class="btn btn-primary btn-sm">Enregistrer</button>
          <button id="btn-client-form-cancel" class="btn btn-secondary btn-sm">Annuler</button>
        </div>
      </div>
    </div>
  `;

  // Render clients table
  renderClientsTable(clients);

  // Save settings
  document.getElementById('portal-save-settings').onclick = async () => {
    const msgEl = document.getElementById('portal-settings-msg');
    msgEl.textContent = '';
    const body = {
      enabled: document.getElementById('portal-enabled').checked,
      portalUrl: document.getElementById('portal-url').value.trim(),
      welcomeText: document.getElementById('portal-welcome').value,
      webOrderKanbanFolder: document.getElementById('portal-kanban-folder').value.trim() || 'Commandes web',
      maxLoginAttempts: parseInt(document.getElementById('portal-max-attempts').value) || 5,
      lockDurationMinutes: parseInt(document.getElementById('portal-lock-duration').value) || 30,
      maxUploadSizeMb: parseInt(document.getElementById('portal-max-size').value) || 500,
      maxFilesPerOrder: parseInt(document.getElementById('portal-max-files').value) || 10,
      availableFormats: document.getElementById('portal-formats').value.split('\n').map(s => s.trim()).filter(Boolean),
      availablePapers: document.getElementById('portal-papers').value.split('\n').map(s => s.trim()).filter(Boolean),
      availableFinitions: document.getElementById('portal-finitions').value.split('\n').map(s => s.trim()).filter(Boolean),
      smtp: {
        host: document.getElementById('smtp-host').value.trim(),
        port: parseInt(document.getElementById('smtp-port').value) || 587,
        useSsl: document.getElementById('smtp-ssl').checked,
        username: document.getElementById('smtp-username').value.trim(),
        password: document.getElementById('smtp-password').value,
        fromAddress: document.getElementById('smtp-from').value.trim(),
        fromName: document.getElementById('smtp-from-name').value.trim(),
        atelierNotifyEmail: document.getElementById('smtp-atelier-email').value.trim()
      }
    };

    try {
      const r = await fetch('/api/admin/portal/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✓ Enregistré'; showNotification('Configuration portail enregistrée', 'success'); }
      else { msgEl.style.color = '#dc2626'; msgEl.textContent = r.error || 'Erreur'; }
    } catch { msgEl.style.color = '#dc2626'; msgEl.textContent = 'Erreur réseau'; }
  };

  // New client button
  let editingClientId = null;
  document.getElementById('btn-new-client').onclick = () => {
    editingClientId = null;
    document.getElementById('client-form-title').textContent = 'Nouveau compte client';
    document.getElementById('cf-email').value = '';
    document.getElementById('cf-password').value = '';
    document.getElementById('cf-display-name').value = '';
    document.getElementById('cf-company-name').value = '';
    document.getElementById('cf-phone').value = '';
    document.getElementById('cf-password').placeholder = 'Minimum 8 caractères';
    document.getElementById('client-form-card').classList.remove('hidden');
    document.getElementById('client-form-error').classList.add('hidden');
    document.getElementById('cf-email').focus();
  };
  document.getElementById('btn-client-form-cancel').onclick = () => {
    document.getElementById('client-form-card').classList.add('hidden');
  };
  document.getElementById('btn-client-form-save').onclick = async () => {
    const errEl = document.getElementById('client-form-error');
    errEl.classList.add('hidden');
    const email = document.getElementById('cf-email').value.trim();
    const password = document.getElementById('cf-password').value;
    const displayName = document.getElementById('cf-display-name').value.trim();
    const companyName = document.getElementById('cf-company-name').value.trim();
    const contactPhone = document.getElementById('cf-phone').value.trim();

    if (!editingClientId && !password) { errEl.textContent = 'Le mot de passe est obligatoire'; errEl.classList.remove('hidden'); return; }

    try {
      let url, method, body;
      if (editingClientId) {
        url = `/api/admin/portal/clients/${editingClientId}`;
        method = 'PUT';
        body = { email, displayName, companyName, contactPhone };
        if (password) body.password = password;
      } else {
        url = '/api/admin/portal/clients';
        method = 'POST';
        body = { email, password, displayName, companyName, contactPhone };
      }

      const r = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify(body)
      }).then(r => r.json());

      if (!r.ok) { errEl.textContent = r.error || 'Erreur'; errEl.classList.remove('hidden'); return; }

      document.getElementById('client-form-card').classList.add('hidden');
      showNotification(editingClientId ? 'Client mis à jour' : 'Client créé', 'success');

      // Refresh
      const rc = await fetch('/api/admin/portal/clients', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
      if (rc.ok) renderClientsTable(rc.clients || []);
    } catch { errEl.textContent = 'Erreur réseau'; errEl.classList.remove('hidden'); }
  };

  function renderClientsTable(cls) {
    const container = document.getElementById('clients-list');
    if (!cls.length) {
      container.innerHTML = `<p style="color:#6b7280;font-size:13px;">Aucun compte client. Créez-en un ci-dessus.</p>`;
      return;
    }
    const rows = cls.map(c => `
      <tr>
        <td><strong>${esc(c.email)}</strong></td>
        <td>${esc(c.displayName || '')} ${c.companyName ? `<span style="color:#6b7280">(${esc(c.companyName)})</span>` : ''}</td>
        <td>${c.lastLoginAt ? new Date(c.lastLoginAt).toLocaleDateString('fr-FR') : '—'}</td>
        <td><span class="badge ${c.enabled ? 'badge-green' : 'badge-red'}">${c.enabled ? 'Actif' : 'Désactivé'}</span></td>
        <td>
          <div style="display:flex;gap:4px;flex-wrap:wrap;">
            <button class="btn btn-secondary btn-sm" onclick="editClient('${esc(c.id)}','${esc(c.email)}','${esc(c.displayName||'')}','${esc(c.companyName||'')}','${esc(c.contactPhone||'')}')">Modifier</button>
            <button class="btn btn-secondary btn-sm" onclick="toggleClient('${esc(c.id)}',${!c.enabled})">${c.enabled ? 'Désactiver' : 'Réactiver'}</button>
            <button class="btn btn-secondary btn-sm" onclick="resetClientPwd('${esc(c.id)}')">Reset MDP</button>
          </div>
        </td>
      </tr>`).join('');

    container.innerHTML = `<table class="settings-table" style="width:100%;border-collapse:collapse;font-size:13px;">
      <thead><tr>
        <th style="text-align:left;padding:8px 10px;background:#f9fafb;border-bottom:1px solid #e5e7eb;">Email</th>
        <th style="text-align:left;padding:8px 10px;background:#f9fafb;border-bottom:1px solid #e5e7eb;">Nom / Société</th>
        <th style="text-align:left;padding:8px 10px;background:#f9fafb;border-bottom:1px solid #e5e7eb;">Dernière connexion</th>
        <th style="text-align:left;padding:8px 10px;background:#f9fafb;border-bottom:1px solid #e5e7eb;">Statut</th>
        <th style="text-align:left;padding:8px 10px;background:#f9fafb;border-bottom:1px solid #e5e7eb;">Actions</th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  }

  window.editClient = (id, email, displayName, companyName, phone) => {
    editingClientId = id;
    document.getElementById('client-form-title').textContent = 'Modifier le compte client';
    document.getElementById('cf-email').value = email;
    document.getElementById('cf-password').value = '';
    document.getElementById('cf-password').placeholder = 'Laisser vide pour ne pas changer';
    document.getElementById('cf-display-name').value = displayName;
    document.getElementById('cf-company-name').value = companyName;
    document.getElementById('cf-phone').value = phone;
    document.getElementById('client-form-card').classList.remove('hidden');
    document.getElementById('client-form-error').classList.add('hidden');
    document.getElementById('cf-email').focus();
  };

  window.toggleClient = async (id, enabled) => {
    try {
      const r = await fetch(`/api/admin/portal/clients/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ enabled })
      }).then(r => r.json());
      if (r.ok) {
        showNotification(enabled ? 'Compte réactivé' : 'Compte désactivé', 'success');
        const rc = await fetch('/api/admin/portal/clients', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
        if (rc.ok) renderClientsTable(rc.clients || []);
      }
    } catch { showNotification('Erreur', 'error'); }
  };

  window.resetClientPwd = async (id) => {
    const pwd = prompt('Nouveau mot de passe (minimum 8 caractères) :');
    if (!pwd) return;
    if (pwd.length < 8) { alert('Minimum 8 caractères'); return; }
    try {
      const r = await fetch(`/api/admin/portal/clients/${id}/reset-password`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ password: pwd })
      }).then(r => r.json());
      if (r.ok) showNotification('Mot de passe réinitialisé', 'success');
      else showNotification(r.error || 'Erreur', 'error');
    } catch { showNotification('Erreur réseau', 'error'); }
  };
}
