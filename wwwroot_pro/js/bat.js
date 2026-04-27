// bat.js — Gestion BAT : popup BAT complet / BAT simple + envoi hotfolder
import { authToken, currentUser, normalizePath, fnKey, showNotification } from './core.js';

// ======================================================
// BAT PROGRESS TRACKER — Suivi temps réel opérateur
// ======================================================

const STEP_LABELS = {
  copying_to_temp:      "📂 Copie vers TEMP_COPY…",
  sent_to_hotfolder:    "📤 Envoyé au hotfolder PrismaPrepare",
  waiting_for_epreuve:  "⏳ En attente de l'épreuve PrismaPrepare…",
  processing_epreuve:   "🔄 Épreuve détectée, traitement…",
  renaming:             "✏️ Renommage en BAT_…",
  moving_to_bat:        "📁 Déplacement vers le dossier BAT…",
  creating_notification:"🔔 Création de la notification…",
  completed:            "✅ Terminé"
};

const POLL_INTERVAL_MS = 2000;
const AUTO_HIDE_DELAY_MS = 30000;

let _bptInterval = null;
let _bptDismissed = false;
let _bptSeenInProgress = false;
let _bptPollCount = 0;

function getBatProgressTracker() {
  return document.getElementById("bat-progress-tracker");
}

function removeBatProgressTracker() {
  const el = getBatProgressTracker();
  if (el) el.remove();
  if (_bptInterval) { clearInterval(_bptInterval); _bptInterval = null; }
}

export function startBatProgressTracker() {
  _bptDismissed = false;
  _bptSeenInProgress = false;
  _bptPollCount = 0;
  // Create tracker element if not already present
  if (!getBatProgressTracker()) {
    const div = document.createElement("div");
    div.id = "bat-progress-tracker";
    div.innerHTML = `
      <div class="bpt-header">
        <span class="bpt-title">
          <span id="bpt-title-text">Traitement BAT en cours</span>
        </span>
        <button class="bpt-close" id="bpt-close-btn" title="Fermer">✕</button>
      </div>
      <div class="bpt-body">
        <div class="bpt-filename" id="bpt-filename">—</div>
        <div class="bpt-progress-wrap" id="bpt-progress-wrap">
          <div class="bpt-progress-bar" id="bpt-progress-bar"></div>
        </div>
        <div class="bpt-step"><span class="bpt-step-label" id="bpt-step-label">Initialisation…</span></div>
        <div class="bpt-elapsed" id="bpt-elapsed"></div>
        <div id="bpt-result"></div>
      </div>
    `;
    document.body.appendChild(div);
    div.querySelector("#bpt-close-btn").onclick = () => {
      _bptDismissed = true;
      removeBatProgressTracker();
    };
  }

  if (_bptInterval) clearInterval(_bptInterval);
  _bptInterval = setInterval(_pollBatProgress, POLL_INTERVAL_MS);
  _pollBatProgress(); // immediate first call
}

// Step → approximate percentage for progress bar
const STEP_PERCENT = {
  copying_to_temp:       25,
  sent_to_hotfolder:     40,
  waiting_for_epreuve:   55,
  processing_epreuve:    70,
  renaming:              82,
  moving_to_bat:         90,
  creating_notification: 96,
  completed:            100
};

