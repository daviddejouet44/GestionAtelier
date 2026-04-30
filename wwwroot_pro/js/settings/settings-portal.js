import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPortal(panel) {
  panel.innerHTML = `<h3>🌐 Portail client</h3><p style="color:#6b7280;">Chargement…</p>`;

  let settings = {};
  let smtp = {};
  let clients = [];
  let theme = {};
  let formFields = [];

  try {
    const [r, rc, rt, rf] = await Promise.all([
      fetch('/api/admin/portal/settings',    { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/clients',     { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/theme',       { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
      fetch('/api/admin/portal/form-fields', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({})),
    ]);
    if (r.ok)  { settings = r.settings || {}; smtp = r.smtp || {}; }
    if (rc.ok) clients = rc.clients || [];
    if (rt.ok) theme = rt.theme || {};
    if (rf.ok) formFields = rf.fields || [];
  } catch (e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>🌐 Portail client</h3>
    <div class="settings-subtabs" style="display:flex;gap:4px;margin-bottom:20px;border-bottom:2px solid #e5e7eb;padding-bottom:0;">
      <button class="portal-subtab active" data-subtab="general" style="padding:8px 16px;border:none;background:none;cursor:pointer;font-size:13px;font-weight:600;color:#1d4ed8;border-bottom:2px solid #1d4ed8;margin-bottom:-2px;">⚙️ Général</button>
      <button class="portal-subtab" data-subtab="form-fields" style="padding:8px 16px;border:none;background:none;cursor:pointer;font-size:13px;font-weight:500;color:#6b7280;">📋 Champs du formulaire</button>
      <button class="portal-subtab" data-subtab="theme" style="padding:8px 16px;border:none;background:none;cursor:pointer;font-size:13px;font-weight:500;color:#6b7280;">🎨 Apparence</button>
      <button class="portal-subtab" data-subtab="clients" style="padding:8px 16px;border:none;background:none;cursor:pointer;font-size:13px;font-weight:500;color:#6b7280;">👥 Comptes clients</button>
    </div>

    <!-- GENERAL TAB -->
    <div id="portal-tab-general" class="portal-tab-content">
      <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">Configurez le portail de commande en ligne accessible par vos clients.</p>

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
          <div style="display:flex;gap:8px;align-items:center;">
            <input type="text" id="portal-kanban-folder" value="${esc(settings.webOrderKanbanFolder || 'Commandes web')}" class="settings-input" style="max-width:260px;" />
            <span style="font-size:12px;color:#6b7280;">La tuile sera automatiquement ajoutée dans Paramétrage → Tuiles à la sauvegarde.</span>
          </div>
        </div>
      </div>

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
    </div>

    <!-- FORM FIELDS TAB -->
    <div id="portal-tab-form-fields" class="portal-tab-content hidden">
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Configurez les champs affichés dans le formulaire "Nouvelle commande" du portail. Les champs marqués 🔒 sont critiques et ne peuvent pas être cachés ni rendus optionnels.
      </p>
      <div id="portal-fields-list" style="display:flex;flex-direction:column;gap:8px;margin-bottom:16px;"></div>
      <div style="display:flex;gap:8px;">
        <button id="portal-fields-save" class="btn btn-primary">Enregistrer</button>
        <button id="portal-fields-reset" class="btn btn-sm" style="color:#6b7280;">Réinitialiser par défaut</button>
        <span id="portal-fields-msg" style="margin-left:8px;font-size:13px;align-self:center;"></span>
      </div>
    </div>

    <!-- THEME TAB -->
    <div id="portal-tab-theme" class="portal-tab-content hidden">
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Personnalisez l'apparence du portail client. Les changements sont appliqués automatiquement à toutes les pages du portail.</p>

      <div class="settings-section-card">
        <h4>🎨 Couleurs</h4>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;">
          <div class="settings-form-group">
            <label>Couleur principale (boutons, liens)</label>
            <div style="display:flex;gap:8px;align-items:center;">
              <input type="color" id="theme-primary" value="${esc(theme.primaryColor || '#1d4ed8')}" style="width:48px;height:36px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;padding:2px;" />
              <input type="text" id="theme-primary-hex" value="${esc(theme.primaryColor || '#1d4ed8')}" class="settings-input" style="width:100px;font-family:monospace;" placeholder="#1d4ed8" />
            </div>
          </div>
          <div class="settings-form-group">
            <label>Couleur principale foncée (hover)</label>
            <div style="display:flex;gap:8px;align-items:center;">
              <input type="color" id="theme-primary-dark" value="${esc(theme.primaryDarkColor || '#1e40af')}" style="width:48px;height:36px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;padding:2px;" />
              <input type="text" id="theme-primary-dark-hex" value="${esc(theme.primaryDarkColor || '#1e40af')}" class="settings-input" style="width:100px;font-family:monospace;" placeholder="#1e40af" />
            </div>
          </div>
          <div class="settings-form-group">
            <label>Couleur de fond</label>
            <div style="display:flex;gap:8px;align-items:center;">
              <input type="color" id="theme-bg" value="${esc(theme.backgroundColor || '#f9fafb')}" style="width:48px;height:36px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;padding:2px;" />
              <input type="text" id="theme-bg-hex" value="${esc(theme.backgroundColor || '#f9fafb')}" class="settings-input" style="width:100px;font-family:monospace;" placeholder="#f9fafb" />
            </div>
          </div>
          <div class="settings-form-group">
            <label>Couleur du texte</label>
            <div style="display:flex;gap:8px;align-items:center;">
              <input type="color" id="theme-text" value="${esc(theme.textColor || '#374151')}" style="width:48px;height:36px;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;padding:2px;" />
              <input type="text" id="theme-text-hex" value="${esc(theme.textColor || '#374151')}" class="settings-input" style="width:100px;font-family:monospace;" placeholder="#374151" />
            </div>
          </div>
        </div>
      </div>

      <div class="settings-section-card" style="margin-top:16px;">
        <h4>🔤 Typographie</h4>
        <div class="settings-form-group">
          <label>Police de caractères</label>
          <select id="theme-font" class="settings-input">
            <option value="system-ui" ${(theme.fontFamily||'system-ui')==='system-ui'?'selected':''}>Système (system-ui)</option>
            <option value="Inter, sans-serif" ${(theme.fontFamily||'')==='Inter, sans-serif'?'selected':''}>Inter</option>
            <option value="Roboto, sans-serif" ${(theme.fontFamily||'')==='Roboto, sans-serif'?'selected':''}>Roboto</option>
            <option value="'Open Sans', sans-serif" ${(theme.fontFamily||'')==="'Open Sans', sans-serif"?'selected':''}>Open Sans</option>
            <option value="Lato, sans-serif" ${(theme.fontFamily||'')==='Lato, sans-serif'?'selected':''}>Lato</option>
            <option value="Arial, sans-serif" ${(theme.fontFamily||'')==='Arial, sans-serif'?'selected':''}>Arial</option>
          </select>
        </div>
      </div>

      <div class="settings-section-card" style="margin-top:16px;">
        <h4>🏢 En-tête et pied de page</h4>
        <div class="settings-form-group">
          <label>Nom de l'entreprise (affiché dans l'en-tête)</label>
          <input type="text" id="theme-company" value="${esc(theme.companyName || 'Espace client')}" class="settings-input settings-input-wide" placeholder="Mon Entreprise" />
        </div>
        <div class="settings-form-group" style="margin-top:12px;">
          <label>Slogan / sous-titre</label>
          <input type="text" id="theme-tagline" value="${esc(theme.tagline || '')}" class="settings-input settings-input-wide" placeholder="Votre imprimeur de confiance" />
        </div>
        <div class="settings-form-group" style="margin-top:12px;">
          <label>Lien "Nous contacter"</label>
          <input type="url" id="theme-contact" value="${esc(theme.contactLink || '')}" class="settings-input settings-input-wide" placeholder="https://votre-site.com/contact" />
        </div>
        <div class="settings-form-group" style="margin-top:12px;">
          <label>Pied de page (mentions légales, CGV…)</label>
          <textarea id="theme-footer" class="settings-input settings-input-wide" rows="4" placeholder="© Mon Entreprise 2025 — Mentions légales — CGV">${esc(theme.footerText || '')}</textarea>
        </div>
      </div>

      <div class="settings-section-card" style="margin-top:16px;">
        <h4>📄 Page de connexion</h4>
        <div class="settings-form-group">
          <label>Texte de la page "Mes commandes"</label>
          <textarea id="theme-orders-text" class="settings-input settings-input-wide" rows="3" placeholder="Bienvenue ! Retrouvez ici toutes vos commandes.">${esc(theme.ordersPageText || '')}</textarea>
        </div>
      </div>

      <div class="settings-section-card" style="margin-top:16px;">
        <h4>💻 CSS personnalisé (avancé)</h4>
        <p style="font-size:12px;color:#6b7280;margin-bottom:8px;">Ce CSS est injecté uniquement sur les pages /portal/*. Utilisez-le pour des ajustements fins.</p>
        <textarea id="theme-css" class="settings-input settings-input-wide" rows="8" style="font-family:monospace;font-size:12px;">${esc(theme.customCss || '')}</textarea>
      </div>

      <div style="display:flex;gap:8px;margin-top:16px;">
        <button id="portal-theme-save" class="btn btn-primary">Enregistrer l'apparence</button>
        <a href="/portal/login.html" target="_blank" class="btn btn-secondary btn-sm" style="align-self:center;">🔍 Aperçu portail</a>
        <span id="portal-theme-msg" style="margin-left:8px;font-size:13px;align-self:center;"></span>
      </div>
    </div>

    <!-- CLIENTS TAB -->
    <div id="portal-tab-clients" class="portal-tab-content hidden">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:12px;">
        <h4 style="margin:0;">Comptes clients</h4>
        <button id="btn-new-client" class="btn btn-primary btn-sm">+ Nouveau client</button>
      </div>

      <div id="clients-list"></div>

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

  // ── Sub-tab navigation ───────────────────────────────────────────────────
  panel.querySelectorAll('.portal-subtab').forEach(tab => {
    tab.onclick = () => {
      panel.querySelectorAll('.portal-subtab').forEach(t => {
        t.classList.remove('active');
        t.style.color = '#6b7280';
        t.style.borderBottom = 'none';
        t.style.fontWeight = '500';
      });
      tab.classList.add('active');
      tab.style.color = '#1d4ed8';
      tab.style.borderBottom = '2px solid #1d4ed8';
      tab.style.fontWeight = '600';
      panel.querySelectorAll('.portal-tab-content').forEach(c => c.classList.add('hidden'));
      panel.querySelector(`#portal-tab-${tab.dataset.subtab}`).classList.remove('hidden');
    };
  });

  // ── Sync color pickers with hex inputs ──────────────────────────────────
  const colorPairs = [
    ['theme-primary', 'theme-primary-hex'],
    ['theme-primary-dark', 'theme-primary-dark-hex'],
    ['theme-bg', 'theme-bg-hex'],
    ['theme-text', 'theme-text-hex'],
  ];
  colorPairs.forEach(([pickerId, hexId]) => {
    const picker = panel.querySelector(`#${pickerId}`);
    const hex    = panel.querySelector(`#${hexId}`);
    if (!picker || !hex) return;
    picker.oninput = () => { hex.value = picker.value; };
    hex.oninput = () => { if (/^#[0-9a-fA-F]{6}$/.test(hex.value)) picker.value = hex.value; };
  });

  // ── Save general settings ────────────────────────────────────────────────
  panel.querySelector('#portal-save-settings').onclick = async () => {
    const msgEl = panel.querySelector('#portal-settings-msg');
    msgEl.textContent = '';
    const body = {
      enabled: panel.querySelector('#portal-enabled').checked,
      portalUrl: panel.querySelector('#portal-url').value.trim(),
      welcomeText: panel.querySelector('#portal-welcome').value,
      webOrderKanbanFolder: panel.querySelector('#portal-kanban-folder').value.trim() || 'Commandes web',
      maxLoginAttempts: parseInt(panel.querySelector('#portal-max-attempts').value) || 5,
      lockDurationMinutes: parseInt(panel.querySelector('#portal-lock-duration').value) || 30,
      maxUploadSizeMb: parseInt(panel.querySelector('#portal-max-size').value) || 500,
      maxFilesPerOrder: parseInt(panel.querySelector('#portal-max-files').value) || 10,
      availableFormats: panel.querySelector('#portal-formats').value.split('\n').map(s => s.trim()).filter(Boolean),
      availablePapers: panel.querySelector('#portal-papers').value.split('\n').map(s => s.trim()).filter(Boolean),
      availableFinitions: panel.querySelector('#portal-finitions').value.split('\n').map(s => s.trim()).filter(Boolean),
      smtp: {
        host: panel.querySelector('#smtp-host').value.trim(),
        port: parseInt(panel.querySelector('#smtp-port').value) || 587,
        useSsl: panel.querySelector('#smtp-ssl').checked,
        username: panel.querySelector('#smtp-username').value.trim(),
        password: panel.querySelector('#smtp-password').value,
        fromAddress: panel.querySelector('#smtp-from').value.trim(),
        fromName: panel.querySelector('#smtp-from-name').value.trim(),
        atelierNotifyEmail: panel.querySelector('#smtp-atelier-email').value.trim()
      }
    };

    try {
      const r = await fetch('/api/admin/portal/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✓ Enregistré (tuile Kanban synchronisée)'; showNotification('Configuration portail enregistrée', 'success'); }
      else { msgEl.style.color = '#dc2626'; msgEl.textContent = r.error || 'Erreur'; }
    } catch { msgEl.style.color = '#dc2626'; msgEl.textContent = 'Erreur réseau'; }
  };

  // ── Save theme ────────────────────────────────────────────────────────────
  panel.querySelector('#portal-theme-save').onclick = async () => {
    const msgEl = panel.querySelector('#portal-theme-msg');
    msgEl.textContent = '';
    const body = {
      primaryColor:     panel.querySelector('#theme-primary-hex').value.trim(),
      primaryDarkColor: panel.querySelector('#theme-primary-dark-hex').value.trim(),
      backgroundColor:  panel.querySelector('#theme-bg-hex').value.trim(),
      textColor:        panel.querySelector('#theme-text-hex').value.trim(),
      fontFamily:       panel.querySelector('#theme-font').value,
      companyName:      panel.querySelector('#theme-company').value.trim(),
      tagline:          panel.querySelector('#theme-tagline').value.trim(),
      contactLink:      panel.querySelector('#theme-contact').value.trim(),
      footerText:       panel.querySelector('#theme-footer').value,
      ordersPageText:   panel.querySelector('#theme-orders-text').value,
      customCss:        panel.querySelector('#theme-css').value,
    };
    try {
      const r = await fetch('/api/admin/portal/theme', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify(body)
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✓ Apparence enregistrée'; showNotification('Apparence portail enregistrée', 'success'); }
      else { msgEl.style.color = '#dc2626'; msgEl.textContent = r.error || 'Erreur'; }
    } catch { msgEl.style.color = '#dc2626'; msgEl.textContent = 'Erreur réseau'; }
  };

  // ── Form fields UI ────────────────────────────────────────────────────────
  renderFormFieldsList(panel, formFields);

  panel.querySelector('#portal-fields-save').onclick = async () => {
    const msgEl = panel.querySelector('#portal-fields-msg');
    msgEl.textContent = '';
    const rows = Array.from(panel.querySelectorAll('.pff-row'));
    const fields = rows.map((row, i) => ({
      id:           row.dataset.fieldId,
      label:        row.querySelector('.pff-label').textContent,
      customLabel:  row.querySelector('.pff-custom-label').value.trim(),
      placeholder:  row.querySelector('.pff-placeholder').value.trim(),
      type:         row.querySelector('.pff-type') ? row.querySelector('.pff-type').textContent : 'text',
      visible:      row.querySelector('.pff-visible').checked,
      required:     row.querySelector('.pff-required').checked,
      critical:     row.dataset.critical === 'true',
      order:        i,
      defaultValue: row.querySelector('.pff-default') ? row.querySelector('.pff-default').value.trim() : '',
      allowedValues: (row.querySelector('.pff-allowed') ? row.querySelector('.pff-allowed').value : '').split('\n').map(s => s.trim()).filter(Boolean),
    }));
    try {
      const r = await fetch('/api/admin/portal/form-fields', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ fields })
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✓ Enregistré'; showNotification('Champs du formulaire enregistrés', 'success'); }
      else { msgEl.style.color = '#dc2626'; msgEl.textContent = r.error || 'Erreur'; }
    } catch { msgEl.style.color = '#dc2626'; msgEl.textContent = 'Erreur réseau'; }
  };

  panel.querySelector('#portal-fields-reset').onclick = async () => {
    if (!confirm('Réinitialiser les champs du formulaire par défaut ?')) return;
    try {
      await fetch('/api/admin/portal/form-fields', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ fields: [] })
      }).then(r => r.json());
      const rf = await fetch('/api/admin/portal/form-fields', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
      renderFormFieldsList(panel, rf.fields || []);
      showNotification('Champs réinitialisés', 'success');
    } catch { showNotification('Erreur', 'error'); }
  };

  // ── Clients tab ───────────────────────────────────────────────────────────
  renderClientsTable(clients, panel);

  let editingClientId = null;
  panel.querySelector('#btn-new-client').onclick = () => {
    editingClientId = null;
    panel.querySelector('#client-form-title').textContent = 'Nouveau compte client';
    panel.querySelector('#cf-email').value = '';
    panel.querySelector('#cf-password').value = '';
    panel.querySelector('#cf-display-name').value = '';
    panel.querySelector('#cf-company-name').value = '';
    panel.querySelector('#cf-phone').value = '';
    panel.querySelector('#cf-password').placeholder = 'Minimum 8 caractères';
    panel.querySelector('#client-form-card').classList.remove('hidden');
    panel.querySelector('#client-form-error').classList.add('hidden');
    panel.querySelector('#cf-email').focus();
  };
  panel.querySelector('#btn-client-form-cancel').onclick = () => {
    panel.querySelector('#client-form-card').classList.add('hidden');
  };
  panel.querySelector('#btn-client-form-save').onclick = async () => {
    const errEl = panel.querySelector('#client-form-error');
    errEl.classList.add('hidden');
    const email = panel.querySelector('#cf-email').value.trim();
    const password = panel.querySelector('#cf-password').value;
    const displayName = panel.querySelector('#cf-display-name').value.trim();
    const companyName = panel.querySelector('#cf-company-name').value.trim();
    const contactPhone = panel.querySelector('#cf-phone').value.trim();

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

      panel.querySelector('#client-form-card').classList.add('hidden');
      showNotification(editingClientId ? 'Client mis à jour' : 'Client créé', 'success');

      const rc = await fetch('/api/admin/portal/clients', { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({}));
      if (rc.ok) renderClientsTable(rc.clients || [], panel);
    } catch { errEl.textContent = 'Erreur réseau'; errEl.classList.remove('hidden'); }
  };

  window.editClient = (id, email, displayName, companyName, phone) => {
    editingClientId = id;
    const subtabBtn = panel.querySelector('[data-subtab="clients"]');
    if (subtabBtn) subtabBtn.click();
    panel.querySelector('#client-form-title').textContent = 'Modifier le compte client';
    panel.querySelector('#cf-email').value = email;
    panel.querySelector('#cf-password').value = '';
    panel.querySelector('#cf-password').placeholder = 'Laisser vide pour ne pas changer';
    panel.querySelector('#cf-display-name').value = displayName;
    panel.querySelector('#cf-company-name').value = companyName;
    panel.querySelector('#cf-phone').value = phone;
    panel.querySelector('#client-form-card').classList.remove('hidden');
    panel.querySelector('#client-form-error').classList.add('hidden');
    panel.querySelector('#cf-email').focus();
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
        if (rc.ok) renderClientsTable(rc.clients || [], panel);
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

function renderFormFieldsList(panel, fields) {
  const container = panel.querySelector('#portal-fields-list');
  if (!container) return;

  container.innerHTML = `
    <div style="display:grid;grid-template-columns:180px 1fr 1fr 70px 80px 60px;gap:6px;font-size:12px;font-weight:600;color:#6b7280;padding:4px 8px;">
      <span>Champ</span><span>Libellé personnalisé</span><span>Aide / placeholder</span><span>Visible</span><span>Obligatoire</span><span>Ordre</span>
    </div>
  `;

  fields.sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).forEach((field, i) => {
    const isCritical = field.critical === true;
    const row = document.createElement('div');
    row.className = 'pff-row';
    row.dataset.fieldId = field.id;
    row.dataset.critical = isCritical ? 'true' : 'false';
    row.style.cssText = `display:grid;grid-template-columns:180px 1fr 1fr 70px 80px 60px;gap:6px;align-items:start;padding:8px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;`;

    const labelPart = document.createElement('div');
    labelPart.style.cssText = 'display:flex;flex-direction:column;gap:2px;';
    labelPart.innerHTML = `
      <span class="pff-label" style="font-size:13px;font-weight:600;color:#374151;">${esc(field.label)}</span>
      <span class="pff-type" style="font-size:11px;color:#9ca3af;">${esc(field.type)}${isCritical ? ' 🔒' : ''}</span>
    `;
    row.appendChild(labelPart);

    const customLabelInput = document.createElement('input');
    customLabelInput.type = 'text';
    customLabelInput.className = 'settings-input pff-custom-label';
    customLabelInput.value = esc(field.customLabel || '');
    customLabelInput.placeholder = field.label;
    customLabelInput.style.fontSize = '12px';
    row.appendChild(customLabelInput);

    const placeholderInput = document.createElement('input');
    placeholderInput.type = 'text';
    placeholderInput.className = 'settings-input pff-placeholder';
    placeholderInput.value = esc(field.placeholder || '');
    placeholderInput.placeholder = 'Texte d\'aide…';
    placeholderInput.style.fontSize = '12px';
    row.appendChild(placeholderInput);

    const visibleLabel = document.createElement('label');
    visibleLabel.style.cssText = 'display:flex;align-items:center;gap:4px;font-size:12px;';
    const visibleCb = document.createElement('input');
    visibleCb.type = 'checkbox';
    visibleCb.className = 'pff-visible';
    visibleCb.checked = field.visible !== false;
    if (isCritical) { visibleCb.checked = true; visibleCb.disabled = true; }
    visibleLabel.appendChild(visibleCb);
    visibleLabel.appendChild(document.createTextNode('Visible'));
    row.appendChild(visibleLabel);

    const reqLabel = document.createElement('label');
    reqLabel.style.cssText = 'display:flex;align-items:center;gap:4px;font-size:12px;';
    const reqCb = document.createElement('input');
    reqCb.type = 'checkbox';
    reqCb.className = 'pff-required';
    reqCb.checked = field.required === true;
    if (isCritical) { reqCb.checked = true; reqCb.disabled = true; }
    reqLabel.appendChild(reqCb);
    reqLabel.appendChild(document.createTextNode('Obligatoire'));
    row.appendChild(reqLabel);

    const orderBtns = document.createElement('div');
    orderBtns.style.cssText = 'display:flex;flex-direction:column;gap:2px;';
    const upBtn = document.createElement('button');
    upBtn.className = 'btn btn-sm';
    upBtn.textContent = '↑';
    upBtn.style.padding = '2px 8px';
    upBtn.type = 'button';
    upBtn.onclick = () => {
      const prev = row.previousElementSibling;
      if (prev && prev.classList.contains('pff-row')) container.insertBefore(row, prev);
    };
    const downBtn = document.createElement('button');
    downBtn.className = 'btn btn-sm';
    downBtn.textContent = '↓';
    downBtn.style.padding = '2px 8px';
    downBtn.type = 'button';
    downBtn.onclick = () => {
      const next = row.nextElementSibling;
      if (next && next.classList.contains('pff-row')) container.insertBefore(next, row);
    };
    orderBtns.appendChild(upBtn);
    orderBtns.appendChild(downBtn);
    row.appendChild(orderBtns);

    container.appendChild(row);
  });
}

function renderClientsTable(cls, panel) {
  const container = panel.querySelector('#clients-list');
  if (!container) return;
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
