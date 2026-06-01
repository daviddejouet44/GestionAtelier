// app/dashboard.js — Vue tableau de bord de production

import { currentUser, authToken, esc } from '../core.js';

// Chart instances — kept to destroy before re-rendering
let _charts = [];

function destroyCharts() {
  _charts.forEach(c => { try { c.destroy(); } catch(e) {} });
  _charts = [];
}

export async function initDashboardView() {
  destroyCharts();
  const dashEl = document.getElementById("dashboard");
  dashEl.innerHTML = `
    <div class="settings-container" style="max-width:100%;">
      <div style="display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:10px;margin-bottom:20px;">
        <h2 style="margin:0;">📊 Tableau de bord production</h2>
        <div style="display:flex;gap:8px;flex-wrap:wrap;">
          <input id="dashboard-export-from" type="date" class="settings-input" style="font-size:13px;min-height:34px;" />
          <input id="dashboard-export-to" type="date" class="settings-input" style="font-size:13px;min-height:34px;" />
          <button id="dashboard-refresh-btn" class="btn" style="font-size:13px;">🔄 Actualiser</button>
          <button id="dashboard-export-csv-btn" class="btn btn-primary" style="font-size:13px;">📥 Exporter CSV</button>
        </div>
      </div>
      <div id="dashboard-content"><div style="color:#6b7280;padding:40px;text-align:center;">⏳ Chargement des statistiques...</div></div>
    </div>
  `;

  const refreshBtn = dashEl.querySelector("#dashboard-refresh-btn");
  const exportBtn = dashEl.querySelector("#dashboard-export-csv-btn");

  if (refreshBtn) refreshBtn.onclick = () => loadDashboardData();
  if (exportBtn) exportBtn.onclick = exportCSV;

  await loadDashboardData();
}

