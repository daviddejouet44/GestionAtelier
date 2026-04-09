// calendar.js — Calendrier FullCalendar (planification, soumission)
import { authToken, deliveriesByPath, fnKey, normalizePath, FIN_PROD_FOLDER, daysDiffFromToday, showNotification } from './core.js';

export let calendar = null;
export let submissionCalendar = null;

// ======================================================
// CALENDRIER PRINCIPAL
// ======================================================

export async function ensureCalendar() {
  if (!calendar) await initCalendar();
}

export function colorForEvent(fullPath, isoDate) {
  const normalized = normalizePath(fullPath).toLowerCase();
  const finProdLower = FIN_PROD_FOLDER.toLowerCase();

  if (normalized.includes(finProdLower)) {
    return { bg: "#16A34A", bc: "#16A34A", tc: "#fff" };
  }

  const d = daysDiffFromToday(isoDate);
  if (d <= 1) return { bg: "#DC2626", bc: "#DC2626", tc: "#fff" };
  if (d <= 3) return { bg: "#F59E0B", bc: "#F59E0B", tc: "#111827" };
  return { bg: "#2563EB", bc: "#2563EB", tc: "#fff" };
}

export function openPlanificationCalendar(fullPath) {
  const today = new Date();
  const defaultDate = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;

  const div = document.createElement("div");
  div.style.cssText = `position: fixed; inset: 0; background: rgba(0,0,0,.5); display: flex; align-items: center; justify-content: center; z-index: 10000;`;

  const panel = document.createElement("div");
  panel.style.cssText = `background: white; border-radius: 12px; padding: 20px; box-shadow: 0 10px 40px rgba(0,0,0,.3); min-width: 320px;`;
  panel.innerHTML = `
    <h3 style="margin-top: 0;">Planifier</h3>
    <label style="display: block; margin: 12px 0;"><strong>Date</strong><br/><input type="date" id="planDate" value="${defaultDate}" style="width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 6px; font-size: 14px;" /></label>
    <label style="display: block; margin: 12px 0;"><strong>Heure</strong><br/><input type="time" id="planTime" value="09:00" style="width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 6px; font-size: 14px;" /></label>
    <div style="display: flex; gap: 10px; margin-top: 20px;">
      <button id="planCancel" style="flex: 1; padding: 10px; border: 1px solid #ddd; background: white; border-radius: 6px; cursor: pointer; font-weight: 500;">Annuler</button>
      <button id="planOK" style="flex: 1; padding: 10px; border: 1px solid #D50000; background: #D50000; color: white; border-radius: 6px; cursor: pointer; font-weight: 500;">Planifier</button>
    </div>
  `;

  div.appendChild(panel);
  document.body.appendChild(div);

  panel.querySelector("#planCancel").onclick = () => div.remove();
  panel.querySelector("#planOK").onclick = async () => {
    const dateVal = panel.querySelector("#planDate").value;
    const timeVal = panel.querySelector("#planTime").value;

    if (!dateVal || !timeVal) {
      alert("Sélectionnez date et heure");
      return;
    }

    const r = await fetch("/api/delivery", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath, fileName: fnKey(fullPath), date: dateVal, time: timeVal })
    }).then(r => r.json());

    if (!r.ok) {
      alert("Erreur");
      return;
    }

    const fk = fnKey(fullPath);
    deliveriesByPath[fk] = dateVal;
    deliveriesByPath[fk + "_time"] = timeVal;

    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();

    div.remove();
    alert(`Planifié pour ${dateVal}`);
  };
}

export async function initCalendar() {
  const calendarEl = document.getElementById("calendar");
  if (calendar || !calendarEl || !window.FullCalendar) return;

  let schedStart = "07:00", schedEnd = "21:00";
  try {
    const sr = await fetch("/api/config/schedule", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
    if (sr.ok && sr.config) {
      if (sr.config.workStart) schedStart = sr.config.workStart;
      if (sr.config.workEnd) {
        const [h, m] = sr.config.workEnd.split(":").map(Number);
        const endH = Math.min(h + 1, 24);
        schedEnd = `${String(endH).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      }
    }
  } catch(e) { /* use defaults */ }

  calendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "dayGridMonth",
    locale: "fr",
    timeZone: "local",
    height: 480,
    scrollTime: schedStart,
    slotMinTime: schedStart,
    slotMaxTime: schedEnd,
    headerToolbar: {
      left: "prev,next today",
      center: "title",
      right: "dayGridMonth,timeGridWeek"
    },
    editable: true,
    eventDurationEditable: false,
    events: async (_info, success) => {
      try {
        const list = await fetch("/api/delivery").then(r => r.json());
        const ev = list.map(x => {
          const full = normalizePath(x.fullPath);
          const locked = !!x.locked;
          const bg = locked ? "#22c55e" : colorForEvent(full, x.date).bg;
          const bc = locked ? "#16a34a" : colorForEvent(full, x.date).bc;
          const tc = locked ? "#ffffff" : colorForEvent(full, x.date).tc;
          const time = x.time || "09:00";
          return {
            title: (locked ? "🔒 " : "") + x.fileName,
            start: `${x.date}T${time}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            editable: !locked,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time, locked }
          };
        });
        try {
          const schedResp = await fetch("/api/config/schedule", {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json());
          if (schedResp.ok && schedResp.config && schedResp.config.holidays) {
            schedResp.config.holidays.forEach(h => {
              ev.push({
                title: "Férié",
                start: h,
                allDay: true,
                display: "background",
                backgroundColor: "#fee2e2",
                borderColor: "#fecaca"
              });
            });
          }
        } catch(e) { console.error("Impossible de charger les jours fériés:", e); }
        success(ev);
      } catch (err) {
        console.error("Erreur events:", err);
        success([]);
      }
    },
    eventDidMount: (info) => {
      const { bg, bc, tc } = info.event.extendedProps || {};
      if (bg) info.el.style.setProperty("--fc-event-bg-color", bg);
      if (bc) info.el.style.setProperty("--fc-event-border-color", bc);
      if (tc) info.el.style.setProperty("--fc-event-text-color", tc);
    },
    eventDrop: async (info) => {
      try {
        const fullPath = normalizePath(info.event.extendedProps.fullPath);
        const fk = fnKey(fullPath);
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fk] = newDate;
        deliveriesByPath[fk + "_time"] = newTime;

        const { bg, bc, tc } = colorForEvent(fullPath, newDate);
        info.event.setProp("backgroundColor", bg);
        info.event.setProp("borderColor", bc);
        info.event.setProp("textColor", tc);

        showNotification(`✅ Planning mis à jour pour le ${newDate}`, "success");
      } catch (err) {
        alert(err.message || "Impossible de déplacer");
        info.revert();
      }
    },
    eventClick: (info) => {
      info.jsEvent.preventDefault();
      const full = normalizePath(info.event.extendedProps.fullPath);
      // openFabrication is registered via callback from app.js
      if (full && window._openFabrication) window._openFabrication(full);
    }
  });

  calendar.render();
}

