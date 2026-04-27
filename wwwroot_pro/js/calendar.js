// calendar.js — Calendrier FullCalendar (planification, soumission)
import { authToken, deliveriesByPath, fnKey, normalizePath, FIN_PROD_FOLDER, daysDiffFromToday, showNotification, currentUser, esc } from './core.js';

export let calendar = null;
export let submissionCalendar = null;

// Current planning view mode: 'global' | 'machine' | 'operator' | 'finitions'
let _planningViewMode = 'global';

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

// ======================================================
// PLANNING VIEW SWITCHER
// ======================================================

function buildPlanningViewSwitcher(calendarEl) {
  // Remove existing switcher if any
  const existing = document.getElementById("planning-view-switcher");
  if (existing) existing.remove();

  // Default view depends on user profile
  const userProfile = (() => {
    try {
      const token = authToken;
      if (!token) return 0;
      const decoded = atob(token);
      const parts = decoded.split(':');
      return parseInt(parts[2] || '0');
    } catch(e) { return 0; }
  })();
  if (_planningViewMode === 'global' && userProfile !== 3) {
    _planningViewMode = 'operator';
  }

  const switcher = document.createElement("div");
  switcher.id = "planning-view-switcher";
  switcher.style.cssText = "display:flex;gap:6px;margin-bottom:10px;flex-wrap:wrap;align-items:center;";
  const views = [
    { id: 'global', label: '🌐 Fin de production' },
    { id: 'machine', label: '🖨️ Par machine' },
    { id: 'operator', label: '👤 Par opérateur' },
    { id: 'finitions', label: '✂️ Finitions du jour' },
  ];
  views.forEach(v => {
    const btn = document.createElement("button");
    btn.className = "btn" + (_planningViewMode === v.id ? " btn-primary" : "");
    btn.style.cssText = "font-size:13px;padding:5px 14px;border-radius:20px;";
    btn.textContent = v.label;
    btn.onclick = async () => {
      _planningViewMode = v.id;
      switcher.querySelectorAll("button").forEach(b => b.classList.remove("btn-primary"));
      btn.classList.add("btn-primary");
      await applyPlanningView(calendarEl);
    };
    switcher.appendChild(btn);
  });
  calendarEl.parentNode?.insertBefore(switcher, calendarEl);
}

async function applyPlanningView(calendarEl) {
  const finitionsEl = document.getElementById("planning-finitions-view");
  const operatorEl = document.getElementById("planning-operator-view");

  if (_planningViewMode === 'finitions') {
    if (calendar) {
      calendarEl.style.display = 'none';
    }
    if (finitionsEl) {
      finitionsEl.style.display = '';
      await buildFinitionsView(finitionsEl);
    }
    if (operatorEl) operatorEl.style.display = 'none';
    return;
  }

  if (_planningViewMode === 'operator') {
    if (calendar) calendarEl.style.display = 'none';
    if (finitionsEl) finitionsEl.style.display = 'none';
    let opEl = operatorEl;
    if (!opEl) {
      opEl = document.createElement("div");
      opEl.id = "planning-operator-view";
      opEl.style.cssText = "padding:8px 0;";
      calendarEl.parentNode?.appendChild(opEl);
    }
    opEl.style.display = '';
    await buildOperatorView(opEl);
    return;
  }

  if (calendar) calendarEl.style.display = '';
  if (finitionsEl) finitionsEl.style.display = 'none';
  if (operatorEl) operatorEl.style.display = 'none';
  calendar?.refetchEvents();
}

// ======================================================
// FINITIONS VIEW
// ======================================================

