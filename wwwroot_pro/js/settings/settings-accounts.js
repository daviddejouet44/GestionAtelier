import { authToken, showNotification, esc } from '../core.js';

const PROFILE_LABELS = { 1: "Soumission", 2: "Opérateur", 3: "Admin", 4: "Finitions", 5: "Lecture plannings", 6: "Opérateur restreint" };

function profileOptions(selected) {
  return [1,2,3,4,5,6].map(p => `<option value="${p}" ${selected === p ? 'selected' : ''}>${p} — ${PROFILE_LABELS[p] || p}</option>`).join("");
}

export async function renderSettingsAccounts(panel) {
  panel.innerHTML = `
    <h3>Gestion des comptes et des rôles</h3>
    <div class="accounts-new-user" style="margin-bottom: 20px;">
      <h4>Créer un nouveau compte</h4>
      <div style="display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px;">
        <input type="text" id="sa-login" placeholder="Login" class="settings-input" />
        <input type="password" id="sa-password" placeholder="Mot de passe" class="settings-input" />
        <input type="text" id="sa-name" placeholder="Nom complet" class="settings-input" />
        <select id="sa-profile" class="settings-input">
          <option value="1">Profil 1 — Soumission</option>
          <option value="2">Profil 2 — Opérateur</option>
          <option value="3">Profil 3 — Admin</option>
          <option value="4">Profil 4 — Finitions (lecture seule)</option>
          <option value="5">Profil 5 — Lecture plannings (lecture seule)</option>
          <option value="6">Profil 6 — Opérateur restreint (sans modif. planning/dates)</option>
        </select>
        <button id="sa-create" class="btn btn-primary">Créer</button>
      </div>
      <div id="sa-error" style="color: #ef4444; font-size: 13px; display: none;"></div>
    </div>
    <h4>Comptes existants</h4>
    <div id="sa-users-list"></div>
    <div id="sa-edit-modal" style="display:none;position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:9999;align-items:center;justify-content:center;">
      <div style="background:white;border-radius:12px;padding:24px;min-width:360px;max-width:480px;box-shadow:0 8px 32px rgba(0,0,0,0.2);">
        <h4 style="margin:0 0 16px;">Modifier le compte</h4>
        <div class="settings-form-group" style="margin-bottom:10px;">
          <label style="font-size:12px;font-weight:600;color:#374151;">Login</label>
          <input type="text" id="sa-edit-login" class="settings-input" style="width:100%;" />
        </div>
        <div class="settings-form-group" style="margin-bottom:10px;">
          <label style="font-size:12px;font-weight:600;color:#374151;">Nom complet</label>
          <input type="text" id="sa-edit-name" class="settings-input" style="width:100%;" />
        </div>
        <div class="settings-form-group" style="margin-bottom:10px;">
          <label style="font-size:12px;font-weight:600;color:#374151;">Profil</label>
          <select id="sa-edit-profile" class="settings-input" style="width:100%;"></select>
        </div>
        <div class="settings-form-group" style="margin-bottom:16px;">
          <label style="font-size:12px;font-weight:600;color:#374151;">Nouveau mot de passe (laisser vide pour ne pas changer)</label>
          <input type="password" id="sa-edit-password" class="settings-input" style="width:100%;" placeholder="Laisser vide = inchangé" />
        </div>
        <div id="sa-edit-error" style="color:#ef4444;font-size:13px;margin-bottom:8px;display:none;"></div>
        <div style="display:flex;gap:10px;justify-content:flex-end;">
          <button id="sa-edit-cancel" class="btn">Annuler</button>
          <button id="sa-edit-save" class="btn btn-primary">Enregistrer</button>
        </div>
      </div>
    </div>
  `;

  panel.querySelector("#sa-create").onclick = async () => {
    const login = panel.querySelector("#sa-login").value.trim();
    const password = panel.querySelector("#sa-password").value.trim();
    const name = panel.querySelector("#sa-name").value.trim();
    const profile = parseInt(panel.querySelector("#sa-profile").value);
    const errorEl = panel.querySelector("#sa-error");

    if (!login || !password || !name) {
      errorEl.textContent = "Tous les champs sont requis";
      errorEl.style.display = "block";
      return;
    }

    const r = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ login, password, name, profile })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      errorEl.style.display = "none";
      showNotification("✅ Compte créé", "success");
      panel.querySelector("#sa-login").value = "";
      panel.querySelector("#sa-password").value = "";
      panel.querySelector("#sa-name").value = "";
      await refreshSettingsUsersList(panel);
    } else {
      errorEl.textContent = r.error || "Erreur";
      errorEl.style.display = "block";
    }
  };

  // Edit modal
  let editUserId = null;
  const modal = panel.querySelector("#sa-edit-modal");
  modal.style.display = "none";

  panel.querySelector("#sa-edit-cancel").onclick = () => { modal.style.display = "none"; editUserId = null; };

  panel.querySelector("#sa-edit-save").onclick = async () => {
    const errEl = panel.querySelector("#sa-edit-error");
    const loginVal = panel.querySelector("#sa-edit-login").value.trim();
    const nameVal = panel.querySelector("#sa-edit-name").value.trim();
    const profileVal = parseInt(panel.querySelector("#sa-edit-profile").value);
    const pwdVal = panel.querySelector("#sa-edit-password").value;
    if (!loginVal || !nameVal) { errEl.textContent = "Login et nom requis"; errEl.style.display = "block"; return; }
    const body = { login: loginVal, name: nameVal, profile: profileVal };
    if (pwdVal.trim()) body.password = pwdVal;
    const r = await fetch(`/api/auth/users/${editUserId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(body)
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
    if (r.ok) {
      modal.style.display = "none";
      editUserId = null;
      showNotification("✅ Compte modifié", "success");
      await refreshSettingsUsersList(panel);
    } else {
      errEl.textContent = r.error || "Erreur";
      errEl.style.display = "block";
    }
  };

  panel._openEditModal = (u) => {
    editUserId = u.id;
    panel.querySelector("#sa-edit-login").value = u.login;
    panel.querySelector("#sa-edit-name").value = u.name;
    panel.querySelector("#sa-edit-profile").innerHTML = profileOptions(u.profile);
    panel.querySelector("#sa-edit-password").value = "";
    panel.querySelector("#sa-edit-error").style.display = "none";
    modal.style.display = "flex";
  };

  await refreshSettingsUsersList(panel);
}

export async function refreshSettingsUsersList(panel) {
  const resolvedPanel = panel || document.getElementById("settings-panel-accounts");
  if (!resolvedPanel) return;
  const listEl = resolvedPanel.querySelector ? resolvedPanel.querySelector("#sa-users-list") : document.getElementById("sa-users-list");
  if (!listEl) return;
  const resp = await fetch("/api/auth/users", {
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json());
  listEl.innerHTML = "";
  if (resp.ok && resp.users) {
    resp.users.forEach(u => {
      const isOnline = !!u.online;
      const dot = isOnline
        ? `<span title="Connecté" style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#22c55e;flex-shrink:0;"></span>`
        : `<span title="Déconnecté" style="display:inline-block;width:10px;height:10px;border-radius:50%;background:#d1d5db;flex-shrink:0;"></span>`;
      const div = document.createElement("div");
      div.style.cssText = "display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: white; border-radius: 6px; margin-bottom: 6px; border: 1px solid #e5e7eb;";
      div.innerHTML = `
        ${dot}
        <div style="flex: 1;">
          <strong>${esc(u.login)}</strong> — ${esc(u.name)}
          <small style="display: block; color: #6b7280;">Profil ${u.profile} — ${PROFILE_LABELS[u.profile] || u.profile}</small>
        </div>
        <button class="btn btn-sm sa-edit-btn" data-id="${esc(u.id)}">Modifier</button>
        <button class="btn btn-sm sa-disconnect-btn" data-id="${esc(u.id)}" data-login="${esc(u.login)}" style="color: #f59e0b; border-color: #f59e0b;" title="Forcer la déconnexion de cet utilisateur">⛔ Déconnecter</button>
        <button class="btn btn-sm sa-delete-btn" data-id="${esc(u.id)}" data-login="${esc(u.login)}" style="color: #ef4444; border-color: #ef4444;">Supprimer</button>
      `;
      div.querySelector(".sa-edit-btn").onclick = () => {
        if (resolvedPanel._openEditModal) resolvedPanel._openEditModal(u);
      };
      div.querySelector(".sa-disconnect-btn").onclick = async () => {
        if (!confirm(`Forcer la déconnexion de "${u.login}" ?`)) return;
        const r = await fetch(`/api/auth/users/${u.id}/force-disconnect`, {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (r.ok) showNotification("✅ Utilisateur déconnecté", "success");
        else showNotification("❌ " + (r.error || "Erreur"), "error");
        const p = resolvedPanel || document.getElementById("settings-panel-accounts");
        if (p) { p._loaded = false; await renderSettingsAccounts(p); }
      };
      div.querySelector(".sa-delete-btn").onclick = async () => {
        if (!confirm(`Supprimer l'utilisateur "${u.login}" ?`)) return;
        await fetch(`/api/auth/users/${u.id}`, {
          method: "DELETE",
          headers: { "Authorization": `Bearer ${authToken}` }
        });
        showNotification("✅ Utilisateur supprimé", "success");
        const p = resolvedPanel || document.getElementById("settings-panel-accounts");
        if (p) { p._loaded = false; await renderSettingsAccounts(p); }
      };
      listEl.appendChild(div);
    });
  }
}
