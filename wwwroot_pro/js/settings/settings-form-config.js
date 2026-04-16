// settings-form-config.js — Configuration dynamique de la fiche de production
import { authToken, showNotification, esc } from '../core.js';

let _formConfig = null; // cached config

export async function renderSettingsFormConfig(panel) {
  panel.innerHTML = `<h3>Fiche de production</h3><p style="color:#6b7280;">Chargement...</p>`;

  try {
    const resp = await fetch("/api/settings/form-config", {
      headers: { "Authorization": `Bearer ${authToken}` }
    });
    _formConfig = await resp.json();
  } catch (e) {
    panel.innerHTML = `<h3>Fiche de production</h3><p style="color:#ef4444;">Erreur de chargement de la configuration.</p>`;
    return;
  }

  renderConfigUI(panel, _formConfig);
}

function renderConfigUI(panel, config) {
  const sections = config.sections || [];
  const fields = config.fields || [];

  panel.innerHTML = `
    <h3>Fiche de production — Configuration des champs</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:20px;">
      Configurez l'ordre, la visibilité et le label des champs affichés dans la fiche de fabrication.
      Les sections et champs masqués n'apparaîtront ni dans la fiche ni dans le PDF généré.
    </p>
    <div style="display:flex;gap:12px;margin-bottom:20px;flex-wrap:wrap;">
      <button id="ffc-save" class="btn btn-primary">💾 Enregistrer</button>
      <button id="ffc-reset" class="btn" style="color:#ef4444;border-color:#ef4444;">🔄 Réinitialiser par défaut</button>
    </div>
    <div id="ffc-msg" style="font-size:13px;margin-bottom:12px;"></div>
    <div id="ffc-sections-container"></div>
  `;

  renderSections(panel, config);

  panel.querySelector("#ffc-save").onclick = () => saveConfig(panel);
  panel.querySelector("#ffc-reset").onclick = () => resetConfig(panel);
}

