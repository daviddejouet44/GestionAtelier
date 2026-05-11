import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsCoverProducts(panel) {
  panel.innerHTML = `<h3>Produits nécessitant une couverture</h3><p style="color:#6b7280;">Chargement...</p>`;

  let selectedProducts = [];
  let workTypes = [];
  try {
    selectedProducts = await fetch("/api/settings/cover-products", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
    const wt = await fetch("/api/config/work-types", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);
    if (Array.isArray(wt)) workTypes = wt;
  } catch(e) { /* ignore */ }

  const checkboxesHtml = workTypes.length === 0
    ? `<p style="color:#9ca3af;font-size:13px;">Aucun type de travail configuré. Ajoutez des types dans l'onglet "Types de travail".</p>`
    : workTypes.map(t => {
        const checked = selectedProducts.includes(t) ? 'checked' : '';
        return `<label style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;cursor:pointer;font-size:13px;">
          <input type="checkbox" class="cover-product-cb" value="${esc(t)}" ${checked} />
          <span>${esc(t)}</span>
        </label>`;
      }).join("");

  panel.innerHTML = `
    <h3>Produits nécessitant une couverture</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Sélectionnez les types de travail pour lesquels le champ "Couverture" doit apparaître dans la fiche de production.
    </p>
    <div style="display:flex;flex-direction:column;gap:6px;max-width:400px;margin-bottom:16px;">
      ${checkboxesHtml}
    </div>
    <button id="cover-products-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
    <div id="cover-products-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  panel.querySelector("#cover-products-save").onclick = async () => {
    const msgEl = panel.querySelector("#cover-products-msg");
    const checked = Array.from(panel.querySelectorAll(".cover-product-cb:checked")).map(cb => cb.value);
    try {
      const r = await fetch("/api/settings/cover-products", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ products: checked })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = `✅ ${checked.length} produit(s) enregistré(s)`;
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}

export async function renderSettingsSheetCalcRules(panel) {
  panel.innerHTML = `<h3>Règles de calcul — Nombre de feuilles</h3><p style="color:#6b7280;">Chargement...</p>`;

  let rules = {};
  let workTypes = [];
  try {
    const [rulesResp, typesResp] = await Promise.all([
      fetch("/api/settings/sheet-calculation-rules", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()).catch(() => ({ rules: {} })),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => [])
    ]);
    rules = rulesResp.rules || {};
    workTypes = Array.isArray(typesResp) ? typesResp : [];
  } catch(e) { /* ignore */ }

  const rulesHtml = Object.entries(rules).map(([type, divisor]) =>
    `<tr><td style="padding:4px 8px;">${esc(type)}</td><td style="padding:4px 8px;">${divisor}</td><td style="padding:4px 8px;"><button class="btn btn-sm calc-rule-delete" data-type="${esc(type)}" style="color:#ef4444;border-color:#ef4444;font-size:11px;">✕</button></td></tr>`
  ).join("") || '<tr><td colspan="3" style="color:#9ca3af;font-size:13px;padding:8px;">Aucune règle définie</td></tr>';

  const typeOptions = workTypes.map(t => `<option value="${esc(t)}">${esc(t)}</option>`).join('');

  panel.innerHTML = `
    <h3>Règles de calcul — Nombre de feuilles</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Formule : <code>Nombre de feuilles = (Quantité + Justifs) ÷ Diviseur + Passes</code> (arrondi au supérieur)
    </p>
    <table style="width:100%;max-width:600px;border-collapse:collapse;margin-bottom:16px;font-size:13px;">
      <thead><tr><th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Type de travail</th><th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Diviseur</th><th style="width:40px;padding:6px 8px;background:#f3f4f6;"></th></tr></thead>
      <tbody id="calc-rules-tbody">${rulesHtml}</tbody>
    </table>
    <h4 style="margin-bottom:8px;">Modifier / ajouter une règle</h4>
    <div style="display:flex;gap:8px;align-items:center;margin-bottom:8px;">
      <select id="calc-rule-type" class="settings-input" style="flex:1;max-width:220px;">
        <option value="">— Sélectionner un type —</option>
        ${typeOptions}
      </select>
      <input type="number" id="calc-rule-divisor" placeholder="Diviseur" class="settings-input" style="width:80px;" min="1" />
      <button id="calc-rule-add" class="btn btn-primary">Ajouter / Modifier</button>
    </div>
    <button id="calc-rules-save" class="btn btn-primary">Enregistrer toutes les règles</button>
    <div id="calc-rules-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  let currentRules = { ...rules };

  panel.querySelector("#calc-rule-add").onclick = () => {
    const type = panel.querySelector("#calc-rule-type").value.trim();
    const divisor = parseInt(panel.querySelector("#calc-rule-divisor").value);
    if (!type || !divisor || divisor < 1) return;
    currentRules[type] = divisor;
    const tbody = panel.querySelector("#calc-rules-tbody");
    tbody.innerHTML = Object.entries(currentRules).map(([t, d]) =>
      `<tr><td style="padding:4px 8px;">${esc(t)}</td><td style="padding:4px 8px;">${d}</td></tr>`
    ).join("");
  };

  panel.querySelector("#calc-rules-save").onclick = async () => {
    const msgEl = panel.querySelector("#calc-rules-msg");
    try {
      const r = await fetch("/api/settings/sheet-calculation-rules", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ rules: currentRules })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Règles enregistrées";
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}

export async function renderSettingsDeliveryDelay(panel) {
  panel.innerHTML = `<h3>Dates clés</h3><p style="color:#6b7280;">Chargement...</p>`;

  let cfg = {
    livraisonEnvoiHeures: 48, livraisonFinitionsHeures: 72, livraisonImpressionHeures: 96,
    retraitEnvoiHeures: 0, retraitFinitionsHeures: 24, retraitImpressionHeures: 48
  };
  try {
    const r = await fetch("/api/config/key-dates-offsets", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok && r.config) cfg = { ...cfg, ...r.config };
  } catch(e) { /* ignore */ }

  panel.innerHTML = `
    <h3>Dates clés</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      À partir de la date de réception souhaitée, les dates clés sont calculées automatiquement en remontant dans le temps.<br>
      Configurez les décalages selon le mode de livraison choisi dans la fiche.
    </p>

    <h4 style="margin-bottom:10px;color:#1e3a5f;">Mode Livraison</h4>
    <div class="settings-form-group">
      <label>Décalage "Date d'envoi" (heures avant réception)</label>
      <input type="number" id="kd-liv-send" class="settings-input" value="${cfg.livraisonEnvoiHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 48h</small>
    </div>
    <div class="settings-form-group">
      <label>Décalage "Date production Finitions" (heures avant réception)</label>
      <input type="number" id="kd-liv-finitions" class="settings-input" value="${cfg.livraisonFinitionsHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 72h</small>
    </div>
    <div class="settings-form-group">
      <label>Décalage "Date d'impression" (heures avant réception)</label>
      <input type="number" id="kd-liv-impression" class="settings-input" value="${cfg.livraisonImpressionHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 96h</small>
    </div>

    <h4 style="margin-top:24px;margin-bottom:10px;color:#1e3a5f;">Mode Retrait imprimerie</h4>
    <div class="settings-form-group">
      <label>Décalage "Date d'envoi" (heures avant réception)</label>
      <input type="number" id="kd-ret-send" class="settings-input" value="${cfg.retraitEnvoiHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 0h</small>
    </div>
    <div class="settings-form-group">
      <label>Décalage "Date production Finitions" (heures avant réception)</label>
      <input type="number" id="kd-ret-finitions" class="settings-input" value="${cfg.retraitFinitionsHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 24h</small>
    </div>
    <div class="settings-form-group">
      <label>Décalage "Date d'impression" (heures avant réception)</label>
      <input type="number" id="kd-ret-impression" class="settings-input" value="${cfg.retraitImpressionHeures}" min="0" style="width:120px;" />
      <small style="color:#6b7280;margin-left:8px;">par défaut 48h</small>
    </div>

    <p style="color:#9ca3af;font-size:12px;margin-top:4px;">Les heures sont données à titre indicatif.</p>
    <button id="kd-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
    <div id="kd-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  panel.querySelector("#kd-save").onclick = async () => {
    const msgEl = panel.querySelector("#kd-msg");
    const newCfg = {
      livraisonEnvoiHeures: parseInt(panel.querySelector("#kd-liv-send").value) || 48,
      livraisonFinitionsHeures: parseInt(panel.querySelector("#kd-liv-finitions").value) || 72,
      livraisonImpressionHeures: parseInt(panel.querySelector("#kd-liv-impression").value) || 96,
      retraitEnvoiHeures: parseInt(panel.querySelector("#kd-ret-send").value) || 0,
      retraitFinitionsHeures: parseInt(panel.querySelector("#kd-ret-finitions").value) || 24,
      retraitImpressionHeures: parseInt(panel.querySelector("#kd-ret-impression").value) || 48
    };
    try {
      const r = await fetch("/api/config/key-dates-offsets", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify(newCfg)
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Dates clés enregistrées";
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}

export async function renderSettingsPassesConfig(panel) {
  panel.innerHTML = `<h3>Configuration des passes</h3><p style="color:#6b7280;">Chargement...</p>`;

  let cfg = { faconnage: 0, pelliculageRecto: 0, pelliculageRectoVerso: 0, rainage: 0, dorure: 0, dosCarreColle: 0 };
  try {
    const r = await fetch("/api/settings/passes-config", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ config: {} }));
    if (r.config) cfg = { ...cfg, ...r.config };
  } catch(e) { /* ignore */ }

  panel.innerHTML = `
    <h3>Configuration des passes</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Nombre de feuilles supplémentaires à ajouter selon les finitions choisies.
    </p>
    <div class="settings-form-group"><label>Façonnage (feuilles)</label><input type="number" id="passes-faconnage" class="settings-input" value="${cfg.faconnage}" min="0" style="width:100px;" /></div>
    <div class="settings-form-group"><label>Pelliculage recto (feuilles)</label><input type="number" id="passes-pelliculage-recto" class="settings-input" value="${cfg.pelliculageRecto}" min="0" style="width:100px;" /></div>
    <div class="settings-form-group"><label>Pelliculage recto/verso (feuilles)</label><input type="number" id="passes-pelliculage-rv" class="settings-input" value="${cfg.pelliculageRectoVerso}" min="0" style="width:100px;" /></div>
    <div class="settings-form-group"><label>Rainage (feuilles)</label><input type="number" id="passes-rainage" class="settings-input" value="${cfg.rainage}" min="0" style="width:100px;" /></div>
    <div class="settings-form-group"><label>Dorure (feuilles)</label><input type="number" id="passes-dorure" class="settings-input" value="${cfg.dorure}" min="0" style="width:100px;" /></div>
    <div class="settings-form-group"><label>Dos carré collé (exemplaires)</label><input type="number" id="passes-dos-carre" class="settings-input" value="${cfg.dosCarreColle}" min="0" style="width:100px;" /></div>
    <button id="passes-config-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
    <div id="passes-config-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  panel.querySelector("#passes-config-save").onclick = async () => {
    const msgEl = panel.querySelector("#passes-config-msg");
    const newCfg = {
      faconnage:              parseInt(panel.querySelector("#passes-faconnage").value) || 0,
      pelliculageRecto:       parseInt(panel.querySelector("#passes-pelliculage-recto").value) || 0,
      pelliculageRectoVerso:  parseInt(panel.querySelector("#passes-pelliculage-rv").value) || 0,
      rainage:                parseInt(panel.querySelector("#passes-rainage").value) || 0,
      dorure:                 parseInt(panel.querySelector("#passes-dorure").value) || 0,
      dosCarreColle:          parseInt(panel.querySelector("#passes-dos-carre").value) || 0
    };
    msgEl.textContent = "⏳ Enregistrement...";
    msgEl.style.color = "#6b7280";
    try {
      const resp = await fetch("/api/settings/passes-config", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify(newCfg)
      });
      let r;
      try { r = await resp.json(); } catch(je) { r = { ok: resp.ok }; }
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Configuration enregistrée";
        showNotification("✅ Configuration des passes enregistrée", "success");
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || `HTTP ${resp.status}`);
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau : " + e.message;
    }
  };
}

