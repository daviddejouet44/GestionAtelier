"use strict";

let currentUser = null;
let authToken = null;

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
document.getElementById("btn-accounts").onclick = showAccountsModal;

// ======================================================
// SETUP PROFILS
// ======================================================

function setupProfileUI() {
  const btnSubmission = document.getElementById("btnViewSubmission");
  const btnSettings = document.getElementById("btn-settings");
  const btnAccounts = document.getElementById("btn-accounts");
  const btnProduction = document.getElementById("btnViewProduction");
  const btnRecycle = document.getElementById("btnViewRecycle");
  const userInfo = document.getElementById("user-info");

  userInfo.textContent = `${currentUser.name} (Profil ${currentUser.profile})`;

  // Corbeille visible for all profiles
  if (btnRecycle) btnRecycle.style.display = "inline-block";

  if (currentUser.profile === 1) {
    btnSubmission.style.display = "inline-block";
    btnProduction.style.display = "inline-block";
  } else if (currentUser.profile === 2) {
    btnSubmission.style.display = "inline-block";
  } else if (currentUser.profile === 3) {
    btnSettings.style.display = "inline-block";
    btnAccounts.style.display = "inline-block";
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
  } else if (currentUser.profile === 3) {
    btnKanban.style.display = "inline-block";
    btnCalendar.style.display = "inline-block";
    btnSubmission.style.display = "inline-block";
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
  document.getElementById("settings-view").classList.add("hidden");
  document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
}

function showKanban() {
  hideAllViews();
  document.getElementById("kanban").classList.remove("hidden");
  document.getElementById("btnViewKanban").classList.add("active");
  refreshKanban();
}

function showCalendar() {
  hideAllViews();
  document.getElementById("calendar").classList.remove("hidden");
  document.getElementById("btnViewCalendar").classList.add("active");
  ensureCalendar();
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
  
  // Vide et reconstruit
  productionEl.innerHTML = "";
  
  // Crée un conteneur pour le kanban (même style que le kanban opérateur)
  const kanbanContainer = document.createElement("div");
  kanbanContainer.id = "productionKanban";
  kanbanContainer.style.cssText = "display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; padding: 20px; width: 100%;";
  
  productionEl.appendChild(kanbanContainer);
  
  // Charge les affectations puis construit le kanban
  loadAssignments().then(() => buildProductionKanban(kanbanContainer));
}

async function buildProductionKanban(container) {
  const folderConfig = [
    { folder: "1.Reception", label: "Début de production", color: "#5fa8c4" },
    { folder: "2.Corrections", label: "Corrections", color: "#5fa8c4" },
    { folder: "3.Rapport", label: "Rapport", color: "#5fa8c4" },
    { folder: "2.Corrections + fond perdu", label: "Corrections et fond perdu", color: "#5fa8c4" },
    { folder: "6.Archivage", label: "Prêt pour impression", color: "#5fa8c4" },
    { folder: "4.BAT", label: "BAT", color: "#5fa8c4" },
    { folder: "5.Relecture", label: "Impression en cours", color: "#5fa8c4" },
    { folder: "7.Termine", label: "PrismaPrepare", color: "#6b7e89" },
    { folder: "8. Fin de production", label: "Fiery", color: "#6b7e89" },
    { folder: "9. Archived", label: "Fin de production", color: "#22c55e" }
  ];

  container.innerHTML = "";

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    col.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;

    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.textContent = cfg.label;
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

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      card.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      card.appendChild(sub);

      const full = normalizePath(job.fullPath || "");

      // Assignment badge (read-only view — visible but no action)
      const assignment = assignmentsByPath[full];
      if (assignment) {
        const badge = document.createElement("div");
        badge.className = "assignment-badge";
        badge.textContent = `👤 ${assignment.operatorName}`;
        card.appendChild(badge);
      }

      const iso = deliveriesByPath[full];
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
      
      <!-- CALENDRIER EN HAUT (100% width) -->
      <div class="submission-section">
        <h3>📅 Planning de livraison</h3>
        <div id="submissionCalendar" class="submission-calendar"></div>
      </div>

      <!-- LAYOUT : DRAG & DROP + FICHIERS -->
      <div class="submission-split">
        
        <!-- GAUCHE : DRAG & DROP -->
        <div class="submission-upload-section">
          <h3>📤 Déposer fichiers</h3>
          <div class="upload-zone" id="uploadZone">
            <div class="upload-icon">📁</div>
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
            <h3 style="margin: 0;">📋 Fichiers soumis</h3>
            <div style="display: flex; gap: 8px;">
              <button id="btnSelectAll" class="btn btn-sm">Sélectionner tout</button>
              <button id="btnSendAnalysis" class="btn btn-primary btn-sm">Envoyer analyse</button>
            </div>
          </div>
          <div id="submissionKanban" class="submission-kanban"></div>
        </div>

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

function initSubmissionCalendar() {
  const calendarEl = document.getElementById("submissionCalendar");
  if (!calendarEl || submissionCalendar) return;

  submissionCalendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "timeGridWeek",
    locale: "fr",
    height: "auto",
    slotLabelInterval: "01:00",
    slotMinTime: "07:00",
    slotMaxTime: "21:00",
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
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fullPath] = newDate;
        deliveriesByPath[fullPath + "_time"] = newTime;

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

    uploadStatus.textContent = `📤 Envoi ${file.name}...`;

    try {
      const formData = new FormData();
      formData.append("file", file);
      formData.append("folder", "1.Reception");

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
    const jobs = await fetch("/api/jobs?folder=1.Reception").then(r => r.json()).catch(() => []);

    submissionKanban.innerHTML = "";
    if (jobs.length === 0) {
      submissionKanban.innerHTML = `<div style="text-align: center; padding: 40px; color: #9ca3af;"><p>📭 Aucun fichier</p></div>`;
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
        preview.innerHTML = '<div class="submission-card-preview-text">📄</div>';
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

      const iso = deliveriesByPath[full];
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

      const btnPlan = document.createElement("button");
      btnPlan.className = "btn btn-primary";
      btnPlan.textContent = "Planifier";
      btnPlan.onclick = () => openPlanificationCalendar(full);
      actions.appendChild(btnPlan);

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn";
      btnDelete.textContent = "🗑️";
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

    if (!confirm(`Envoyer ${checkboxes.length} fichier(s) à l'analyse ?`)) return;

    let successCount = 0;

    for (const checkbox of checkboxes) {
      const card = checkbox.closest(".submission-card");
      const fullPath = card.dataset.fullPath;

      try {
        const r = await fetch("/api/jobs/move", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ source: fullPath, destination: "2.Analyse", overwrite: true })
        }).then(r => r.json());

        if (r.ok) successCount++;
      } catch (err) {
        console.error("Erreur:", err);
      }
    }

    if (successCount > 0) {
      showNotification(`✅ ${successCount} fichier(s) envoyé(s)`, "success");
      await refreshSubmissionView();
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
      <h2>🗂️ Corbeille</h2>
      <div style="display: flex; gap: 10px; margin-bottom: 16px;">
        <button id="recycle-refresh" class="btn btn-primary">🔄 Rafraîchir</button>
        <button id="recycle-purge" class="btn">🗑️ Purger les anciens fichiers (&gt; 7 jours)</button>
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

  try {
    const files = await fetch("/api/recycle/list").then(r => r.json()).catch(() => []);

    if (!Array.isArray(files) || files.length === 0) {
      listEl.innerHTML = '<p style="color: #9ca3af; text-align: center; padding: 20px;">📭 La corbeille est vide</p>';
      return;
    }

    listEl.innerHTML = "";
    files.forEach(f => {
      const div = document.createElement("div");
      div.style.cssText = "display: flex; align-items: center; gap: 10px; padding: 12px 16px; border-bottom: 1px solid #e5e7eb; background: white; border-radius: 6px; margin-bottom: 6px;";
      div.innerHTML = `
        <div style="flex: 1;">
          <strong>${f.fileName}</strong>
          <small style="display: block; color: #6b7280;">Supprimé le ${new Date(f.deletedAt).toLocaleDateString("fr-FR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}</small>
        </div>
        <button class="btn btn-sm btn-primary" data-path="${f.fullPath}">↩️ Restaurer</button>
      `;
      div.querySelector("button").onclick = () => restoreFromRecycle(f.fullPath, f.fileName);
      listEl.appendChild(div);
    });
  } catch (err) {
    listEl.innerHTML = `<p style="color: #ef4444;">Erreur : ${err.message}</p>`;
  }
}

async function restoreFromRecycle(fullPath, fileName) {
  const folder = prompt(`Restaurer "${fileName}" dans quel dossier ?`, "1.Reception");
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
      <h2>⚙️ Paramétrage</h2>
      <div class="settings-tabs">
        <button class="settings-tab active" data-tab="accounts">👥 Comptes &amp; Rôles</button>
        <button class="settings-tab" data-tab="schedule">📅 Plages horaires</button>
        <button class="settings-tab" data-tab="paths">📁 Chemins d'accès</button>
        <button class="settings-tab" data-tab="integrations">🔌 Prepare / Fiery</button>
        <button class="settings-tab" data-tab="fabrication-imports">📄 Imports fiche</button>
        <button class="settings-tab" data-tab="logs">📋 Logs</button>
        <button class="settings-tab" data-tab="stats">📊 Dashboard</button>
      </div>
      <div class="settings-panel" id="settings-panel-accounts"></div>
      <div class="settings-panel hidden" id="settings-panel-schedule"></div>
      <div class="settings-panel hidden" id="settings-panel-paths"></div>
      <div class="settings-panel hidden" id="settings-panel-integrations"></div>
      <div class="settings-panel hidden" id="settings-panel-fabrication-imports"></div>
      <div class="settings-panel hidden" id="settings-panel-logs"></div>
      <div class="settings-panel hidden" id="settings-panel-stats"></div>
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
    case "integrations": renderSettingsIntegrations(panelEl); break;
    case "fabrication-imports": await renderSettingsFabricationImports(panelEl); break;
    case "logs": await renderSettingsLogs(panelEl); break;
    case "stats": await renderSettingsStats(panelEl); break;
  }
  panelEl._loaded = true;
}