async function _pollBatProgress() {
  if (_bptDismissed) { removeBatProgressTracker(); return; }

  let data;
  try {
    data = await fetch("/api/bat/progress", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
  } catch { return; }

  const tracker = getBatProgressTracker();
  if (!tracker) return;

  const fileEl = tracker.querySelector("#bpt-filename");
  const stepEl = tracker.querySelector("#bpt-step-label");
  const elapsedEl = tracker.querySelector("#bpt-elapsed");
  const resultEl = tracker.querySelector("#bpt-result");
  const progressBar = tracker.querySelector("#bpt-progress-bar");
  const titleTextEl = tracker.querySelector("#bpt-title-text");

  if (data.inProgress) {
    _bptSeenInProgress = true;
    // In progress — show current state
    fileEl.textContent = data.currentFileName || "—";
    const stepText = STEP_LABELS[data.currentStep] || data.currentStep || "Traitement…";
    stepEl.textContent = stepText;
    stepEl.className = "bpt-step-label";
    elapsedEl.textContent = data.elapsedSeconds > 0 ? `⏱ ${data.elapsedSeconds}s écoulées` : "";
    resultEl.innerHTML = "";
    if (progressBar) {
      const pct = STEP_PERCENT[data.currentStep] || 10;
      progressBar.style.width = pct + "%";
      progressBar.className = "bpt-progress-bar";
    }
    if (titleTextEl) titleTextEl.textContent = "Traitement BAT en cours";
  } else {
    _bptPollCount++;
    // Not in progress — show last result or hide
    // If we haven't seen inProgress yet, wait a few cycles before giving up
    if (!_bptSeenInProgress && _bptPollCount < 5) return;

    if (data.lastCompletedFileName) {
      // Show completed result
      if (progressBar) { progressBar.style.width = "100%"; progressBar.className = "bpt-progress-bar done"; }
      if (titleTextEl) titleTextEl.textContent = "BAT terminé";
      fileEl.textContent = data.lastCompletedFileName;
      stepEl.textContent = "✅ Terminé";
      stepEl.className = "bpt-step-label done";
      elapsedEl.textContent = "";

      const pl = data.parsedLog;
      if (pl) {
        const isOk = pl.success;
        const warnTxt = pl.warnings > 0 ? ` — ${pl.warnings} avertissement(s)` : "";
        resultEl.innerHTML = `
          <div class="bpt-result ${isOk ? 'success' : 'warning'}">
            ${isOk ? '✅ Succès' : '⚠️ Terminé avec avertissements'}${warnTxt}
            ${pl.startTime && pl.endTime ? `<br><small>Démarré : ${pl.startTime} — Traité : ${pl.endTime}</small>` : ''}
          </div>
          ${data.lastPrismaLog ? `<button class="bpt-log-toggle" id="bpt-log-toggle">Afficher le log PrismaPrepare</button>
          <div class="bpt-log-content" id="bpt-log-content">${escapeHtml(data.lastPrismaLog)}</div>` : ''}
        `;
        const toggleBtn = resultEl.querySelector("#bpt-log-toggle");
        const logDiv = resultEl.querySelector("#bpt-log-content");
        if (toggleBtn && logDiv) {
          toggleBtn.onclick = () => {
            const visible = logDiv.style.display === "block";
            logDiv.style.display = visible ? "none" : "block";
            toggleBtn.textContent = visible ? "Afficher le log PrismaPrepare" : "Masquer le log";
          };
        }
      } else {
        resultEl.innerHTML = '<div class="bpt-result success">✅ Épreuve BAT traitée</div>';
      }

      // Auto-hide after AUTO_HIDE_DELAY_MS
      setTimeout(() => { if (!_bptDismissed) removeBatProgressTracker(); }, AUTO_HIDE_DELAY_MS);
      if (_bptInterval) { clearInterval(_bptInterval); _bptInterval = null; }
    } else {
      // Nothing to show — hide tracker
      removeBatProgressTracker();
    }
  }
}

function escapeHtml(text) {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

// ======================================================
// POPUP CHOIX BAT
// ======================================================

/**
 * Affiche la popup de choix BAT (BAT Complet / BAT Papier / BAT Simple).
 * @param {string} fullPath - Chemin complet du fichier
 * @param {Function} onComplete - Callback appelé après le choix (ex: refreshKanban)
 */
export function openBatChoiceModal(fullPath, onComplete) {
  const fileName = fullPath.split("\\").pop() || fullPath.split("/").pop() || fullPath;

  const overlay = document.createElement("div");
  overlay.className = "bat-choice-overlay";
  overlay.innerHTML = `
    <div class="bat-choice-modal">
      <div class="bat-choice-header">
        <h3>Envoi en BAT</h3>
        <p class="bat-choice-filename">Fichier : <strong>${fileName}</strong></p>
      </div>
      <div class="bat-choice-options">
        <button class="bat-choice-btn bat-complet-btn" id="bat-complet-btn">
          <div class="bat-choice-icon">🖨</div>
          <div class="bat-choice-text">
            <strong>BAT Complet</strong>
            <span>Copie vers TEMP_COPY et le hotfolder PrismaPrepare (selon le type de travail). Le fichier Epreuve.pdf sera automatiquement renommé en BAT_{nom}.pdf et déplacé dans la tuile BAT.</span>
          </div>
        </button>
        <button class="bat-choice-btn bat-papier-btn" id="bat-papier-btn">
          <div class="bat-choice-icon">📄🖨</div>
          <div class="bat-choice-text">
            <strong>BAT Papier</strong>
            <span>Même process que BAT Complet, avec un template email dédié pour le BAT papier.</span>
          </div>
        </button>
        <button class="bat-choice-btn bat-simple-btn" id="bat-simple-btn">
          <div class="bat-choice-icon">📄</div>
          <div class="bat-choice-text">
            <strong>BAT Simple</strong>
            <span>Ouverture du fichier via le droplet configuré par l'administrateur</span>
          </div>
        </button>
      </div>
      <button class="btn bat-choice-cancel" id="bat-cancel-btn">Annuler</button>
    </div>
  `;

  document.body.appendChild(overlay);

  const close = () => overlay.remove();

  overlay.querySelector("#bat-cancel-btn").onclick = close;
  overlay.onclick = (e) => { if (e.target === overlay) close(); };
  document.addEventListener("keydown", function escHandler(e) {
    if (e.key === "Escape") { close(); document.removeEventListener("keydown", escHandler); }
  });

  overlay.querySelector("#bat-complet-btn").onclick = async () => {
    close();
    await sendBatComplet(fullPath);
    if (onComplete) onComplete();
  };

  overlay.querySelector("#bat-papier-btn").onclick = async () => {
    close();
    await sendBatPapier(fullPath);
    if (onComplete) onComplete();
  };

  overlay.querySelector("#bat-simple-btn").onclick = async () => {
    close();
    await sendBatSimple(fullPath);
    if (onComplete) onComplete();
  };
}

// ======================================================
// BAT COMPLET — Copie vers TEMP_COPY + hotfolder PrismaPrepare (sans déplacer le fichier source)
// ======================================================
async function sendBatComplet(fullPath) {
  const path = normalizePath(fullPath);
  const fileName = fnKey(path);

  try {
    // Check serialization status — only one BAT at a time
    const status = await fetch("/api/bat/serialization-status", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (status && status.inProgress) {
      const currentFile = status.currentFileName || "un fichier";
      showNotification(
        `⏳ BAT en cours de génération pour "${currentFile}". Veuillez patienter avant d'en envoyer un nouveau.`,
        "warning"
      );
      return;
    }

    const r = await fetch("/api/bat/copy-for-bat", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ fileName, fullPath: path, requestedBy: currentUser?.login || "" })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification(
        `✅ BAT Complet : copié vers TEMP_COPY et hotfolder${r.hotfolder ? " (" + r.hotfolder + ")" : ""}. En attente de l'épreuve PrismaPrepare...`,
        "success"
      );
      startBatProgressTracker();
    } else if (r.error === "bat_in_progress") {
      const msg = r.message || "Un BAT est déjà en cours de génération. Veuillez patienter.";
      showNotification(`⏳ ${msg}`, "warning");
    } else {
      showNotification("❌ BAT Complet : " + (r.error || "Erreur inconnue"), "error");
    }
  } catch (err) {
    showNotification("❌ BAT Complet : " + err.message, "error");
  }
}

// ======================================================
// BAT SIMPLE — Lance le droplet configuré (sans déplacer le fichier)
// ======================================================
async function sendBatSimple(fullPath) {
  const path = normalizePath(fullPath);

  try {
    const r = await fetch("/api/bat/simple", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath: path })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification("✅ BAT Simple : droplet lancé", "success");
    } else {
      showNotification("❌ BAT Simple : " + (r.error || "Erreur lancement droplet"), "error");
    }
  } catch (err) {
    showNotification("❌ BAT Simple : " + err.message, "error");
  }
}