async function buildFinitionsView(container) {
  container.innerHTML = '<p style="color:#6b7280;font-size:13px;">Chargement...</p>';
  const today = new Date().toISOString().split('T')[0];

  try {
    const list = await fetch("/api/delivery").then(r => r.json());
    const todayJobs = list.filter(x => x.date === today);

    if (todayJobs.length === 0) {
      container.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucune production planifiée pour aujourd\'hui.</p>';
      return;
    }

    const FINITION_LABELS = {
      embellissement: 'Embellissement', rainage: 'Rainage', pliage: 'Pliage',
      faconnage: 'Façonnage', coupe: 'Coupe', emballage: 'Emballage',
      depart: 'Départ', livraison: 'Livraison'
    };

    let html = `<h3 style="margin-bottom:16px;font-size:16px;font-weight:600;">Production du jour — ${new Date(today + 'T00:00:00').toLocaleDateString('fr-FR', { weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' })}</h3>`;

    for (const job of todayJobs) {
      let fiche = null;
      try {
        fiche = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fnKey(job.fullPath || '')), {
          headers: { 'Authorization': `Bearer ${authToken}` }
        }).then(r => r.json());
      } catch(e) { /* ignore */ }

      const numeroDossier = fiche?.numeroDossier || fnKey(job.fullPath || '') || '—';
      const faconnageList = Array.isArray(fiche?.faconnage) ? fiche.faconnage : [];
      const ennoblissementList = Array.isArray(fiche?.ennoblissement) ? fiche.ennoblissement : [];

      // Build finitions list
      const finitions = [];
      if (ennoblissementList.length > 0) finitions.push('Embellissement (' + ennoblissementList.join(', ') + ')');
      if (fiche?.rainage) finitions.push('Rainage');
      if (faconnageList.includes('Pliage')) finitions.push('Pliage');
      if (fiche?.faconnageBinding) finitions.push('Façonnage (' + fiche.faconnageBinding + ')');
      else if (faconnageList.some(f => f.toLowerCase().includes('façon'))) finitions.push('Façonnage');
      if (faconnageList.includes('Coupe')) finitions.push('Coupe');
      if (faconnageList.includes('Emballage')) finitions.push('Emballage');
      if (fiche?.retraitLivraison === 'Retrait imprimerie' || fiche?.retraitLivraison === 'Départ') finitions.push('Départ');
      else finitions.push('Livraison');

      html += `<div style="margin-bottom:16px;padding:14px 16px;background:white;border:1px solid #e5e7eb;border-radius:10px;box-shadow:0 1px 4px rgba(0,0,0,.06);">
        <div style="font-weight:600;font-size:14px;margin-bottom:8px;color:#111827;">Dossier N° ${numeroDossier}</div>
        ${fiche?.client ? `<div style="font-size:12px;color:#6b7280;margin-bottom:6px;">${fiche.client}</div>` : ''}
        <ul style="margin:0;padding:0 0 0 16px;font-size:13px;color:#374151;">
          ${finitions.map(f => `<li>${f}</li>`).join('')}
        </ul>
      </div>`;
    }

    container.innerHTML = html;
  } catch(err) {
    container.innerHTML = '<p style="color:#ef4444;">Erreur lors du chargement des finitions.</p>';
    console.error("buildFinitionsView error:", err);
  }
}

// ======================================================
// OPERATOR VIEW
// ======================================================

async function buildOperatorView(container) {
  container.innerHTML = '<p style="color:#6b7280;font-size:13px;">Chargement...</p>';

  try {
    const list = await fetch("/api/delivery").then(r => r.json()).catch(() => []);

    if (list.length === 0) {
      container.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun job planifié.</p>';
      return;
    }

    // Fetch fabrication data for all jobs to get their operator
    const withFab = await Promise.all(list.map(async x => {
      try {
        const fiche = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fnKey(x.fullPath || x.fileName || '')), {
          headers: { 'Authorization': `Bearer ${authToken}` }
        }).then(r => r.json()).catch(() => ({}));
        return { x, operateur: fiche?.operateur || '', numeroDossier: fiche?.numeroDossier || '', client: fiche?.client || '', locked: !!fiche?.locked };
      } catch(e) { return { x, operateur: '', numeroDossier: '', client: '', locked: false }; }
    }));

    // Group by operator
    const grouped = {};
    for (const item of withFab) {
      const op = item.operateur || '— Non assigné —';
      if (!grouped[op]) grouped[op] = [];
      grouped[op].push(item);
    }

    const sortedOps = Object.keys(grouped).sort((a, b) => {
      if (a === '— Non assigné —') return 1;
      if (b === '— Non assigné —') return -1;
      return a.localeCompare(b);
    });

    let html = `<h3 style="margin-bottom:16px;font-size:16px;font-weight:600;">Planning par opérateur</h3>`;
    for (const op of sortedOps) {
      const jobs = grouped[op];
      html += `<div style="margin-bottom:24px;">
        <div style="font-size:14px;font-weight:700;color:#1e3a5f;padding:8px 12px;background:#e8f0fe;border-radius:8px;margin-bottom:10px;">👤 ${esc(op)} (${jobs.length})</div>
        <div style="display:flex;flex-wrap:wrap;gap:10px;">`;
      for (const { x, numeroDossier, client, locked } of jobs) {
        const fn = fnKey(x.fullPath || x.fileName || '');
        const label = numeroDossier ? `#${numeroDossier}${client ? ' — ' + client : ''}` : fn;
        const dateStr = x.date ? new Date(x.date + 'T00:00:00').toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' }) : '—';
        const bg = locked ? '#22c55e' : '#f9fafb';
        const border = locked ? '#16a34a' : '#e5e7eb';
        const textColor = locked ? '#fff' : '#111827';
        html += `<div style="background:${bg};border:1px solid ${border};border-radius:8px;padding:10px 14px;min-width:140px;max-width:220px;cursor:pointer;" 
          onclick="if(window._openFabrication)window._openFabrication(${JSON.stringify(normalizePath(x.fullPath || ''))})">
          <div style="font-size:13px;font-weight:600;color:${textColor};">${locked ? '🔒 ' : ''}${esc(label)}</div>
          <div style="font-size:12px;color:${locked ? '#d1fae5' : '#6b7280'};margin-top:4px;">📅 ${dateStr}${x.time && x.time !== '09:00' ? ' ' + esc(x.time) : ''}</div>
        </div>`;
      }
      html += `</div></div>`;
    }

    container.innerHTML = html;
  } catch(err) {
    container.innerHTML = `<div style="color:#dc2626;">Erreur : ${esc(err.message)}</div>`;
  }
}

