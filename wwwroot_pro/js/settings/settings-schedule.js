import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsSchedule(panel) {
  panel.innerHTML = `<h3>Plages horaires et jours fériés</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { workStart: "08:00", workEnd: "18:00", holidays: [] };
  try {
    const resp = await fetch("/api/config/schedule", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  const holidays = Array.isArray(cfg.holidays) ? cfg.holidays : [];

  panel.innerHTML = `
    <h3>Plages horaires et jours fériés</h3>
    <div class="settings-form-group">
      <label>Début journée</label>
      <input type="time" id="sch-start" value="${cfg.workStart || '08:00'}" class="settings-input" />
    </div>
    <div class="settings-form-group">
      <label>Fin journée</label>
      <input type="time" id="sch-end" value="${cfg.workEnd || '18:00'}" class="settings-input" />
    </div>
    <button id="sch-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les plages</button>
    <hr style="margin: 20px 0;" />
    <h4>Jours fériés</h4>
    <div style="display: flex; gap: 8px; margin-bottom: 10px; flex-wrap: wrap;">
      <input type="date" id="sch-holiday-date" class="settings-input" />
      <button id="sch-add-holiday" class="btn btn-primary">Ajouter</button>
      <button id="sch-add-french-holidays" class="btn">Ajouter jours fériés français</button>
    </div>
    <div id="sch-holidays-list">
      ${holidays.length === 0 ? '<p style="color:#9ca3af;">Aucun jour férié configuré</p>' : holidays.map(h => `
        <div style="display: flex; align-items: center; gap: 10px; padding: 6px 10px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 4px;">
          <span style="flex:1;">${new Date(h + "T00:00:00").toLocaleDateString("fr-FR", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })}</span>
          <button class="btn btn-sm" data-date="${h}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
        </div>
      `).join("")}
    </div>
  `;

  document.getElementById("sch-save").onclick = async () => {
    const workStart = document.getElementById("sch-start").value;
    const workEnd = document.getElementById("sch-end").value;
    const r = await fetch("/api/config/schedule", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ workStart, workEnd })
    }).then(r => r.json());
    if (r.ok) {
      const [h, m] = workEnd.split(":").map(Number);
      const bufferedEnd = `${String(Math.min(h + 1, 24)).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      if (window._calendar) {
        window._calendar.setOption("slotMinTime", workStart);
        window._calendar.setOption("slotMaxTime", bufferedEnd);
      }
      if (window._submissionCalendar) {
        window._submissionCalendar.setOption("slotMinTime", workStart);
        window._submissionCalendar.setOption("slotMaxTime", bufferedEnd);
      }
      showNotification("✅ Plages horaires enregistrées", "success");
    } else alert("Erreur : " + r.error);
  };

  document.getElementById("sch-add-holiday").onclick = async () => {
    const dateVal = document.getElementById("sch-holiday-date").value;
    if (!dateVal) { alert("Sélectionnez une date"); return; }
    const r = await fetch("/api/config/schedule/holidays", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ date: dateVal })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Jour férié ajouté", "success");
      panel._loaded = false;
      await renderSettingsSchedule(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("sch-add-french-holidays").onclick = async () => {
    const year = new Date().getFullYear();
    const frenchHolidays = getFrenchPublicHolidays(year);
    let added = 0;
    for (const date of frenchHolidays) {
      const r = await fetch("/api/config/schedule/holidays", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ date })
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) added++;
    }
    showNotification(`✅ ${added} jours fériés français ajoutés pour ${year}`, "success");
    panel._loaded = false;
    await renderSettingsSchedule(panel);
  };

  document.querySelectorAll("#sch-holidays-list button[data-date]").forEach(btn => {
    btn.onclick = async () => {
      const dateToRemove = btn.dataset.date;
      const r = await fetch(`/api/config/schedule/holidays?date=${encodeURIComponent(dateToRemove)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("✅ Jour férié supprimé", "success");
        panel._loaded = false;
        await renderSettingsSchedule(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}

export function getFrenchPublicHolidays(year) {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  const easter = new Date(year, month - 1, day);

  function addDays(d, n) {
    const r = new Date(d);
    r.setDate(r.getDate() + n);
    return r.toISOString().split("T")[0];
  }
  function fmt(y, m, d) {
    return `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
  }

  return [
    fmt(year, 1, 1),
    addDays(easter, 1),
    fmt(year, 5, 1),
    fmt(year, 5, 8),
    addDays(easter, 39),
    addDays(easter, 50),
    fmt(year, 7, 14),
    fmt(year, 8, 15),
    fmt(year, 11, 1),
    fmt(year, 11, 11),
    fmt(year, 12, 25)
  ];
}
