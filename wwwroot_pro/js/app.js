"use strict";

let currentUser = null;
let authToken = null;

// ======================================================
// CONSTANTES — Noms de dossiers
// ======================================================
const FOLDER_SOUMISSION = "Soumission";
const FOLDER_DEBUT_PRODUCTION = "Début de production";
const FOLDER_FIN_PRODUCTION = "Fin de production";

// ======================================================
// UTILITAIRE — Formatage date/heure
// ======================================================
function formatDateTime(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  return d.toLocaleDateString("fr-FR") + " " + d.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
}

// Luminosity check for contrast
function isLight(hex) {
  const r = parseInt(hex.slice(1,3),16), g = parseInt(hex.slice(3,5),16), b = parseInt(hex.slice(5,7),16);
  return (r*299 + g*587 + b*114) / 1000 > 128;
}

// ======================================================
// AUTHENTIFICATION
// ======================================================

async function initLogin() {
  const loginForm = document.getElementById("login-form");
  const loginInput = document.getElementById("login-input");
  const passwordInput = document.getElementById("password-input");
  const loginError = document.getElementById("login-error");
  const loginContainer = document.getElementById("login-container");
  const appContainer = document.getElementById("app-container");

  const savedToken = localStorage.getItem("authToken");
  if (savedToken) {
    authToken = savedToken;
    const meResp = await fetch("/api/auth/me", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (meResp && meResp.ok && meResp.user) {
      currentUser = meResp.user;
      loginContainer.classList.add("hidden");
      appContainer.classList.remove("hidden");
      initApp();
      return;
    }
  }

  loginForm.onsubmit = async (e) => {
    e.preventDefault();
    loginError.style.display = "none";

    const login = loginInput.value.trim();
    const password = passwordInput.value.trim();

    if (!login || !password) {
      loginError.textContent = "Remplissez tous les champs";
      loginError.style.display = "block";
      return;
    }

    try {
      const resp = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ login, password })
      }).then(r => r.json());

      if (!resp.ok) {
        loginError.textContent = resp.error || "Erreur de connexion";
        loginError.style.display = "block";
        return;
      }

      authToken = resp.token;
      currentUser = resp.user;
      localStorage.setItem("authToken", authToken);

      loginContainer.classList.add("hidden");
      appContainer.classList.remove("hidden");
      loginInput.value = "";
      passwordInput.value = "";

      initApp();
    } catch (err) {
      loginError.textContent = "Erreur réseau";
      loginError.style.display = "block";
    }
  };
}

function logout() {
  authToken = null;
  currentUser = null;
  localStorage.removeItem("authToken");
  location.reload();
}

document.getElementById("btn-logout").onclick = logout;
document.getElementById("btn-settings").onclick = showSettings;

// ======================================================
// SETUP PROFILS
// ======================================================

function setupProfileUI() {
  const btnSubmission = document.getElementById("btnViewSubmission");
  const btnSettings = document.getElementById("btn-settings");
  const btnProduction = document.getElementById("btnViewProduction");
  const btnRecycle = document.getElementById("btnViewRecycle");
  const btnDashboard = document.getElementById("btnViewDashboard");
  const btnDossiers = document.getElementById("btnViewDossiers");
  const userInfo = document.getElementById("user-info");

  userInfo.textContent = `${currentUser.name} (Profil ${currentUser.profile})`;

  // Corbeille et Dossiers visibles pour tous les profils ; Dashboard uniquement pour Admin (profil 3)
  if (btnRecycle) btnRecycle.style.display = "inline-block";
  if (btnDossiers) btnDossiers.style.display = "inline-block";
  if (btnDashboard) btnDashboard.style.display = currentUser.profile === 3 ? "inline-block" : "none";
  const btnGlobalProd = document.getElementById("btnViewGlobalProd");
  if (btnGlobalProd) btnGlobalProd.style.display = (currentUser.profile === 2 || currentUser.profile === 3) ? "inline-block" : "none";

  if (currentUser.profile === 1) {
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  } else if (currentUser.profile === 2) {
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  } else if (currentUser.profile === 3) {
    btnSettings.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  }

  setupKanbanActions();
}

function setupKanbanActions() {
  const btnKanban = document.getElementById("btnViewKanban");
  const btnCalendar = document.getElementById("btnViewCalendar");
  const btnSubmission = document.getElementById("btnViewSubmission");
  const btnProduction = document.getElementById("btnViewProduction");

  if (currentUser.profile === 1) {
    btnKanban.style.display = "none";
    btnCalendar.style.display = "none";
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  } else if (currentUser.profile === 2) {
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  } else if (currentUser.profile === 3) {
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  }
}

// ======================================================
// AFFICHAGE DES VUES
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
  const productionEl = document.getElementById("production");
  productionEl.classList.remove("hidden");
  document.getElementById("btnViewProduction").classList.add("active");

  // Rebuild each time
  productionEl.innerHTML = "";
  productionEl.style.cssText = "display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; padding: 20px; width: 100%;";

  const folderConfig = [
    { folder: "Début de production", label: "Début de production", color: "#5fa8c4" },
    { folder: "Corrections", label: "Corrections", color: "#e0e0e0" },
    { folder: "Corrections et fond perdu", label: "Corrections et fond perdu", color: "#e0e0e0" },
    { folder: "Rapport", label: "Rapport", color: "#cccccc" },
    { folder: "Prêt pour impression", label: "Prêt pour impression", color: "#b8b8b8" },
    { folder: "BAT", label: "BAT", color: "#a3a3a3" },
    { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f" },
    { folder: "Fiery", label: "Fiery", color: "#8f8f8f" },
    { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a" },
    { folder: "Façonnage", label: "Façonnage", color: "#666666" },
    { folder: "Fin de production", label: "Fin de production", color: "#22c55e" }
  ];

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;
    title.style.color = isLight(cfg.color) ? '#1D1D1F' : '#FFFFFF';
    title.textContent = cfg.label;
    const counter = document.createElement("span");
    counter.className = "kanban-col-counter";
    counter.textContent = "0";
    title.appendChild(counter);
    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    productionEl.appendChild(col);
  }

  // Use the same operator card renderer (profile 1 — no drag/delete, but with Affecter à)
  loadAssignments().then(() => refreshProductionViewKanban()).catch(err => console.error("Erreur production kanban:", err));
}

async function refreshProductionViewKanban() {
  const productionEl = document.getElementById("production");
  if (!productionEl || productionEl.classList.contains("hidden")) return;
  const cols = productionEl.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnOperator(col.dataset.folder, "", "date_desc", col, true);
  }
}

async function buildProductionKanban(container) {
  const folderConfig = [
    { folder: "Début de production", label: "Début de production", color: "#5fa8c4" },
    { folder: "Corrections", label: "Corrections", color: "#e0e0e0" },
    { folder: "Corrections et fond perdu", label: "Corrections et fond perdu", color: "#e0e0e0" },
    { folder: "Rapport", label: "Rapport", color: "#cccccc" },
    { folder: "Prêt pour impression", label: "Prêt pour impression", color: "#b8b8b8" },
    { folder: "BAT", label: "BAT", color: "#a3a3a3" },
    { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f" },
    { folder: "Fiery", label: "Fiery", color: "#8f8f8f" },
    { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a" },
    { folder: "Façonnage", label: "Façonnage", color: "#666666" },
    { folder: "Fin de production", label: "Fin de production", color: "#22c55e" }
  ];

  container.innerHTML = "";

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;
    title.style.color = isLight(cfg.color) ? '#1D1D1F' : '#FFFFFF';
    title.textContent = cfg.label;
    const counterRO = document.createElement("span");
    counterRO.className = "kanban-col-counter";
    counterRO.textContent = "0";
    title.appendChild(counterRO);
    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    container.appendChild(col);
  }

  await refreshProductionKanban(container);
}

async function refreshProductionKanban(container) {
  const cols = container.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnReadOnly(col.dataset.folder, col);
  }
}