// ======================================================
// CALENDRIER SOUMISSION (Profil 1)
// ======================================================

export async function initSubmissionCalendar() {
  const calendarEl = document.getElementById("submissionCalendar");
  if (!calendarEl || submissionCalendar) return;

  let schedStart = "07:00", schedEnd = "21:00";
  try {
    const sr = await fetch("/api/config/schedule", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
    if (sr.ok && sr.config) {
      if (sr.config.workStart) schedStart = sr.config.workStart;
      if (sr.config.workEnd) {
        const [h, m] = sr.config.workEnd.split(":").map(Number);
        const endH = Math.min(h + 1, 24);
        schedEnd = `${String(endH).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      }
    }
  } catch(e) { /* use defaults */ }

  submissionCalendar = new FullCalendar.Calendar(calendarEl, {
    initialView: "timeGridWeek",
    locale: "fr",
    timeZone: "local",
    height: 360,
    scrollTime: schedStart,
    slotLabelInterval: "01:00",
    slotMinTime: schedStart,
    slotMaxTime: schedEnd,
    headerToolbar: { left: "prev,next today", center: "title", right: "dayGridMonth,timeGridWeek" },
    editable: true,
    eventDurationEditable: false,
    events: async (info, success) => {
      try {
        const list = await fetch("/api/delivery").then(r => r.json());
        const ev = list.map(x => {
          const full = normalizePath(x.fullPath);
          const locked = !!x.locked;
          const bg = locked ? "#22c55e" : colorForEvent(full, x.date).bg;
          const bc = locked ? "#16a34a" : colorForEvent(full, x.date).bc;
          const tc = locked ? "#ffffff" : colorForEvent(full, x.date).tc;
          const time = x.time || "09:00";
          return {
            title: (locked ? "🔒 " : "") + x.fileName,
            start: `${x.date}T${time}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            editable: !locked,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time, locked }
          };
        });
        try {
          const schedResp = await fetch("/api/config/schedule", {
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r => r.json());
          if (schedResp.ok && schedResp.config && schedResp.config.holidays) {
            schedResp.config.holidays.forEach(h => {
              ev.push({
                title: "Férié",
                start: h,
                allDay: true,
                display: "background",
                backgroundColor: "#fee2e2",
                borderColor: "#fecaca"
              });
            });
          }
        } catch(e) { console.error("Impossible de charger les jours fériés:", e); }
        success(ev);
      } catch (err) {
        console.error("Erreur events:", err);
        success([]);
      }
    },
    eventDidMount: (info) => {
      const { bg, bc, tc } = info.event.extendedProps || {};
      if (bg) info.el.style.setProperty("--fc-event-bg-color", bg);
      if (bc) info.el.style.setProperty("--fc-event-border-color", bc);
      if (tc) info.el.style.setProperty("--fc-event-text-color", tc);
    },
    eventDrop: async (info) => {
      try {
        const fullPath = normalizePath(info.event.extendedProps.fullPath);
        const fk = fnKey(fullPath);
        const newDate = info.event.startStr.split('T')[0];
        const newTime = info.event.startStr.split('T')[1]?.substring(0, 5) || "09:00";

        const r = await fetch("/api/delivery", {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
        }).then(r => r.json());

        if (!r.ok) throw new Error(r.error || "Erreur");

        deliveriesByPath[fk] = newDate;
        deliveriesByPath[fk + "_time"] = newTime;

        const { bg, bc, tc } = colorForEvent(fullPath, newDate);
        info.event.setProp("backgroundColor", bg);
        info.event.setProp("borderColor", bc);
        info.event.setProp("textColor", tc);

        showNotification(`✅ Planning mis à jour`, "success");
      } catch (err) {
        showNotification(`❌ ${err.message}`, "error");
        info.revert();
      }
    },
    eventClick: (info) => {
      const full = normalizePath(info.event.extendedProps.fullPath);
      if (full && window._openFabrication) window._openFabrication(full);
    }
  });

  submissionCalendar.render();
  setTimeout(() => submissionCalendar?.refetchEvents(), 500);
}
