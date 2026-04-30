// app.js — Point d'entrée minimal (ES6 module)
// Importe tous les modules, orchestre l'initialisation et gère l'état partagé.

import {
  currentUser, authToken, deliveriesByPath, assignmentsByPath,
  setCurrentUser, setAuthToken,
  FOLDER_SOUMISSION, FOLDER_DEBUT_PRODUCTION, FOLDER_FIN_PRODUCTION, FIN_PROD_FOLDER,
  formatDateTime, isLight, fnKey, normalizePath, sanitizeFolderName,
  fmtBytes, daysDiffFromToday, darkenColor, showNotification,
  loadDeliveries, loadAssignments,
  initLogin, logout
} from './core.js';

import { buildKanban, refreshKanban, openAssignDropdown } from './kanban.js';
import { openFabrication, initFabrication } from './fabrication.js';
import { initCalendar, ensureCalendar, calendar, submissionCalendar, initSubmissionCalendar, colorForEvent, openPlanificationCalendar, refreshOperatorView } from './calendar.js';
import { initDossiersView, loadDossiersList, openDossierDetail } from './dossiers.js';
import { initSettingsView } from './settings.js';
import { pollNotifications, initNotificationBell } from './notifications.js';
import { initGlobalProductionView, refreshProductionViewKanban, buildProductionView } from './production-view.js';
import { STAGE_PROGRESS, STAGE_DISPLAY_LABELS } from './constants.js';

import { hideAllViews, showDossiers, showSettings, showGlobalProduction } from './app/navigation.js';
import { initDashboardView } from './app/dashboard.js';

// ======================================================
// DOM REFS
// ======================================================
const kanbanDiv = document.getElementById("kanban");
const calendarEl = document.getElementById("calendar");
const globalAlert = document.getElementById("global-alert");
const searchInput = document.getElementById("searchInput");
const sortBy = document.getElementById("sortBy");

let submissionJobs = [];

// ======================================================
// CALLBACKS GLOBAUX (injection de dépendances cross-module)
// ======================================================
window._openFabrication = openFabrication;
window._refreshKanban = refreshKanban;
window._buildKanban = buildKanban;
window._refreshSubmissionView = refreshSubmissionView;
window._loadDeliveries = loadDeliveries;
window._loadAssignments = loadAssignments;
window.buildBatView = buildBatView;
window._updateGlobalAlert = updateGlobalAlert;
window._renderPdfThumbnail = renderPdfThumbnail;
window._deleteFile = deleteFile;
window._handleDesktopDrop = handleDesktopDrop;
window._openPlanificationCalendar = openPlanificationCalendar;
window._refreshOperatorView = () => refreshOperatorView().catch(() => {});

// ======================================================
// UTILITAIRE — ALERTE GLOBALE
// ======================================================
async function updateGlobalAlert() {
  if (!globalAlert) return;

  const esc = (s) => (s || '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');

  // Production delay alerts are only relevant when on the Production (kanban) tab
  const isProductionTabActive = !document.getElementById("kanban-layout")?.classList.contains("hidden");

  try {
    const [delayResp, batResp] = await Promise.all([
      isProductionTabActive
        ? fetch("/api/alerts/production-delay").then(r => r.json()).catch(() => ({ ok: false, groups: [] }))
        : Promise.resolve({ ok: false, groups: [] }),
      fetch("/api/alerts/bat-pending").then(r => r.json()).catch(() => [])
    ]);

    const delayGroups = (delayResp.ok && Array.isArray(delayResp.groups)) ? delayResp.groups : [];
    const batAlerts = Array.isArray(batResp) ? batResp : [];

    let html = "";

    // Production delay alerts grouped by machine
    if (delayGroups.length > 0) {
      const groupsHtml = delayGroups.map(g => {
        const jobsHtml = g.jobs.map(j => {
          const dossier = j.numeroDossier ? `#${esc(j.numeroDossier)}` : esc(j.fileName);
          const retard = j.retardJours === 1 ? "1 jour" : `${j.retardJours} jours`;
          const titleAttr = esc(j.fileName) + (j.nomClient ? ' — ' + esc(j.nomClient) : '');
          return `<span style="display:inline-block;background:#fef2f2;border:1px solid #fecaca;border-radius:4px;padding:1px 7px;font-size:11px;margin:1px 2px;color:#b91c1c;" title="${titleAttr}">⏰ ${dossier} (${retard})</span>`;
        }).join('');
        return `<div style="display:flex;align-items:flex-start;gap:6px;flex-wrap:wrap;"><span style="font-size:11px;font-weight:700;color:#7f1d1d;white-space:nowrap;padding-top:2px;">🖨️ ${esc(g.moteur)} :</span>${jobsHtml}</div>`;
      }).join('');
      // Admin purge button — cleans up orphan records in one click
      const purgeBtn = (currentUser?.profile === 3)
        ? `<button id="alert-purge-orphans-btn" style="font-size:11px;padding:2px 8px;margin-left:8px;cursor:pointer;background:#b91c1c;color:#fff;border:none;border-radius:4px;" title="Supprimer les alertes fantômes (admin)">🗑 Purger</button>`
        : '';
      html += `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:8px 12px;margin-bottom:6px;">
        <div style="display:flex;align-items:center;flex-wrap:wrap;gap:4px;">
          <span style="font-size:12px;font-weight:700;color:#b91c1c;">⚠️ Retard de production</span>${purgeBtn}
        </div>
        <div style="margin-top:4px;display:flex;flex-direction:column;gap:4px;">${groupsHtml}</div>
      </div>`;
    }

    // BAT pending alerts
    if (batAlerts.length > 0) {
      const batHtml = batAlerts.map(a => {
        const dossier = a.numeroDossier ? ` — N°${esc(a.numeroDossier)}` : '';
        return `<span style="display:inline-block;background:#fefce8;border:1px solid #fde68a;border-radius:4px;padding:1px 7px;font-size:11px;margin:1px 2px;color:#92400e;" title="${esc(a.fileName)}">${esc(a.fileName)}${dossier} (${a.ageDays >= 1 ? a.ageDays + 'j' : a.ageHours + 'h'})</span>`;
      }).join('');
      html += `<div style="background:#fefce8;border:1px solid #fde68a;border-radius:8px;padding:8px 12px;">
        <span style="font-size:12px;font-weight:700;color:#92400e;">📋 BAT en attente de validation (${batAlerts.length})</span>
        <div style="margin-top:4px;">${batHtml}</div>
      </div>`;
    }

    if (html) {
      globalAlert.innerHTML = html;
      globalAlert.style.cssText = "display:block;padding:8px 16px;background:transparent;";
      // Wire up admin purge button if present
      const purgeBtn = globalAlert.querySelector("#alert-purge-orphans-btn");
      if (purgeBtn) {
        purgeBtn.onclick = async () => {
          purgeBtn.disabled = true;
          purgeBtn.textContent = '⏳ Purge...';
          try {
            const r = await fetch("/api/alerts/purge-orphans", {
              method: "DELETE",
              headers: { "Authorization": `Bearer ${authToken}` }
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) {
              await updateGlobalAlert();
            } else {
              purgeBtn.textContent = '🗑 Purger';
              purgeBtn.disabled = false;
            }
          } catch(e) {
            purgeBtn.textContent = '🗑 Purger';
            purgeBtn.disabled = false;
          }
        };
      }
    } else {
      globalAlert.style.display = "none";
    }
  } catch(e) {
    globalAlert.style.display = "none";
  }
}

// ======================================================
// PDF THUMBNAIL
// ======================================================
async function renderPdfThumbnail(fullPath, container) {
  if (!window.pdfjsLib) return;
  try {
    const pdf = await pdfjsLib.getDocument("/api/file?path=" + encodeURIComponent(fullPath)).promise;
    const page = await pdf.getPage(1);
    const viewport = page.getViewport({ scale: 0.25 });
    const canvas = document.createElement("canvas");
    canvas.width = viewport.width;
    canvas.height = viewport.height;
    await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
    const img = new Image();
    img.src = canvas.toDataURL("image/png");
    img.style.width = "100%";
    img.style.height = "100%";
    img.style.objectFit = "cover";
    container.textContent = "";
    container.appendChild(img);
  } catch (err) {
    console.warn("Erreur PDF:", err);
  }
}

// ======================================================
// SUPPRESSION DE FICHIER
// ======================================================
async function deleteFile(fullPath) {
  if (!confirm(`Supprimer "${fullPath.split("\\").pop()}" ?`)) return;

  const r = await fetch("/api/jobs/delete", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ fullPath })
  }).then(r => r.json()).catch(() => ({ ok: false }));

  if (r.ok) {
    const fk = fnKey(fullPath);
    delete deliveriesByPath[fk];
    delete deliveriesByPath[fk + "_time"];
    delete assignmentsByPath[fk];

    showNotification("✅ Fichier supprimé", "success");
    updateGlobalAlert();
    await refreshKanban();
    await refreshSubmissionView();
    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();
  } else {
    showNotification("❌ Erreur : " + (r.error || ""), "error");
  }
}