async function refreshKanbanColumnReadOnly(folderName, col) {
  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(folderName)}`)
      .then(r => r.json())
      .catch(() => []);

    const drop = col.querySelector(".kanban-col-operator__drop");
    drop.innerHTML = "";

    jobs.sort((a, b) => new Date(b.modified) - new Date(a.modified));

    for (const job of jobs) {
      const card = document.createElement("div");
      card.className = "kanban-card-operator";
      card.dataset.fullPath = normalizePath(job.fullPath || "");

      const thumb = document.createElement("div");
      thumb.className = "thumb";
      thumb.textContent = "PDF";
      card.appendChild(thumb);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        renderPdfThumbnail(normalizePath(job.fullPath || ""), thumb).catch(() => {});
      }

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      card.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      card.appendChild(sub);

      const full = normalizePath(job.fullPath || "");
      const jobFileName = fnKey(full) || (job.name || "").toLowerCase();

      // Assignment badge (read-only view — keyed by fileName)
      const assignment = assignmentsByPath[jobFileName];
      if (assignment) {
        const badge = document.createElement("div");
        badge.className = "assignment-badge";
        badge.textContent = `${assignment.operatorName}`;
        card.appendChild(badge);
      }

      const iso = deliveriesByPath[jobFileName];
      if (iso) {
        const status = document.createElement("div");
        status.className = "kanban-card-operator-status";
        const daysLeft = daysDiffFromToday(iso);
        if (daysLeft <= 1) status.classList.add("urgent");
        else if (daysLeft <= 3) status.classList.add("warning");
        status.textContent = new Date(iso).toLocaleDateString("fr-FR");
        card.appendChild(status);
      }

      // Read-only actions: only "Ouvrir" and "Fiche"
      const actions = document.createElement("div");
      actions.className = "kanban-card-operator-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");
      actions.appendChild(btnOpen);

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => openFabrication(full);
      actions.appendChild(btnFiche);

      card.appendChild(actions);
      drop.appendChild(card);
    }

    // Update column counter
    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = jobs.length;
  } catch (err) {
    console.error("Erreur refresh kanban read-only:", err);
  }
}

document.getElementById("btnViewKanban").onclick = () => {
  if (currentUser.profile === 1) {
    alert("Vous n'avez accès qu'à la Soumission");
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
document.getElementById("btnViewProduction").onclick = showProduction;
document.getElementById("btnViewRecycle").onclick = showRecycle;
document.getElementById("btnViewDashboard").onclick = showDashboard;
document.getElementById("btnViewDossiers").onclick = showDossiers;
const btnViewGlobalProd = document.getElementById("btnViewGlobalProd");
if (btnViewGlobalProd) btnViewGlobalProd.onclick = showGlobalProduction;

// ======================================================
// SOUMISSION (Profil 1)
// ======================================================

let submissionJobs = [];
let submissionCalendar = null;

async function initSubmissionView() {
  const submissionEl = document.getElementById("submission");

  if (submissionEl.innerHTML) return;

  submissionEl.innerHTML = `
    <div class="submission-container">

      <!-- LAYOUT : DRAG & DROP + FICHIERS (EN HAUT) -->
      <div class="submission-split">
        
        <!-- GAUCHE : DRAG & DROP -->
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

        <!-- DROITE : FICHIERS SOUMIS -->
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

      <!-- CALENDRIER EN BAS (100% width) -->
      <div class="submission-section">
        <h3>Planning de livraison</h3>
        <div id="submissionCalendar" class="submission-calendar"></div>
      </div>

    </div>
  `;

  const uploadZone = document.getElementById("uploadZone");
  const uploadInput = document.getElementById("uploadInput");

  uploadZone.onclick = () => uploadInput.click();
  uploadZone.addEventListener("dragover", (e) => {
    e.preventDefault();
    uploadZone.classList.add("drag-over");
  });
  uploadZone.addEventListener("dragleave", () => {
    uploadZone.classList.remove("drag-over");
  });
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

  initSubmissionCalendar();
  await refreshSubmissionView();
  setupSubmissionButtons();
}

async function initSubmissionCalendar() {
  const calendarEl = document.getElementById("submissionCalendar");
  if (!calendarEl || submissionCalendar) return;

  let schedStart = "07:00", schedEnd = "21:00";
  try {
    const sr = await fetch("/api/config/schedule", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
    if (sr.ok && sr.config) {
      if (sr.config.workStart) schedStart = sr.config.workStart;
      if (sr.config.workEnd) {
        // add 1-hour buffer so last hour slot is fully visible
        const [h, m] = sr.config.workEnd.split(":").map(Number);
        const endH = Math.min(h + 1, 24);
        schedEnd = `${String(endH).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      }
    }
  } catch(e) { /* use defaults */ }

  submissionCalendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "timeGridWeek",
    locale: "fr",
    timeZone: "local",
    height: 360,
    scrollTime: schedStart,
    slotLabelInterval: "01:00",
    slotMinTime: schedStart,
    slotMaxTime: schedEnd,
    headerToolbar: { left: "prev,next today", center: "title", right: "dayGridMonth,timeGridWeek" },
    editable: true,
    eventDurationEditable: false,
    events: async (info, success) => {
      try {
        const list = await fetch("/api/delivery").then(r => r.json());
        const ev = list.map(x => {
          const full = normalizePath(x.fullPath);
          const { bg, bc, tc } = colorForEvent(full, x.date);
          const time = x.time || "09:00";
          return {
            title: x.fileName,
            start: `${x.date}T${time}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time }
          };
        });
        try {
          const schedResp = await fetch("/api/config/schedule", {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json());
          if (schedResp.ok && schedResp.config && schedResp.config.holidays) {
            schedResp.config.holidays.forEach(h => {
              ev.push({
                title: "Férié",
                start: h,
                allDay: true,
                display: "background",
                backgroundColor: "#fee2e2",
                borderColor: "#fecaca"
              });
            });
          }
        } catch(e) { console.error("Impossible de charger les jours fériés:", e); }
        success(ev);
      } catch (err) {
        console.error("Erreur events:", err);
        success([]);
      }
    },
    eventDidMount: (info) => {
      const { bg, bc, tc } = info.event.extendedProps || {};
      if (bg) info.el.style.setProperty("--fc-event-bg-color", bg);
      if (bc) info.el.style.setProperty("--fc-event-border-color", bc);
      if (tc) info.el.style.setProperty("--fc-event-text-color", tc);
    },
    eventDrop: async (info) => {
      try {
        const fullPath = normalizePath(info.event.extendedProps.fullPath);
        const fk = fnKey(fullPath);
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fk] = newDate;
        deliveriesByPath[fk + "_time"] = newTime;

        const { bg, bc, tc } = colorForEvent(fullPath, newDate);
        info.event.setProp("backgroundColor", bg);
        info.event.setProp("borderColor", bc);
        info.event.setProp("textColor", tc);

        await refreshSubmissionView();
        showNotification(`✅ Planning mis à jour`, "success");
      } catch (err) {
        showNotification(`❌ ${err.message}`, "error");
        info.revert();
      }
    },
    eventClick: (info) => {
      const full = normalizePath(info.event.extendedProps.fullPath);
      if (full) openFabrication(full);
    }
  });

  submissionCalendar.render();
  setTimeout(() => submissionCalendar?.refetchEvents(), 500);
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
// PRODUCTION (Profil 1 - Lecture seule)
// ======================================================

async function initProductionView() {
  const productionEl = document.getElementById("production");
  
  productionEl.innerHTML = '';
  productionEl.classList.remove("hidden");
  
  await buildKanban();
  ensureCalendar();
  
  kanbanDiv.style.pointerEvents = "none";
  kanbanDiv.style.opacity = "0.9";
  calendarEl.style.pointerEvents = "none";
  calendarEl.style.opacity = "0.9";
  
  kanbanDiv.classList.remove("hidden");
  calendarEl.classList.remove("hidden");
  
  productionEl.appendChild(kanbanDiv);
  productionEl.appendChild(calendarEl);
  
  calendar?.refetchEvents();
}

// ======================================================
// REST DU CODE (Kanban, Calendar, Fabrication, etc.)
// ======================================================

// ======================================================
// DOSSIERS DE PRODUCTION (tous profils)
// ======================================================

function showDossiers() {
  hideAllViews();
  document.getElementById("dossiers").classList.remove("hidden");
  document.getElementById("btnViewDossiers").classList.add("active");
  initDossiersView();
}

async function initDossiersView() {
  const el = document.getElementById("dossiers");
  el.innerHTML = `
    <div class="settings-container">
      <h2>Dossiers de production</h2>
      <div style="display: flex; gap: 10px; margin-bottom: 16px;">
        <button id="dossiers-refresh" class="btn btn-primary">Rafraîchir</button>
      </div>
      <div id="dossiers-list"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  document.getElementById("dossiers-refresh").onclick = loadDossiersList;
  await loadDossiersList();
}

async function loadDossiersList() {
  const listEl = document.getElementById("dossiers-list");
  if (!listEl) return;
  try {
    const folders = await fetch("/api/production-folders", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);

    if (!Array.isArray(folders) || folders.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun dossier de production</p>';
      return;
    }

    listEl.innerHTML = "";
    const grid = document.createElement("div");
    grid.style.cssText = "display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px;";

    folders.forEach(folder => {
      const folderName = folder.fileName || '';
      const card = document.createElement("div");
      card.className = "dossier-card";
      card.style.cssText = "background: white; border: 1px solid #e5e7eb; border-radius: 16px; padding: 20px; box-shadow: 0 2px 12px rgba(0,0,0,0.08); cursor: pointer; transition: all 0.2s;";
      card.onmouseenter = () => { card.style.boxShadow = "0 4px 20px rgba(0,0,0,0.15)"; card.style.transform = "translateY(-2px)"; };
      card.onmouseleave = () => { card.style.boxShadow = "0 2px 12px rgba(0,0,0,0.08)"; card.style.transform = ""; };
      card.innerHTML = `
        <div style="display:flex;align-items:flex-start;gap:12px;margin-bottom:12px;min-width:0;">
          ${folder.numeroDossier ? `<div style="font-size:28px;font-weight:800;color:#111827;min-width:56px;font-family:monospace;line-height:1;">${folder.numeroDossier}</div>` : ''}
          <div style="min-width:0;flex:1;">
            <div class="dossier-card-name" title="${folderName}" style="font-weight:600;font-size:14px;color:#374151;word-break:break-word;">${folderName}</div>
            <div style="font-size:12px;color:#6b7280;margin-top:2px;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : ''}</div>
          </div>
        </div>
        <div style="display:flex;justify-content:space-between;align-items:center;">
          <span style="background:#dbeafe;color:#1e40af;padding:4px 10px;border-radius:20px;font-size:12px;font-weight:500;">${folder.currentStage || 'Début de production'}</span>
          <span style="color:#6b7280;font-size:12px;">${typeof folder.files === 'number' ? folder.files : 0} fichier(s)</span>
        </div>
      `;
      card.onclick = (e) => { if (e.target.closest(".btn-danger")) return; openDossierDetail(folder._id || folder.id); };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-danger btn-sm";
      btnDelete.textContent = "Supprimer";
      btnDelete.style.cssText = "margin-top:12px;width:100%;";
      btnDelete.onclick = async (e) => {
        e.stopPropagation();
        if (!confirm(`Supprimer le dossier "${folderName}" et tous ses fichiers ?`)) return;
        const r = await fetch(`/api/production-folder?path=${encodeURIComponent(folder.path || folder.folderPath || "")}`, { method: "DELETE" }).then(r=>r.json()).catch(()=>({ok:false,error:"Erreur réseau"}));
        if (r.ok) { showNotification("Dossier supprimé", "success"); loadDossiersList(); }
        else showNotification("Erreur: " + (r.error || ""), "error");
      };
      card.appendChild(btnDelete);

      grid.appendChild(card);
    });

    listEl.appendChild(grid);
  } catch (err) {
    listEl.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

async function openDossierDetail(dossierId) {
  try {
    const folder = await fetch(`/api/production-folders/${dossierId}`, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (!folder) {
      showNotification("❌ Dossier introuvable", "error");
      return;
    }

    // Load the fabrication sheet using fileName (resilient to path changes by Acrobat Pro)
    const fabFilePath = folder.originalFilePath || folder.currentFilePath || "";
    const fabFileName = folder.fileName || "";
    let fab = {};
    // Prefer fileName-based lookup (works even if Acrobat moved the file)
    if (fabFileName) {
      try {
        const fabResp = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fabFileName), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    // Fallback to path-based lookup
    if (!fab.client && !fab.numeroDossier && fabFilePath) {
      try {
        const fabResp = await fetch("/api/fabrication?fullPath=" + encodeURIComponent(fabFilePath), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    // Fallback to embedded fabricationSheet if no shared sheet found
    if (!fab.client && !fab.numeroDossier) {
      const embedded = folder.fabricationSheet || {};
      if (Object.keys(embedded).length > 0) fab = embedded;
    }

    const overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:flex-start;justify-content:center;padding:40px 20px;overflow-y:auto;";

    const modal = document.createElement("div");
    modal.style.cssText = "background:white;border-radius:16px;padding:32px;width:100%;max-width:800px;box-shadow:0 20px 60px rgba(0,0,0,0.3);";

    const numeroDossier = fab.numeroDossier || folder.numeroDossier || String(folder.number||0).padStart(3,'0');
    modal.innerHTML = `
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;">
        <h2 style="margin:0;font-size:22px;color:#111827;">Dossier ${numeroDossier} — ${folder.fileName||''}</h2>
        <button id="dossier-close" style="background:none;border:none;font-size:24px;cursor:pointer;color:#6b7280;">✕</button>
      </div>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px;">
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">ÉTAPE ACTUELLE</label>
          <span style="background:#dbeafe;color:#1e40af;padding:6px 12px;border-radius:20px;font-size:13px;font-weight:500;">${folder.currentStage||'Début de production'}</span>
        </div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">DATE DE CRÉATION</label>
          <span style="font-size:14px;color:#111827;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : '—'}</span>
        </div>
      </div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fiche de fabrication</h3>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:24px;">
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">DÉLAI</label><input id="df-delai" type="date" value="${fab.delai ? (() => { try { return new Date(fab.delai).toISOString().split('T')[0]; } catch(e) { return ''; } })() : ''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">N° DOSSIER</label><input id="df-numero-dossier" type="text" value="${fab.numeroDossier||folder.numeroDossier||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">CLIENT</label><input id="df-client" type="text" value="${fab.client||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">QUANTITÉ</label><input id="df-quantite" type="number" value="${fab.quantite||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">FORMAT</label><input id="df-format" type="text" value="${fab.format||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">MOTEUR D'IMPRESSION</label><input id="df-moteur" type="text" value="${fab.moteurImpression||fab.machine||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">TYPE DE TRAVAIL</label><input id="df-type-travail" type="text" value="${fab.typeTravail||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">RECTO/VERSO</label><input id="df-recto-verso" type="text" value="${fab.rectoVerso||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">FAÇONNAGE</label><input id="df-faconnage" type="text" value="${fab.faconnage||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">PAPIER / MÉDIA 1</label><input id="df-media1" type="text" value="${fab.media1||fab.papier||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div style="grid-column:1/-1;"><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">NOTES</label><textarea id="df-notes" rows="2" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;">${fab.notes||''}</textarea></div>
      </div>
      <button id="df-save" class="btn btn-primary" style="margin-bottom:24px;">Enregistrer la fiche</button>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fichiers par étape</h3>
      <div id="df-files" style="margin-bottom:24px;"></div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Ajouter un fichier</h3>
      <div style="border:2px dashed #e5e7eb;border-radius:12px;padding:20px;text-align:center;cursor:pointer;" id="df-upload-zone">
        <p style="color:#6b7280;margin:0;">Cliquez ou déposez un fichier (PDF, Excel, Word, PSD, InDesign...)</p>
        <input type="file" id="df-upload-input" style="display:none;" multiple />
      </div>
    `;

    // Render files list
    const filesEl = modal.querySelector("#df-files");
    const files = folder.files || [];
    if (files.length === 0) {
      filesEl.innerHTML = '<p style="color:#9ca3af;font-size:13px;">Aucun fichier dans ce dossier</p>';
    } else {
      files.forEach(f => {
        const row = document.createElement("div");
        row.style.cssText = "display:flex;align-items:center;gap:12px;padding:10px 14px;background:#f9fafb;border-radius:8px;margin-bottom:8px;";
        row.innerHTML = `
          <span style="font-size:13px;font-weight:700;color:#BC0024;font-family:monospace;">PDF</span>
          <div style="flex:1;">
            <div style="font-size:13px;font-weight:600;color:#111827;">${f.fileName||''}</div>
            <div style="font-size:11px;color:#6b7280;">${f.stage||''} · ${f.addedAt ? new Date(f.addedAt).toLocaleDateString("fr-FR") : ''}</div>
          </div>
          <a href="/api/production-folders/${dossierId}/files/${encodeURIComponent(f.fileName||'')}" target="_blank" class="btn btn-sm">Télécharger</a>
        `;
        filesEl.appendChild(row);
      });
    }

    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    modal.querySelector("#dossier-close").onclick = () => overlay.remove();
    overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };

    modal.querySelector("#df-save").onclick = async () => {
      // Always save via the shared /api/fabrication endpoint
      // Use fileName as key so it works even if the file was moved by Acrobat Pro
      const savePayload = {
        fullPath: fabFilePath || fabFileName || "",
        fileName: fabFileName,
        delai: modal.querySelector("#df-delai").value || null,
        numeroDossier: modal.querySelector("#df-numero-dossier").value || null,
        client: modal.querySelector("#df-client").value,
        quantite: parseInt(modal.querySelector("#df-quantite").value, 10) || null,
        format: modal.querySelector("#df-format").value,
        moteurImpression: modal.querySelector("#df-moteur").value,
        machine: modal.querySelector("#df-moteur").value,
        typeTravail: modal.querySelector("#df-type-travail").value,
        rectoVerso: modal.querySelector("#df-recto-verso").value,
        faconnage: modal.querySelector("#df-faconnage").value,
        media1: modal.querySelector("#df-media1").value,
        notes: modal.querySelector("#df-notes").value
      };
      const r = await fetch("/api/fabrication", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify(savePayload)
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("✅ Fiche enregistrée", "success");
      } else {
        showNotification("❌ Erreur : " + (r.error || ""), "error");
      }
    };

    const uploadZone = modal.querySelector("#df-upload-zone");
    const uploadInput = modal.querySelector("#df-upload-input");
    uploadZone.onclick = () => uploadInput.click();
    uploadZone.addEventListener("dragover", e => { e.preventDefault(); uploadZone.style.background = "#f3f4f6"; });
    uploadZone.addEventListener("dragleave", () => { uploadZone.style.background = ""; });
    uploadZone.addEventListener("drop", async (e) => {
      e.preventDefault();
      uploadZone.style.background = "";
      const files = Array.from(e.dataTransfer.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
    });
    uploadInput.addEventListener("change", async (e) => {
      const files = Array.from(e.target.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
      uploadInput.value = "";
    });

  } catch (err) {
    showNotification("❌ " + err.message, "error");
  }
}

async function uploadDossierFiles(dossierId, files, overlay) {
  for (const file of files) {
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch(`/api/production-folders/${dossierId}/upload`, {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` },
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${file.name} ajouté`, "success");
    } else {
      showNotification(`❌ Erreur upload ${file.name}`, "error");
    }
  }
  overlay.remove();
  openDossierDetail(dossierId);
}

// ======================================================
// DASHBOARD STATISTIQUES (tous profils)
// ======================================================

function showDashboard() {
  hideAllViews();
  document.getElementById("dashboard").classList.remove("hidden");
  document.getElementById("btnViewDashboard").classList.add("active");
  initDashboardView();
}

// ======================================================
// VUE PRODUCTION GLOBALE (profils 2 et 3)
// ======================================================

function showGlobalProduction() {
  hideAllViews();
  const el = document.getElementById("global-production");
  if (el) el.classList.remove("hidden");
  const btn = document.getElementById("btnViewGlobalProd");
  if (btn) btn.classList.add("active");
  initGlobalProductionView();
}

const STAGE_PROGRESS = {
  "Début de production": 0,
  "1.Reception": 0,
  "Corrections": 25,
  "Corrections et fond perdu": 25,
  "Prêt pour impression": 50,
  "6.Archivage": 50,
  "BAT": 65,
  "4.BAT": 65,
  "PrismaPrepare": 75,
  "Fiery": 75,
  "Impression en cours": 75,
  "Façonnage": 90,
  "Fin de production": 100
};

function getStageProgress(stage) {
  if (!stage) return 0;
  const key = Object.keys(STAGE_PROGRESS).find(k => stage.toLowerCase().includes(k.toLowerCase()));
  return key !== undefined ? STAGE_PROGRESS[key] : 0;
}

async function initGlobalProductionView() {
  const el = document.getElementById("global-production");
  if (!el) return;
  el.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement...</div>';

  try {
    const folders = await fetch("/api/production-folders/global-progress", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);

    if (!Array.isArray(folders) || folders.length === 0) {
      el.innerHTML = '<div style="padding:20px;color:#6b7280;">Aucun dossier de production.</div>';
      return;
    }

    const grid = document.createElement("div");
    grid.className = "global-prod-grid";

    for (const folder of folders) {
      const stage = folder.currentStage || "Inconnu";
      const progress = folder.progress !== undefined ? folder.progress : getStageProgress(stage);
      const card = document.createElement("div");
      card.className = "global-prod-card";
      card.innerHTML = `
        <div class="global-prod-name">${folder.numeroDossier || folder.fileName || folder.number || 'Dossier'}</div>
        <div class="global-prod-stage">${stage}</div>
        <div class="global-prod-progress">
          <div class="progress-bar" style="width:${progress}%"></div>
        </div>
        <span class="global-prod-percent">${progress}%</span>
      `;
      grid.appendChild(card);
    }

    el.innerHTML = "";
    el.appendChild(grid);
  } catch(err) {
    el.innerHTML = `<div style="padding:20px;color:#dc2626;">Erreur : ${err.message}</div>`;
  }
}

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
// CORBEILLE (tous profils)
// ======================================================

function showRecycle() {
  hideAllViews();
  document.getElementById("recycle").classList.remove("hidden");
  document.getElementById("btnViewRecycle").classList.add("active");
  initRecycleView();
}

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

    // Handle error response
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
// PARAMÉTRAGE (profil 3 — Admin)
// ======================================================

function showSettings() {
  hideAllViews();
  document.getElementById("settings-view").classList.remove("hidden");
  initSettingsView();
}

async function initSettingsView() {
  const settingsEl = document.getElementById("settings-view");
  settingsEl.innerHTML = `
    <div class="settings-container">
      <h2>Paramétrage</h2>
      <div class="settings-tabs">
        <button class="settings-tab active" data-tab="accounts">Comptes &amp; Rôles</button>
        <button class="settings-tab" data-tab="schedule">Plages horaires</button>
        <button class="settings-tab" data-tab="paths">Chemins d'accès</button>
        <button class="settings-tab" data-tab="integrations">Prepare / Fiery</button>
        <button class="settings-tab" data-tab="print-engines">Moteurs d'impression</button>
        <button class="settings-tab" data-tab="work-types">Types de travail</button>
        <button class="settings-tab" data-tab="fabrication-imports">Imports fiche</button>
        <button class="settings-tab" data-tab="bat-command">Commande BAT</button>
        <button class="settings-tab" data-tab="action-buttons">Boutons d'action</button>
        <button class="settings-tab" data-tab="logs">Logs</button>
      </div>
      <div class="settings-panel" id="settings-panel-accounts"></div>
      <div class="settings-panel hidden" id="settings-panel-schedule"></div>
      <div class="settings-panel hidden" id="settings-panel-paths"></div>
      <div class="settings-panel hidden" id="settings-panel-integrations"></div>
      <div class="settings-panel hidden" id="settings-panel-print-engines"></div>
      <div class="settings-panel hidden" id="settings-panel-work-types"></div>
      <div class="settings-panel hidden" id="settings-panel-fabrication-imports"></div>
      <div class="settings-panel hidden" id="settings-panel-bat-command"></div>
      <div class="settings-panel hidden" id="settings-panel-action-buttons"></div>
      <div class="settings-panel hidden" id="settings-panel-logs"></div>
    </div>
  `;

  settingsEl.querySelectorAll(".settings-tab").forEach(tab => {
    tab.onclick = () => {
      settingsEl.querySelectorAll(".settings-tab").forEach(t => t.classList.remove("active"));
      settingsEl.querySelectorAll(".settings-panel").forEach(p => p.classList.add("hidden"));
      tab.classList.add("active");
      const panel = settingsEl.querySelector(`#settings-panel-${tab.dataset.tab}`);
      if (panel) {
        panel.classList.remove("hidden");
        loadSettingsPanel(tab.dataset.tab, panel);
      }
    };
  });

  await loadSettingsPanel("accounts", settingsEl.querySelector("#settings-panel-accounts"));
}

async function loadSettingsPanel(tabName, panelEl) {
  if (!panelEl) return;
  if (panelEl._loaded) return;
  switch (tabName) {
    case "accounts": await renderSettingsAccounts(panelEl); break;
    case "schedule": await renderSettingsSchedule(panelEl); break;
    case "paths": await renderSettingsPaths(panelEl); break;
    case "integrations": await renderSettingsIntegrations(panelEl); break;
    case "print-engines": await renderSettingsPrintEngines(panelEl); break;
    case "work-types": await renderSettingsWorkTypes(panelEl); break;
    case "fabrication-imports": await renderSettingsFabricationImports(panelEl); break;
    case "bat-command": await renderSettingsBatCommand(panelEl); break;
    case "action-buttons": await renderSettingsActionButtons(panelEl); break;
    case "logs": await renderSettingsLogs(panelEl); break;
  }
  panelEl._loaded = true;
}

async function renderSettingsAccounts(panel) {
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
        </select>
        <button id="sa-create" class="btn btn-primary">Créer</button>
      </div>
    </div>
    <h4>Utilisateurs existants</h4>
    <div id="sa-users-list"></div>
  `;

  document.getElementById("sa-create").onclick = async () => {
    const login = document.getElementById("sa-login").value.trim();
    const password = document.getElementById("sa-password").value.trim();
    const name = document.getElementById("sa-name").value.trim();
    const profile = parseInt(document.getElementById("sa-profile").value);
    if (!login || !password || !name) { alert("Remplissez tous les champs"); return; }
    const r = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ login, password, name, profile })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Utilisateur créé", "success");
      document.getElementById("sa-login").value = "";
      document.getElementById("sa-password").value = "";
      document.getElementById("sa-name").value = "";
      panel._loaded = false;
      await renderSettingsAccounts(panel);
    } else {
      alert("Erreur : " + r.error);
    }
  };

  await refreshSettingsUsersList();
}

async function refreshSettingsUsersList() {
  const listEl = document.getElementById("sa-users-list");
  if (!listEl) return;
  const resp = await fetch("/api/auth/users", {
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json());
  listEl.innerHTML = "";
  if (resp.ok && resp.users) {
    const profileLabel = { 1: "Soumission", 2: "Opérateur", 3: "Admin" };
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

async function renderSettingsSchedule(panel) {
  panel.innerHTML = `<h3>Plages horaires et jours fériés</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { workStart: "08:00", workEnd: "18:00", holidays: [] };
  try {
    const resp = await fetch("/api/config/schedule", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  const holidays = Array.isArray(cfg.holidays) ? cfg.holidays : [];

  panel.innerHTML = `
    <h3>Plages horaires et jours fériés</h3>
    <div class="settings-form-group">
      <label>Début journée</label>
      <input type="time" id="sch-start" value="${cfg.workStart || '08:00'}" class="settings-input" />
    </div>
    <div class="settings-form-group">
      <label>Fin journée</label>
      <input type="time" id="sch-end" value="${cfg.workEnd || '18:00'}" class="settings-input" />
    </div>
    <button id="sch-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les plages</button>
    <hr style="margin: 20px 0;" />
    <h4>Jours fériés</h4>
    <div style="display: flex; gap: 8px; margin-bottom: 10px; flex-wrap: wrap;">
      <input type="date" id="sch-holiday-date" class="settings-input" />
      <button id="sch-add-holiday" class="btn btn-primary">Ajouter</button>
      <button id="sch-add-french-holidays" class="btn">Ajouter jours fériés français</button>
    </div>
    <div id="sch-holidays-list">
      ${holidays.length === 0 ? '<p style="color:#9ca3af;">Aucun jour férié configuré</p>' : holidays.map(h => `
        <div style="display: flex; align-items: center; gap: 10px; padding: 6px 10px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 4px;">
          <span style="flex:1;">${new Date(h + "T00:00:00").toLocaleDateString("fr-FR", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })}</span>
          <button class="btn btn-sm" data-date="${h}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
        </div>
      `).join("")}
    </div>
  `;

  document.getElementById("sch-save").onclick = async () => {
    const workStart = document.getElementById("sch-start").value;
    const workEnd = document.getElementById("sch-end").value;
    const r = await fetch("/api/config/schedule", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ workStart, workEnd })
    }).then(r => r.json());
    if (r.ok) {
      // Compute buffered end (add 1 hour so last slot is fully visible)
      const [h, m] = workEnd.split(":").map(Number);
      const bufferedEnd = `${String(Math.min(h + 1, 24)).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      // Update FullCalendar displays immediately
      if (calendar) {
        calendar.setOption("slotMinTime", workStart);
        calendar.setOption("slotMaxTime", bufferedEnd);
      }
      if (submissionCalendar) {
        submissionCalendar.setOption("slotMinTime", workStart);
        submissionCalendar.setOption("slotMaxTime", bufferedEnd);
      }
      showNotification("✅ Plages horaires enregistrées", "success");
    }
    else alert("Erreur : " + r.error);
  };

  document.getElementById("sch-add-holiday").onclick = async () => {
    const dateVal = document.getElementById("sch-holiday-date").value;
    if (!dateVal) { alert("Sélectionnez une date"); return; }
    const r = await fetch("/api/config/schedule/holidays", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ date: dateVal })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Jour férié ajouté", "success");
      panel._loaded = false;
      await renderSettingsSchedule(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("sch-add-french-holidays").onclick = async () => {
    const year = new Date().getFullYear();
    const frenchHolidays = getFrenchPublicHolidays(year);
    let added = 0;
    for (const date of frenchHolidays) {
      const r = await fetch("/api/config/schedule/holidays", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ date })
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) added++;
    }
    showNotification(`✅ ${added} jours fériés français ajoutés pour ${year}`, "success");
    panel._loaded = false;
    await renderSettingsSchedule(panel);
  };

  document.querySelectorAll("#sch-holidays-list button[data-date]").forEach(btn => {
    btn.onclick = async () => {
      const dateToRemove = btn.dataset.date;
      const r = await fetch(`/api/config/schedule/holidays?date=${encodeURIComponent(dateToRemove)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("✅ Jour férié supprimé", "success");
        panel._loaded = false;
        await renderSettingsSchedule(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}

// Compute French public holidays for a given year (fixed + Easter-based)
function getFrenchPublicHolidays(year) {
  // Meeus/Jones/Butcher algorithm for Easter Sunday
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  const easter = new Date(year, month - 1, day);

  function addDays(d, n) {
    const r = new Date(d);
    r.setDate(r.getDate() + n);
    return r.toISOString().split("T")[0];
  }
  function fmt(y, m, d) {
    return `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
  }

  return [
    fmt(year, 1, 1),       // Jour de l'An
    addDays(easter, 1),    // Lundi de Pâques
    fmt(year, 5, 1),       // Fête du Travail
    fmt(year, 5, 8),       // Victoire 1945
    addDays(easter, 39),   // Ascension
    addDays(easter, 50),   // Lundi de Pentecôte
    fmt(year, 7, 14),      // Fête nationale
    fmt(year, 8, 15),      // Assomption
    fmt(year, 11, 1),      // Toussaint
    fmt(year, 11, 11),     // Armistice
    fmt(year, 12, 25)      // Noël
  ];
}

async function renderSettingsPaths(panel) {
  panel.innerHTML = `<h3>Chemins d'accès aux dossiers</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { hotfoldersRoot: "C:\\Flux", recycleBinPath: "" };
  try {
    const resp = await fetch("/api/config/paths", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Chemins d'accès aux dossiers</h3>
    <div class="settings-form-group">
      <label>Racine des hotfolders (GA_HOTFOLDERS_ROOT)</label>
      <input type="text" id="paths-hotfolders" value="${cfg.hotfoldersRoot || 'C:\\\\Flux'}" class="settings-input" style="width: 100%; max-width: 500px;" />
    </div>
    <div class="settings-form-group">
      <label>Chemin corbeille</label>
      <input type="text" id="paths-recycle" value="${cfg.recycleBinPath || ''}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Corbeille" />
    </div>
    <button id="paths-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("paths-save").onclick = async () => {
    const hotfoldersRoot = document.getElementById("paths-hotfolders").value.trim();
    const recycleBinPath = document.getElementById("paths-recycle").value.trim();
    const r = await fetch("/api/config/paths", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ hotfoldersRoot, recycleBinPath })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}

async function renderSettingsIntegrations(panel) {
  panel.innerHTML = `<h3>Prepare / Fiery — Chemins d'accès</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { preparePath: "", fieryPath: "" };
  let cmdCfg = { prismaCommand: "" };
  try {
    const resp = await fetch("/api/config/integrations", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }
  try {
    const resp2 = await fetch("/api/config/commands", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp2.ok && resp2.config) cmdCfg = resp2.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Prepare / Fiery — Chemins d'accès</h3>

    <div style="border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 20px; background: #f9fafb;">
      <h4 style="margin-top: 0; margin-bottom: 12px;">Prepare</h4>
      <div class="settings-form-group">
        <label>Chemin vers Prepare</label>
        <input type="text" id="int-prepare" value="${cfg.preparePath || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Prepare\\prepare.exe" />
      </div>
    </div>

    <div style="border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 20px; background: #f9fafb;">
      <h4 style="margin-top: 0; margin-bottom: 12px;">Fiery</h4>
      <div class="settings-form-group">
        <label>Chemin vers Fiery</label>
        <input type="text" id="int-fiery" value="${cfg.fieryPath || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Fiery\\fiery.exe" />
      </div>
    </div>

    <div style="border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 20px; background: #f9fafb;">
      <h4 style="margin-top: 0; margin-bottom: 12px;">Commande PrismaPrepare (bouton "PrismaPrepare" dans le Kanban)</h4>
      <p style="font-size:12px;color:#6b7280;margin-bottom:8px;">Variables disponibles : <code>{xmlPath}</code> (fiche XML), <code>{filePath}</code> (chemin du PDF)</p>
      <div class="settings-form-group">
        <label>Commande</label>
        <input type="text" id="int-prisma-cmd" value="${(cmdCfg.prismaCommand || '').replace(/"/g,'&quot;')}" class="settings-input" style="width:100%;max-width:600px;" placeholder='Ex: "C:\\Program Files\\Canon\\PRISMACore\\PRISMAprepare.exe" /import "{xmlPath}" /file "{filePath}"' />
      </div>
    </div>

    <button id="int-save" class="btn btn-primary">Enregistrer</button>
  `;

  document.getElementById("int-save").onclick = async () => {
    const preparePath = document.getElementById("int-prepare").value.trim();
    const fieryPath = document.getElementById("int-fiery").value.trim();
    const prismaCommand = document.getElementById("int-prisma-cmd").value.trim();
    const r1 = await fetch("/api/config/integrations", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ preparePath, fieryPath })
    }).then(r => r.json());
    const r2 = await fetch("/api/config/commands", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ prismaCommand })
    }).then(r => r.json()).catch(() => ({ ok: true }));
    if (r1.ok && r2.ok) showNotification("✅ Configuration Prepare/Fiery enregistrée", "success");
    else alert("Erreur : " + (r1.error || r2.error || ""));
  };
}

async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>Gestion des imports — Fiche de fabrication</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  try {
    const resp = await fetch("/api/config/fabrication-imports", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Gestion des imports — Fiche de fabrication</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Configurez les chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.</p>
    <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${cfg.media1Path || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
    <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${cfg.media2Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${cfg.media3Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${cfg.media4Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${cfg.typeDocumentPath || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <button id="fi-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("fi-save").onclick = async () => {
    const r = await fetch("/api/config/fabrication-imports", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({
        media1Path: document.getElementById("fi-media1").value.trim(),
        media2Path: document.getElementById("fi-media2").value.trim(),
        media3Path: document.getElementById("fi-media3").value.trim(),
        media4Path: document.getElementById("fi-media4").value.trim(),
        typeDocumentPath: document.getElementById("fi-typedoc").value.trim()
      })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins d'import enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}

async function renderSettingsPrintEngines(panel) {
  panel.innerHTML = `<h3>Moteurs d'impression</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshPrintEnginesPanel(panel);
}

function extractEngineName(e) {
  return (typeof e === "object" && e !== null) ? (e.name || "") : String(e || "");
}

async function refreshPrintEnginesPanel(panel) {
  let engines = [];
  try {
    const resp = await fetch("/api/config/print-engines").then(r => r.json());
    engines = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Moteurs d'impression</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des moteurs d'impression disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="pe-new-name" placeholder="Nom du moteur" class="settings-input" style="max-width:200px;" />
      <input type="text" id="pe-new-ip" placeholder="IP / URL (optionnel)" class="settings-input" style="max-width:250px;" />
      <button id="pe-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        Importer CSV
        <input type="file" id="pe-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="pe-list">
      ${engines.length === 0
        ? '<p style="color:#9ca3af;">Aucun moteur configuré</p>'
        : engines.map(e => {
            const name = extractEngineName(e);
            const safeName = name.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `
          <div style="display: flex; align-items: center; gap: 10px; padding: 8px 12px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 6px;">
            <span style="flex: 1; font-size: 13px;">${name}</span>
            <button class="btn btn-sm pe-delete" data-name="${safeName}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
          </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("pe-add").onclick = async () => {
    const name = document.getElementById("pe-new-name").value.trim();
    const ip   = document.getElementById("pe-new-ip").value.trim();
    if (!name) { alert("Entrez un nom"); return; }
    const r = await fetch("/api/config/print-engines", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ name, ip })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("Moteur ajouté", "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("pe-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const text = await file.text();
    // Parse CSV with semicolon separator: Presse;IP — skip header row
    const lines = text.split(/[\r\n]+/).filter(Boolean);
    const enginesList = [];
    for (const line of lines) {
      const parts = line.split(";");
      const name = (parts[0] || "").trim();
      const ip   = (parts[1] || "").trim();
      // Skip header row (e.g. "Presse;IP", "Nom;Adresse", "PRESSE;IP")
      const knownHeaders = ["presse", "nom", "name", "moteur", "engine"];
      if (!name || knownHeaders.includes(name.toLowerCase())) continue;
      enginesList.push({ name, ip });
    }
    if (enginesList.length === 0) { alert("Aucun moteur trouvé dans le fichier"); return; }
    const r = await fetch("/api/config/print-engines/import", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ engines: enginesList })
    }).then(r => r.json());
    if (r.ok) {
      showNotification(`${r.count || enginesList.length} moteurs importés`, "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
    e.target.value = "";
  };

  panel.querySelectorAll(".pe-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/print-engines/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("Moteur supprimé", "success");
        panel._loaded = false;
        await refreshPrintEnginesPanel(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}

async function renderSettingsWorkTypes(panel) {
  panel.innerHTML = `<h3>Types de travail</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshWorkTypesPanel(panel);
}

async function refreshWorkTypesPanel(panel) {
  let types = [];
  try {
    const resp = await fetch("/api/config/work-types").then(r => r.json());
    types = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Types de travail</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des types de travail disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="wt-new-name" placeholder="Nouveau type" class="settings-input" style="max-width:300px;" />
      <button id="wt-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        📥 Importer CSV
        <input type="file" id="wt-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="wt-list">
      ${types.length === 0
        ? '<p style="color:#9ca3af;">Aucun type configuré</p>'
        : types.map(t => {
            const escapedType = t.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `<div style="display:flex;align-items:center;gap:10px;padding:8px 12px;background:white;border:1px solid #e5e7eb;border-radius:6px;margin-bottom:6px;">
              <span style="flex:1;font-size:13px;">${t}</span>
              <button class="btn btn-sm wt-delete" data-name="${escapedType}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("wt-add").onclick = async () => {
    const name = document.getElementById("wt-new-name").value.trim();
    if (!name) { alert("Entrez un type de travail"); return; }
    const formData = new FormData();
    const blob = new Blob([name], { type: "text/plain" });
    formData.append("file", blob, "type.csv");
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Type ajouté", "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };

  document.getElementById("wt-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${r.count || 0} type(s) importé(s)`, "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
    e.target.value = "";
  };

  panel.querySelectorAll(".wt-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/work-types/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("Type supprimé", "success");
        panel._loaded = false;
        await refreshWorkTypesPanel(panel);
      } else { alert("Erreur : " + (r.error || "")); }
    };
  });
}

async function renderSettingsBatCommand(panel) {
  let cmd = "";
  try {
    const r = await fetch("/api/config/bat-command").then(r => r.json());
    if (r.ok) cmd = r.command || "";
  } catch(e) { /* use default */ }
  panel.innerHTML = `
    <h3>Commande BAT</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Utilisez <code>{filePath}</code>, <code>{type}</code> et <code>{qty}</code> comme variables.</p>
    <div class="settings-form-group">
      <label>Commande</label>
      <input type="text" id="bat-cmd-input" value="${(cmd || '').replace(/"/g,'&quot;')}" class="settings-input" style="width:100%;max-width:600px;" />
    </div>
    <button id="bat-cmd-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("bat-cmd-save").onclick = async () => {
    const command = document.getElementById("bat-cmd-input").value;
    const r = await fetch("/api/config/bat-command", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ command })
    }).then(r => r.json());
    if (r.ok) showNotification("Commande BAT enregistrée", "success");
    else alert("Erreur");
  };
}

async function renderSettingsActionButtons(panel) {
  let buttons = {};
  try {
    const r = await fetch("/api/config/action-buttons").then(r => r.json());
    if (r.ok) buttons = r.buttons || {};
  } catch(e) { /* use defaults */ }

  const esc = s => (s || "").replace(/"/g,'&quot;');
  panel.innerHTML = `
    <h3>Boutons d'action</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Configurez les chemins d'exécutables pour chaque bouton d'action.</p>
    <div class="settings-form-group"><label>Contrôleur</label><input type="text" id="ab-controller" value="${esc(buttons.controller)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>PrismaPrepare</label><input type="text" id="ab-prisma" value="${esc(buttons.prismaPrepare)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Impression</label><input type="text" id="ab-print" value="${esc(buttons.print)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Modification</label><input type="text" id="ab-modification" value="${esc(buttons.modification)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Fiery (hotfolder)</label><input type="text" id="ab-fiery" value="${esc(buttons.fiery)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <button id="ab-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("ab-save").onclick = async () => {
    const data = {
      buttons: {
        controller: document.getElementById("ab-controller").value,
        prismaPrepare: document.getElementById("ab-prisma").value,
        print: document.getElementById("ab-print").value,
        modification: document.getElementById("ab-modification").value,
        fiery: document.getElementById("ab-fiery").value
      }
    };
    const r = await fetch("/api/config/action-buttons", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(data)
    }).then(r => r.json());
    if (r.ok) showNotification("Boutons d'action enregistrés", "success");
    else alert("Erreur");
  };
}

async function pollNotifications() {
  if (!currentUser) return;
  try {
    const notifs = await fetch(`/api/notifications?login=${encodeURIComponent(currentUser.login)}`).then(r=>r.json()).catch(()=>[]);
    const count = Array.isArray(notifs) ? notifs.filter(n => !n.read).length : 0;
    const bell = document.getElementById("notif-bell");
    const countEl = document.getElementById("notif-count");
    if (bell) bell.style.display = "flex";
    if (countEl) {
      countEl.textContent = count;
      countEl.classList.toggle("hidden", count === 0);
    }

    // Show pop-up only for NEW unread notifications (persisted in localStorage to survive refresh)
    if (Array.isArray(notifs) && notifs.length > 0) {
      const storageKey = `seenNotifs_${currentUser.login}`;
      const seenIds = new Set(JSON.parse(localStorage.getItem(storageKey) || "[]"));
      const newNotifs = notifs.filter(n => !n.read && n.id && !seenIds.has(n.id));
      if (newNotifs.length > 0) {
        showNotificationPopup();
        newNotifs.forEach(n => seenIds.add(n.id));
        localStorage.setItem(storageKey, JSON.stringify([...seenIds]));
      }
    }
    window._lastNotifs = notifs;
  } catch(e) { console.error("Notification poll error:", e); }
}

function showNotificationPopup() {
  // Remove any existing notification popup
  const existingPopup = document.getElementById("notif-popup-overlay");
  if (existingPopup) existingPopup.remove();

  const overlay = document.createElement("div");
  overlay.id = "notif-popup-overlay";
  overlay.className = "notification-popup-overlay";

  const box = document.createElement("div");
  box.className = "notification-popup";
  box.innerHTML = `
    <p>Une tâche vous a été affectée.</p>
    <button id="notif-popup-ok">OK</button>
  `;
  overlay.appendChild(box);
  document.body.appendChild(overlay);

  let timer;
  const dismiss = () => { overlay.remove(); if (timer) clearTimeout(timer); };
  document.getElementById("notif-popup-ok").onclick = dismiss;
  overlay.onclick = (e) => { if (e.target === overlay) dismiss(); };
  timer = setTimeout(dismiss, 5000);
}

function initNotificationBell() {
  const btn = document.getElementById("notif-btn");
  const dropdown = document.getElementById("notif-dropdown");
  if (!btn || !dropdown) return;

  btn.onclick = (e) => {
    e.stopPropagation();
    const isHidden = dropdown.classList.contains("hidden");
    dropdown.classList.toggle("hidden", !isHidden);
    if (isHidden) {
      const notifs = window._lastNotifs || [];
      if (notifs.length === 0) {
        dropdown.innerHTML = '<div class="notif-empty">Aucune notification</div>';
      } else {
        dropdown.innerHTML = notifs.map(n => `
          <div class="notif-item ${n.read ? '' : 'unread'}">
            <div>${n.message || ''}</div>
            <div style="font-size:11px;color:#86868b;margin-top:2px;">${n.timestamp ? new Date(n.timestamp).toLocaleString("fr-FR") : ''}</div>
          </div>
        `).join("");
      }
      // Mark all as read
      fetch("/api/notifications/read", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ login: currentUser.login })
      }).then(() => pollNotifications()).catch(() => {});
    }
  };

  if (!document._notifOutsideHandlerAdded) {
    document._notifOutsideHandlerAdded = true;
    document.addEventListener("click", () => dropdown.classList.add("hidden"));
  }
}

async function renderSettingsLogs(panel) {
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

async function loadSettingsLogs() {
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

async function initApp() {
  setupProfileUI();
  initNotificationBell();

  try {
    await loadDeliveries();
    await loadAssignments();
    updateGlobalAlert();
    await buildKanban();
    await ensureCalendar();

    if (currentUser.profile === 1) {
      showSubmission();
    } else {
      showKanban();
    }
  } catch (err) {
    console.error("Erreur init:", err);
  }

  // Start notification polling
  pollNotifications();
  setInterval(pollNotifications, 30000);
}

document.addEventListener("DOMContentLoaded", initLogin);

// ======================================================
// UTILITAIRES & REST
// ======================================================

const FIN_PROD_FOLDER = FOLDER_FIN_PRODUCTION;
const btnViewKanban = document.getElementById("btnViewKanban");
const btnViewCalendar = document.getElementById("btnViewCalendar");
const calendarEl = document.getElementById("calendar");
const kanbanDiv = document.getElementById("kanban");
const globalAlert = document.getElementById("global-alert");

const searchInput = document.getElementById("searchInput");
const sortBy = document.getElementById("sortBy");

let calendar = null;
let deliveriesByPath = {};
let assignmentsByPath = {};

async function loadAssignments() {
  try {
    const list = await fetch("/api/assignments").then(r => r.json()).catch(() => []);
    assignmentsByPath = {};
    list.forEach(a => {
      const fname = fnKey(a.fileName || a.fullPath || "");
      if (fname) assignmentsByPath[fname] = a;
    });
  } catch(err) {
    console.error("Erreur loadAssignments:", err);
  }
}

setInterval(async () => {
  if (kanbanDiv.classList.contains("hidden")) return;
  await loadDeliveries();
  await loadAssignments();
  updateGlobalAlert();
  await refreshKanban();
}, 30000);

function normalizePath(p) {
  if (!p) return "";
  return decodeURIComponent(p).replace(/\u00A0/g, " ").replace(/\//g, "\\").replace(/%5C/gi, "\\");
}

// Returns a normalized fileName-only key (lowercase, no path).
// This is the universal key for deliveriesByPath and assignmentsByPath — resilient to
// path changes when Acrobat Pro moves files between hotfolder subfolders.
function fnKey(pathOrName) {
  if (!pathOrName) return "";
  return (pathOrName.split(/[/\\]/).pop() || "").toLowerCase();
}

function sanitizeFolderName(s) {
  return (s || "").replace(/\u00A0/g, " ").replace(/\s+/g, " ").trim().toLowerCase();
}

function fmtBytes(b) {
  if (b == null) return "";
  const units = ["o", "Ko", "Mo", "Go"];
  let i = 0, x = b;
  while (x >= 1024 && i < units.length - 1) { x /= 1024; i++; }
  return `${x.toFixed(i ? 1 : 0)} ${units[i]}`;
}

function daysDiffFromToday(iso) {
  const t = new Date();
  t.setHours(0, 0, 0, 0);
  const d = new Date(iso + "T00:00:00");
  return Math.ceil((d - t) / 86400000);
}

function getDropForFolder(folderName) {
  const target = sanitizeFolderName(folderName);
  const cols = kanbanDiv.querySelectorAll(".kanban-col");
  for (const col of cols) {
    if (sanitizeFolderName(col.dataset.folder || "") === target) {
      return col.querySelector(".kanban-col__drop");
    }
  }
  return null;
}

function updateGlobalAlert() {
  const dates = Object.values(deliveriesByPath).filter(v => typeof v === 'string' && v.match(/\d{4}-\d{2}-\d{2}/));
  if (!dates.length) {
    globalAlert.style.display = "none";
    return;
  }

  let min = +Infinity;
  for (const iso of dates) min = Math.min(min, daysDiffFromToday(iso));

  if (min <= 1) {
    globalAlert.textContent = "Urgences J-1";
    globalAlert.className = "global-alert";
  } else if (min <= 3) {
    globalAlert.textContent = "Attention : < 3 jours";
    globalAlert.className = "global-alert orange";
  } else {
    globalAlert.style.display = "none";
    return;
  }

  globalAlert.style.display = "block";
}

async function ensureCalendar() {
  if (!calendar) await initCalendar();
}

let fabCurrentPath = null;
const fabModal = document.getElementById("fab-modal");
const fabClose = document.getElementById("fab-close");
const fabSave = document.getElementById("fab-save");
const fabPdf = document.getElementById("fab-pdf");
const fabFinProd = document.getElementById("fab-finprod");
const fabPrisma = document.getElementById("fab-prisma");
const fabBat = document.getElementById("fab-bat");
const fabMoteur = document.getElementById("fab-moteur");
const fabOperateur = document.getElementById("fab-operateur");
const fabQuantite = document.getElementById("fab-quantite");
const fabType = document.getElementById("fab-type");
const fabFormat = document.getElementById("fab-format");
const fabRectoVerso = document.getElementById("fab-recto-verso");
const fabClient = document.getElementById("fab-client");
const fabNumeroDossier = document.getElementById("fab-numero-dossier");
const fabNotes = document.getElementById("fab-notes");
const fabDelai = document.getElementById("fab-delai");
const fabFaconnage = document.getElementById("fab-faconnage");
const fabMedia1 = document.getElementById("fab-media1");
const fabMedia2 = document.getElementById("fab-media2");
const fabMedia3 = document.getElementById("fab-media3");
const fabMedia4 = document.getElementById("fab-media4");
const fabHistory = document.getElementById("fab-history");
const fabRemove = document.getElementById("fab-delivery-remove");
const fabStageBanner = document.getElementById("fab-stage-banner");

fabClose.onclick = () => fabModal.classList.add("hidden");
document.addEventListener("keydown", e => { if (e.key === "Escape") fabModal.classList.add("hidden"); });

async function openFabrication(fullPath) {
  fabCurrentPath = normalizePath(fullPath);
  const fabCurrentFileName = fnKey(fabCurrentPath);

  // Fetch fabrication by fileName (resilient to path changes by Acrobat Pro)
  const r = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fabCurrentFileName));
  const j = await r.json();
  const d = j.ok === false ? {} : j;

  // Load print engines into dropdown
  try {
    const engines = await fetch("/api/config/print-engines").then(r => r.json()).catch(() => []);
    fabMoteur.innerHTML = '<option value="">— Sélectionner —</option>';
    engines.forEach(e => {
      const name = extractEngineName(e);
      const opt = document.createElement("option");
      opt.value = name;
      opt.textContent = name;
      fabMoteur.appendChild(opt);
    });
  } catch(err) { console.warn("Erreur print-engines:", err); }

  // Load work types from stored list
  try {
    const types = await fetch("/api/config/work-types").then(r => r.json()).catch(() => []);
    fabType.innerHTML = '<option value="">— Sélectionner —</option>';
    types.forEach(t => {
      const opt = document.createElement("option");
      opt.value = t;
      opt.textContent = t;
      fabType.appendChild(opt);
    });
  } catch(e) { console.warn("Erreur work-types:", e); }

  // Load media options from Paper Catalog
  try {
    const papers = await fetch("/api/config/paper-catalog").then(r => r.json()).catch(() => []);
    [fabMedia1, fabMedia2, fabMedia3, fabMedia4].forEach(sel => {
      if (!sel) return;
      sel.innerHTML = '<option value="">— Sélectionner —</option>';
      papers.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p;
        opt.textContent = p;
        sel.appendChild(opt);
      });
    });
  } catch(e) { console.warn("Paper catalog error:", e); }

  fabMoteur.value = d.moteurImpression || d.machine || "";
  fabOperateur.value = d.operateur || "";
  fabQuantite.value = d.quantite || "";
  fabType.value = d.typeTravail || "";
  fabFormat.value = d.format || "";
  fabRectoVerso.value = d.rectoVerso || "";
  fabClient.value = d.client || "";
  if (fabNumeroDossier) fabNumeroDossier.value = d.numeroDossier || "";
  fabNotes.value = d.notes || "";
  fabFaconnage.value = d.faconnage || "";
  if (fabMedia1) fabMedia1.value = d.media1 || "";
  if (fabMedia2) fabMedia2.value = d.media2 || "";
  if (fabMedia3) fabMedia3.value = d.media3 || "";
  if (fabMedia4) fabMedia4.value = d.media4 || "";

  // Pre-fill Délai from delivery if available (keyed by fileName)
  const deliveryDate = deliveriesByPath[fabCurrentFileName];
  if (d.delai) {
    fabDelai.value = new Date(d.delai).toISOString().split("T")[0];
  } else if (deliveryDate) {
    fabDelai.value = deliveryDate;
  } else {
    fabDelai.value = "";
  }

  fabHistory.innerHTML = "";
  (d.history || []).forEach(h => {
    const div = document.createElement("div");
    div.textContent = `${new Date(h.date).toLocaleDateString("fr-FR", {day:"2-digit",month:"2-digit",year:"numeric",hour:"2-digit",minute:"2-digit"})} — ${h.user} — ${h.action}`;
    fabHistory.appendChild(div);
  });

  fabRemove.onclick = async () => {
    if (!fabCurrentFileName) return;
    if (!confirm("Retirer du planning ?")) return;

    const resp = await fetch("/api/delivery?fileName=" + encodeURIComponent(fabCurrentFileName), { method: "DELETE" }).then(r => r.json()).catch(() => ({ ok: false }));
    if (!resp.ok) { alert("Erreur"); return; }

    delete deliveriesByPath[fabCurrentFileName];
    delete deliveriesByPath[fabCurrentFileName + "_time"];
    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();
    await refreshKanban();
    updateGlobalAlert();
    alert("Retiré du planning");
  };

  // Show current stage (which folder the file is in)
  if (fabStageBanner) {
    fabStageBanner.style.display = "none";
    fetch("/api/file-stage?fileName=" + encodeURIComponent(fabCurrentFileName))
      .then(r => r.json())
      .then(s => {
        if (s.ok && s.folder) {
          fabStageBanner.textContent = "📍 Étape actuelle : " + s.folder;
          fabStageBanner.style.display = "block";
          // Update fabCurrentPath if the file was found at a different location
          if (s.fullPath) fabCurrentPath = normalizePath(s.fullPath);
        }
      })
      .catch(() => {});
  }

  fabModal.classList.remove("hidden");
}

fabSave.onclick = async () => {
  if (!fabCurrentPath) return;
  const ok = await saveFabrication();
  if (ok) {
    fabModal.classList.add("hidden");
    showNotification("✅ Fiche enregistrée", "success");
  }
};

async function saveFabrication() {
  if (!fabCurrentPath) return false;
  const fileName = fnKey(fabCurrentPath);

  const payload = {
    fullPath: fabCurrentPath,
    fileName: fileName,
    moteurImpression: fabMoteur.value,
    machine: fabMoteur.value,
    quantite: parseInt(fabQuantite.value) || null,
    typeTravail: fabType.value,
    format: fabFormat.value,
    rectoVerso: fabRectoVerso.value,
    client: fabClient.value,
    numeroDossier: fabNumeroDossier ? fabNumeroDossier.value || null : null,
    notes: fabNotes.value,
    faconnage: fabFaconnage.value,
    delai: fabDelai.value || null,
    media1: fabMedia1 ? fabMedia1.value || null : null,
    media2: fabMedia2 ? fabMedia2.value || null : null,
    media3: fabMedia3 ? fabMedia3.value || null : null,
    media4: fabMedia4 ? fabMedia4.value || null : null
  };

  const r = await fetch("/api/fabrication", {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
    body: JSON.stringify(payload)
  }).then(r => r.json());

  if (!r.ok) {
    alert("Erreur : " + r.error);
    return false;
  }
  return true;
}

fabPdf.onclick = async () => {
  if (!fabCurrentPath) return;

  // Save current form data first
  await saveFabrication();

  // Generate PDF and open in new tab
  try {
    const r = await fetch("/api/fabrication/pdf?fullPath=" + encodeURIComponent(fabCurrentPath) + "&save=true", {
      headers: { "Authorization": `Bearer ${authToken}` }
    });

    if (r.ok) {
      const blob = await r.blob();
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank");
      showNotification("✅ PDF généré et enregistré dans le dossier de production", "success");
    } else {
      const err = await r.json().catch(() => ({}));
      showNotification("❌ Erreur : " + (err.error || "Impossible de générer le PDF"), "error");
    }
  } catch(err) {
    showNotification("❌ Erreur réseau", "error");
  }
};

fabFinProd.onclick = async () => {
  if (!fabCurrentPath) {
    alert("Erreur : chemin introuvable");
    return;
  }

  if (!confirm("Marquer comme 'Fin de production' ?")) return;

  const moveResp = await fetch("/api/jobs/move", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ source: fabCurrentPath, destination: FIN_PROD_FOLDER, overwrite: true })
  }).then(r => r.json()).catch(() => ({ ok: false }));

  if (!moveResp.ok) {
    alert("Erreur : " + (moveResp.error || ""));
    return;
  }

  // Delivery is keyed by fileName — no need to re-create it after move
  updateGlobalAlert();
  await refreshKanban();
  await refreshSubmissionView();
  fabModal.classList.add("hidden");
  alert("Fin de production marquée");
};

if (fabPrisma) {
  fabPrisma.onclick = async () => {
    if (!fabCurrentPath) { alert("Chemin introuvable"); return; }
    if (!confirm("Déplacer vers PrismaPrepare ?")) return;
    const r = await fetch("/api/jobs/move", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ source: fabCurrentPath, destination: "PrismaPrepare", overwrite: true })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      if (r.moved) fabCurrentPath = normalizePath(r.moved);
      fabModal.classList.add("hidden");
      await refreshKanban();
      showNotification("✅ Fichier envoyé vers PrismaPrepare", "success");
    } else {
      alert("Erreur : " + (r.error || ""));
    }
  };
}

if (fabBat) {
  fabBat.onclick = async () => {
    if (!fabCurrentPath) { alert("Chemin introuvable"); return; }
    if (!confirm("Déplacer vers BAT ?")) return;
    const r = await fetch("/api/jobs/move", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ source: fabCurrentPath, destination: "BAT", overwrite: true })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      if (r.moved) fabCurrentPath = normalizePath(r.moved);
      fabModal.classList.add("hidden");
      await refreshKanban();
      showNotification("✅ Fichier envoyé vers BAT", "success");
    } else {
      alert("Erreur : " + (r.error || ""));
    }
  };
}

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

async function deleteFile(fullPath) {
  const fileName = fullPath.split("\\").pop();
  if (!confirm(`Supprimer "${fileName}" ?`)) return;

  try {
    const r = await fetch("/api/jobs/delete", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath })
    }).then(r => r.json());

    if (!r.ok) {
      alert("Erreur : " + r.error);
      return;
    }

    const fk = fnKey(fullPath);
    if (deliveriesByPath[fk]) { delete deliveriesByPath[fk]; delete deliveriesByPath[fk + "_time"]; }
    updateGlobalAlert();
    await refreshKanban();
    if (submissionCalendar) submissionCalendar.refetchEvents();
    alert("Supprimé");
  } catch (err) {
    console.error("Erreur:", err);
  }
}

async function handleDesktopDrop(e, folderName) {
  e.preventDefault();
  const files = Array.from(e.dataTransfer.files || []);
  if (!files.length) return;

  for (const file of files) {
    if (!file.name.toLowerCase().endsWith(".pdf")) {
      alert("Seuls les PDF !");
      continue;
    }

    const formData = new FormData();
    formData.append("file", file);
    formData.append("folder", folderName);

    const r = await fetch("/api/upload", { method: "POST", body: formData }).then(r => r.json());
    if (!r.ok) {
      alert("Erreur : " + r.error);
      continue;
    }

    openFabrication(r.fullPath);
  }

  await refreshKanban();
}

function openPlanificationCalendar(fullPath) {
  const today = new Date();
  const defaultDate = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

  const div = document.createElement("div");
  div.style.cssText = `position: fixed; inset: 0; background: rgba(0,0,0,.5); display: flex; align-items: center; justify-content: center; z-index: 10000;`;

  const panel = document.createElement("div");
  panel.style.cssText = `background: white; border-radius: 12px; padding: 20px; box-shadow: 0 10px 40px rgba(0,0,0,.3); min-width: 320px;`;
  panel.innerHTML = `
    <h3 style="margin-top: 0;">Planifier</h3>
    <label style="display: block; margin: 12px 0;"><strong>Date</strong><br/><input type="date" id="planDate" value="${defaultDate}" style="width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 6px; font-size: 14px;" /></label>
    <label style="display: block; margin: 12px 0;"><strong>Heure</strong><br/><input type="time" id="planTime" value="09:00" style="width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 6px; font-size: 14px;" /></label>
    <div style="display: flex; gap: 10px; margin-top: 20px;">
      <button id="planCancel" style="flex: 1; padding: 10px; border: 1px solid #ddd; background: white; border-radius: 6px; cursor: pointer; font-weight: 500;">Annuler</button>
      <button id="planOK" style="flex: 1; padding: 10px; border: 1px solid #D50000; background: #D50000; color: white; border-radius: 6px; cursor: pointer; font-weight: 500;">Planifier</button>
    </div>
  `;

  div.appendChild(panel);
  document.body.appendChild(div);

  panel.querySelector("#planCancel").onclick = () => div.remove();
  panel.querySelector("#planOK").onclick = async () => {
    const dateVal = panel.querySelector("#planDate").value;
    const timeVal = panel.querySelector("#planTime").value;

    if (!dateVal || !timeVal) {
      alert("Sélectionnez date et heure");
      return;
    }

    const r = await fetch("/api/delivery", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath, fileName: fnKey(fullPath), date: dateVal, time: timeVal })
    }).then(r => r.json());

    if (!r.ok) {
      alert("Erreur");
      return;
    }

    const fk = fnKey(fullPath);
    deliveriesByPath[fk] = dateVal;
    deliveriesByPath[fk + "_time"] = timeVal;

    updateGlobalAlert();
    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();
    await refreshKanban();
    await refreshSubmissionView();

    div.remove();
    alert(`Planifié pour ${dateVal}`);
  };
}

