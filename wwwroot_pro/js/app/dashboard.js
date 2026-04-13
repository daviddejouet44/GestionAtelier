// app/dashboard.js — Vue tableau de bord

import { currentUser } from '../core.js';

export async function initDashboardView() {
  const dashEl = document.getElementById("dashboard");
  dashEl.innerHTML = `
    <div class="settings-container">
      <h2>Dashboard — Vue d'ensemble de l'atelier</h2>
      <div id="dashboard-content"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  await loadDashboardData();
}

export async function loadDashboardData() {
  const contentEl = document.getElementById("dashboard-content");
  if (!contentEl) return;

  if (currentUser && currentUser.profile === 3) {
    // Admin: show Prismalytics direct access links (iframes blocked by CSP frame-ancestors)
    contentEl.innerHTML = `
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