async function renderSettingsAccounts(panel) {
  panel.innerHTML = `
    <h3>👥 Gestion des comptes et des rôles</h3>
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
  panel.innerHTML = `<h3>📅 Plages horaires et jours fériés</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { workStart: "08:00", workEnd: "18:00", holidays: [] };
  try {
    const resp = await fetch("/api/config/schedule", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  const holidays = Array.isArray(cfg.holidays) ? cfg.holidays : [];

  panel.innerHTML = `
    <h3>📅 Plages horaires et jours fériés</h3>
    <div class="settings-form-group">
      <label>Début journée</label>
      <input type="time" id="sch-start" value="${cfg.workStart || '08:00'}" class="settings-input" />
    </div>
    <div class="settings-form-group">
      <label>Fin journée</label>
      <input type="time" id="sch-end" value="${cfg.workEnd || '18:00'}" class="settings-input" />
    </div>
    <button id="sch-save" class="btn btn-primary" style="margin-top: 10px;">💾 Enregistrer les plages</button>
    <hr style="margin: 20px 0;" />
    <h4>Jours fériés</h4>
    <div style="display: flex; gap: 8px; margin-bottom: 10px;">
      <input type="date" id="sch-holiday-date" class="settings-input" />
      <button id="sch-add-holiday" class="btn btn-primary">Ajouter</button>
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
    if (r.ok) showNotification("✅ Plages horaires enregistrées", "success");
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

async function renderSettingsPaths(panel) {
  panel.innerHTML = `<h3>📁 Chemins d'accès aux dossiers</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { hotfoldersRoot: "C:\\Flux", recycleBinPath: "" };
  try {
    const resp = await fetch("/api/config/paths", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>📁 Chemins d'accès aux dossiers</h3>
    <div class="settings-form-group">
      <label>Racine des hotfolders (GA_HOTFOLDERS_ROOT)</label>
      <input type="text" id="paths-hotfolders" value="${cfg.hotfoldersRoot || 'C:\\\\Flux'}" class="settings-input" style="width: 100%; max-width: 500px;" />
    </div>
    <div class="settings-form-group">
      <label>Chemin corbeille</label>
      <input type="text" id="paths-recycle" value="${cfg.recycleBinPath || ''}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Corbeille" />
    </div>
    <button id="paths-save" class="btn btn-primary" style="margin-top: 10px;">💾 Enregistrer les chemins</button>
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

function renderSettingsIntegrations(panel) {
  panel.innerHTML = `
    <h3>🔌 Gestion des chemins Prepare / Fiery / contrôleurs</h3>
    <div style="background: #fef9c3; border: 1px solid #fbbf24; border-radius: 8px; padding: 20px; margin-top: 16px;">
      <p style="font-size: 16px; margin: 0;">🚧 <strong>À venir</strong> — Configuration des chemins Prepare, Fiery et contrôleurs</p>
      <p style="color: #6b7280; margin-top: 8px; margin-bottom: 0;">Cette section sera disponible dans une prochaine version.</p>
    </div>
  `;
}

async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>📄 Gestion des imports — Fiche de fabrication</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  try {
    const resp = await fetch("/api/config/fabrication-imports", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>📄 Gestion des imports — Fiche de fabrication</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Configurez les chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.</p>
    <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${cfg.media1Path || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
    <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${cfg.media2Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${cfg.media3Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${cfg.media4Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${cfg.typeDocumentPath || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <button id="fi-save" class="btn btn-primary" style="margin-top: 10px;">💾 Enregistrer les chemins</button>
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

async function renderSettingsLogs(panel) {
  panel.innerHTML = `
    <h3>📋 Logs de l'application</h3>
    <div style="display: flex; gap: 8px; margin-bottom: 16px; align-items: center;">
      <input type="date" id="logs-date-filter" class="settings-input" />
      <button id="logs-refresh" class="btn btn-primary">🔄 Rafraîchir</button>
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
  const url = "/api/admin/logs" + (dateFilter ? `?date=${encodeURIComponent(dateFilter)}` : "");

  try {
    const resp = await fetch(url, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());

    const logs = resp.logs || [];
    if (logs.length === 0) {
      container.innerHTML = '<p style="color:#9ca3af;">Aucun log disponible</p>';
      return;
    }

    container.innerHTML = `
      <table style="width:100%; border-collapse: collapse; font-size: 13px;">
        <thead>
          <tr style="background: #f3f4f6; text-align: left;">
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Date</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Méthode</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">URL</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Statut</th>
          </tr>
        </thead>
        <tbody>
          ${logs.map(l => `
            <tr style="border-bottom: 1px solid #e5e7eb;">
              <td style="padding: 6px 12px; white-space: nowrap; color: #6b7280;">${new Date(l.timestamp).toLocaleString("fr-FR")}</td>
              <td style="padding: 6px 12px;"><span style="background: #dbeafe; color: #1e40af; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 600;">${l.method || ""}</span></td>
              <td style="padding: 6px 12px; font-family: monospace; color: #374151;">${l.path || ""}</td>
              <td style="padding: 6px 12px;"><span style="color: ${(l.statusCode || 200) >= 400 ? '#ef4444' : '#16a34a'}; font-weight: 600;">${l.statusCode || ""}</span></td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    `;
  } catch (err) {
    container.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

async function renderSettingsStats(panel) {
  panel.innerHTML = `<h3>📊 Dashboard — Vue d'ensemble de l'atelier</h3><p style="color:#6b7280;">Chargement...</p>`;
  try {
    const resp = await fetch("/api/admin/stats", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());

    const stats = resp.stats || {};
    const filesByFolder = stats.filesByFolder || {};
    const scheduledThisWeek = stats.scheduledThisWeek || 0;
    const activeAssignments = stats.activeAssignments || 0;
    const totalFiles = stats.totalFiles || 0;

    panel.innerHTML = `
      <h3>📊 Dashboard — Vue d'ensemble de l'atelier</h3>
      <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 24px;">
        <div style="background: #dbeafe; border-radius: 10px; padding: 20px; text-align: center;">
          <div style="font-size: 32px; font-weight: 700; color: #1e40af;">${totalFiles}</div>
          <div style="color: #3b82f6; font-weight: 500; margin-top: 4px;">📄 Fichiers totaux</div>
        </div>
        <div style="background: #dcfce7; border-radius: 10px; padding: 20px; text-align: center;">
          <div style="font-size: 32px; font-weight: 700; color: #15803d;">${scheduledThisWeek}</div>
          <div style="color: #16a34a; font-weight: 500; margin-top: 4px;">📅 Planifiés cette semaine</div>
        </div>
        <div style="background: #fef9c3; border-radius: 10px; padding: 20px; text-align: center;">
          <div style="font-size: 32px; font-weight: 700; color: #a16207;">${activeAssignments}</div>
          <div style="color: #ca8a04; font-weight: 500; margin-top: 4px;">👤 Affectations actives</div>
        </div>
      </div>
      <h4>Fichiers par étape</h4>
      <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px;">
        ${Object.entries(filesByFolder).map(([folder, count]) => `
          <div style="background: white; border: 1px solid #e5e7eb; border-radius: 8px; padding: 12px 16px; display: flex; justify-content: space-between; align-items: center;">
            <span style="font-size: 13px; color: #374151;">${folder}</span>
            <span style="background: #f3f4f6; border-radius: 999px; padding: 2px 10px; font-weight: 700; color: #111827;">${count}</span>
          </div>
        `).join("")}
      </div>
    `;
  } catch (err) {
    panel.innerHTML = `<h3>📊 Dashboard</h3><p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

async function showAccountsModal() {
  const modal = document.getElementById("accounts-modal");
  const usersList = document.getElementById("users-list");
  const btnCreate = document.getElementById("btn-create-user");
  const closeBtn = document.getElementById("accounts-close");

  const resp = await fetch("/api/auth/users", {
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json());

  usersList.innerHTML = "";
  if (resp.ok && resp.users) {
    resp.users.forEach(u => {
      const div = document.createElement("div");
      div.className = "user-item";
      div.innerHTML = `
        <div class="user-item-info"><strong>${u.login}</strong><small>${u.name}</small></div>
        <div class="user-item-profile">Profil ${u.profile}</div>
        <button class="btn-delete" data-id="${u.id}">Supprimer</button>
      `;
      div.querySelector(".btn-delete").onclick = async () => {
        if (!confirm("Supprimer ?")) return;
        await fetch(`/api/auth/users/${u.id}`, {
          method: "DELETE",
          headers: { "Authorization": `Bearer ${authToken}` }
        });
        showAccountsModal();
      };
      usersList.appendChild(div);
    });
  }

  btnCreate.onclick = async () => {
    const login = document.getElementById("new-login").value.trim();
    const password = document.getElementById("new-password").value.trim();
    const name = document.getElementById("new-name").value.trim();
    const profile = parseInt(document.getElementById("new-profile").value);

    if (!login || !password || !name) {
      alert("Remplissez tous les champs");
      return;
    }

    const r = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ login, password, name, profile })
    }).then(r => r.json());

    if (r.ok) {
      alert("Utilisateur créé !");
      document.getElementById("new-login").value = "";
      document.getElementById("new-password").value = "";
      document.getElementById("new-name").value = "";
      showAccountsModal();
    } else {
      alert("Erreur : " + r.error);
    }
  };

  closeBtn.onclick = () => modal.classList.add("hidden");
  modal.classList.remove("hidden");
}

async function initApp() {
  setupProfileUI();

  try {
    await loadDeliveries();
    await loadAssignments();
    updateGlobalAlert();
    await buildKanban();
    ensureCalendar();

    if (currentUser.profile === 1) {
      showProduction();
    } else {
      showKanban();
    }
  } catch (err) {
    console.error("Erreur init:", err);
  }
}

document.addEventListener("DOMContentLoaded", initLogin);

// ======================================================
// UTILITAIRES & REST
// ======================================================

const FIN_PROD_FOLDER = "8. Fin de production".replace(/\u00A0/g, " ");
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
      if (a && a.fullPath) assignmentsByPath[normalizePath(a.fullPath)] = a;
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

function ensureCalendar() {
  if (!calendar) initCalendar();
}

let fabCurrentPath = null;
const fabModal = document.getElementById("fab-modal");
const fabClose = document.getElementById("fab-close");
const fabSave = document.getElementById("fab-save");
const fabPdf = document.getElementById("fab-pdf");
const fabFinProd = document.getElementById("fab-finprod");
const fabMoteur = document.getElementById("fab-moteur");
const fabOperateur = document.getElementById("fab-operateur");
const fabQuantite = document.getElementById("fab-quantite");
const fabType = document.getElementById("fab-type");
const fabFormat = document.getElementById("fab-format");
const fabPapier = document.getElementById("fab-papier");
const fabRectoVerso = document.getElementById("fab-rectoverso");
const fabEncres = document.getElementById("fab-encres");
const fabClient = document.getElementById("fab-client");
const fabNumero = document.getElementById("fab-numero");
const fabNotes = document.getElementById("fab-notes");
const fabDelai = document.getElementById("fab-delai");
const fabFaconnage = document.getElementById("fab-faconnage");
const fabLivraison = document.getElementById("fab-livraison");
const fabMedia1 = document.getElementById("fab-media1");
const fabMedia2 = document.getElementById("fab-media2");
const fabMedia3 = document.getElementById("fab-media3");
const fabMedia4 = document.getElementById("fab-media4");
const fabTypeDoc = document.getElementById("fab-typedoc");
const fabNbFeuilles = document.getElementById("fab-nbfeuilles");
const fabHistory = document.getElementById("fab-history");
const fabRemove = document.getElementById("fab-delivery-remove");

fabClose.onclick = () => fabModal.classList.add("hidden");
document.addEventListener("keydown", e => { if (e.key === "Escape") fabModal.classList.add("hidden"); });

async function openFabrication(fullPath) {
  fabCurrentPath = normalizePath(fullPath);

  const r = await fetch("/api/fabrication?fullPath=" + encodeURIComponent(fabCurrentPath));
  const j = await r.json();
  const d = j.ok === false ? {} : j;

  // Load print engines into dropdown
  try {
    const engines = await fetch("/api/config/print-engines").then(r => r.json()).catch(() => []);
    fabMoteur.innerHTML = '<option value="">— Sélectionner —</option>';
    engines.forEach(e => {
      const opt = document.createElement("option");
      opt.value = e;
      opt.textContent = e;
      fabMoteur.appendChild(opt);
    });
  } catch(err) { console.warn("Erreur print-engines:", err); }

  fabMoteur.value = d.moteurImpression || d.machine || "";
  fabOperateur.value = d.operateur || "";
  fabQuantite.value = d.quantite || "";
  fabType.value = d.typeTravail || "";
  fabFormat.value = d.format || "";
  fabPapier.value = d.papier || "";
  fabRectoVerso.value = d.rectoVerso || "";
  fabEncres.value = d.encres || "";
  fabClient.value = d.client || "";
  fabNumero.value = d.numeroAffaire || "";
  fabNotes.value = d.notes || "";
  fabFaconnage.value = d.faconnage || "";
  fabLivraison.value = d.livraison || "";
  fabMedia1.value = d.media1 || "";
  fabMedia2.value = d.media2 || "";
  fabMedia3.value = d.media3 || "";
  fabMedia4.value = d.media4 || "";
  fabTypeDoc.value = d.typeDocument || "";
  fabNbFeuilles.value = d.nombreFeuilles || "";

  // Pre-fill Délai from delivery if available
  const deliveryDate = deliveriesByPath[fabCurrentPath];
  if (d.delai) {
    fabDelai.value = new Date(d.delai).toISOString().split("T")[0];
  } else if (deliveryDate) {
    fabDelai.value = deliveryDate;
  } else {
    fabDelai.value = "";
  }

  // Show/hide admin-only fields
  document.querySelectorAll(".fab-admin-field").forEach(el => {
    el.style.display = currentUser.profile === 3 ? "" : "none";
  });

  fabHistory.innerHTML = "";
  (d.history || []).forEach(h => {
    const div = document.createElement("div");
    div.textContent = `${new Date(h.date).toLocaleDateString("fr-FR", {day:"2-digit",month:"2-digit",year:"numeric",hour:"2-digit",minute:"2-digit"})} — ${h.user} — ${h.action}`;
    fabHistory.appendChild(div);
  });

  fabRemove.onclick = async () => {
    if (!fabCurrentPath) return;
    if (!confirm("Retirer du planning ?")) return;

    const resp = await fetch("/api/delivery?fullPath=" + encodeURIComponent(fabCurrentPath), { method: "DELETE" }).then(r => r.json()).catch(() => ({ ok: false }));
    if (!resp.ok) { alert("Erreur"); return; }

    delete deliveriesByPath[fabCurrentPath];
    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();
    await refreshKanban();
    updateGlobalAlert();
    alert("Retiré du planning");
  };

  fabModal.classList.remove("hidden");
}

fabSave.onclick = async () => {
  if (!fabCurrentPath) return;

  const payload = {
    fullPath: fabCurrentPath,
    fileName: fabCurrentPath.split("\\").pop(),
    moteurImpression: fabMoteur.value,
    machine: fabMoteur.value,
    quantite: parseInt(fabQuantite.value) || null,
    typeTravail: fabType.value,
    format: fabFormat.value,
    papier: fabPapier.value,
    rectoVerso: fabRectoVerso.value,
    encres: fabEncres.value,
    client: fabClient.value,
    numeroAffaire: fabNumero.value,
    notes: fabNotes.value,
    faconnage: fabFaconnage.value,
    livraison: fabLivraison.value,
    delai: fabDelai.value || null,
    media1: fabMedia1.value || null,
    media2: fabMedia2.value || null,
    media3: fabMedia3.value || null,
    media4: fabMedia4.value || null,
    typeDocument: fabTypeDoc.value || null,
    nombreFeuilles: parseInt(fabNbFeuilles.value) || null
  };

  const r = await fetch("/api/fabrication", {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
    body: JSON.stringify(payload)
  }).then(r => r.json());

  if (!r.ok) {
    alert("Erreur : " + r.error);
    return;
  }

  fabModal.classList.add("hidden");
  showNotification("✅ Fiche enregistrée", "success");
};

fabPdf.onclick = () => {
  if (!fabCurrentPath) return;
  window.open("/api/fabrication/pdf?fullPath=" + encodeURIComponent(fabCurrentPath), "_blank", "noopener");
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

  const dst = normalizePath(moveResp.moved || "");
  const oldIso = deliveriesByPath[fabCurrentPath];

  if (oldIso) {
    const oldTime = deliveriesByPath[fabCurrentPath + "_time"] || "09:00";
    await fetch("/api/delivery?fullPath=" + encodeURIComponent(fabCurrentPath), { method: "DELETE" });
    await new Promise(resolve => setTimeout(resolve, 100));

    const r = await fetch("/api/delivery", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath: dst, date: oldIso, time: oldTime })
    }).then(r => r.json()).catch(() => ({ ok: false }));

    if (r.ok) {
      delete deliveriesByPath[fabCurrentPath];
      delete deliveriesByPath[fabCurrentPath + "_time"];
      deliveriesByPath[dst] = oldIso;
      deliveriesByPath[dst + "_time"] = oldTime;
      if (calendar) calendar.refetchEvents();
      if (submissionCalendar) submissionCalendar.refetchEvents();
    }
  }

  updateGlobalAlert();
  await refreshKanban();
  await refreshSubmissionView();
  fabModal.classList.add("hidden");
  alert("Fin de production marquée");
};

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

    if (deliveriesByPath[fullPath]) delete deliveriesByPath[fullPath];
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
      body: JSON.stringify({ fullPath, date: dateVal, time: timeVal })
    }).then(r => r.json());

    if (!r.ok) {
      alert("Erreur");
      return;
    }

    deliveriesByPath[fullPath] = dateVal;
    deliveriesByPath[fullPath + "_time"] = timeVal;

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
  // Ligne 1
  { folder: "1.Reception", label: "Début de production", color: "#5fa8c4" },
  { folder: "2.Corrections", label: "Corrections", color: "#5fa8c4" },
  { folder: "3.Rapport", label: "Rapport", color: "#5fa8c4" },
  { folder: "2.Corrections + fond perdu", label: "Corrections et fond perdu", color: "#5fa8c4" },
  // Ligne 2
  { folder: "6.Archivage", label: "Prêt pour impression", color: "#5fa8c4" },
  { folder: "4.BAT", label: "BAT", color: "#5fa8c4" },
  { folder: "5.Relecture", label: "Impression en cours", color: "#5fa8c4" },
  // Ligne 3
  { folder: "7.Termine", label: "PrismaPrepare", color: "#6b7e89" },
  { folder: "8. Fin de production", label: "Fiery", color: "#6b7e89" },
  { folder: "9. Archived", label: "Fin de production", color: "#22c55e" }
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
    col.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;

    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.textContent = cfg.label;
    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    drop.addEventListener("dragover", e => {
      e.preventDefault();
      drop.classList.add("drag-over");
    });

    drop.addEventListener("dragleave", () => {
      drop.classList.remove("drag-over");
    });

    drop.addEventListener("drop", async (e) => {
      if (e.dataTransfer && e.dataTransfer.files?.length) {
        e.preventDefault();
        drop.classList.remove("drag-over");
        handleDesktopDrop(e, "1.Reception");
        return;
      }

      e.preventDefault();
      drop.classList.remove("drag-over");

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

      const dstFull = normalizePath(moveResp.moved || "");
      const oldIso = deliveriesByPath[srcFull];
      const oldTime = deliveriesByPath[srcFull + "_time"] || "09:00";

      if (oldIso) {
        if (destFolder === "8. Fin de production" || destFolder === "9. Archived") {
          const remove = confirm("Retirer du planning ?");
          if (remove) {
            await fetch("/api/delivery?fullPath=" + encodeURIComponent(srcFull), { method: "DELETE" });
            delete deliveriesByPath[srcFull];
            delete deliveriesByPath[srcFull + "_time"];
          } else {
            await fetch("/api/delivery?fullPath=" + encodeURIComponent(srcFull), { method: "DELETE" });
            const put = await fetch("/api/delivery", {
              method: "PUT",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: dstFull, date: oldIso, time: oldTime })
            }).then(r => r.json()).catch(() => ({ ok: false }));

            if (put.ok) {
              delete deliveriesByPath[srcFull];
              delete deliveriesByPath[srcFull + "_time"];
              deliveriesByPath[dstFull] = oldIso;
              deliveriesByPath[dstFull + "_time"] = oldTime;
            }
          }
        } else {
          await fetch("/api/delivery?fullPath=" + encodeURIComponent(srcFull), { method: "DELETE" });
          const put = await fetch("/api/delivery", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: dstFull, date: oldIso, time: oldTime })
          }).then(r => r.json()).catch(() => ({ ok: false }));

          if (put.ok) {
            delete deliveriesByPath[srcFull];
            delete deliveriesByPath[srcFull + "_time"];
            deliveriesByPath[dstFull] = oldIso;
            deliveriesByPath[dstFull + "_time"] = oldTime;
          }
        }
      }

      await loadDeliveries();
      updateGlobalAlert();
      await refreshKanban();
      await refreshSubmissionView();
      calendar?.refetchEvents();
      submissionCalendar?.refetchEvents();
    });

    kanbanDiv.appendChild(col);
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
}

async function refreshKanbanColumnOperator(folderName, q, sort, col) {
  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(folderName)}`)
      .then(r => r.json())
      .catch(() => []);

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

    // Ajoute bouton Acrobat au niveau de la tuile si c'est Corrections
if (folderName === "2.Corrections" || folderName === "2.Corrections + fond perdu") {
  const acrobatBtn = document.createElement("button");
  acrobatBtn.className = "btn btn-acrobat";
  acrobatBtn.textContent = "📄 Ouvrir dans Acrobat Pro";
  acrobatBtn.onclick = async () => {
  try {
    const resp = await fetch("/api/acrobat", { method: "POST" });
    if (!resp.ok) throw new Error("Erreur au lancement d'Acrobat");
    alert("Acrobat Pro est en cours de lancement...");
  } catch (err) {
    alert("❌ " + err.message);
  }
};
  drop.appendChild(acrobatBtn);
}

    for (const job of filtered) {
      const card = document.createElement("div");
      card.className = "kanban-card-operator";
      card.draggable = true;
      card.dataset.fullPath = normalizePath(job.fullPath || "");

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      card.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      card.appendChild(sub);

      const full = normalizePath(job.fullPath || "");

      // Assignment badge
      const assignment = assignmentsByPath[full];
      if (assignment) {
        const badge = document.createElement("div");
        badge.className = "assignment-badge";
        badge.textContent = `👤 ${assignment.operatorName}`;
        card.appendChild(badge);
      }

      const iso = deliveriesByPath[full];
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
        card.appendChild(status);
      }

      // Actions
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

      // "Affecter à" button (visible for profiles 2 and 3)
      if (currentUser.profile === 2 || currentUser.profile === 3) {
        const btnAssign = document.createElement("button");
        btnAssign.className = "btn btn-sm btn-assign";
        btnAssign.textContent = "Affecter à";
        btnAssign.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssign, full); };
        actions.appendChild(btnAssign);

        const btnDelete = document.createElement("button");
        btnDelete.className = "btn btn-sm";
        btnDelete.textContent = "Corbeille";
        btnDelete.onclick = () => deleteFile(full);
        actions.appendChild(btnDelete);
      }

      card.appendChild(actions);

      card.addEventListener("dragstart", (e) => {
        e.dataTransfer.effectAllowed = "move";
        e.dataTransfer.setData("text/plain", card.dataset.fullPath);
      });

      drop.appendChild(card);
    }
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
      const r = await fetch("/api/assignment", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ fullPath, operatorId: op.id })
      }).then(r => r.json()).catch(() => ({ ok: false }));

      if (r.ok) {
        assignmentsByPath[normalizePath(fullPath)] = { fullPath, operatorName: r.operatorName || op.name, operatorId: op.id };
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

function initCalendar() {
  if (calendar || !calendarEl || !window.FullCalendar) return;

  calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
    locale: "fr",
    height: "auto",
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
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fullPath] = newDate;
        deliveriesByPath[fullPath + "_time"] = newTime;

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
  toast.style.cssText = `
    position: fixed;
    bottom: 20px;
    right: 20px;
    padding: 16px 20px;
    border-radius: 8px;
    font-weight: 500;
    z-index: 10000;
    animation: slideIn 0.3s ease;
    ${type === "success" ? "background: #dcfce7; color: #15803d;" : ""}
    ${type === "error" ? "background: #fee2e2; color: #991b1b;" : ""}
    ${type === "info" ? "background: #dbeafe; color: #1e40af;" : ""}
  `;
  toast.textContent = message;
  document.body.appendChild(toast);

  setTimeout(() => {
    toast.style.animation = "slideOut 0.3s ease";
    setTimeout(() => toast.remove(), 300);
  }, 3000);
}

async function loadDeliveries() {
  try {
    const list = await fetch("/api/delivery").then(r => r.json()).catch(() => []);
    const newDeliveries = {};
    
    list.forEach(x => {
      const normalized = normalizePath(x.fullPath);
      newDeliveries[normalized] = x.date;
      newDeliveries[normalized + "_time"] = x.time || "09:00";
    });
    
    Object.assign(deliveriesByPath, newDeliveries);
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