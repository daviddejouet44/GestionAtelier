// stock.js — Gestion des stocks (point 7)
import { authToken, showNotification, esc, currentUser } from './core.js';

const CAT_LABEL = {
  papier: "📄 Papiers", encre: "🎨 Encres", plaque: "🟫 Plaques",
  carton: "📦 Cartons", consommable: "🧰 Consommables"
};
const STATUS_STYLE = {
  ok:      { bg: "#dcfce7", fg: "#16a34a", label: "🟢 OK" },
  bas:     { bg: "#fef3c7", fg: "#b45309", label: "🟠 Bas" },
  rupture: { bg: "#fee2e2", fg: "#dc2626", label: "🔴 Rupture" }
};

export async function initStockView() {
  const container = document.getElementById("stock-view");
  if (!container) return;
  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px;flex-wrap:wrap;gap:8px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);">📦 Gestion des stocks</h2>
        <button id="stock-add-btn" class="btn btn-primary" style="border-radius:50px;">＋ Ajouter un article</button>
      </div>
      <div id="stock-alerts"></div>
      <div id="stock-add-form"></div>
      <div id="stock-body"><p style="color:#6b7280;">Chargement…</p></div>
    </div>`;
  document.getElementById("stock-add-btn").onclick = () => toggleAddForm();
  await loadStock();
}

async function loadStock() {
  const body = document.getElementById("stock-body");
  if (!body) return;
  let data;
  try {
    data = await fetch("/api/stock", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
  } catch (e) { body.innerHTML = '<p style="color:#dc2626;">Erreur de chargement</p>'; return; }
  if (!data.ok) { body.innerHTML = `<p style="color:#dc2626;">${esc(data.error || "Erreur")}</p>`; return; }

  renderAlerts(data.items || []);
  renderBody(data.items || [], data.categories || []);
}

function renderAlerts(items) {
  const el = document.getElementById("stock-alerts");
  if (!el) return;
  const rupture = items.filter(i => i.status === "rupture");
  const bas = items.filter(i => i.status === "bas");
  if (rupture.length === 0 && bas.length === 0) {
    el.innerHTML = '<div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:8px 12px;font-size:13px;color:#15803d;margin-bottom:12px;">🟢 Aucun article en rupture ni sous le seuil.</div>';
    return;
  }
  el.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:10px 12px;font-size:13px;color:#991b1b;margin-bottom:12px;">
    ⚠️ <strong>Alertes de stock</strong> —
    ${rupture.length ? `<strong>${rupture.length}</strong> en rupture (${rupture.map(i => esc(i.name)).join(', ')})` : ''}
    ${rupture.length && bas.length ? ' ; ' : ''}
    ${bas.length ? `<strong>${bas.length}</strong> sous le seuil (${bas.map(i => esc(i.name)).join(', ')})` : ''}
  </div>`;
}

function renderBody(items, categories) {
  const body = document.getElementById("stock-body");
  if (!body) return;
  if (items.length === 0) {
    body.innerHTML = '<p style="color:#9ca3af;padding:20px 0;">Aucun article. Cliquez sur « ＋ Ajouter un article ».</p>';
    return;
  }
  const isAdmin = currentUser && currentUser.profile === 3;
  const order = categories.length ? categories : ["papier", "encre", "plaque", "carton", "consommable"];
  let html = "";
  for (const cat of order) {
    const rows = items.filter(i => i.category === cat);
    if (rows.length === 0) continue;
    html += `<div style="margin-bottom:18px;">
      <h3 style="font-size:15px;font-weight:700;color:#1e3a5f;margin:0 0 8px;">${CAT_LABEL[cat] || esc(cat)} <span style="font-weight:500;color:#9ca3af;font-size:12px;">(${rows.length})</span></h3>
      <div style="overflow-x:auto;"><table style="width:100%;border-collapse:collapse;font-size:13px;">
        <thead><tr style="text-align:left;color:#6b7280;border-bottom:1px solid #e5e7eb;">
          <th style="padding:6px 8px;">Article</th><th style="padding:6px 8px;">Quantité</th>
          <th style="padding:6px 8px;">Seuil</th><th style="padding:6px 8px;">Statut</th>
          <th style="padding:6px 8px;">Fournisseur</th><th style="padding:6px 8px;text-align:right;">Actions</th>
        </tr></thead><tbody>`;
    for (const i of rows) {
      const st = STATUS_STYLE[i.status] || STATUS_STYLE.ok;
      html += `<tr data-id="${esc(i.id)}" style="border-bottom:1px solid #f1f5f9;">
        <td style="padding:7px 8px;font-weight:600;color:#111827;">${esc(i.name)}${i.reference ? ` <span style="font-weight:400;color:#9ca3af;">(${esc(i.reference)})</span>` : ''}</td>
        <td style="padding:7px 8px;"><strong>${_num(i.quantity)}</strong> ${esc(i.unit || '')}</td>
        <td style="padding:7px 8px;color:#6b7280;">${_num(i.minThreshold)}</td>
        <td style="padding:7px 8px;"><span style="background:${st.bg};color:${st.fg};font-weight:700;font-size:11px;border-radius:10px;padding:2px 9px;white-space:nowrap;">${st.label}</span></td>
        <td style="padding:7px 8px;color:#6b7280;">${esc(i.supplier || '—')}</td>
        <td style="padding:7px 8px;text-align:right;white-space:nowrap;">
          <button class="stock-mv" data-id="${esc(i.id)}" data-type="entree" title="Entrée de stock" style="border:1px solid #16a34a;color:#16a34a;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;font-weight:700;">＋</button>
          <button class="stock-mv" data-id="${esc(i.id)}" data-type="sortie" title="Sortie de stock" style="border:1px solid #dc2626;color:#dc2626;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;font-weight:700;">－</button>
          <button class="stock-edit" data-id="${esc(i.id)}" title="Modifier" style="border:1px solid #d1d5db;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;">✏️</button>
          ${isAdmin ? `<button class="stock-del" data-id="${esc(i.id)}" title="Supprimer" style="border:1px solid #d1d5db;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;">🗑️</button>` : ''}
        </td>
      </tr>`;
    }
    html += `</tbody></table></div></div>`;
  }
  body.innerHTML = html;

  const dataById = Object.fromEntries(items.map(i => [i.id, i]));
  body.querySelectorAll(".stock-mv").forEach(b => b.onclick = () => openMovementModal(dataById[b.dataset.id], b.dataset.type));
  body.querySelectorAll(".stock-edit").forEach(b => b.onclick = () => openItemModal(dataById[b.dataset.id]));
  body.querySelectorAll(".stock-del").forEach(b => b.onclick = () => deleteItem(dataById[b.dataset.id]));
}

