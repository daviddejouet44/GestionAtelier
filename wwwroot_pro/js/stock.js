// stock.js — Gestion des stocks (point 7)
import { authToken, showNotification, esc, currentUser } from './core.js';

const STATUS_STYLE = {
  ok:      { bg: "#dcfce7", fg: "#16a34a", label: "🟢 OK" },
  bas:     { bg: "#fef3c7", fg: "#b45309", label: "🟠 Bas" },
  rupture: { bg: "#fee2e2", fg: "#dc2626", label: "🔴 Rupture" }
};

// Cache des catégories chargées depuis l'API
let _categories = [];

function catLabel(cat) {
  const c = _categories.find(x => x.id === cat);
  return c ? `${c.emoji || ''} ${c.label}`.trim() : esc(cat);
}

export async function initStockView() {
  const container = document.getElementById("stock-view");
  if (!container) return;
  container.innerHTML = `
    <div class="settings-container">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px;flex-wrap:wrap;gap:8px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:var(--text-primary);">📦 Gestion des stocks</h2>
        <div style="display:flex;gap:8px;flex-wrap:wrap;">
          <button id="stock-manage-cats-btn" class="btn" style="border-radius:50px;">🗂️ Gérer les catégories</button>
          <button id="stock-import-btn" class="btn" style="border-radius:50px;">📥 Importer</button>
          <button id="stock-add-btn" class="btn btn-primary" style="border-radius:50px;">＋ Ajouter un article</button>
        </div>
      </div>
      <div id="stock-import-panel" style="display:none;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px;margin-bottom:16px;">
        <h4 style="margin:0 0 12px;font-size:14px;font-weight:700;color:#1e3a5f;">📥 Import CSV / XML</h4>
        <div style="display:flex;flex-wrap:wrap;gap:12px;align-items:flex-end;">
          <div>
            <label style="font-size:12px;color:#374151;display:block;margin-bottom:4px;">Catégorie cible</label>
            <select id="import-cat-select" class="settings-input" style="min-width:160px;"></select>
          </div>
          <div>
            <label style="font-size:12px;color:#374151;display:block;margin-bottom:4px;">Mode</label>
            <select id="import-mode-select" class="settings-input">
              <option value="merge">Fusionner</option>
              <option value="overwrite">Écraser</option>
            </select>
          </div>
          <div>
            <label style="font-size:12px;color:#374151;display:block;margin-bottom:4px;">Fichier CSV ou XML</label>
            <input type="file" id="import-file-input" accept=".csv,.xml" class="settings-input" style="font-size:13px;" />
          </div>
          <button id="import-submit-btn" class="btn btn-primary">Importer</button>
          <button id="import-close-btn" class="btn">✕</button>
        </div>
        <div style="margin-top:10px;font-size:11px;color:#6b7280;">
          <strong>CSV</strong> — colonnes (séparateur ; ou ,) : <code>nom</code> (obligatoire), <code>quantité</code>, <code>unité</code>, <code>seuil</code>, <code>fournisseur</code>, <code>référence</code>, <code>note</code><br/>
          <strong>XML</strong> — <code>&lt;articles&gt;&lt;article nom="…" quantite="…" unite="…" seuil="…" fournisseur="…" reference="…" note="…"/&gt;&lt;/articles&gt;</code>
        </div>
        <div id="import-result" style="margin-top:10px;font-size:13px;"></div>
      </div>
      <div id="stock-alerts"></div>
      <div id="stock-add-form"></div>
      <div id="stock-body"><p style="color:#6b7280;">Chargement…</p></div>
    </div>`;

  document.getElementById("stock-add-btn").onclick = () => openItemModal(null);
  document.getElementById("stock-manage-cats-btn").onclick = () => openManageCategoriesModal();
  document.getElementById("stock-import-btn").onclick = () => toggleImportPanel();
  document.getElementById("import-close-btn").onclick = () => {
    document.getElementById("stock-import-panel").style.display = "none";
  };
  document.getElementById("import-submit-btn").onclick = () => runImport();

  await loadStock();
}

