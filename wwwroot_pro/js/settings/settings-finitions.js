// settings-finitions.js — Options et icônes de finitions
import { authToken, showNotification, esc } from '../core.js';

const FINITION_TYPES = [
  { key: "embellissement", label: "Embellissement" },
  { key: "rainage",        label: "Rainage" },
  { key: "pliage",         label: "Pliage" },
  { key: "faconnage",      label: "Façonnage (reliure)" },
  { key: "coupe",          label: "Coupe" },
  { key: "emballage",      label: "Emballage" },
  { key: "depart",         label: "Départ" },
  { key: "livraison",      label: "Livraison" }
];

export async function renderSettingsFinitions(panel) {
  panel.innerHTML = `<h3>Finitions</h3><p style="color:#6b7280;">Chargement...</p>`;

  let faconnageOptions = [];
  let icons = [];

  try {
    faconnageOptions = await fetch("/api/settings/faconnage-options", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
  } catch(e) { /* ignore */ }

  try {
    const iconsResp = await fetch("/api/settings/finition-icons", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ ok: false, icons: [] }));
    if (iconsResp.ok) icons = iconsResp.icons || [];
  } catch(e) { /* ignore */ }

  const iconsByType = {};
  icons.forEach(ic => { iconsByType[ic.type] = ic.url; });

  const listHtml = faconnageOptions.length === 0
    ? '<span style="color:#9ca3af;font-size:13px;">Aucune option définie</span>'
    : faconnageOptions.map(o => `<span style="display:inline-block;background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:13px;margin:3px;">${esc(o)}</span>`).join("");

  const iconsHtml = FINITION_TYPES.map(ft => {
    const iconUrl = iconsByType[ft.key];
    const imgHtml = iconUrl
      ? `<img src="${esc(iconUrl)}" alt="${esc(ft.label)}" style="width:32px;height:32px;object-fit:contain;border:1px solid #e5e7eb;border-radius:4px;">`
      : `<span style="display:inline-block;width:32px;height:32px;border:1px dashed #d1d5db;border-radius:4px;background:#f9fafb;"></span>`;
    return `
      <tr style="border-top:1px solid #e5e7eb;">
        <td style="padding:8px 12px;">${imgHtml}</td>
        <td style="padding:8px 12px;font-size:13px;font-weight:600;">${esc(ft.label)}</td>
        <td style="padding:8px 12px;">
          <input type="file" id="icon-file-${ft.key}" accept=".png,.jpg,.jpeg,.gif,.webp,.svg" style="font-size:12px;">
          <button class="btn" data-icon-upload="${ft.key}" style="margin-left:6px;font-size:12px;padding:3px 8px;">Uploader</button>
          <span id="icon-msg-${ft.key}" style="font-size:12px;margin-left:6px;"></span>
        </td>
      </tr>
    `;
  }).join("");

  panel.innerHTML = `
    <h3>Finitions</h3>

    <h4 style="margin-bottom:8px;">Options de façonnage</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">
      Gérez les options de façonnage disponibles dans la fiche de production. L'administrateur décide des options et sous-options.
    </p>

    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:16px;margin-bottom:16px;max-width:600px;">
      <div style="display:flex;gap:8px;margin-bottom:12px;">
        <input type="text" id="faconnage-new-option" class="settings-input" placeholder="Nouvelle option (ex: Pelliculage mat)" style="flex:1;" />
        <button id="faconnage-add-option" class="btn btn-primary" style="white-space:nowrap;">+ Ajouter</button>
      </div>
      <div id="faconnage-options-list" style="display:flex;flex-direction:column;gap:6px;min-height:40px;">
        ${faconnageOptions.length === 0 ? '<span style="color:#9ca3af;font-size:13px;">Aucune option définie</span>' : ''}
      </div>
      <div style="display:flex;gap:8px;margin-top:12px;border-top:1px solid #e5e7eb;padding-top:12px;">
        <button id="faconnage-save-options" class="btn btn-primary">💾 Enregistrer</button>
        <button id="faconnage-csv-toggle" class="btn btn-sm" style="font-size:12px;">📥 Importer depuis CSV</button>
        <span id="faconnage-save-msg" style="font-size:13px;line-height:32px;"></span>
      </div>
    </div>

    <div id="faconnage-csv-section" style="display:none;background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:12px;margin-bottom:16px;max-width:600px;">
      <p style="color:#6b7280;font-size:13px;margin-bottom:8px;">Importez un fichier CSV avec une option par ligne. Les options existantes seront remplacées.</p>
      <div style="display:flex;gap:8px;align-items:center;">
        <input type="file" id="faconnage-csv-input" accept=".csv,.txt" class="settings-input" />
        <button id="faconnage-csv-import" class="btn btn-primary">Importer</button>
      </div>
      <div id="faconnage-import-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

    <h4 style="margin-bottom:8px;margin-top:24px;">Icônes des étapes de finition</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">
      Uploadez une icône pour chaque type de finition (PNG, JPG, SVG…).
    </p>
    <table style="width:100%;border-collapse:collapse;max-width:700px;">
      <thead>
        <tr style="background:#f3f4f6;">
          <th style="padding:8px 12px;text-align:left;font-size:12px;">Icône</th>
          <th style="padding:8px 12px;text-align:left;font-size:12px;">Finition</th>
          <th style="padding:8px 12px;text-align:left;font-size:12px;">Action</th>
        </tr>
      </thead>
      <tbody>${iconsHtml}</tbody>
    </table>
  `;

  // Build dynamic options list
  let currentOptions = [...faconnageOptions];

  function renderOptionsList() {
    const listEl = panel.querySelector("#faconnage-options-list");
    if (currentOptions.length === 0) {
      listEl.innerHTML = '<span style="color:#9ca3af;font-size:13px;">Aucune option définie</span>';
      return;
    }
    listEl.innerHTML = '';
    currentOptions.forEach((opt, idx) => {
      const row = document.createElement("div");
      row.style.cssText = "display:flex;align-items:center;gap:8px;padding:6px 10px;background:white;border:1px solid #e5e7eb;border-radius:6px;";
      row.innerHTML = `
        <span style="flex:1;font-size:13px;color:#374151;">${esc(opt)}</span>
        <button class="btn btn-sm" data-remove-idx="${idx}" style="color:#ef4444;border-color:#ef4444;padding:2px 8px;font-size:12px;" title="Supprimer">✕</button>
      `;
      row.querySelector("[data-remove-idx]").onclick = () => {
        currentOptions.splice(idx, 1);
        renderOptionsList();
      };
      listEl.appendChild(row);
    });
  }
  renderOptionsList();

  // Add option button
  panel.querySelector("#faconnage-add-option").onclick = () => {
    const input = panel.querySelector("#faconnage-new-option");
    const val = input.value.trim();
    if (!val) { showNotification("⚠️ Saisissez une option", "warning"); return; }
    if (currentOptions.includes(val)) { showNotification("⚠️ Option déjà existante", "warning"); return; }
    currentOptions.push(val);
    input.value = "";
    renderOptionsList();
  };
  panel.querySelector("#faconnage-new-option").onkeydown = (e) => {
    if (e.key === "Enter") panel.querySelector("#faconnage-add-option").click();
  };

  // Save options button
  panel.querySelector("#faconnage-save-options").onclick = async () => {
    const msgEl = panel.querySelector("#faconnage-save-msg");
    try {
      const r = await fetch("/api/settings/faconnage-options", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ options: currentOptions })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = `✅ ${r.count} option(s) enregistrée(s)`;
        showNotification("✅ Options de façonnage enregistrées", "success");
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };

  // Toggle CSV section
  panel.querySelector("#faconnage-csv-toggle").onclick = () => {
    const csvSection = panel.querySelector("#faconnage-csv-section");
    csvSection.style.display = csvSection.style.display === "none" ? "block" : "none";
  };

  // Faconnage CSV import
  panel.querySelector("#faconnage-csv-import").onclick = async () => {
    const fileInput = panel.querySelector("#faconnage-csv-input");
    const msgEl = panel.querySelector("#faconnage-import-msg");
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "Sélectionnez un fichier CSV";
      return;
    }
    const formData = new FormData();
    formData.append("file", fileInput.files[0]);
    try {
      const r = await fetch("/api/settings/faconnage-import", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: formData
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = `✅ ${r.count} option(s) importée(s)`;
        panel._loaded = false;
        await renderSettingsFinitions(panel);
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };

  // Icon upload buttons
  panel.querySelectorAll("[data-icon-upload]").forEach(btn => {
    const key = btn.dataset.iconUpload;
    btn.onclick = async () => {
      const fileInput = panel.querySelector(`#icon-file-${key}`);
      const msgEl = panel.querySelector(`#icon-msg-${key}`);
      if (!fileInput.files || fileInput.files.length === 0) {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "Sélectionnez un fichier";
        return;
      }
      const formData = new FormData();
      formData.append("file", fileInput.files[0]);
      formData.append("type", key);
      try {
        const r = await fetch("/api/settings/finition-icons", {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` },
          body: formData
        }).then(r => r.json());
        if (r.ok) {
          msgEl.style.color = "#16a34a";
          msgEl.textContent = "✅ Uploadé";
          panel._loaded = false;
          await renderSettingsFinitions(panel);
        } else {
          msgEl.style.color = "#ef4444";
          msgEl.textContent = "❌ " + (r.error || "Erreur");
        }
      } catch(e) {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ Erreur réseau";
      }
    };
  });
}