async function buildKanban() {
  const backendFolders = await fetch("/api/folders").then(r => r.json()).catch(() => []);
  
  const folderConfig = [
    { folder: "Début de production", label: "Début de production", color: "#5fa8c4" },
    { folder: "Corrections", label: "Corrections", color: "#e0e0e0" },
    { folder: "Corrections et fond perdu", label: "Corrections et fond perdu", color: "#e0e0e0" },
    { folder: "Rapport", label: "Rapport", color: "#cccccc" },
    { folder: "Prêt pour impression", label: "Prêt pour impression", color: "#b8b8b8" },
    { folder: "BAT", label: "BAT", color: "#a3a3a3" },
    { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f" },
    { folder: "Fiery", label: "Fiery", color: "#8f8f8f" },
    { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a" },
    { folder: "Façonnage", label: "Façonnage", color: "#666666" },
    { folder: "Fin de production", label: "Fin de production", color: "#22c55e" }
  ];

  kanbanDiv.innerHTML = "";
  kanbanDiv.style.gridTemplateColumns = "repeat(3, 1fr)";
  kanbanDiv.style.gap = "20px";
  kanbanDiv.style.padding = "20px";
  updateGlobalAlert();

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;
    title.style.color = isLight(cfg.color) ? '#1D1D1F' : '#FFFFFF';
    title.textContent = cfg.label;
    const counter = document.createElement("span");
    counter.className = "kanban-col-counter";
    counter.textContent = "0";
    title.appendChild(counter);
    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    if (cfg.folder === "Corrections" || cfg.folder === "Corrections et fond perdu") {
      const acrobatBtn = document.createElement("button");
      acrobatBtn.className = "btn btn-acrobat";
      acrobatBtn.textContent = "Ouvrir dans Acrobat Pro";
      acrobatBtn.style.cssText = "margin: 0 15px 10px 15px; width: calc(100% - 30px);";
      acrobatBtn.onclick = async () => {
        try {
          const resp = await fetch("/api/acrobat", { method: "POST" });
          if (!resp.ok) throw new Error("Erreur au lancement d'Acrobat");
          alert("Acrobat Pro est en cours de lancement...");
        } catch (err) {
          alert("❌ " + err.message);
        }
      };
      col.insertBefore(acrobatBtn, drop);
    }

    drop.addEventListener("dragover", e => {
      e.preventDefault();
      drop.classList.add("drag-over");
    });

    drop.addEventListener("dragleave", () => {
      drop.classList.remove("drag-over");
    });

    drop.addEventListener("drop", async (e) => {
      e.preventDefault();
      drop.classList.remove("drag-over");

      if (e.dataTransfer && e.dataTransfer.files?.length) {
        handleDesktopDrop(e, cfg.folder);
        return;
      }

      const srcFull = normalizePath(e.dataTransfer.getData("text/plain"));
      if (!srcFull) return;

      const destFolder = cfg.folder;
      const moveResp = await fetch("/api/jobs/move", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ source: srcFull, destination: destFolder, overwrite: true })
      }).then(r => r.json()).catch(() => ({ ok: false }));

      if (!moveResp.ok) {
        alert("Erreur : " + (moveResp.error || ""));
        return;
      }

      // Deliveries and assignments are keyed by fileName — no path update needed.
      // If moving to Fin de production, optionally remove from planning.
      if (destFolder === FOLDER_FIN_PRODUCTION) {
        const srcFk = fnKey(srcFull);
        if (deliveriesByPath[srcFk]) {
          const remove = confirm("Retirer du planning ?");
          if (remove) {
            await fetch("/api/delivery?fileName=" + encodeURIComponent(srcFk), { method: "DELETE" });
            delete deliveriesByPath[srcFk];
            delete deliveriesByPath[srcFk + "_time"];
          }
        }
      }

      await loadDeliveries();
      await loadAssignments();
      updateGlobalAlert();
      await refreshKanban();
      await refreshSubmissionView();
      calendar?.refetchEvents();
      submissionCalendar?.refetchEvents();
    });

    kanbanDiv.appendChild(col);
  }

  // Create summary div before kanban
  let summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) {
    summaryEl = document.createElement("div");
    summaryEl.id = "kanban-summary";
    summaryEl.className = "kanban-summary";
    kanbanDiv.parentNode?.insertBefore(summaryEl, kanbanDiv);
  }

  await refreshKanban();

  if (searchInput) searchInput.oninput = () => refreshKanban();
  if (sortBy) sortBy.onchange = () => refreshKanban();
}