// ======================================================
// DÉPÔT DEPUIS LE BUREAU
// ======================================================
async function handleDesktopDrop(e, destFolder) {
  const files = Array.from(e.dataTransfer.files || []);
  for (const file of files) {
    if (!file.name.toLowerCase().endsWith(".pdf")) {
      showNotification(`❌ ${file.name} : seuls les PDF sont acceptés`, "error");
      continue;
    }
    const formData = new FormData();
    formData.append("file", file);
    formData.append("folder", destFolder);
    const r = await fetch("/api/upload", { method: "POST", body: formData }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${file.name} ajouté dans ${destFolder}`, "success");
    } else {
      showNotification(`❌ Erreur upload ${file.name}`, "error");
    }
  }
  await refreshKanban();
}

// ======================================================
// NAVIGATION
// ======================================================
function showKanban() {
  hideAllViews();
  document.getElementById("kanban-layout").classList.remove("hidden");
  document.getElementById("btnViewKanban").classList.add("active");
  // Show kanban-specific filter bar
  const filterBarEl = document.getElementById("kanban-filter-bar");
  if (filterBarEl) filterBarEl.style.display = "";
  buildKanban();
  buildKanbanSidebar();
}

async function showCalendar() {
  hideAllViews();
  document.getElementById("calendar").classList.remove("hidden");
  document.getElementById("btnViewCalendar").classList.add("active");
  await ensureCalendar();
  // Restore planning view switcher visibility
  const planSwitcher = document.getElementById("planning-view-switcher");
  if (planSwitcher) planSwitcher.style.display = "";
  // Update calendar refs for settings.js
  window._calendar = calendar;
  calendar?.refetchEvents();
  updateGlobalAlert();
}

function showSubmission() {
  hideAllViews();
  document.getElementById("submission").classList.remove("hidden");
  document.getElementById("btnViewSubmission").classList.add("active");
  initSubmissionView();
  updateGlobalAlert();
}

function showProduction() {
  hideAllViews();
  document.getElementById("production").classList.remove("hidden");
  document.getElementById("btnViewKanban").classList.add("active");
  loadAssignments().then(() => buildProductionView()).catch(err => console.error("Erreur production:", err));
}

function showRecycle() {
  hideAllViews();
  document.getElementById("recycle").classList.remove("hidden");
  document.getElementById("btnViewRecycle").classList.add("active");
  initRecycleView();
}

function showDashboard() {
  hideAllViews();
  document.getElementById("dashboard").classList.remove("hidden");
  document.getElementById("btnViewDashboard").classList.add("active");
  initDashboardView();
}

// ======================================================
// BAT VIEW
// ======================================================
function showBatView() {
  hideAllViews();
  document.getElementById("bat-view").classList.remove("hidden");
  const btn = document.getElementById("btnViewBat");
  if (btn) btn.classList.add("active");
  buildBatView();
}

async function buildBatView(_filterStatus, _sortField) {
  const container = document.getElementById("bat-view");
  if (!container) return;
  const filterStatus = _filterStatus || container._batFilter || "all";
  const sortField = _sortField || container._batSort || "date";
  container._batFilter = filterStatus;
  container._batSort = sortField;

  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;flex-wrap:wrap;gap:8px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);">Bon à tirer (BAT)</h2>
        <div style="display:flex;gap:8px;flex-wrap:wrap;">
          <button id="bat-view-adobe" class="btn btn-acrobat" style="border-radius:50px;">📄 Acrobat Online</button>
          <button id="bat-view-refresh" class="btn btn-primary" style="border-radius:50px;">↺ Rafraîchir</button>
        </div>
      </div>
      <div id="bat-view-summary" style="display:flex;gap:10px;flex-wrap:wrap;margin-bottom:16px;"></div>
      <div style="display:flex;gap:10px;align-items:center;margin-bottom:16px;flex-wrap:wrap;">
        <span style="font-size:13px;font-weight:500;">Filtrer :</span>
        <button class="btn btn-sm bat-filter-btn ${filterStatus === 'all' ? 'active' : ''}" data-filter="all">Tous</button>
        <button class="btn btn-sm bat-filter-btn ${filterStatus === 'pending' ? 'active' : ''}" data-filter="pending" style="${filterStatus === 'pending' ? 'background:#fef9c3;border-color:#fde68a;color:#92400e;' : ''}">⏳ En attente</button>
        <button class="btn btn-sm bat-filter-btn ${filterStatus === 'sent' ? 'active' : ''}" data-filter="sent" style="${filterStatus === 'sent' ? 'background:#dbeafe;border-color:#93c5fd;color:#1e40af;' : ''}">✉️ Envoyé</button>
        <button class="btn btn-sm bat-filter-btn ${filterStatus === 'validated' ? 'active' : ''}" data-filter="validated" style="${filterStatus === 'validated' ? 'background:#dcfce7;border-color:#86efac;color:#166534;' : ''}">✅ Validé</button>
        <button class="btn btn-sm bat-filter-btn ${filterStatus === 'rejected' ? 'active' : ''}" data-filter="rejected" style="${filterStatus === 'rejected' ? 'background:#fee2e2;border-color:#fca5a5;color:#991b1b;' : ''}">❌ Refusé</button>
        <span style="font-size:13px;font-weight:500;margin-left:8px;">Trier :</span>
        <select id="bat-sort-select" class="settings-input" style="font-size:12px;padding:3px 8px;">
          <option value="date" ${sortField==='date'?'selected':''}>Date (récent)</option>
          <option value="name" ${sortField==='name'?'selected':''}>Nom</option>
          <option value="status" ${sortField==='status'?'selected':''}>Statut</option>
        </select>
      </div>
      <div id="bat-view-list" class="bat-list-grid" style="max-height:calc(100vh - 260px);overflow-y:auto;scrollbar-width:thin;padding-bottom:16px;"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;

  container.querySelector("#bat-view-adobe").onclick = () => window.open("https://www.adobe.com/files#", "_blank", "noopener");
  container.querySelector("#bat-view-refresh").onclick = () => buildBatView();
  container.querySelectorAll(".bat-filter-btn").forEach(btn => {
    btn.onclick = () => buildBatView(btn.dataset.filter, sortField);
  });
  container.querySelector("#bat-sort-select").onchange = (e) => buildBatView(filterStatus, e.target.value);

  const listEl = container.querySelector("#bat-view-list");
  const summaryEl = container.querySelector("#bat-view-summary");

  const fmtDT = (dt) => {
    if (!dt) return null;
    const d = new Date(dt);
    return `${d.toLocaleDateString("fr-FR", {day:"2-digit",month:"2-digit",year:"numeric"})} à ${d.toLocaleTimeString("fr-FR",{hour:"2-digit",minute:"2-digit"})}`;
  };

  try {
    const jobs = await fetch("/api/jobs?folder=" + encodeURIComponent("BAT")).then(r => r.json()).catch(() => []);
    if (!Array.isArray(jobs) || jobs.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:60px 40px;">Aucun fichier en BAT</p>';
      return;
    }

    // Load all statuses in parallel
    const statuses = await Promise.all(jobs.map(job => {
      const full = normalizePath(job.fullPath || "");
      return fetch(`/api/bat/status?path=${encodeURIComponent(full)}`).then(r => r.json()).catch(() => ({}));
    }));

    // Load all fabrication data in parallel
    const fabs = await Promise.all(jobs.map(job => {
      const full = normalizePath(job.fullPath || "");
      const jobFn = fnKey(full);
      let lookupFn = jobFn;
      if (lookupFn.toLowerCase().startsWith("bat_")) lookupFn = lookupFn.substring(4);
      return fetch("/api/fabrication?fileName=" + encodeURIComponent(lookupFn), {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({}));
    }));

    // Combine jobs with their status
    let items = jobs.map((job, i) => ({ job, status: statuses[i], fab: fabs[i] }));

    // Compute status label for each
    const getStatusCategory = (st) => {
      if (st.rejectedAt) return 'rejected';
      if (st.validatedAt) return 'validated';
      if (st.sentAt) return 'sent';
      return 'pending';
    };
    items.forEach(it => { it.statusCategory = getStatusCategory(it.status); });

    // Summary counts
    const counts = { pending: 0, sent: 0, validated: 0, rejected: 0 };
    items.forEach(it => counts[it.statusCategory]++);
    const summaryItems = [
      { key: 'pending', label: '⏳ En attente', bg: '#fef9c3', bc: '#fde68a', tc: '#92400e' },
      { key: 'sent', label: '✉️ Envoyé', bg: '#dbeafe', bc: '#93c5fd', tc: '#1e40af' },
      { key: 'validated', label: '✅ Validé', bg: '#dcfce7', bc: '#86efac', tc: '#166534' },
      { key: 'rejected', label: '❌ Refusé', bg: '#fee2e2', bc: '#fca5a5', tc: '#991b1b' },
    ];
    summaryEl.innerHTML = summaryItems.map(s => `
      <div onclick="buildBatView('${s.key}')" style="background:${s.bg};border:1px solid ${s.bc};border-radius:10px;padding:10px 18px;cursor:pointer;display:flex;flex-direction:column;align-items:center;min-width:90px;">
        <span style="font-size:22px;font-weight:700;color:${s.tc};">${counts[s.key]}</span>
        <span style="font-size:11px;color:${s.tc};font-weight:600;margin-top:2px;">${s.label}</span>
      </div>
    `).join('');

    // Apply filter
    if (filterStatus !== 'all') {
      items = items.filter(it => it.statusCategory === filterStatus);
    }

    // Apply sort
    items.sort((a, b) => {
      if (sortField === 'name') return (a.job.name || '').localeCompare(b.job.name || '');
      if (sortField === 'status') {
        const order = { rejected: 0, validated: 1, sent: 2, pending: 3 };
        return (order[a.statusCategory] ?? 99) - (order[b.statusCategory] ?? 99);
      }
      // date (default): most recent first
      return new Date(b.job.modified || 0) - new Date(a.job.modified || 0);
    });

    if (items.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun BAT dans ce filtre</p>';
      return;
    }

    listEl.innerHTML = "";
    for (const { job, status, fab } of items) {
      const full = normalizePath(job.fullPath || "");
      const jobFn = fnKey(full);
      let lookupFn = jobFn;
      if (lookupFn.toLowerCase().startsWith("bat_")) lookupFn = lookupFn.substring(4);

      const card = document.createElement("div");
      card.className = "bat-card-modern";

      // Status bar
      const statusBar = document.createElement("div");
      statusBar.className = "bat-card-status-bar";
      if (status.rejectedAt) statusBar.className += " bat-status-rejected";
      else if (status.validatedAt) statusBar.className += " bat-status-validated";
      else if (status.sentAt) statusBar.className += " bat-status-sent";
      else statusBar.className += " bat-status-new";
      card.appendChild(statusBar);

      const innerDiv = document.createElement("div");
      innerDiv.className = "bat-card-inner";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "bat-card-thumb";
      thumbDiv.textContent = "PDF";
      if ((job.name || "").toLowerCase().endsWith(".pdf") && window._renderPdfThumbnail) {
        window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      const bodyDiv = document.createElement("div");
      bodyDiv.className = "bat-card-body";

      const dossierEl = document.createElement("div");
      dossierEl.className = "bat-card-dossier";
      dossierEl.textContent = fab && fab.numeroDossier ? `N° ${fab.numeroDossier}${fab.client ? ' — ' + fab.client : ''}` : "—";

      const filenameEl = document.createElement("div");
      filenameEl.className = "bat-card-filename";
      filenameEl.textContent = job.name || "—";

      const metaEl = document.createElement("div");
      metaEl.className = "bat-card-meta";
      metaEl.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;

      const trackingEl = document.createElement("div");
      trackingEl.className = "bat-tracking";

      bodyDiv.appendChild(dossierEl);
      bodyDiv.appendChild(filenameEl);
      bodyDiv.appendChild(metaEl);
      bodyDiv.appendChild(trackingEl);

      const actionsDiv = document.createElement("div");
      actionsDiv.className = "bat-card-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "🔍 Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnAcrobat = document.createElement("button");
      btnAcrobat.className = "btn btn-sm btn-acrobat";
      btnAcrobat.textContent = "📄 Télécharger (Acrobat)";
      btnAcrobat.title = "Télécharger le PDF sur votre poste pour l'ouvrir dans Acrobat";
      btnAcrobat.onclick = () => {
        const a = document.createElement("a");
        a.href = "/api/file?path=" + encodeURIComponent(full);
        a.download = job.name || "bat.pdf";
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
      };

      const btnArchiver = document.createElement("button");
      btnArchiver.className = "btn btn-sm";
      btnArchiver.textContent = "📦 Archiver";
      btnArchiver.onclick = async () => {
        if (!confirm(`Archiver le BAT "${job.name}" dans le dossier de production ?`)) return;
        const archiveFolder = "Fin de production";
        const archivePath = full.replace(/[/\\]BAT[/\\]/, "/" + archiveFolder + "/").replace(/\\/g, "/");
        const resp = await fetch("/api/jobs/move", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ source: full, destination: archiveFolder, overwrite: true })
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (resp.ok) { showNotification("✅ Archivé dans Fin de production", "success"); buildBatView(); }
        else showNotification("❌ Erreur archivage", "error");
      };

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "📋 Fiche";
      btnFiche.onclick = () => { if (window._openFabrication) window._openFabrication(full.replace(/[/\\]BAT_/, "/").replace("BAT/BAT_", "/")); };

      actionsDiv.appendChild(btnOpen);
      actionsDiv.appendChild(btnAcrobat);
      actionsDiv.appendChild(btnArchiver);
      actionsDiv.appendChild(btnFiche);

      innerDiv.appendChild(thumbDiv);
      innerDiv.appendChild(bodyDiv);
      innerDiv.appendChild(actionsDiv);
      card.appendChild(innerDiv);
      listEl.appendChild(card);

      // Tracking buttons
      const btnSent = document.createElement("button");
      btnSent.className = "bat-status-badge bat-sent" + (status.sentAt ? " active" : "");
      const sentTs = status.sentAt ? fmtDT(status.sentAt) : null;
      btnSent.innerHTML = status.sentAt
        ? `✉️ Envoyé<span style="font-size:9px;font-weight:400;margin-left:4px;">${sentTs}</span>`
        : "✉️ Marquer envoyé";
      btnSent.onclick = async () => {
        let tmpl = null;
        try {
          const tr = await fetch("/api/config/bat-mail-template").then(r => r.json()).catch(() => null);
          if (tr && tr.ok && tr.template) tmpl = tr.template;
        } catch(e) { /* ignore */ }
        if (tmpl && (tmpl.subject || tmpl.body)) {
          const replaceVars = (str) => (str || '')
            .replace(/\{\{numeroDossier\}\}/g, fab.numeroDossier || '')
            .replace(/\{\{nomClient\}\}/g, fab.nomClient || '')
            .replace(/\{\{nomFichier\}\}/g, job.name || '')
            .replace(/\{\{typeTravail\}\}/g, fab.typeTravail || '')
            .replace(/\{\{operateur\}\}/g, fab.operateur || '');
          const to = tmpl.to || fab.mailClient || '';
          const mailto = `mailto:${to}?subject=${encodeURIComponent(replaceVars(tmpl.subject))}&body=${encodeURIComponent(replaceVars(tmpl.body))}`;
          window.open(mailto);
        }
        fetch("/api/bat/send", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(() => buildBatView());
      };

      const sep1 = document.createElement("span");
      sep1.className = "bat-tracking-sep";

      const btnValidate = document.createElement("button");
      btnValidate.className = "bat-status-badge bat-validated" + (status.validatedAt ? " active" : "");
      const validateTs = status.validatedAt ? fmtDT(status.validatedAt) : null;
      btnValidate.innerHTML = status.validatedAt
        ? `✅ Validé<span style="font-size:9px;font-weight:400;margin-left:4px;">${validateTs}</span>`
        : "✅ Valider";
      btnValidate.onclick = () => fetch("/api/bat/validate", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(() => buildBatView());

      const sep2 = document.createElement("span");
      sep2.className = "bat-tracking-sep";

      const btnReject = document.createElement("button");
      btnReject.className = "bat-status-badge bat-rejected" + (status.rejectedAt ? " active" : "");
      const rejectTs = status.rejectedAt ? fmtDT(status.rejectedAt) : null;
      btnReject.innerHTML = status.rejectedAt
        ? `❌ Refusé<span style="font-size:9px;font-weight:400;margin-left:4px;">${rejectTs}</span>`
        : "❌ Refuser";
      btnReject.onclick = () => fetch("/api/bat/reject", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(() => buildBatView());

      trackingEl.appendChild(btnSent);
      trackingEl.appendChild(sep1);
      trackingEl.appendChild(btnValidate);
      trackingEl.appendChild(sep2);
      trackingEl.appendChild(btnReject);

      if (status.sentAt && !status.validatedAt && !status.rejectedAt) {
        const MS_PER_HOUR = 3600000;
        const ageHours = (Date.now() - new Date(status.sentAt)) / MS_PER_HOUR;
        if (ageHours >= 48) {
          const alertEl = document.createElement("div");
          alertEl.className = "bat-alert-j2";
          alertEl.textContent = `⚠️ BAT envoyé depuis ${Math.floor(ageHours / 24)} jour(s) sans réponse !`;
          bodyDiv.appendChild(alertEl);
        }
      }
    }
  } catch (err) {
    listEl.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

// ======================================================
// RAPPORT VIEW
// ======================================================
function showRapportView() {
  hideAllViews();
  document.getElementById("rapport-view").classList.remove("hidden");
  const btn = document.getElementById("btnViewRapport");
  if (btn) btn.classList.add("active");
  buildRapportView();
}

async function buildRapportView() {
  const container = document.getElementById("rapport-view");
  if (!container) return;
  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:20px;">
        <h2 style="margin:0;font-size:20px;font-weight:700;color:#111827;">Rapport</h2>
        <button id="rapport-view-refresh" class="btn btn-primary">Rafraîchir</button>
      </div>
      <div id="rapport-view-list"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  container.querySelector("#rapport-view-refresh").onclick = buildRapportView;

  const listEl = container.querySelector("#rapport-view-list");
  try {
    const jobs = await fetch("/api/jobs?folder=" + encodeURIComponent("Rapport")).then(r => r.json()).catch(() => []);
    if (!Array.isArray(jobs) || jobs.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun fichier en Rapport</p>';
      return;
    }
    listEl.innerHTML = "";
    for (const job of jobs) {
      const full = normalizePath(job.fullPath || "");
      const card = document.createElement("div");
      card.style.cssText = "background:white;border:1px solid #e5e7eb;border-radius:12px;padding:16px;margin-bottom:10px;display:flex;align-items:center;gap:12px;flex-wrap:wrap;";

      const thumbDiv = document.createElement("div");
      thumbDiv.style.cssText = "width:60px;height:70px;flex-shrink:0;background:#f3f4f6;border:1px solid #e5e7eb;border-radius:6px;display:flex;align-items:center;justify-content:center;overflow:hidden;";
      if ((job.name || "").toLowerCase().endsWith(".pdf") && window._renderPdfThumbnail) {
        thumbDiv.textContent = "";
        const canvas = document.createElement("canvas");
        canvas.style.cssText = "width:100%;height:100%;object-fit:contain;";
        thumbDiv.appendChild(canvas);
        window._renderPdfThumbnail(full, thumbDiv).catch(() => { thumbDiv.textContent = "PDF"; });
      } else {
        thumbDiv.textContent = "PDF";
        thumbDiv.style.cssText += "font-weight:700;font-size:12px;color:#BC0024;font-family:monospace;";
      }

      const info = document.createElement("div");
      info.style.cssText = "flex:1;min-width:0;";
      info.innerHTML = `
        <div style="font-weight:600;font-size:14px;color:#111827;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${job.name || '—'}</div>
        <div style="font-size:12px;color:#6b7280;">${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}</div>
      `;

      const btnRow = document.createElement("div");
      btnRow.style.cssText = "display:flex;gap:6px;flex-wrap:wrap;";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => { if (window._openFabrication) window._openFabrication(full); };

      const btnAcrobatPro = document.createElement("button");
      btnAcrobatPro.className = "btn btn-sm btn-acrobat";
      btnAcrobatPro.textContent = "📄 Ouvrir dans Acrobat Pro";
      btnAcrobatPro.onclick = async () => {
        const r = await fetch("/api/acrobat/open", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath: full })
        }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
        if (!r.ok) showNotification("❌ " + (r.error || "Erreur ouverture Acrobat"), "error");
      };

      const btnArchiver = document.createElement("button");
      btnArchiver.className = "btn btn-sm";
      btnArchiver.textContent = "📦 Archiver";
      btnArchiver.onclick = async () => {
        if (!confirm(`Archiver "${job.name}" dans le dossier de production ?`)) return;
        const r = await fetch("/api/jobs/archive", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath: full })
        }).then(res => res.json()).catch(() => ({ ok: false }));
        if (r.ok) { showNotification("✅ Rapport archivé", "success"); buildRapportView(); }
        else showNotification("❌ Erreur : " + (r.error || ""), "error");
      };

      btnRow.appendChild(btnOpen);
      btnRow.appendChild(btnFiche);
      btnRow.appendChild(btnAcrobatPro);
      btnRow.appendChild(btnArchiver);

      card.appendChild(thumbDiv);
      card.appendChild(info);
      card.appendChild(btnRow);
      listEl.appendChild(card);
    }
  } catch(err) {
    listEl.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

// ======================================================
// SIDEBAR KANBAN (panneau latéral)
// ======================================================
async function buildKanbanSidebar() {
  const sidebar = document.getElementById("kanban-sidebar");
  if (!sidebar) return;
  sidebar.innerHTML = '<div style="padding:12px;color:#6b7280;font-size:12px;">Chargement...</div>';

  try {
    sidebar.innerHTML = `
      <div class="kanban-sidebar-section">
        <div style="font-size:12px;font-weight:700;color:#374151;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">🖨️ Planning Machine</div>
        <div id="kanban-sidebar-machine-list" style="font-size:11px;color:#9ca3af;">Chargement...</div>
      </div>
      <div class="kanban-sidebar-section">
        <div style="font-size:12px;font-weight:700;color:#374151;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">🖨 BAT en cours</div>
        <div id="kanban-sidebar-bat-list" style="font-size:11px;color:#9ca3af;">Chargement...</div>
      </div>
    `;

    // Load Machine planning and BAT sidebar asynchronously
    loadMachineSidebar();
    loadBatSidebar();
  } catch(e) {
    sidebar.innerHTML = '<div style="padding:12px;color:#9ca3af;font-size:12px;">—</div>';
  }
}

async function loadMachineSidebar() {
  const listEl = document.getElementById("kanban-sidebar-machine-list");
  if (!listEl) return;
  try {
    const resp = await fetch("/api/fabrication/events", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ ok: false, events: [] }));

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const todayIso = today.toISOString().split('T')[0];

    const fabEvents = (resp.ok && Array.isArray(resp.events)) ? resp.events : [];
    const machineEvents = fabEvents
      .filter(fe => fe.type === 'impression' && fe.date && fe.date >= todayIso)
      .sort((a, b) => a.date.localeCompare(b.date) || (a.manualTime || '').localeCompare(b.manualTime || ''));

    if (machineEvents.length === 0) {
      listEl.innerHTML = '<div style="color:#9ca3af;">Aucune impression planifiée</div>';
      return;
    }

    function escH(s) { return (s||"").replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;"); }

    listEl.innerHTML = machineEvents.slice(0, 10).map(fe => {
      const d = new Date(fe.date + 'T00:00:00');
      const dateStr = d.toLocaleDateString("fr-FR", { weekday: "short", day: "numeric", month: "short" });
      const timeStr = fe.manualTime || "09:00";
      const isLocked = !!fe.locked;
      const bg = isLocked ? '#dcfce7' : '#ede9fe';
      const tc = isLocked ? '#166534' : '#5b21b6';
      const lockIcon = isLocked ? '🔒 ' : '';
      const label = fe.title || fe.fileName || '—';
      const truncLabel = label.length > 20 ? label.substring(0, 18) + '…' : label;
      const machine = fe.moteurImpression ? ` · ${escH(fe.moteurImpression)}` : '';
      return `<div style="padding:5px 0;border-bottom:1px solid #f0f0f0;">
        <div style="display:flex;justify-content:space-between;align-items:center;gap:4px;">
          <span style="background:${bg};color:${tc};padding:1px 5px;border-radius:4px;font-size:9px;font-weight:700;white-space:nowrap;">${lockIcon}${escH(dateStr)} ${escH(timeStr)}</span>
        </div>
        <div style="font-size:11px;color:#374151;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;margin-top:2px;" title="${escH(label)}">${escH(truncLabel)}${machine}</div>
      </div>`;
    }).join("");

    if (machineEvents.length > 10) {
      listEl.innerHTML += `<div style="font-size:10px;color:#9ca3af;padding-top:4px;text-align:center;">+${machineEvents.length - 10} autres</div>`;
    }
  } catch(e) {
    const el = document.getElementById("kanban-sidebar-machine-list");
    if (el) el.innerHTML = '<div style="color:#9ca3af;">—</div>';
  }
}

async function loadBatSidebar() {
  const listEl = document.getElementById("kanban-sidebar-bat-list");
  if (!listEl) return;
  try {
    const batJobs = await fetch("/api/jobs?folder=" + encodeURIComponent("BAT"))
      .then(r => r.json()).catch(() => []);

    if (!Array.isArray(batJobs) || batJobs.length === 0) {
      listEl.innerHTML = '<div style="color:#9ca3af;">Aucun fichier en BAT</div>';
      return;
    }

    listEl.innerHTML = batJobs.slice(0, 6).map(job => {
      const name = job.name || "—";
      const truncName = name.length > 22 ? name.substring(0, 20) + "…" : name;
      return `<div style="padding:5px 0;border-bottom:1px solid #f0f0f0;display:flex;justify-content:space-between;align-items:center;gap:4px;">
        <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#374151;font-weight:500;" title="${name}">${truncName}</span>
        <span style="background:#fef9c3;color:#92400e;padding:1px 5px;border-radius:4px;font-size:9px;font-weight:700;white-space:nowrap;">BAT</span>
      </div>`;
    }).join("");

    if (batJobs.length > 6) {
      listEl.innerHTML += `<div style="font-size:10px;color:#9ca3af;padding-top:4px;text-align:center;">+${batJobs.length - 6} autres</div>`;
    }

    // Load BAT status async for each job
    for (const job of batJobs.slice(0, 6)) {
      const full = normalizePath(job.fullPath || "");
      fetch(`/api/bat/status?path=${encodeURIComponent(full)}`)
        .then(r => r.json()).then(st => {
          const items = listEl.querySelectorAll("div[style*='border-bottom']");
          const idx = batJobs.indexOf(job);
          const el = items[idx];
          if (!el) return;
          const badge = el.querySelector("span:last-child");
          if (!badge) return;
          if (st.rejectedAt) { badge.textContent = "REFUSÉ"; badge.style.cssText = "background:#fee2e2;color:#991b1b;padding:1px 5px;border-radius:4px;font-size:9px;font-weight:700;white-space:nowrap;"; }
          else if (st.validatedAt) { badge.textContent = "VALIDÉ"; badge.style.cssText = "background:#dcfce7;color:#166534;padding:1px 5px;border-radius:4px;font-size:9px;font-weight:700;white-space:nowrap;"; }
          else if (st.sentAt) { badge.textContent = "ENVOYÉ"; badge.style.cssText = "background:#dbeafe;color:#1e40af;padding:1px 5px;border-radius:4px;font-size:9px;font-weight:700;white-space:nowrap;"; }
        }).catch(() => {});
    }
  } catch(e) {
    const listEl = document.getElementById("kanban-sidebar-bat-list");
    if (listEl) listEl.innerHTML = '<div style="color:#9ca3af;">—</div>';
  }
}

function setupProfileUI() {
  const btnSubmission = document.getElementById("btnViewSubmission");
  const btnSettings = document.getElementById("btn-settings");
  const btnRecycle = document.getElementById("btnViewRecycle");
  const btnDashboard = document.getElementById("btnViewDashboard");
  const btnDossiers = document.getElementById("btnViewDossiers");
  const btnBat = document.getElementById("btnViewBat");
  const btnRapport = document.getElementById("btnViewRapport");
  const userInfo = document.getElementById("user-info");

  const profileLabels = { 1: "Soumission", 2: "Opérateur", 3: "Admin", 4: "Finitions", 5: "Lecture plannings", 6: "Opérateur restreint" };
  const profileLabel = profileLabels[currentUser.profile] || `Profil ${currentUser.profile}`;
  userInfo.textContent = `${currentUser.name} (${profileLabel})`;

  // Profile 4 (Façonnage/Finitions): sees plannings but NOT BAT and Rapport
  if (currentUser.profile === 4) {
    if (btnRecycle) btnRecycle.style.display = "none";
    if (btnDossiers) btnDossiers.style.display = "inline-block";
    if (btnDashboard) btnDashboard.style.display = "none";
    if (btnBat) btnBat.style.display = "none";
    if (btnRapport) btnRapport.style.display = "none";
    if (btnSubmission) btnSubmission.style.display = "none";
    if (btnSettings) btnSettings.style.display = "none";
    const btnGlobalProd = document.getElementById("btnViewGlobalProd");
    if (btnGlobalProd) btnGlobalProd.style.display = "inline-block";
    setupKanbanActions();
    return;
  }

  // Profile 5 (Lecture plannings): only sees planning calendar, no modifications
  if (currentUser.profile === 5) {
    if (btnRecycle) btnRecycle.style.display = "none";
    if (btnDossiers) btnDossiers.style.display = "none";
    if (btnDashboard) btnDashboard.style.display = "none";
    if (btnBat) btnBat.style.display = "none";
    if (btnRapport) btnRapport.style.display = "none";
    if (btnSubmission) btnSubmission.style.display = "none";
    if (btnSettings) btnSettings.style.display = "none";
    const btnGlobalProd = document.getElementById("btnViewGlobalProd");
    if (btnGlobalProd) btnGlobalProd.style.display = "none";
    setupKanbanActions();
    return;
  }

  // Profile 6 (Opérateur restreint): like Opérateur but plannings and dates are read-only
  if (currentUser.profile === 6) {
    if (btnRecycle) btnRecycle.style.display = "inline-block";
    if (btnDossiers) btnDossiers.style.display = "inline-block";
    if (btnDashboard) btnDashboard.style.display = "none";
    if (btnBat) btnBat.style.display = "inline-block";
    if (btnRapport) btnRapport.style.display = "inline-block";
    if (btnSubmission) btnSubmission.style.display = "inline-block";
    if (btnSettings) btnSettings.style.display = "none";
    const btnGlobalProd = document.getElementById("btnViewGlobalProd");
    if (btnGlobalProd) btnGlobalProd.style.display = "inline-block";
    setupKanbanActions();
    return;
  }

  if (btnRecycle) btnRecycle.style.display = "inline-block";
  if (btnDossiers) btnDossiers.style.display = "inline-block";
  if (btnDashboard) btnDashboard.style.display = currentUser.profile === 3 ? "inline-block" : "none";
  // Profile 1: no Rapport in menu
  if (btnRapport) btnRapport.style.display = currentUser.profile === 1 ? "none" : "inline-block";
  if (btnBat) btnBat.style.display = "inline-block";

  // Vue production globale visible pour les profils 1, 2, 3
  const btnGlobalProd = document.getElementById("btnViewGlobalProd");
  if (btnGlobalProd) btnGlobalProd.style.display = "inline-block";

  if (currentUser.profile === 1) {
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 2) {
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 3) {
    btnSettings.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
  }

  setupKanbanActions();
}

function setupKanbanActions() {
  const btnKanban = document.getElementById("btnViewKanban");
  const btnCalendar = document.getElementById("btnViewCalendar");
  const btnSubmission = document.getElementById("btnViewSubmission");

  if (currentUser.profile === 1) {
    // Profile 1: Soumission / Production / BAT / Dossiers / Vue production / Corbeille (no Calendrier, no Rapport)
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "none";
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 2) {
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 3) {
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 4) {
    // Profile 4 (Finitions): kanban read-only + calendar (planning), no submission
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    if (btnSubmission) btnSubmission.style.display = "none";
  } else if (currentUser.profile === 5) {
    // Profile 5 (Lecture plannings): only calendar, read-only
    btnKanban.style.display = "none";
    btnCalendar.style.display = "inline-block";
    if (btnSubmission) btnSubmission.style.display = "none";
  } else if (currentUser.profile === 6) {
    // Profile 6 (Opérateur restreint): kanban + calendar (read-only plannings)
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    if (btnSubmission) btnSubmission.style.display = "inline-block";
  }
}

// ======================================================
// SOUMISSION (Profil 1)
// ======================================================
async function initSubmissionView() {
  const submissionEl = document.getElementById("submission");
  if (submissionEl.innerHTML) return;

  submissionEl.innerHTML = `
    <div class="submission-container">
      <div class="submission-split">
        <div class="submission-upload-section">
          <h3>Déposer fichiers</h3>
          <div class="upload-zone" id="uploadZone">
            <div class="upload-icon">PDF</div>
            <p class="upload-text">Déposez vos fichiers PDF ici</p>
            <p class="upload-subtext">ou cliquez pour parcourir · PDF et XML acceptés</p>
            <input type="file" id="uploadInput" multiple accept=".pdf,.xml" style="display: none;" />
          </div>
          <div id="uploadProgress" class="upload-progress" style="display: none;">
            <div class="progress-bar"><div class="progress-fill"></div></div>
            <p id="uploadStatus">Envoi en cours...</p>
          </div>
        </div>
        <div class="submission-files-section">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px;">
            <h3 style="margin: 0;">Fichiers soumis</h3>
            <div style="display: flex; gap: 8px;">
              <button id="btnImportFromMail" class="btn btn-sm">📧 Importer depuis un mail</button>
              <button id="btnSelectAll" class="btn btn-sm">Sélectionner tout</button>
              <button id="btnSendAnalysis" class="btn btn-primary btn-sm">Envoyer en production</button>
            </div>
          </div>
          <div id="submissionKanban" class="submission-kanban"></div>
        </div>
      </div>
      <div class="submission-section">
        <h3>Planning de livraison</h3>
        <div id="submissionCalendar" class="submission-calendar"></div>
      </div>
    </div>
  `;

  const uploadZone = document.getElementById("uploadZone");
  const uploadInput = document.getElementById("uploadInput");

  uploadZone.onclick = () => uploadInput.click();
  uploadZone.addEventListener("dragover", (e) => { e.preventDefault(); uploadZone.classList.add("drag-over"); });
  uploadZone.addEventListener("dragleave", () => { uploadZone.classList.remove("drag-over"); });
  uploadZone.addEventListener("drop", async (e) => {
    e.preventDefault();
    uploadZone.classList.remove("drag-over");
    const files = Array.from(e.dataTransfer.files || []);
    if (files.length > 0) await handleSubmissionFiles(files);
  });
  uploadInput.addEventListener("change", async (e) => {
    const files = Array.from(e.target.files || []);
    await handleSubmissionFiles(files);
    uploadInput.value = "";
  });

  await initSubmissionCalendar();
  // Update submission calendar ref for settings.js
  window._submissionCalendar = submissionCalendar;

  await refreshSubmissionView();
  setupSubmissionButtons();
  setupMailImportButton();
}

// ── ERP/W2P lookup popup ──────────────────────────────────────────────────────
async function openErpLookupPopup(prefillCb, defaultRef = "") {
  // Load active ERP/W2P sources
  let lookupCfg = {};
  try {
    const r = await fetch("/api/settings/submission-erp-lookup", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({}));
    if (r.ok) lookupCfg = r.config || {};
  } catch(e) { /* ignore */ }

  const sources = [
    { id: "pressero", label: "🌐 Pressero" },
    { id: "mdsf",     label: "🌐 MDSF" },
    ...((lookupCfg.erpSources || []).map(s => ({ id: s.id, label: `🔗 ${s.name}` })))
  ];

  const overlay = document.createElement("div");
  overlay.className = "modal-overlay";
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:9999;display:flex;align-items:center;justify-content:center;";

  overlay.innerHTML = `
    <div style="background:#fff;border-radius:12px;padding:24px;width:420px;max-width:95vw;box-shadow:0 20px 60px rgba(0,0,0,.3);">
      <h3 style="margin:0 0 16px;font-size:16px;font-weight:700;color:#111827;">🔗 Récupérer depuis ERP / W2P</h3>
      <div style="margin-bottom:14px;">
        <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:5px;">Source</label>
        <select id="erp-lookup-source" style="width:100%;padding:8px 10px;border:1px solid #d1d5db;border-radius:8px;font-size:13px;">
          ${sources.map(s => `<option value="${s.id}">${s.label}</option>`).join("")}
        </select>
      </div>
      <div style="margin-bottom:14px;">
        <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:5px;">Référence / N° commande</label>
        <input id="erp-lookup-ref" type="text" value="${defaultRef}" placeholder="ex: ORDER-12345"
          style="width:100%;box-sizing:border-box;padding:8px 10px;border:1px solid #d1d5db;border-radius:8px;font-size:13px;" />
      </div>
      <div id="erp-lookup-results" style="display:none;margin-bottom:14px;"></div>
      <div id="erp-lookup-msg" style="font-size:12px;min-height:18px;margin-bottom:10px;"></div>
      <div style="display:flex;gap:10px;justify-content:flex-end;">
        <button id="erp-lookup-cancel" class="btn" style="padding:8px 16px;">Annuler</button>
        <button id="erp-lookup-search" class="btn btn-primary" style="padding:8px 16px;">🔍 Rechercher</button>
      </div>
    </div>`;

  document.body.appendChild(overlay);

  overlay.querySelector("#erp-lookup-cancel").onclick = () => overlay.remove();

  async function doSearch() {
    const source  = overlay.querySelector("#erp-lookup-source").value;
    const ref     = overlay.querySelector("#erp-lookup-ref").value.trim();
    const msgEl   = overlay.querySelector("#erp-lookup-msg");
    const resultsEl = overlay.querySelector("#erp-lookup-results");

    if (!ref) { msgEl.style.color = "#ef4444"; msgEl.textContent = "Saisissez une référence commande."; return; }

    msgEl.style.color = "#6b7280"; msgEl.textContent = "⏳ Recherche en cours…";
    resultsEl.style.display = "none";

    try {
      const r = await fetch(`/api/external/${encodeURIComponent(source)}/lookup`, {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}`, "Content-Type": "application/json" },
        body: JSON.stringify({ ref })
      }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

      if (!r.ok) { msgEl.style.color = "#ef4444"; msgEl.textContent = `❌ ${r.error || "Erreur"}`; return; }

      msgEl.textContent = "";
      const fiche = r.fiche || {};
      const fieldLabels = {
        referenceCommande: "Référence", nomClient: "Client", client: "Société",
        typeTravail: "Type de travail", quantite: "Quantité", formatFini: "Format fini",
        dateLivraisonSouhaitee: "Livraison souhaitée", dateReceptionSouhaitee: "Réception souhaitée",
        commentaire: "Commentaire"
      };
      const rows = Object.entries(fiche).filter(([, v]) => v).map(([k, v]) =>
        `<tr><td style="padding:4px 8px;font-size:12px;color:#6b7280;white-space:nowrap;">${fieldLabels[k]||k}</td>
             <td style="padding:4px 8px;font-size:12px;font-weight:600;">${v}</td></tr>`
      ).join("");

      resultsEl.style.display = "block";
      resultsEl.innerHTML = rows
        ? `<table style="width:100%;border-collapse:collapse;background:#f9fafb;border-radius:8px;overflow:hidden;">${rows}</table>
           <button id="erp-lookup-apply" class="btn btn-primary" style="width:100%;margin-top:10px;">✅ Appliquer ces métadonnées</button>`
        : `<p style="color:#9ca3af;font-size:13px;text-align:center;">Aucune métadonnée trouvée.</p>`;

      if (rows) {
        resultsEl.querySelector("#erp-lookup-apply").onclick = () => {
          prefillCb(fiche);
          overlay.remove();
        };
      }
    } catch(e) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
  }

  overlay.querySelector("#erp-lookup-search").onclick = doSearch;
  overlay.querySelector("#erp-lookup-ref").onkeydown = (e) => { if (e.key === "Enter") doSearch(); };

  // Auto-lookup if configured and a ref was detected
  if (defaultRef && lookupCfg.autoLookup) doSearch();
}

