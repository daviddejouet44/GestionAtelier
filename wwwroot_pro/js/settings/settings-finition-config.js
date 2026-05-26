// settings-finition-config.js — Finition time rules, sheet formulas, rainage options
import { authToken, showNotification, esc } from '../core.js';

// ─────────────────────────────────────────────────────────────────────────────
// RAINAGE OPTIONS
// ─────────────────────────────────────────────────────────────────────────────

export async function renderSettingsRainageOptions(panel) {
  panel.innerHTML = `<h3>Options Rainage</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { options: [] };
  try {
    cfg = await fetch("/api/settings/rainage-options", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
  } catch(e) { /* ignore */ }

  renderRainageUI(panel, cfg.options || []);
}

function renderRainageUI(panel, options) {
  panel.innerHTML = `
    <h3>Options Rainage</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:16px;">
      Définissez les types de rainage disponibles dans la fiche de production.<br>
      Lorsqu'au moins une option est configurée, le champ Rainage devient un menu déroulant à la place d'une simple case à cocher.
    </p>
    <div style="max-width:480px;">
      <div id="rainage-list" style="margin-bottom:12px;"></div>
      <div style="display:flex;gap:8px;margin-bottom:16px;">
        <input id="rainage-new" class="settings-input" placeholder="Ex: Rainage croisé" style="flex:1;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        <button id="rainage-add" class="btn btn-primary">＋ Ajouter</button>
      </div>
      <button id="rainage-save" class="btn btn-primary">💾 Enregistrer</button>
      <div id="rainage-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  const listEl  = panel.querySelector("#rainage-list");
  const newInp  = panel.querySelector("#rainage-new");
  const addBtn  = panel.querySelector("#rainage-add");
  const saveBtn = panel.querySelector("#rainage-save");
  const msgEl   = panel.querySelector("#rainage-msg");

  let opts = [...options];

  function refreshList() {
    listEl.innerHTML = opts.length === 0
      ? `<p style="color:#9ca3af;font-size:13px;">Aucune option définie — le champ Rainage restera une case à cocher.</p>`
      : opts.map((o, i) => `
          <div style="display:flex;align-items:center;gap:8px;padding:5px 8px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;margin-bottom:4px;">
            <span style="flex:1;font-size:13px;">${esc(o)}</span>
            <button class="btn rainage-del" data-i="${i}" style="padding:2px 7px;font-size:11px;color:#ef4444;border-color:#ef4444;">✕</button>
          </div>`
        ).join('');
    listEl.querySelectorAll(".rainage-del").forEach(btn => {
      btn.onclick = () => { opts.splice(parseInt(btn.dataset.i), 1); refreshList(); };
    });
  }
  refreshList();

  addBtn.onclick = () => {
    const v = newInp.value.trim();
    if (!v) return;
    if (opts.includes(v)) { msgEl.style.color="#ef4444"; msgEl.textContent="Cette option existe déjà."; return; }
    opts.push(v);
    newInp.value = '';
    refreshList();
  };
  newInp.onkeydown = e => { if (e.key === 'Enter') addBtn.click(); };

  saveBtn.onclick = async () => {
    try {
      const r = await fetch("/api/settings/rainage-options", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ options: opts })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Options enregistrées";
        showNotification("✅ Options rainage enregistrées", "success");
        if (window._invalidateFabFormConfig) window._invalidateFabFormConfig();
      } else {
        msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// FINITION TIME RULES
// ─────────────────────────────────────────────────────────────────────────────

export async function renderSettingsFinitionTimeRules(panel) {
  panel.innerHTML = `<h3>Temps indicatifs par finition</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { rules: [] };
  let faconnageOptions = [];
  try {
    [cfg, faconnageOptions] = await Promise.all([
      fetch("/api/settings/finition-time-rules", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>({rules:[]})),
      fetch("/api/settings/faconnage-options", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>[])
    ]);
  } catch(e) { /* ignore */ }
  renderFinitionTimeUI(panel, cfg.rules || [], faconnageOptions);
}

function renderFinitionTimeUI(panel, rules, faconnageOptions) {
  const FINITIONS_KNOWN = [
    'Dorure à chaud : Or','Dorure à chaud : Argent',
    'Pelliculage : Mat recto','Pelliculage : Mat recto/verso',
    'Pelliculage : Brillant recto','Pelliculage : Brillant recto/verso',
    'Pelliculage : Soft Touch recto','Pelliculage : Soft Touch recto/verso',
    'Vernis sélectif',
    ...(Array.isArray(faconnageOptions) ? faconnageOptions : [])
  ];

  panel.innerHTML = `
    <h3>Temps indicatifs par finition</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:16px;">
      Associez un temps de production (en minutes) à chaque type de finition.
      Ces temps sont additionnés au temps de production global selon les finitions sélectionnées dans la fiche.
    </p>
    <table style="width:100%;max-width:680px;border-collapse:collapse;margin-bottom:16px;font-size:13px;">
      <thead>
        <tr style="background:#f3f4f6;">
          <th style="text-align:left;padding:7px 10px;">Finition</th>
          <th style="text-align:left;padding:7px 10px;">Temps (min)</th>
          <th style="text-align:left;padding:7px 10px;">Notes</th>
          <th style="width:40px;padding:7px 10px;"></th>
        </tr>
      </thead>
      <tbody id="ft-rules-tbody"></tbody>
    </table>
    <h4 style="margin-bottom:8px;">Ajouter / modifier une règle</h4>
    <div style="display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;max-width:680px;margin-bottom:16px;">
      <div style="flex:2;min-width:200px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Finition</label>
        <input id="ft-new-name" list="ft-finitions-list" class="settings-input" placeholder="Nom de la finition"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        <datalist id="ft-finitions-list">
          ${FINITIONS_KNOWN.map(f=>`<option value="${esc(f)}">`).join('')}
        </datalist>
      </div>
      <div style="width:100px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Minutes</label>
        <input id="ft-new-time" type="number" min="0" class="settings-input" placeholder="0"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
      </div>
      <div style="flex:1;min-width:150px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Notes (optionnel)</label>
        <input id="ft-new-notes" class="settings-input" placeholder="Ex: par 500 ex."
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
      </div>
      <div style="display:flex;align-items:flex-end;padding-bottom:1px;">
        <button id="ft-add" class="btn btn-primary" style="white-space:nowrap;">＋ Ajouter</button>
      </div>
    </div>
    <button id="ft-save" class="btn btn-primary">💾 Enregistrer</button>
    <div id="ft-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  let localRules = rules.map(r => ({...r}));

  function refreshTable() {
    const tbody = panel.querySelector("#ft-rules-tbody");
    tbody.innerHTML = localRules.length === 0
      ? `<tr><td colspan="4" style="color:#9ca3af;padding:10px;text-align:center;">Aucune règle définie</td></tr>`
      : localRules.map((r,i) => `
          <tr style="border-bottom:1px solid #f3f4f6;">
            <td style="padding:6px 10px;">${esc(r.finitionName)}</td>
            <td style="padding:6px 10px;font-weight:600;">${r.timeMinutes} min</td>
            <td style="padding:6px 10px;color:#6b7280;font-size:12px;">${esc(r.notes||'')}</td>
            <td style="padding:6px 10px;">
              <button class="btn ft-del" data-i="${i}" style="padding:2px 7px;font-size:11px;color:#ef4444;border-color:#ef4444;">✕</button>
            </td>
          </tr>`
        ).join('');
    tbody.querySelectorAll(".ft-del").forEach(btn => {
      btn.onclick = () => { localRules.splice(parseInt(btn.dataset.i), 1); refreshTable(); };
    });
  }
  refreshTable();

  panel.querySelector("#ft-add").onclick = () => {
    const name  = panel.querySelector("#ft-new-name").value.trim();
    const time  = parseInt(panel.querySelector("#ft-new-time").value) || 0;
    const notes = panel.querySelector("#ft-new-notes").value.trim();
    if (!name) return;
    const existing = localRules.findIndex(r => r.finitionName === name);
    if (existing >= 0) localRules[existing] = { finitionName: name, timeMinutes: time, notes: notes || null };
    else localRules.push({ finitionName: name, timeMinutes: time, notes: notes || null });
    panel.querySelector("#ft-new-name").value  = '';
    panel.querySelector("#ft-new-time").value  = '';
    panel.querySelector("#ft-new-notes").value = '';
    refreshTable();
  };

  panel.querySelector("#ft-save").onclick = async () => {
    const msgEl = panel.querySelector("#ft-msg");
    try {
      const r = await fetch("/api/settings/finition-time-rules", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ rules: localRules })
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color="#16a34a"; msgEl.textContent="✅ Règles enregistrées"; showNotification("✅ Temps finitions enregistrés","success"); }
      else       { msgEl.style.color="#ef4444"; msgEl.textContent="❌ "+(r.error||"Erreur"); }
    } catch(e)   { msgEl.style.color="#ef4444"; msgEl.textContent="❌ Erreur réseau"; }
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// FINITION SHEET FORMULAS
// ─────────────────────────────────────────────────────────────────────────────

export async function renderSettingsFinitionSheetFormulas(panel) {
  panel.innerHTML = `<h3>Formules feuilles finitions</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { formulas: [] };
  let workTypes = [];
  let faconnageOptions = [];
  try {
    [cfg, workTypes, faconnageOptions] = await Promise.all([
      fetch("/api/settings/finition-sheet-formulas", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>({formulas:[]})),
      fetch("/api/config/work-types", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>[]),
      fetch("/api/settings/faconnage-options", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r=>r.json()).catch(()=>[])
    ]);
  } catch(e) { /* ignore */ }
  renderSheetFormulaUI(panel, cfg.formulas || [], workTypes, faconnageOptions);
}

function renderSheetFormulaUI(panel, formulas, workTypes, faconnageOptions) {
  const FINITIONS_KNOWN = [
    'Dorure à chaud : Or','Dorure à chaud : Argent',
    'Pelliculage : Mat recto','Pelliculage : Mat recto/verso',
    'Pelliculage : Brillant recto','Pelliculage : Brillant recto/verso',
    'Pelliculage : Soft Touch recto','Pelliculage : Soft Touch recto/verso',
    'Vernis sélectif',
    ...(Array.isArray(faconnageOptions) ? faconnageOptions : [])
  ];
  const typeOptions = ['', ...workTypes].map(t =>
    t ? `<option value="${esc(t)}">${esc(t)}</option>` : `<option value="">— Toutes les combinaisons —</option>`
  ).join('');

  panel.innerHTML = `
    <h3>Formules — Nombre de feuilles par finition</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:16px;">
      Définissez combien de feuilles supplémentaires (passes) une finition requiert selon le type de travail.<br>
      <strong>Exemple :</strong> "Dorure à chaud" sur "Brochure" = 1 seule feuille (pas toutes les feuilles du tirage).
    </p>
    <table style="width:100%;max-width:760px;border-collapse:collapse;margin-bottom:16px;font-size:13px;">
      <thead>
        <tr style="background:#f3f4f6;">
          <th style="text-align:left;padding:7px 10px;">Finition</th>
          <th style="text-align:left;padding:7px 10px;">Type de travail</th>
          <th style="text-align:left;padding:7px 10px;">Feuilles (override)</th>
          <th style="text-align:left;padding:7px 10px;">Notes</th>
          <th style="width:40px;padding:7px 10px;"></th>
        </tr>
      </thead>
      <tbody id="fsf-tbody"></tbody>
    </table>
    <h4 style="margin-bottom:8px;">Ajouter / modifier une formule</h4>
    <div style="display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;max-width:760px;margin-bottom:16px;">
      <div style="flex:2;min-width:180px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Finition</label>
        <input id="fsf-new-fin" list="fsf-fin-list" class="settings-input" placeholder="Nom de la finition"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        <datalist id="fsf-fin-list">
          ${FINITIONS_KNOWN.map(f=>`<option value="${esc(f)}">`).join('')}
        </datalist>
      </div>
      <div style="flex:2;min-width:160px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Type de travail (optionnel)</label>
        <select id="fsf-new-type" class="settings-input" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;">
          ${typeOptions}
        </select>
      </div>
      <div style="width:110px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Feuilles</label>
        <input id="fsf-new-sheets" type="number" min="0" class="settings-input" placeholder="1"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
      </div>
      <div style="flex:1;min-width:140px;">
        <label style="font-size:11px;font-weight:600;display:block;margin-bottom:3px;">Notes</label>
        <input id="fsf-new-notes" class="settings-input" placeholder="Optionnel"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
      </div>
      <div style="display:flex;align-items:flex-end;padding-bottom:1px;">
        <button id="fsf-add" class="btn btn-primary">＋ Ajouter</button>
      </div>
    </div>
    <button id="fsf-save" class="btn btn-primary">💾 Enregistrer</button>
    <div id="fsf-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  let localFormulas = formulas.map(f => ({...f}));

  function refreshTable() {
    const tbody = panel.querySelector("#fsf-tbody");
    tbody.innerHTML = localFormulas.length === 0
      ? `<tr><td colspan="5" style="color:#9ca3af;padding:10px;text-align:center;">Aucune formule définie</td></tr>`
      : localFormulas.map((f,i) => `
          <tr style="border-bottom:1px solid #f3f4f6;">
            <td style="padding:6px 10px;">${esc(f.finitionName)}</td>
            <td style="padding:6px 10px;color:#6b7280;">${esc(f.typeTravail||'Tous')}</td>
            <td style="padding:6px 10px;font-weight:600;">${f.sheetsOverride} feuille(s)</td>
            <td style="padding:6px 10px;font-size:12px;color:#6b7280;">${esc(f.notes||'')}</td>
            <td style="padding:6px 10px;">
              <button class="btn fsf-del" data-i="${i}" style="padding:2px 7px;font-size:11px;color:#ef4444;border-color:#ef4444;">✕</button>
            </td>
          </tr>`
        ).join('');
    tbody.querySelectorAll(".fsf-del").forEach(btn => {
      btn.onclick = () => { localFormulas.splice(parseInt(btn.dataset.i), 1); refreshTable(); };
    });
  }
  refreshTable();

  panel.querySelector("#fsf-add").onclick = () => {
    const fin    = panel.querySelector("#fsf-new-fin").value.trim();
    const type   = panel.querySelector("#fsf-new-type").value;
    const sheets = parseInt(panel.querySelector("#fsf-new-sheets").value) || 1;
    const notes  = panel.querySelector("#fsf-new-notes").value.trim();
    if (!fin) return;
    const key = fin + '|' + type;
    const existing = localFormulas.findIndex(f => f.finitionName===fin && (f.typeTravail||'') === type);
    const item = { finitionName: fin, typeTravail: type||null, sheetsOverride: sheets, notes: notes||null };
    if (existing >= 0) localFormulas[existing] = item;
    else localFormulas.push(item);
    panel.querySelector("#fsf-new-fin").value    = '';
    panel.querySelector("#fsf-new-sheets").value = '';
    panel.querySelector("#fsf-new-notes").value  = '';
    refreshTable();
  };

  panel.querySelector("#fsf-save").onclick = async () => {
    const msgEl = panel.querySelector("#fsf-msg");
    try {
      const r = await fetch("/api/settings/finition-sheet-formulas", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ formulas: localFormulas })
      }).then(r => r.json());
      if (r.ok) { msgEl.style.color="#16a34a"; msgEl.textContent="✅ Formules enregistrées"; showNotification("✅ Formules feuilles enregistrées","success"); }
      else       { msgEl.style.color="#ef4444"; msgEl.textContent="❌ "+(r.error||"Erreur"); }
    } catch(e)   { msgEl.style.color="#ef4444"; msgEl.textContent="❌ Erreur réseau"; }
  };
}
