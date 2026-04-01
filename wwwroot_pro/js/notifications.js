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
        showNotificationPopup();
        newNotifs.forEach(n => seenIds.add(n.id));
        localStorage.setItem(storageKey, JSON.stringify([...seenIds]));
      }
    }
    window._lastNotifs = notifs;
  } catch(e) { console.error("Notification poll error:", e); }
}

export function showNotificationPopup() {
  const existingPopup = document.getElementById("notif-popup-overlay");
  if (existingPopup) existingPopup.remove();

  const overlay = document.createElement("div");
  overlay.id = "notif-popup-overlay";
  overlay.className = "notification-popup-overlay";

  const box = document.createElement("div");
  box.className = "notification-popup";
  box.innerHTML = `
    <p>Une tâche vous a été affectée.</p>
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
