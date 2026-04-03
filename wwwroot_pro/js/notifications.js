// notifications.js — Système de notifications (cloche, polling, popup)
import { currentUser, authToken } from './core.js';

export async function pollNotifications() {
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

    if (Array.isArray(notifs) && notifs.length > 0) {
      const storageKey = `seenNotifs_${currentUser.login}`;
      const seenIds = new Set(JSON.parse(localStorage.getItem(storageKey) || "[]"));
      const newNotifs = notifs.filter(n => !n.read && n.id && !seenIds.has(n.id));
      if (newNotifs.length > 0) {
        // Handle each new notification by type
        for (const notif of newNotifs) {
          if (notif.type === "bat_ready") {
            showBatReadyPopup(notif);
          } else {
            showNotificationPopup(notif);
          }
          seenIds.add(notif.id);
        }
        localStorage.setItem(storageKey, JSON.stringify([...seenIds]));
      }
    }
    window._lastNotifs = notifs;
  } catch(e) { console.error("Notification poll error:", e); }
}

export function showBatReadyPopup(notif) {
  const existingPopup = document.getElementById("bat-ready-popup-overlay");
  if (existingPopup) existingPopup.remove();

  const numeroDossier = notif.numeroDossier || notif.fileName || "—";
  const overlay = document.createElement("div");
  overlay.id = "bat-ready-popup-overlay";
  overlay.className = "notification-popup-overlay";
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:20000;display:flex;align-items:flex-start;justify-content:center;padding-top:80px;";

  const box = document.createElement("div");
  box.className = "notification-popup";
  box.style.cssText = "background:white;border-radius:14px;padding:24px 28px;box-shadow:0 12px 40px rgba(0,0,0,0.25);max-width:400px;width:90%;border-left:5px solid #22c55e;";
  box.innerHTML = `
    <div style="font-size:22px;margin-bottom:8px;">✅</div>
    <div style="font-size:16px;font-weight:700;color:#111827;margin-bottom:6px;">BAT prêt !</div>
    <div style="font-size:14px;color:#374151;margin-bottom:16px;">Le BAT pour le dossier <strong>${numeroDossier}</strong> est prêt et disponible dans la vue BAT.</div>
    <div style="display:flex;gap:10px;">
      <button id="bat-ready-popup-view" class="btn btn-primary btn-sm">Voir le BAT</button>
      <button id="bat-ready-popup-ok" class="btn btn-sm">Fermer</button>
    </div>
  `;
  overlay.appendChild(box);
  document.body.appendChild(overlay);

  const dismiss = () => {
    overlay.remove();
    // Mark notification as read
    fetch("/api/notifications/read", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ login: currentUser.login, id: notif.id })
    }).catch(() => {});
  };

  box.querySelector("#bat-ready-popup-ok").onclick = dismiss;
  box.querySelector("#bat-ready-popup-view").onclick = () => {
    dismiss();
    // Navigate to BAT view if available
    const btnBat = document.getElementById("btnViewBat");
    if (btnBat) btnBat.click();
  };
  overlay.onclick = (e) => { if (e.target === overlay) dismiss(); };
}

export function showNotificationPopup(notif) {
  const existingPopup = document.getElementById("notif-popup-overlay");
  if (existingPopup) existingPopup.remove();

  const overlay = document.createElement("div");
  overlay.id = "notif-popup-overlay";
  overlay.className = "notification-popup-overlay";

  const box = document.createElement("div");
  box.className = "notification-popup";
  box.innerHTML = `
    <p>${notif?.message || "Une tâche vous a été affectée."}</p>
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

export function initNotificationBell() {
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
