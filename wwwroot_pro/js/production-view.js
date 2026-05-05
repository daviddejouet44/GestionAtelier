// production-view.js — Vue production globale (lecture seule, tous profils)
import { authToken, isLight, darkenColor, fnKey, normalizePath, deliveriesByPath, assignmentsByPath, daysDiffFromToday, fmtBytes, showNotification } from './core.js';
import { STAGE_PROGRESS, getStageProgress, STAGE_DISPLAY_LABELS, getStageLabelDisplay, getStageColor } from './constants.js';

// ======================================================
// VUE PRODUCTION GLOBALE (profils 1, 2 et 3)
// Affiche : numéro de dossier, étape actuelle, pourcentage d'avancement
// ======================================================

export function showGlobalProduction() {
  // Navigation handled by app.js
  initGlobalProductionView();
}

export async function initGlobalProductionView() {
  const el = document.getElementById("global-production");
  if (!el) return;
  el.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement...</div>';
  el.style.cssText = "padding: 20px; width: 100%; box-sizing: border-box; overflow-y: auto;";

  try {
    await buildGlobalProgressView(el);
  } catch(err) {
    el.innerHTML = `<div style="padding:20px;color:#dc2626;">Erreur : ${err.message}</div>`;
  }
}

async function buildGlobalProgressView(container) {
  const [jobs, assignments, deliveries] = await Promise.all([
    fetch("/api/production/summary", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []),
    fetch("/api/assignments", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []),
    fetch("/api/delivery", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => [])
  ]);

  if (!Array.isArray(jobs) || jobs.length === 0) {
    container.innerHTML = '<div style="padding:40px;text-align:center;color:#9ca3af;">Aucun fichier en production actuellement.</div>';
    return;
  }

  // Build assignment lookup by fileName key
  const assignMap = {};
  if (Array.isArray(assignments)) {
    assignments.forEach(a => {
      const key = (a.fileName || (a.fullPath || "").split(/[/\\]/).pop() || "").toLowerCase();
      if (key) assignMap[key] = a.operatorName || "";
    });
  }

  // Build delivery date lookup
  const deliveryMap = {};
  if (Array.isArray(deliveries)) {
    deliveries.forEach(d => {
      const key = (d.fileName || "").toLowerCase();
      if (key) deliveryMap[key] = d.deliveryDate || d.date || "";
    });
  }

  container.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;flex-wrap:wrap;gap:10px;">
      <h2 style="margin:0;font-size:20px;font-weight:700;color:#111827;">Vue production globale</h2>
      <div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;">
        <label style="font-size:13px;color:#6b7280;font-weight:500;">Trier par :</label>
        <select id="global-prod-sort" style="padding:7px 12px;border:1.5px solid #e5e7eb;border-radius:8px;font-size:13px;background:white;color:#111827;cursor:pointer;outline:none;">
          <option value="stage">Étape actuelle</option>
          <option value="dossier">Numéro de dossier</option>
          <option value="progress">Avancement</option>
          <option value="delivery">Date de livraison</option>
          <option value="operator">Opérateur</option>
        </select>
        <button id="global-prod-refresh" class="btn btn-primary btn-sm">Rafraîchir</button>
      </div>
    </div>
    <div id="global-prod-table-wrap"></div>
  `;

  container.querySelector("#global-prod-refresh").onclick = () => buildGlobalProgressView(container);
  const sortSel = container.querySelector("#global-prod-sort");
  sortSel.onchange = () => renderGlobalProdTable(jobs, assignMap, deliveryMap, sortSel.value, container.querySelector("#global-prod-table-wrap"));

  renderGlobalProdTable(jobs, assignMap, deliveryMap, "stage", container.querySelector("#global-prod-table-wrap"));
}

function renderGlobalProdTable(jobs, assignMap, deliveryMap, sortMode, wrap) {
  // Sort jobs based on mode
  const sorted = [...jobs].sort((a, b) => {
    switch(sortMode) {
      case "dossier": {
        const na = (a.numeroDossier || a.fileName || "").toLowerCase();
        const nb = (b.numeroDossier || b.fileName || "").toLowerCase();
        return na.localeCompare(nb);
      }
      case "progress": {
        return getStageProgress(a.currentStage) - getStageProgress(b.currentStage);
      }
      case "delivery": {
        const da = deliveryMap[(a.fileName || "").toLowerCase()] || "9999";
        const db = deliveryMap[(b.fileName || "").toLowerCase()] || "9999";
        return da.localeCompare(db);
      }
      case "operator": {
        const oa = (assignMap[(a.fileName || "").toLowerCase()] || "zzz").toLowerCase();
        const ob = (assignMap[(b.fileName || "").toLowerCase()] || "zzz").toLowerCase();
        return oa.localeCompare(ob);
      }
      default: { // stage
        return getStageProgress(a.currentStage) - getStageProgress(b.currentStage);
      }
    }
  });

  const table = document.createElement("div");
  table.style.cssText = "width:100%;";

  const header = document.createElement("div");
  header.style.cssText = "display:grid;grid-template-columns:1fr 1.5fr 1fr 1fr 1fr 2fr;gap:8px;padding:10px 16px;background:#f3f4f6;border-radius:8px;font-size:12px;font-weight:700;color:#6b7280;text-transform:uppercase;letter-spacing:0.05em;margin-bottom:8px;";
  header.innerHTML = `
    <span>Numéro dossier</span>
    <span>Fichier</span>
    <span>Étape actuelle</span>
    <span>Affecté à</span>
    <span>Date livraison</span>
    <span>Avancement</span>
  `;
  table.appendChild(header);

  for (const job of sorted) {
    const progress = getStageProgress(job.currentStage);
    const color = getStageColor(progress);
    const displayNum = job.numeroDossier || job.fileName || "—";
    const fileKey = (job.fileName || "").toLowerCase();
    const operatorName = assignMap[fileKey] || "";
    const deliveryDate = deliveryMap[fileKey] || "";

    // Build stage display label, including BAT sub-status when available (centralized in constants.js)
    const stageDisplay = getStageLabelDisplay(job.currentStage, job.batStatus) || '—';
    let stageBadgeStyle = "background:#dbeafe;color:#1e40af;"; // default blue
    if (job.currentStage === 'BAT') {
      if (job.batStatus === 'refuse') {
        stageBadgeStyle = "background:#fee2e2;color:#991b1b;";
      } else if (job.batStatus === 'valide') {
        stageBadgeStyle = "background:#dcfce7;color:#166534;";
      } else if (job.batStatus === 'envoye') {
        stageBadgeStyle = "background:#fef9c3;color:#92400e;";
      }
    }

    const row = document.createElement("div");
    row.style.cssText = "display:grid;grid-template-columns:1fr 1.5fr 1fr 1fr 1fr 2fr;gap:8px;padding:12px 16px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;align-items:center;";

    const deliveryDisplay = deliveryDate
      ? `<span style="font-size:12px;color:#374151;">${deliveryDate}</span>`
      : `<span style="font-size:12px;color:#9ca3af;font-style:italic;">—</span>`;

    row.innerHTML = `
      <div style="font-size:14px;font-weight:700;color:#111827;font-family:monospace;">${escapeHtml(displayNum)}</div>
      <div style="font-size:12px;color:#6b7280;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="${escapeHtml(job.fileName || '')}">${escapeHtml(job.fileName || '—')}</div>
      <div>
        <span style="${stageBadgeStyle}padding:3px 8px;border-radius:12px;font-size:11px;font-weight:600;white-space:nowrap;">${escapeHtml(stageDisplay)}</span>
      </div>
      <div style="font-size:12px;${operatorName ? 'color:#111827;font-weight:500;' : 'color:#9ca3af;font-style:italic;'}">${escapeHtml(operatorName || 'Non assigné')}</div>
      <div>${deliveryDisplay}</div>
      <div style="display:flex;align-items:center;gap:10px;">
        <div style="flex:1;background:#e5e7eb;border-radius:99px;height:10px;overflow:hidden;">
          <div style="width:${progress}%;background:${color};height:100%;border-radius:99px;transition:width 0.3s;"></div>
        </div>
        <span style="font-size:12px;font-weight:700;color:${color};min-width:32px;">${progress}%</span>
      </div>
    `;
    table.appendChild(row);
  }

  wrap.innerHTML = "";
  wrap.appendChild(table);
}

function escapeHtml(str) {
  return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}


// ======================================================
// VUE PRODUCTION KANBAN (profil 2/3 — tuiles opérateur)
// ======================================================

const PRODUCTION_FOLDER_CONFIG = [
  { folder: "Début de production", label: "Jobs à traiter", color: "#5fa8c4" },
  { folder: "Corrections", label: "Preflight", color: "#e0e0e0" },
  { folder: "Corrections et fond perdu", label: "Preflight avec fond perdu", color: "#e0e0e0" },
  { folder: "Prêt pour impression", label: "En attente", color: "#b8b8b8" },
  { folder: "BAT", label: "BAT", color: "#8b5cf6" },
  { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f" },
  { folder: "Fiery", label: "Fiery", color: "#8f8f8f" },
  { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a" },
  { folder: "Façonnage", label: "Finitions", color: "#666666" },
  { folder: "Fin de production", label: "Fin de production", color: "#22c55e" }
];

export function showProduction() {
  // Navigation handled by app.js
  buildProductionView();
}

export function buildProductionView() {
  const productionEl = document.getElementById("production");
  productionEl.innerHTML = "";
  productionEl.style.cssText = "padding: 20px; width: 100%; box-sizing: border-box; overflow-y: auto;";

  // Filter bar (ITEM 20)
  const filterBar = document.createElement("div");
  filterBar.style.cssText = "display:flex;gap:12px;flex-wrap:wrap;align-items:center;margin-bottom:16px;padding:12px 16px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;";

  const machineLabel = document.createElement("label");
  machineLabel.style.cssText = "font-size:13px;font-weight:600;color:#374151;white-space:nowrap;";
  machineLabel.textContent = "Machine :";
  filterBar.appendChild(machineLabel);

  const machineSelect = document.createElement("select");
  machineSelect.id = "prod-machine-filter";
  machineSelect.className = "settings-input";
  machineSelect.style.cssText = "padding:5px 10px;font-size:13px;min-width:150px;";
  machineSelect.innerHTML = '<option value="">Toutes</option>';
  filterBar.appendChild(machineSelect);

  const opLabel = document.createElement("label");
  opLabel.style.cssText = "font-size:13px;font-weight:600;color:#374151;white-space:nowrap;margin-left:8px;";
  opLabel.textContent = "Opérateur :";
  filterBar.appendChild(opLabel);

  const opSelect = document.createElement("select");
  opSelect.id = "prod-operator-filter";
  opSelect.className = "settings-input";
  opSelect.style.cssText = "padding:5px 10px;font-size:13px;min-width:150px;";
  opSelect.innerHTML = '<option value="">Tous</option>';
  filterBar.appendChild(opSelect);

  const btnReset = document.createElement("button");
  btnReset.className = "btn btn-sm";
  btnReset.textContent = "Réinitialiser";
  btnReset.onclick = () => {
    machineSelect.value = "";
    opSelect.value = "";
    applyProductionFilters();
  };
  filterBar.appendChild(btnReset);

  productionEl.appendChild(filterBar);

  // Load filter options async
  fetch("/api/config/print-engines").then(r => r.json()).then(data => {
    const engines = Array.isArray(data) ? data : (Array.isArray(data.engines) ? data.engines : []);
    engines.forEach(e => {
      const opt = document.createElement("option");
      opt.value = e.name || e;
      opt.textContent = e.name || e;
      machineSelect.appendChild(opt);
    });
  }).catch(() => {});

  fetch("/api/settings/users").then(r => r.json()).then(data => {
    const users = Array.isArray(data) ? data : (Array.isArray(data.users) ? data.users : []);
    users.forEach(u => {
      const opt = document.createElement("option");
      opt.value = u.name || u.login || u;
      opt.textContent = u.name || u.login || u;
      opSelect.appendChild(opt);
    });
  }).catch(() => {});

  machineSelect.onchange = applyProductionFilters;
  opSelect.onchange = applyProductionFilters;

  // Kanban grid
  const kanbanGrid = document.createElement("div");
  kanbanGrid.id = "prod-kanban-grid";
  kanbanGrid.style.cssText = "display:grid;grid-template-columns:repeat(3,1fr);gap:20px;";
  productionEl.appendChild(kanbanGrid);

  for (const cfg of PRODUCTION_FOLDER_CONFIG) {
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

    kanbanGrid.appendChild(col);
  }

  refreshProductionViewKanban().catch(err => console.error("Erreur production kanban:", err));
}

function applyProductionFilters() {
  const machineVal = (document.getElementById("prod-machine-filter")?.value || "").toLowerCase();
  const opVal = (document.getElementById("prod-operator-filter")?.value || "").toLowerCase();
  const grid = document.getElementById("prod-kanban-grid");
  if (!grid) return;
  grid.querySelectorAll(".kanban-card-operator").forEach(card => {
    const cardMachine = (card.dataset.machine || "").toLowerCase();
    const cardOp = (card.dataset.operator || "").toLowerCase();
    const matchMachine = !machineVal || cardMachine === machineVal;
    const matchOp = !opVal || cardOp === opVal;
    card.style.display = (matchMachine && matchOp) ? "" : "none";
  });
}

export async function refreshProductionViewKanban() {
  const productionEl = document.getElementById("production");
  if (!productionEl || productionEl.classList.contains("hidden")) return;
  const cols = productionEl.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnReadOnly(col.dataset.folder, col);
  }
}

export async function buildProductionKanban(container) {
  container.innerHTML = "";

  for (const cfg of PRODUCTION_FOLDER_CONFIG) {
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

export async function refreshProductionKanban(container) {
  const cols = container.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnReadOnly(col.dataset.folder, col);
  }
}

export async function refreshKanbanColumnReadOnly(folderName, col) {
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
        renderPdfThumbnailRO(normalizePath(job.fullPath || ""), thumb).catch(() => {});
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

      // ITEM 16 — Status badge container (loaded async)
      const statusBadgeEl = document.createElement("div");
      statusBadgeEl.style.cssText = "margin-top:4px;min-height:20px;";
      card.appendChild(statusBadgeEl);

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
      btnFiche.onclick = () => { if (window._openFabrication) window._openFabrication(full); };
      actions.appendChild(btnFiche);

      // ITEM 16 — Validate / Refuse buttons
      const btnValider = document.createElement("button");
      btnValider.className = "btn btn-sm";
      btnValider.style.cssText = "background:#dcfce7;color:#166534;border-color:#86efac;";
      btnValider.textContent = "✅ Validé";
      btnValider.onclick = async (e) => {
        e.stopPropagation();
        const r = await fetch("/api/fabrication/statut-production", {
          method: "PUT",
          headers: { "Content-Type": "application/json", "Authorization": "Bearer " + (authToken || "") },
          body: JSON.stringify({ fileName: jobFileName, statut: "valide" })
        }).then(res => res.json()).catch(() => ({ ok: false }));
        if (r.ok) {
          renderProductionStatusBadge(statusBadgeEl, "valide");
          showNotification("✅ Statut enregistré", "success");
        } else {
          showNotification("❌ Erreur : " + (r.error || ""), "error");
        }
      };
      actions.appendChild(btnValider);

      const btnRefuser = document.createElement("button");
      btnRefuser.className = "btn btn-sm";
      btnRefuser.style.cssText = "background:#fee2e2;color:#991b1b;border-color:#fca5a5;";
      btnRefuser.textContent = "❌ Refusé";
      btnRefuser.onclick = async (e) => {
        e.stopPropagation();
        const r = await fetch("/api/fabrication/statut-production", {
          method: "PUT",
          headers: { "Content-Type": "application/json", "Authorization": "Bearer " + (authToken || "") },
          body: JSON.stringify({ fileName: jobFileName, statut: "refuse" })
        }).then(res => res.json()).catch(() => ({ ok: false }));
        if (r.ok) {
          renderProductionStatusBadge(statusBadgeEl, "refuse");
          showNotification("✅ Statut enregistré", "success");
        } else {
          showNotification("❌ Erreur : " + (r.error || ""), "error");
        }
      };
      actions.appendChild(btnRefuser);

      card.appendChild(actions);

      // Load fab data async to populate machine/operator for filters + show status badge
      fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName))
        .then(r => r.json())
        .then(fab => {
          if (!fab) return;
          if (fab.moteurImpression) card.dataset.machine = fab.moteurImpression;
          if (fab.operateur) card.dataset.operator = fab.operateur;
          // Only show BAT status badge while the file is still in a BAT-relevant folder.
          // Once the PDF moves to another folder (e.g. Finitions), the column title conveys
          // the current stage — the stale BAT badge must not override it.
          const BAT_BADGE_FOLDERS = new Set(["BAT", "Prêt pour impression"]);
          if (fab.statutProduction && BAT_BADGE_FOLDERS.has(folderName)) {
            renderProductionStatusBadge(statusBadgeEl, fab.statutProduction);
          }
        }).catch(() => {});

      drop.appendChild(card);
    }

    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = jobs.length;
  } catch (err) {
    console.error("Erreur refresh kanban read-only:", err);
  }
}

function renderProductionStatusBadge(container, statut) {
  container.innerHTML = "";
  if (statut === "valide") {
    container.innerHTML = '<span style="background:#dcfce7;color:#166534;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:700;">✅ VALIDÉ</span>';
  } else if (statut === "refuse") {
    container.innerHTML = '<span style="background:#fee2e2;color:#991b1b;padding:2px 8px;border-radius:4px;font-size:11px;font-weight:700;">❌ REFUSÉ</span>';
  }
}

async function renderPdfThumbnailRO(fullPath, container) {
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
// AUTO-REFRESH pour la vue production (toutes les 30s)
// ======================================================
let _productionRefreshInterval = null;

export function startProductionAutoRefresh() {
  stopProductionAutoRefresh();
  _productionRefreshInterval = setInterval(async () => {
    const prodEl = document.getElementById("production");
    if (prodEl && !prodEl.classList.contains("hidden")) {
      await refreshProductionViewKanban();
    }
    const globalProdEl = document.getElementById("global-production");
    if (globalProdEl && !globalProdEl.classList.contains("hidden")) {
      await initGlobalProductionView();
    }
  }, 30000);
}

export function stopProductionAutoRefresh() {
  if (_productionRefreshInterval) {
    clearInterval(_productionRefreshInterval);
    _productionRefreshInterval = null;
  }
}

