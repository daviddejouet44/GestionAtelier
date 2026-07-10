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

  // Coûts de calage (planification intelligente)
  let co = { calageBaseMinutes: 15, changementPapierMinutes: 10, changementFormatMinutes: 8 };
  try {
    const cor = await fetch("/api/config/changeover-costs", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (cor.ok && cor.config) co = cor.config;
  } catch(e) { /* use defaults */ }

  const holidays = Array.isArray(cfg.holidays) ? cfg.holidays : [];

  // Auto-add holidays for current year and next year if missing
  const currentYear = new Date().getFullYear();
  const nextYear = currentYear + 1;
  const missingYears = [];
  if (!holidays.some(h => h.startsWith(String(currentYear)))) missingYears.push(currentYear);
  if (!holidays.some(h => h.startsWith(String(nextYear)))) missingYears.push(nextYear);
  if (missingYears.length > 0) {
    for (const yr of missingYears) {
      const frHolidays = getFrenchPublicHolidays(yr);
      for (const date of frHolidays) {
        if (!holidays.includes(date)) {
          await fetch("/api/config/schedule/holidays", {
            method: "POST",
            headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
            body: JSON.stringify({ date })
          }).then(r => r.json()).catch(() => ({ ok: false }));
          holidays.push(date);
        }
      }
    }
    holidays.sort();
  }

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
    <div style="display: flex; gap: 8px; margin-bottom: 10px; flex-wrap: wrap; align-items: center;">
      <input type="date" id="sch-holiday-date" class="settings-input" />
      <button id="sch-add-holiday" class="btn btn-primary">Ajouter</button>
      <input type="number" id="sch-holiday-year" class="settings-input" value="${currentYear}" min="2020" max="2050" style="width:90px;" title="Année" />
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
    <hr style="margin: 20px 0;" />
    <h4>🪄 Coûts de calage (planification intelligente)</h4>
    <p style="color:#6b7280;font-size:13px;margin-top:0;">Utilisés pour proposer le meilleur ordre de fabrication (regroupement par papier / format) et estimer le temps économisé.</p>
    <div class="settings-form-group">
      <label>Calage de base par tirage (min)</label>
      <input type="number" min="0" id="co-base" value="${co.calageBaseMinutes ?? 15}" class="settings-input" style="width:120px;" />
    </div>
    <div class="settings-form-group">
      <label>Surcoût changement de papier (min)</label>
      <input type="number" min="0" id="co-papier" value="${co.changementPapierMinutes ?? 10}" class="settings-input" style="width:120px;" />
    </div>
    <div class="settings-form-group">
      <label>Surcoût changement de format (min)</label>
      <input type="number" min="0" id="co-format" value="${co.changementFormatMinutes ?? 8}" class="settings-input" style="width:120px;" />
    </div>
    <button id="co-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les coûts de calage</button>
  `;

  const coSaveBtn = document.getElementById("co-save");
  if (coSaveBtn) coSaveBtn.onclick = async () => {
    const payload = {
      calageBaseMinutes: parseInt(document.getElementById("co-base").value) || 0,
      changementPapierMinutes: parseInt(document.getElementById("co-papier").value) || 0,
      changementFormatMinutes: parseInt(document.getElementById("co-format").value) || 0,
      engines: Array.isArray(co.engines) ? co.engines : []
    };
    const r = await fetch("/api/config/changeover-costs", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(payload)
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Coûts de calage enregistrés", "success");
    else alert("Erreur : " + (r.error || "inconnue"));
  };

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
    const yearInput = document.getElementById("sch-holiday-year");
    const year = parseInt(yearInput ? yearInput.value : String(new Date().getFullYear())) || new Date().getFullYear();
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
    fmt(year, 1, 1),   // Jour de l'An
    addDays(easter, 1), // Lundi de Pâques
    fmt(year, 5, 1),   // Fête du Travail
    fmt(year, 5, 8),   // Victoire 1945
    addDays(easter, 39), // Ascension
    addDays(easter, 50), // Lundi de Pentecôte
    fmt(year, 7, 14),  // Fête Nationale
    fmt(year, 8, 15),  // Assomption
    fmt(year, 11, 1),  // Toussaint
    fmt(year, 11, 11), // Armistice
    fmt(year, 12, 25)  // Noël
  ];
}
