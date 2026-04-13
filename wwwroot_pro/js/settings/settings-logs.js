import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsLogs(panel) {
  panel.innerHTML = `
    <h3>Journaux d'activité utilisateurs</h3>
    <div style="display: flex; gap: 8px; margin-bottom: 16px; align-items: center;">
      <input type="date" id="logs-date-filter" class="settings-input" />
      <button id="logs-refresh" class="btn btn-primary">Rafraîchir</button>
    </div>
    <div id="logs-table-container"><p style="color:#9ca3af;">Chargement...</p></div>
  `;

  document.getElementById("logs-refresh").onclick = () => loadSettingsLogs();
  await loadSettingsLogs();
}

export async function loadSettingsLogs() {
  const container = document.getElementById("logs-table-container");
  if (!container) return;

  const dateFilter = document.getElementById("logs-date-filter")?.value || "";
  const url = "/api/admin/activity-logs" + (dateFilter ? `?date=${encodeURIComponent(dateFilter)}` : "");

  try {
    const resp = await fetch(url, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());

    const logs = resp.logs || [];
    if (logs.length === 0) {
      container.innerHTML = '<p style="color:#9ca3af;">Aucune activité enregistrée</p>';
      return;
    }

    container.innerHTML = `
      <table style="width:100%; border-collapse: collapse; font-size: 13px;">
        <thead>
          <tr style="background: #f3f4f6; text-align: left;">
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Date</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Utilisateur</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Action</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Détails</th>
          </tr>
        </thead>
        <tbody>
          ${logs.map(l => `
            <tr style="border-bottom: 1px solid #e5e7eb;">
              <td style="padding: 6px 12px; white-space: nowrap; color: #6b7280;">${new Date(l.timestamp).toLocaleString("fr-FR")}</td>
              <td style="padding: 6px 12px; font-weight: 600;">${l.userName || l.userLogin || "—"}</td>
              <td style="padding: 6px 12px;"><span style="background: #dbeafe; color: #1e40af; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 600;">${l.action || ""}</span></td>
              <td style="padding: 6px 12px; color: #374151;">${l.details || ""}</td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    `;
  } catch (err) {
    container.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}
