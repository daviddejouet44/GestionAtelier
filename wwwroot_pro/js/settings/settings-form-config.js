// settings-form-config.js — Configuration dynamique de la fiche de production
import { authToken, showNotification, esc } from '../core.js';

let _formConfig = null;

export async function renderSettingsFormConfig(panel) {
  panel.innerHTML = `<h3>Fiche de production</h3><p style="color:#6b7280;">Chargement...</p>`;
  try {
    const resp = await fetch("/api/settings/form-config", {
      headers: { "Authorization": `Bearer ${authToken}` }
    });
    _formConfig = await resp.json();
  } catch (e) {
    panel.innerHTML = `<h3>Fiche de production</h3><p style="color:#ef4444;">Erreur de chargement.</p>`;
    return;
  }
  renderConfigUI(panel, _formConfig);
}

// ─── Main UI ───────────────────────────────────────────────────────────────

function renderConfigUI(panel, config) {
  panel.innerHTML = `
    <h3>Fiche de production — Configuration des champs</h3>
    <p style="font-size:13px;color:#6b7280;margin-bottom:16px;">
      Configurez l'ordre, la visibilité et les propriétés des champs. Les champs masqués n'apparaissent ni dans la fiche ni dans le PDF.
    </p>
    <div id="ffc-subpdf" style="border:1px solid #e5e7eb;border-radius:10px;padding:14px 16px;margin-bottom:18px;background:#fafafa;">
      <h4 style="margin:0 0 6px;font-size:14px;">📄 PDF de substitution (fiche sans PDF)</h4>
      <p style="font-size:12px;color:#6b7280;margin:0 0 10px;">
        Ce PDF est utilisé comme vignette lorsqu'une fiche est créée sans importer de PDF. Il est remplacé par le PDF final une fois celui-ci importé.
      </p>
      <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;">
        <span id="ffc-subpdf-status" style="font-size:13px;color:#374151;">Chargement…</span>
        <a id="ffc-subpdf-preview" href="#" target="_blank" rel="noopener" class="btn" style="display:none;">👁️ Aperçu</a>
        <button id="ffc-subpdf-upload" class="btn btn-primary">⬆️ Importer un PDF</button>
        <button id="ffc-subpdf-reset" class="btn" style="color:#ef4444;border-color:#ef4444;">🔄 PDF par défaut</button>
        <input id="ffc-subpdf-file" type="file" accept=".pdf,application/pdf" style="display:none;" />
      </div>
    </div>
    <div style="display:flex;gap:10px;margin-bottom:16px;flex-wrap:wrap;">
      <button id="ffc-save"  class="btn btn-primary">💾 Enregistrer</button>
      <button id="ffc-add-section" class="btn" style="background:#f0fdf4;border-color:#16a34a;color:#16a34a;">＋ Nouvelle section</button>
      <button id="ffc-add-field"   class="btn" style="background:#eff6ff;border-color:#3b82f6;color:#3b82f6;">＋ Nouveau champ</button>
      <button id="ffc-reset" class="btn" style="color:#ef4444;border-color:#ef4444;margin-left:auto;">🔄 Réinitialiser</button>
    </div>
    <div id="ffc-msg" style="font-size:13px;margin-bottom:10px;"></div>
    <div id="ffc-sections-container"></div>
  `;

  renderSections(panel, config);
  setupSubstitutionPdf(panel);

  panel.querySelector("#ffc-save").onclick        = () => saveConfig(panel);
  panel.querySelector("#ffc-reset").onclick       = () => resetConfig(panel);
  panel.querySelector("#ffc-add-section").onclick = () => promptAddSection(panel, config);
  panel.querySelector("#ffc-add-field").onclick   = () => openAddFieldModal(panel, config);
}

// ─── Substitution PDF (fiche sans PDF) ────────────────────────────────────