async function handleSubmissionFiles(files) {
  const uploadProgress = document.getElementById("uploadProgress");
  const progressFill = uploadProgress.querySelector(".progress-fill");
  const uploadStatus = document.getElementById("uploadStatus");
  const uploadZone = document.getElementById("uploadZone");

  // ── Separate PDFs and XMLs ────────────────────────────────────────────────
  const pdfFiles = files.filter(f => f.name.toLowerCase().endsWith(".pdf"));
  const xmlFiles = files.filter(f => f.name.toLowerCase().endsWith(".xml"));

  // ── Cases with XML files: use the coupled upload endpoint ─────────────────
  if (xmlFiles.length > 0) {
    uploadProgress.style.display = "block";
    uploadZone.style.opacity = "0.5";
    uploadZone.style.pointerEvents = "none";

    // Load coupling config
    let couplingCfg = { enabled: true, mode: "prefill" };
    try {
      const r = await fetch("/api/settings/submission-xml-coupling", {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({}));
      if (r.ok && r.config) couplingCfg = r.config;
    } catch(e) { /* use defaults */ }

    // Detect pairs by base name (PDF base == XML base)
    const getBase = f => f.name.replace(/\.(pdf|xml)$/i, "").toLowerCase();

    /** Build pairs: each entry is { pdfs: File[], xml: File|null } */
    const pairs = [];

    if (pdfFiles.length > 0 && xmlFiles.length === 1) {
      // 1 XML → all PDFs share the same XML
      pairs.push({ pdfs: pdfFiles, xml: xmlFiles[0] });
    } else if (pdfFiles.length > 0 && xmlFiles.length > 1) {
      // Multiple XMLs → match by base name
      const xmlByBase = {};
      xmlFiles.forEach(x => { xmlByBase[getBase(x)] = x; });
      const unmatched = [];
      pdfFiles.forEach(p => {
        const base = getBase(p);
        if (xmlByBase[base]) {
          pairs.push({ pdfs: [p], xml: xmlByBase[base] });
        } else {
          unmatched.push(p);
        }
      });
      if (unmatched.length > 0) pairs.push({ pdfs: unmatched, xml: null });
    } else {
      // No PDFs or XML only
      pairs.push({ pdfs: pdfFiles, xml: xmlFiles[0] || null });
    }

    let successCount = 0, errorCount = 0;
    const total = pairs.length;

    for (let i = 0; i < pairs.length; i++) {
      const { pdfs, xml } = pairs[i];
      progressFill.style.width = Math.round(((i + 1) / total) * 100) + "%";

      const label = pdfs.length > 0
        ? `${pdfs.map(p => p.name).join(", ")}${xml ? " + " + xml.name : ""}`
        : xml?.name || "fichier inconnu";
      uploadStatus.textContent = `Envoi ${label}…`;

      const formData = new FormData();
      pdfs.forEach(p => formData.append("pdf", p));
      if (xml) formData.append("xml", xml);

      try {
        const r = await fetch("/api/soumission/upload-with-xml", {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` },
          body: formData
        }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

        if (!r.ok) {
          uploadStatus.textContent = `❌ ${label}: ${r.error || "Erreur"}`;
          errorCount++;
          continue;
        }

        const hasPrefill = r.fichePrefill && Object.keys(r.fichePrefill).length > 0;

        if (hasPrefill && couplingCfg.mode === "prefill") {
          // Open fabrication form pre-filled
          uploadStatus.textContent = `✅ ${label} — ouverture du formulaire pré-rempli`;
          successCount++;

          const firstPdf = (r.jobIds && r.jobIds.length > 0 && r.jobIds[0]?.fullPath)
            ? r.jobIds[0].fullPath : null;

          // Show a badge in the upload zone
          const badge = document.createElement("div");
          badge.style.cssText = "display:inline-flex;gap:6px;align-items:center;background:#f0fdf4;border:1px solid #86efac;border-radius:6px;padding:4px 10px;font-size:12px;margin:4px 0;";
          badge.innerHTML = `<span style="background:#ef4444;color:#fff;border-radius:4px;padding:2px 5px;font-weight:700;">PDF</span>
            <span style="background:#3b82f6;color:#fff;border-radius:4px;padding:2px 5px;font-weight:700;">XML ✓</span>
            <span style="color:#166534;">Formulaire pré-rempli prêt</span>`;
          uploadZone.appendChild(badge);

          if (firstPdf) openFabrication(firstPdf, r.fichePrefill);
          await new Promise(resolve => setTimeout(resolve, 600));
        } else {
          uploadStatus.textContent = `✅ ${label}`;
          successCount++;
          await new Promise(resolve => setTimeout(resolve, 500));
        }
      } catch (err) {
        uploadStatus.textContent = `❌ Erreur`;
        errorCount++;
      }
    }

    uploadProgress.style.display = "none";
    uploadZone.style.opacity = "1";
    uploadZone.style.pointerEvents = "auto";

    if (successCount > 0) {
      showNotification(`✅ ${successCount} envoi(s) réussi(s)`, "success");
      await refreshSubmissionView();
      if (submissionCalendar) submissionCalendar.refetchEvents();
    }
    if (errorCount > 0) showNotification(`❌ ${errorCount} erreur(s)`, "error");
    return;
  }

  // ── PDF only — existing behavior + optional ERP lookup button ─────────────
  uploadProgress.style.display = "block";
  uploadZone.style.opacity = "0.5";
  uploadZone.style.pointerEvents = "none";

  let successCount = 0;
  let errorCount = 0;

  for (let i = 0; i < files.length; i++) {
    const file = files[i];
    progressFill.style.width = Math.round(((i + 1) / files.length) * 100) + "%";

    if (!file.name.toLowerCase().endsWith(".pdf")) {
      uploadStatus.textContent = `❌ ${file.name} : seuls les PDF`;
      errorCount++;
      continue;
    }

    uploadStatus.textContent = `Envoi ${file.name}...`;

    try {
      const formData = new FormData();
      formData.append("file", file);
      formData.append("folder", FOLDER_SOUMISSION);

      const r = await fetch("/api/upload", { method: "POST", body: formData }).then(r => r.json());

      if (!r.ok) {
        uploadStatus.textContent = `❌ ${file.name}`;
        errorCount++;
        continue;
      }

      uploadStatus.textContent = `✅ ${file.name}`;
      successCount++;

      // Try ERP auto-lookup if configured
      try {
        const lookupR = await fetch(`/api/external/detect-ref?filename=${encodeURIComponent(file.name)}`, {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json()).catch(() => ({}));
        if (lookupR.ok && lookupR.detected && r.fullPath) {
          // Check if auto-lookup is enabled
          const cfgR = await fetch("/api/settings/submission-erp-lookup", {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json()).catch(() => ({}));
          if (cfgR.ok && cfgR.config?.autoLookup && cfgR.config?.enabled) {
            const src = cfgR.config.defaultSource || "pressero";
            const lR = await fetch(`/api/external/${encodeURIComponent(src)}/lookup`, {
              method: "POST",
              headers: { "Authorization": `Bearer ${authToken}`, "Content-Type": "application/json" },
              body: JSON.stringify({ ref: lookupR.detected })
            }).then(r => r.json()).catch(() => ({}));
            if (lR.ok && lR.fiche && Object.keys(lR.fiche).length > 0) {
              openFabrication(r.fullPath, lR.fiche);
            }
          }
        }
      } catch(e) { /* auto-lookup failure is non-critical */ }

      await new Promise(resolve => setTimeout(resolve, 500));
    } catch (err) {
      uploadStatus.textContent = `❌ Erreur`;
      errorCount++;
    }
  }

  uploadProgress.style.display = "none";
  uploadZone.style.opacity = "1";
  uploadZone.style.pointerEvents = "auto";

  if (successCount > 0) {
    showNotification(`✅ ${successCount} fichier(s) envoyé(s)`, "success");
    await refreshSubmissionView();
    if (submissionCalendar) submissionCalendar.refetchEvents();
  }
  if (errorCount > 0) {
    showNotification(`❌ ${errorCount} erreur(s)`, "error");
  }
}

async function refreshSubmissionView() {
  const submissionKanban = document.getElementById("submissionKanban");
  if (!submissionKanban) return;

  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(FOLDER_SOUMISSION)}`).then(r => r.json()).catch(() => []);

    submissionKanban.innerHTML = "";
    if (jobs.length === 0) {
      submissionKanban.innerHTML = `<div style="text-align: center; padding: 40px; color: #9ca3af;"><p>Aucun fichier</p></div>`;
      return;
    }

    for (const job of jobs) {
      const full = normalizePath(job.fullPath || "");
      const card = document.createElement("div");
      card.className = "submission-card";
      card.dataset.fullPath = full;

      const checkbox = document.createElement("input");
      checkbox.type = "checkbox";
      checkbox.className = "submission-card-checkbox";
      checkbox.onchange = () => {
        card.classList.toggle("selected", checkbox.checked);
        updateSelectAllButton();
      };
      card.appendChild(checkbox);

      const preview = document.createElement("div");
      preview.className = "submission-card-preview";
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        renderPdfThumbnail(full, preview).catch(() => {});
      } else {
        preview.innerHTML = '<div class="submission-card-preview-text">PDF</div>';
      }
      card.appendChild(preview);

      const body = document.createElement("div");
      body.className = "submission-card-body";

      const header = document.createElement("div");
      header.className = "submission-card-header";

      const title = document.createElement("div");
      title.className = "submission-card-title";
      title.textContent = job.name || "Sans nom";
      header.appendChild(title);

      const info = document.createElement("div");
      info.className = "submission-card-info";
      info.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      header.appendChild(info);
      body.appendChild(header);

      const iso = deliveriesByPath[fnKey(full)];
      const status = document.createElement("div");
      status.className = `submission-card-status ${iso ? (daysDiffFromToday(iso) <= 1 ? "done" : "scheduled") : "pending"}`;
      status.textContent = iso ? `Prévu ${new Date(iso).toLocaleDateString("fr-FR")}` : "En attente";
      body.appendChild(status);

      const actions = document.createElement("div");
      actions.className = "submission-card-actions";

      const btnView = document.createElement("button");
      btnView.className = "btn";
      btnView.textContent = "Voir";
      btnView.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");
      actions.appendChild(btnView);

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => openFabrication(full);
      actions.appendChild(btnFiche);

      const btnErpLookup = document.createElement("button");
      btnErpLookup.className = "btn";
      btnErpLookup.title = "Récupérer les métadonnées depuis ERP / W2P";
      btnErpLookup.textContent = "🔗 ERP/W2P";
      btnErpLookup.onclick = async (e) => {
        e.stopPropagation();
        // Try to detect reference from filename
        let detectedRef = "";
        try {
          const dr = await fetch(`/api/external/detect-ref?filename=${encodeURIComponent(job.name || "")}`, {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json()).catch(() => ({}));
          if (dr.ok && dr.detected) detectedRef = dr.detected;
        } catch(e) { /* ignore */ }
        openErpLookupPopup((fiche) => openFabrication(full, fiche), detectedRef);
      };
      actions.appendChild(btnErpLookup);

      const btnAssignSub = document.createElement("button");
      btnAssignSub.className = "btn btn-assign";
      btnAssignSub.textContent = "Affecter à";
      btnAssignSub.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssignSub, full); };
      actions.appendChild(btnAssignSub);

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn";
      btnDelete.textContent = "Supprimer";
      btnDelete.onclick = () => deleteFile(full);
      actions.appendChild(btnDelete);

      body.appendChild(actions);
      card.appendChild(body);
      card.onclick = () => {
        checkbox.checked = !checkbox.checked;
        checkbox.onchange();
      };

      submissionKanban.appendChild(card);
    }

    setupSubmissionButtons();
  } catch (err) {
    console.error("Erreur refresh:", err);
  }
}

function updateSelectAllButton() {
  const btnSelectAll = document.getElementById("btnSelectAll");
  if (!btnSelectAll) return;
  const checkboxes = document.querySelectorAll(".submission-card-checkbox");
  const checkedCount = Array.from(checkboxes).filter(cb => cb.checked).length;
  btnSelectAll.textContent = checkedCount === checkboxes.length && checkboxes.length > 0 ? "Désélectionner tout" : "Sélectionner tout";
}

function setupSubmissionButtons() {
  const btnSelectAll = document.getElementById("btnSelectAll");
  const btnSendAnalysis = document.getElementById("btnSendAnalysis");

  if (!btnSelectAll || !btnSendAnalysis) return;

  btnSelectAll.onclick = () => {
    const checkboxes = document.querySelectorAll(".submission-card-checkbox");
    const allChecked = Array.from(checkboxes).every(cb => cb.checked);
    checkboxes.forEach(cb => {
      cb.checked = !allChecked;
      cb.onchange();
    });
  };

  btnSendAnalysis.onclick = async () => {
    const checkboxes = document.querySelectorAll(".submission-card-checkbox:checked");
    if (checkboxes.length === 0) {
      alert("Sélectionnez au moins un fichier");
      return;
    }

    if (!confirm(`Envoyer ${checkboxes.length} fichier(s) en production ?`)) return;

    let successCount = 0;

    for (const checkbox of checkboxes) {
      const card = checkbox.closest(".submission-card");
      const fullPath = card.dataset.fullPath;

      try {
        const r = await fetch("/api/jobs/move", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ source: fullPath, destination: FOLDER_DEBUT_PRODUCTION, overwrite: true })
        }).then(r => r.json());

        if (r.ok) successCount++;
      } catch (err) {
        console.error("Erreur:", err);
      }
    }

    if (successCount > 0) {
      await loadDeliveries();
      showNotification(`✅ ${successCount} fichier(s) envoyé(s) en production`, "success");
      await refreshSubmissionView();
      calendar?.refetchEvents();
      submissionCalendar?.refetchEvents();
    }
  };

  updateSelectAllButton();
}