export async function initCalendar() {
  const calendarEl = document.getElementById("calendar");
  if (calendar || !calendarEl || !window.FullCalendar) return;

  // Create finitions view container
  let finitionsEl = document.getElementById("planning-finitions-view");
  if (!finitionsEl) {
    finitionsEl = document.createElement("div");
    finitionsEl.id = "planning-finitions-view";
    finitionsEl.style.display = "none";
    calendarEl.parentNode?.insertBefore(finitionsEl, calendarEl.nextSibling);
  }

  buildPlanningViewSwitcher(calendarEl);

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
    weekends: true,
    eventDurationEditable: false,
    eventAllow: (_dropInfo, draggedEvent) => !draggedEvent.extendedProps?.locked,
    events: async (_info, success) => {
      try {
        // Load delivery events (manual planning) + fabrication key-date events
        const [deliveryList, fabEventsResp] = await Promise.all([
          fetch("/api/delivery").then(r => r.json()).catch(() => []),
          fetch("/api/fabrication/events", { headers: { 'Authorization': `Bearer ${authToken}` } })
            .then(r => r.json()).catch(() => ({ ok: false, events: [] }))
        ]);

        const fabEvents = (fabEventsResp.ok && Array.isArray(fabEventsResp.events)) ? fabEventsResp.events : [];

        // Get current user login AND name for operator filter
        const myLogin = (() => {
          try {
            const token = authToken;
            if (!token) return '';
            const decoded = atob(token);
            return decoded.split(':')[1] || '';
          } catch(e) { return ''; }
        })();
        const myName = currentUser?.name || '';

        // Get machine filter value
        const machineFilter = document.getElementById("planning-machine-filter")?.value || "";

        // Build events from fabrication key dates based on view mode
        const fabCalEvents = fabEvents
          .filter(fe => {
            if (_planningViewMode === 'global') return fe.type === 'envoi';
            if (_planningViewMode === 'machine') {
              if (fe.type !== 'impression') return false;
              if (machineFilter && fe.moteurImpression !== machineFilter) return false;
              return true;
            }
            if (_planningViewMode === 'operator') {
              if (fe.type !== 'impression') return false;
              if ((myLogin || myName) && fe.operateur) {
                const opMatch = fe.operateur === myLogin || fe.operateur === myName;
                if (!opMatch) return false;
              }
              return true;
            }
            return false;
          })
          .map(fe => {
            const colorMap = {
              envoi:      { bg: '#3b82f6', bc: '#2563eb', tc: '#ffffff' },
              impression: { bg: '#8b5cf6', bc: '#7c3aed', tc: '#ffffff' },
              finitions:  { bg: '#f59e0b', bc: '#d97706', tc: '#ffffff' }
            };
            const c = colorMap[fe.type] || { bg: '#6b7280', bc: '#4b5563', tc: '#ffffff' };
            const durationMins = (fe.tempsProduitMinutes && fe.tempsProduitMinutes > 0)
              ? fe.tempsProduitMinutes : 30;
            const timeStr = fe.manualTime || "09:00";
            const startDt = new Date(`${fe.date}T${timeStr}:00`);
            const endDt = new Date(startDt.getTime() + durationMins * 60000);
            return {
              title: fe.title,
              start: startDt.toISOString(),
              end: endDt.toISOString(),
              allDay: false,
              backgroundColor: c.bg,
              borderColor: c.bc,
              textColor: c.tc,
              editable: true,
              startEditable: true,
              durationEditable: false,
              extendedProps: { fullPath: fe.fullPath, isFabEvent: true, fabType: fe.type, fabFileName: fe.fileName }
            };
          });

        // For non-global views, delivery events can still show (they are manually planned)
        let filtered = deliveryList;
        if (_planningViewMode === 'machine' && machineFilter) {
          const withMachine = await Promise.all(filtered.map(async x => {
            try {
              const fiche = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fnKey(x.fullPath || '')), {
                headers: { 'Authorization': `Bearer ${authToken}` }
              }).then(r => r.json());
              return { x, machine: fiche?.moteurImpression || fiche?.machine || '' };
            } catch(e) { return { x, machine: '' }; }
          }));
          filtered = withMachine.filter(wm => wm.machine === machineFilter).map(wm => wm.x);
        } else if (_planningViewMode === 'operator' && (myLogin || myName)) {
          const withOperator = await Promise.all(filtered.map(async x => {
            try {
              const fiche = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fnKey(x.fullPath || '')), {
                headers: { 'Authorization': `Bearer ${authToken}` }
              }).then(r => r.json());
              return { x, operateur: fiche?.operateur || '' };
            } catch(e) { return { x, operateur: '' }; }
          }));
          filtered = withOperator.filter(wo => !wo.operateur || wo.operateur === myLogin || wo.operateur === myName).map(wo => wo.x);
        }

        const ev = filtered.map(x => {
          const full = normalizePath(x.fullPath);
          const inFinProd = full.toLowerCase().includes(FIN_PROD_FOLDER.toLowerCase());
          const locked = !!x.locked || inFinProd;
          const bg = locked ? "#22c55e" : colorForEvent(full, x.date).bg;
          const bc = locked ? "#16a34a" : colorForEvent(full, x.date).bc;
          const tc = locked ? "#ffffff" : colorForEvent(full, x.date).tc;
          const time = x.time || "09:00";
          // Duration based on tempsProduitMinutes if available
          const durationMins = x.tempsProduitMinutes || 60;
          const startDate = new Date(`${x.date}T${time}:00`);
          return {
            title: (locked ? "🔒 " : "") + x.fileName,
            start: startDate.toISOString(),
            end: new Date(startDate.getTime() + durationMins * 60000).toISOString(),
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            editable: !locked,
            startEditable: !locked,
            durationEditable: false,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time, locked }
          };
        });

        // Merge delivery events and fab key-date events
        ev.push(...fabCalEvents);

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

        if (info.event.extendedProps.isFabEvent) {
          // Save manual time for fabrication key-date event
          const fabType = info.event.extendedProps.fabType;
          const viewTypeMap = { envoi: 'global', impression: 'machine', finitions: 'operator' };
          const viewType = viewTypeMap[fabType];
          if (!viewType) { info.revert(); return; }
          const fileName = info.event.extendedProps.fabFileName || fk;
          const r = await fetch("/api/fabrication/event-time", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fileName, viewType, newDate, newTime })
          }).then(r => r.json());
          if (!r.ok) throw new Error(r.error || "Erreur");
          showNotification(`✅ Planning mis à jour : ${newTime}`, "success");
        } else {
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
        }
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

  // Add machine filter if in machine view
  addMachineFilter(calendarEl);
}