async function exportCSV() {
  try {
    const from = document.getElementById("dashboard-export-from")?.value || "";
    const to = document.getElementById("dashboard-export-to")?.value || "";
    const query = new URLSearchParams();
    if (from) query.set("from", from);
    if (to) query.set("to", to);
    const apiUrl = "/api/dashboard/stats/export-csv" + (query.toString() ? `?${query}` : "");

    const a = document.createElement("a");
    a.href = apiUrl;
    const resp = await fetch(apiUrl, {
      headers: { "Authorization": "Bearer " + authToken }
    });
    if (!resp.ok) { alert("Erreur lors de l'export"); return; }
    const blob = await resp.blob();
    const url = URL.createObjectURL(blob);
    const date = new Date().toISOString().split("T")[0];
    a.href = url;
    a.download = `stats-production-${date}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  } catch(e) {
    alert("Erreur d'export : " + e.message);
  }
}

// Colour palette for charts
const PALETTE = [
  "#2563eb","#7c3aed","#db2777","#ea580c","#16a34a",
  "#0891b2","#ca8a04","#dc2626","#9333ea","#0284c7",
  "#65a30d","#d97706","#e11d48","#7c3aed","#0369a1"
];

export async function loadDashboardData() {
  destroyCharts();
  const contentEl = document.getElementById("dashboard-content");
  if (!contentEl) return;

  contentEl.innerHTML = '<div style="color:#6b7280;padding:40px;text-align:center;">⏳ Chargement...</div>';

  let stats = null;
  try {
    const resp = await fetch("/api/dashboard/stats", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok) stats = resp;
  } catch(e) { /* ignore */ }

  if (!stats) {
    contentEl.innerHTML = '<div style="color:#ef4444;padding:40px;text-align:center;">❌ Impossible de charger les statistiques.</div>';
    return;
  }

  const s = stats.summary || {};
  const byFolder = Array.isArray(stats.byFolder) ? stats.byFolder : [];
  const byMoteur = Array.isArray(stats.byMoteur) ? stats.byMoteur : [];
  const byTypeTravail = Array.isArray(stats.byTypeTravail) ? stats.byTypeTravail : [];
  const byProcess = Array.isArray(stats.byProcess) ? stats.byProcess : [];
  const paperConsumption = Array.isArray(stats.paperConsumption) ? stats.paperConsumption : [];
  const byOperateur = Array.isArray(stats.byOperateur) ? stats.byOperateur : [];
  const recentJobs = Array.isArray(stats.recentJobs) ? stats.recentJobs : [];
  const byEnnoblissement = Array.isArray(stats.byEnnoblissement) ? stats.byEnnoblissement : [];
  const byFaconnageBinding = Array.isArray(stats.byFaconnageBinding) ? stats.byFaconnageBinding : [];
  const byRainage = Array.isArray(stats.byRainage) ? stats.byRainage : [];
  const byPlis = Array.isArray(stats.byPlis) ? stats.byPlis : [];
  const bySortie = Array.isArray(stats.bySortie) ? stats.bySortie : [];
  const jobsWithTemps = Array.isArray(stats.jobsWithTemps) ? stats.jobsWithTemps : [];

  const generatedAt = stats.generatedAt
    ? new Date(stats.generatedAt).toLocaleString("fr-FR", { day:"2-digit", month:"2-digit", year:"numeric", hour:"2-digit", minute:"2-digit" })
    : "";

  // ──────────────────────────────────────────────────────
  // KPI Cards
  // ──────────────────────────────────────────────────────
  function kpiCard(icon, value, label, color = "#2563eb", subtext = "") {
    return `<div style="background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:18px 20px;flex:1;min-width:150px;max-width:220px;box-shadow:0 1px 4px rgba(0,0,0,.06);">
      <div style="font-size:26px;margin-bottom:6px;">${icon}</div>
      <div style="font-size:28px;font-weight:800;color:${color};">${value}</div>
      <div style="font-size:12px;color:#6b7280;font-weight:600;text-transform:uppercase;letter-spacing:.04em;margin-top:2px;">${label}</div>
      ${subtext ? `<div style="font-size:11px;color:#9ca3af;margin-top:4px;">${subtext}</div>` : ''}
    </div>`;
  }

  function formatMinutes(totalMinutes) {
    const mins = Math.max(0, Number(totalMinutes) || 0);
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return `${h}h ${m}min`;
  }

  const kpiHtml = `
    <div style="display:flex;flex-wrap:wrap;gap:14px;margin-bottom:14px;">
      ${kpiCard("📄", s.totalActive ?? 0, "Dossiers actifs", "#2563eb", `${s.jobsWithFiche ?? 0} avec fiche`)}
      ${kpiCard("🖨️", s.totalFeuilles ? s.totalFeuilles.toLocaleString("fr-FR") : "0", "Feuilles en cours", "#7c3aed")}
      ${kpiCard("📦", s.totalQuantite ? s.totalQuantite.toLocaleString("fr-FR") : "0", "Exemplaires en cours", "#0891b2")}
      ${kpiCard("⚠️", s.retardsCount ?? 0, "En retard", s.retardsCount > 0 ? "#dc2626" : "#16a34a")}
    </div>
    <div style="background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:16px 18px;box-shadow:0 1px 4px rgba(0,0,0,.06);margin-bottom:24px;">
      <h4 style="margin:0 0 12px;font-size:14px;font-weight:700;color:#1e3a5f;">📅 Planifiés (7j / 30j)</h4>
      <div style="display:grid;grid-template-columns:2fr 1fr 1fr;gap:10px;font-size:13px;align-items:center;">
        <div style="color:#6b7280;font-weight:700;text-transform:uppercase;font-size:11px;">Étape</div>
        <div style="color:#6b7280;font-weight:700;text-transform:uppercase;font-size:11px;text-align:right;">7 jours</div>
        <div style="color:#6b7280;font-weight:700;text-transform:uppercase;font-size:11px;text-align:right;">30 jours</div>

        <div style="font-weight:600;color:#374151;">🖨️ Impression</div>
        <div style="text-align:right;font-weight:800;color:#16a34a;">${s.plannedImpression7 ?? 0}</div>
        <div style="text-align:right;font-weight:800;color:#0284c7;">${s.plannedImpression30 ?? 0}</div>

        <div style="font-weight:600;color:#374151;">✂️ Finitions</div>
        <div style="text-align:right;font-weight:800;color:#16a34a;">${s.plannedFinitions7 ?? 0}</div>
        <div style="text-align:right;font-weight:800;color:#0284c7;">${s.plannedFinitions30 ?? 0}</div>

        <div style="font-weight:600;color:#374151;">🚚 Livraisons</div>
        <div style="text-align:right;font-weight:800;color:#16a34a;">${s.plannedLivraisons7 ?? 0}</div>
        <div style="text-align:right;font-weight:800;color:#0284c7;">${s.plannedLivraisons30 ?? 0}</div>
      </div>
    </div>`;

  // ──────────────────────────────────────────────────────
  // Sections
  // ──────────────────────────────────────────────────────
  const sectionStyle = "background:#fff;border:1px solid #e5e7eb;border-radius:12px;padding:20px;box-shadow:0 1px 4px rgba(0,0,0,.06);";

  // Jobs par étape Kanban (progress bars)
  const kanbanFolders = byFolder
    .filter(f => (f.folder || "").toLowerCase() !== "quotes_pdf")
    .map(f => ({ ...f, displayFolder: (f.folder || "") === "Façonnage" ? "Finitions" : (f.folder || "") }));

  const folderBars = kanbanFolders.length === 0
    ? '<p style="color:#9ca3af;font-size:13px;">Aucun dossier actif.</p>'
    : kanbanFolders
        .sort((a, b) => b.count - a.count)
        .map(f => {
          const pct = Math.round((f.count / Math.max(1, ...kanbanFolders.map(x => x.count))) * 100);
          return `<div style="margin-bottom:10px;">
            <div style="display:flex;justify-content:space-between;font-size:13px;margin-bottom:4px;">
              <span style="font-weight:500;color:#374151;">${esc(f.displayFolder)}</span>
              <span style="color:#6b7280;font-weight:700;">${f.count}</span>
            </div>
            <div style="background:#f3f4f6;border-radius:6px;height:10px;overflow:hidden;">
              <div style="height:100%;width:${pct}%;background:#2563eb;border-radius:6px;transition:width .3s;"></div>
            </div>
          </div>`;
        }).join('');

  // Jobs par processus (process breakdown)
  const processBars = byProcess.length === 0
    ? '<p style="color:#9ca3af;font-size:13px;">Données non disponibles.</p>'
    : byProcess.map((p, i) => {
        const pct = Math.round((p.count / Math.max(1, byProcess.reduce((a, x) => a + x.count, 0))) * 100);
        return `<div style="margin-bottom:10px;">
          <div style="display:flex;justify-content:space-between;font-size:13px;margin-bottom:4px;">
            <span style="font-weight:500;color:#374151;">${esc(p.process)}</span>
            <span style="color:#6b7280;font-weight:700;">${p.count} (${pct}%)</span>
          </div>
          <div style="background:#f3f4f6;border-radius:6px;height:10px;overflow:hidden;">
            <div style="height:100%;width:${pct}%;background:${PALETTE[i % PALETTE.length]};border-radius:6px;"></div>
          </div>
        </div>`;
      }).join('');

  // Recent jobs table
  const recentRows = recentJobs.length === 0
    ? '<tr><td colspan="4" style="text-align:center;color:#9ca3af;padding:12px;">Aucun dossier récent.</td></tr>'
    : recentJobs.map(j => `<tr>
        <td style="padding:6px 10px;font-size:12px;color:#374151;">${j.numeroDossier ? `#${esc(j.numeroDossier)}` : esc(j.fileName)}</td>
        <td style="padding:6px 10px;font-size:12px;color:#6b7280;">${esc(j.client || '—')}</td>
        <td style="padding:6px 10px;font-size:12px;"><span style="background:#eff6ff;color:#1d4ed8;padding:2px 8px;border-radius:4px;font-size:11px;">${esc(j.folder)}</span></td>
        <td style="padding:6px 10px;font-size:12px;color:#9ca3af;">${new Date(j.modified).toLocaleString("fr-FR", {day:"2-digit",month:"2-digit",hour:"2-digit",minute:"2-digit"})}</td>
      </tr>`).join('');

  // ──────────────────────────────────────────────────────
  // Operator table
  // ──────────────────────────────────────────────────────
  const opRows = byOperateur.length === 0
    ? '<tr><td colspan="2" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : byOperateur.map(o => `<tr>
        <td style="padding:5px 10px;font-size:13px;color:#374151;">${esc(o.operateur)}</td>
        <td style="padding:5px 10px;font-size:13px;font-weight:700;color:#2563eb;text-align:right;">${o.count}</td>
      </tr>`).join('');

  const ennoblissementRows = byEnnoblissement.length === 0
    ? '<tr><td colspan="2" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : byEnnoblissement.map(i => `<tr><td style="padding:5px 10px;font-size:13px;color:#374151;">${esc(i.value)}</td><td style="padding:5px 10px;font-size:13px;text-align:right;font-weight:700;color:#1d4ed8;">${i.count}</td></tr>`).join('');

  const bindingRows = byFaconnageBinding.length === 0
    ? '<tr><td colspan="2" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : byFaconnageBinding.map(i => `<tr><td style="padding:5px 10px;font-size:13px;color:#374151;">${esc(i.value)}</td><td style="padding:5px 10px;font-size:13px;text-align:right;font-weight:700;color:#1d4ed8;">${i.count}</td></tr>`).join('');

  const plisRows = byPlis.length === 0
    ? '<tr><td colspan="2" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : byPlis.map(i => `<tr><td style="padding:5px 10px;font-size:13px;color:#374151;">${esc(i.value)}</td><td style="padding:5px 10px;font-size:13px;text-align:right;font-weight:700;color:#1d4ed8;">${i.count}</td></tr>`).join('');

  const sortieRows = bySortie.length === 0
    ? '<tr><td colspan="2" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : bySortie.map(i => `<tr><td style="padding:5px 10px;font-size:13px;color:#374151;">${esc(i.value)}</td><td style="padding:5px 10px;font-size:13px;text-align:right;font-weight:700;color:#1d4ed8;">${i.count}</td></tr>`).join('');

  const rainageValue = byRainage.length > 0 ? (byRainage[0].count || 0) : 0;

  const tempsRows = jobsWithTemps.length === 0
    ? '<tr><td colspan="4" style="text-align:center;color:#9ca3af;padding:12px;">Aucune donnée.</td></tr>'
    : jobsWithTemps.map(j => `<tr>
        <td style="padding:6px 10px;font-size:12px;color:#374151;">${j.numeroDossier ? `#${esc(j.numeroDossier)}` : esc(j.fileName)}</td>
        <td style="padding:6px 10px;font-size:12px;color:#6b7280;">${esc(j.client || '—')}</td>
        <td style="padding:6px 10px;font-size:12px;color:#374151;">${esc(j.moteurImpression || '—')}</td>
        <td style="padding:6px 10px;font-size:12px;color:#1d4ed8;font-weight:700;text-align:right;">${formatMinutes(j.tempsProduitMinutes || 0)}</td>
      </tr>`).join('');

  // ──────────────────────────────────────────────────────
  // HTML layout
  // ──────────────────────────────────────────────────────
  contentEl.innerHTML = `
    ${kpiHtml}

    <!-- Row 1: Moteur + Type de travail charts -->
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px;margin-bottom:16px;">
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">🖨️ Jobs par moteur d'impression</h4>
        <div style="position:relative;height:220px;"><canvas id="chart-moteur"></canvas></div>
      </div>
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">📋 Types de travail</h4>
        <div style="position:relative;height:220px;"><canvas id="chart-type-travail"></canvas></div>
      </div>
    </div>

    <!-- Row 2: Consommation papier + Process -->
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px;margin-bottom:16px;">
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">📄 Consommation papier (feuilles par média)</h4>
        <div style="position:relative;height:240px;"><canvas id="chart-papier"></canvas></div>
      </div>
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">⚙️ Process d'impression</h4>
        ${processBars}
        <hr style="border:none;border-top:1px solid #f3f4f6;margin:16px 0 10px;">
        <h4 style="margin:0 0 12px;font-size:14px;font-weight:700;color:#1e3a5f;">👤 Jobs par opérateur</h4>
        <table style="width:100%;border-collapse:collapse;">
          <tbody>${opRows}</tbody>
        </table>
      </div>
    </div>

    <!-- Row 3: Jobs par étape + Dossiers récents -->
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px;margin-bottom:16px;">
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">📁 Jobs par étape Kanban</h4>
        ${folderBars}
      </div>
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">🕐 Dossiers récemment modifiés</h4>
        <table style="width:100%;border-collapse:collapse;">
          <thead><tr>
            <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Dossier</th>
            <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Client</th>
            <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Étape</th>
            <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Modifié</th>
          </tr></thead>
          <tbody>${recentRows}</tbody>
        </table>
      </div>
    </div>



    <!-- Row 4: Reporting finitions -->
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px;margin-bottom:16px;">
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">✂️ Reporting Finitions — Ennoblissements</h4>
        <table style="width:100%;border-collapse:collapse;"><tbody>${ennoblissementRows}</tbody></table>
      </div>
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">📚 Façonnages / Reliures</h4>
        <table style="width:100%;border-collapse:collapse;"><tbody>${bindingRows}</tbody></table>
      </div>
      <div style="${sectionStyle}">
        <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">📊 Rainage / Plis / Sortie</h4>
        <div style="margin-bottom:10px;font-size:13px;"><strong>Rainage :</strong> <span style="color:#1d4ed8;font-weight:700;">${rainageValue}</span></div>
        <h5 style="margin:8px 0;font-size:13px;color:#374151;">Plis</h5>
        <table style="width:100%;border-collapse:collapse;margin-bottom:10px;"><tbody>${plisRows}</tbody></table>
        <h5 style="margin:8px 0;font-size:13px;color:#374151;">Sortie</h5>
        <table style="width:100%;border-collapse:collapse;"><tbody>${sortieRows}</tbody></table>
      </div>
    </div>

    <!-- Row 5: Temps production -->
    <div style="${sectionStyle};margin-bottom:16px;">
      <h4 style="margin:0 0 14px;font-size:14px;font-weight:700;color:#1e3a5f;">⏱️ Temps de production par job</h4>
      <div style="display:flex;flex-wrap:wrap;gap:12px;margin-bottom:12px;">
        ${kpiCard("⏱️", formatMinutes(stats.totalTempsMinutes || 0), "Total temps", "#2563eb")}
        ${kpiCard("📈", formatMinutes(stats.avgTempsMinutes || 0), "Moyenne", "#7c3aed")}
      </div>
      <table style="width:100%;border-collapse:collapse;">
        <thead><tr>
          <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">N° dossier</th>
          <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Client</th>
          <th style="text-align:left;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Moteur</th>
          <th style="text-align:right;padding:6px 10px;font-size:11px;background:#f9fafb;color:#6b7280;font-weight:600;text-transform:uppercase;">Temps</th>
        </tr></thead>
        <tbody>${tempsRows}</tbody>
      </table>
    </div>

    ${generatedAt ? `<p style="text-align:right;font-size:11px;color:#d1d5db;margin:4px 0 0;">Généré le ${generatedAt}</p>` : ''}
  `;

  // ──────────────────────────────────────────────────────
  // Build charts with Chart.js
  // ──────────────────────────────────────────────────────
  if (!window.Chart) return; // Chart.js not loaded

  const chartDefaults = {
    plugins: {
      legend: { display: false }
    },
    responsive: true,
    maintainAspectRatio: false,
  };

  // Bar: Jobs par moteur
  const moteurCanvas = document.getElementById("chart-moteur");
  if (moteurCanvas && byMoteur.length > 0) {
    const c = new window.Chart(moteurCanvas, {
      type: "bar",
      data: {
        labels: byMoteur.map(m => m.moteur),
        datasets: [{
          label: "Dossiers",
          data: byMoteur.map(m => m.count),
          backgroundColor: PALETTE.slice(0, byMoteur.length),
          borderRadius: 6,
          borderSkipped: false
        }]
      },
      options: {
        ...chartDefaults,
        plugins: {
          ...chartDefaults.plugins,
          tooltip: {
            callbacks: {
              afterLabel: (ctx) => {
                const m = byMoteur[ctx.dataIndex];
                return m.totalFeuilles > 0 ? `${m.totalFeuilles.toLocaleString("fr-FR")} feuilles` : "";
              }
            }
          }
        },
        scales: {
          x: { ticks: { font: { size: 11 }, maxRotation: 30 } },
          y: { beginAtZero: true, ticks: { stepSize: 1 } }
        }
      }
    });
    _charts.push(c);
  } else if (moteurCanvas) {
    moteurCanvas.parentElement.innerHTML = '<p style="color:#9ca3af;font-size:13px;text-align:center;padding:60px 0;">Aucune donnée</p>';
  }

  // Doughnut: Types de travail
  const typeCanvas = document.getElementById("chart-type-travail");
  if (typeCanvas && byTypeTravail.length > 0) {
    const c = new window.Chart(typeCanvas, {
      type: "doughnut",
      data: {
        labels: byTypeTravail.map(t => t.type),
        datasets: [{
          data: byTypeTravail.map(t => t.count),
          backgroundColor: PALETTE.slice(0, byTypeTravail.length),
          borderWidth: 2,
          borderColor: "#fff"
        }]
      },
      options: {
        ...chartDefaults,
        plugins: {
          legend: {
            display: true,
            position: "bottom",
            labels: { font: { size: 11 }, boxWidth: 14, padding: 8 }
          }
        },
        cutout: "55%"
      }
    });
    _charts.push(c);
  } else if (typeCanvas) {
    typeCanvas.parentElement.innerHTML = '<p style="color:#9ca3af;font-size:13px;text-align:center;padding:60px 0;">Aucune donnée</p>';
  }

  // Bar (horizontal): Consommation papier
  const papierCanvas = document.getElementById("chart-papier");
  if (papierCanvas && paperConsumption.length > 0) {
    const topPapers = paperConsumption.slice(0, 10);
    const c = new window.Chart(papierCanvas, {
      type: "bar",
      data: {
        labels: topPapers.map(p => p.papier),
        datasets: [{
          label: "Feuilles",
          data: topPapers.map(p => p.totalFeuilles),
          backgroundColor: PALETTE.slice(0, topPapers.length),
          borderRadius: 4,
          borderSkipped: false
        }]
      },
      options: {
        ...chartDefaults,
        indexAxis: "y",
        plugins: {
          ...chartDefaults.plugins,
          tooltip: {
            callbacks: {
              afterLabel: (ctx) => {
                const p = topPapers[ctx.dataIndex];
                return `${p.jobCount} dossier(s)`;
              }
            }
          }
        },
        scales: {
          x: { beginAtZero: true },
          y: { ticks: { font: { size: 11 } } }
        }
      }
    });
    _charts.push(c);
  } else if (papierCanvas) {
    papierCanvas.parentElement.innerHTML = '<p style="color:#9ca3af;font-size:13px;text-align:center;padding:60px 0;">Aucun média enregistré dans les fiches</p>';
  }
}
