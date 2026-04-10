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
import { initCalendar, ensureCalendar, calendar, submissionCalendar, initSubmissionCalendar, colorForEvent, openPlanificationCalendar } from './calendar.js';
import { initDossiersView, loadDossiersList, openDossierDetail } from './dossiers.js';
import { initSettingsView } from './settings.js';
import { pollNotifications, initNotificationBell } from './notifications.js';
import { initGlobalProductionView, refreshProductionViewKanban, buildProductionView } from './production-view.js';

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
window._refreshSubmissionView = refreshSubmissionView;
window._loadDeliveries = loadDeliveries;
window._loadAssignments = loadAssignments;
window._updateGlobalAlert = updateGlobalAlert;
window._renderPdfThumbnail = renderPdfThumbnail;
window._deleteFile = deleteFile;
window._handleDesktopDrop = handleDesktopDrop;
window._openPlanificationCalendar = openPlanificationCalendar;

// ======================================================
// UTILITAIRE — ALERTE GLOBALE
// ======================================================
async function updateGlobalAlert() {
  if (globalAlert) globalAlert.style.display = "none";
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
// NAVIGATION — MASQUER TOUTES LES VUES
// ======================================================
function hideAllViews() {
  document.getElementById("kanban-layout").classList.add("hidden");
  document.getElementById("calendar").classList.add("hidden");
  document.getElementById("submission").classList.add("hidden");
  document.getElementById("production").classList.add("hidden");
  document.getElementById("recycle").classList.add("hidden");
  document.getElementById("dashboard").classList.add("hidden");
  document.getElementById("dossiers").classList.add("hidden");
  document.getElementById("settings-view").classList.add("hidden");
  document.getElementById("bat-view").classList.add("hidden");
  document.getElementById("rapport-view").classList.add("hidden");
  const globalProdEl = document.getElementById("global-production");
  if (globalProdEl) globalProdEl.classList.add("hidden");
  // Hide kanban-specific controls
  const filterBarEl = document.getElementById("kanban-filter-bar");
  if (filterBarEl) filterBarEl.style.display = "none";
  document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
}

function showKanban() {
  hideAllViews();
  document.getElementById("kanban-layout").classList.remove("hidden");
  document.getElementById("btnViewKanban").classList.add("active");
  // Show kanban-specific filter bar
  const filterBarEl = document.getElementById("kanban-filter-bar");
  if (filterBarEl) filterBarEl.style.display = "";
  refreshKanban();
  buildKanbanSidebar();
}

async function showCalendar() {
  hideAllViews();
  document.getElementById("calendar").classList.remove("hidden");
  document.getElementById("btnViewCalendar").classList.add("active");
  await ensureCalendar();
  // Update calendar refs for settings.js
  window._calendar = calendar;
  calendar?.refetchEvents();
}

function showSubmission() {
  hideAllViews();
  document.getElementById("submission").classList.remove("hidden");
  document.getElementById("btnViewSubmission").classList.add("active");
  initSubmissionView();
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

function showDossiers() {
  hideAllViews();
  document.getElementById("dossiers").classList.remove("hidden");
  document.getElementById("btnViewDossiers").classList.add("active");
  initDossiersView();
}

function showSettings() {
  hideAllViews();
  document.getElementById("settings-view").classList.remove("hidden");
  initSettingsView();
}

function showGlobalProduction() {
  hideAllViews();
  const el = document.getElementById("global-production");
  if (el) el.classList.remove("hidden");
  const btn = document.getElementById("btnViewGlobalProd");
  if (btn) btn.classList.add("active");
  initGlobalProductionView();
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

async function buildBatView() {
  const container = document.getElementById("bat-view");
  if (!container) return;
  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);letter-spacing:-0.02em;">Bon à tirer (BAT)</h2>
        <div style="display:flex;gap:10px;">
          <button id="bat-view-adobe" class="btn btn-acrobat" style="border-radius:50px;">📄 Acrobat Online</button>
          <button id="bat-view-refresh" class="btn btn-primary" style="border-radius:50px;">↺ Rafraîchir</button>
        </div>
      </div>
      <div id="bat-view-list" class="bat-list-grid" style="max-height:calc(100vh - 160px);overflow-y:auto;scrollbar-width:thin;padding-bottom:16px;"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  container.querySelector("#bat-view-adobe").onclick = () => window.open("https://www.adobe.com/files#", "_blank", "noopener");
  container.querySelector("#bat-view-refresh").onclick = buildBatView;

  const listEl = container.querySelector("#bat-view-list");

  // Helper: format a date/time for display
  const fmtDT = (dt) => {
    if (!dt) return null;
    const d = new Date(dt);
    const date = d.toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit", year: "numeric" });
    const time = d.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
    return `${date} à ${time}`;
  };

  try {
    const jobs = await fetch("/api/jobs?folder=" + encodeURIComponent("BAT")).then(r => r.json()).catch(() => []);
    if (!Array.isArray(jobs) || jobs.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:60px 40px;">Aucun fichier en BAT</p>';
      return;
    }
    listEl.innerHTML = "";
    for (const job of jobs) {
      const full = normalizePath(job.fullPath || "");
      const jobFn = fnKey(full);

      const card = document.createElement("div");
      card.className = "bat-card-modern";

      // --- Thumbnail ---
      const thumbDiv = document.createElement("div");
      thumbDiv.className = "bat-card-thumb";
      thumbDiv.textContent = "PDF";
      if ((job.name || "").toLowerCase().endsWith(".pdf") && window._renderPdfThumbnail) {
        window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      // --- Body ---
      const bodyDiv = document.createElement("div");
      bodyDiv.className = "bat-card-body";

      const dossierEl = document.createElement("div");
      dossierEl.className = "bat-card-dossier";
      dossierEl.textContent = "—";

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

      // --- Actions ---
      const actionsDiv = document.createElement("div");
      actionsDiv.className = "bat-card-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "🔍 Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnAcrobat = document.createElement("button");
      btnAcrobat.className = "btn btn-sm btn-acrobat";
      btnAcrobat.textContent = "📄 Ouvrir dans Acrobat";
      btnAcrobat.onclick = async () => {
        try {
          const r = await fetch("/api/acrobat/open", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: full })
          }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
          if (!r.ok) showNotification("❌ " + (r.error || "Erreur ouverture Acrobat"), "error");
        } catch (err) {
          showNotification("❌ " + err.message, "error");
        }
      };

      const btnArchiver = document.createElement("button");
      btnArchiver.className = "btn btn-sm";
      btnArchiver.textContent = "📦 Archiver";
      btnArchiver.onclick = async () => {
        if (!confirm(`Archiver le BAT "${job.name}" dans le dossier de production ?`)) return;
        const r = await fetch("/api/jobs/archive", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath: full })
        }).then(res => res.json()).catch(() => ({ ok: false }));
        if (r.ok) { showNotification("✅ BAT archivé", "success"); buildBatView(); }
        else showNotification("❌ Erreur : " + (r.error || ""), "error");
      };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-sm btn-danger";
      btnDelete.textContent = "🗑 Supprimer";
      btnDelete.onclick = async () => {
        if (!confirm(`Supprimer le BAT "${job.name}" ?`)) return;
        const r = await fetch("/api/jobs/delete", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath: full })
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (r.ok) { showNotification("✅ BAT supprimé", "success"); buildBatView(); }
        else showNotification("❌ Erreur : " + (r.error || ""), "error");
      };

      actionsDiv.appendChild(btnOpen);
      actionsDiv.appendChild(btnAcrobat);
      actionsDiv.appendChild(btnArchiver);
      actionsDiv.appendChild(btnDelete);

      card.appendChild(thumbDiv);
      card.appendChild(bodyDiv);
      card.appendChild(actionsDiv);
      listEl.appendChild(card);

      // Load dossier number async — strip BAT_ prefix before lookup (MongoDB stores without it)
      let lookupFn = jobFn;
      if (lookupFn.toLowerCase().startsWith("bat_")) lookupFn = lookupFn.substring(4);
      fetch("/api/fabrication?fileName=" + encodeURIComponent(lookupFn))
        .then(r => r.json()).then(d => {
          if (d && d.numeroDossier) dossierEl.textContent = d.numeroDossier;
        }).catch(() => {});

      try {
        const status = await fetch(`/api/bat/status?path=${encodeURIComponent(full)}`).then(r => r.json()).catch(() => ({}));

        const btnSent = document.createElement("button");
        btnSent.className = "bat-status-badge bat-sent" + (status.sentAt ? " active" : "");
        const sentTs = status.sentAt ? fmtDT(status.sentAt) : null;
        btnSent.innerHTML = status.sentAt
          ? `✉️ Envoyé<span style="font-size:9px;font-weight:400;margin-left:4px;">${sentTs}</span>`
          : "✉️ Marquer envoyé";
        btnSent.onclick = () => fetch("/api/bat/send", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(buildBatView);

        const sep1 = document.createElement("span");
        sep1.className = "bat-tracking-sep";

        const btnValidate = document.createElement("button");
        btnValidate.className = "bat-status-badge bat-validated" + (status.validatedAt ? " active" : "");
        const validateTs = status.validatedAt ? fmtDT(status.validatedAt) : null;
        btnValidate.innerHTML = status.validatedAt
          ? `✅ Validé<span style="font-size:9px;font-weight:400;margin-left:4px;">${validateTs}</span>`
          : "✅ Valider";
        btnValidate.onclick = () => fetch("/api/bat/validate", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(buildBatView);

        const sep2 = document.createElement("span");
        sep2.className = "bat-tracking-sep";

        const btnReject = document.createElement("button");
        btnReject.className = "bat-status-badge bat-rejected" + (status.rejectedAt ? " active" : "");
        const rejectTs = status.rejectedAt ? fmtDT(status.rejectedAt) : null;
        btnReject.innerHTML = status.rejectedAt
          ? `❌ Refusé<span style="font-size:9px;font-weight:400;margin-left:4px;">${rejectTs}</span>`
          : "❌ Refuser";
        btnReject.onclick = () => fetch("/api/bat/reject", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ fullPath: full }) }).then(buildBatView);

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
      } catch (e) { /* ignore */ }
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
    // --- Section 1: Calendrier semaine compact (lun-ven) ---
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    // Local ISO date string (avoids UTC timezone offset bug)
    function localIso(d) {
      return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    }
    const todayIso = localIso(today);

    // Find Monday of the current week (Mon–Fri only)
    const dayOfWeek = today.getDay(); // 0=Sun, 1=Mon, ..., 6=Sat
    const monday = new Date(today);
    monday.setDate(today.getDate() - (dayOfWeek === 0 ? 6 : dayOfWeek - 1));

    const weekDays = [];
    for (let i = 0; i < 5; i++) {
      const d = new Date(monday);
      d.setDate(monday.getDate() + i);
      weekDays.push(d);
    }

    const weekJobsHtml = weekDays.map(d => {
      const iso = localIso(d);
      const jobs = Object.entries(deliveriesByPath)
        .filter(([k, v]) => !k.endsWith("_time") && v === iso)
        .map(([k]) => k);
      const label = d.toLocaleDateString("fr-FR", { weekday: "short", day: "numeric", month: "short" });
      const isToday = iso === todayIso;
      function escHtml(s) { return (s||"").replace(/&/g,"&amp;").replace(/</g,"&lt;").replace(/>/g,"&gt;").replace(/"/g,"&quot;"); }
      return `
        <div style="padding:6px 0;border-bottom:1px solid #f0f0f0;">
          <div style="font-size:11px;font-weight:${isToday ? '700' : '500'};color:${isToday ? '#BC0024' : '#374151'};">${label}</div>
          ${jobs.length > 0
            ? jobs.map(j => `<div style="font-size:11px;background:#dbeafe;color:#1e40af;border-radius:6px;padding:3px 8px;margin-top:3px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:220px;font-weight:500;" title="${escHtml(j)}">📄 ${escHtml(j)}</div>`).join("")
            : `<div style="font-size:10px;color:#9ca3af;margin-top:2px;">—</div>`}
        </div>
      `;
    }).join("");

    // --- Section 2: Vue production globale compacte ---
    let prodRows = "";
    try {
      const prodJobs = await fetch("/api/production/summary", {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => []);

      const STAGE_PROGRESS = {
        "Début de production": 0, "Corrections": 25, "Corrections et fond perdu": 25,
        "Prêt pour impression": 50, "BAT": 65, "PrismaPrepare": 75, "Fiery": 75,
        "Impression en cours": 75, "Façonnage": 90, "Fin de production": 100
      };
      const STAGE_LABELS = {
        "Début de production": "Jobs à traiter", "Corrections": "Preflight",
        "Corrections et fond perdu": "Preflight fp", "Prêt pour impression": "En attente"
      };

      if (Array.isArray(prodJobs) && prodJobs.length > 0) {
        prodRows = prodJobs.slice(0, 8).map(job => {
          const stageLabel = STAGE_LABELS[job.currentStage] || job.currentStage || "—";
          const progress = Object.entries(STAGE_PROGRESS).find(([k]) => (job.currentStage || "").includes(k))?.[1] ?? 0;
          const color = progress === 100 ? "#22c55e" : progress >= 75 ? "#f97316" : progress >= 50 ? "#3b82f6" : "#f59e0b";
          return `
            <div style="padding:6px 0;border-bottom:1px solid #f0f0f0;">
              <div style="font-size:11px;font-weight:600;color:#111827;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${job.numeroDossier || job.fileName || '—'}</div>
              <div style="display:flex;align-items:center;gap:6px;margin-top:3px;">
                <div style="flex:1;background:#e5e7eb;border-radius:4px;height:6px;overflow:hidden;">
                  <div style="width:${progress}%;height:100%;background:${color};border-radius:4px;"></div>
                </div>
                <span style="font-size:10px;color:#6b7280;white-space:nowrap;">${stageLabel}</span>
              </div>
            </div>
          `;
        }).join("");
      } else {
        prodRows = '<div style="font-size:11px;color:#9ca3af;padding:8px 0;">Aucun job en production</div>';
      }
    } catch(e) {
      prodRows = '<div style="font-size:11px;color:#9ca3af;">—</div>';
    }

    sidebar.innerHTML = `
      <div class="kanban-sidebar-section">
        <div style="font-size:12px;font-weight:700;color:#374151;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">📅 Cette semaine</div>
        ${weekJobsHtml}
      </div>
      <div class="kanban-sidebar-section">
        <div style="font-size:12px;font-weight:700;color:#374151;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">📊 Production</div>
        ${prodRows}
      </div>
      <div class="kanban-sidebar-section">
        <div style="font-size:12px;font-weight:700;color:#374151;margin-bottom:8px;text-transform:uppercase;letter-spacing:0.05em;">🖨 BAT en cours</div>
        <div id="kanban-sidebar-bat-list" style="font-size:11px;color:#9ca3af;">Chargement...</div>
      </div>
    `;

    // Load BAT sidebar asynchronously
    loadBatSidebar();
  } catch(e) {
    sidebar.innerHTML = '<div style="padding:12px;color:#9ca3af;font-size:12px;">—</div>';
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

  const profileLabel = currentUser.profile === 4 ? "Façonnage" : `Profil ${currentUser.profile}`;
  userInfo.textContent = `${currentUser.name} (${profileLabel})`;

  // Profile 4 (Façonnage): read-only access, only sees kanban (not submission/settings)
  if (currentUser.profile === 4) {
    if (btnRecycle) btnRecycle.style.display = "none";
    if (btnDossiers) btnDossiers.style.display = "inline-block";
    if (btnDashboard) btnDashboard.style.display = "none";
    if (btnBat) btnBat.style.display = "inline-block";
    if (btnRapport) btnRapport.style.display = "inline-block";
    if (btnSubmission) btnSubmission.style.display = "none";
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
    // Profile 4 (Façonnage): kanban read-only, no submission, no calendar
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "none";
    if (btnSubmission) btnSubmission.style.display = "none";
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
            <p class="upload-subtext">ou cliquez pour parcourir</p>
            <input type="file" id="uploadInput" multiple accept=".pdf" style="display: none;" />
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
}

async function handleSubmissionFiles(files) {
  const uploadProgress = document.getElementById("uploadProgress");
  const progressFill = uploadProgress.querySelector(".progress-fill");
  const uploadStatus = document.getElementById("uploadStatus");
  const uploadZone = document.getElementById("uploadZone");

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

    if (file.size > 100 * 1024 * 1024) {
      uploadStatus.textContent = `❌ ${file.name} : > 100 Mo`;
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

      const btnAssignSub = document.createElement("button");
      btnAssignSub.className = "btn btn-assign";
      btnAssignSub.textContent = "Affecter à";
      btnAssignSub.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssignSub, full); };
      actions.appendChild(btnAssignSub);

      const btnPlan = document.createElement("button");
      btnPlan.className = "btn btn-primary";
      btnPlan.textContent = "Planifier";
      btnPlan.onclick = () => openPlanificationCalendar(full);
      actions.appendChild(btnPlan);

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
      div.innerHTML = `
        <div style="flex:1;">
          <strong style="font-size:13px;color:var(--text-primary);">${f.fileName}</strong>
          <small style="display:block;color:var(--text-tertiary);font-size:11px;">Supprimé le ${new Date(f.deletedAt).toLocaleDateString("fr-FR", { day:"2-digit", month:"2-digit", year:"numeric", hour:"2-digit", minute:"2-digit" })}</small>
        </div>
        <button class="btn btn-sm btn-primary" data-path="${f.fullPath}">↩️ Restaurer</button>
      `;
      div.querySelector("button").onclick = () => restoreFromRecycle(f.fullPath, f.fileName);
      listEl.appendChild(div);
    });
  } catch (err) {
    listEl.innerHTML = `<p style="color:var(--danger);">Erreur : ${err.message}</p>`;
  }
}

