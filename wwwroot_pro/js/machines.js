// machines.js — Suivi temps réel des machines (point 3)
import { authToken, showNotification, esc } from './core.js';

let _pollTimer = null;
let _statuses = ["Disponible", "En impression", "En attente", "En panne", "Maintenance"];

const STATUT_STYLE = {
  "Disponible":     { bg: "#dcfce7", bc: "#16a34a", dot: "#16a34a", label: "🟢 Disponible" },
  "En impression":  { bg: "#dbeafe", bc: "#2563eb", dot: "#2563eb", label: "🖨️ En impression" },
  "En attente":     { bg: "#fef3c7", bc: "#d97706", dot: "#d97706", label: "⏸️ En attente" },
  "En panne":       { bg: "#fee2e2", bc: "#dc2626", dot: "#dc2626", label: "🛑 En panne" },
  "Maintenance":    { bg: "#f3e8ff", bc: "#7c3aed", dot: "#7c3aed", label: "🔧 Maintenance" }
};

export async function initMachinesView() {
  const container = document.getElementById("machines-view");
  if (!container) return;
  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;flex-wrap:wrap;gap:8px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);">🖨️ Suivi des machines</h2>
        <div style="display:flex;align-items:center;gap:10px;">
          <span style="font-size:12px;color:#6b7280;">🔄 Actualisation automatique</span>
          <button id="machines-refresh" class="btn btn-primary" style="border-radius:50px;">↻ Rafraîchir</button>
        </div>
      </div>
      <div id="machines-grid" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:14px;">
        <p style="color:#6b7280;">Chargement…</p>
      </div>
    </div>`;

  const refreshBtn = document.getElementById("machines-refresh");
  if (refreshBtn) refreshBtn.onclick = () => loadMachines(true);

  await loadMachines(true);

  // Polling temps réel (n'écrase pas une carte en cours d'édition).
  if (_pollTimer) clearInterval(_pollTimer);
  _pollTimer = setInterval(() => {
    const view = document.getElementById("machines-view");
    if (!view || view.classList.contains("hidden")) { clearInterval(_pollTimer); _pollTimer = null; return; }
    const active = document.activeElement;
    if (active && view.contains(active) && (active.tagName === "INPUT" || active.tagName === "SELECT" || active.tagName === "TEXTAREA")) return;
    loadMachines(false);
  }, 8000);
}

async function loadMachines(showSpinner) {
  const grid = document.getElementById("machines-grid");
  if (!grid) return;
  if (showSpinner) grid.innerHTML = '<p style="color:#6b7280;">Chargement…</p>';
  try {
    const resp = await fetch("/api/machines/status", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (!resp.ok) { grid.innerHTML = `<p style="color:#dc2626;">${esc(resp.error || "Erreur")}</p>`; return; }
    if (Array.isArray(resp.statuses)) _statuses = resp.statuses;
    renderGrid(resp.machines || []);
  } catch (e) {
    grid.innerHTML = `<p style="color:#dc2626;">Erreur de chargement</p>`;
  }
}

function renderGrid(machines) {
  const grid = document.getElementById("machines-grid");
  if (!grid) return;
  if (machines.length === 0) {
    grid.innerHTML = '<p style="color:#9ca3af;">Aucune machine. Ajoutez des moteurs d\'impression dans les réglages.</p>';
    return;
  }
  grid.innerHTML = machines.map(m => renderCard(m)).join("");
  machines.forEach(m => attachCard(m.moteur));
}

function _fmtRemaining(min) {
  min = Math.round(min || 0);
  if (min <= 0) return "—";
  if (min < 60) return `${min} min`;
  const h = Math.floor(min / 60), r = min % 60;
  return r ? `${h} h ${r}` : `${h} h`;
}

function renderCard(m) {
  const st = STATUT_STYLE[m.statut] || STATUT_STYLE["Disponible"];
  const key = _cardKey(m.moteur);
  const optionHtml = _statuses.map(s => `<option value="${esc(s)}" ${s === m.statut ? "selected" : ""}>${esc(s)}</option>`).join("");
  const maj = m.updatedAt ? new Date(m.updatedAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : "—";
  const isPrinting = m.statut === "En impression";
  return `
    <div id="card-${key}" style="border:1px solid ${st.bc};border-radius:12px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.06);background:#fff;">
      <div style="background:${st.bg};padding:10px 14px;display:flex;align-items:center;justify-content:space-between;gap:8px;">
        <div style="font-size:15px;font-weight:700;color:#111827;display:flex;align-items:center;gap:8px;">
          <span style="width:10px;height:10px;border-radius:50%;background:${st.dot};display:inline-block;"></span>
          ${esc(m.moteur)}
        </div>
        <span style="font-size:12px;font-weight:700;color:${st.bc};">${st.label}</span>
      </div>
      <div style="padding:12px 14px;display:flex;flex-direction:column;gap:9px;">
        <label style="font-size:12px;color:#374151;">Statut
          <select data-k="statut" class="settings-input" style="width:100%;margin-top:3px;font-size:13px;">${optionHtml}</select>
        </label>
        <label style="font-size:12px;color:#374151;">Papier chargé
          <input data-k="papierCharge" class="settings-input" value="${esc(m.papierCharge || '')}" placeholder="Ex : Couché 350g" style="width:100%;margin-top:3px;font-size:13px;" />
        </label>
        <div style="display:flex;gap:8px;">
          <label style="font-size:12px;color:#374151;flex:1;">Compteur feuilles
            <input data-k="compteurFeuilles" type="number" min="0" class="settings-input" value="${m.compteurFeuilles || 0}" style="width:100%;margin-top:3px;font-size:13px;" />
          </label>
          <label style="font-size:12px;color:#374151;flex:1;">OF en cours (fichier)
            <input data-k="ofEnCours" class="settings-input" value="${esc(m.ofEnCours || '')}" placeholder="ex : 1007.pdf" style="width:100%;margin-top:3px;font-size:13px;" />
          </label>
        </div>
        <div style="display:flex;justify-content:space-between;align-items:center;font-size:12px;color:#6b7280;background:#f9fafb;border-radius:8px;padding:6px 10px;">
          <span>${m.ofEnCoursDossier ? '📄 OF #' + esc(m.ofEnCoursDossier) : '📄 —'}</span>
          <span>⏳ Temps restant : <strong style="color:${isPrinting ? '#2563eb' : '#374151'};">${_fmtRemaining(m.tempsRestantMinutes)}</strong></span>
        </div>
        <label style="font-size:12px;color:#374151;">Note
          <input data-k="note" class="settings-input" value="${esc(m.note || '')}" placeholder="Commentaire…" style="width:100%;margin-top:3px;font-size:13px;" />
        </label>
        <div style="display:flex;justify-content:space-between;align-items:center;margin-top:2px;">
          <span style="font-size:11px;color:#9ca3af;">MàJ : ${esc(maj)}${m.updatedBy ? ' · ' + esc(m.updatedBy) : ''}</span>
          <button data-save="${key}" class="btn btn-primary" style="font-size:12px;padding:5px 14px;border-radius:8px;">💾 Enregistrer</button>
        </div>
      </div>
    </div>`;
}

function _cardKey(moteur) {
  return "m_" + btoa(unescape(encodeURIComponent(moteur))).replace(/[^a-zA-Z0-9]/g, "");
}

function attachCard(moteur) {
  const key = _cardKey(moteur);
  const card = document.getElementById("card-" + key);
  if (!card) return;
  const saveBtn = card.querySelector(`[data-save="${key}"]`);
  if (saveBtn) saveBtn.onclick = async () => {
    const payload = { moteur };
    card.querySelectorAll("[data-k]").forEach(el => {
      const k = el.dataset.k;
      if (k === "compteurFeuilles") payload[k] = parseInt(el.value) || 0;
      else payload[k] = el.value;
    });
    saveBtn.disabled = true; saveBtn.textContent = "⏳";
    const r = await fetch("/api/machines/status", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(payload)
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { showNotification(`✅ ${moteur} mis à jour`, "success"); loadMachines(false); }
    else { saveBtn.disabled = false; saveBtn.textContent = "💾 Enregistrer"; showNotification(`❌ ${r.error || "Échec"}`, "error"); }
  };
}