async function loadStock() {
  const body = document.getElementById("stock-body");
  if (!body) return;
  let data;
  try {
    data = await fetch("/api/stock", { headers: { "Authorization": `****** } }).then(r => r.json());
  } catch (e) { body.innerHTML = '<p style="color:#dc2626;">Erreur de chargement</p>'; return; }
  if (!data.ok) { body.innerHTML = `<p style="color:#dc2626;">${esc(data.error || "Erreur")}</p>`; return; }

  if (Array.isArray(data.categories)) _categories = data.categories;

  // Attribuer un identifiant synthétique aux articles virtuels du catalogue papiers
  // (ils n'ont pas encore d'_id en base)
  for (const item of data.items || []) {
    if (item.isVirtual && !item.id) {
      item.id = "~vp~" + item.name;
    }
  }

  renderAlerts(data.items || []);
  renderBody(data.items || [], data.categories || []);

  const importSelect = document.getElementById("import-cat-select");
  if (importSelect) _populateCatSelect(importSelect);
}

function _populateCatSelect(select) {
  // Exclure la catégorie papier de l'import : elle est synchronisée automatiquement
  select.innerHTML = _categories
    .filter(c => c.id !== "papier")
    .map(c => `<option value="${esc(c.id)}">${esc(c.emoji || '')} ${esc(c.label)}</option>`)
    .join('');
}

function toggleImportPanel() {
  const panel = document.getElementById("stock-import-panel");
  if (!panel) return;
  const visible = panel.style.display !== "none";
  panel.style.display = visible ? "none" : "";
  if (!visible) {
    const importSelect = document.getElementById("import-cat-select");
    if (importSelect) _populateCatSelect(importSelect);
  }
}

async function runImport() {
  const fileInput = document.getElementById("import-file-input");
  const catSelect = document.getElementById("import-cat-select");
  const modeSelect = document.getElementById("import-mode-select");
  const resultEl = document.getElementById("import-result");
  if (!fileInput || !catSelect || !modeSelect) return;

  const file = fileInput.files?.[0];
  if (!file) { showNotification("Sélectionnez un fichier", "error"); return; }
  const category = catSelect.value;
  const mode = modeSelect.value;
  if (!category) { showNotification("Sélectionnez une catégorie", "error"); return; }

  const btn = document.getElementById("import-submit-btn");
  if (btn) { btn.disabled = true; btn.textContent = "Import…"; }
  if (resultEl) resultEl.innerHTML = '<span style="color:#6b7280;">Import en cours…</span>';

  try {
    const fd = new FormData();
    fd.append("file", file);
    fd.append("category", category);
    fd.append("mode", mode);
    const r = await fetch("/api/stock/import", {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` },
      body: fd
    }).then(res => res.json());

    if (r.ok) {
      const parts = [];
      if (r.added) parts.push(`<strong style="color:#16a34a;">+${r.added} créé(s)</strong>`);
      if (r.updated) parts.push(`<strong style="color:#2563eb;">${r.updated} mis à jour</strong>`);
      if (r.skipped) parts.push(`<span style="color:#9ca3af;">${r.skipped} ignoré(s)</span>`);
      let html = `✅ Import terminé — ${parts.join(', ')}`;
      if (r.errors && r.errors.length) {
        html += `<ul style="margin:6px 0 0;padding-left:16px;font-size:11px;color:#dc2626;">${r.errors.slice(0, 10).map(e => `<li>${esc(e)}</li>`).join('')}</ul>`;
      }
      if (resultEl) resultEl.innerHTML = html;
      showNotification(`✅ Import terminé : ${r.added} créé(s), ${r.updated} mis à jour`, "success");
      fileInput.value = "";
      await loadStock();
    } else {
      if (resultEl) resultEl.innerHTML = `<span style="color:#dc2626;">❌ ${esc(r.error || "Erreur")}</span>`;
      showNotification(`❌ ${r.error || "Erreur import"}`, "error");
    }
  } catch (e) {
    if (resultEl) resultEl.innerHTML = '<span style="color:#dc2626;">❌ Erreur réseau</span>';
    showNotification("❌ Erreur réseau", "error");
  } finally {
    if (btn) { btn.disabled = false; btn.textContent = "Importer"; }
  }
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
  const sortedCats = Array.isArray(categories) && categories.length
    ? categories.map(c => typeof c === 'object' ? c.id : c)
    : _categories.map(c => c.id);

  let html = "";
  for (const catId of sortedCats) {
    const rows = items.filter(i => i.category === catId);
    if (rows.length === 0) continue;
    const isPapier = catId === "papier";
    html += `<div style="margin-bottom:18px;">
      <h3 style="font-size:15px;font-weight:700;color:#1e3a5f;margin:0 0 8px;">${catLabel(catId)} <span style="font-weight:500;color:#9ca3af;font-size:12px;">(${rows.length})</span></h3>`;
    if (isPapier) {
      html += `<div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:8px;padding:8px 12px;font-size:12px;color:#1d4ed8;margin-bottom:8px;">
        📄 Ces papiers sont synchronisés automatiquement depuis le <strong>Catalogue papiers</strong>.
        Pour ajouter ou supprimer un papier, utilisez <a href="#" onclick="document.querySelector('[data-section=settings]')?.click();return false;" style="color:#1d4ed8;text-decoration:underline;">Réglages → Catalogue papiers</a>.
        Les quantités et seuils restent modifiables ici.
      </div>`;
    }
    html += `<div style="overflow-x:auto;"><table style="width:100%;border-collapse:collapse;font-size:13px;">
        <thead><tr style="text-align:left;color:#6b7280;border-bottom:1px solid #e5e7eb;">
          <th style="padding:6px 8px;">Article</th><th style="padding:6px 8px;">Quantité</th>
          <th style="padding:6px 8px;">Seuil</th><th style="padding:6px 8px;">Statut</th>
          <th style="padding:6px 8px;">Fournisseur</th><th style="padding:6px 8px;text-align:right;">Actions</th>
        </tr></thead><tbody>`;
    for (const i of rows) {
      const st = STATUS_STYLE[i.status] || STATUS_STYLE.ok;
      const rowBg = i.isVirtual ? 'background:#f8fafc;' : (i.isOrphan ? 'background:#fffbeb;' : '');
      const orphanBadge = i.isOrphan ? ' <span style="font-size:10px;background:#fef3c7;color:#b45309;border-radius:4px;padding:1px 5px;vertical-align:middle;">orphelin</span>' : '';
      html += `<tr data-id="${esc(i.id)}" style="border-bottom:1px solid #f1f5f9;${rowBg}">
        <td style="padding:7px 8px;font-weight:600;color:${i.isVirtual ? '#9ca3af' : '#111827'};">${esc(i.name)}${i.reference ? ` <span style="font-weight:400;color:#9ca3af;">(${esc(i.reference)})</span>` : ''}${orphanBadge}</td>
        <td style="padding:7px 8px;"><strong>${_num(i.quantity)}</strong> ${esc(i.unit || '')}</td>
        <td style="padding:7px 8px;color:#6b7280;">${_num(i.minThreshold)}</td>
        <td style="padding:7px 8px;"><span style="background:${st.bg};color:${st.fg};font-weight:700;font-size:11px;border-radius:10px;padding:2px 9px;white-space:nowrap;">${st.label}</span></td>
        <td style="padding:7px 8px;color:#6b7280;">${esc(i.supplier || '—')}</td>
        <td style="padding:7px 8px;text-align:right;white-space:nowrap;">
          <button class="stock-mv" data-id="${esc(i.id)}" data-type="entree" title="Entrée de stock" style="border:1px solid #16a34a;color:#16a34a;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;font-weight:700;">＋</button>
          <button class="stock-mv" data-id="${esc(i.id)}" data-type="sortie" title="Sortie de stock" style="border:1px solid #dc2626;color:#dc2626;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;font-weight:700;">－</button>
          <button class="stock-edit" data-id="${esc(i.id)}" title="Modifier" style="border:1px solid #d1d5db;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;">✏️</button>
          ${isAdmin && !i.fromCatalog ? `<button class="stock-del" data-id="${esc(i.id)}" title="Supprimer" style="border:1px solid #d1d5db;background:#fff;border-radius:6px;padding:3px 8px;cursor:pointer;">🗑️</button>` : ''}
        </td>
      </tr>`;
    }
    html += `</tbody></table></div></div>`;
  }

  const knownIds = new Set(sortedCats);
  const orphans = items.filter(i => !knownIds.has(i.category));
  if (orphans.length) {
    html += `<div style="margin-bottom:18px;">
      <h3 style="font-size:15px;font-weight:700;color:#9ca3af;margin:0 0 8px;">❓ Autres (${orphans.length})</h3>
      <div style="overflow-x:auto;"><table style="width:100%;border-collapse:collapse;font-size:13px;">
        <thead><tr style="text-align:left;color:#6b7280;border-bottom:1px solid #e5e7eb;">
          <th style="padding:6px 8px;">Article</th><th style="padding:6px 8px;">Catégorie</th><th style="padding:6px 8px;">Quantité</th>
          <th style="padding:6px 8px;">Statut</th><th style="padding:6px 8px;text-align:right;">Actions</th>
        </tr></thead><tbody>`;
    for (const i of orphans) {
      const st = STATUS_STYLE[i.status] || STATUS_STYLE.ok;
      html += `<tr data-id="${esc(i.id)}" style="border-bottom:1px solid #f1f5f9;">
        <td style="padding:7px 8px;font-weight:600;color:#111827;">${esc(i.name)}</td>
        <td style="padding:7px 8px;color:#9ca3af;">${esc(i.category)}</td>
        <td style="padding:7px 8px;"><strong>${_num(i.quantity)}</strong> ${esc(i.unit || '')}</td>
        <td style="padding:7px 8px;"><span style="background:${st.bg};color:${st.fg};font-weight:700;font-size:11px;border-radius:10px;padding:2px 9px;white-space:nowrap;">${st.label}</span></td>
        <td style="padding:7px 8px;text-align:right;white-space:nowrap;">
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

/**
 * Matérialise un article papier virtuel (catalogue uniquement) en article de stock réel.
 * Si l'article existe déjà en base, retourne son id sans modification.
 * Met à jour item.id et item.isVirtual après matérialisation.
 */
async function ensurePaperId(item) {
  if (!item.isVirtual) return item.id;
  const r = await fetch("/api/stock/ensure-paper", {
    method: "POST",
    headers: { "Content-Type": "application/json", "Authorization": `****** },
    body: JSON.stringify({ name: item.name })
  }).then(res => res.json()).catch(() => ({ ok: false }));
  if (!r.ok) throw new Error(r.error || "Impossible de matérialiser l'article papier");
  item.id = r.id;
  item.isVirtual = false;
  return r.id;
}