function darkenColor(color, percent) {
  const num = parseInt(color.replace("#", ""), 16);
  const amt = Math.round(2.55 * percent);
  const R = (num >> 16) - amt;
  const G = (num >> 8 & 0x00FF) - amt;
  const B = (num & 0x0000FF) - amt;
  return "#" + (0x1000000 + (R < 255 ? R < 1 ? 0 : R : 255) * 0x10000 +
    (G < 255 ? G < 1 ? 0 : G : 255) * 0x100 +
    (B < 255 ? B < 1 ? 0 : B : 255))
    .toString(16).slice(1);
}

async function refreshKanban() {
  const q = (searchInput?.value || "").trim().toLowerCase();
  const sort = (sortBy?.value || "date_desc");

  const cols = kanbanDiv.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnOperator(col.dataset.folder, q, sort, col);
  }
  await updateKanbanSummary();

  // Trigger automatic cleanup of source files in Corrections folders
  fetch("/api/jobs/cleanup-corrections", { method: "POST" }).catch(() => {});
}

async function updateKanbanSummary() {
  const summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) return;

  try {
    const folders = ["Début de production","Corrections","Corrections et fond perdu","Rapport","Prêt pour impression","BAT","PrismaPrepare","Fiery","Impression en cours","Façonnage","Fin de production"];
    const counts = {};
    for (const f of folders) {
      const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(f)}`).then(r => r.json()).catch(() => []);
      counts[f] = Array.isArray(jobs) ? jobs.length : 0;
    }

    const labelMap = {
      "Début de production": "Début prod",
      "Corrections": "Corrections",
      "Corrections et fond perdu": "Corr. fp",
      "Rapport": "Rapport",
      "Prêt pour impression": "Prêt impr.",
      "BAT": "BAT",
      "PrismaPrepare": "Prisma",
      "Fiery": "Fiery",
      "Impression en cours": "Impression",
      "Façonnage": "Façonnage",
      "Fin de production": "Fin prod"
    };

    const countsHtml = folders.map(f =>
      `<span class="kanban-summary-count">${labelMap[f]}: <strong>${counts[f]}</strong></span>`
    ).join("");

    const today = new Date(); today.setHours(0,0,0,0);
    const urgent = Object.entries(deliveriesByPath)
      .filter(([k]) => !k.endsWith("_time"))
      .map(([name, date]) => {
        const d = new Date(date + "T00:00:00");
        const diff = Math.ceil((d - today) / 86400000);
        return { name, date, diff };
      })
      .filter(x => x.diff >= 0 && x.diff <= 3)
      .sort((a,b) => a.diff - b.diff);

    const urgentHtml = urgent.length === 0 ? '<span style="color:#9ca3af;font-size:14px;">Aucune urgence</span>' :
      urgent.map(x => {
        const cls = x.diff === 0 ? "urgent-j0" : x.diff === 1 ? "urgent-j1" : x.diff === 2 ? "urgent-j2" : "urgent-j3";
        const label = x.diff === 0 ? "Aujourd'hui" : `J+${x.diff}`;
        return `<span class="urgent-badge ${cls}" title="${x.name}">${label}: ${x.name}</span>`;
      }).join("");

    summaryEl.innerHTML = `
      <div class="kanban-summary-urgent"><strong style="font-size:16px;color:#374151;margin-right:8px;">🚨 Urgences:</strong>${urgentHtml}</div>
    `;
  } catch(e) { console.error("Erreur summary:", e); }
}

async function openPrintDialog(fullPath) {
  const fab = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fnKey(fullPath))).then(r=>r.json()).catch(()=>({}));

  const modal = document.createElement("div");
  modal.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:center;justify-content:center;";
  modal.innerHTML = `
    <div style="background:white;border-radius:16px;padding:32px;max-width:440px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,0.3);">
      <h3 style="margin:0 0 20px;font-size:18px;color:#111;">Options d'impression</h3>
      <p style="color:#6b7280;font-size:13px;margin-bottom:20px;">Fichier: <strong>${fullPath.split("\\").pop()}</strong></p>
      <div style="display:flex;flex-direction:column;gap:10px;">
        <button class="btn btn-primary print-opt" data-action="controller">Envoyer vers le contrôleur</button>
        <button class="btn btn-primary print-opt" data-action="prisma">Envoyer vers PrismaPrepare</button>
        <button class="btn btn-primary print-opt" data-action="print">Envoyer en impression</button>
        <button class="btn btn-primary print-opt" data-action="modify">Modification</button>
        <button class="btn btn-primary print-opt" data-action="fiery">Envoyer sur Fiery</button>
      </div>
      <button id="print-dialog-close" class="btn" style="margin-top:16px;width:100%;">Annuler</button>
    </div>
  `;

  const printActions = {
    controller: { endpoint: "/api/commands/send-controller" },
    prisma: { endpoint: "/api/commands/send-prisma" },
    print: { endpoint: "/api/commands/send-print" },
    modify: { endpoint: "/api/commands/modify" },
    fiery: { endpoint: "/api/commands/send-fiery" }
  };

  modal.querySelectorAll(".print-opt").forEach(btn => {
    btn.onclick = async () => {
      const action = btn.dataset.action;
      const cfg = printActions[action];
      try {
        const r = await fetch(cfg.endpoint, {
          method: "POST",
          headers: {"Content-Type":"application/json"},
          body: JSON.stringify({ filePath: fullPath, product: fab.typeTravail || "", fabricationData: fab })
        }).then(r => r.json()).catch(()=>({ok:false,error:"Erreur réseau"}));

        if (r.ok) {
          showNotification(`✅ Action lancée`, "success");
          modal.remove();
          await refreshKanban();
        } else {
          showNotification("❌ " + (r.error || "Erreur"), "error");
        }
      } catch(e) {
        showNotification("❌ " + e.message, "error");
      }
    };
  });

  modal.querySelector("#print-dialog-close").onclick = () => modal.remove();
  document.body.appendChild(modal);
}

// Store previous column data to avoid unnecessary DOM rebuilds
const _columnCache = {};

async function refreshKanbanColumnOperator(folderName, q, sort, col, readOnly = false) {
  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(folderName)}`)
      .then(r => r.json())
      .catch(() => []);

    // Create fingerprint including file list, assignments, and deliveries for this column
    const fingerprint = JSON.stringify(jobs.map(j => {
      const fn = fnKey(j.fullPath || j.name || '');
      return (j.name || '') + '|' + j.modified + '|' + j.size
        + '|' + ((assignmentsByPath[fn] || {}).operatorName || '')
        + '|' + (deliveriesByPath[fn] || '');
    }));
    const cacheKey = folderName + '|' + q + '|' + sort;
    if (_columnCache[cacheKey] === fingerprint) {
      return; // No changes, skip DOM rebuild
    }
    _columnCache[cacheKey] = fingerprint;

    const drop = col.querySelector(".kanban-col-operator__drop");
    drop.innerHTML = "";

    let filtered = jobs;
    if (q) {
      filtered = jobs.filter(j => (j.name || "").toLowerCase().includes(q.toLowerCase()));
    }

    if (sort === "name_asc") filtered.sort((a, b) => (a.name || "").localeCompare(b.name || ""));
    else if (sort === "name_desc") filtered.sort((a, b) => (b.name || "").localeCompare(a.name || ""));
    else if (sort === "size_asc") filtered.sort((a, b) => (a.size || 0) - (b.size || 0));
    else if (sort === "size_desc") filtered.sort((a, b) => (b.size || 0) - (a.size || 0));
    else filtered.sort((a, b) => new Date(b.modified) - new Date(a.modified));

    for (const job of filtered) {
      const card = document.createElement("div");
      card.className = "kanban-card-operator";
      if (!readOnly) {
        card.draggable = true;
      }
      card.dataset.fullPath = normalizePath(job.fullPath || "");
      card.dataset.folder = folderName;

      const full = normalizePath(job.fullPath || "");

      // Horizontal layout: thumbnail left + info right
      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      const textDiv = document.createElement("div");
      textDiv.style.cssText = "flex: 1; min-width: 0;";

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      textDiv.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      textDiv.appendChild(sub);

      // Assignment badge — look up by fileName (path-change resilient)
      const jobFileName = fnKey(full);
      const assignment = assignmentsByPath[jobFileName];
      if (assignment) {
        const badge = document.createElement("div");
        badge.className = "assignment-badge";
        badge.textContent = `${assignment.operatorName}`;
        textDiv.appendChild(badge);
      }

      const iso = deliveriesByPath[jobFileName];
      if (iso) {
        const status = document.createElement("div");
        status.className = "kanban-card-operator-status";
        const daysLeft = daysDiffFromToday(iso);
        if (daysLeft <= 1) {
          status.classList.add("urgent");
        } else if (daysLeft <= 3) {
          status.classList.add("warning");
        }
        status.textContent = new Date(iso).toLocaleDateString("fr-FR");
        textDiv.appendChild(status);
      }

      layout.appendChild(textDiv);
      card.appendChild(layout);

      // Actions row below
      const actions = document.createElement("div");
      actions.className = "kanban-card-operator-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => openFabrication(full);

      const btnAssign = document.createElement("button");
      btnAssign.className = "btn btn-sm btn-assign";
      btnAssign.textContent = "Affecter à";
      btnAssign.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssign, full); };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-sm";
      btnDelete.textContent = "Corbeille";
      btnDelete.onclick = () => deleteFile(full);

      if (folderName === "Rapport") {
        actions.appendChild(btnOpen);
        const btnAcrobat = document.createElement("button");
        btnAcrobat.className = "btn btn-sm";
        btnAcrobat.innerHTML = "Acrobat Pro";
        btnAcrobat.onclick = () => {
          fetch("/api/acrobat/open", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fullPath: full })
          }).then(r => r.json()).then(r => {
            if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
          });
        };
        actions.appendChild(btnAcrobat);

        const btnDelSrc = document.createElement("button");
        btnDelSrc.className = "btn btn-sm";
        btnDelSrc.style.cssText = "color:#dc2626;border-color:#dc2626;";
        btnDelSrc.innerHTML = "Supprimer source";
        btnDelSrc.onclick = async () => {
          if (!confirm("Supprimer le fichier source dans Corrections / Corrections et fond perdu ?")) return;
          const r = await fetch("/api/jobs/delete-corrections-source", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fileName: fileName })
          }).then(r => r.json()).catch(() => ({ok: false}));
          if (r.ok) { showNotification("✅ Source supprimée", "success"); await refreshKanban(); }
          else showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        actions.appendChild(btnDelSrc);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "BAT") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnAssign);
        const btnAcrobatBat = document.createElement("button");
        btnAcrobatBat.className = "btn btn-sm";
        btnAcrobatBat.innerHTML = "Acrobat";
        btnAcrobatBat.onclick = () => {
          fetch("/api/acrobat/open", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fullPath: full })
          }).then(r => r.json()).then(r => {
            if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
          });
        };
        actions.appendChild(btnAcrobatBat);
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Prêt pour impression") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        const btnBAT = document.createElement("button");
        btnBAT.className = "btn btn-sm btn-primary";
        btnBAT.innerHTML = "🖨 PrismaPrepare";
        btnBAT.onclick = async () => {
          if (!confirm("Déplacer vers PrismaPrepare ?")) return;
          btnBAT.disabled = true;
          btnBAT.textContent = "…";
          try {
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
              body: JSON.stringify({ source: full, destination: "PrismaPrepare", overwrite: true })
            }).then(r => r.json()).catch(() => ({ ok: false }));
            if (r.ok) {
              showNotification("✅ Fichier envoyé vers PrismaPrepare", "success");
              await refreshKanban();
            } else {
              showNotification("❌ " + (r.error || "Erreur"), "error");
            }
          } finally {
            btnBAT.disabled = false;
            btnBAT.innerHTML = "🖨 PrismaPrepare";
          }
        };
        actions.appendChild(btnBAT);

        const btnPrint = document.createElement("button");
        btnPrint.className = "btn btn-sm btn-primary";
        btnPrint.innerHTML = "Imprimer";
        btnPrint.onclick = () => openPrintDialog(full);
        actions.appendChild(btnPrint);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      }

      card.appendChild(actions);

      // BAT tracking for BAT folder
      if (folderName === "BAT") {
        const batTracking = document.createElement("div");
        batTracking.className = "bat-tracking";
        batTracking.innerHTML = '<span style="color:var(--text-tertiary);font-size:10px;">Chargement...</span>';
        fetch(`/api/bat/status?path=${encodeURIComponent(full)}`)
          .then(r => r.json())
          .then(status => {
            batTracking.innerHTML = "";

            const btnSent = document.createElement("button");
            btnSent.className = "bat-status-badge bat-sent" + (status.sentAt ? " active" : "");
            btnSent.innerHTML = status.sentAt
              ? `ENVOYÉ ${formatDateTime(status.sentAt)}`
              : "MARQUER ENVOYÉ";
            btnSent.onclick = (e) => { e.stopPropagation(); fetch("/api/bat/send",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            const btnValidate = document.createElement("button");
            btnValidate.className = "bat-status-badge bat-validated" + (status.validatedAt ? " active" : "");
            btnValidate.innerHTML = status.validatedAt
              ? `VALIDÉ ${formatDateTime(status.validatedAt)}`
              : "VALIDER";
            btnValidate.onclick = (e) => { e.stopPropagation(); fetch("/api/bat/validate",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            const btnReject = document.createElement("button");
            btnReject.className = "bat-status-badge bat-rejected" + (status.rejectedAt ? " active" : "");
            btnReject.innerHTML = status.rejectedAt
              ? `REFUSÉ ${formatDateTime(status.rejectedAt)}`
              : "REFUSER";
            btnReject.onclick = (e) => { e.stopPropagation(); fetch("/api/bat/reject",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            batTracking.appendChild(btnSent);
            batTracking.appendChild(btnValidate);
            batTracking.appendChild(btnReject);

            // J+2 alert: BAT sent > 2 days without validation or rejection
            if (status.sentAt && !status.validatedAt && !status.rejectedAt) {
              const sentDate = new Date(status.sentAt);
              const now = new Date();
              const diffDays = (now - sentDate) / (1000 * 60 * 60 * 24);
              if (diffDays >= 2) {
                const alertJ2 = document.createElement("div");
                alertJ2.className = "bat-alert-j2";
                alertJ2.textContent = "⚠️ BAT envoyé depuis plus de 2 jours !";
                batTracking.appendChild(alertJ2);
              }
            }
          }).catch(() => { batTracking.innerHTML = ""; });
        card.appendChild(batTracking);
      }

      if (!readOnly) {
        card.addEventListener("dragstart", (e) => {
          e.dataTransfer.effectAllowed = "move";
          e.dataTransfer.setData("text/plain", card.dataset.fullPath);
        });
      }

      drop.appendChild(card);
    }

    // Update column counter
    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = filtered.length;
  } catch (err) {
    console.error("Erreur refresh kanban operator:", err);
  }
}

async function openAssignDropdown(btn, fullPath) {
  // Close any open assign dropdowns
  document.querySelectorAll(".assign-dropdown").forEach(d => d.remove());

  let operators = [];
  try {
    const resp = await fetch("/api/operators").then(r => r.json());
    operators = resp.operators || [];
  } catch(err) {
    showNotification("❌ Impossible de charger les opérateurs", "error");
    return;
  }

  if (operators.length === 0) {
    showNotification("ℹ️ Aucun opérateur disponible", "info");
    return;
  }

  const dropdown = document.createElement("div");
  dropdown.className = "assign-dropdown";
  dropdown.style.cssText = `
    position: absolute; background: white; border: 1px solid #e5e7eb;
    border-radius: 8px; box-shadow: 0 4px 16px rgba(0,0,0,0.15);
    z-index: 9999; min-width: 180px; overflow: hidden;
  `;

  operators.forEach(op => {
    const item = document.createElement("div");
    item.style.cssText = "padding: 10px 14px; cursor: pointer; font-size: 13px; transition: background 0.15s;";
    item.textContent = op.name;
    item.onmouseenter = () => item.style.background = "#f3f4f6";
    item.onmouseleave = () => item.style.background = "";
    item.onclick = async () => {
      dropdown.remove();
      const fileName = fnKey(fullPath);
      const r = await fetch("/api/assignment", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ fullPath, fileName, operatorId: op.id })
      }).then(r => r.json()).catch(() => ({ ok: false }));

      if (r.ok) {
        const asgn = { fullPath, fileName, operatorName: r.operatorName || op.name, operatorId: op.id };
        assignmentsByPath[fileName] = asgn;
        showNotification(`✅ Job affecté à ${r.operatorName || op.name}`, "success");
        await refreshKanban();
      } else {
        showNotification("❌ Erreur : " + (r.error || ""), "error");
      }
    };
    dropdown.appendChild(item);
  });

  // Position relative to button
  const rect = btn.getBoundingClientRect();
  dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
  dropdown.style.left = (rect.left + window.scrollX) + "px";
  document.body.appendChild(dropdown);

  // Close on outside click
  setTimeout(() => {
    document.addEventListener("click", function closeDropdown() {
      dropdown.remove();
      document.removeEventListener("click", closeDropdown);
    }, { once: true });
  }, 10);
}