// ============================================================
// PALLIER TEMPS PAR GRAMMAGE
// ============================================================
export async function renderSettingsGrammageTimeConfig(panel) {
  panel.innerHTML = `<h3>Pallier temps par grammage</h3><p style="color:#6b7280;">Chargement...</p>`;

  let rules = [];
  let engines = [];
  try {
    const [rr, er] = await Promise.all([
      fetch("/api/settings/grammage-time-config", { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json()),
      fetch("/api/config/print-engines").then(r => r.json())
    ]);
    rules = rr.rules || [];
    engines = Array.isArray(er) ? er.map(e => typeof e === 'object' ? (e.name || '') : String(e || '')) : [];
  } catch(e) { /* ignore */ }

  const engineOptions = engines.map(e => `<option value="${esc(e)}">${esc(e)}</option>`).join('');

  function renderRulesTable(rs) {
    if (rs.length === 0) return '<p style="color:#9ca3af;font-size:13px;">Aucune règle définie.</p>';
    return `<table style="width:100%;border-collapse:collapse;font-size:13px;margin-bottom:12px;">
      <thead><tr>
        <th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Moteur</th>
        <th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Grammage min</th>
        <th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Grammage max</th>
        <th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Temps/feuille (s)</th>
        <th style="padding:6px 8px;background:#f3f4f6;"></th>
      </tr></thead>
      <tbody>
        ${rs.map((r, idx) => `<tr>
          <td style="padding:4px 8px;">${esc(r.engineName || '')}</td>
          <td style="padding:4px 8px;">${r.grammageMin ?? 0}</td>
          <td style="padding:4px 8px;">${r.grammageMax ?? 999}</td>
          <td style="padding:4px 8px;">${r.timePerSheetSeconds ?? 5}</td>
          <td style="padding:4px 8px;"><button class="btn btn-sm gtt-delete" data-idx="${idx}" style="color:#ef4444;padding:2px 8px;">Supprimer</button></td>
        </tr>`).join('')}
      </tbody>
    </table>`;
  }

  panel.innerHTML = `
    <h3>Pallier temps par grammage</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Configurez les plages de grammage et le temps indicatif de production par feuille pour chaque moteur d'impression.
      <br>Formule : <code>(nombre de feuilles + passes) × temps/feuille</code>
    </p>
    <div id="gtt-rules-container">${renderRulesTable(rules)}</div>
    <h4 style="margin-top:16px;margin-bottom:8px;">Ajouter une règle</h4>
    <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px;">
      <div>
        <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Moteur</label>
        <select id="gtt-engine" class="settings-input" style="min-width:160px;">
          <option value="">— Sélectionner —</option>${engineOptions}
        </select>
      </div>
      <div>
        <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Grammage min (g/m²)</label>
        <input type="number" id="gtt-gmin" class="settings-input" value="80" min="0" style="width:100px;" />
      </div>
      <div>
        <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Grammage max (g/m²)</label>
        <input type="number" id="gtt-gmax" class="settings-input" value="170" min="0" style="width:100px;" />
      </div>
      <div>
        <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Temps par feuille (secondes)</label>
        <input type="number" id="gtt-tps" class="settings-input" value="5" min="1" style="width:100px;" />
      </div>
      <button id="gtt-add" class="btn btn-primary">Ajouter</button>
    </div>
    <button id="gtt-save" class="btn btn-primary">Enregistrer toutes les règles</button>
    <div id="gtt-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  let currentRules = [...rules];

  function refreshTable() {
    panel.querySelector("#gtt-rules-container").innerHTML = renderRulesTable(currentRules);
    panel.querySelectorAll(".gtt-delete").forEach(btn => {
      btn.onclick = () => {
        const idx = parseInt(btn.dataset.idx);
        currentRules.splice(idx, 1);
        refreshTable();
      };
    });
  }
  refreshTable();

  panel.querySelector("#gtt-add").onclick = () => {
    const engine = panel.querySelector("#gtt-engine").value;
    if (!engine) { alert("Sélectionnez un moteur"); return; }
    const gmin = parseInt(panel.querySelector("#gtt-gmin").value) || 0;
    const gmax = parseInt(panel.querySelector("#gtt-gmax").value) || 999;
    const tps = parseInt(panel.querySelector("#gtt-tps").value) || 5;
    currentRules.push({ engineName: engine, grammageMin: gmin, grammageMax: gmax, timePerSheetSeconds: tps });
    refreshTable();
  };

  panel.querySelector("#gtt-save").onclick = async () => {
    const msgEl = panel.querySelector("#gtt-msg");
    try {
      const r = await fetch("/api/settings/grammage-time-config", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ rules: currentRules })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Règles enregistrées";
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}

// ============================================================
// CONFIG JDF
// ============================================================
const JDF_FIELDS = [
  { fieldId: 'numeroDossier', label: 'Numéro de dossier' },
  { fieldId: 'client', label: 'Client' },
  { fieldId: 'quantite', label: 'Quantité' },
  { fieldId: 'typeTravail', label: 'Type de travail' },
  { fieldId: 'nombreFeuilles', label: 'Nombre de feuilles' },
  { fieldId: 'formatFeuilleMachine', label: 'Format feuille machine' },
  { fieldId: 'rectoVerso', label: 'Recto/Verso' },
  { fieldId: 'moteurImpression', label: "Moteur d'impression" },
  { fieldId: 'media1', label: 'Média 1' },
  { fieldId: 'media2', label: 'Média 2' },
  { fieldId: 'media3', label: 'Média 3' },
  { fieldId: 'media4', label: 'Média 4' },
  { fieldId: 'notes', label: 'Notes' },
  { fieldId: 'faconnage', label: 'Façonnage' },
  { fieldId: 'dateDepart', label: 'Date de départ' },
  { fieldId: 'dateLivraison', label: 'Date de livraison' },
];

export async function renderSettingsJdfConfig(panel) {
  panel.innerHTML = `<h3>Configuration JDF</h3><p style="color:#6b7280;">Chargement...</p>`;

  let cfg = { enabled: false, fields: [] };
  try {
    const r = await fetch("/api/settings/jdf-config", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (r.ok) cfg = { enabled: r.enabled ?? false, fields: r.fields || [] };
  } catch(e) { /* ignore */ }

  const includedFields = new Set((cfg.fields || []).filter(f => f.included).map(f => f.fieldId));

  const checkboxesHtml = JDF_FIELDS.map(f => {
    const checked = includedFields.has(f.fieldId) ? 'checked' : '';
    return `<label style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;cursor:pointer;font-size:13px;">
      <input type="checkbox" class="jdf-field-cb" value="${esc(f.fieldId)}" ${checked} />
      <span>${esc(f.label)}</span>
    </label>`;
  }).join('');

  panel.innerHTML = `
    <h3>Configuration JDF</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Le JDF (Job Definition Format) permet d'envoyer les données de la fiche vers le moteur d'impression via PrismaSync ou Fiery.
    </p>
    <div class="settings-form-group">
      <label>Activer la génération JDF</label>
      <label style="display:inline-flex;align-items:center;gap:8px;cursor:pointer;">
        <input id="jdf-enabled" type="checkbox" style="width:16px;height:16px;" ${cfg.enabled ? 'checked' : ''} />
        <span id="jdf-enabled-label">${cfg.enabled ? 'Activé' : 'Désactivé'}</span>
      </label>
    </div>
    <div id="jdf-fields-section" style="${cfg.enabled ? '' : 'display:none;'}">
      <h4 style="margin-top:16px;margin-bottom:8px;">Champs à inclure dans le JDF</h4>
      <div style="display:flex;flex-direction:column;gap:6px;max-width:400px;margin-bottom:16px;">
        ${checkboxesHtml}
      </div>
    </div>
    <button id="jdf-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
    <div id="jdf-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  const enabledCb = panel.querySelector("#jdf-enabled");
  const fieldsSection = panel.querySelector("#jdf-fields-section");
  const enabledLabel = panel.querySelector("#jdf-enabled-label");
  enabledCb.onchange = () => {
    fieldsSection.style.display = enabledCb.checked ? '' : 'none';
    enabledLabel.textContent = enabledCb.checked ? 'Activé' : 'Désactivé';
  };

  panel.querySelector("#jdf-save").onclick = async () => {
    const msgEl = panel.querySelector("#jdf-msg");
    const enabled = enabledCb.checked;
    const fields = JDF_FIELDS.map(f => ({
      fieldId: f.fieldId,
      label: f.label,
      included: !!panel.querySelector(`.jdf-field-cb[value="${f.fieldId}"]`)?.checked
    }));
    try {
      const r = await fetch("/api/settings/jdf-config", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ enabled, fields })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Configuration JDF enregistrée";
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };
}