// ======================================================
// BAT PAPIER — Même process que BAT Complet, template email dédié
// ======================================================
async function sendBatPapier(fullPath) {
  const path = normalizePath(fullPath);
  const fileName = fnKey(path);

  try {
    const status = await fetch("/api/bat/serialization-status", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (status && status.inProgress) {
      const currentFile = status.currentFileName || "un fichier";
      showNotification(
        `⏳ BAT en cours de génération pour "${currentFile}". Veuillez patienter avant d'en envoyer un nouveau.`,
        "warning"
      );
      return;
    }

    const r = await fetch("/api/bat/bat-papier", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ fileName, fullPath: path, requestedBy: currentUser?.login || "" })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification(
        `✅ BAT Papier : copié vers TEMP_COPY et hotfolder${r.hotfolder ? " (" + r.hotfolder + ")" : ""}. En attente de l'épreuve PrismaPrepare...`,
        "success"
      );
      startBatProgressTracker();
    } else if (r.error === "bat_in_progress") {
      const msg = r.message || "Un BAT est déjà en cours de génération. Veuillez patienter.";
      showNotification(`⏳ ${msg}`, "warning");
    } else {
      showNotification("❌ BAT Papier : " + (r.error || "Erreur inconnue"), "error");
    }
  } catch (err) {
    showNotification("❌ BAT Papier : " + err.message, "error");
  }
}
