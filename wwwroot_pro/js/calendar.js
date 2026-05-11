// calendar.js — Calendrier FullCalendar (planification, soumission)
import { authToken, deliveriesByPath, fnKey, normalizePath, FIN_PROD_FOLDER, daysDiffFromToday, showNotification, currentUser, esc } from './core.js';

export let calendar = null;
export let submissionCalendar = null;

// Current planning view mode: 'machine' | 'finitions' | 'global' | 'livraison'
let _planningViewMode = 'machine';

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

  const switcher = document.createElement("div");
  switcher.id = "planning-view-switcher";
  switcher.style.cssText = "display:flex;gap:6px;margin-bottom:10px;flex-wrap:wrap;align-items:center;";
  const views = [
    { id: 'machine', label: '🖨️ Machine' },
    { id: 'finitions', label: '✂️ Finitions' },
    { id: 'global', label: '🌐 Fin de production' },
    { id: 'livraison', label: '📥 Livraison' },
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

  // Always hide the flat finitions list and legacy operator view
  if (finitionsEl) finitionsEl.style.display = 'none';
  if (operatorEl) operatorEl.style.display = 'none';

  // All three modes now use the main FullCalendar
  if (calendar) calendarEl.style.display = '';
  calendar?.refetchEvents();

  // Show/hide filter bars
  const machineWrap = document.getElementById("planning-machine-filter-wrap");
  if (machineWrap) machineWrap.style.display = _planningViewMode === 'machine' ? 'flex' : 'none';
  const finWrap = document.getElementById("planning-finitions-filter-wrap");
  if (finWrap) finWrap.style.display = _planningViewMode === 'finitions' ? 'flex' : 'none';
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

// Exported: refresh the operator view if currently visible
export async function refreshOperatorView() {
  const operatorEl = document.getElementById("planning-operator-view");
  if (!operatorEl || operatorEl.style.display === 'none') return;
  await buildOperatorView(operatorEl);
}

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

// ======================================================
// LIVRAISON CONFIRMATION POPUP
// Returns: true (update all), false (livraison only), null (cancelled)
// ======================================================
function _showLivraisonConfirmPopup(newDate) {
  return new Promise((resolve) => {
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000;';

    const panel = document.createElement('div');
    panel.style.cssText = 'background:#fff;border-radius:12px;padding:24px;box-shadow:0 10px 40px rgba(0,0,0,.3);max-width:420px;width:90%;';
    panel.innerHTML = `
      <h3 style="margin:0 0 12px;font-size:16px;font-weight:700;color:#1e3a5f;">📦 Déplacer la livraison</h3>
      <p style="font-size:13px;color:#4b5563;margin-bottom:16px;">
        Nouvelle date : <strong>${newDate}</strong><br/>
        Voulez-vous mettre à jour les autres plannings (dates clés liées) en conséquence ?
      </p>
      <div style="display:flex;flex-direction:column;gap:8px;">
        <button id="_liv-yes" style="padding:10px 16px;background:#1d4ed8;color:#fff;border:none;border-radius:8px;font-size:13px;font-weight:600;cursor:pointer;">
          ✅ Oui, mettre à jour les autres plannings
        </button>
        <button id="_liv-no" style="padding:10px 16px;background:#f9fafb;color:#374151;border:1px solid #d1d5db;border-radius:8px;font-size:13px;font-weight:600;cursor:pointer;">
          Non, déplacer uniquement la livraison
        </button>
        <button id="_liv-cancel" style="padding:10px 16px;background:#fff;color:#6b7280;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;cursor:pointer;">
          Annuler
        </button>
      </div>
    `;

    overlay.appendChild(panel);
    document.body.appendChild(overlay);

    const cleanup = () => document.body.removeChild(overlay);

    const yesBtn    = panel.querySelector('#_liv-yes');
    const noBtn     = panel.querySelector('#_liv-no');
    const cancelBtn = panel.querySelector('#_liv-cancel');

    if (yesBtn)    yesBtn.onclick    = () => { cleanup(); resolve(true); };
    if (noBtn)     noBtn.onclick     = () => { cleanup(); resolve(false); };
    if (cancelBtn) cancelBtn.onclick = () => { cleanup(); resolve(null); };
    overlay.onclick = (e) => { if (e.target === overlay) { cleanup(); resolve(null); } };
  });
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

  // Load planning color config (admin-configurable)
  let _planningColors = { engines: {}, finitions: {} };
  try {
    const cr = await fetch("/api/settings/planning-colors").then(r => r.json()).catch(() => ({ ok: false }));
    if (cr.ok && cr.colors) _planningColors = cr.colors;
  } catch(e) { /* use defaults */ }

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

  // Profiles 5 (Lecture plannings) and 6 (Opérateur restreint) get read-only calendars
  const isReadOnlyProfile = currentUser && (currentUser.profile === 5 || currentUser.profile === 6);

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
    editable: !isReadOnlyProfile,
    weekends: true,
    eventDurationEditable: false,
    eventAllow: (_dropInfo, draggedEvent) => !isReadOnlyProfile && !draggedEvent.extendedProps?.locked,
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

        // Get machine filter — pill buttons (empty selection = all)
        const machinePillsEl = document.getElementById("planning-machine-pills");
        const machineFilters = machinePillsEl
          ? Array.from(machinePillsEl.querySelectorAll('.planning-engine-pill[data-selected="true"]')).map(p => p.dataset.value).filter(Boolean)
          : [];
        const operatorFilter = document.getElementById("planning-operator-filter")?.value || "";

        // Get finitions filters — pill buttons (empty selection = all types)
        const finOpFilter = document.getElementById("planning-finitions-operator-filter")?.value || "";
        const finPillsEl = document.getElementById("planning-finitions-pills");
        const finTypeFilters = finPillsEl
          ? Array.from(finPillsEl.querySelectorAll('.planning-engine-pill[data-selected="true"]')).map(p => p.dataset.value).filter(Boolean)
          : [];

        // Build events from fabrication key dates based on view mode
        const fabCalEvents = fabEvents
          .filter(fe => {
            if (_planningViewMode === 'global') return fe.type === 'envoi';
            if (_planningViewMode === 'machine') {
              if (fe.type !== 'impression') return false;
              if (machineFilters.length > 0 && !machineFilters.includes(fe.moteurImpression)) return false;
              if (operatorFilter && fe.operateur !== operatorFilter) return false;
              return true;
            }
            if (_planningViewMode === 'finitions') {
              if (fe.type !== 'finitions') return false;
              if (finOpFilter && fe.operateur !== finOpFilter) return false;
              if (finTypeFilters.length > 0 && !(fe.finitionTypes || []).some(t => finTypeFilters.includes(t))) return false;
              return true;
            }
            if (_planningViewMode === 'livraison') return fe.type === 'reception';
            return false;
          })
          .flatMap(fe => {
            if (!fe.date) return []; // skip entries with no date
            const isLocked = !!fe.locked;
            // Apply configured colors: engines by moteurImpression name, finitions by type, fallback to defaults
            function _darkenHex(hex, amount = 30) {
              const r = Math.max(0, parseInt(hex.slice(1,3),16)-amount);
              const g = Math.max(0, parseInt(hex.slice(3,5),16)-amount);
              const b = Math.max(0, parseInt(hex.slice(5,7),16)-amount);
              return '#'+[r,g,b].map(v=>v.toString(16).padStart(2,'0')).join('');
            }
            let baseBg, baseBc;
            if (fe.type === 'impression' && _planningColors.engines && fe.moteurImpression && _planningColors.engines[fe.moteurImpression]) {
              baseBg = _planningColors.engines[fe.moteurImpression];
              baseBc = _darkenHex(baseBg);
            } else if (fe.type === 'finitions' && _planningColors.finitions) {
              const ftypes = fe.finitionTypes || [];
              const firstType = ftypes[0] || '';
              baseBg = (_planningColors.finitions[firstType]) || '#f59e0b';
              baseBc = _darkenHex(baseBg);
            } else {
              const defaultColorMap = {
                envoi:      { bg: '#3b82f6', bc: '#2563eb' },
                impression: { bg: '#8b5cf6', bc: '#7c3aed' },
                finitions:  { bg: '#f59e0b', bc: '#d97706' },
                reception:  { bg: '#06b6d4', bc: '#0891b2' }
              };
              const dc = defaultColorMap[fe.type] || { bg: '#6b7280', bc: '#4b5563' };
              baseBg = dc.bg; baseBc = dc.bc;
            }
            const baseColor = { bg: baseBg, bc: baseBc, tc: '#ffffff' };
            const c = isLocked ? { bg: '#22c55e', bc: '#16a34a', tc: '#ffffff' } : baseColor;
            const durationMins = (fe.tempsProduitMinutes && fe.tempsProduitMinutes > 0)
              ? fe.tempsProduitMinutes : 30;
            const timeStr = fe.manualTime || "09:00";
            const startDt = new Date(`${fe.date}T${timeStr}:00`);
            if (isNaN(startDt.getTime())) return []; // skip invalid dates
            const endDt = new Date(startDt.getTime() + durationMins * 60000);
            return [{
              title: (isLocked ? '🔒 ' : '') + fe.title,
              start: startDt.toISOString(),
              end: endDt.toISOString(),
              allDay: false,
              backgroundColor: c.bg,
              borderColor: c.bc,
              textColor: c.tc,
              editable: !isLocked,
              startEditable: !isLocked,
              durationEditable: false,
              extendedProps: { fullPath: fe.fullPath, isFabEvent: true, fabType: fe.type, fabFileName: fe.fileName, locked: isLocked, bg: c.bg, bc: c.bc, tc: c.tc }
            }];
          });

        // For machine view, filter delivery events by machine and/or operator
        // For finitions and livraison views, delivery events are not relevant (use fab events instead)
        let filtered = (_planningViewMode === 'finitions' || _planningViewMode === 'livraison') ? [] : deliveryList;
        if (_planningViewMode === 'machine' && (machineFilters.length > 0 || operatorFilter)) {
          const withFiche = await Promise.all(filtered.map(async x => {
            try {
              const fiche = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fnKey(x.fullPath || '')), {
                headers: { 'Authorization': `Bearer ${authToken}` }
              }).then(r => r.json());
              return { x, machine: fiche?.moteurImpression || '', operateur: fiche?.operateur || '' };
            } catch(e) { return { x, machine: '', operateur: '' }; }
          }));
          filtered = withFiche
            .filter(wm => (machineFilters.length === 0 || machineFilters.includes(wm.machine)) && (!operatorFilter || wm.operateur === operatorFilter))
            .map(wm => wm.x);
        }

        const ev = filtered.flatMap(x => {
          if (!x.date) return []; // skip entries with no date to prevent instability
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
          if (isNaN(startDate.getTime())) return []; // skip invalid dates
          return [{
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
          }];
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
        // Use event.start (JS Date) to extract local date/time — avoids timezone offset issues
        // that can occur when parsing startStr (e.g. UTC date ≠ local date near midnight).
        const startLocal = info.event.start;
        const newDate = startLocal.toLocaleDateString('sv-SE'); // always YYYY-MM-DD in local TZ
        const newTime = String(startLocal.getHours()).padStart(2, '0') + ':' + String(startLocal.getMinutes()).padStart(2, '0');

        if (info.event.extendedProps.isFabEvent) {
          // Save manual time for fabrication key-date event
          const fabType = info.event.extendedProps.fabType;
          const fileName = info.event.extendedProps.fabFileName || fk;

          // Livraison view: reception events need popup + key-date update (not event-time)
          if (fabType === 'reception') {
            const confirmed = await _showLivraisonConfirmPopup(newDate);
            if (confirmed === null) { info.revert(); return; }

            // Always update dateReceptionSouhaitee
            const r = await fetch("/api/fabrication/key-date", {
              method: "PUT",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fileName, field: "dateReceptionSouhaitee", date: newDate, time: newTime })
            }).then(r => r.json());
            if (!r.ok) throw new Error(r.error || "Erreur");

            if (confirmed === true) {
              // Propagate to other key dates by shifting them the same delta
              const MS_PER_DAY = 86400000;
              const oldEventStart = info.oldEvent && info.oldEvent.start;
              const oldDate = oldEventStart ? oldEventStart.toLocaleDateString('sv-SE') : newDate;
              const deltaDays = Math.round((new Date(newDate) - new Date(oldDate)) / MS_PER_DAY);
              if (deltaDays !== 0) {
                const fab = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fileName), {
                  headers: { 'Authorization': `Bearer ${authToken}` }
                }).then(r => r.json()).catch(() => ({}));
                for (const field of ['dateEnvoi', 'dateImpression', 'dateProductionFinitions']) {
                  const existing = fab && fab[field] ? fab[field] : null;
                  if (existing) {
                    try {
                      const shifted = new Date(new Date(existing).getTime() + deltaDays * MS_PER_DAY);
                      const shiftedDate = shifted.toLocaleDateString('sv-SE');
                      await fetch("/api/fabrication/key-date", {
                        method: "PUT",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ fileName, field, date: shiftedDate, time: newTime })
                      }).catch(() => {});
                    } catch(e) { /* ignore */ }
                  }
                }
              }
            }

            showNotification(`✅ Planning livraison mis à jour pour le ${newDate}`, "success");
            return;
          }

          const viewTypeMap = { envoi: 'global', impression: 'machine', finitions: 'finitions' };
          const viewType = viewTypeMap[fabType];
          if (!viewType) { info.revert(); return; }
          const r = await fetch("/api/fabrication/event-time", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fileName, viewType, newDate, newTime })
          }).then(r => r.json());
          if (!r.ok) throw new Error(r.error || "Erreur");

          // Cascade: moving impression should also move finition by the same delta
          let cascadeOk = true;
          if (fabType === 'impression' && info.oldEvent && info.oldEvent.start) {
            const MS_PER_DAY = 86400000;
            const oldDate = info.oldEvent.start.toLocaleDateString('sv-SE');
            const deltaDays = Math.round((new Date(newDate) - new Date(oldDate)) / MS_PER_DAY);
            if (deltaDays !== 0) {
              const fab = await fetch('/api/fabrication?fileName=' + encodeURIComponent(fileName), {
                headers: { 'Authorization': `Bearer ${authToken}` }
              }).then(r2 => r2.json()).catch(() => ({}));
              if (fab && fab.dateProductionFinitions) {
                try {
                  const shifted = new Date(new Date(fab.dateProductionFinitions).getTime() + deltaDays * MS_PER_DAY);
                  const shiftedDate = shifted.toLocaleDateString('sv-SE');
                  const cr = await fetch("/api/fabrication/key-date", {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ fileName, field: "dateProductionFinitions", date: shiftedDate, time: newTime })
                  }).then(r2 => r2.json());
                  if (!cr.ok) { console.error('[Calendar] Cascade finition failed:', cr.error); cascadeOk = false; }
                } catch(e) { console.error('[Calendar] Cascade finition error:', e); cascadeOk = false; }
              }
            }
          }

          if (cascadeOk) showNotification(`✅ Planning mis à jour : ${newTime}`, "success");
          else showNotification(`⚠️ Impression déplacée mais la mise à jour de la finition a échoué`, "error");
        } else {
          // For livraison view: show a confirmation popup asking whether to also update linked plannings
          if (_planningViewMode === 'livraison') {
            const confirmed = await _showLivraisonConfirmPopup(newDate);
            if (confirmed === null) { info.revert(); return; } // user cancelled

            const r = await fetch("/api/delivery", {
              method: "PUT",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
            }).then(r => r.json());
            if (!r.ok) throw new Error(r.error || "Erreur");

            deliveriesByPath[fk] = newDate;
            deliveriesByPath[fk + "_time"] = newTime;

            if (confirmed === true) {
              // "Oui" — also update dateReceptionSouhaitee (and via fabrication key-date endpoint any linked dates)
              await fetch("/api/fabrication/key-date", {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ fileName: fk, field: "dateReceptionSouhaitee", date: newDate, time: newTime })
              }).catch(() => {});
            }

            const { bg, bc, tc } = colorForEvent(fullPath, newDate);
            info.event.setProp("backgroundColor", bg);
            info.event.setProp("borderColor", bc);
            info.event.setProp("textColor", tc);

            showNotification(`✅ Planning livraison mis à jour pour le ${newDate}`, "success");
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
  wrap.style.cssText = "margin-bottom:8px;display:none;align-items:flex-start;gap:16px;flex-wrap:wrap;";
  wrap.innerHTML = `
    <div style="display:flex;align-items:flex-start;gap:6px;flex-direction:column;">
      <label style="font-size:13px;font-weight:500;">Moteur(s) :</label>
      <div id="planning-machine-pills" style="display:flex;flex-wrap:wrap;gap:6px;"></div>
    </div>
    <div style="display:flex;align-items:flex-start;gap:6px;flex-direction:column;">
      <label style="font-size:13px;font-weight:500;">Opérateur :</label>
      <select id="planning-operator-filter" class="settings-input" style="font-size:13px;padding:4px 8px;min-width:160px;"></select>
    </div>`;
  calendarEl.parentNode?.insertBefore(wrap, calendarEl);

  // Also build finitions filters (hidden until finitions view is active)
  const existingFin = document.getElementById("planning-finitions-filter-wrap");
  if (existingFin) existingFin.remove();
  const finWrap = document.createElement("div");
  finWrap.id = "planning-finitions-filter-wrap";
  finWrap.style.cssText = "margin-bottom:8px;display:none;align-items:flex-start;gap:16px;flex-wrap:wrap;";
  finWrap.innerHTML = `
    <div style="display:flex;align-items:flex-start;gap:6px;flex-direction:column;">
      <label style="font-size:13px;font-weight:500;">Opérateur :</label>
      <select id="planning-finitions-operator-filter" class="settings-input" style="font-size:13px;padding:4px 8px;min-width:160px;"></select>
    </div>
    <div style="display:flex;align-items:flex-start;gap:6px;flex-direction:column;">
      <label style="font-size:13px;font-weight:500;">Type de finition :</label>
      <div id="planning-finitions-pills" style="display:flex;flex-wrap:wrap;gap:6px;"></div>
    </div>`;
  calendarEl.parentNode?.insertBefore(finWrap, calendarEl);

  /** Build clickable pill buttons for multi-selection */
  function buildPills(containerId, values, onToggle) {
    const container = document.getElementById(containerId);
    if (!container) return;
    container.innerHTML = "";
    if (values.length === 0) {
      container.innerHTML = '<span style="font-size:12px;color:#9ca3af;">Aucun élément</span>';
      return;
    }
    values.forEach(val => {
      const pill = document.createElement("button");
      pill.type = "button";
      pill.className = "planning-engine-pill";
      pill.dataset.value = val;
      pill.dataset.selected = "false";
      pill.textContent = val;
      pill.style.cssText = "font-size:12px;font-weight:600;padding:5px 13px;border-radius:20px;border:2px solid #d1d5db;background:white;color:#374151;cursor:pointer;transition:all 0.15s;white-space:nowrap;";
      pill.onclick = () => {
        const isSelected = pill.dataset.selected === "true";
        pill.dataset.selected = isSelected ? "false" : "true";
        pill.style.cssText = `font-size:12px;font-weight:600;padding:5px 13px;border-radius:20px;border:2px solid ${isSelected ? '#d1d5db' : '#2563eb'};background:${isSelected ? 'white' : '#2563eb'};color:${isSelected ? '#374151' : '#fff'};cursor:pointer;transition:all 0.15s;white-space:nowrap;`;
        onToggle();
      };
      container.appendChild(pill);
    });
  }

  try {
    const [engines, usersResp, faconnageOpts] = await Promise.all([
      fetch("/api/config/print-engines").then(r => r.json()).catch(() => []),
      fetch("/api/auth/users", { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ ok: false, users: [] })),
      fetch("/api/settings/faconnage-options", { headers: { 'Authorization': `Bearer ${authToken}` } }).then(r => r.json()).catch(() => [])
    ]);

    // Machine pills
    const engineNames = Array.isArray(engines)
      ? engines.map(e => typeof e === 'object' ? (e.name || '') : String(e || '')).filter(Boolean)
      : [];
    buildPills("planning-machine-pills", engineNames, () => calendar?.refetchEvents());

    const userList = (usersResp.ok && Array.isArray(usersResp.users)) ? usersResp.users : [];
    const opSel = wrap.querySelector("#planning-operator-filter");
    opSel.innerHTML = '<option value="">Tous opérateurs</option>' + userList.map(u => {
      const name = u.name || u.login || '';
      return `<option value="${name}">${name}</option>`;
    }).join('');
    opSel.onchange = () => calendar?.refetchEvents();

    // Finitions operator filter
    const finOpSel = finWrap.querySelector("#planning-finitions-operator-filter");
    finOpSel.innerHTML = '<option value="">Tous opérateurs</option>' + userList.map(u => {
      const name = u.name || u.login || '';
      return `<option value="${name}">${name}</option>`;
    }).join('');
    finOpSel.onchange = () => calendar?.refetchEvents();

    // Finitions type pills
    const finTypes = ['Embellissement','Rainage','Pliage','Façonnage','Coupe','Emballage','Départ','Livraison'];
    if (Array.isArray(faconnageOpts)) faconnageOpts.forEach(o => { if (!finTypes.includes(o)) finTypes.push(o); });
    buildPills("planning-finitions-pills", finTypes, () => calendar?.refetchEvents());
  } catch(e) { /* ignore */ }
}

