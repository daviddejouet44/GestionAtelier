// production-view.js — Vue production globale (lecture seule, tous profils)
import { authToken, isLight, darkenColor, fnKey, normalizePath, deliveriesByPath, assignmentsByPath, daysDiffFromToday, fmtBytes, showNotification } from './core.js';

// ======================================================
// VUE PRODUCTION GLOBALE (profils 1, 2 et 3)
// ======================================================

export function showGlobalProduction() {
  // Navigation handled by app.js
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

export async function initGlobalProductionView() {
  const el = document.getElementById("global-production");
  if (!el) return;
  el.innerHTML = '<div style="padding:20px;color:#6b7280;">Chargement...</div>';
  // Set grid layout for the kanban columns
  el.style.cssText = "display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; padding: 20px; width: 100%;";

  try {
    await buildProductionKanban(el);
  } catch(err) {
    el.innerHTML = `<div style="padding:20px;color:#dc2626;">Erreur : ${err.message}</div>`;
  }
}

// ======================================================
// VUE PRODUCTION KANBAN (profil 1 et tous profils)
// ======================================================

const PRODUCTION_FOLDER_CONFIG = [
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

export function showProduction() {
  // Navigation handled by app.js
  buildProductionView();
}

export function buildProductionView() {
  const productionEl = document.getElementById("production");
  productionEl.innerHTML = "";
  productionEl.style.cssText = "display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; padding: 20px; width: 100%;";

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

    productionEl.appendChild(col);
  }

  refreshProductionViewKanban().catch(err => console.error("Erreur production kanban:", err));
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

      card.appendChild(actions);
      drop.appendChild(card);
    }

    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = jobs.length;
  } catch (err) {
    console.error("Erreur refresh kanban read-only:", err);
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
      await refreshProductionKanban(globalProdEl);
    }
  }, 30000);
}

export function stopProductionAutoRefresh() {
  if (_productionRefreshInterval) {
    clearInterval(_productionRefreshInterval);
    _productionRefreshInterval = null;
  }
}
