// machines.js — Suivi temps réel des machines (point 3) + pilotage / connexion (point 8)
import { authToken, showNotification, esc, currentUser } from './core.js';

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
  machines.forEach(m => attachCard(m));
}

function _connBadge(m) {
  const proto = (m.protocol || 'manual').toUpperCase();
  const state = m.connectionState || 'manual';
  if (state === 'online')
    return `<span style="font-size:11px;font-weight:700;color:#16a34a;">🔌 En ligne</span> <span style="font-size:11px;color:#9ca3af;">${esc(proto)}${m.lastTelemetryAt ? ' · vu ' + _ago(m.lastTelemetryAt) : ''}</span>`;
  if (state === 'offline')
    return `<span style="font-size:11px;font-weight:700;color:#dc2626;">⚪ Hors ligne</span> <span style="font-size:11px;color:#9ca3af;">${esc(proto)}</span>`;
  return `<span style="font-size:11px;font-weight:600;color:#6b7280;">✋ Manuel</span>`;
}

function _ago(iso) {
  const s = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
  if (s < 60) return `il y a ${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `il y a ${m} min`;
  return `il y a ${Math.floor(m / 60)} h`;
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
      <div style="padding:6px 14px;background:#fafafa;border-bottom:1px solid #f1f5f9;display:flex;justify-content:space-between;align-items:center;gap:8px;">
        ${_connBadge(m)}
        ${currentUser && currentUser.profile === 3 ? `<button data-conn="${key}" style="font-size:11px;padding:3px 9px;border:1px solid #d1d5db;background:#fff;border-radius:6px;cursor:pointer;">🔌 Connexion</button>` : ''}
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

function attachCard(m) {
  const moteur = m.moteur;
  const key = _cardKey(moteur);
  const card = document.getElementById("card-" + key);
  if (!card) return;
  const connBtn = card.querySelector(`[data-conn="${key}"]`);
  if (connBtn) connBtn.onclick = () => openConnectionModal(m);
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

async function openConnectionModal(m) {
  const moteur = m.moteur;
  // Charge la config connexion + le token machine (admin).
  let conn = { protocol: 'manual', address: '', pollIntervalSec: 30, enabled: false };
  let token = '';
  try {
    const [cr, tr] = await Promise.all([
      fetch("/api/config/machine-connections", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ ok: false })),
      fetch("/api/config/machine-token", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ ok: false }))
    ]);
    if (cr.ok && Array.isArray(cr.connections)) {
      const found = cr.connections.find(c => c.moteur === moteur);
      if (found) conn = found;
    }
    if (tr.ok) token = tr.token || '';
  } catch (e) { /* defaults */ }

  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000;padding:20px;";
  const panel = document.createElement("div");
  panel.style.cssText = "background:#fff;border-radius:12px;box-shadow:0 10px 40px rgba(0,0,0,.3);max-width:560px;width:100%;max-height:90vh;overflow-y:auto;";
  const base = location.origin;
  panel.innerHTML = `
    <div style="padding:18px 22px;border-bottom:1px solid #eee;"><h3 style="margin:0;font-size:16px;font-weight:700;color:#1e3a5f;">🔌 Connexion — ${esc(moteur)}</h3></div>
    <div style="padding:16px 22px;display:flex;flex-direction:column;gap:12px;">
      <label style="font-size:12px;color:#374151;">Mode de connexion
        <select id="mc-proto" class="settings-input" style="width:100%;margin-top:3px;">
          <option value="manual" ${conn.protocol === 'manual' ? 'selected' : ''}>Manuel (saisie opérateur)</option>
          <option value="push" ${conn.protocol === 'push' ? 'selected' : ''}>Push (l'agent / passerelle envoie la télémétrie)</option>
          <option value="http" ${conn.protocol === 'http' ? 'selected' : ''}>HTTP (le serveur interroge une URL de statut)</option>
        </select>
      </label>
      <div id="mc-http-block" style="display:${conn.protocol === 'http' ? 'flex' : 'none'};flex-direction:column;gap:10px;">
        <label style="font-size:12px;color:#374151;">URL de statut (JSON)
          <input id="mc-address" class="settings-input" value="${esc(conn.address || '')}" placeholder="http://192.168.1.50/status" style="width:100%;margin-top:3px;" />
        </label>
        <label style="font-size:12px;color:#374151;">Intervalle d'interrogation (s)
          <input id="mc-interval" type="number" min="5" max="3600" class="settings-input" value="${conn.pollIntervalSec || 30}" style="width:120px;margin-top:3px;" />
        </label>
      </div>
      <div id="mc-push-block" style="display:${conn.protocol === 'push' ? 'block' : 'none'};background:#f8fafc;border:1px solid #e5e7eb;border-radius:8px;padding:10px 12px;">
        <div style="font-size:12px;font-weight:600;color:#374151;margin-bottom:6px;">L'agent/passerelle envoie la télémétrie :</div>
        <pre style="font-size:11px;background:#0f172a;color:#e2e8f0;border-radius:6px;padding:10px;overflow-x:auto;white-space:pre-wrap;">POST ${esc(base)}/api/machines/telemetry
X-Machine-Token: ${esc(token || '—')}
{ "moteur": "${esc(moteur)}", "statut": "En impression", "compteurFeuilles": 12345, "ofEnCours": "1007.pdf" }</pre>
      </div>
      <label style="font-size:13px;color:#374151;display:flex;align-items:center;gap:8px;">
        <input type="checkbox" id="mc-enabled" ${conn.enabled ? 'checked' : ''}/> Connexion active
      </label>
      <div style="font-size:11px;color:#9ca3af;">SNMP / JMF (Canon PRISMAsync, EFI Fiery) : à brancher via un connecteur qui pousse sur ce même point d'ingestion.</div>
    </div>
    <div style="padding:14px 22px;border-top:1px solid #eee;display:flex;justify-content:flex-end;gap:10px;">
      <button id="mc-cancel" class="btn" style="border-radius:8px;">Annuler</button>
      <button id="mc-save" class="btn btn-primary" style="border-radius:8px;">Enregistrer</button>
    </div>`;
  overlay.appendChild(panel); document.body.appendChild(overlay);
  const close = () => overlay.remove();
  overlay.onclick = e => { if (e.target === overlay) close(); };
  panel.querySelector("#mc-cancel").onclick = close;
  panel.querySelector("#mc-proto").onchange = (e) => {
    panel.querySelector("#mc-http-block").style.display = e.target.value === 'http' ? 'flex' : 'none';
    panel.querySelector("#mc-push-block").style.display = e.target.value === 'push' ? 'block' : 'none';
  };
  panel.querySelector("#mc-save").onclick = async () => {
    const payload = {
      moteur,
      protocol: panel.querySelector("#mc-proto").value,
      address: panel.querySelector("#mc-address").value.trim(),
      pollIntervalSec: parseInt(panel.querySelector("#mc-interval").value) || 30,
      enabled: panel.querySelector("#mc-enabled").checked
    };
    const r = await fetch("/api/config/machine-connections", {
      method: "PUT", headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(payload)
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { close(); showNotification("✅ Connexion enregistrée", "success"); loadMachines(false); }
    else showNotification(`❌ ${r.error || "Échec"}`, "error");
  };
}