// ======================================================
// IMPORT DEPUIS UN MAIL (ITEM 19)
// ======================================================
function setupMailImportButton() {
  const btn = document.getElementById("btnImportFromMail");
  if (!btn) return;

  btn.onclick = () => openMailImportModal();
}

async function openMailImportModal() {
  // Load IMAP settings if configured
  let imapCfg = {};
  try {
    const r = await fetch("/api/settings/imap").then(res => res.json()).catch(() => ({}));
    if (r.ok && r.settings) imapCfg = r.settings;
  } catch(e) {}

  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;z-index:10000;";

  const modal = document.createElement("div");
  modal.style.cssText = "background:white;border-radius:12px;padding:28px;min-width:400px;max-width:640px;width:94%;box-shadow:0 12px 50px rgba(0,0,0,.3);max-height:90vh;overflow-y:auto;";

  modal.innerHTML = `
    <h3 style="margin:0 0 12px;font-size:17px;font-weight:700;color:#111827;">📧 Importer depuis un mail</h3>
    <div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:8px;padding:10px 14px;margin-bottom:14px;font-size:12px;color:#1e40af;">
      <strong>💡 Aide connexion :</strong><br>
      • <strong>Gmail</strong> : utilisez un <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener" style="color:#1d4ed8;font-weight:600;">mot de passe d'application</a> (pas votre mot de passe habituel). Activez d'abord la validation en 2 étapes dans votre compte Google.<br>
      • <strong>Outlook / Office 365</strong> : serveur <code>outlook.office365.com</code>, port 993, SSL activé. Si la MFA est activée, générez un mot de passe d'application dans votre compte Microsoft.<br>
      • Serveurs : Gmail → <code>imap.gmail.com:993</code> · Outlook → <code>outlook.office365.com:993</code>
    </div>
    <div style="display:grid;grid-template-columns:1fr 80px;gap:8px;margin-bottom:10px;">
      <div>
        <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Serveur IMAP</label>
        <input id="imap-host" type="text" class="settings-input settings-input-wide" placeholder="imap.gmail.com" value="${imapCfg.host || ''}" />
      </div>
      <div>
        <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Port</label>
        <input id="imap-port" type="number" class="settings-input" value="${imapCfg.port || 993}" style="width:70px;" />
      </div>
    </div>
    <div style="margin-bottom:10px;">
      <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Email</label>
      <input id="imap-email" type="email" class="settings-input settings-input-wide" placeholder="votre@email.com" value="${imapCfg.email || ''}" />
    </div>
    <div style="margin-bottom:10px;">
      <label style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:3px;">Mot de passe (ou mot de passe d'application)</label>
      <input id="imap-password" type="password" class="settings-input settings-input-wide" />
    </div>
    <div style="display:flex;align-items:center;gap:8px;margin-bottom:16px;">
      <input type="checkbox" id="imap-ssl" ${imapCfg.useSsl !== false ? 'checked' : ''} />
      <label for="imap-ssl" style="font-size:13px;color:#374151;">SSL / TLS</label>
    </div>
    <div style="display:flex;gap:8px;margin-bottom:4px;">
      <button id="imap-test-btn" class="btn" style="flex:0 0 auto;">🔌 Tester la connexion</button>
      <button id="imap-search-btn" class="btn btn-primary" style="flex:1;">🔍 Rechercher les mails récents</button>
    </div>
    <div id="imap-results" style="margin-top:16px;"></div>
    <div style="display:flex;justify-content:flex-end;margin-top:16px;">
      <button id="imap-close-btn" class="btn">Fermer</button>
    </div>
  `;

  overlay.appendChild(modal);
  document.body.appendChild(overlay);
  overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };
  modal.querySelector("#imap-close-btn").onclick = () => overlay.remove();

  // Test connection button
  modal.querySelector("#imap-test-btn").onclick = async () => {
    const host = modal.querySelector("#imap-host").value.trim();
    const port = parseInt(modal.querySelector("#imap-port").value) || 993;
    const email = modal.querySelector("#imap-email").value.trim();
    const password = modal.querySelector("#imap-password").value;
    const useSsl = modal.querySelector("#imap-ssl").checked;
    if (!host || !email || !password) {
      showNotification("⚠️ Renseignez tous les champs IMAP", "warning");
      return;
    }
    const testBtn = modal.querySelector("#imap-test-btn");
    const resultsDiv = modal.querySelector("#imap-results");
    testBtn.disabled = true;
    testBtn.textContent = "⏳ Test...";
    resultsDiv.innerHTML = '<div style="color:#6b7280;font-size:13px;">Test de connexion...</div>';
    try {
      const r = await fetch("/api/submission/test-imap-connection", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ host, port, email, password, useSsl })
      }).then(res => res.json());
      testBtn.disabled = false;
      testBtn.textContent = "🔌 Tester la connexion";
      if (r.ok) {
        resultsDiv.innerHTML = '<div style="color:#16a34a;font-size:13px;font-weight:600;">✅ Connexion réussie ! Les identifiants sont valides.</div>';
      } else {
        const errMsg = r.error || "Erreur de connexion";
        const isCredErr = errMsg.toLowerCase().includes("invalid") || errMsg.toLowerCase().includes("credentials") || errMsg.toLowerCase().includes("authentication");
        let helpHtml = '';
        if (isCredErr) {
          helpHtml = `<div style="margin-top:8px;padding:10px 12px;background:#fef3c7;border:1px solid #fbbf24;border-radius:6px;font-size:12px;color:#92400e;">
            💡 <strong>Aide :</strong><br>
            • <strong>Gmail</strong> : utilisez un <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener" style="color:#1d4ed8;">mot de passe d'application</a>, pas votre mot de passe habituel.<br>
            • <strong>Office 365</strong> : si MFA activée, créez un mot de passe d'application dans votre compte Microsoft.
          </div>`;
        }
        resultsDiv.innerHTML = `<div style="color:#dc2626;font-size:13px;">❌ ${errMsg}</div>${helpHtml}`;
      }
    } catch(e) {
      testBtn.disabled = false;
      testBtn.textContent = "🔌 Tester la connexion";
      resultsDiv.innerHTML = '<div style="color:#dc2626;font-size:13px;">❌ Erreur réseau</div>';
    }
  };

  modal.querySelector("#imap-search-btn").onclick = async () => {
    const host = modal.querySelector("#imap-host").value.trim();
    const port = parseInt(modal.querySelector("#imap-port").value) || 993;
    const email = modal.querySelector("#imap-email").value.trim();
    const password = modal.querySelector("#imap-password").value;
    const useSsl = modal.querySelector("#imap-ssl").checked;

    if (!host || !email || !password) {
      showNotification("⚠️ Renseignez tous les champs IMAP", "warning");
      return;
    }

    const resultsDiv = modal.querySelector("#imap-results");
    const searchBtn = modal.querySelector("#imap-search-btn");
    searchBtn.disabled = true;
    searchBtn.textContent = "⏳ Recherche en cours...";
    resultsDiv.innerHTML = '<div style="color:#6b7280;font-size:13px;">Connexion au serveur IMAP...</div>';

    try {
      const r = await fetch("/api/submission/list-mail-attachments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ host, port, email, password, useSsl })
      }).then(res => res.json());

      searchBtn.disabled = false;
      searchBtn.textContent = "🔍 Rechercher les mails récents";

      if (!r.ok) {
        const errMsg = r.error || "Erreur de connexion";
        const isCredErr = errMsg.toLowerCase().includes("invalid") || errMsg.toLowerCase().includes("credentials") || errMsg.toLowerCase().includes("authentication");
        let helpHtml = '';
        if (isCredErr) {
          helpHtml = `<div style="margin-top:10px;padding:10px 12px;background:#fef3c7;border:1px solid #fbbf24;border-radius:6px;font-size:12px;color:#92400e;">
            💡 <strong>Aide :</strong><br>
            • <strong>Gmail :</strong> Utilisez un <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener" style="color:#1d4ed8;">mot de passe d'application</a> (pas votre mot de passe habituel). Activez d'abord la validation en deux étapes.<br>
            • <strong>Outlook/Office365 :</strong> Activez IMAP dans les paramètres de votre compte et utilisez votre mot de passe habituel ou un mot de passe d'application si l'authentification multifacteur est activée.<br>
            • <strong>Serveur IMAP :</strong> Gmail → <code>imap.gmail.com:993</code>, Outlook → <code>outlook.office365.com:993</code>
          </div>`;
        }
        resultsDiv.innerHTML = `<div style="color:#dc2626;font-size:13px;">❌ ${errMsg}</div>${helpHtml}`;
        return;
      }

      const attachments = r.attachments || [];
      if (attachments.length === 0) {
        resultsDiv.innerHTML = '<div style="color:#6b7280;font-size:13px;">Aucune pièce jointe PDF trouvée dans les 48 dernières heures.</div>';
        return;
      }

      resultsDiv.innerHTML = `<div style="font-size:13px;font-weight:600;color:#374151;margin-bottom:10px;">${attachments.length} pièce(s) jointe(s) PDF trouvée(s) :</div>`;

      for (const att of attachments) {
        const row = document.createElement("div");
        row.style.cssText = "display:flex;justify-content:space-between;align-items:center;padding:8px 10px;border:1px solid #e5e7eb;border-radius:6px;margin-bottom:6px;gap:8px;";
        const info = document.createElement("div");
        info.style.cssText = "flex:1;min-width:0;";
        info.innerHTML = `
          <div style="font-size:13px;font-weight:600;color:#111827;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" title="${att.attachmentName}">${att.attachmentName}</div>
          <div style="font-size:11px;color:#6b7280;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${att.subject} — ${att.from}</div>
          <div style="font-size:10px;color:#9ca3af;">${new Date(att.date).toLocaleString('fr-FR')}</div>
        `;
        const btnImport = document.createElement("button");
        btnImport.className = "btn btn-sm btn-primary";
        btnImport.textContent = "📥 Importer";
        btnImport.style.flexShrink = "0";
        btnImport.onclick = async () => {
          btnImport.disabled = true;
          btnImport.textContent = "⏳ Import...";
          try {
            const ir = await fetch("/api/submission/import-mail-attachment", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ host, port, email, password, useSsl, messageId: att.messageId, attachmentName: att.attachmentName, destinationFolder: FOLDER_SOUMISSION })
            }).then(res => res.json());
            if (ir.ok) {
              btnImport.textContent = "✅ Importé";
              showNotification(`✅ ${ir.fileName} importé dans Soumission`, "success");
              await refreshSubmissionView();
            } else {
              btnImport.disabled = false;
              btnImport.textContent = "📥 Importer";
              showNotification("❌ " + (ir.error || "Erreur"), "error");
            }
          } catch(err) {
            btnImport.disabled = false;
            btnImport.textContent = "📥 Importer";
            showNotification("❌ " + err.message, "error");
          }
        };
        row.appendChild(info);
        row.appendChild(btnImport);
        resultsDiv.appendChild(row);
      }
    } catch(err) {
      searchBtn.disabled = false;
      searchBtn.textContent = "🔍 Rechercher les mails récents";
      resultsDiv.innerHTML = `<div style="color:#dc2626;font-size:13px;">❌ ${err.message}</div>`;
    }
  };
}

