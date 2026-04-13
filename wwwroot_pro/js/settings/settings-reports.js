// settings-reports.js — Paramètres des rapports quotidiens
import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsReports(panel) {
  panel.innerHTML = `<h3>Rapports quotidiens</h3><p style="color:#6b7280;">Chargement...</p>`;

  let config = { enabled: false, reportHour: 18, reportMinute: 0, reportPath: "" };
  let history = [];

  try {
    const r = await fetch("/api/settings/report-config", {
      headers: { "Authorization": "Bearer " + authToken }
    });
    if (r.ok) {
      const d = await r.json();
      if (d.ok && d.config) config = d.config;
    }
  } catch (e) { /* use defaults */ }

  try {
    const r2 = await fetch("/api/reports/history", {
      headers: { "Authorization": "Bearer " + authToken }
    });
    if (r2.ok) {
      const d2 = await r2.json();
      if (d2.ok && Array.isArray(d2.items)) history = d2.items;
    }
  } catch (e) { /* ignore */ }

  const historyHtml = history.length === 0
    ? `<p style="color:#9ca3af;font-size:13px;">Aucun rapport généré</p>`
    : `<table style="width:100%;font-size:12px;border-collapse:collapse;">
        <thead><tr style="background:#f3f4f6;">
          <th style="padding:6px 10px;text-align:left;">Date</th>
          <th style="padding:6px 10px;text-align:left;">Rapport machines</th>
          <th style="padding:6px 10px;text-align:left;">Rapport finitions</th>
        </tr></thead>
        <tbody>
        ${history.map(h => {
          const dt = h.generatedAt ? new Date(h.generatedAt).toLocaleString('fr-FR') : '—';
          return `<tr style="border-top:1px solid #e5e7eb;">
            <td style="padding:5px 10px;">${esc(dt)}</td>
            <td style="padding:5px 10px;font-size:11px;color:#6b7280;">${esc(h.machinesReport || '—')}</td>
            <td style="padding:5px 10px;font-size:11px;color:#6b7280;">${esc(h.finitionsReport || '—')}</td>
          </tr>`;
        }).join('')}
        </tbody>
      </table>`;

  panel.innerHTML = `
    <h3>Rapports quotidiens</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:20px;">
      Génère automatiquement chaque soir deux fichiers CSV : charge par machine et charge finitions.
    </p>

    <div style="max-width:600px;display:flex;flex-direction:column;gap:16px;">
      <label style="display:flex;align-items:center;gap:10px;font-size:14px;">
        <input type="checkbox" id="report-enabled" ${config.enabled ? 'checked' : ''}>
        Activer la génération automatique
      </label>

      <div>
        <label style="font-size:13px;font-weight:600;display:block;margin-bottom:4px;">Heure de génération</label>
        <div style="display:flex;align-items:center;gap:6px;">
          <input type="number" id="report-hour" value="${config.reportHour ?? 18}" min="0" max="23" style="width:60px;padding:6px;" class="settings-input">
          <span>h</span>
          <input type="number" id="report-minute" value="${config.reportMinute ?? 0}" min="0" max="59" style="width:60px;padding:6px;" class="settings-input">
        </div>
      </div>

      <div>
        <label style="font-size:13px;font-weight:600;display:block;margin-bottom:4px;">Dossier de sortie</label>
        <input type="text" id="report-path" value="${esc(config.reportPath || '')}" placeholder="Ex: C:\\Rapports" style="width:100%;padding:8px;" class="settings-input">
        <p style="font-size:11px;color:#9ca3af;margin-top:4px;">Laisser vide pour utiliser le dossier temporaire du système</p>
      </div>

      <div style="display:flex;gap:10px;flex-wrap:wrap;">
        <button id="report-save" class="btn btn-primary">💾 Enregistrer</button>
        <button id="report-generate-now" class="btn" style="background:#7c3aed;color:#fff;">⚡ Générer maintenant</button>
      </div>

      <div id="report-msg" style="font-size:13px;min-height:20px;"></div>

      <div>
        <h4 style="font-size:14px;margin-bottom:8px;">Historique des rapports</h4>
        <div id="report-history">${historyHtml}</div>
      </div>
    </div>
  `;

  panel.querySelector("#report-save").onclick = async () => {
    const msg = panel.querySelector("#report-msg");
    const cfg = {
      enabled:      panel.querySelector("#report-enabled").checked,
      reportHour:   parseInt(panel.querySelector("#report-hour").value) || 18,
      reportMinute: parseInt(panel.querySelector("#report-minute").value) || 0,
      reportPath:   panel.querySelector("#report-path").value.trim()
    };
    try {
      const r = await fetch("/api/settings/report-config", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": "Bearer " + authToken },
        body: JSON.stringify(cfg)
      });
      const d = await r.json();
      if (d.ok) {
        msg.style.color = "#16a34a";
        msg.textContent = "✅ Configuration enregistrée";
        panel._loaded = false; // force reload on next open
      } else {
        msg.style.color = "#dc2626";
        msg.textContent = "❌ " + (d.error || "Erreur");
      }
    } catch (e) {
      msg.style.color = "#dc2626";
      msg.textContent = "❌ Erreur réseau";
    }
  };

  panel.querySelector("#report-generate-now").onclick = async () => {
    const msg = panel.querySelector("#report-msg");
    msg.style.color = "#6b7280";
    msg.textContent = "⏳ Génération en cours…";
    try {
      const r = await fetch("/api/reports/generate-now", {
        method: "POST",
        headers: { "Authorization": "Bearer " + authToken }
      });
      const d = await r.json();
      if (d.ok) {
        msg.style.color = "#16a34a";
        msg.textContent = "✅ Rapports générés";
        // Reload history
        panel._loaded = false;
        await renderSettingsReports(panel);
      } else {
        msg.style.color = "#dc2626";
        msg.textContent = "❌ " + (d.error || "Erreur");
      }
    } catch (e) {
      msg.style.color = "#dc2626";
      msg.textContent = "❌ Erreur réseau";
    }
  };
}
