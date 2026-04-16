// app/dashboard.js — Vue tableau de bord

import { currentUser, authToken } from '../core.js';

export async function initDashboardView() {
  const dashEl = document.getElementById("dashboard");
  dashEl.innerHTML = `
    <div class="settings-container">
      <h2>Dashboard</h2>
      <div id="dashboard-content"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  await loadDashboardData();
}

export async function loadDashboardData() {
  const contentEl = document.getElementById("dashboard-content");
  if (!contentEl) return;

  // Load PrismaSync URL setting
  let prismaSyncUrl = "";
  try {
    const r = await fetch("/api/config/prismasync-url").then(r => r.json()).catch(() => ({}));
    if (r.ok && r.url) prismaSyncUrl = r.url;
  } catch(e) { /* use default */ }

  if (prismaSyncUrl) {
    // Show PrismaSync iframe or link (iframe may be blocked by CSP on some installations)
    contentEl.innerHTML = `
      <div style="display:flex;flex-direction:column;gap:12px;height:calc(100vh - 160px);">
        <div style="display:flex;align-items:center;gap:10px;flex-shrink:0;">
          <span style="font-size:13px;color:var(--text-secondary);">PrismaSync :</span>
          <a href="${prismaSyncUrl}" target="_blank" rel="noopener" style="font-size:13px;color:var(--primary);font-weight:600;">Ouvrir dans un nouvel onglet ↗</a>
          ${currentUser && currentUser.profile === 3 ? `<button id="prismasync-settings-btn" class="btn btn-sm" style="margin-left:auto;">⚙️ Modifier l'URL</button>` : ''}
        </div>
        <iframe
          src="${prismaSyncUrl}"
          style="flex:1;width:100%;border:1px solid var(--border-light);border-radius:var(--radius-lg);background:white;"
          sandbox="allow-same-origin allow-scripts allow-forms allow-popups"
          title="PrismaSync Dashboard"
        ></iframe>
      </div>
    `;
    if (currentUser && currentUser.profile === 3) {
      const settingsBtn = contentEl.querySelector("#prismasync-settings-btn");
      if (settingsBtn) settingsBtn.onclick = () => _showPrismaSyncUrlEditor(contentEl);
    }
  } else if (currentUser && currentUser.profile === 3) {
    // Admin: show URL editor + Prismalytics links
    contentEl.innerHTML = `
      <div style="margin-bottom:24px;background:var(--bg-card);border:1px solid var(--border-light);border-radius:var(--radius-lg);padding:20px;box-shadow:var(--shadow-sm);">
        <h3 style="margin:0 0 8px 0;font-size:15px;font-weight:700;">Dashboard PrismaSync</h3>
        <p style="color:var(--text-secondary);font-size:13px;margin:0 0 14px 0;">Configurez l'URL PrismaSync pour afficher le dashboard directement ici.</p>
        <div style="display:flex;gap:8px;align-items:center;">
          <input type="url" id="prismasync-url-input" placeholder="https://prismasync.example.com/dashboard" class="settings-input" style="flex:1;max-width:500px;" />
          <button id="prismasync-url-save" class="btn btn-primary">Enregistrer</button>
        </div>
      </div>
      <div style="margin-bottom:20px;">
        <p style="color:var(--text-secondary);font-size:13px;margin:0 0 20px 0;">
          Accédez directement aux outils Prismalytics Canon dans un nouvel onglet.
        </p>
        <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:16px;">
          <a href="https://prismalytics-eu.cpp.canon/accounting#" target="_blank" rel="noopener"
             style="display:flex;flex-direction:column;gap:10px;background:var(--bg-card);border:1px solid var(--border-light);border-radius:var(--radius-lg);padding:24px;text-decoration:none;color:inherit;box-shadow:var(--shadow-md);transition:box-shadow 0.2s,transform 0.18s;"
             onmouseover="this.style.boxShadow='var(--shadow-hover)';this.style.transform='translateY(-2px)';"
             onmouseout="this.style.boxShadow='var(--shadow-md)';this.style.transform='';">
            <span style="font-size:36px;">📊</span>
            <strong style="font-size:16px;font-weight:700;color:var(--text-primary);">Accounting</strong>
            <span style="font-size:12px;color:var(--text-secondary);">Suivi des impressions et facturation Canon Prismalytics</span>
            <span style="font-size:12px;color:var(--primary);font-weight:600;margin-top:4px;">Ouvrir ↗</span>
          </a>
          <a href="https://prismalytics-eu.cpp.canon/dashboard#" target="_blank" rel="noopener"
             style="display:flex;flex-direction:column;gap:10px;background:var(--bg-card);border:1px solid var(--border-light);border-radius:var(--radius-lg);padding:24px;text-decoration:none;color:inherit;box-shadow:var(--shadow-md);transition:box-shadow 0.2s,transform 0.18s;"
             onmouseover="this.style.boxShadow='var(--shadow-hover)';this.style.transform='translateY(-2px)';"
             onmouseout="this.style.boxShadow='var(--shadow-md)';this.style.transform='';">
            <span style="font-size:36px;">📈</span>
            <strong style="font-size:16px;font-weight:700;color:var(--text-primary);">Dashboard</strong>
            <span style="font-size:12px;color:var(--text-secondary);">Vue d'ensemble et statistiques de production Prismalytics</span>
            <span style="font-size:12px;color:var(--primary);font-weight:600;margin-top:4px;">Ouvrir ↗</span>
          </a>
        </div>
      </div>
    `;
    const saveBtn = contentEl.querySelector("#prismasync-url-save");
    if (saveBtn) {
      saveBtn.onclick = async () => {
        const url = contentEl.querySelector("#prismasync-url-input").value.trim();
        const r = await fetch("/api/config/prismasync-url", {
          method: "PUT",
          headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
          body: JSON.stringify({ url })
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (r.ok) {
          await loadDashboardData();
        } else {
          alert("Erreur : " + (r.error || ""));
        }
      };
    }
  } else {
    contentEl.innerHTML = `
      <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <div style="background: #fef9c3; border: 1px solid #fbbf24; border-radius: 12px; padding: 30px;">
          <h3 style="margin: 0 0 12px 0; font-size: 20px;">Reporting</h3>
          <p style="color: #92400e; margin: 0 0 8px 0;">À venir</p>
          <p style="color: #6b7280; margin: 0; font-size: 13px;">
            Cette section contiendra les rapports de production, les temps de traitement,
            les statistiques par opérateur et les analyses de performance.
          </p>
        </div>
        <div style="background: #fef9c3; border: 1px solid #fbbf24; border-radius: 12px; padding: 30px;">
          <h3 style="margin: 0 0 12px 0; font-size: 20px;">Presses numériques</h3>
          <p style="color: #92400e; margin: 0 0 8px 0;">À venir</p>
          <p style="color: #6b7280; margin: 0; font-size: 13px;">
            Connexion aux presses numériques pour le suivi en temps réel :
            état des machines, files d'attente, consommation d'encre et alertes.
          </p>
        </div>
      </div>
    `;
  }
}

function _showPrismaSyncUrlEditor(contentEl) {
  const current = contentEl.querySelector("iframe")?.src || "";
  const input = document.createElement("input");
  input.type = "url";
  input.value = current;
  input.className = "settings-input";
  input.style.cssText = "flex:1;max-width:500px;";
  input.placeholder = "https://...";

  const saveBtn = document.createElement("button");
  saveBtn.className = "btn btn-primary";
  saveBtn.textContent = "Enregistrer";
  saveBtn.onclick = async () => {
    const url = input.value.trim();
    const r = await fetch("/api/config/prismasync-url", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ url })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      await loadDashboardData();
    } else {
      alert("Erreur : " + (r.error || ""));
    }
  };

  const row = document.createElement("div");
  row.style.cssText = "position:fixed;bottom:20px;right:20px;z-index:9999;display:flex;gap:8px;align-items:center;background:white;border:1px solid #e5e7eb;border-radius:10px;padding:12px 16px;box-shadow:0 8px 24px rgba(0,0,0,0.15);";
  row.appendChild(Object.assign(document.createElement("span"), { textContent: "URL PrismaSync:", style: "font-size:13px;font-weight:600;" }));
  row.appendChild(input);
  row.appendChild(saveBtn);
  const cancelBtn = document.createElement("button");
  cancelBtn.className = "btn btn-sm";
  cancelBtn.textContent = "Annuler";
  cancelBtn.onclick = () => row.remove();
  row.appendChild(cancelBtn);
  document.body.appendChild(row);
  input.focus();
}
