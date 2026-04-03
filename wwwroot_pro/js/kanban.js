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

// ======================================================
// BUILD KANBAN
// ======================================================
export async function buildKanban() {
  const folderConfig = [
    { folder: "Début de production", label: "Jobs à traiter", color: "#5fa8c4" },
    { folder: "Corrections", label: "Preflight", color: "#e0e0e0" },
    { folder: "Corrections et fond perdu", label: "Preflight avec fond perdu", color: "#e0e0e0" },
    { folder: "Prêt pour impression", label: "En attente", color: "#b8b8b8" },
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
  if (window._updateGlobalAlert) window._updateGlobalAlert();

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

  // Summary bar
  let summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) {
    summaryEl = document.createElement("div");
    summaryEl.id = "kanban-summary";
    summaryEl.className = "kanban-summary";
    kanbanDiv.parentNode?.insertBefore(summaryEl, kanbanDiv);
  }

  // Filter bar (date + operator) — inserted after summary, before kanban
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
    filterBar = document.createElement("div");
    filterBar.id = "kanban-filter-bar";
    filterBar.style.cssText = "display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:8px 20px;background:#f9fafb;border-bottom:1px solid #e5e7eb;font-size:13px;";
    // Insert after summary bar, before kanban
    const summaryEl = document.getElementById("kanban-summary");
    if (summaryEl && summaryEl.nextSibling) {
      summaryEl.parentNode?.insertBefore(filterBar, summaryEl.nextSibling);
    } else {
      kanbanDiv.parentNode?.insertBefore(filterBar, kanbanDiv);
    }
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
    await refreshKanbanColumnOperator(col.dataset.folder, q, sort, col);
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
    const folders = ["Début de production","Corrections","Corrections et fond perdu","Prêt pour impression","PrismaPrepare","Fiery","Impression en cours","Façonnage","Fin de production"];
    const counts = {};
    for (const f of folders) {
      const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(f)}`).then(r => r.json()).catch(() => []);
      counts[f] = Array.isArray(jobs) ? jobs.length : 0;
    }

    const labelMap = {
      "Début de production": "Jobs à traiter",
      "Corrections": "Preflight",
      "Corrections et fond perdu": "Preflight fp",
      "Prêt pour impression": "En attente",
      "PrismaPrepare": "Prisma",
      "Fiery": "Fiery",
      "Impression en cours": "Impression",
      "Façonnage": "Façonnage",
      "Fin de production": "Fin prod"
    };

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
    { label: "Envoyer en impression", action: "send-to-print" },
    { label: "Ouvrir dans PrismaPrepare", action: "open-prisma" },
    { label: "Impression directe", action: "direct-print" }
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

  if (action === "open-prisma") {
    // Open in PrismaPrepare using configured executable/URL
    try {
      const r = await fetch("/api/jobs/open-in-prismaprepare", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ fileName, fullPath })
      }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
      if (r.ok) showNotification("✅ Ouverture dans PrismaPrepare lancée", "success");
      else showNotification("❌ " + (r.error || "Erreur"), "error");
    } catch(e) { showNotification("❌ " + e.message, "error"); }
    return;
  }

  try {
    const r = await fetch("/api/jobs/send-to-print", {
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
          // Match against current user login or name
          return asgn.operatorId === (currentUser?.id || currentUser?.login || "")
            || asgn.operatorName === (currentUser?.name || currentUser?.login || "");
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

      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        if (window._renderPdfThumbnail) window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
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
        if (daysLeft <= 1) status.classList.add("urgent");
        else if (daysLeft <= 3) status.classList.add("warning");
        status.textContent = new Date(iso).toLocaleDateString("fr-FR");
        textDiv.appendChild(status);
      }

      layout.appendChild(textDiv);
      card.appendChild(layout);

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

      if (folderName === "Prêt pour impression") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

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
