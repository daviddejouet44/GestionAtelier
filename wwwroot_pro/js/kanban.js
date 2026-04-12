// kanban.js — Tableau Kanban complet
import { authToken, currentUser, deliveriesByPath, assignmentsByPath, fnKey, normalizePath, isLight, darkenColor, fmtBytes, daysDiffFromToday, formatDateTime, showNotification, FOLDER_FIN_PRODUCTION } from './core.js';
import { openBatChoiceModal } from './bat.js';

const kanbanDiv = document.getElementById("kanban");
const searchInput = document.getElementById("searchInput");
const sortBy = document.getElementById("sortBy");
const _columnCache = {};

// Kanban filter state
let _kanbanDateFilter = ""; // ISO date string "YYYY-MM-DD" or ""
let _kanbanOperatorFilter = ""; // "all", "mine", or operatorId string

// Module-level variable: true when both Corrections columns are hidden
let _preflightColumnsHidden = false;

// Default kanban columns (used as fallback if API fails)
const DEFAULT_KANBAN_COLUMNS = [
  { folder: "Début de production", label: "Jobs à traiter", color: "#5fa8c4", visible: true, order: 0 },
  { folder: "Corrections", label: "Preflight", color: "#e0e0e0", visible: true, order: 1 },
  { folder: "Corrections et fond perdu", label: "Preflight avec fond perdu", color: "#e0e0e0", visible: true, order: 2 },
  { folder: "Prêt pour impression", label: "En attente", color: "#b8b8b8", visible: true, order: 3 },
  { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f", visible: true, order: 4 },
  { folder: "Fiery", label: "Fiery", color: "#8f8f8f", visible: true, order: 5 },
  { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a", visible: true, order: 6 },
  { folder: "Façonnage", label: "Façonnage", color: "#666666", visible: true, order: 7 },
  { folder: "Fin de production", label: "Fin de production", color: "#22c55e", visible: true, order: 8 }
];

// ======================================================
// BUILD KANBAN
// ======================================================
export async function buildKanban() {
  // Load kanban column config from API (accessible to all profiles)
  let allColumns = DEFAULT_KANBAN_COLUMNS;
  try {
    const resp = await fetch("/api/config/kanban-columns").then(r => r.json()).catch(() => null);
    if (resp && resp.ok && Array.isArray(resp.columns) && resp.columns.length > 0) {
      allColumns = resp.columns;
    }
  } catch(e) { /* use defaults */ }

  // Filter visible columns and sort by order
  const folderConfig = allColumns
    .filter(c => c.visible !== false)
    .sort((a, b) => (a.order ?? 0) - (b.order ?? 0));

  // Determine if preflight columns are hidden
  const correctionsCol = allColumns.find(c => c.folder === "Corrections");
  const correctionsFpCol = allColumns.find(c => c.folder === "Corrections et fond perdu");
  _preflightColumnsHidden = (correctionsCol?.visible === false) && (correctionsFpCol?.visible === false);

  kanbanDiv.innerHTML = "";
  kanbanDiv.style.gridTemplateColumns = "repeat(3, 1fr)";
  kanbanDiv.style.gap = "20px";
  kanbanDiv.style.padding = "20px";
  if (window._updateGlobalAlert) window._updateGlobalAlert();

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;
    title.style.color = isLight(cfg.color) ? '#1D1D1F' : '#FFFFFF';
    const labelSpan = document.createElement("span");
    labelSpan.textContent = cfg.label;
    labelSpan.style.flex = "1";
    title.appendChild(labelSpan);

    // Bouton "Ouvrir dans Acrobat Pro" dans l'en-tête des colonnes Preflight
    if (cfg.folder === "Corrections" || cfg.folder === "Corrections et fond perdu") {
      const btnAcrobat = document.createElement("button");
      btnAcrobat.className = "btn btn-acrobat";
      btnAcrobat.textContent = "Ouvrir dans Acrobat Pro";
      btnAcrobat.style.cssText = "font-size:10px;padding:2px 7px;flex-shrink:0;margin-right:6px;";
      btnAcrobat.title = "Lancer Adobe Acrobat Pro (sans ouvrir de fichier)";
      btnAcrobat.onclick = async (e) => {
        e.stopPropagation();
        try {
          const resp = await fetch("/api/acrobat", { method: "POST" });
          const data = await resp.json().catch(() => ({}));
          if (!data.ok) showNotification("❌ " + (data.error || "Erreur lancement Acrobat"), "error");
        } catch (err) {
          showNotification("❌ " + err.message, "error");
        }
      };
      title.appendChild(btnAcrobat);
    }

    // Bouton "Productions à venir" dans l'en-tête Façonnage
    if (cfg.folder === "Façonnage") {
      const btnProdsVenir = document.createElement("button");
      btnProdsVenir.className = "btn";
      btnProdsVenir.textContent = "📋 Productions à venir";
      btnProdsVenir.style.cssText = "font-size:10px;padding:2px 7px;flex-shrink:0;margin-right:6px;background:rgba(255,255,255,0.2);border:1px solid rgba(255,255,255,0.4);color:inherit;";
      btnProdsVenir.onclick = async (e) => {
        e.stopPropagation();
        await showFaconnageAlerts();
      };
      title.appendChild(btnProdsVenir);
    }

    const counter = document.createElement("span");
    counter.className = "kanban-col-counter";
    counter.textContent = "0";
    title.appendChild(counter);

    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    if (cfg.folder === "BAT") {
      // BAT column removed from kanban (now a separate view)
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
        if (window._handleDesktopDrop) window._handleDesktopDrop(e, cfg.folder);
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

      if (window._loadDeliveries) await window._loadDeliveries();
      if (window._loadAssignments) await window._loadAssignments();
      if (window._updateGlobalAlert) window._updateGlobalAlert();
      await refreshKanban();
      if (window._refreshSubmissionView) await window._refreshSubmissionView();
      if (window._calendar) window._calendar.refetchEvents();
      if (window._submissionCalendar) window._submissionCalendar.refetchEvents();
    });

    kanbanDiv.appendChild(col);
  }

  // Summary bar — now static in HTML, just reference it
  const summaryEl = document.getElementById("kanban-summary");
  if (summaryEl) summaryEl.style.display = "none"; // hidden until updateKanbanSummary populates it

  // Filter bar (date + operator) — uses static #kanban-filter-bar from HTML
  buildKanbanFilterBar();

  await refreshKanban();

  if (searchInput) searchInput.oninput = () => refreshKanban();
  if (sortBy) sortBy.onchange = () => refreshKanban();
}