function _num(n) {
  const v = Number(n || 0);
  return Number.isInteger(v) ? String(v) : v.toFixed(1);
}

function toggleAddForm() {
  const el = document.getElementById("stock-add-form");
  if (!el) return;
  if (el.innerHTML) { el.innerHTML = ""; return; }
  openItemModal(null); // création via modale
}

// Modale création / édition d'article
function openItemModal(item) {
  const isEdit = !!item;
  const cats = ["papier", "encre", "plaque", "carton", "consommable"];
  const overlay = _overlay();
  const panel = _panel("520px");
  panel.innerHTML = `
    <div style="padding:18px 22px;border-bottom:1px solid #eee;"><h3 style="margin:0;font-size:16px;font-weight:700;color:#1e3a5f;">${isEdit ? "✏️ Modifier l'article" : "＋ Nouvel article"}</h3></div>
    <div style="padding:16px 22px;display:flex;flex-direction:column;gap:10px;">
      <label style="font-size:12px;color:#374151;">Nom<input id="si-name" class="settings-input" value="${esc(item?.name || '')}" style="width:100%;margin-top:3px;" /></label>
      <div style="display:flex;gap:10px;">
        <label style="font-size:12px;color:#374151;flex:1;">Catégorie
          <select id="si-cat" class="settings-input" style="width:100%;margin-top:3px;">${cats.map(c => `<option value="${c}" ${item?.category === c ? 'selected' : ''}>${CAT_LABEL[c].replace(/^\S+\s/,'')}</option>`).join('')}</select>
        </label>
        <label style="font-size:12px;color:#374151;flex:1;">Unité<input id="si-unit" class="settings-input" value="${esc(item?.unit || '')}" placeholder="feuilles, kg, L…" style="width:100%;margin-top:3px;" /></label>
      </div>
      <div style="display:flex;gap:10px;">
        ${isEdit ? '' : `<label style="font-size:12px;color:#374151;flex:1;">Quantité initiale<input id="si-qty" type="number" min="0" step="any" class="settings-input" value="0" style="width:100%;margin-top:3px;" /></label>`}
        <label style="font-size:12px;color:#374151;flex:1;">Seuil d'alerte<input id="si-min" type="number" min="0" step="any" class="settings-input" value="${item?.minThreshold ?? 0}" style="width:100%;margin-top:3px;" /></label>
      </div>
      <div style="display:flex;gap:10px;">
        <label style="font-size:12px;color:#374151;flex:1;">Fournisseur<input id="si-sup" class="settings-input" value="${esc(item?.supplier || '')}" style="width:100%;margin-top:3px;" /></label>
        <label style="font-size:12px;color:#374151;flex:1;">Référence<input id="si-ref" class="settings-input" value="${esc(item?.reference || '')}" style="width:100%;margin-top:3px;" /></label>
      </div>
      <label style="font-size:12px;color:#374151;">Note<input id="si-note" class="settings-input" value="${esc(item?.note || '')}" style="width:100%;margin-top:3px;" /></label>
    </div>
    <div style="padding:14px 22px;border-top:1px solid #eee;display:flex;justify-content:flex-end;gap:10px;">
      <button id="si-cancel" class="btn" style="border-radius:8px;">Annuler</button>
      <button id="si-save" class="btn btn-primary" style="border-radius:8px;">${isEdit ? 'Enregistrer' : 'Créer'}</button>
    </div>`;
  overlay.appendChild(panel); document.body.appendChild(overlay);
  const close = () => overlay.remove();
  overlay.onclick = e => { if (e.target === overlay) close(); };
  panel.querySelector("#si-cancel").onclick = close;
  panel.querySelector("#si-save").onclick = async () => {
    const payload = {
      name: panel.querySelector("#si-name").value.trim(),
      category: panel.querySelector("#si-cat").value,
      unit: panel.querySelector("#si-unit").value.trim(),
      minThreshold: parseFloat(panel.querySelector("#si-min").value) || 0,
      supplier: panel.querySelector("#si-sup").value.trim(),
      reference: panel.querySelector("#si-ref").value.trim(),
      note: panel.querySelector("#si-note").value.trim()
    };
    if (!payload.name) { showNotification("Nom requis", "error"); return; }
    if (!isEdit) payload.quantity = parseFloat(panel.querySelector("#si-qty").value) || 0;
    const url = isEdit ? `/api/stock/${item.id}` : "/api/stock";
    const method = isEdit ? "PUT" : "POST";
    const r = await fetch(url, { method, headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` }, body: JSON.stringify(payload) }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { close(); showNotification(isEdit ? "✅ Article modifié" : "✅ Article créé", "success"); loadStock(); }
    else showNotification(`❌ ${r.error || "Échec"}`, "error");
  };
}

// Modale mouvement (entrée / sortie)
function openMovementModal(item, type) {
  if (!item) return;
  const isIn = type === "entree";
  const overlay = _overlay();
  const panel = _panel("420px");
  panel.innerHTML = `
    <div style="padding:18px 22px;border-bottom:1px solid #eee;"><h3 style="margin:0;font-size:16px;font-weight:700;color:${isIn ? '#16a34a' : '#dc2626'};">${isIn ? '＋ Entrée de stock' : '－ Sortie de stock'}</h3>
      <div style="font-size:13px;color:#6b7280;margin-top:4px;">${esc(item.name)} — stock actuel : <strong>${_num(item.quantity)} ${esc(item.unit || '')}</strong></div></div>
    <div style="padding:16px 22px;display:flex;flex-direction:column;gap:10px;">
      <label style="font-size:12px;color:#374151;">Quantité (${isIn ? 'ajoutée' : 'retirée'})<input id="mv-qty" type="number" min="0" step="any" class="settings-input" value="" style="width:100%;margin-top:3px;" autofocus /></label>
      <label style="font-size:12px;color:#374151;">Motif<input id="mv-reason" class="settings-input" value="" placeholder="${isIn ? 'Réception fournisseur…' : 'Consommation OF…'}" style="width:100%;margin-top:3px;" /></label>
    </div>
    <div style="padding:14px 22px;border-top:1px solid #eee;display:flex;justify-content:flex-end;gap:10px;">
      <button id="mv-cancel" class="btn" style="border-radius:8px;">Annuler</button>
      <button id="mv-save" class="btn btn-primary" style="border-radius:8px;background:${isIn ? '#16a34a' : '#dc2626'};border-color:${isIn ? '#16a34a' : '#dc2626'};">Valider</button>
    </div>`;
  overlay.appendChild(panel); document.body.appendChild(overlay);
  const close = () => overlay.remove();
  overlay.onclick = e => { if (e.target === overlay) close(); };
  panel.querySelector("#mv-cancel").onclick = close;
  panel.querySelector("#mv-save").onclick = async () => {
    const qty = parseFloat(panel.querySelector("#mv-qty").value);
    if (!(qty > 0)) { showNotification("Quantité invalide", "error"); return; }
    const r = await fetch(`/api/stock/${item.id}/movement`, {
      method: "POST", headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ type, quantity: qty, reason: panel.querySelector("#mv-reason").value.trim() })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { close(); showNotification(`✅ Stock mis à jour : ${_num(r.quantity)}`, "success"); loadStock(); }
    else showNotification(`❌ ${r.error || "Échec"}`, "error");
  };
}

async function deleteItem(item) {
  if (!item) return;
  if (!confirm(`Supprimer l'article « ${item.name} » et son historique ?`)) return;
  const r = await fetch(`/api/stock/${item.id}`, { method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ ok: false }));
  if (r.ok) { showNotification("✅ Article supprimé", "success"); loadStock(); }
  else showNotification(`❌ ${r.error || "Échec"}`, "error");
}

function _overlay() {
  const o = document.createElement("div");
  o.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000;padding:20px;";
  return o;
}
function _panel(maxW) {
  const p = document.createElement("div");
  p.style.cssText = `background:#fff;border-radius:12px;box-shadow:0 10px 40px rgba(0,0,0,.3);max-width:${maxW};width:100%;max-height:90vh;overflow-y:auto;`;
  return p;
}