async function setupSubstitutionPdf(panel) {
  const statusEl  = panel.querySelector("#ffc-subpdf-status");
  const previewEl = panel.querySelector("#ffc-subpdf-preview");
  const uploadBtn = panel.querySelector("#ffc-subpdf-upload");
  const resetBtn  = panel.querySelector("#ffc-subpdf-reset");
  const fileInput = panel.querySelector("#ffc-subpdf-file");
  if (!statusEl) return;

  async function refresh() {
    try {
      const r = await fetch("/api/settings/substitution-pdf", {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(res => res.json());
      if (r.ok && r.configured) {
        statusEl.textContent = "PDF actuel : " + (r.fileName || "substitution.pdf");
        if (previewEl && r.path) {
          previewEl.style.display = "";
          previewEl.href = "/api/file?path=" + encodeURIComponent(r.path) + "&token=" + encodeURIComponent(authToken || "");
        }
      } else {
        statusEl.textContent = "Aucun PDF personnalisé — un PDF par défaut sera utilisé.";
        if (previewEl) previewEl.style.display = "none";
      }
    } catch (e) {
      statusEl.textContent = "Erreur de chargement.";
    }
  }

  if (uploadBtn) uploadBtn.onclick = () => fileInput && fileInput.click();
  if (fileInput) fileInput.onchange = async () => {
    const file = fileInput.files && fileInput.files[0];
    fileInput.value = "";
    if (!file) return;
    if (!file.name.toLowerCase().endsWith(".pdf")) { showNotification("❌ Seuls les PDF sont acceptés", "error"); return; }
    const fd = new FormData();
    fd.append("file", file);
    try {
      const r = await fetch("/api/settings/substitution-pdf", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: fd
      }).then(res => res.json());
      if (r.ok) { showNotification("✅ PDF de substitution enregistré", "success"); await refresh(); }
      else showNotification("❌ " + (r.error || "Erreur"), "error");
    } catch (e) { showNotification("❌ Erreur réseau", "error"); }
  };
  if (resetBtn) resetBtn.onclick = async () => {
    if (!confirm("Réinitialiser le PDF de substitution par défaut ?")) return;
    try {
      const r = await fetch("/api/settings/substitution-pdf", {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(res => res.json());
      if (r.ok) { showNotification("✅ PDF de substitution réinitialisé", "success"); await refresh(); }
      else showNotification("❌ " + (r.error || "Erreur"), "error");
    } catch (e) { showNotification("❌ Erreur réseau", "error"); }
  };

  refresh();
}

// ─── Sections renderer ────────────────────────────────────────────────────

function renderSections(panel, config) {
  const container = panel.querySelector("#ffc-sections-container");
  if (!container) return;
  container.innerHTML = "";

  const sections = config.sections || [];
  const fields   = config.fields   || [];

  sections.forEach((section, sectionIdx) => {
    const sectionFields = fields
      .filter(f => f.section === section)
      .sort((a, b) => a.order - b.order);

    const sectionEl = document.createElement("div");
    sectionEl.className = "ffc-section";
    sectionEl.style.cssText = "border:1px solid #e5e7eb;border-radius:10px;margin-bottom:14px;overflow:hidden;";

    // Section header
    const hdr = document.createElement("div");
    hdr.style.cssText = "background:#f9fafb;padding:10px 14px;display:flex;align-items:center;gap:8px;border-bottom:1px solid #e5e7eb;";
    hdr.innerHTML = `
      <div style="display:flex;gap:3px;">
        <button class="ffc-section-up btn" data-idx="${sectionIdx}" style="padding:2px 7px;font-size:11px;" title="Monter">↑</button>
        <button class="ffc-section-down btn" data-idx="${sectionIdx}" style="padding:2px 7px;font-size:11px;" title="Descendre">↓</button>
      </div>
      <input type="text" class="ffc-section-name settings-input" value="${esc(section)}"
             style="font-weight:600;font-size:14px;flex:1;padding:4px 8px;border:1px solid #d1d5db;border-radius:6px;" />
      <span style="font-size:11px;color:#9ca3af;">${sectionFields.length} champ(s)</span>
      <button class="ffc-section-add-field btn" data-section="${esc(section)}"
              style="padding:2px 9px;font-size:11px;background:#eff6ff;border-color:#3b82f6;color:#3b82f6;" title="Ajouter un champ dans cette section">＋ Champ</button>
      <button class="ffc-section-delete btn" data-idx="${sectionIdx}" data-section="${esc(section)}"
              style="padding:2px 7px;font-size:11px;color:#ef4444;border-color:#ef4444;" title="Supprimer cette section">🗑️</button>
    `;
    sectionEl.appendChild(hdr);

    // Fields list
    const body = document.createElement("div");
    body.style.cssText = "padding:8px 14px;";
    body.innerHTML = `
      <div style="display:grid;grid-template-columns:36px 1fr 90px 110px 52px 52px 52px 52px 52px;gap:6px;align-items:center;
                  font-size:11px;font-weight:600;color:#6b7280;background:#f9fafb;padding:6px 8px;border-radius:6px;margin-bottom:6px;">
        <span></span><span>Label</span><span>Largeur</span><span>Section</span>
        <span>Vis.</span><span>Req.</span><span>🔒</span><span>⚙️</span><span>🗑️</span>
      </div>
      <div class="ffc-fields-list" data-section="${esc(section)}"></div>
    `;
    sectionEl.appendChild(body);

    const fieldsList = body.querySelector(".ffc-fields-list");
    sectionFields.forEach((field, fieldIdx) => {
      fieldsList.appendChild(createFieldRow(field, fieldIdx, sectionFields.length, sections, fields));
    });

    container.appendChild(sectionEl);
  });

  // ── Event delegation for section actions ──
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
  container.querySelectorAll(".ffc-section-delete").forEach(btn => {
    btn.onclick = () => {
      const sectionName = btn.dataset.section;
      const fieldsInSection = fields.filter(f => f.section === sectionName);
      const msg = fieldsInSection.length > 0
        ? `Supprimer la section "${sectionName}" ? Ses ${fieldsInSection.length} champ(s) seront déplacés vers la première section.`
        : `Supprimer la section "${sectionName}" ?`;
      if (!confirm(msg)) return;
      config.sections.splice(parseInt(btn.dataset.idx), 1);
      const fallback = config.sections[0] || "Informations générales";
      fields.forEach(f => { if (f.section === sectionName) f.section = fallback; });
      renderSections(panel, config);
    };
  });
  container.querySelectorAll(".ffc-section-add-field").forEach(btn => {
    btn.onclick = () => openAddFieldModal(panel, config, btn.dataset.section);
  });

  // ── Field movement ──
  container.querySelectorAll(".ffc-field-up").forEach(btn => {
    btn.onclick = () => {
      const sec = btn.dataset.section;
      const idx = parseInt(btn.dataset.idx);
      const sf  = fields.filter(f => f.section === sec).sort((a,b) => a.order-b.order);
      if (idx > 0) { const tmp = sf[idx-1].order; sf[idx-1].order = sf[idx].order; sf[idx].order = tmp; renderSections(panel, config); }
    };
  });
  container.querySelectorAll(".ffc-field-down").forEach(btn => {
    btn.onclick = () => {
      const sec = btn.dataset.section;
      const idx = parseInt(btn.dataset.idx);
      const sf  = fields.filter(f => f.section === sec).sort((a,b) => a.order-b.order);
      if (idx < sf.length-1) { const tmp = sf[idx].order; sf[idx].order = sf[idx+1].order; sf[idx+1].order = tmp; renderSections(panel, config); }
    };
  });

  // ── Live-bind property changes ──
  container.querySelectorAll(".ffc-field-visible").forEach(cb => {
    cb.onchange = () => { const f = fields.find(x => x.id === cb.dataset.id); if (f) f.visible = cb.checked; };
  });
  container.querySelectorAll(".ffc-field-required").forEach(cb => {
    cb.onchange = () => { const f = fields.find(x => x.id === cb.dataset.id); if (f) f.required = cb.checked; };
  });
  container.querySelectorAll(".ffc-field-readonly").forEach(cb => {
    cb.onchange = () => {
      const f = fields.find(x => x.id === cb.dataset.id);
      if (!f) return;
      f.readOnly = cb.checked;
      const reqCb = container.querySelector(`.ffc-field-required[data-id="${CSS.escape(cb.dataset.id)}"]`);
      if (cb.checked) { if (reqCb) { reqCb.checked = false; reqCb.disabled = true; f.required = false; } }
      else             { if (reqCb) reqCb.disabled = false; }
    };
  });
  container.querySelectorAll(".ffc-field-label").forEach(inp => {
    inp.oninput = () => { const f = fields.find(x => x.id === inp.dataset.id); if (f) f.label = inp.value; };
  });
  container.querySelectorAll(".ffc-field-width").forEach(sel => {
    sel.onchange = () => { const f = fields.find(x => x.id === sel.dataset.id); if (f) f.width = sel.value; };
  });
  container.querySelectorAll(".ffc-field-section").forEach(sel => {
    sel.onchange = () => {
      const f = fields.find(x => x.id === sel.dataset.id);
      if (f) { f.section = sel.value; renderSections(panel, config); }
    };
  });
  container.querySelectorAll(".ffc-section-name").forEach(inp => {
    inp.dataset.oldName = inp.value;
    inp.oninput = () => {
      const oldN = inp.dataset.oldName;
      const newN = inp.value.trim();
      if (!newN || newN === oldN) return;
      const secIdx = config.sections.indexOf(oldN);
      if (secIdx >= 0) config.sections[secIdx] = newN;
      fields.forEach(f => { if (f.section === oldN) f.section = newN; });
      inp.dataset.oldName = newN;
    };
  });
  // ── Field configure (options, conditions) ──
  container.querySelectorAll(".ffc-field-configure").forEach(btn => {
    btn.onclick = () => {
      const f = _formConfig.fields.find(x => x.id === btn.dataset.id);
      if (f) openFieldConfigModal(panel, f, config);
    };
  });
  // ── Delete custom field ──
  container.querySelectorAll(".ffc-field-delete").forEach(btn => {
    btn.onclick = () => {
      const f = fields.find(x => x.id === btn.dataset.id);
      if (!f) return;
      if (!confirm(`Supprimer le champ personnalisé "${f.label}" ?`)) return;
      config.fields.splice(config.fields.indexOf(f), 1);
      renderSections(panel, config);
    };
  });
}

// ─── Field row ────────────────────────────────────────────────────────────

function createFieldRow(field, fieldIdx, totalFields, allSections, allFields) {
  const sectionOptions = allSections.map(s =>
    `<option value="${esc(s)}" ${s === field.section ? 'selected' : ''}>${esc(s)}</option>`
  ).join('');

  const row = document.createElement("div");
  row.style.cssText = "display:grid;grid-template-columns:36px 1fr 90px 110px 52px 52px 52px 52px 52px;gap:6px;align-items:center;padding:5px 8px;border-bottom:1px solid #f3f4f6;transition:background .1s;";
  row.onmouseenter = () => row.style.background = "#f9fafb";
  row.onmouseleave = () => row.style.background = "";

  const hasCond = field.dependsOn || (field.dependsOnValues && field.dependsOnValues.length > 0);
  const hasOpts = ['select','multiselect'].includes(field.type) || (field.options && field.options.length > 0);
  const typeColor = field.isCustom ? 'color:#7c3aed;' : 'color:#9ca3af;';

  row.innerHTML = `
    <div style="display:flex;gap:1px;flex-direction:column;">
      <button class="ffc-field-up btn" data-idx="${fieldIdx}" data-section="${esc(field.section||'')}"
              style="padding:1px 5px;font-size:10px;" ${fieldIdx===0?'disabled':''}>↑</button>
      <button class="ffc-field-down btn" data-idx="${fieldIdx}" data-section="${esc(field.section||'')}"
              style="padding:1px 5px;font-size:10px;" ${fieldIdx>=totalFields-1?'disabled':''}>↓</button>
    </div>
    <div>
      <input type="text" class="ffc-field-label settings-input" data-id="${esc(field.id)}" value="${esc(field.label)}"
             style="width:100%;padding:3px 7px;border:1px solid #d1d5db;border-radius:5px;font-size:12px;" />
      <div style="font-size:10px;${typeColor}">
        ${esc(field.id)} · ${esc(field.type)}${hasCond?' 🔗':''}${hasOpts?' 📋':''}${field.sansPdfOnly?' 📄':''}
      </div>
    </div>
    <select class="ffc-field-width settings-input" data-id="${esc(field.id)}"
            style="padding:3px 5px;font-size:11px;border:1px solid #d1d5db;border-radius:5px;">
      <option value="half" ${field.width==='half'?'selected':''}>½</option>
      <option value="full" ${field.width==='full'?'selected':''}>Pleine</option>
    </select>
    <select class="ffc-field-section settings-input" data-id="${esc(field.id)}"
            style="padding:3px 5px;font-size:11px;border:1px solid #d1d5db;border-radius:5px;">
      ${sectionOptions}
    </select>
    <label style="display:flex;justify-content:center;cursor:pointer;" title="Visible">
      <input type="checkbox" class="ffc-field-visible" data-id="${esc(field.id)}" ${field.visible?'checked':''} />
    </label>
    <label style="display:flex;justify-content:center;cursor:pointer;" title="Obligatoire">
      <input type="checkbox" class="ffc-field-required" data-id="${esc(field.id)}"
             ${field.required?'checked':''} ${field.readOnly?'disabled':''} />
    </label>
    <label style="display:flex;justify-content:center;cursor:pointer;" title="Lecture seule">
      <input type="checkbox" class="ffc-field-readonly" data-id="${esc(field.id)}" ${field.readOnly?'checked':''} />
    </label>
    <button class="ffc-field-configure btn" data-id="${esc(field.id)}"
            style="padding:2px 6px;font-size:11px;background:#f5f3ff;border-color:#7c3aed;color:#7c3aed;"
            title="Options, conditions, sous-menus">⚙️</button>
    <button class="ffc-field-delete btn" data-id="${esc(field.id)}"
            style="padding:2px 6px;font-size:11px;color:${field.isCustom?'#ef4444':'#d1d5db'};border-color:${field.isCustom?'#ef4444':'#e5e7eb'};"
            title="${field.isCustom?'Supprimer ce champ personnalisé':'Champ système (non supprimable)'}"
            ${field.isCustom?'':'disabled'}>🗑️</button>
  `;
  return row;
}

// ─── Add section ──────────────────────────────────────────────────────────

function promptAddSection(panel, config) {
  const name = prompt("Nom de la nouvelle section :");
  if (!name || !name.trim()) return;
  const trimmed = name.trim();
  if (config.sections.includes(trimmed)) {
    alert(`La section "${trimmed}" existe déjà.`);
    return;
  }
  config.sections.push(trimmed);
  renderSections(panel, config);
}

// ─── Add custom field modal ───────────────────────────────────────────────

function openAddFieldModal(panel, config, defaultSection) {
  const existingModal = document.getElementById("ffc-add-field-modal");
  if (existingModal) existingModal.remove();

  const sections = config.sections || [];
  const sectionOptions = sections.map(s =>
    `<option value="${esc(s)}" ${s === defaultSection ? 'selected' : ''}>${esc(s)}</option>`
  ).join('');

  const modal = document.createElement("div");
  modal.id = "ffc-add-field-modal";
  modal.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:9999;display:flex;align-items:center;justify-content:center;";
  modal.innerHTML = `
    <div style="background:#fff;border-radius:12px;padding:24px;width:520px;max-width:95vw;max-height:90vh;overflow-y:auto;box-shadow:0 20px 60px rgba(0,0,0,.3);">
      <h4 style="margin:0 0 16px;font-size:16px;">➕ Nouveau champ personnalisé</h4>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Identifiant (unique) <span style="color:#ef4444;">*</span></label>
          <input id="ffc-new-id" class="settings-input" placeholder="ex: monChampPerso" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
          <small style="color:#9ca3af;font-size:10px;">Lettres, chiffres, camelCase uniquement</small>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Label <span style="color:#ef4444;">*</span></label>
          <input id="ffc-new-label" class="settings-input" placeholder="ex: Mon champ" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Type</label>
          <select id="ffc-new-type" class="settings-input" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;">
            <option value="text">Texte</option>
            <option value="number">Nombre</option>
            <option value="select">Menu déroulant (select)</option>
            <option value="multiselect">Multi-sélection</option>
            <option value="checkbox">Case à cocher</option>
            <option value="date">Date</option>
            <option value="textarea">Zone de texte</option>
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Section</label>
          <select id="ffc-new-section" class="settings-input" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;">
            ${sectionOptions}
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Largeur</label>
          <select id="ffc-new-width" class="settings-input" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;">
            <option value="half">½ largeur</option>
            <option value="full">Pleine largeur</option>
          </select>
        </div>
        <div style="display:flex;align-items:flex-end;gap:16px;padding-bottom:4px;">
          <label style="display:flex;align-items:center;gap:5px;font-size:12px;cursor:pointer;">
            <input type="checkbox" id="ffc-new-required" /> Obligatoire
          </label>
          <label style="display:flex;align-items:center;gap:5px;font-size:12px;cursor:pointer;">
            <input type="checkbox" id="ffc-new-readonly" /> Lecture seule
          </label>
        </div>
      </div>
      <div id="ffc-new-options-wrap" style="display:none;margin-bottom:12px;">
        <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Options (une par ligne)</label>
        <textarea id="ffc-new-options" rows="4" class="settings-input"
                  style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:12px;resize:vertical;"
                  placeholder="Option 1&#10;Option 2&#10;Option 3"></textarea>
      </div>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Affiché si le champ… (conditionnel)</label>
          <select id="ffc-new-depends-on" class="settings-input" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;">
            <option value="">— Aucune condition —</option>
            ${(config.fields||[]).map(f => `<option value="${esc(f.id)}">${esc(f.label)} (${esc(f.id)})</option>`).join('')}
          </select>
        </div>
        <div>
          <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">… vaut (valeurs déclencheuses)</label>
          <input id="ffc-new-depends-val" class="settings-input" placeholder="ex: Brochure,Flyer"
                 style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
          <small style="color:#9ca3af;font-size:10px;">Séparer plusieurs valeurs par une virgule</small>
        </div>
      </div>
      <div id="ffc-new-err" style="font-size:13px;color:#ef4444;margin-bottom:8px;"></div>
      <div style="display:flex;gap:10px;justify-content:flex-end;">
        <button id="ffc-new-cancel" class="btn">Annuler</button>
        <button id="ffc-new-submit" class="btn btn-primary">Ajouter le champ</button>
      </div>
    </div>
  `;
  document.body.appendChild(modal);

  const typeEl   = modal.querySelector("#ffc-new-type");
  const optsWrap = modal.querySelector("#ffc-new-options-wrap");
  typeEl.onchange = () => {
    optsWrap.style.display = ['select','multiselect'].includes(typeEl.value) ? '' : 'none';
  };

  modal.querySelector("#ffc-new-cancel").onclick = () => modal.remove();
  modal.onclick = e => { if (e.target === modal) modal.remove(); };

  modal.querySelector("#ffc-new-submit").onclick = () => {
    const errEl   = modal.querySelector("#ffc-new-err");
    const id      = modal.querySelector("#ffc-new-id").value.trim();
    const label   = modal.querySelector("#ffc-new-label").value.trim();
    const type    = typeEl.value;
    const section = modal.querySelector("#ffc-new-section").value;
    const width   = modal.querySelector("#ffc-new-width").value;
    const required= modal.querySelector("#ffc-new-required").checked;
    const readOnly= modal.querySelector("#ffc-new-readonly").checked;
    const depOn   = modal.querySelector("#ffc-new-depends-on").value;
    const depVal  = modal.querySelector("#ffc-new-depends-val").value.trim();

    if (!id)    { errEl.textContent = "L'identifiant est requis."; return; }
    if (!/^[a-zA-Z][a-zA-Z0-9_]*$/.test(id)) { errEl.textContent = "L'ID doit commencer par une lettre et ne contenir que lettres, chiffres et _."; return; }
    if (!label) { errEl.textContent = "Le label est requis."; return; }
    if ((config.fields||[]).some(f => f.id === id)) { errEl.textContent = `Un champ avec l'ID "${id}" existe déjà.`; return; }

    const options = ['select','multiselect'].includes(type)
      ? modal.querySelector("#ffc-new-options").value.split('\n').map(s=>s.trim()).filter(Boolean)
      : undefined;

    const depVals = depVal ? depVal.split(',').map(s=>s.trim()).filter(Boolean) : undefined;
    const depValue = depVals && depVals.length === 1 ? depVals[0] : undefined;

    const maxOrder = (config.fields||[]).length > 0 ? Math.max(...config.fields.map(f=>f.order)) + 1 : 0;

    const newField = {
      id, label, type, section, width, required, readOnly,
      visible: true, isCustom: true, order: maxOrder,
      options: options || null,
      dependsOn: depOn || null,
      dependsOnValue: depValue || null,
      dependsOnValues: (depVals && depVals.length > 1) ? depVals : null
    };

    if (!config.fields) config.fields = [];
    config.fields.push(newField);
    if (!config.sections.includes(section)) config.sections.push(section);

    modal.remove();
    renderSections(panel, config);
    showNotification("✅ Champ ajouté — pensez à enregistrer.", "success");
  };
}

// ─── Field configure modal (options, conditions, sub-options) ─────────────

function openFieldConfigModal(panel, field, config) {
  const existingModal = document.getElementById("ffc-config-modal");
  if (existingModal) existingModal.remove();

  const isSelect = ['select','multiselect'].includes(field.type);
  const currentOptions = (field.options || []).join('\n');
  const currentDep = field.dependsOn || '';
  const currentDepVals = field.dependsOnValues
    ? field.dependsOnValues.join(', ')
    : (field.dependsOnValue || '');

  // Sub-options: build textarea per parent option
  const subOptsSections = (field.options || []).map(opt => {
    const subs = (field.subOptions && field.subOptions[opt]) ? field.subOptions[opt].join('\n') : '';
    return `
      <div style="margin-bottom:8px;">
        <label style="font-size:11px;font-weight:600;color:#374151;display:block;margin-bottom:2px;">
          Sous-options de "${esc(opt)}" (une par ligne, laisser vide si aucune) :
        </label>
        <textarea class="ffc-sub-opts" data-parent="${esc(opt)}" rows="2"
                  style="width:100%;padding:5px 7px;border:1px solid #d1d5db;border-radius:5px;font-size:12px;resize:vertical;">${esc(subs)}</textarea>
      </div>`;
  }).join('');

  const modal = document.createElement("div");
  modal.id = "ffc-config-modal";
  modal.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.45);z-index:9999;display:flex;align-items:center;justify-content:center;";
  modal.innerHTML = `
    <div style="background:#fff;border-radius:12px;padding:24px;width:580px;max-width:96vw;max-height:90vh;overflow-y:auto;box-shadow:0 20px 60px rgba(0,0,0,.3);">
      <h4 style="margin:0 0 4px;font-size:16px;">⚙️ Configuration : ${esc(field.label)}</h4>
      <p style="font-size:11px;color:#9ca3af;margin:0 0 16px;">ID: ${esc(field.id)} · Type: ${esc(field.type)}</p>

      ${isSelect ? `
      <div style="margin-bottom:16px;padding:12px;border:1px solid #e5e7eb;border-radius:8px;">
        <h5 style="margin:0 0 8px;font-size:13px;">📋 Options du menu (une par ligne)</h5>
        <textarea id="ffc-cfg-options" rows="6" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:12px;resize:vertical;"
                  placeholder="Option 1&#10;Option 2&#10;Option 3">${esc(currentOptions)}</textarea>
      </div>
      <div id="ffc-cfg-subopts-section" style="margin-bottom:16px;padding:12px;border:1px solid #e5e7eb;border-radius:8px;">
        <h5 style="margin:0 0 8px;font-size:13px;">🔀 Sous-menus conditionnels</h5>
        <p style="font-size:11px;color:#6b7280;margin:0 0 8px;">Pour chaque option ci-dessus, vous pouvez définir des sous-options qui s'afficheront dans un champ enfant automatiquement créé.</p>
        ${subOptsSections || '<p style="color:#9ca3af;font-size:12px;">Enregistrez d\'abord les options principales pour configurer les sous-menus.</p>'}
      </div>
      ` : ''}

      <div style="margin-bottom:16px;padding:12px;border:1px solid #e5e7eb;border-radius:8px;">
        <h5 style="margin:0 0 8px;font-size:13px;">🔗 Affichage conditionnel</h5>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;">
          <div>
            <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">Afficher si le champ :</label>
            <select id="ffc-cfg-dep-on" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:12px;">
              <option value="">— Aucune condition —</option>
              ${(config.fields||[]).filter(f=>f.id!==field.id).map(f =>
                `<option value="${esc(f.id)}" ${currentDep===f.id?'selected':''}>${esc(f.label)} (${esc(f.id)})</option>`
              ).join('')}
            </select>
          </div>
          <div>
            <label style="font-size:12px;font-weight:600;display:block;margin-bottom:4px;">… vaut (virgule = OU) :</label>
            <input id="ffc-cfg-dep-vals" value="${esc(currentDepVals)}"
                   placeholder="ex: Brochure, Flyer"
                   style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:12px;" />
          </div>
        </div>
      </div>

      <div style="margin-bottom:16px;padding:12px;border:1px solid #e5e7eb;border-radius:8px;">
        <h5 style="margin:0 0 8px;font-size:13px;">✏️ Valeur par défaut</h5>
        <input id="ffc-cfg-default" value="${esc(field.defaultValue||'')}"
               placeholder="Valeur préremplie au chargement de la fiche"
               style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:12px;" />
      </div>

      <div style="margin-bottom:16px;padding:12px;border:1px solid #e5e7eb;border-radius:8px;">
        <h5 style="margin:0 0 8px;font-size:13px;">📄 Process « fiche sans PDF »</h5>
        <label style="display:flex;align-items:center;gap:8px;font-size:12px;cursor:pointer;">
          <input type="checkbox" id="ffc-cfg-sanspdf" ${field.sansPdfOnly?'checked':''} />
          Afficher ce champ uniquement pour les fiches créées sans PDF
        </label>
      </div>

      <div style="display:flex;gap:10px;justify-content:flex-end;">
        <button id="ffc-cfg-cancel" class="btn">Annuler</button>
        <button id="ffc-cfg-ok" class="btn btn-primary">Appliquer</button>
      </div>
    </div>
  `;
  document.body.appendChild(modal);

  modal.querySelector("#ffc-cfg-cancel").onclick = () => modal.remove();
  modal.onclick = e => { if (e.target === modal) modal.remove(); };

  modal.querySelector("#ffc-cfg-ok").onclick = () => {
    // Options
    if (isSelect) {
      const raw = modal.querySelector("#ffc-cfg-options").value;
      field.options = raw.split('\n').map(s=>s.trim()).filter(Boolean);

      // Sub-options
      const subOptsMap = {};
      modal.querySelectorAll(".ffc-sub-opts").forEach(ta => {
        const parent = ta.dataset.parent;
        const subs = ta.value.split('\n').map(s=>s.trim()).filter(Boolean);
        if (subs.length > 0) subOptsMap[parent] = subs;
      });
      field.subOptions = Object.keys(subOptsMap).length > 0 ? subOptsMap : null;
    }

    // Conditions
    const depOn  = modal.querySelector("#ffc-cfg-dep-on").value;
    const depRaw = modal.querySelector("#ffc-cfg-dep-vals").value.trim();
    const depVals = depRaw ? depRaw.split(',').map(s=>s.trim()).filter(Boolean) : [];
    field.dependsOn = depOn || null;
    field.dependsOnValue  = (depOn && depVals.length === 1) ? depVals[0] : null;
    field.dependsOnValues = (depOn && depVals.length > 1)  ? depVals    : null;

    // Default value
    field.defaultValue = modal.querySelector("#ffc-cfg-default").value || null;

    // "Fiche sans PDF" visibility
    field.sansPdfOnly = modal.querySelector("#ffc-cfg-sanspdf").checked;

    modal.remove();
    renderSections(panel, config);
    showNotification("✅ Champ configuré — pensez à enregistrer.", "info");
  };
}

// ─── Save / reset ─────────────────────────────────────────────────────────

async function saveConfig(panel) {
  const msgEl = panel.querySelector("#ffc-msg");
  normaliseOrders();
  try {
    const r = await fetch("/api/settings/form-config", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(_formConfig)
    }).then(r => r.json());
    if (r.ok) {
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
  let globalOrder = 0;
  _formConfig.sections.forEach(section => {
    _formConfig.fields
      .filter(f => f.section === section)
      .sort((a, b) => a.order - b.order)
      .forEach(f => { f.order = globalOrder++; });
  });
}