// ======================================================
// FILTER BAR
// ======================================================
function buildKanbanFilterBar() {
  let filterBar = document.getElementById("kanban-filter-bar");
  if (!filterBar) {
    // Fallback: create inline if static placeholder not found
    filterBar = document.createElement("div");
    filterBar.id = "kanban-filter-bar";
    filterBar.style.cssText = "display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:8px 20px;background:#f9fafb;border-bottom:1px solid #e5e7eb;font-size:13px;";
    kanbanDiv.parentNode?.insertBefore(filterBar, kanbanDiv);

  }
  filterBar.innerHTML = "";

  // Date filter
  const dateLabel = document.createElement("label");
  dateLabel.style.cssText = "font-weight:600;color:#374151;white-space:nowrap;";
  dateLabel.textContent = "Filtrer par jour :";
  filterBar.appendChild(dateLabel);

  const dateInput = document.createElement("input");
  dateInput.type = "date";
  dateInput.id = "kanban-date-filter";
  dateInput.className = "settings-input";
  dateInput.style.cssText = "padding:4px 8px;font-size:13px;";
  dateInput.value = _kanbanDateFilter;
  dateInput.onchange = () => {
    _kanbanDateFilter = dateInput.value;
    Object.keys(_columnCache).forEach(k => delete _columnCache[k]);
    updateFilterIndicator();
    refreshKanban();
  };
  filterBar.appendChild(dateInput);

  const btnToday = document.createElement("button");
  btnToday.className = "btn btn-sm";
  btnToday.textContent = "Aujourd'hui";
  btnToday.onclick = () => {
    const today = new Date().toISOString().slice(0, 10);
    dateInput.value = today;
    _kanbanDateFilter = today;
    Object.keys(_columnCache).forEach(k => delete _columnCache[k]);
    updateFilterIndicator();
    refreshKanban();
  };
  filterBar.appendChild(btnToday);

  // Separator
  const sep = document.createElement("span");
  sep.style.cssText = "color:#d1d5db;margin:0 4px;";
  sep.textContent = "|";
  filterBar.appendChild(sep);

  // Operator filter
  const opLabel = document.createElement("label");
  opLabel.style.cssText = "font-weight:600;color:#374151;white-space:nowrap;";
  opLabel.textContent = "Opérateur :";
  filterBar.appendChild(opLabel);

  const opSelect = document.createElement("select");
  opSelect.id = "kanban-operator-filter";
  opSelect.className = "settings-input";
  opSelect.style.cssText = "padding:4px 8px;font-size:13px;min-width:140px;";

  const optAll = document.createElement("option");
  optAll.value = "all";
  optAll.textContent = "Tous";
  opSelect.appendChild(optAll);

  const optMine = document.createElement("option");
  optMine.value = "mine";
  optMine.textContent = "Mes jobs";
  opSelect.appendChild(optMine);

  // Load operators for admin/operator
  fetch("/api/operators").then(r => r.json()).then(resp => {
    const operators = resp.operators || [];
    operators.forEach(op => {
      const opt = document.createElement("option");
      opt.value = op.id;
      opt.textContent = op.name;
      opSelect.appendChild(opt);
    });
    opSelect.value = _kanbanOperatorFilter || "all";
  }).catch(() => {});

  opSelect.value = _kanbanOperatorFilter || "all";
  opSelect.onchange = () => {
    _kanbanOperatorFilter = opSelect.value;
    Object.keys(_columnCache).forEach(k => delete _columnCache[k]);
    updateFilterIndicator();
    refreshKanban();
  };
  filterBar.appendChild(opSelect);

  // Reset all filters button
  const btnReset = document.createElement("button");
  btnReset.id = "kanban-filter-reset";
  btnReset.className = "btn btn-sm";
  btnReset.textContent = "Réinitialiser";
  btnReset.style.display = (_kanbanDateFilter || (_kanbanOperatorFilter && _kanbanOperatorFilter !== "all")) ? "inline-block" : "none";
  btnReset.onclick = () => {
    _kanbanDateFilter = "";
    _kanbanOperatorFilter = "all";
    dateInput.value = "";
    opSelect.value = "all";
    Object.keys(_columnCache).forEach(k => delete _columnCache[k]);
    updateFilterIndicator();
    refreshKanban();
  };
  filterBar.appendChild(btnReset);

  // Filter indicator
  const indicator = document.createElement("span");
  indicator.id = "kanban-filter-indicator";
  indicator.style.cssText = "font-size:12px;color:#6b7280;margin-left:4px;";
  filterBar.appendChild(indicator);

  updateFilterIndicator();
}