// ======================================================
// CORBEILLE
// ======================================================
async function initRecycleView() {
  const recycleEl = document.getElementById("recycle");
  recycleEl.innerHTML = `
    <div class="settings-container">
      <h2>Corbeille</h2>
      <div style="display: flex; gap: 10px; margin-bottom: 16px;">
        <button id="recycle-refresh" class="btn btn-primary">Rafraîchir</button>
        <button id="recycle-purge" class="btn">Purger les anciens fichiers (&gt; 7 jours)</button>
      </div>
      <div id="recycle-list"></div>
    </div>
  `;
  document.getElementById("recycle-refresh").onclick = loadRecycleList;
  document.getElementById("recycle-purge").onclick = purgeRecycle;
  await loadRecycleList();
}

async function loadRecycleList() {
  const listEl = document.getElementById("recycle-list");
  if (!listEl) return;

  listEl.innerHTML = '<p style="color:var(--text-tertiary);">Chargement...</p>';

  try {
    const resp = await fetch("/api/recycle/list").then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (resp && !Array.isArray(resp) && resp.ok === false) {
      listEl.innerHTML = `<p style="color:var(--danger);">❌ Erreur : ${resp.error || "Impossible de charger la corbeille"}</p>`;
      return;
    }

    const files = Array.isArray(resp) ? resp : [];

    if (files.length === 0) {
      listEl.innerHTML = '<p style="color:var(--text-tertiary);text-align:center;padding:20px;">La corbeille est vide</p>';
      return;
    }

    listEl.innerHTML = "";
    files.forEach(f => {
      const div = document.createElement("div");
      div.style.cssText = "display:flex;align-items:center;gap:10px;padding:12px 16px;border:1px solid var(--border-light);background:var(--bg-card);border-radius:var(--radius-sm);margin-bottom:6px;";
      const sourceFolderBadge = f.sourceFolder ? `<small style="color:var(--text-tertiary);font-size:11px;">📁 ${f.sourceFolder}</small>` : '';
      div.innerHTML = `
        <div style="flex:1;">
          <strong style="font-size:13px;color:var(--text-primary);">${f.fileName}</strong>
          <small style="display:block;color:var(--text-tertiary);font-size:11px;">Supprimé le ${new Date(f.deletedAt).toLocaleDateString("fr-FR", { day:"2-digit", month:"2-digit", year:"numeric", hour:"2-digit", minute:"2-digit" })}</small>
          ${sourceFolderBadge}
        </div>
        <button class="btn btn-sm btn-primary" data-path="${f.fullPath}">↩️ Restaurer</button>
      `;
      div.querySelector("button").onclick = () => restoreFromRecycle(f.fullPath, f.fileName, f.sourceFolder);
      listEl.appendChild(div);
    });
  } catch (err) {
    listEl.innerHTML = `<p style="color:var(--danger);">Erreur : ${err.message}</p>`;
  }
}

