// bat.js — Gestion BAT : popup BAT complet / BAT simple + envoi hotfolder
import { authToken, currentUser, normalizePath, fnKey, showNotification } from './core.js';

// ======================================================
// POPUP CHOIX BAT
// ======================================================

/**
 * Affiche la popup de choix BAT (BAT Complet / BAT Simple).
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
        <button class="bat-choice-btn bat-simple-btn" id="bat-simple-btn">
          <div class="bat-choice-icon">📄</div>
          <div class="bat-choice-text">
            <strong>BAT Simple</strong>
            <span>Ouverture du fichier dans Acrobat Pro (sans déplacer le fichier)</span>
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
    } else {
      showNotification("❌ BAT Complet : " + (r.error || "Erreur inconnue"), "error");
    }
  } catch (err) {
    showNotification("❌ BAT Complet : " + err.message, "error");
  }
}

// ======================================================
// BAT SIMPLE — Ouvrir dans Acrobat Pro (sans déplacer le fichier)
// ======================================================
async function sendBatSimple(fullPath) {
  const path = normalizePath(fullPath);

  try {
    const r = await fetch("/api/acrobat/open", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath: path })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification("✅ BAT Simple : fichier ouvert dans Acrobat Pro", "success");
    } else {
      showNotification("❌ BAT Simple : " + (r.error || "Erreur ouverture Acrobat"), "error");
    }
  } catch (err) {
    showNotification("❌ BAT Simple : " + err.message, "error");
  }
}