function renderSections(panel, config) {
  const container = panel.querySelector("#ffc-sections-container");
  if (!container) return;
  container.innerHTML = "";

  const sections = config.sections || [];
  const fields = config.fields || [];

  sections.forEach((section, sectionIdx) => {
    const sectionFields = fields
      .filter(f => f.section === section)
      .sort((a, b) => a.order - b.order);

    const sectionEl = document.createElement("div");
    sectionEl.className = "ffc-section";
    sectionEl.dataset.section = section;
    sectionEl.style.cssText = "border:1px solid #e5e7eb;border-radius:10px;margin-bottom:16px;overflow:hidden;";

    // Section header
    const sectionHeader = document.createElement("div");
    sectionHeader.style.cssText = "background:#f9fafb;padding:10px 14px;display:flex;align-items:center;gap:10px;border-bottom:1px solid #e5e7eb;";
    sectionHeader.innerHTML = `
      <div style="display:flex;gap:4px;">
        <button class="ffc-section-up btn" data-idx="${sectionIdx}" style="padding:2px 8px;font-size:12px;" title="Monter la section">↑</button>
        <button class="ffc-section-down btn" data-idx="${sectionIdx}" style="padding:2px 8px;font-size:12px;" title="Descendre la section">↓</button>
      </div>
      <input type="text" class="ffc-section-name settings-input" value="${esc(section)}" style="font-weight:600;font-size:14px;flex:1;padding:4px 8px;border:1px solid #d1d5db;border-radius:6px;" />
      <span style="font-size:12px;color:#9ca3af;">${sectionFields.length} champ(s)</span>
    `;
    sectionEl.appendChild(sectionHeader);

    // Fields table
    const table = document.createElement("div");
    table.style.cssText = "padding:10px 14px;";
    table.innerHTML = `
    <div style="display:grid;grid-template-columns:40px 1fr 100px 60px 60px 60px 60px;gap:8px;align-items:center;font-size:12px;font-weight:600;color:#6b7280;border-bottom:2px solid #e5e7eb;padding-bottom:8px;margin-bottom:8px;background:#f9fafb;padding:8px 10px;border-radius:6px 6px 0 0;">
        <span>Ordre</span><span>Label</span><span>Largeur</span><span>Visible</span><span>Req.</span><span>Bloqué</span><span></span>
      </div>
      <div class="ffc-fields-list" data-section="${esc(section)}"></div>
    `;
    sectionEl.appendChild(table);

    const fieldsList = table.querySelector(".ffc-fields-list");
    sectionFields.forEach((field, fieldIdx) => {
      const row = createFieldRow(field, fieldIdx, sectionFields.length);
      fieldsList.appendChild(row);
    });

    container.appendChild(sectionEl);
  });

  // Section up/down handlers
  container.querySelectorAll(".ffc-section-up").forEach(btn => {
    btn.onclick = () => {
      const idx = parseInt(btn.dataset.idx);
      if (idx > 0) {
        [config.sections[idx - 1], config.sections[idx]] = [config.sections[idx], config.sections[idx - 1]];
        renderSections(panel, config);
      }
    };
  });
  container.querySelectorAll(".ffc-section-down").forEach(btn => {
    btn.onclick = () => {
      const idx = parseInt(btn.dataset.idx);
      if (idx < config.sections.length - 1) {
        [config.sections[idx], config.sections[idx + 1]] = [config.sections[idx + 1], config.sections[idx]];
        renderSections(panel, config);
      }
    };
  });

  // Field up/down handlers
  container.querySelectorAll(".ffc-field-up").forEach(btn => {
    btn.onclick = () => {
      const section = btn.dataset.section;
      const idx = parseInt(btn.dataset.idx);
      const sectionFieldsSorted = fields.filter(f => f.section === section).sort((a, b) => a.order - b.order);
      if (idx > 0) {
        const a = sectionFieldsSorted[idx - 1];
        const b = sectionFieldsSorted[idx];
        const tmp = a.order; a.order = b.order; b.order = tmp;
        renderSections(panel, config);
      }
    };
  });
  container.querySelectorAll(".ffc-field-down").forEach(btn => {
    btn.onclick = () => {
      const section = btn.dataset.section;
      const idx = parseInt(btn.dataset.idx);
      const sectionFieldsSorted = fields.filter(f => f.section === section).sort((a, b) => a.order - b.order);
      if (idx < sectionFieldsSorted.length - 1) {
        const a = sectionFieldsSorted[idx];
        const b = sectionFieldsSorted[idx + 1];
        const tmp = a.order; a.order = b.order; b.order = tmp;
        renderSections(panel, config);
      }
    };
  });

  // Live-bind visibility/label/width/required changes to config fields
  container.querySelectorAll(".ffc-field-visible").forEach(cb => {
    cb.onchange = () => {
      const f = fields.find(x => x.id === cb.dataset.id);
      if (f) f.visible = cb.checked;
    };
  });
  container.querySelectorAll(".ffc-field-required").forEach(cb => {
    cb.onchange = () => {
      const f = fields.find(x => x.id === cb.dataset.id);
      if (f) f.required = cb.checked;
    };
  });
  container.querySelectorAll(".ffc-field-readonly").forEach(cb => {
    cb.onchange = () => {
      const f = fields.find(x => x.id === cb.dataset.id);
      if (f) {
        f.readOnly = cb.checked;
        // If readOnly, required makes no sense — uncheck it
        if (cb.checked) {
          const reqCb = cb.closest("div,tr")?.parentElement?.querySelector(`.ffc-field-required[data-id="${cb.dataset.id}"]`);
          if (reqCb) { reqCb.checked = false; reqCb.disabled = true; f.required = false; }
        } else {
          const reqCb = cb.closest("div,tr")?.parentElement?.querySelector(`.ffc-field-required[data-id="${cb.dataset.id}"]`);
          if (reqCb) reqCb.disabled = false;
        }
      }
    };
  });
  container.querySelectorAll(".ffc-field-label").forEach(inp => {
    inp.oninput = () => {
      const f = fields.find(x => x.id === inp.dataset.id);
      if (f) f.label = inp.value;
    };
  });
  container.querySelectorAll(".ffc-field-width").forEach(sel => {
    sel.onchange = () => {
      const f = fields.find(x => x.id === sel.dataset.id);
      if (f) f.width = sel.value;
    };
  });
  // Section rename — track current name via dataset to support multi-step renames
  container.querySelectorAll(".ffc-section-name").forEach(inp => {
    inp.dataset.oldName = inp.value; // initialise with original name
    inp.oninput = () => {
      const oldN = inp.dataset.oldName;
      const newN = inp.value.trim();
      if (!newN || newN === oldN) return;
      const secIdx = config.sections.indexOf(oldN);
      if (secIdx >= 0) config.sections[secIdx] = newN;
      fields.forEach(f => { if (f.section === oldN) f.section = newN; });
      inp.dataset.oldName = newN; // update tracked name
    };
  });
}