async function restoreFromRecycle(fullPath, fileName, sourceFolder) {
  const defaultFolder = sourceFolder || FOLDER_SOUMISSION;

  // Build a modal with a dropdown for available folders
  let folderOptions = [defaultFolder];
  try {
    const resp = await fetch("/api/config/kanban-columns").then(r => r.json()).catch(() => null);
    if (resp && resp.ok && Array.isArray(resp.columns)) {
      const names = resp.columns.filter(c => c.visible !== false).map(c => c.folder);
      folderOptions = [defaultFolder, ...names.filter(n => n !== defaultFolder)];
    }
  } catch(e) { /* use default */ }

  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:9999;display:flex;align-items:center;justify-content:center;";
  const modal = document.createElement("div");
  modal.style.cssText = "background:#fff;border-radius:12px;padding:28px 32px;width:440px;max-width:95vw;box-shadow:0 8px 40px rgba(0,0,0,0.18);";
  const escHtml = s => s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
  const opts = folderOptions.map(f => {
    const escaped = escHtml(f);
    return `<option value="${escaped}" ${f===defaultFolder?'selected':''}>${escaped}</option>`;
  }).join('');
  modal.innerHTML = `
    <h3 style="font-size:17px;font-weight:700;color:#111827;margin:0 0 6px;">Restaurer le fichier</h3>
    <p style="font-size:13px;color:#6b7280;margin:0 0 18px;">${escHtml(fileName)}</p>
    <div style="margin-bottom:16px;">
      <label style="font-size:13px;font-weight:600;color:#374151;display:block;margin-bottom:6px;">Dossier de destination</label>
      <select id="restore-folder-sel" style="width:100%;padding:8px 10px;border:1px solid #d1d5db;border-radius:8px;font-size:14px;">${opts}</select>
      ${defaultFolder ? `<p style="font-size:11px;color:#6b7280;margin-top:4px;">📁 Dossier d'origine : <strong>${escHtml(defaultFolder)}</strong></p>` : ''}
    </div>
    <div style="display:flex;gap:10px;justify-content:flex-end;">
      <button id="restore-cancel-btn" class="btn">Annuler</button>
      <button id="restore-confirm-btn" class="btn btn-primary">Restaurer</button>
    </div>
  `;
  overlay.appendChild(modal);
  document.body.appendChild(overlay);

  return new Promise(resolve => {
    modal.querySelector("#restore-cancel-btn").onclick = () => { overlay.remove(); resolve(); };
    overlay.onclick = (e) => { if(e.target===overlay){ overlay.remove(); resolve(); } };
    modal.querySelector("#restore-confirm-btn").onclick = async () => {
      const folder = modal.querySelector("#restore-folder-sel").value;
      overlay.remove();
      const r = await fetch(`/api/recycle/restore?fullPath=${encodeURIComponent(fullPath)}&destinationFolder=${encodeURIComponent(folder)}`, {
        method: "POST"
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification(`✅ Fichier restauré dans ${folder}`, "success");
        await loadRecycleList();
      } else {
        showNotification("❌ Erreur : " + (r.error || ""), "error");
      }
      resolve();
    };
  });
}

async function purgeRecycle() {
  if (!confirm("Supprimer définitivement les anciens fichiers de la corbeille (> 7 jours) ?")) return;

  const r = await fetch("/api/recycle/purge", { method: "DELETE" }).then(r => r.json()).catch(() => ({ ok: false }));

  if (r.ok) {
    showNotification(`✅ ${r.purged || 0} fichier(s) supprimé(s) définitivement`, "success");
    await loadRecycleList();
  } else {
    showNotification("❌ Erreur : " + (r.error || ""), "error");
  }
}

// ======================================================
// BACKGROUND LOGIN + HEADER BANNER (ITEMS 13 & 14)
// ======================================================
function applyLoginBackground() {
  const loginContainer = document.getElementById("login-container");
  if (!loginContainer) return;
  const img = new Image();
  img.onload = () => {
    loginContainer.style.backgroundImage = `url('/api/background-login?v=${Date.now()}')`;
    loginContainer.style.backgroundSize = "cover";
    loginContainer.style.backgroundPosition = "center";
  };
  img.onerror = () => { /* No background configured — keep default */ };
  img.src = `/api/background-login?v=${Date.now()}`;
}

function applyHeaderBanner() {
  const headerEl = document.querySelector("header");
  if (!headerEl) return;
  const img = new Image();
  img.onload = () => {
    headerEl.style.backgroundImage = `url('/api/header-banner?v=${Date.now()}')`;
    headerEl.style.backgroundSize = "cover";
    headerEl.style.backgroundPosition = "center";
  };
  img.onerror = () => { /* No banner configured — keep default */ };
  img.src = `/api/header-banner?v=${Date.now()}`;
}

// ======================================================
// INITIALISATION DE L'APP
// ======================================================
async function initApp() {
  // Expose calendar refs to settings.js via globals
  window._calendar = calendar;
  window._submissionCalendar = submissionCalendar;

  // Apply login page background image (ITEM 13)
  applyLoginBackground();
  // Apply header banner (ITEM 14)
  applyHeaderBanner();

  setupProfileUI();
  initNotificationBell();
  initFabrication();

  try {
    await loadDeliveries();
    await loadAssignments();
    updateGlobalAlert();
    await buildKanban();
    await ensureCalendar();

    // Expose calendar refs after init
    window._calendar = calendar;
    window._submissionCalendar = submissionCalendar;

    if (currentUser.profile === 1) {
      showSubmission();
    } else if (currentUser.profile === 5) {
      showCalendar();
    } else {
      showKanban();
    }
  } catch (err) {
    console.error("Erreur init:", err);
  }

  pollNotifications();
  setInterval(pollNotifications, 30000);

  // Heartbeat for connected-user indicator (every 60s)
  const sendHeartbeat = () => {
    if (!authToken) return;
    fetch("/api/auth/heartbeat", {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` }
    }).catch(() => {});
  };
  sendHeartbeat();
  setInterval(sendHeartbeat, 60000);
}

