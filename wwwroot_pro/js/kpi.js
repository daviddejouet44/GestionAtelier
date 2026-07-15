// kpi.js — KPI de production (point 9)
import { authToken, esc } from './core.js';

let _charts = [];

function _fmtMin(min) {
  min = Math.round(min || 0);
  if (min < 60) return `${min} min`;
  const h = Math.floor(min / 60), m = min % 60;
  return m ? `${h} h ${m}` : `${h} h`;
}
function _isoDay(d) { return d.toISOString().slice(0, 10); }

export async function initKpiView() {
  const container = document.getElementById("kpi-view");
  if (!container) return;
  const today = new Date();
  const from = new Date(today.getTime() - 29 * 86400000);

  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px;flex-wrap:wrap;gap:10px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);">📊 KPI de production</h2>
        <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">
          <label style="font-size:12px;color:#6b7280;">Du <input type="date" id="kpi-from" value="${_isoDay(from)}" class="settings-input" style="font-size:13px;padding:4px 8px;"/></label>
          <label style="font-size:12px;color:#6b7280;">au <input type="date" id="kpi-to" value="${_isoDay(today)}" class="settings-input" style="font-size:13px;padding:4px 8px;"/></label>
          <button id="kpi-apply" class="btn btn-primary" style="border-radius:50px;">Appliquer</button>
        </div>
      </div>
      <div id="kpi-cards" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px;margin-bottom:16px;"></div>
      <div id="kpi-charts" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(320px,1fr));gap:16px;"></div>
      <div id="kpi-occupation" style="margin-top:16px;"></div>
    </div>`;

  document.getElementById("kpi-apply").onclick = () => loadKpi();
  await loadKpi();
}

function _destroyCharts() { _charts.forEach(c => { try { c.destroy(); } catch (e) {} }); _charts = []; }

async function loadKpi() {
  const cards = document.getElementById("kpi-cards");
  const charts = document.getElementById("kpi-charts");
  if (!cards || !charts) return;
  const from = document.getElementById("kpi-from").value;
  const to = document.getElementById("kpi-to").value;
  cards.innerHTML = '<p style="color:#6b7280;">Chargement…</p>';
  _destroyCharts();
  charts.innerHTML = "";

  let d;
  try {
    d = await fetch(`/api/kpi?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
  } catch (e) { cards.innerHTML = '<p style="color:#dc2626;">Erreur de chargement</p>'; return; }
  if (!d.ok) { cards.innerHTML = `<p style="color:#dc2626;">${esc(d.error || "Erreur")}</p>`; return; }

  renderCards(d.summary || {});
  renderCharts(d);
  renderOccupation(d.machineOccupation || []);
}

function card(label, value, sub, color) {
  return `<div style="background:#fff;border:1px solid #e5e7eb;border-radius:10px;padding:11px 14px;">
    <div style="font-size:12px;color:#6b7280;">${esc(label)}</div>
    <div style="font-size:22px;font-weight:700;color:${color || '#111827'};">${value}</div>
    ${sub ? `<div style="font-size:11px;color:#9ca3af;">${esc(sub)}</div>` : ''}
  </div>`;
}

function renderCards(s) {
  const el = document.getElementById("kpi-cards");
  if (!el) return;
  el.innerHTML =
    card("OF produits", s.ofCount ?? 0, `du ${s.from} au ${s.to}`) +
    card("Impressions (feuilles)", (s.totalFeuilles ?? 0).toLocaleString('fr-FR')) +
    card("Temps moyen / OF", _fmtMin(s.avgTempsMinutes), `total ${_fmtMin(s.totalTempsMinutes)}`) +
    card("Disponibilité", (s.disponibilitePct ?? 0) + " %", "composante du TRS", "#16a34a") +
    card("Taux d'occupation", (s.occupationPct ?? 0) + " %", "temps en impression", "#2563eb") +
    card("Temps perdu (arrêts)", _fmtMin(s.tempsPerduMinutes), "pannes + maintenance", "#dc2626") +
    card("BAT refusés", s.batRefuses ?? 0, "sur la période", (s.batRefuses ? "#b45309" : "#111827"));
}

function chartBox(title, id) {
  return `<div style="background:#fff;border:1px solid #e5e7eb;border-radius:10px;padding:12px 14px;">
    <div style="font-size:13px;font-weight:700;color:#1e3a5f;margin-bottom:8px;">${esc(title)}</div>
    <div style="position:relative;height:240px;"><canvas id="${id}"></canvas></div>
  </div>`;
}