function updateFilterIndicator() {
  const indicator = document.getElementById("kanban-filter-indicator");
  const resetBtn = document.getElementById("kanban-filter-reset");
  if (!indicator) return;

  const parts = [];
  if (_kanbanDateFilter) {
    parts.push(`Jour : ${new Date(_kanbanDateFilter + "T00:00:00").toLocaleDateString("fr-FR")}`);
  }
  if (_kanbanOperatorFilter && _kanbanOperatorFilter !== "all") {
    parts.push(_kanbanOperatorFilter === "mine" ? "Mes jobs" : "Opérateur sélectionné");
  }

  if (parts.length > 0) {
    indicator.textContent = "Filtré : " + parts.join(" · ");
    if (resetBtn) resetBtn.style.display = "inline-block";
  } else {
    indicator.textContent = "";
    if (resetBtn) resetBtn.style.display = "none";
  }
}

// ======================================================
// REFRESH KANBAN
// ======================================================
export async function refreshKanban() {
  const q = (searchInput?.value || "").trim().toLowerCase();
  const sort = (sortBy?.value || "date_desc");

  const cols = kanbanDiv.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    // Profile 1 (Production lecture seule) and Profile 4 (Façonnage): read-only for all but Façonnage
    const readOnly = currentUser?.profile === 1 ||
                     (currentUser?.profile === 4 && col.dataset.folder !== "Façonnage");
    await refreshKanbanColumnOperator(col.dataset.folder, q, sort, col, readOnly);
  }
  await updateKanbanSummary();

  fetch("/api/jobs/cleanup-corrections", { method: "POST" }).catch(() => {});
}

