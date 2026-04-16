import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsCoverProducts(panel) {
  panel.innerHTML = `<h3>Produits nécessitant une couverture</h3><p style="color:#6b7280;">Chargement...</p>`;

  let selectedProducts = [];
  let workTypes = [];
  try {
    selectedProducts = await fetch("/api/settings/cover-products", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
    const wt = await fetch("/api/settings/work-types", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);
    if (wt && Array.isArray(wt.types)) workTypes = wt.types;
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
  try {
    const r = await fetch("/api/settings/sheet-calculation-rules", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ rules: {} }));
    rules = r.rules || {};
  } catch(e) { /* ignore */ }

  const rulesHtml = Object.entries(rules).map(([type, divisor]) =>
    `<tr><td style="padding:4px 8px;">${esc(type)}</td><td style="padding:4px 8px;">${divisor}</td></tr>`
  ).join("") || '<tr><td colspan="2" style="color:#9ca3af;font-size:13px;padding:8px;">Aucune règle définie</td></tr>';

  panel.innerHTML = `
    <h3>Règles de calcul — Nombre de feuilles</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Formule : <code>Nombre de feuilles = Quantité ÷ Diviseur</code> (arrondi au supérieur)
    </p>
    <table style="width:100%;max-width:500px;border-collapse:collapse;margin-bottom:16px;font-size:13px;">
      <thead><tr><th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Type de travail</th><th style="text-align:left;padding:6px 8px;background:#f3f4f6;">Diviseur</th></tr></thead>
      <tbody id="calc-rules-tbody">${rulesHtml}</tbody>
    </table>
    <h4 style="margin-bottom:8px;">Modifier / ajouter une règle</h4>
    <div style="display:flex;gap:8px;align-items:center;margin-bottom:8px;">
      <input type="text" id="calc-rule-type" placeholder="Type de travail" class="settings-input" style="flex:1;max-width:220px;" />
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

  let delayHours = 48;
  try {
    const r = await fetch("/api/settings/delivery-delay", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({ delayHours: 48 }));
    delayHours = r.delayHours || 48;
  } catch(e) { /* ignore */ }

  panel.innerHTML = `
    <h3>Dates clés</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Date de livraison = Date de départ + délai en heures
    </p>
    <div class="settings-form-group">
      <label>Délai (heures)</label>
      <input type="number" id="delivery-delay-hours" class="settings-input" value="${delayHours}" min="0" style="width:120px;" />
    </div>
    <button id="delivery-delay-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
    <div id="delivery-delay-msg" style="margin-top:8px;font-size:13px;"></div>
  `;

  panel.querySelector("#delivery-delay-save").onclick = async () => {
    const msgEl = panel.querySelector("#delivery-delay-msg");
    const hours = parseInt(panel.querySelector("#delivery-delay-hours").value);
    try {
      const r = await fetch("/api/settings/delivery-delay", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ delayHours: hours })
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Délai enregistré";
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
    try {
      const r = await fetch("/api/settings/passes-config", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify(newCfg)
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Configuration enregistrée";
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