function renderCharts(d) {
  const el = document.getElementById("kpi-charts");
  if (!el || !window.Chart) { if (el) el.innerHTML = '<p style="color:#9ca3af;">Chart.js indisponible.</p>'; return; }
  const byDay = d.byDay || [], byMachine = d.byMachine || [], byOperateur = d.byOperateur || [], causes = d.causesArret || [];

  el.innerHTML =
    chartBox("Production par jour", "kpi-c-day") +
    chartBox("Feuilles par machine", "kpi-c-machine") +
    chartBox("Temps par opérateur", "kpi-c-op") +
    chartBox("Causes d'arrêt (temps)", "kpi-c-causes");

  // Production par jour : feuilles (barres) + OF (ligne, axe secondaire)
  _charts.push(new Chart(document.getElementById("kpi-c-day"), {
    data: {
      labels: byDay.map(x => x.day.slice(5)),
      datasets: [
        { type: 'bar', label: 'Feuilles', data: byDay.map(x => x.feuilles), backgroundColor: '#93c5fd', yAxisID: 'y' },
        { type: 'line', label: 'OF', data: byDay.map(x => x.ofCount), borderColor: '#7c3aed', backgroundColor: '#7c3aed', yAxisID: 'y1', tension: .3 }
      ]
    },
    options: { responsive: true, maintainAspectRatio: false,
      scales: { y: { beginAtZero: true, position: 'left' }, y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false } } } }
  }));

  _charts.push(new Chart(document.getElementById("kpi-c-machine"), {
    type: 'bar',
    data: { labels: byMachine.map(x => x.moteur), datasets: [{ label: 'Feuilles', data: byMachine.map(x => x.feuilles), backgroundColor: '#2563eb' }] },
    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } }
  }));

  _charts.push(new Chart(document.getElementById("kpi-c-op"), {
    type: 'bar',
    data: { labels: byOperateur.map(x => x.operateur), datasets: [{ label: 'Heures', data: byOperateur.map(x => +(x.tempsMinutes / 60).toFixed(1)), backgroundColor: '#16a34a' }] },
    options: { indexAxis: 'y', responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { x: { beginAtZero: true } } }
  }));

  const causeCanvas = document.getElementById("kpi-c-causes");
  if (causes.length === 0) {
    causeCanvas.parentElement.innerHTML = '<p style="color:#9ca3af;font-size:13px;padding-top:80px;text-align:center;">Aucun arrêt sur la période 🟢</p>';
  } else {
    _charts.push(new Chart(causeCanvas, {
      type: 'doughnut',
      data: { labels: causes.map(x => x.cause), datasets: [{ data: causes.map(x => x.minutes), backgroundColor: ['#dc2626', '#f59e0b', '#7c3aed', '#0891b2', '#65a30d', '#db2777'] }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom', labels: { font: { size: 11 } } },
        tooltip: { callbacks: { label: c => `${c.label}: ${_fmtMin(c.parsed)}` } } } }
    }));
  }
}

function renderOccupation(rows) {
  const el = document.getElementById("kpi-occupation");
  if (!el) return;
  if (rows.length === 0) { el.innerHTML = ""; return; }
  el.innerHTML = `<div style="background:#fff;border:1px solid #e5e7eb;border-radius:10px;padding:12px 14px;">
    <div style="font-size:13px;font-weight:700;color:#1e3a5f;margin-bottom:8px;">Disponibilité & occupation par machine</div>
    <table style="width:100%;border-collapse:collapse;font-size:13px;">
      <thead><tr style="text-align:left;color:#6b7280;border-bottom:1px solid #e5e7eb;">
        <th style="padding:6px 8px;">Machine</th><th style="padding:6px 8px;">Disponibilité</th>
        <th style="padding:6px 8px;">Occupation</th><th style="padding:6px 8px;">Temps d'arrêt</th><th style="padding:6px 8px;">Causes</th></tr></thead>
      <tbody>${rows.map(r => `<tr style="border-bottom:1px solid #f1f5f9;">
        <td style="padding:7px 8px;font-weight:600;">${esc(r.moteur)}</td>
        <td style="padding:7px 8px;color:${r.disponibilitePct >= 90 ? '#16a34a' : (r.disponibilitePct >= 70 ? '#b45309' : '#dc2626')};font-weight:700;">${r.disponibilitePct} %</td>
        <td style="padding:7px 8px;">${r.occupationPct} %</td>
        <td style="padding:7px 8px;color:${r.downMinutes ? '#dc2626' : '#6b7280'};">${_fmtMin(r.downMinutes)}</td>
        <td style="padding:7px 8px;color:#6b7280;">${(r.causes || []).map(c => esc(c.cause) + ' (' + _fmtMin(c.minutes) + ')').join(', ') || '—'}</td>
      </tr>`).join('')}</tbody>
    </table>
    <div style="font-size:11px;color:#9ca3af;margin-top:8px;">Disponibilité = temps hors panne/maintenance ÷ temps de la période. Le TRS complet (× performance × qualité) nécessite la cadence réelle et le taux de rebut par machine.</div>
  </div>`;
}
