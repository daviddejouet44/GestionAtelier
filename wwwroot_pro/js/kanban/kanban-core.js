// kanban/kanban-core.js — Build, refresh, summary
import { currentUser, deliveriesByPath, fnKey, normalizePath, isLight, darkenColor, showNotification, FOLDER_FIN_PRODUCTION } from '../core.js';
import { refreshKanbanColumnOperator } from './kanban-cards.js';
import { showFaconnageAlerts } from './kanban-actions.js';

const kanbanDiv = document.getElementById("kanban");
const searchInput = document.getElementById("searchInput");
const sortBy = document.getElementById("sortBy");

// Shared mutable state (imported by kanban-cards.js)
export const state = {
  dateFilter: "",        // was _kanbanDateFilter
  operatorFilter: "",    // was _kanbanOperatorFilter
  preflightColumnsHidden: false,  // was _preflightColumnsHidden
  columnCache: {},       // was _columnCache
  visibleActionsMap: {}  // folder → string[] | null (null = show all)
};

// Default kanban columns (used as fallback if API fails)
const DEFAULT_KANBAN_COLUMNS = [
  { folder: "Début de production", label: "Jobs à traiter", color: "#5fa8c4", visible: true, order: 0 },
  { folder: "Corrections", label: "Preflight", color: "#e0e0e0", visible: true, order: 1 },
  { folder: "Corrections et fond perdu", label: "Preflight avec fond perdu", color: "#e0e0e0", visible: true, order: 2 },
  { folder: "Prêt pour impression", label: "En attente", color: "#b8b8b8", visible: true, order: 3 },
  { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f", visible: true, order: 4 },
  { folder: "Fiery", label: "Fiery", color: "#8f8f8f", visible: true, order: 5 },
  { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a", visible: true, order: 6 },
  { folder: "Façonnage", label: "Finitions", color: "#666666", visible: true, order: 7 },
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
  state.preflightColumnsHidden = (correctionsCol?.visible === false) && (correctionsFpCol?.visible === false);

  // Build visible actions map: folder → string[] | null
  state.visibleActionsMap = {};
  for (const c of allColumns) {
    if (Array.isArray(c.visibleActions)) {
      // null means "show all" (retrocompat) — an array (even empty) restricts to listed actions
      state.visibleActionsMap[c.folder] = c.visibleActions;
    } else {
      state.visibleActionsMap[c.folder] = null; // null = show all (retrocompat)
    }
  }

  kanbanDiv.innerHTML = "";
  kanbanDiv.style.gridTemplateColumns = "repeat(3, 1fr)";
  kanbanDiv.style.gap = "20px";
  kanbanDiv.style.padding = "20px";
  if (window._updateGlobalAlert) window._updateGlobalAlert();

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    if (cfg.folderPath) col.dataset.folderPath = cfg.folderPath;
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
      if (window._refreshOperatorView) window._refreshOperatorView();
    });

    kanbanDiv.appendChild(col);
  }

  // Summary bar — now static in HTML, just reference it
  const summaryEl = document.getElementById("kanban-summary");
  if (summaryEl) summaryEl.style.display = "none"; // hidden until updateKanbanSummary populates it

  // Filter bar (date + operator) — uses static #kanban-filter-bar from HTML
  buildKanbanFilterBar();

  // Clear column cache so new settings (visibleActions, etc.) take effect immediately
  state.columnCache = {};

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
  dateInput.value = state.dateFilter;
  dateInput.onchange = () => {
    state.dateFilter = dateInput.value;
    Object.keys(state.columnCache).forEach(k => delete state.columnCache[k]);
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
    state.dateFilter = today;
    Object.keys(state.columnCache).forEach(k => delete state.columnCache[k]);
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
    opSelect.value = state.operatorFilter || "all";
  }).catch(() => {});

  opSelect.value = state.operatorFilter || "all";
  opSelect.onchange = () => {
    state.operatorFilter = opSelect.value;
    Object.keys(state.columnCache).forEach(k => delete state.columnCache[k]);
    updateFilterIndicator();
    refreshKanban();
  };
  filterBar.appendChild(opSelect);

  // Reset all filters button
  const btnReset = document.createElement("button");
  btnReset.id = "kanban-filter-reset";
  btnReset.className = "btn btn-sm";
  btnReset.textContent = "Réinitialiser";
  btnReset.style.display = (state.dateFilter || (state.operatorFilter && state.operatorFilter !== "all")) ? "inline-block" : "none";
  btnReset.onclick = () => {
    state.dateFilter = "";
    state.operatorFilter = "all";
    dateInput.value = "";
    opSelect.value = "all";
    Object.keys(state.columnCache).forEach(k => delete state.columnCache[k]);
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
  if (state.dateFilter) {
    parts.push(`Jour : ${new Date(state.dateFilter + "T00:00:00").toLocaleDateString("fr-FR")}`);
  }
  if (state.operatorFilter && state.operatorFilter !== "all") {
    parts.push(state.operatorFilter === "mine" ? "Mes jobs" : "Opérateur sélectionné");
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
    await refreshKanbanColumnOperator(col.dataset.folder, q, sort, col, readOnly, col.dataset.folderPath || null);
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
    const resp = await fetch("/api/urgences").then(r => r.json()).catch(() => ({ ok: false, groups: [] }));
    const groups = (resp.ok && Array.isArray(resp.groups)) ? resp.groups : [];

    // Filter only groups with at least one non-terminated urgent job
    const activeGroups = groups.filter(g => g.jobs && g.jobs.some(j => !j.termine));

    if (activeGroups.length === 0) {
      summaryEl.style.display = "none";
      return;
    }

    const urgClasses = { 0: "urgent-j0", 1: "urgent-j1", 2: "urgent-j2", 3: "urgent-j3" };

    const columnsHtml = activeGroups.map(g => {
      const jobsHtml = g.jobs.map(job => {
        const cls = urgClasses[job.diff] || "urgent-j3";
        const livraisonFr = job.dateLivraison
          ? new Date(job.dateLivraison + "T00:00:00").toLocaleDateString("fr-FR") : "—";
        const machineFr = job.datePlanningMachine
          ? new Date(job.datePlanningMachine + "T00:00:00").toLocaleDateString("fr-FR") : null;
        const nameStyle = job.termine ? "text-decoration:line-through;color:#9ca3af;" : "";
        const label = job.diff === 0 ? "Aujourd'hui" : `J+${job.diff}`;
        return `
          <div class="urgence-job ${cls}" style="border-left:4px solid;">
            <div style="display:flex;justify-content:space-between;align-items:center;gap:4px;">
              <strong style="font-size:11px;color:#111827;">${job.numeroDossier || '—'}</strong>
              <span class="urgent-badge ${cls}" style="font-size:10px;padding:1px 6px;">${label}</span>
            </div>
            <div style="font-size:11px;${nameStyle}white-space:nowrap;overflow:hidden;text-overflow:ellipsis;" title="${job.fileName}">${job.fileName}</div>
            ${machineFr ? `<div style="font-size:10px;color:#6b7280;">Machine: ${machineFr}</div>` : ''}
            <div style="font-size:10px;color:#6b7280;">Livraison: ${livraisonFr}</div>
          </div>`;
      }).join('');
      return `
        <div class="urgence-column">
          <div class="urgence-column-title">${g.moteur}</div>
          ${jobsHtml}
        </div>`;
    }).join('');

    summaryEl.innerHTML = `
      <div class="urgence-header">🚨 <strong>Urgences</strong></div>
      <div class="urgence-columns">${columnsHtml}</div>
    `;
    summaryEl.style.display = "";
  } catch(e) { console.error("Erreur summary:", e); }
}