// ======================================================
// SUMMARY BAR
// ======================================================
export async function updateKanbanSummary() {
  const summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) return;

  try {
    // Load dynamic config (use defaults if API fails)
    let allColumns = DEFAULT_KANBAN_COLUMNS;
    try {
      const resp = await fetch("/api/config/kanban-columns").then(r => r.json()).catch(() => null);
      if (resp && resp.ok && Array.isArray(resp.columns) && resp.columns.length > 0) {
        allColumns = resp.columns;
      }
    } catch(e) { /* use defaults */ }

    const folders = allColumns.filter(c => c.visible !== false).map(c => c.folder);
    const counts = {};
    for (const f of folders) {
      const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(f)}`).then(r => r.json()).catch(() => []);
      counts[f] = Array.isArray(jobs) ? jobs.length : 0;
    }

    const labelMap = {};
    allColumns.forEach(c => { labelMap[c.folder] = c.label; });

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
    summaryEl.style.display = ""; // show now that content is ready
  } catch(e) { console.error("Erreur summary:", e); }
}

// ======================================================
// DIALOG IMPRESSION — remplacé par openActionsDropdown
// (conservé pour compatibilité)
// ======================================================
export async function openPrintDialog(fullPath) {
  openActionsDropdown(null, fullPath);
}

// ======================================================
// ACTIONS DROPDOWN (En attente)
// ======================================================
export async function openActionsDropdown(btnEl, fullPath) {
  document.querySelectorAll(".actions-dropdown").forEach(d => d.remove());

  const dropdown = document.createElement("div");
  dropdown.className = "actions-dropdown";
  dropdown.style.cssText = `
    position: fixed; background: white; border: 1px solid #e5e7eb;
    border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,0.18);
    z-index: 9999; min-width: 220px; overflow: hidden; padding: 4px 0;
  `;

  const items = [
    { label: "Envoyer vers PrismaSync", action: "prismasync" },
    { label: "Ouvrir dans PrismaPrepare", action: "prisma-prepare" },
    { label: "Impression directe", action: "direct-print" },
    { label: "Envoyer dans Fiery", action: "fiery" }
  ];

  items.forEach(item => {
    const el = document.createElement("div");
    el.style.cssText = "padding: 10px 16px; cursor: pointer; font-size: 13px; color: #111827; transition: background 0.15s; white-space: nowrap;";
    el.textContent = item.label;
    el.onmouseenter = () => el.style.background = "#f3f4f6";
    el.onmouseleave = () => el.style.background = "";
    el.onclick = async () => {
      dropdown.remove();
      await handlePrintAction(item.action, fullPath);
    };
    dropdown.appendChild(el);
  });

  document.body.appendChild(dropdown);

  // Position relative to button if provided, else center
  if (btnEl) {
    const rect = btnEl.getBoundingClientRect();
    const dropW = 220;
    let left = rect.left + window.scrollX;
    if (left + dropW > window.innerWidth) left = window.innerWidth - dropW - 8;
    dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
    dropdown.style.left = left + "px";
  } else {
    dropdown.style.top = "50%";
    dropdown.style.left = "50%";
    dropdown.style.transform = "translate(-50%, -50%)";
  }

  setTimeout(() => {
    document.addEventListener("click", function closeDropdown(e) {
      if (!dropdown.contains(e.target)) {
        dropdown.remove();
        document.removeEventListener("click", closeDropdown);
      }
    });
  }, 10);
}

async function handlePrintAction(action, fullPath) {
  const fileName = fnKey(fullPath);

  try {
    const r = await fetch("/api/jobs/send-to-action", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ fileName, fullPath, action })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification(`✅ ${r.message || "Envoi effectué"}`, "success");
      await refreshKanban();
    } else {
      showNotification("❌ " + (r.error || "Erreur"), "error");
    }
  } catch(e) { showNotification("❌ " + e.message, "error"); }
}

// ======================================================
// COLONNE KANBAN (opérateur)
// ======================================================
export async function refreshKanbanColumnOperator(folderName, q, sort, col, readOnly = false) {
  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(folderName)}`)
      .then(r => r.json())
      .catch(() => []);

    const fingerprint = JSON.stringify(jobs.map(j => {
      const fn = fnKey(j.fullPath || j.name || '');
      return (j.name || '') + '|' + j.modified + '|' + j.size
        + '|' + ((assignmentsByPath[fn] || {}).operatorName || '')
        + '|' + (deliveriesByPath[fn] || '');
    })) + '|' + _kanbanDateFilter + '|' + (_kanbanOperatorFilter || 'all');
    const cacheKey = folderName + '|' + q + '|' + sort;
    if (_columnCache[cacheKey] === fingerprint) return;
    _columnCache[cacheKey] = fingerprint;

    const drop = col.querySelector(".kanban-col-operator__drop");
    drop.innerHTML = "";

    let filtered = jobs;

    // Text search filter
    if (q) {
      filtered = filtered.filter(j => (j.name || "").toLowerCase().includes(q.toLowerCase()));
    }

    // Date filter — only show jobs whose deliveryDate matches selected date
    if (_kanbanDateFilter) {
      filtered = filtered.filter(j => {
        const fn = fnKey(j.fullPath || j.name || '');
        const iso = deliveriesByPath[fn];
        return iso && iso === _kanbanDateFilter;
      });
    }

    // Operator filter
    if (_kanbanOperatorFilter && _kanbanOperatorFilter !== "all") {
      filtered = filtered.filter(j => {
        const fn = fnKey(j.fullPath || j.name || '');
        const asgn = assignmentsByPath[fn];
        if (!asgn) return false;
        if (_kanbanOperatorFilter === "mine") {
          // Only match if current user has a non-empty identity
          const myId = currentUser?.id || "";
          const myLogin = currentUser?.login || "";
          const myName = currentUser?.name || "";
          if (!myId && !myLogin && !myName) return false;
          return (myId && asgn.operatorId === myId)
            || (myLogin && asgn.operatorId === myLogin)
            || (myName && asgn.operatorName === myName)
            || (myLogin && asgn.operatorName === myLogin);
        }
        return asgn.operatorId === _kanbanOperatorFilter;
      });
    }

    if (sort === "name_asc") filtered.sort((a, b) => (a.name || "").localeCompare(b.name || ""));
    else if (sort === "name_desc") filtered.sort((a, b) => (b.name || "").localeCompare(a.name || ""));
    else if (sort === "size_asc") filtered.sort((a, b) => (a.size || 0) - (b.size || 0));
    else if (sort === "size_desc") filtered.sort((a, b) => (b.size || 0) - (a.size || 0));
    else filtered.sort((a, b) => new Date(b.modified) - new Date(a.modified));

    for (const job of filtered) {
      const card = document.createElement("div");
      card.className = "kanban-card-operator";
      if (!readOnly) card.draggable = true;
      card.dataset.fullPath = normalizePath(job.fullPath || "");
      card.dataset.folder = folderName;

      const full = normalizePath(job.fullPath || "");
      const jobFileName = fnKey(full);
      const assignment = assignmentsByPath[jobFileName];
      const iso = deliveriesByPath[jobFileName];

      // Card layout: vignette left, center info, right delivery+operator
      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        if (window._renderPdfThumbnail) window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      // Center: dossier number (big) + PDF name + import date
      const centerDiv = document.createElement("div");
      centerDiv.style.cssText = "flex: 1; min-width: 0;";

      const dossierEl = document.createElement("div");
      dossierEl.className = "kanban-card-dossier";
      dossierEl.textContent = "—";
      centerDiv.appendChild(dossierEl);

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      centerDiv.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = new Date(job.modified).toLocaleDateString("fr-FR");
      centerDiv.appendChild(sub);

      layout.appendChild(centerDiv);

      // Right: delivery date + operator
      if (iso || assignment) {
        const rightDiv = document.createElement("div");
        rightDiv.className = "kanban-card-operator-right";

        if (iso) {
          const deliveryEl = document.createElement("div");
          deliveryEl.className = "kanban-card-operator-status";
          const daysLeft = daysDiffFromToday(iso);
          if (daysLeft <= 1) deliveryEl.classList.add("urgent");
          else if (daysLeft <= 3) deliveryEl.classList.add("warning");
          deliveryEl.textContent = new Date(iso).toLocaleDateString("fr-FR");
          rightDiv.appendChild(deliveryEl);
        }

        if (assignment) {
          const badge = document.createElement("div");
          badge.className = "assignment-badge";
          badge.textContent = assignment.operatorName;
          rightDiv.appendChild(badge);
        }

        layout.appendChild(rightDiv);
      }

      card.appendChild(layout);

      // Load dossier number asynchronously
      fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName))
        .then(r => r.json()).then(d => {
          if (d && d.numeroDossier) dossierEl.textContent = d.numeroDossier;
        }).catch(() => {});

      const actions = document.createElement("div");
      actions.className = "kanban-card-operator-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => { if (window._openFabrication) window._openFabrication(full); };

      const btnAssign = document.createElement("button");
      btnAssign.className = "btn btn-sm btn-assign";
      btnAssign.textContent = "Affecter à";
      btnAssign.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssign, full); };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-sm";
      btnDelete.textContent = "Corbeille";
      btnDelete.onclick = () => { if (window._deleteFile) window._deleteFile(full); };

      if (folderName === "Début de production") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        const btnPlan = document.createElement("button");
        btnPlan.className = "btn btn-sm";
        btnPlan.textContent = "📅 Planifier";
        btnPlan.onclick = () => { if (window._openPlanificationCalendar) window._openPlanificationCalendar(full); };
        actions.appendChild(btnPlan);

        // Bouton Preflight conditionnel — visible uniquement si les tuiles Preflight sont masquées
        if (_preflightColumnsHidden) {
          const btnPreflightDirect = document.createElement("button");
          btnPreflightDirect.className = "btn btn-sm btn-primary";
          btnPreflightDirect.textContent = "▶ Preflight ▾";
          btnPreflightDirect.title = "Lancer le Preflight avec le droplet de votre choix";
          btnPreflightDirect.onclick = async (e) => {
            e.stopPropagation();
            document.querySelectorAll(".preflight-direct-dropdown").forEach(d => d.remove());

            // Fetch available droplets
            let droplets = [];
            try {
              const dr = await fetch("/api/config/preflight/droplets").then(r => r.json()).catch(() => null);
              if (dr && dr.ok && Array.isArray(dr.droplets)) droplets = dr.droplets;
            } catch(ex) { /* ignore */ }

            if (droplets.length === 0) {
              showNotification("❌ Aucun droplet configuré. Configurez-les dans Paramétrage > Preflight.", "error");
              return;
            }

            const dropdown = document.createElement("div");
            dropdown.className = "preflight-direct-dropdown";
            dropdown.style.cssText = "position:fixed;background:white;border:1px solid #e5e7eb;border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,0.18);z-index:9999;min-width:200px;overflow:hidden;padding:4px 0;";

            droplets.forEach(dp => {
              const item = document.createElement("div");
              item.style.cssText = "padding:10px 16px;cursor:pointer;font-size:13px;color:#111827;transition:background 0.15s;white-space:nowrap;";
              item.textContent = dp.name || dp.path;
              item.onmouseenter = () => item.style.background = "#f3f4f6";
              item.onmouseleave = () => item.style.background = "";
              item.onclick = async () => {
                dropdown.remove();
                const fileName = full.split(/[\\/]/).pop();
                btnPreflightDirect.disabled = true;
                btnPreflightDirect.textContent = "⏳ Preflight...";
                showNotification(`⏳ Preflight en cours pour ${fileName}...`, "info");
                try {
                  const r = await fetch("/api/acrobat/preflight", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ fullPath: full, dropletPath: dp.path })
                  }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
                  if (r.ok) {
                    showNotification(`✅ Preflight terminé — ${fileName} déplacé vers Prêt pour impression`, "success");
                    await refreshKanban();
                  } else {
                    showNotification("❌ Preflight : " + (r.error || "Erreur inconnue"), "error");
                    btnPreflightDirect.disabled = false;
                    btnPreflightDirect.textContent = "▶ Preflight ▾";
                  }
                } catch (err) {
                  showNotification("❌ Preflight : " + err.message, "error");
                  btnPreflightDirect.disabled = false;
                  btnPreflightDirect.textContent = "▶ Preflight ▾";
                }
              };
              dropdown.appendChild(item);
            });

            document.body.appendChild(dropdown);
            const rect = btnPreflightDirect.getBoundingClientRect();
            const dropW = 200;
            let left = rect.left + window.scrollX;
            if (left + dropW > window.innerWidth) left = window.innerWidth - dropW - 8;
            dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
            dropdown.style.left = left + "px";

            setTimeout(() => {
              document.addEventListener("click", function closePfDropdown(ev) {
                if (!dropdown.contains(ev.target)) {
                  dropdown.remove();
                  document.removeEventListener("click", closePfDropdown);
                }
              });
            }, 10);
          };
          actions.appendChild(btnPreflightDirect);
        }

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Prêt pour impression") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        // Planning button — opens the planning dialog
        const btnPlan = document.createElement("button");
        btnPlan.className = "btn btn-sm";
        btnPlan.textContent = "📅 Planifier";
        btnPlan.onclick = () => { if (window._openPlanificationCalendar) window._openPlanificationCalendar(full); };
        actions.appendChild(btnPlan);

        // BAT button — ouvre popup BAT complet / BAT simple
        const btnBAT = document.createElement("button");
        btnBAT.className = "btn btn-sm btn-primary";
        btnBAT.innerHTML = "→ BAT";
        btnBAT.onclick = () => {
          openBatChoiceModal(full, async () => {
            await refreshKanban();
          });
        };
        actions.appendChild(btnBAT);

        const btnPrint = document.createElement("button");
        btnPrint.className = "btn btn-sm btn-primary";
        btnPrint.innerHTML = "Actions ▾";
        btnPrint.onclick = (e) => { e.stopPropagation(); openActionsDropdown(btnPrint, full); };
        actions.appendChild(btnPrint);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Corrections" || folderName === "Corrections et fond perdu") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        // Bouton Preflight automatique
        const btnPreflight = document.createElement("button");
        btnPreflight.className = "btn btn-sm btn-primary";
        btnPreflight.textContent = "▶ Preflight";
        btnPreflight.title = "Lancer le Preflight en arrière-plan et déplacer vers Prêt pour impression";
        btnPreflight.onclick = async (e) => {
          e.stopPropagation();
          const fileName = full.split(/[\\/]/).pop();
          btnPreflight.disabled = true;
          btnPreflight.textContent = "⏳ Preflight...";
          showNotification(`⏳ Preflight en cours pour ${fileName}...`, "info");
          try {
            const r = await fetch("/api/acrobat/preflight", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full, folder: folderName })
            }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
            if (r.ok) {
              showNotification(`✅ Preflight terminé — ${fileName} déplacé vers Prêt pour impression`, "success");
              await refreshKanban();
            } else {
              showNotification("❌ Preflight : " + (r.error || "Erreur inconnue"), "error");
              btnPreflight.disabled = false;
              btnPreflight.textContent = "▶ Preflight";
            }
          } catch (err) {
            showNotification("❌ Preflight : " + err.message, "error");
            btnPreflight.disabled = false;
            btnPreflight.textContent = "▶ Preflight";
          }
        };
        actions.appendChild(btnPreflight);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "PrismaPrepare") {
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        const btnPrisma = document.createElement("button");
        btnPrisma.className = "btn btn-sm btn-primary";
        btnPrisma.textContent = "Ouvrir dans PrismaPrepare";
        btnPrisma.onclick = async (e) => {
          e.stopPropagation();
          const r = await fetch("/api/jobs/open-in-prismaprepare", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: full })
          }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
          if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        actions.appendChild(btnPrisma);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnImpressionLancee = document.createElement("button");
          btnImpressionLancee.className = "btn btn-sm btn-primary";
          btnImpressionLancee.textContent = "▶ Impression lancée";
          btnImpressionLancee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Impression en cours", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Impression en cours", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          actions.appendChild(btnImpressionLancee);
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Fiery") {
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        const btnFiery = document.createElement("button");
        btnFiery.className = "btn btn-sm btn-primary";
        btnFiery.textContent = "Ouvrir dans Fiery";
        btnFiery.onclick = async (e) => {
          e.stopPropagation();
          const r = await fetch("/api/jobs/open-in-fiery", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: full })
          }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
          if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        actions.appendChild(btnFiery);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnLancerImpression = document.createElement("button");
          btnLancerImpression.className = "btn btn-sm btn-primary";
          btnLancerImpression.textContent = "▶ Lancer l'impression";
          btnLancerImpression.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Impression en cours", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Impression lancée", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          actions.appendChild(btnLancerImpression);
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Impression en cours") {
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnImpTerminee = document.createElement("button");
          btnImpTerminee.className = "btn btn-sm btn-primary";
          btnImpTerminee.textContent = "✅ Impression terminée";
          btnImpTerminee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Façonnage", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Façonnage", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          actions.appendChild(btnImpTerminee);
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Façonnage") {
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        // Façonnage badges — loaded asynchronously
        const badgesDiv = document.createElement("div");
        badgesDiv.style.cssText = "display:flex;flex-wrap:wrap;gap:4px;margin-top:4px;";
        card.appendChild(badgesDiv);
        const jfn = jobFileName;
        fetch("/api/fabrication?fileName=" + encodeURIComponent(jfn))
          .then(r => r.json()).then(d => {
            if (Array.isArray(d.faconnage)) {
              d.faconnage.forEach(opt => {
                const badge = document.createElement("span");
                badge.style.cssText = "background:#fef9c3;color:#92400e;border:1px solid #fde68a;border-radius:4px;padding:1px 6px;font-size:10px;font-weight:600;";
                badge.textContent = opt;
                badgesDiv.appendChild(badge);
              });
            }
          }).catch(() => {});

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3 || currentUser.profile === 4)) {
          const btnTerminee = document.createElement("button");
          btnTerminee.className = "btn btn-sm btn-primary";
          btnTerminee.textContent = "✅ Terminée";
          btnTerminee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Fin de production", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Fin de production", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          actions.appendChild(btnTerminee);
        }
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Fin de production") {
        actions.appendChild(btnFiche);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnTermine = document.createElement("button");
          btnTermine.className = "btn btn-sm btn-primary";
          btnTermine.textContent = "🔒 Terminé";
          btnTermine.title = "Verrouille le fichier et marque la tâche comme terminée (vert dans le calendrier)";
          btnTermine.onclick = async (e) => {
            e.stopPropagation();
            if (!confirm("Marquer comme terminé et verrouiller ce fichier ?")) return;
            const r = await fetch("/api/jobs/lock", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) {
              showNotification("✅ Fichier verrouillé — tâche terminée", "success");
              card.draggable = false;
              btnTermine.disabled = true;
              btnTermine.textContent = "🔒 Verrouillé";
              if (window._calendar) window._calendar.refetchEvents();
            } else {
              showNotification("❌ " + (r.error || "Erreur"), "error");
            }
          };
          actions.appendChild(btnTermine);

          const btnArchiver = document.createElement("button");
          btnArchiver.className = "btn btn-sm";
          btnArchiver.textContent = "📦 Archiver";
          btnArchiver.onclick = async (e) => {
            e.stopPropagation();
            if (!confirm("Archiver ce fichier dans le dossier de production ?")) return;
            const r = await fetch("/api/jobs/archive", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Archivé", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          actions.appendChild(btnArchiver);
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

      if (!readOnly) {
        card.addEventListener("dragstart", (e) => {
          e.dataTransfer.effectAllowed = "move";
          e.dataTransfer.setData("text/plain", card.dataset.fullPath);
        });
      }

      drop.appendChild(card);
    }

    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = filtered.length;
  } catch (err) {
    console.error("Erreur refresh kanban operator:", err);
  }
}

