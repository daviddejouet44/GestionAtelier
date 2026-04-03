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

// ======================================================
// UTILITAIRE — ALERTE GLOBALE
// ======================================================
async function updateGlobalAlert() {
  const dates = Object.values(deliveriesByPath).filter(v => typeof v === 'string' && v.match(/\d{4}-\d{2}-\d{2}/));

  let minDays = +Infinity;
  for (const iso of dates) minDays = Math.min(minDays, daysDiffFromToday(iso));

  // Check BAT pending alerts
  let batAlerts = [];
  try {
    batAlerts = await fetch("/api/alerts/bat-pending", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
    if (!Array.isArray(batAlerts)) batAlerts = [];
  } catch { batAlerts = []; }

  const hasBatAlerts = batAlerts.length > 0;

  if (minDays <= 1 || hasBatAlerts) {
    const parts = [];
    if (minDays <= 1) parts.push("Urgences J-1");
    else if (minDays <= 3) parts.push("Attention : < 3 jours");
    if (hasBatAlerts) {
      if (batAlerts.length === 1) parts.push(batAlerts[0].message || `⚠️ BAT en attente : ${batAlerts[0].fileName}`);
      else parts.push(`⚠️ ${batAlerts.length} BAT(s) en attente sans réponse`);
    }
    globalAlert.textContent = parts.join(" | ");
    globalAlert.className = "global-alert" + (minDays <= 1 ? "" : " orange");
    globalAlert.style.display = "block";
  } else if (minDays <= 3) {
    globalAlert.textContent = "Attention : < 3 jours";
    globalAlert.className = "global-alert orange";
    globalAlert.style.display = "block";
  } else {
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
// NAVIGATION — MASQUER TOUTES LES VUES
// ======================================================
function hideAllViews() {
  document.getElementById("kanban").classList.add("hidden");
  document.getElementById("calendar").classList.add("hidden");
  document.getElementById("submission").classList.add("hidden");
  document.getElementById("production").classList.add("hidden");
  document.getElementById("recycle").classList.add("hidden");
  document.getElementById("dashboard").classList.add("hidden");
  document.getElementById("dossiers").classList.add("hidden");
  document.getElementById("settings-view").classList.add("hidden");
  const globalProdEl = document.getElementById("global-production");
  if (globalProdEl) globalProdEl.classList.add("hidden");
  document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
}

function showKanban() {
  hideAllViews();
  document.getElementById("kanban").classList.remove("hidden");
  document.getElementById("btnViewKanban").classList.add("active");
  refreshKanban();
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
// SETUP PROFILS
// ======================================================
function setupProfileUI() {
  const btnSubmission = document.getElementById("btnViewSubmission");
  const btnSettings = document.getElementById("btn-settings");
  const btnRecycle = document.getElementById("btnViewRecycle");
  const btnDashboard = document.getElementById("btnViewDashboard");
  const btnDossiers = document.getElementById("btnViewDossiers");
  const userInfo = document.getElementById("user-info");

  userInfo.textContent = `${currentUser.name} (Profil ${currentUser.profile})`;

  if (btnRecycle) btnRecycle.style.display = "inline-block";
  if (btnDossiers) btnDossiers.style.display = "inline-block";
  if (btnDashboard) btnDashboard.style.display = currentUser.profile === 3 ? "inline-block" : "none";

  // Vue production globale visible pour TOUS les profils (1, 2, 3)
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
      <div style="display: flex; gap: 10px; margin-bottom: 16px;">
        <button id="dashboard-refresh" class="btn btn-primary">Rafraîchir</button>
      </div>
      <div id="dashboard-content"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  document.getElementById("dashboard-refresh").onclick = loadDashboardData;
  await loadDashboardData();
}

async function loadDashboardData() {
  const contentEl = document.getElementById("dashboard-content");
  if (!contentEl) return;
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
  if (currentUser.profile === 1) {
    showProduction();
    return;
  }
  showKanban();
};

document.getElementById("btnViewCalendar").onclick = () => {
  if (currentUser.profile === 1) {
    alert("Vous n'avez accès qu'à la Soumission");
    return;
  }
  showCalendar();
};

document.getElementById("btnViewSubmission").onclick = showSubmission;
document.getElementById("btnViewRecycle").onclick = showRecycle;
document.getElementById("btnViewDashboard").onclick = showDashboard;
document.getElementById("btnViewDossiers").onclick = showDossiers;
const btnViewGlobalProd = document.getElementById("btnViewGlobalProd");
if (btnViewGlobalProd) btnViewGlobalProd.onclick = showGlobalProduction;

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
  if (kanbanDiv.classList.contains("hidden")) return;
  await loadDeliveries();
  await loadAssignments();
  updateGlobalAlert();
  await refreshKanban();
}, 30000);

// ======================================================
// DOMContentLoaded — POINT D'ENTRÉE
// ======================================================
document.addEventListener("DOMContentLoaded", () => {
  initLogin(initApp);
});