function createFieldRow(field, fieldIdx, totalFields) {
  const row = document.createElement("div");
  row.style.cssText = "display:grid;grid-template-columns:40px 1fr 100px 60px 60px 60px 60px;gap:8px;align-items:center;padding:6px 10px;border-bottom:1px solid #f3f4f6;transition:background 0.1s;";
  row.onmouseenter = () => row.style.background = "#f9fafb";
  row.onmouseleave = () => row.style.background = "";
  row.innerHTML = `
    <div style="display:flex;gap:2px;">
      <button class="ffc-field-up btn" data-idx="${fieldIdx}" data-section="${esc(field.section || '')}" style="padding:2px 6px;font-size:11px;" ${fieldIdx === 0 ? 'disabled' : ''} title="Monter">↑</button>
      <button class="ffc-field-down btn" data-idx="${fieldIdx}" data-section="${esc(field.section || '')}" style="padding:2px 6px;font-size:11px;" ${fieldIdx >= totalFields - 1 ? 'disabled' : ''} title="Descendre">↓</button>
    </div>
    <div>
      <input type="text" class="ffc-field-label settings-input" data-id="${esc(field.id)}" value="${esc(field.label)}" style="width:100%;padding:4px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
      <span style="font-size:10px;color:#9ca3af;">ID: ${esc(field.id)} · ${esc(field.type)}</span>
    </div>
    <select class="ffc-field-width settings-input" data-id="${esc(field.id)}" style="padding:4px 6px;font-size:12px;border:1px solid #d1d5db;border-radius:6px;">
      <option value="half" ${field.width === 'half' ? 'selected' : ''}>½ largeur</option>
      <option value="full" ${field.width === 'full' ? 'selected' : ''}>Pleine</option>
    </select>
    <label style="display:flex;align-items:center;gap:4px;cursor:pointer;font-size:13px;" title="Afficher ce champ">
      <input type="checkbox" class="ffc-field-visible" data-id="${esc(field.id)}" ${field.visible ? 'checked' : ''} />
      Vis.
    </label>
    <label style="display:flex;align-items:center;gap:4px;cursor:pointer;font-size:13px;" title="Champ obligatoire">
      <input type="checkbox" class="ffc-field-required" data-id="${esc(field.id)}" ${field.required ? 'checked' : ''} ${field.readOnly ? 'disabled' : ''} />
      Req.
    </label>
    <label style="display:flex;align-items:center;gap:4px;cursor:pointer;font-size:13px;" title="Champ en lecture seule (bloqué pour l'opérateur)">
      <input type="checkbox" class="ffc-field-readonly" data-id="${esc(field.id)}" ${field.readOnly ? 'checked' : ''} />
      🔒
    </label>
    <span style="font-size:10px;color:#9ca3af;"></span>
  `;
  return row;
}

async function saveConfig(panel) {
  const msgEl = panel.querySelector("#ffc-msg");
  // Normalise order values based on current DOM order before saving
  normaliseOrders();

  try {
    const r = await fetch("/api/settings/form-config", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(_formConfig)
    }).then(r => r.json());

    if (r.ok) {
      // Invalidate fabrication form cache
      if (window._invalidateFabFormConfig) window._invalidateFabFormConfig();
      if (msgEl) { msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Configuration enregistrée"; }
      showNotification("✅ Configuration de la fiche enregistrée", "success");
    } else {
      if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
    }
  } catch (e) {
    if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
  }
}

async function resetConfig(panel) {
  if (!confirm("Réinitialiser la configuration par défaut ? Toutes vos modifications seront perdues.")) return;
  const msgEl = panel.querySelector("#ffc-msg");
  try {
    const r = await fetch("/api/settings/form-config", {
      method: "DELETE",
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());

    if (r.ok) {
      _formConfig = r.config;
      if (window._invalidateFabFormConfig) window._invalidateFabFormConfig();
      renderConfigUI(panel, _formConfig);
      if (msgEl) { msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Configuration réinitialisée"; }
    } else {
      if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
    }
  } catch (e) {
    if (msgEl) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
  }
}

function normaliseOrders() {
  if (!_formConfig) return;
  const sections = _formConfig.sections || [];
  let globalOrder = 0;
  sections.forEach(section => {
    const sectionFields = _formConfig.fields
      .filter(f => f.section === section)
      .sort((a, b) => a.order - b.order);
    sectionFields.forEach(f => { f.order = globalOrder++; });
  });
}
