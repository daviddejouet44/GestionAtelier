import { authToken, showNotification, esc } from '../core.js';

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
          <option value="4">Profil 4 — Façonnage (lecture seule)</option>
        </select>
        <button id="sa-create" class="btn btn-primary">Créer</button>
      </div>
      <div id="sa-error" style="color: #ef4444; font-size: 13px; display: none;"></div>
    </div>
    <h4>Comptes existants</h4>
    <div id="sa-users-list"></div>
  `;

  document.getElementById("sa-create").onclick = async () => {
    const login = document.getElementById("sa-login").value.trim();
    const password = document.getElementById("sa-password").value.trim();
    const name = document.getElementById("sa-name").value.trim();
    const profile = parseInt(document.getElementById("sa-profile").value);
    const errorEl = document.getElementById("sa-error");

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
      document.getElementById("sa-login").value = "";
      document.getElementById("sa-password").value = "";
      document.getElementById("sa-name").value = "";
      await refreshSettingsUsersList();
    } else {
      errorEl.textContent = r.error || "Erreur";
      errorEl.style.display = "block";
    }
  };

  await refreshSettingsUsersList();
}

export async function refreshSettingsUsersList() {
  const listEl = document.getElementById("sa-users-list");
  if (!listEl) return;
  const resp = await fetch("/api/auth/users", {
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json());
  listEl.innerHTML = "";
  if (resp.ok && resp.users) {
    const profileLabel = { 1: "Soumission", 2: "Opérateur", 3: "Admin", 4: "Façonnage" };
    resp.users.forEach(u => {
      const div = document.createElement("div");
      div.style.cssText = "display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: white; border-radius: 6px; margin-bottom: 6px; border: 1px solid #e5e7eb;";
      div.innerHTML = `
        <div style="flex: 1;">
          <strong>${u.login}</strong> — ${u.name}
          <small style="display: block; color: #6b7280;">Profil ${u.profile} — ${profileLabel[u.profile] || u.profile}</small>
        </div>
        <button class="btn btn-sm" data-id="${u.id}" style="color: #ef4444; border-color: #ef4444;">Supprimer</button>
      `;
      div.querySelector("button").onclick = async () => {
        if (!confirm(`Supprimer l'utilisateur "${u.login}" ?`)) return;
        await fetch(`/api/auth/users/${u.id}`, {
          method: "DELETE",
          headers: { "Authorization": `Bearer ${authToken}` }
        });
        showNotification("✅ Utilisateur supprimé", "success");
        const panel = document.getElementById("settings-panel-accounts");
        if (panel) { panel._loaded = false; await renderSettingsAccounts(panel); }
      };
      listEl.appendChild(div);
    });
  }
}