async function addMachineFilter(calendarEl) {
  const existing = document.getElementById("planning-machine-filter-wrap");
  if (existing) existing.remove();

  const wrap = document.createElement("div");
  wrap.id = "planning-machine-filter-wrap";
  wrap.style.cssText = "margin-bottom:8px;display:none;align-items:center;gap:8px;";
  wrap.innerHTML = `<label style="font-size:13px;font-weight:500;">Moteur :</label>
    <select id="planning-machine-filter" class="settings-input" style="font-size:13px;padding:4px 8px;min-width:160px;"></select>`;
  calendarEl.parentNode?.insertBefore(wrap, calendarEl);

  try {
    const engines = await fetch("/api/config/print-engines").then(r => r.json()).catch(() => []);
    const sel = wrap.querySelector("#planning-machine-filter");
    sel.innerHTML = '<option value="">Toutes</option>' + (Array.isArray(engines) ? engines.map(e => {
      const n = typeof e === 'object' ? (e.name || '') : String(e || '');
      return `<option value="${n}">${n}</option>`;
    }).join('') : '');
    sel.onchange = () => calendar?.refetchEvents();
  } catch(e) { /* ignore */ }

  // Show filter only in machine view
  const switcher = document.getElementById("planning-view-switcher");
  if (switcher) {
    switcher.querySelectorAll("button").forEach(btn => {
      btn.addEventListener("click", () => {
        wrap.style.display = _planningViewMode === 'machine' ? 'flex' : 'none';
      });
    });
  }
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
    eventAllow: (_dropInfo, draggedEvent) => !draggedEvent.extendedProps?.locked,
    events: async (info, success) => {
      try {
        const list = await fetch("/api/delivery").then(r => r.json());
        const ev = list.map(x => {
          const full = normalizePath(x.fullPath);
          const inFinProd = full.toLowerCase().includes(FIN_PROD_FOLDER.toLowerCase());
          const locked = !!x.locked || inFinProd;
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
            startEditable: !locked,
            durationEditable: false,
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