// ======================================================
// BOUTONS DE NAVIGATION
// ======================================================
document.getElementById("btn-logout").onclick = logout;
document.getElementById("btn-settings").onclick = showSettings;

document.getElementById("btnViewKanban").onclick = () => {
  showKanban();
};

document.getElementById("btnViewCalendar").onclick = () => {
  showCalendar();
};

document.getElementById("btnViewSubmission").onclick = showSubmission;
document.getElementById("btnViewRecycle").onclick = showRecycle;
document.getElementById("btnViewDashboard").onclick = showDashboard;
document.getElementById("btnViewDossiers").onclick = showDossiers;
const btnViewGlobalProd = document.getElementById("btnViewGlobalProd");
if (btnViewGlobalProd) btnViewGlobalProd.onclick = showGlobalProduction;
const btnViewBat = document.getElementById("btnViewBat");
if (btnViewBat) btnViewBat.onclick = showBatView;
const btnViewRapport = document.getElementById("btnViewRapport");
if (btnViewRapport) btnViewRapport.onclick = showRapportView;

// ======================================================
// DRAG & DROP GLOBAL
// ======================================================
document.addEventListener("dragover", e => e.preventDefault());
document.addEventListener("drop", e => {
  if (!e.target.closest(".kanban-col__drop")) {
    e.preventDefault();
  }
});

// ======================================================
// AUTO-REFRESH KANBAN (toutes les 30s)
// ======================================================
setInterval(async () => {
  const layout = document.getElementById("kanban-layout");
  if (!layout || layout.classList.contains("hidden")) return;
  await loadDeliveries();
  await loadAssignments();
  updateGlobalAlert();
  await refreshKanban();
  buildKanbanSidebar();
}, 30000);

// ======================================================
// AUTO-REFRESH PLANNING PAR OPÉRATEUR (toutes les 30s)
// ======================================================
setInterval(async () => {
  const calEl = document.getElementById("calendar");
  if (!calEl || calEl.classList.contains("hidden")) return;
  const opEl = document.getElementById("planning-operator-view");
  if (!opEl || opEl.style.display === 'none') return;
  await loadDeliveries();
  await loadAssignments();
  refreshOperatorView().catch(() => {});
}, 30000);

// ======================================================
// DOMContentLoaded — POINT D'ENTRÉE
// ======================================================
document.addEventListener("DOMContentLoaded", () => {
  applyLoginBackground();
  initLogin(initApp);
});