function colorForEvent(fullPath, isoDate) {
  const normalized = normalizePath(fullPath).toLowerCase();
  const finProdLower = FIN_PROD_FOLDER.toLowerCase();
  
  if (normalized.includes(finProdLower)) {
    return { bg: "#16A34A", bc: "#16A34A", tc: "#fff" };
  }

  const d = daysDiffFromToday(isoDate);
  if (d <= 1) return { bg: "#DC2626", bc: "#DC2626", tc: "#fff" };
  if (d <= 3) return { bg: "#F59E0B", bc: "#F59E0B", tc: "#111827" };
  return { bg: "#2563EB", bc: "#2563EB", tc: "#fff" };
}

async function openInAcrobatPro(fullPath) {
  const r = await fetch("/api/acrobat/open", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ fullPath })
  }).then(r => r.json()).catch(() => ({ ok: false }));

  if (!r.ok) {
    alert("Impossible d'ouvrir : " + (r.error || ""));
  }
}

async function initCalendar() {
  if (calendar || !calendarEl || !window.FullCalendar) return;

  let schedStart = "07:00", schedEnd = "21:00";
  try {
    const sr = await fetch("/api/config/schedule", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
    if (sr.ok && sr.config) {
      if (sr.config.workStart) schedStart = sr.config.workStart;
      if (sr.config.workEnd) {
        const [h, m] = sr.config.workEnd.split(":").map(Number);
        const endH = Math.min(h + 1, 24);
        schedEnd = `${String(endH).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      }
    }
  } catch(e) { /* use defaults */ }

  calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
    locale: "fr",
    timeZone: "local",
    height: 480,
    scrollTime: schedStart,
    slotMinTime: schedStart,
    slotMaxTime: schedEnd,
    headerToolbar: {
      left: "prev,next today",
      center: "title",
      right: "dayGridMonth,timeGridWeek"
    },
    editable: true,
    eventDurationEditable: false,
    events: async (_info, success) => {
      try {
        const list = await fetch("/api/delivery").then(r => r.json());
        const ev = list.map(x => {
          const full = normalizePath(x.fullPath);
          const { bg, bc, tc } = colorForEvent(full, x.date);
          const time = x.time || "09:00";

          return {
            title: x.fileName,
            start: `${x.date}T${time}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time }
          };
        });
        try {
          const schedResp = await fetch("/api/config/schedule", {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json());
          if (schedResp.ok && schedResp.config && schedResp.config.holidays) {
            schedResp.config.holidays.forEach(h => {
              ev.push({
                title: "Férié",
                start: h,
                allDay: true,
                display: "background",
                backgroundColor: "#fee2e2",
                borderColor: "#fecaca"
              });
            });
          }
        } catch(e) { console.error("Impossible de charger les jours fériés:", e); }
        success(ev);
      } catch (err) {
        console.error("Erreur events:", err);
        success([]);
      }
    },
    eventDidMount: (info) => {
      const { bg, bc, tc } = info.event.extendedProps || {};
      if (bg) info.el.style.setProperty("--fc-event-bg-color", bg);
      if (bc) info.el.style.setProperty("--fc-event-border-color", bc);
      if (tc) info.el.style.setProperty("--fc-event-text-color", tc);
    },
    eventDrop: async (info) => {
      try {
        const fullPath = normalizePath(info.event.extendedProps.fullPath);
        const fk = fnKey(fullPath);
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fk] = newDate;
        deliveriesByPath[fk + "_time"] = newTime;

        const { bg, bc, tc } = colorForEvent(fullPath, newDate);
        info.event.setProp("backgroundColor", bg);
        info.event.setProp("borderColor", bc);
        info.event.setProp("textColor", tc);

        updateGlobalAlert();
        await refreshKanban();
      } catch (err) {
        alert(err.message || "Impossible de déplacer");
        info.revert();
      }
    },
    eventClick: (info) => {
      info.jsEvent.preventDefault();
      const full = normalizePath(info.event.extendedProps.fullPath);
      if (full) openFabrication(full);
    }
  });

  calendar.render();
}

function showNotification(message, type = "info") {
  const toast = document.createElement("div");
  toast.className = `toast-notification toast-${type === "success" ? "success" : type === "error" ? "error" : "info"}`;
  toast.textContent = message;
  document.body.appendChild(toast);

  setTimeout(() => {
    toast.style.animation = "toastFadeOut 0.3s ease forwards";
    setTimeout(() => toast.remove(), 300);
  }, 3000);
}

async function loadDeliveries() {
  try {
    const list = await fetch("/api/delivery").then(r => r.json()).catch(() => []);
    const newDeliveries = {};

    list.forEach(x => {
      const fname = fnKey(x.fileName || x.fullPath || "");
      if (fname) {
        newDeliveries[fname] = x.date;
        newDeliveries[fname + "_time"] = x.time || "09:00";
      }
    });

    deliveriesByPath = newDeliveries;
  } catch (err) {
    console.error("Erreur loadDeliveries:", err);
  }
}

document.addEventListener("dragover", e => e.preventDefault());
document.addEventListener("drop", e => {
  if (!e.target.closest(".kanban-col__drop")) {
    e.preventDefault();
  }
});

console.log("GestionAtelier PRO — app.js chargé avec succès");