// ======================================================
// CALENDRIER SOUMISSION (Profil 1)
// ======================================================

export async function initSubmissionCalendar() {
  const calendarEl = document.getElementById("submissionCalendar");
  if (!calendarEl || submissionCalendar) return;

  // Load planning color config (admin-configurable)
  let _planningColors = { engines: {}, finitions: {} };
  try {
    const cr = await fetch("/api/settings/planning-colors").then(r => r.json()).catch(() => ({ ok: false }));
    if (cr.ok && cr.colors) _planningColors = cr.colors;
  } catch(e) { /* use defaults */ }

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
    eventAllow: (_dropInfo, draggedEvent) => !isReadOnlyProfile && !draggedEvent.extendedProps?.locked,
    events: async (info, success) => {
      try {
        // Load fabrication events of type "reception" (dateReceptionSouhaitee)
        const fabEventsResp = await fetch("/api/fabrication/events", { headers: { 'Authorization': `Bearer ${authToken}` } })
          .then(r => r.json()).catch(() => ({ ok: false, events: [] }));
        const fabEvents = (fabEventsResp.ok && Array.isArray(fabEventsResp.events)) ? fabEventsResp.events : [];
        const receptionEvents = fabEvents.filter(fe => fe.type === 'reception');

        // Track which files are covered by reception fab events
        const fabFileNames = new Set(receptionEvents.map(fe => fnKey(fe.fileName || '')));

        // Also load delivery list for jobs that don't have dateReceptionSouhaitee
        const list = await fetch("/api/delivery").then(r => r.json()).catch(() => []);

        const ev = [];

        // First, add fabrication reception events
        for (const fe of receptionEvents) {
          if (!fe.date) continue;
          const isLocked = !!fe.locked;
          const bg = isLocked ? '#22c55e' : '#3b82f6';
          const bc = isLocked ? '#16a34a' : '#2563eb';
          const tc = '#ffffff';
          const timeStr = fe.manualTime || "09:00";
          ev.push({
            title: (isLocked ? '🔒 ' : '') + fe.title,
            start: `${fe.date}T${timeStr}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            editable: !isLocked,
            startEditable: !isLocked,
            durationEditable: false,
            extendedProps: { fullPath: fe.fullPath, isFabEvent: true, fabType: 'reception', fabFileName: fe.fileName, locked: isLocked, bg, bc, tc }
          });
        }

        // Add delivery events that aren't covered by fab reception events
        for (const x of list) {
          const fk = fnKey(x.fullPath || x.fileName || '');
          if (fabFileNames.has(fk)) continue; // already covered by fab reception event
          const eventDate = x.dateReceptionSouhaitee || x.date;
          if (!eventDate) continue;
          const full = normalizePath(x.fullPath);
          const inFinProd = full.toLowerCase().includes(FIN_PROD_FOLDER.toLowerCase());
          const locked = !!x.locked || inFinProd;
          const bg = locked ? "#22c55e" : colorForEvent(full, eventDate).bg;
          const bc = locked ? "#16a34a" : colorForEvent(full, eventDate).bc;
          const tc = locked ? "#ffffff" : colorForEvent(full, eventDate).tc;
          const time = x.time || "09:00";
          ev.push({
            title: (locked ? "🔒 " : "") + x.fileName,
            start: `${eventDate}T${time}:00`,
            allDay: false,
            backgroundColor: bg,
            borderColor: bc,
            textColor: tc,
            editable: !locked,
            startEditable: !locked,
            durationEditable: false,
            extendedProps: { fullPath: full, bg, bc, tc, date: x.date, time: time, locked }
          });
        }

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
        // Use event.start (JS Date) to extract local date/time — avoids timezone issues near midnight.
        const startLocal = info.event.start;
        const newDate = startLocal.toLocaleDateString('sv-SE'); // YYYY-MM-DD in local TZ
        const newTime = String(startLocal.getHours()).padStart(2, '0') + ':' + String(startLocal.getMinutes()).padStart(2, '0');

        if (info.event.extendedProps.isFabEvent) {
          // Update dateReceptionSouhaitee in the fabrication record
          const fileName = info.event.extendedProps.fabFileName || fk;
          const r = await fetch("/api/fabrication/key-date", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fileName, field: "dateReceptionSouhaitee", date: newDate, time: newTime })
          }).then(r => r.json());
          if (!r.ok) throw new Error(r.error || "Erreur");
        } else {
          // Update delivery date AND dateReceptionSouhaitee
          const r = await fetch("/api/delivery", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath, fileName: fk, date: newDate, time: newTime })
          }).then(r => r.json());
          if (!r.ok) throw new Error(r.error || "Erreur");

          // Also update dateReceptionSouhaitee in fabrication
          await fetch("/api/fabrication/key-date", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fileName: fk, field: "dateReceptionSouhaitee", date: newDate, time: newTime })
          }).catch(() => {});

          deliveriesByPath[fk] = newDate;
          deliveriesByPath[fk + "_time"] = newTime;
        }

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