async function openManageCategoriesModal() {
  const overlay = _overlay();
  const panel = _panel("560px");
  const isAdmin = currentUser && currentUser.profile === 3;

  const render = async () => {
    let cats = [];
    try {
      const r = await fetch("/api/stock/categories", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
      if (r.ok) { cats = r.categories || []; _categories = cats; }
    } catch {}

    panel.innerHTML = `
      <div style="padding:18px 22px;border-bottom:1px solid #eee;display:flex;justify-content:space-between;align-items:center;">
        <h3 style="margin:0;font-size:16px;font-weight:700;color:#1e3a5f;">🗂️ Gérer les catégories</h3>
        <button id="mcat-close" class="btn" style="border-radius:8px;">✕</button>
      </div>
      <div style="padding:16px 22px;">
        <div id="mcat-list" style="display:flex;flex-direction:column;gap:8px;margin-bottom:16px;">
          ${cats.map(c => `
            <div style="display:flex;align-items:center;gap:8px;padding:8px 12px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;">
              <span style="font-size:18px;min-width:24px;text-align:center;">${esc(c.emoji || '📁')}</span>
              <span style="flex:1;font-weight:600;color:#111827;font-size:13px;">${esc(c.label)}</span>
              <span style="font-size:11px;color:#9ca3af;font-family:monospace;">${esc(c.id)}</span>
              <button class="mcat-edit-btn btn" data-id="${esc(c.id)}" style="font-size:11px;padding:2px 8px;">✏️</button>
              ${isAdmin ? `<button class="mcat-del-btn btn" data-id="${esc(c.id)}" style="font-size:11px;padding:2px 8px;color:#dc2626;border-color:#dc2626;">🗑️</button>` : ''}
            </div>`).join('')}
        </div>
        ${cats.length === 0 ? '<p style="color:#9ca3af;font-size:13px;">Aucune catégorie.</p>' : ''}
        <div style="border-top:1px solid #e5e7eb;padding-top:14px;">
          <h4 style="margin:0 0 10px;font-size:13px;font-weight:700;color:#374151;">＋ Nouvelle catégorie</h4>
          <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;">
            <div><label style="font-size:12px;color:#6b7280;display:block;margin-bottom:3px;">Emoji</label><input id="mcat-new-emoji" class="settings-input" value="" placeholder="📦" style="width:60px;text-align:center;" maxlength="4" /></div>
            <div style="flex:1;min-width:140px;"><label style="font-size:12px;color:#6b7280;display:block;margin-bottom:3px;">Libellé</label><input id="mcat-new-label" class="settings-input" value="" placeholder="Nom de la catégorie" style="width:100%;" /></div>
            <button id="mcat-add-btn" class="btn btn-primary">Ajouter</button>
          </div>
          <div id="mcat-err" style="margin-top:6px;font-size:12px;color:#dc2626;"></div>
        </div>
      </div>`;

    panel.querySelector("#mcat-close").onclick = () => { overlay.remove(); loadStock(); };

    panel.querySelectorAll(".mcat-edit-btn").forEach(btn => {
      btn.onclick = async () => {
        const cat = cats.find(c => c.id === btn.dataset.id);
        if (!cat) return;
        const newLabel = prompt("Nouveau libellé :", cat.label);
        if (!newLabel || newLabel.trim() === cat.label) return;
        const newEmoji = prompt("Emoji (laisser vide pour conserver) :", cat.emoji || '');
        const r = await fetch(`/api/stock/categories/${encodeURIComponent(cat.id)}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
          body: JSON.stringify({ label: newLabel.trim(), emoji: (newEmoji !== null && newEmoji.trim() !== '') ? newEmoji.trim() : cat.emoji })
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (r.ok) { showNotification("✅ Catégorie renommée", "success"); await render(); }
        else showNotification(`❌ ${r.error || "Erreur"}`, "error");
      };
    });

    panel.querySelectorAll(".mcat-del-btn").forEach(btn => {
      btn.onclick = async () => {
        const cat = cats.find(c => c.id === btn.dataset.id);
        if (!cat) return;
        if (!confirm(`Supprimer la catégorie « ${cat.label} » ? Les articles qu'elle contient doivent d'abord être déplacés.`)) return;
        const r = await fetch(`/api/stock/categories/${encodeURIComponent(cat.id)}`, {
          method: "DELETE",
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(res => res.json()).catch(() => ({ ok: false }));
        if (r.ok) { showNotification("✅ Catégorie supprimée", "success"); await render(); }
        else showNotification(`❌ ${r.error || "Erreur"}`, "error");
      };
    });

    panel.querySelector("#mcat-add-btn").onclick = async () => {
      const errEl = panel.querySelector("#mcat-err");
      const label = (panel.querySelector("#mcat-new-label").value || "").trim();
      const emoji = (panel.querySelector("#mcat-new-emoji").value || "").trim();
      if (!label) { if (errEl) errEl.textContent = "Libellé requis"; return; }
      const r = await fetch("/api/stock/categories", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ label, emoji })
      }).then(res => res.json()).catch(() => ({ ok: false }));
      if (r.ok) { showNotification("✅ Catégorie créée", "success"); await render(); }
      else { if (errEl) errEl.textContent = r.error || "Erreur"; }
    };
  };

  overlay.appendChild(panel); document.body.appendChild(overlay);
  overlay.onclick = e => { if (e.target === overlay) { overlay.remove(); loadStock(); } };
  await render();
}

function openItemModal(item) {
  const isEdit = !!item;
  const isCatalogPaper = isEdit && item.fromCatalog;
  const overlay = _overlay();
  const panel = _panel("520px");

  // Pour les articles du catalogue papier, on n'affiche pas la catégorie papier
  // dans le sélecteur (pas de création manuelle). Pour les nouveaux articles,
  // on exclut papier du choix de catégorie.
  const catsOptions = _categories
    .filter(c => !(c.id === "papier" && !isCatalogPaper))
    .map(c =>
      `<option value="${esc(c.id)}" ${item?.category === c.id ? 'selected' : ''} ${isCatalogPaper && c.id === "papier" ? 'disabled' : ''}>${esc((c.emoji || '') + ' ' + c.label)}</option>`
    ).join('');

  panel.innerHTML = `
    <div style="padding:18px 22px;border-bottom:1px solid #eee;"><h3 style="margin:0;font-size:16px;font-weight:700;color:#1e3a5f;">${isEdit ? "✏️ Modifier l'article" : "＋ Nouvel article"}</h3></div>
    <div style="padding:16px 22px;display:flex;flex-direction:column;gap:10px;">
      ${isCatalogPaper ? `<div style="background:#eff6ff;border:1px solid #bfdbfe;border-radius:6px;padding:7px 10px;font-size:12px;color:#1d4ed8;margin-bottom:4px;">📄 Papier synchronisé depuis le Catalogue papiers — le nom et la catégorie ne sont pas modifiables ici.</div>` : ''}
      <label style="font-size:12px;color:#374151;">Nom<input id="si-name" class="settings-input" value="${esc(item?.name || '')}" style="width:100%;margin-top:3px;" ${isCatalogPaper ? 'disabled style="width:100%;margin-top:3px;opacity:0.6;"' : ''}/></label>
      <div style="display:flex;gap:10px;">
        <label style="font-size:12px;color:#374151;flex:1;">Catégorie
          <select id="si-cat" class="settings-input" style="width:100%;margin-top:3px;" ${isCatalogPaper ? 'disabled' : ''}>${catsOptions}</select>
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
      name: isCatalogPaper ? item.name : panel.querySelector("#si-name").value.trim(),
      category: isCatalogPaper ? "papier" : panel.querySelector("#si-cat").value,
      unit: panel.querySelector("#si-unit").value.trim(),
      minThreshold: parseFloat(panel.querySelector("#si-min").value) || 0,
      supplier: panel.querySelector("#si-sup").value.trim(),
      reference: panel.querySelector("#si-ref").value.trim(),
      note: panel.querySelector("#si-note").value.trim()
    };
    if (!payload.name) { showNotification("Nom requis", "error"); return; }
    if (!isEdit) payload.quantity = parseFloat(panel.querySelector("#si-qty")?.value) || 0;
    let url, method;
    if (isEdit) {
      let itemId;
      try { itemId = await ensurePaperId(item); }
      catch (e) { showNotification(`❌ ${e.message}`, "error"); return; }
      url = `/api/stock/${itemId}`;
      method = "PUT";
    } else {
      url = "/api/stock";
      method = "POST";
    }
    const r = await fetch(url, {
      method,
      headers: { "Content-Type": "application/json", "Authorization": `****** },
      body: JSON.stringify(payload)
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { close(); showNotification(isEdit ? "✅ Article modifié" : "✅ Article créé", "success"); loadStock(); }
    else showNotification(`❌ ${r.error || "Échec"}`, "error");
  };
}

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
    let itemId;
    try { itemId = await ensurePaperId(item); }
    catch (e) { showNotification(`❌ ${e.message}`, "error"); return; }
    const r = await fetch(`/api/stock/${itemId}/movement`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `****** },
      body: JSON.stringify({ type, quantity: qty, reason: panel.querySelector("#mv-reason").value.trim() })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { close(); showNotification(`✅ Stock mis à jour : ${_num(r.quantity)}`, "success"); loadStock(); }
    else showNotification(`❌ ${r.error || "Échec"}`, "error");
  };
}

async function deleteItem(item) {
  if (!item) return;
  if (!confirm(`Supprimer l'article « ${item.name} » et son historique ?`)) return;
  const r = await fetch(`/api/stock/${item.id}`, {
    method: "DELETE",
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json()).catch(() => ({ ok: false }));
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