// ======================================================
// DROPDOWN AFFECTATION
// ======================================================
export async function openAssignDropdown(btn, fullPath) {
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

  const rect = btn.getBoundingClientRect();
  dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
  dropdown.style.left = (rect.left + window.scrollX) + "px";
  document.body.appendChild(dropdown);

  setTimeout(() => {
    document.addEventListener("click", function closeDropdown() {
      dropdown.remove();
      document.removeEventListener("click", closeDropdown);
    }, { once: true });
  }, 10);
}

// ======================================================
// ALERTES FAÇONNAGE — popup
// ======================================================
export async function showFaconnageAlerts() {
  const data = await fetch("/api/alerts/faconnage").then(r => r.json()).catch(() => ({ ok: false }));

  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000;";

  const panel = document.createElement("div");
  panel.style.cssText = "background:white;border-radius:12px;padding:24px;max-width:680px;width:92%;max-height:85vh;overflow-y:auto;box-shadow:0 10px 40px rgba(0,0,0,.3);";

  let html = `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;">
    <h3 style="margin:0;font-size:18px;font-weight:700;">📋 Productions à venir — Prévision façonnage</h3>
    <button id="fa-close" style="background:none;border:none;font-size:20px;cursor:pointer;color:#6b7280;">✕</button>
  </div>`;

  if (!data.ok || !Array.isArray(data.alerts) || data.alerts.length === 0) {
    html += '<p style="color:#9ca3af;text-align:center;padding:20px;">Aucun job en impression en cours</p>';
  } else {
    // Group by façonnage option
    const grouped = {}; // { optionName: [{fileName, numeroDossier, quantite}] }
    let jobsWithNoFaconnage = [];

    for (const item of data.alerts) {
      const quantite = item.quantite ? parseInt(item.quantite) : null;
      if (Array.isArray(item.faconnage) && item.faconnage.length > 0) {
        for (const opt of item.faconnage) {
          if (!grouped[opt]) grouped[opt] = [];
          grouped[opt].push({ fileName: item.fileName, numeroDossier: item.numeroDossier, quantite, allOptions: item.faconnage });
        }
      } else {
        jobsWithNoFaconnage.push({ fileName: item.fileName, numeroDossier: item.numeroDossier, quantite, allOptions: [] });
      }
    }

    html += `<p style="font-size:13px;color:#6b7280;margin-bottom:16px;">${data.alerts.length} job(s) en impression en cours</p>`;
    html += '<div style="display:flex;flex-direction:column;gap:14px;">';

    const optionNames = Object.keys(grouped).sort();
    for (const opt of optionNames) {
      const jobs = grouped[opt];
      const totalQty = jobs.reduce((s, j) => s + (j.quantite || 0), 0);
      const jobRows = jobs.map(j => {
        const dossier = j.numeroDossier || '—';
        const pdfName = j.fileName || '—';
        const optStr = Array.isArray(j.allOptions) && j.allOptions.length > 0 ? ` — [${j.allOptions.join(', ')}]` : '';
        const qty = j.quantite != null ? ` (${j.quantite.toLocaleString("fr-FR")} ex.)` : "";
        return `<div style="font-size:12px;color:#374151;padding:3px 0 3px 12px;border-left:3px solid #fde68a;"><strong>${dossier}</strong> — ${pdfName}${optStr}${qty}</div>`;
      }).join("");
      const totalLine = totalQty > 0 ? `<div style="font-size:12px;font-weight:700;color:#374151;margin-top:6px;">Total : ${totalQty.toLocaleString("fr-FR")} exemplaires</div>` : "";
      html += `<div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:14px;">
        <div style="font-weight:700;font-size:14px;color:#92400e;margin-bottom:8px;">✂️ ${opt} — ${jobs.length} job(s) à venir</div>
        ${jobRows}
        ${totalLine}
      </div>`;
    }

    if (jobsWithNoFaconnage.length > 0) {
      const jobRows = jobsWithNoFaconnage.map(j => {
        const dossier = j.numeroDossier || '—';
        const pdfName = j.fileName || '—';
        return `<div style="font-size:12px;color:#6b7280;padding:3px 0 3px 12px;border-left:3px solid #e5e7eb;"><strong>${dossier}</strong> — ${pdfName}</div>`;
      }).join("");
      html += `<div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:14px;">
        <div style="font-weight:700;font-size:14px;color:#9ca3af;margin-bottom:8px;">Sans façonnage — ${jobsWithNoFaconnage.length} job(s)</div>
        ${jobRows}
      </div>`;
    }

    html += '</div>';
  }

  panel.innerHTML = html;
  overlay.appendChild(panel);
  document.body.appendChild(overlay);

  panel.querySelector("#fa-close").onclick = () => overlay.remove();
  overlay.addEventListener("click", (e) => { if (e.target === overlay) overlay.remove(); });
}
