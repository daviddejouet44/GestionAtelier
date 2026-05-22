// settings-fabrication-imports.js — Catalogue papiers (import XML + CSV + ajout manuel)
import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>Catalogue papiers</h3><p style="color:#6b7280;">Chargement...</p>`;

  let importCfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  let customPapers = [];
  try {
    const [cfgResp, customResp] = await Promise.all([
      fetch("/api/config/fabrication-imports", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>({})),
      fetch("/api/config/paper-catalog/custom",  { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>({papers:[]}))
    ]);
    if (cfgResp.ok && cfgResp.config) importCfg = cfgResp.config;
    if (customResp.ok) customPapers = customResp.papers || [];
  } catch(e) { /* use defaults */ }

  renderPaperUI(panel, importCfg, customPapers);
}

function renderPaperUI(panel, importCfg, customPapers) {
  panel.innerHTML = `
    <h3>Catalogue papiers</h3>

    <!-- ── XML catalog paths ── -->
    <div class="settings-section-card" style="margin-bottom:20px;">
      <h4 style="margin:0 0 8px;">📄 Catalogues XML (import automatique)</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">
        Chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.
      </p>
      <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${esc(importCfg.media1Path||'')}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
      <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${esc(importCfg.media2Path||'')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
      <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${esc(importCfg.media3Path||'')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
      <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${esc(importCfg.media4Path||'')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
      <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${esc(importCfg.typeDocumentPath||'')}" class="settings-input" style="width:100%;max-width:500px;" /></div>
      <button id="fi-save-paths" class="btn btn-primary" style="margin-top:4px;">Enregistrer les chemins</button>
      <div id="fi-paths-msg" style="margin-top:6px;font-size:13px;"></div>
    </div>

    <!-- ── Import CSV ── -->
    <div class="settings-section-card" style="margin-bottom:20px;">
      <h4 style="margin:0 0 8px;">📥 Import CSV</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">
        Importez un fichier CSV contenant vos papiers. Format attendu (séparateur <code>;</code> ou <code>,</code>) :<br>
        <code>Nom du papier ; Grammage ; Format ; Fabricant ; Notes</code><br>
        Seule la première colonne (nom) est obligatoire. Les doublons sont ignorés.
      </p>
      <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;">
        <input type="file" id="fi-csv-file" accept=".csv,.txt" class="settings-input" style="max-width:320px;" />
        <button id="fi-csv-import" class="btn btn-primary">📥 Importer</button>
      </div>
      <div id="fi-csv-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

    <!-- ── Manual add ── -->
    <div class="settings-section-card" style="margin-bottom:20px;">
      <h4 style="margin:0 0 8px;">✏️ Ajouter manuellement</h4>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;">
        <div style="flex:2;min-width:180px;">
          <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Nom <span style="color:#ef4444;">*</span></label>
          <input id="fi-man-name" class="settings-input" placeholder="Ex: Couché mat 170g SRA3"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <div style="width:110px;">
          <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Grammage</label>
          <input id="fi-man-grammage" class="settings-input" placeholder="170g"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <div style="width:110px;">
          <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Format</label>
          <input id="fi-man-format" class="settings-input" placeholder="SRA3"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <div style="flex:1;min-width:140px;">
          <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Fabricant</label>
          <input id="fi-man-fabricant" class="settings-input" placeholder="Sappi"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <div style="flex:1;min-width:140px;">
          <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Notes</label>
          <input id="fi-man-notes" class="settings-input" placeholder="Optionnel"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <button id="fi-man-add" class="btn btn-primary">＋ Ajouter</button>
      </div>
      <div id="fi-man-msg" style="margin-top:6px;font-size:13px;"></div>
    </div>

    <!-- ── Custom papers list ── -->
    <div class="settings-section-card">
      <h4 style="margin:0 0 8px;">📋 Papiers personnalisés (<span id="fi-count">${customPapers.length}</span>)</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:10px;">
        Ces papiers s'ajoutent au catalogue XML dans les menus déroulants de la fiche de fabrication.
      </p>
      <div id="fi-papers-list" style="max-height:360px;overflow-y:auto;"></div>
    </div>
  `;

  let localPapers = [...customPapers];

  function refreshList() {
    const listEl = panel.querySelector("#fi-papers-list");
    const countEl = panel.querySelector("#fi-count");
    if (countEl) countEl.textContent = localPapers.length;
    listEl.innerHTML = localPapers.length === 0
      ? `<p style="color:#9ca3af;font-size:13px;text-align:center;padding:12px;">Aucun papier personnalisé.</p>`
      : `<table style="width:100%;border-collapse:collapse;font-size:12px;">
           <thead><tr style="background:#f3f4f6;">
             <th style="text-align:left;padding:6px 8px;">Nom</th>
             <th style="padding:6px 8px;">Grammage</th>
             <th style="padding:6px 8px;">Format</th>
             <th style="text-align:left;padding:6px 8px;">Fabricant</th>
             <th style="text-align:left;padding:6px 8px;">Notes</th>
             <th style="width:36px;padding:6px 8px;"></th>
           </tr></thead>
           <tbody>
             ${localPapers.map((p,i)=>`
               <tr style="border-bottom:1px solid #f3f4f6;">
                 <td style="padding:5px 8px;">${esc(p.name)}</td>
                 <td style="padding:5px 8px;text-align:center;color:#6b7280;">${esc(p.grammage||'')}</td>
                 <td style="padding:5px 8px;text-align:center;color:#6b7280;">${esc(p.format||'')}</td>
                 <td style="padding:5px 8px;color:#6b7280;">${esc(p.fabricant||'')}</td>
                 <td style="padding:5px 8px;color:#6b7280;">${esc(p.notes||'')}</td>
                 <td style="padding:5px 8px;">
                   <button class="btn fi-del" data-name="${esc(p.name)}" data-i="${i}"
                           style="padding:2px 6px;font-size:11px;color:#ef4444;border-color:#ef4444;">✕</button>
                 </td>
               </tr>`).join('')}
           </tbody>
         </table>`;
    listEl.querySelectorAll(".fi-del").forEach(btn => {
      btn.onclick = async () => {
        const name = btn.dataset.name;
        if (!confirm(`Supprimer "${name}" du catalogue ?`)) return;
        try {
          const r = await fetch(`/api/config/paper-catalog/custom/${encodeURIComponent(name)}`, {
            method: "DELETE",
            headers: { "Authorization": `Bearer ${authToken}` }
          }).then(r=>r.json());
          if (r.ok) {
            localPapers.splice(parseInt(btn.dataset.i), 1);
            refreshList();
            showNotification("✅ Papier supprimé","success");
          } else {
            showNotification("❌ "+(r.error||"Erreur"),"error");
          }
        } catch(e) { showNotification("❌ Erreur réseau","error"); }
      };
    });
  }
  refreshList();

  // ── Save XML paths ──
  panel.querySelector("#fi-save-paths").onclick = async () => {
    const msgEl = panel.querySelector("#fi-paths-msg");
    const r = await fetch("/api/config/fabrication-imports", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({
        media1Path:       panel.querySelector("#fi-media1").value.trim(),
        media2Path:       panel.querySelector("#fi-media2").value.trim(),
        media3Path:       panel.querySelector("#fi-media3").value.trim(),
        media4Path:       panel.querySelector("#fi-media4").value.trim(),
        typeDocumentPath: panel.querySelector("#fi-typedoc").value.trim()
      })
    }).then(r=>r.json());
    if (r.ok) { msgEl.style.color="#16a34a"; msgEl.textContent="✅ Chemins enregistrés"; showNotification("✅ Chemins enregistrés","success"); }
    else       { msgEl.style.color="#ef4444"; msgEl.textContent="❌ "+(r.error||"Erreur"); }
  };

  // ── CSV import ──
  panel.querySelector("#fi-csv-import").onclick = async () => {
    const msgEl = panel.querySelector("#fi-csv-msg");
    const file  = panel.querySelector("#fi-csv-file").files[0];
    if (!file) { msgEl.style.color="#ef4444"; msgEl.textContent="Sélectionnez un fichier CSV."; return; }
    msgEl.style.color="#6b7280"; msgEl.textContent="⏳ Import en cours…";
    const fd = new FormData();
    fd.append("file", file);
    try {
      const r = await fetch("/api/config/paper-catalog/import-csv", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: fd
      }).then(r=>r.json());
      if (r.ok) {
        msgEl.style.color="#16a34a"; msgEl.textContent=`✅ ${r.added} papier(s) importé(s)${r.skipped?`, ${r.skipped} ignoré(s) (déjà présent)`:''}`;
        showNotification(`✅ ${r.added} papier(s) importés`,"success");
        // Reload the custom papers list
        const resp2 = await fetch("/api/config/paper-catalog/custom", { headers: { "Authorization":`Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>({papers:[]}));
        localPapers = resp2.papers || [];
        refreshList();
      } else {
        msgEl.style.color="#ef4444"; msgEl.textContent="❌ "+(r.error||"Erreur");
      }
    } catch(e) { msgEl.style.color="#ef4444"; msgEl.textContent="❌ Erreur réseau"; }
  };

  // ── Manual add ──
  panel.querySelector("#fi-man-add").onclick = async () => {
    const msgEl    = panel.querySelector("#fi-man-msg");
    const name     = panel.querySelector("#fi-man-name").value.trim();
    const grammage = panel.querySelector("#fi-man-grammage").value.trim() || null;
    const format   = panel.querySelector("#fi-man-format").value.trim()   || null;
    const fabricant= panel.querySelector("#fi-man-fabricant").value.trim()|| null;
    const notes    = panel.querySelector("#fi-man-notes").value.trim()    || null;
    if (!name) { msgEl.style.color="#ef4444"; msgEl.textContent="Le nom est obligatoire."; return; }
    msgEl.textContent="";
    try {
      const r = await fetch("/api/config/paper-catalog/add", {
        method: "POST",
        headers: { "Content-Type":"application/json", "Authorization":`Bearer ${authToken}` },
        body: JSON.stringify({ name, grammage, format, fabricant, notes })
      }).then(r=>r.json());
      if (r.ok) {
        localPapers.push({ name, grammage, format, fabricant, notes });
        refreshList();
        panel.querySelector("#fi-man-name").value     = '';
        panel.querySelector("#fi-man-grammage").value = '';
        panel.querySelector("#fi-man-format").value   = '';
        panel.querySelector("#fi-man-fabricant").value= '';
        panel.querySelector("#fi-man-notes").value    = '';
        msgEl.style.color="#16a34a"; msgEl.textContent="✅ Papier ajouté";
        showNotification("✅ Papier ajouté au catalogue","success");
        // Invalidate paper cache in fabrication form
        if (window._invalidateFabFormConfig) window._invalidateFabFormConfig();
      } else {
        msgEl.style.color="#ef4444"; msgEl.textContent="❌ "+(r.error||"Erreur");
      }
    } catch(e) { msgEl.style.color="#ef4444"; msgEl.textContent="❌ Erreur réseau"; }
  };
}
