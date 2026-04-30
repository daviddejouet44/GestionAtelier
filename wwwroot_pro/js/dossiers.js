// dossiers.js — Dossiers de production
import { authToken, showNotification, fnKey, fmtBytes } from './core.js';
import { openFabrication } from './fabrication.js';
import { STAGE_DISPLAY_LABELS, getStageLabelDisplay, STAGE_ORDER } from './constants.js';

export function showDossiers() {
  // Navigation handled by app.js
  initDossiersView();
}

// Dossiers view state
let _dossiersViewMode = "grid"; // "grid" or "list"
let _dossiersSortMode = "date_desc";

export async function initDossiersView() {
  const el = document.getElementById("dossiers");
  el.style.cssText = "width:100%;box-sizing:border-box;padding:20px;overflow-y:auto;";
  el.innerHTML = `
    <div style="width:100%;">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;flex-wrap:wrap;gap:10px;">
        <h2 style="margin:0;font-size:22px;font-weight:700;color:#111827;">Dossiers de production</h2>
        <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">
          <select id="dossiers-sort" style="padding:7px 12px;border:1.5px solid #e5e7eb;border-radius:8px;font-size:13px;background:white;color:#111827;cursor:pointer;outline:none;">
            <option value="date_desc">Date modif. (récent)</option>
            <option value="date_asc">Date modif. (ancien)</option>
            <option value="name_asc">Nom (A→Z)</option>
            <option value="name_desc">Nom (Z→A)</option>
            <option value="size_desc">Taille (grosse→petite)</option>
            <option value="size_asc">Taille (petite→grosse)</option>
          </select>
          <div style="display:flex;gap:2px;background:#f3f4f6;border-radius:8px;padding:3px;">
            <button id="dossiers-view-grid" title="Vue grille" style="padding:5px 10px;border:none;border-radius:6px;background:${_dossiersViewMode==='grid'?'white':'transparent'};cursor:pointer;font-size:16px;line-height:1;box-shadow:${_dossiersViewMode==='grid'?'0 1px 3px rgba(0,0,0,0.15)':'none'};">▦</button>
            <button id="dossiers-view-list" title="Vue liste" style="padding:5px 10px;border:none;border-radius:6px;background:${_dossiersViewMode==='list'?'white':'transparent'};cursor:pointer;font-size:16px;line-height:1;box-shadow:${_dossiersViewMode==='list'?'0 1px 3px rgba(0,0,0,0.15)':'none'};">☰</button>
          </div>
          <button id="dossiers-refresh" class="btn btn-primary">Rafraîchir</button>
          <button id="dossiers-export-xml" class="btn" title="Exporter toutes les commandes en XML">📤 XML</button>
          <button id="dossiers-export-csv" class="btn" title="Exporter toutes les commandes en CSV">📤 CSV</button>
        </div>
      </div>
      <div id="dossiers-list"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  document.getElementById("dossiers-refresh").onclick = loadDossiersList;
  // Export buttons
  const makeExportBtn = (btnId, fmt) => {
    const btn = document.getElementById(btnId);
    if (!btn) return;
    btn.onclick = async () => {
      btn.disabled = true; btn.textContent = '⏳';
      try {
        const resp = await fetch(`/api/integrations/export?format=${fmt}`, { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!resp.ok) { showNotification('❌ Erreur export', 'error'); return; }
        const blob = await resp.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = `export-commandes.${fmt}`;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
        URL.revokeObjectURL(url);
        showNotification(`✅ Export ${fmt.toUpperCase()} téléchargé`, 'success');
      } catch(e) { showNotification('❌ Erreur réseau', 'error'); }
      finally { btn.disabled = false; btn.textContent = fmt === 'xml' ? '📤 XML' : '📤 CSV'; }
    };
  };
  makeExportBtn('dossiers-export-xml', 'xml');
  makeExportBtn('dossiers-export-csv', 'csv');
  document.getElementById("dossiers-sort").value = _dossiersSortMode;
  document.getElementById("dossiers-sort").onchange = (e) => {
    _dossiersSortMode = e.target.value;
    loadDossiersList();
  };
  document.getElementById("dossiers-view-grid").onclick = () => {
    _dossiersViewMode = "grid";
    document.getElementById("dossiers-view-grid").style.background = "white";
    document.getElementById("dossiers-view-grid").style.boxShadow = "0 1px 3px rgba(0,0,0,0.15)";
    document.getElementById("dossiers-view-list").style.background = "transparent";
    document.getElementById("dossiers-view-list").style.boxShadow = "none";
    loadDossiersList();
  };
  document.getElementById("dossiers-view-list").onclick = () => {
    _dossiersViewMode = "list";
    document.getElementById("dossiers-view-list").style.background = "white";
    document.getElementById("dossiers-view-list").style.boxShadow = "0 1px 3px rgba(0,0,0,0.15)";
    document.getElementById("dossiers-view-grid").style.background = "transparent";
    document.getElementById("dossiers-view-grid").style.boxShadow = "none";
    loadDossiersList();
  };
  await loadDossiersList();
}

export async function loadDossiersList() {
  const listEl = document.getElementById("dossiers-list");
  if (!listEl) return;
  listEl.innerHTML = '<p style="color:#6b7280;">Chargement...</p>';
  try {
    const folders = await fetch("/api/production-folders", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);

    if (!Array.isArray(folders) || folders.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun dossier de production</p>';
      return;
    }

    // Fetch real-time file stage for each folder in parallel
    const stageResults = await Promise.all(folders.map(async folder => {
      const fn = folder.fileName || '';
      if (!fn) return null;
      try {
        const res = await fetch("/api/file-stage?fileName=" + encodeURIComponent(fn), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json()).catch(() => null);
        if (res && res.ok && res.folder) return { stage: res.folder, batStatus: res.batStatus || null };
        return null;
      } catch { return null; }
    }));

    // STAGE ordering for "least advanced" logic (imported from constants.js)
    function stageIndex(s) {
      if (!s) return 0;
      const lower = s.toLowerCase();
      let idx = STAGE_ORDER.findIndex(k => k.toLowerCase() === lower);
      if (idx < 0) idx = STAGE_ORDER.findIndex(k => lower.includes(k.toLowerCase()));
      return idx >= 0 ? idx : 0;
    }

    // Group folders by numeroDossier
    const grouped = {};
    const ungrouped = [];

    folders.forEach((folder, idx) => {
      const stageResult = stageResults[idx];
      const realStage = (stageResult && stageResult.stage) ? stageResult.stage : (folder.currentStage || 'Début de production');
      const batStatus = (stageResult && stageResult.batStatus) ? stageResult.batStatus : null;
      const num = (folder.numeroDossier || '').trim();
      if (num) {
        if (!grouped[num]) grouped[num] = [];
        grouped[num].push({ folder, realStage, batStatus });
      } else {
        ungrouped.push({ folder, realStage, batStatus });
      }
    });

    // Build flat list of items for sorting
    const allItems = [
      ...Object.entries(grouped).map(([num, items]) => ({
        type: "grouped",
        numeroDossier: num,
        items,
        name: num,
        date: Math.max(...items.map(i => new Date(i.folder.updatedAt || i.folder.createdAt || 0).getTime())),
        size: items.reduce((acc, i) => acc + (i.folder.fileSize || 0), 0)
      })),
      ...ungrouped.map(({ folder, realStage, batStatus }) => ({
        type: "single",
        folder,
        realStage,
        batStatus,
        name: folder.fileName || folder.numeroDossier || '',
        date: new Date(folder.updatedAt || folder.createdAt || 0).getTime(),
        size: folder.fileSize || 0
      }))
    ];

    // Sort
    allItems.sort((a, b) => {
      switch(_dossiersSortMode) {
        case "name_asc": return a.name.localeCompare(b.name);
        case "name_desc": return b.name.localeCompare(a.name);
        case "date_asc": return a.date - b.date;
        case "date_desc": return b.date - a.date;
        case "size_asc": return a.size - b.size;
        case "size_desc": return b.size - a.size;
        default: return b.date - a.date;
      }
    });

    listEl.innerHTML = "";

    if (_dossiersViewMode === "list") {
      // List/table view
      const table = document.createElement("div");
      table.style.cssText = "width:100%;";
      const header = document.createElement("div");
      header.style.cssText = "display:grid;grid-template-columns:2fr 1fr 1fr 1fr;gap:8px;padding:10px 16px;background:#f3f4f6;border-radius:8px;font-size:12px;font-weight:700;color:#6b7280;text-transform:uppercase;letter-spacing:0.04em;margin-bottom:6px;";

      const colNom = document.createElement("span");
      colNom.textContent = "📁 Nom";
      colNom.style.cursor = "pointer";
      colNom.onclick = () => { document.getElementById("dossiers-sort").value = "name_asc"; document.getElementById("dossiers-sort").dispatchEvent(new Event("change")); };

      const colDate = document.createElement("span");
      colDate.textContent = "Date modif.";
      colDate.style.cursor = "pointer";
      colDate.onclick = () => { document.getElementById("dossiers-sort").value = "date_desc"; document.getElementById("dossiers-sort").dispatchEvent(new Event("change")); };

      const colSize = document.createElement("span");
      colSize.textContent = "Taille";
      colSize.style.cursor = "pointer";
      colSize.onclick = () => { document.getElementById("dossiers-sort").value = "size_desc"; document.getElementById("dossiers-sort").dispatchEvent(new Event("change")); };

      const colEtape = document.createElement("span");
      colEtape.textContent = "Étape";

      header.appendChild(colNom);
      header.appendChild(colDate);
      header.appendChild(colSize);
      header.appendChild(colEtape);
      table.appendChild(header);

      for (const item of allItems) {
        const row = document.createElement("div");
        row.style.cssText = "display:grid;grid-template-columns:2fr 1fr 1fr 1fr;gap:8px;padding:10px 16px;background:white;border:1px solid #f3f4f6;border-radius:8px;margin-bottom:4px;align-items:center;cursor:pointer;transition:background 0.15s;";
        row.onmouseenter = () => { row.style.background = "#f9fafb"; };
        row.onmouseleave = () => { row.style.background = "white"; };

        if (item.type === "grouped") {
          const worstItem = item.items.reduce((worst, curr) => {
            return stageIndex(curr.realStage) < stageIndex(worst.realStage) ? curr : worst;
          }, item.items[0]);
          const globalStage = worstItem.realStage;
          const globalBatStatus = worstItem.batStatus;
          row.innerHTML = `
            <span style="font-size:13px;font-weight:600;color:#111827;font-family:monospace;">${item.numeroDossier} <span style="color:#9ca3af;font-weight:400;font-size:11px;">(${item.items.length} fichier(s))</span></span>
            <span style="font-size:12px;color:#6b7280;">${item.date ? new Date(item.date).toLocaleDateString("fr-FR") : '—'}</span>
            <span style="font-size:12px;color:#6b7280;">${item.size ? fmtBytes(item.size) : '—'}</span>
            <span style="background:#dbeafe;color:#1e40af;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:500;">${getStageLabelDisplay(globalStage, globalBatStatus)}</span>
          `;
          row.onclick = () => openGroupedDossierDetail(item.numeroDossier, item.items);
        } else {
          row.innerHTML = `
            <span style="font-size:13px;color:#374151;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${item.folder.fileName || '—'}</span>
            <span style="font-size:12px;color:#6b7280;">${item.date ? new Date(item.date).toLocaleDateString("fr-FR") : '—'}</span>
            <span style="font-size:12px;color:#6b7280;">${item.size ? fmtBytes(item.size) : '—'}</span>
            <span style="background:#dbeafe;color:#1e40af;padding:2px 8px;border-radius:10px;font-size:11px;font-weight:500;">${getStageLabelDisplay(item.realStage, item.batStatus)}</span>
          `;
          row.onclick = (e) => { if (e.target.closest(".btn-danger")) return; openDossierDetail(item.folder._id || item.folder.id); };
        }
        table.appendChild(row);
      }
      listEl.appendChild(table);
    } else {
      // Grid view
      const grid = document.createElement("div");
      grid.style.cssText = "display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:16px;";

      for (const item of allItems) {
        if (item.type === "grouped") {
          const worstItem = item.items.reduce((worst, curr) => {
            return stageIndex(curr.realStage) < stageIndex(worst.realStage) ? curr : worst;
          }, item.items[0]);
          const globalStage = worstItem.realStage;
          const globalBatStatus = worstItem.batStatus;

          const card = document.createElement("div");
          card.className = "dossier-card";
          card.style.cssText = "background:white;border:1px solid #e5e7eb;border-radius:16px;padding:20px;box-shadow:0 2px 12px rgba(0,0,0,0.07);cursor:pointer;transition:all 0.2s;";
          card.onmouseenter = () => { card.style.boxShadow = "0 4px 20px rgba(0,0,0,0.13)"; card.style.transform = "translateY(-2px)"; };
          card.onmouseleave = () => { card.style.boxShadow = "0 2px 12px rgba(0,0,0,0.07)"; card.style.transform = ""; };

          const filesHtml = item.items.map(({ folder, realStage, batStatus }) => `
            <div style="display:flex;justify-content:space-between;align-items:center;padding:4px 0;border-top:1px solid #f3f4f6;margin-top:4px;">
              <span style="font-size:11px;color:#374151;font-family:monospace;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:160px;" title="${folder.fileName || ''}">${folder.fileName || '—'}</span>
              <span style="background:#f3f4f6;color:#6b7280;padding:2px 6px;border-radius:8px;font-size:10px;white-space:nowrap;margin-left:4px;">${getStageLabelDisplay(realStage, batStatus)}</span>
            </div>
          `).join('');

          card.innerHTML = `
            <div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:12px;min-width:0;">
              <div style="font-size:28px;line-height:1;">📁</div>
              <div style="min-width:0;flex:1;">
                <div style="font-size:20px;font-weight:800;color:#111827;font-family:monospace;line-height:1.2;word-break:break-word;" class="dossier-card-name">${item.numeroDossier}</div>
                <div style="font-size:12px;color:#6b7280;margin-top:2px;">${item.items.length} fichier(s)</div>
              </div>
            </div>
            <div style="margin-bottom:10px;">${filesHtml}</div>
            <div style="display:flex;justify-content:space-between;align-items:center;">
              <span style="background:#dbeafe;color:#1e40af;padding:4px 10px;border-radius:20px;font-size:12px;font-weight:500;">${getStageLabelDisplay(globalStage, globalBatStatus)}</span>
              <span style="color:#6b7280;font-size:11px;">étape globale</span>
            </div>
          `;
          card.onclick = (e) => { if (e.target.closest(".btn-danger")) return; openGroupedDossierDetail(item.numeroDossier, item.items); };
          grid.appendChild(card);
        } else {
          const { folder, realStage, batStatus } = item;
          const folderName = folder.fileName || '';
          const card = document.createElement("div");
          card.className = "dossier-card";
          card.style.cssText = "background:white;border:1px solid #e5e7eb;border-radius:16px;padding:20px;box-shadow:0 2px 12px rgba(0,0,0,0.07);cursor:pointer;transition:all 0.2s;";
          card.onmouseenter = () => { card.style.boxShadow = "0 4px 20px rgba(0,0,0,0.13)"; card.style.transform = "translateY(-2px)"; };
          card.onmouseleave = () => { card.style.boxShadow = "0 2px 12px rgba(0,0,0,0.07)"; card.style.transform = ""; };
          card.innerHTML = `
            <div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:12px;min-width:0;">
              <div style="font-size:28px;line-height:1;">📄</div>
              <div style="min-width:0;flex:1;">
                <div style="font-size:15px;font-weight:600;color:#374151;line-height:1.2;word-break:break-word;" class="dossier-card-name">${folderName || 'Dossier'}</div>
                <div style="font-size:12px;color:#6b7280;margin-top:2px;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : ''}</div>
              </div>
            </div>
            <div style="display:flex;justify-content:space-between;align-items:center;">
              <span style="background:#dbeafe;color:#1e40af;padding:4px 10px;border-radius:20px;font-size:12px;font-weight:500;">${getStageLabelDisplay(realStage, batStatus)}</span>
              <span style="color:#6b7280;font-size:12px;">${typeof folder.files === 'number' ? folder.files : 0} fichier(s)</span>
            </div>
          `;
          card.onclick = (e) => { if (e.target.closest(".btn-danger")) return; openDossierDetail(folder._id || folder.id); };

          const btnDelete = document.createElement("button");
          btnDelete.className = "btn btn-danger btn-sm";
          btnDelete.textContent = "Supprimer";
          btnDelete.style.cssText = "margin-top:12px;width:100%;";
          btnDelete.onclick = async (e) => {
            e.stopPropagation();
            if (!confirm(`Supprimer le dossier "${folderName}" et tous ses fichiers ?`)) return;
            const r = await fetch(`/api/production-folder?path=${encodeURIComponent(folder.path || folder.folderPath || "")}`, { method: "DELETE" }).then(r=>r.json()).catch(()=>({ok:false,error:"Erreur réseau"}));
            if (r.ok) { showNotification("Dossier supprimé", "success"); loadDossiersList(); }
            else showNotification("Erreur: " + (r.error || ""), "error");
          };
          card.appendChild(btnDelete);
          grid.appendChild(card);
        }
      }
      listEl.appendChild(grid);
    }
  } catch (err) {
    listEl.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

// ======================================================
// DOSSIER REGROUPÉ — Detail avec tous les PDFs
// ======================================================
export async function openGroupedDossierDetail(numeroDossier, items) {
  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:flex-start;justify-content:center;padding:40px 20px;overflow-y:auto;";

  const modal = document.createElement("div");
  modal.style.cssText = "background:white;border-radius:16px;padding:32px;width:100%;max-width:860px;box-shadow:0 20px 60px rgba(0,0,0,0.3);";
  modal.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;">
      <h2 style="margin:0;font-size:22px;color:#111827;">Dossier <strong>${numeroDossier}</strong> — ${items.length} fichier(s)</h2>
      <button id="grp-dossier-close" style="background:none;border:none;font-size:24px;cursor:pointer;color:#6b7280;">✕</button>
    </div>
    <div id="grp-dossier-items"></div>
  `;

  overlay.appendChild(modal);
  document.body.appendChild(overlay);
  modal.querySelector("#grp-dossier-close").onclick = () => overlay.remove();
  overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };

  const itemsEl = modal.querySelector("#grp-dossier-items");

  for (const { folder, realStage, batStatus } of items) {
    const fn = folder.fileName || "";
    const folderId = folder._id || folder.id || "";
    const folderPath = folder.folderPath || "";
    const item = document.createElement("div");
    item.style.cssText = "border:1px solid #e5e7eb;border-radius:12px;margin-bottom:12px;overflow:hidden;";

    // Accordion header
    const header = document.createElement("div");
    header.style.cssText = "display:flex;align-items:center;justify-content:space-between;padding:12px 16px;background:#f9fafb;cursor:pointer;gap:12px;";

    const leftInfo = document.createElement("div");
    leftInfo.style.cssText = "display:flex;align-items:center;gap:10px;min-width:0;flex:1;";
    leftInfo.innerHTML = `
      <span style="font-size:11px;font-weight:700;color:#BC0024;font-family:monospace;padding:3px 7px;background:#fee2e2;border-radius:4px;flex-shrink:0;">PDF</span>
      <span style="font-size:13px;font-weight:600;color:#111827;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${fn}</span>
    `;

    const stageSpan = document.createElement("span");
    stageSpan.style.cssText = "background:#dbeafe;color:#1e40af;padding:3px 10px;border-radius:20px;font-size:11px;font-weight:500;white-space:nowrap;flex-shrink:0;";
    stageSpan.textContent = getStageLabelDisplay(realStage, batStatus);

    const deleteBtn = document.createElement("button");
    deleteBtn.className = "btn btn-danger btn-sm";
    deleteBtn.textContent = "🗑 Supprimer";
    deleteBtn.style.cssText = "flex-shrink:0;font-size:11px;padding:3px 8px;";
    deleteBtn.onclick = async (e) => {
      e.stopPropagation();
      if (!confirm(`Supprimer "${fn}" du dossier ?`)) return;
      const path = folderPath || "";
      const r = await fetch(`/api/production-folder?path=${encodeURIComponent(path)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
      if (r.ok) {
        showNotification(`✅ "${fn}" supprimé`, "success");
        item.remove();
      } else {
        showNotification("❌ " + (r.error || "Erreur suppression"), "error");
      }
    };

    const arrowSpan = document.createElement("span");
    arrowSpan.className = "accordion-arrow";
    arrowSpan.style.cssText = "color:#6b7280;font-size:14px;flex-shrink:0;";
    arrowSpan.textContent = "▶";

    header.appendChild(leftInfo);
    header.appendChild(stageSpan);
    header.appendChild(deleteBtn);
    header.appendChild(arrowSpan);

    const body = document.createElement("div");
    body.style.cssText = "display:none;padding:16px;border-top:1px solid #e5e7eb;";

    let expanded = false;
    header.onclick = async (e) => {
      if (e.target.closest(".btn-danger")) return;
      expanded = !expanded;
      body.style.display = expanded ? "block" : "none";
      arrowSpan.textContent = expanded ? "▼" : "▶";
      if (expanded && !body.dataset.loaded) {
        body.dataset.loaded = "1";
        await loadPdfDetails(fn, folder, body);
      }
    };

    item.appendChild(header);
    item.appendChild(body);
    itemsEl.appendChild(item);
  }

  // Upload zone at the bottom — use the first folder's ID
  const firstFolderId = items[0]?.folder?._id || items[0]?.folder?.id || "";
  if (firstFolderId) {
    const uploadSection = document.createElement("div");
    uploadSection.style.cssText = "margin-top:20px;";
    uploadSection.innerHTML = `
      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Ajouter un fichier</h3>
      <div style="border:2px dashed #e5e7eb;border-radius:12px;padding:20px;text-align:center;cursor:pointer;" id="grp-upload-zone">
        <p style="color:#6b7280;margin:0;">Cliquez ou déposez un fichier (PDF, Excel, Word, PSD, InDesign...)</p>
        <input type="file" id="grp-upload-input" style="display:none;" multiple />
      </div>
    `;
    modal.querySelector("#grp-dossier-items").after(uploadSection);

    const uploadZone = uploadSection.querySelector("#grp-upload-zone");
    const uploadInput = uploadSection.querySelector("#grp-upload-input");
    uploadZone.onclick = () => uploadInput.click();
    uploadZone.addEventListener("dragover", e => { e.preventDefault(); uploadZone.style.background = "#f3f4f6"; });
    uploadZone.addEventListener("dragleave", () => { uploadZone.style.background = ""; });
    uploadZone.addEventListener("drop", async (e) => {
      e.preventDefault();
      uploadZone.style.background = "";
      const files = Array.from(e.dataTransfer.files || []);
      if (files.length) await uploadGroupedDossierFiles(firstFolderId, files, overlay);
    });
    uploadInput.addEventListener("change", async (e) => {
      const files = Array.from(e.target.files || []);
      if (files.length) await uploadGroupedDossierFiles(firstFolderId, files, overlay);
      uploadInput.value = "";
    });
  }
}

async function loadPdfDetails(fileName, folder, container) {
  container.innerHTML = '<p style="color:#6b7280;font-size:13px;">Chargement...</p>';
  // Generate a safe DOM ID (no special characters that break querySelector)
  const safeId = 'pdf_' + Math.random().toString(36).slice(2, 9);
  try {
    // Load fabrication sheet
    let fab = {};
    if (fileName) {
      const fabResp = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fileName), {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => null);
      if (fabResp && fabResp.ok !== false) fab = fabResp;
    }

    // Get real-time stage (also captures fullPath for thumbnail)
    let stage = folder.currentStage || "Début de production";
    let stageBatStatus = null;
    let pdfFullPath = folder.originalFilePath || folder.currentFilePath || "";
    if (fileName) {
      const stageRes = await fetch("/api/file-stage?fileName=" + encodeURIComponent(fileName), {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => null);
      if (stageRes && stageRes.ok && stageRes.folder) stage = stageRes.folder;
      if (stageRes && stageRes.batStatus) stageBatStatus = stageRes.batStatus;
      if (stageRes && stageRes.fullPath) pdfFullPath = stageRes.fullPath;
    }

    const fld = (label, value) => value ? `<div style="margin-bottom:6px;"><span style="font-size:11px;color:#6b7280;font-weight:600;text-transform:uppercase;">${label}</span><div style="font-size:13px;color:#111827;">${value}</div></div>` : "";

    // Façonnage display: handle array or string
    let faconnageDisplay = "";
    if (Array.isArray(fab.faconnage) && fab.faconnage.length > 0) {
      faconnageDisplay = fab.faconnage.join(", ");
    } else if (typeof fab.faconnage === 'string' && fab.faconnage) {
      faconnageDisplay = fab.faconnage;
    }

    const history = Array.isArray(fab.history) ? fab.history : [];
    const historyHtml = history.length === 0
      ? '<span style="color:#9ca3af;font-size:12px;">Aucun historique</span>'
      : history.map(h => `<div style="font-size:12px;color:#374151;padding:3px 0;border-bottom:1px solid #f3f4f6;">${new Date(h.date).toLocaleDateString("fr-FR", {day:"2-digit",month:"2-digit",year:"numeric",hour:"2-digit",minute:"2-digit"})} — ${h.user||''} — ${h.action||''}</div>`).join("");

    container.innerHTML = `
      <div style="display:flex;gap:16px;margin-bottom:16px;align-items:flex-start;">
        <canvas id="thumb-${safeId}" style="border:1px solid #e5e7eb;border-radius:8px;max-width:120px;flex-shrink:0;display:none;"></canvas>
        <div style="flex:1;display:grid;grid-template-columns:1fr 1fr;gap:12px;">
          ${fld("Étape actuelle", `<span style="background:#dbeafe;color:#1e40af;padding:3px 10px;border-radius:20px;font-size:12px;">${getStageLabelDisplay(stage, stageBatStatus)}</span>`)}
          ${fld("Numéro de dossier", fab.numeroDossier)}
          ${fld("Client", fab.client)}
          ${fld("Opérateur", fab.operateur)}
          ${fld("Quantité", fab.quantite)}
          ${fld("Type de travail", fab.typeTravail)}
          ${fld("Format fini", fab.format)}
          ${fld("Moteur d'impression", fab.moteurImpression || fab.machine)}
          ${fld("Recto/Verso", fab.rectoVerso)}
          ${fld("Façonnage", faconnageDisplay)}
          ${fld("Délai", fab.delai ? new Date(fab.delai).toLocaleDateString("fr-FR") : null)}
          ${fld("Note", fab.notes)}
        </div>
      </div>
      ${(fab.media1 || fab.media2 || fab.media3 || fab.media4) ? `
      <div style="margin-bottom:16px;">
        <div style="font-size:11px;color:#6b7280;font-weight:600;text-transform:uppercase;margin-bottom:6px;">Médias</div>
        <div style="display:flex;gap:8px;flex-wrap:wrap;">
          ${fab.media1 ? `<span style="background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;">Media 1: ${fab.media1}</span>` : ""}
          ${fab.media2 ? `<span style="background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;">Media 2: ${fab.media2}</span>` : ""}
          ${fab.media3 ? `<span style="background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;">Media 3: ${fab.media3}</span>` : ""}
          ${fab.media4 ? `<span style="background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:12px;">Media 4: ${fab.media4}</span>` : ""}
        </div>
      </div>` : ""}
      <div style="margin-bottom:12px;">
        <div style="font-size:11px;color:#6b7280;font-weight:600;text-transform:uppercase;margin-bottom:6px;">Historique des mouvements</div>
        <div style="max-height:150px;overflow-y:auto;">${historyHtml}</div>
      </div>
      <button class="btn btn-sm btn-primary" id="pdf-open-fiche-${safeId}">Ouvrir la fiche de fabrication</button>
    `;

    // Render PDF thumbnail using pdf.js
    if (pdfFullPath && typeof pdfjsLib !== 'undefined') {
      const canvas = document.getElementById('thumb-' + safeId);
      if (canvas) {
        try {
          const pdfUrl = "/api/file?path=" + encodeURIComponent(pdfFullPath);
          const pdf = await pdfjsLib.getDocument(pdfUrl).promise;
          const page = await pdf.getPage(1);
          const viewport = page.getViewport({ scale: 0.4 });
          canvas.width = viewport.width;
          canvas.height = viewport.height;
          await page.render({ canvasContext: canvas.getContext("2d"), viewport }).promise;
          canvas.style.display = "block";
        } catch(e) { console.warn("PDF thumbnail failed for", pdfFullPath, e); /* thumbnail failed silently */ }
      }
    }

    document.getElementById('pdf-open-fiche-' + safeId)?.addEventListener("click", () => {
      const path = (folder.originalFilePath || folder.currentFilePath || pdfFullPath || fileName || "");
      if (path && window._openFabrication) window._openFabrication(path);
      else if (window._openFabrication) window._openFabrication(fileName);
    });
  } catch (err) {
    container.innerHTML = `<p style="color:#ef4444;font-size:13px;">Erreur : ${err.message}</p>`;
  }
}


export async function openDossierDetail(dossierId) {
  try {
    const folder = await fetch(`/api/production-folders/${dossierId}`, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (!folder) {
      showNotification("❌ Dossier introuvable", "error");
      return;
    }

    const fabFilePath = folder.originalFilePath || folder.currentFilePath || "";
    const fabFileName = folder.fileName || "";
    let fab = {};
    if (fabFileName) {
      try {
        const fabResp = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fabFileName), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    if (!fab.client && !fab.numeroDossier && fabFilePath) {
      try {
        const fabResp = await fetch("/api/fabrication?fullPath=" + encodeURIComponent(fabFilePath), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    if (!fab.client && !fab.numeroDossier) {
      const embedded = folder.fabricationSheet || {};
      if (Object.keys(embedded).length > 0) fab = embedded;
    }

    // Get real-time stage from physical scan (handles Acrobat moves and BAT_ prefix)
    let realTimeStage = folder.currentStage || 'Début de production';
    let realTimeBatStatus = null;
    if (fabFileName) {
      try {
        const stageRes = await fetch("/api/file-stage?fileName=" + encodeURIComponent(fabFileName), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json()).catch(() => null);
        if (stageRes && stageRes.ok && stageRes.folder) realTimeStage = stageRes.folder;
        if (stageRes && stageRes.batStatus) realTimeBatStatus = stageRes.batStatus;
      } catch(e) { /* use stored stage */ }
    }

    const overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:flex-start;justify-content:center;padding:40px 20px;overflow-y:auto;";

    const modal = document.createElement("div");
    modal.style.cssText = "background:white;border-radius:16px;padding:32px;width:100%;max-width:800px;box-shadow:0 20px 60px rgba(0,0,0,0.3);";

    const numeroDossier = fab.numeroDossier || folder.numeroDossier || String(folder.number||0).padStart(3,'0');
    modal.innerHTML = `
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;">
        <h2 style="margin:0;font-size:22px;color:#111827;">Dossier ${numeroDossier} — ${folder.fileName||''}</h2>
        <button id="dossier-close" style="background:none;border:none;font-size:24px;cursor:pointer;color:#6b7280;">✕</button>
      </div>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px;">
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">ÉTAPE ACTUELLE</label>
          <span style="background:#dbeafe;color:#1e40af;padding:6px 12px;border-radius:20px;font-size:13px;font-weight:500;">${getStageLabelDisplay(realTimeStage, realTimeBatStatus)}</span>
        </div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">DATE DE CRÉATION</label>
          <span style="font-size:14px;color:#111827;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : '—'}</span>
        </div>
      </div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fiche de fabrication</h3>
      <div style="margin-bottom:24px;">
        <button id="df-open-fiche" class="btn btn-primary">Ouvrir la fiche de fabrication</button>
      </div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fichiers par étape</h3>
      <div id="df-files" style="margin-bottom:24px;"></div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Ajouter un fichier</h3>
      <div style="border:2px dashed #e5e7eb;border-radius:12px;padding:20px;text-align:center;cursor:pointer;" id="df-upload-zone">
        <p style="color:#6b7280;margin:0;">Cliquez ou déposez un fichier (PDF, Excel, Word, PSD, InDesign...)</p>
        <input type="file" id="df-upload-input" style="display:none;" multiple />
      </div>
    `;

    const filesEl = modal.querySelector("#df-files");
    const files = folder.files || [];
    if (files.length === 0) {
      filesEl.innerHTML = '<p style="color:#9ca3af;font-size:13px;">Aucun fichier dans ce dossier</p>';
    } else {
      files.forEach(f => {
        const row = document.createElement("div");
        row.style.cssText = "display:flex;align-items:center;gap:12px;padding:10px 14px;background:#f9fafb;border-radius:8px;margin-bottom:8px;";
        row.innerHTML = `
          <span style="font-size:13px;font-weight:700;color:#BC0024;font-family:monospace;">PDF</span>
          <div style="flex:1;">
            <div style="font-size:13px;font-weight:600;color:#111827;">${f.fileName||''}</div>
            <div style="font-size:11px;color:#6b7280;">${f.stage||''} · ${f.addedAt ? new Date(f.addedAt).toLocaleDateString("fr-FR") : ''}</div>
          </div>
          <a href="/api/production-folders/${dossierId}/files/${encodeURIComponent(f.fileName||'')}" target="_blank" class="btn btn-sm">Télécharger</a>
        `;
        filesEl.appendChild(row);
      });
    }

    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    modal.querySelector("#dossier-close").onclick = () => overlay.remove();
    overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };

    modal.querySelector("#df-open-fiche").onclick = () => {
      const fichePath = fabFilePath || fabFileName || "";
      if (!fichePath) { showNotification("❌ Chemin du fichier introuvable", "error"); return; }
      openFabrication(fichePath);
    };

    const uploadZone = modal.querySelector("#df-upload-zone");
    const uploadInput = modal.querySelector("#df-upload-input");
    uploadZone.onclick = () => uploadInput.click();
    uploadZone.addEventListener("dragover", e => { e.preventDefault(); uploadZone.style.background = "#f3f4f6"; });
    uploadZone.addEventListener("dragleave", () => { uploadZone.style.background = ""; });
    uploadZone.addEventListener("drop", async (e) => {
      e.preventDefault();
      uploadZone.style.background = "";
      const files = Array.from(e.dataTransfer.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
    });
    uploadInput.addEventListener("change", async (e) => {
      const files = Array.from(e.target.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
      uploadInput.value = "";
    });

  } catch (err) {
    showNotification("❌ " + err.message, "error");
  }
}

async function uploadDossierFiles(dossierId, files, overlay) {
  for (const file of files) {
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch(`/api/production-folders/${dossierId}/upload`, {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` },
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${file.name} ajouté`, "success");
    } else {
      showNotification(`❌ Erreur upload ${file.name}`, "error");
    }
  }
  overlay.remove();
  openDossierDetail(dossierId);
}

async function uploadGroupedDossierFiles(dossierId, files, overlay) {
  for (const file of files) {
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch(`/api/production-folders/${dossierId}/upload`, {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` },
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${file.name} ajouté`, "success");
    } else {
      showNotification(`❌ Erreur upload ${file.name}`, "error");
    }
  }
  overlay.remove();
  loadDossiersList();
}
