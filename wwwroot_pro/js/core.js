// core.js — État global partagé, constantes et utilitaires
// Importé par tous les autres modules. Aucune dépendance vers d'autres modules.

// ======================================================
// ÉTAT GLOBAL
// ======================================================
export let currentUser = null;
export let authToken = null;
export let deliveriesByPath = {};
export let assignmentsByPath = {};

// Setters — nécessaires car les modules importateurs ne peuvent pas réassigner un binding importé
export function setCurrentUser(u) { currentUser = u; }
export function setAuthToken(t) { authToken = t; }
export function setDeliveriesByPath(d) { deliveriesByPath = d; }
export function setAssignmentsByPath(a) { assignmentsByPath = a; }

// ======================================================
// CONSTANTES — Noms de dossiers
// ======================================================
export const FOLDER_SOUMISSION = "Soumission";
export const FOLDER_DEBUT_PRODUCTION = "Début de production";
export const FOLDER_FIN_PRODUCTION = "Fin de production";
export const FIN_PROD_FOLDER = "Fin de production";

// ======================================================
// UTILITAIRES — Formatage date/heure
// ======================================================
export function formatDateTime(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  return d.toLocaleDateString("fr-FR") + " " + d.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
}

// Luminosity check for contrast
export function isLight(hex) {
  const r = parseInt(hex.slice(1,3),16), g = parseInt(hex.slice(3,5),16), b = parseInt(hex.slice(5,7),16);
  return (r*299 + g*587 + b*114) / 1000 > 128;
}

export function fnKey(pathOrName) {
  if (!pathOrName) return "";
  return (pathOrName.split(/[/\\]/).pop() || "").toLowerCase();
}

export function normalizePath(p) {
  if (!p) return "";
  return decodeURIComponent(p).replace(/\u00A0/g, " ").replace(/\//g, "\\").replace(/%5C/gi, "\\");
}

export function sanitizeFolderName(s) {
  return (s || "").replace(/\u00A0/g, " ").replace(/\s+/g, " ").trim().toLowerCase();
}

export function fmtBytes(b) {
  if (b == null) return "";
  const units = ["o", "Ko", "Mo", "Go"];
  let i = 0, x = b;
  while (x >= 1024 && i < units.length - 1) { x /= 1024; i++; }
  return `${x.toFixed(i ? 1 : 0)} ${units[i]}`;
}

export function daysDiffFromToday(iso) {
  const t = new Date();
  t.setHours(0, 0, 0, 0);
  const d = new Date(iso + "T00:00:00");
  return Math.ceil((d - t) / 86400000);
}

export function darkenColor(color, percent) {
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

export function showNotification(message, type = "info") {
  const toast = document.createElement("div");
  toast.className = `toast-notification toast-${type === "success" ? "success" : type === "error" ? "error" : "info"}`;
  toast.textContent = message;
  document.body.appendChild(toast);

  setTimeout(() => {
    toast.style.animation = "toastFadeOut 0.3s ease forwards";
    setTimeout(() => toast.remove(), 300);
  }, 3000);
}

export function esc(s) {
  return (s || "").replace(/"/g, '&quot;');
}

// ======================================================
// CHARGEMENT DES LIVRAISONS ET AFFECTATIONS
// ======================================================
export async function loadDeliveries() {
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

export async function loadAssignments() {
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

// ======================================================
// AUTHENTIFICATION
// ======================================================
export async function initLogin(onSuccess) {
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
      onSuccess();
      return;
    }
  }

  async function attemptLogin(login, password, force = false) {
    try {
      const resp = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ login, password, ...(force ? { force: true } : {}) })
      }).then(r => r.json());

      if (!resp.ok) {
        // Session already active on another device — show confirmation modal
        if (resp.error === "session_active") {
          _showForceLoginModal(resp.message || "Ce compte est déjà connecté sur un autre poste.", () => {
            attemptLogin(login, password, true);
          });
          return;
        }
        loginError.textContent = resp.message || resp.error || "Erreur de connexion";
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

      onSuccess();
    } catch (err) {
      loginError.textContent = "Erreur réseau";
      loginError.style.display = "block";
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

    await attemptLogin(login, password);
  };
}

function _showForceLoginModal(message, onConfirm) {
  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;z-index:99999;";

  const box = document.createElement("div");
  box.style.cssText = "background:#fff;border-radius:14px;padding:28px 32px;max-width:420px;width:90%;box-shadow:0 12px 40px rgba(0,0,0,.25);";
  box.innerHTML = `
    <div style="display:flex;align-items:center;gap:12px;margin-bottom:16px;">
      <span style="font-size:28px;">🔒</span>
      <h3 style="margin:0;font-size:16px;font-weight:700;color:#1e3a5f;">Compte déjà connecté</h3>
    </div>
    <p style="font-size:13px;color:#4b5563;margin-bottom:20px;line-height:1.5;">${message}</p>
    <div style="display:flex;flex-direction:column;gap:8px;">
      <button id="_force-login-btn" style="padding:10px 16px;background:#dc2626;color:#fff;border:none;border-radius:8px;font-size:13px;font-weight:600;cursor:pointer;">
        🔓 Forcer la connexion (déconnecter l'autre session)
      </button>
      <button id="_force-cancel-btn" style="padding:10px 16px;background:#f9fafb;color:#374151;border:1px solid #d1d5db;border-radius:8px;font-size:13px;cursor:pointer;">
        Annuler
      </button>
    </div>
  `;

  overlay.appendChild(box);
  document.body.appendChild(overlay);

  box.querySelector("#_force-login-btn").onclick = () => {
    overlay.remove();
    onConfirm();
  };
  box.querySelector("#_force-cancel-btn").onclick = () => overlay.remove();
  overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };
}

export function logout() {
  authToken = null;
  currentUser = null;
  localStorage.removeItem("authToken");
  location.reload();
}