async function restoreFromRecycle(fullPath, fileName) {
  const folder = prompt(`Restaurer "${fileName}" dans quel dossier ?`, FOLDER_SOUMISSION);
  if (!folder) return;

  const r = await fetch(`/api/recycle/restore?fullPath=${encodeURIComponent(fullPath)}&destinationFolder=${encodeURIComponent(folder)}`, {
    method: "POST"
  }).then(r => r.json()).catch(() => ({ ok: false }));

  if (r.ok) {
    showNotification(`✅ Fichier restauré dans ${folder}`, "success");
    await loadRecycleList();
  } else {
    showNotification("❌ Erreur : " + (r.error || ""), "error");
  }
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
// DASHBOARD
// ======================================================
async function initDashboardView() {
  const dashEl = document.getElementById("dashboard");
  dashEl.innerHTML = `
    <div class="settings-container">
      <h2>Dashboard — Vue d'ensemble de l'atelier</h2>
      <div id="dashboard-content"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  await loadDashboardData();
}

async function loadDashboardData() {
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

// ======================================================
// INITIALISATION DE L'APP
// ======================================================
async function initApp() {
  // Expose calendar refs to settings.js via globals
  window._calendar = calendar;
  window._submissionCalendar = submissionCalendar;

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
    } else {
      showKanban();
    }
  } catch (err) {
    console.error("Erreur init:", err);
  }

  pollNotifications();
  setInterval(pollNotifications, 30000);
}

// ======================================================
// BOUTONS DE NAVIGATION
// ======================================================
document.getElementById("btn-logout").onclick = logout;
document.getElementById("btn-settings").onclick = showSettings;

document.getElementById("btnViewKanban").onclick = () => {
  if (currentUser && currentUser.profile === 1) {
    showProduction();
  } else {
    showKanban();
  }
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
// DOMContentLoaded — POINT D'ENTRÉE
// ======================================================
document.addEventListener("DOMContentLoaded", () => {
  initLogin(initApp);
});
