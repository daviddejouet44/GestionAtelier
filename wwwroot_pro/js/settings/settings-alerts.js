import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsProductionDelayAlert(panel) {
  panel.innerHTML = `<h3>Alerte retard de production</h3><p style="color:#6b7280;">Chargement...</p>`;

  let cfg = {
    enabled: true,
    delayThresholdDays: 0,
    title: "Retard de production",
    maxJobsPerGroup: 3,
    filterMachines: []
  };
  let engines = [];

  try {
    const [cfgResp, enginesResp] = await Promise.all([
      fetch("/api/settings/production-delay-alert", {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false })),
      fetch("/api/config/print-engines").then(r => r.json()).catch(() => [])
    ]);
    if (cfgResp.ok && cfgResp.config) cfg = { ...cfg, ...cfgResp.config };
    engines = Array.isArray(enginesResp)
      ? enginesResp.map(e => typeof e === 'object' ? (e.name || '') : String(e || '')).filter(Boolean)
      : [];
  } catch(e) { /* use defaults */ }

  // Build machine checkboxes
  const machineCheckboxesHtml = engines.length === 0
    ? `<p style="color:#9ca3af;font-size:13px;">Aucun moteur configuré. Ajoutez des moteurs dans "Moteurs d'impression".</p>`
    : engines.map(engine => {
        const checked = Array.isArray(cfg.filterMachines) && cfg.filterMachines.includes(engine) ? 'checked' : '';
        return `<label style="display:flex;align-items:center;gap:8px;padding:6px 10px;background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;cursor:pointer;font-size:13px;margin-bottom:4px;">
          <input type="checkbox" class="alert-machine-cb" value="${esc(engine)}" ${checked} />
          <span>${esc(engine)}</span>
        </label>`;
      }).join('');

  panel.innerHTML = `
    <h3>⚠️ Alerte retard de production</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">
      Configurez le comportement de l'alerte "Retard de production" affichée dans le panneau latéral du Kanban et dans le bandeau global.
    </p>

    <div class="settings-section-card">
      <h4>Activation</h4>
      <div style="display:flex;align-items:center;gap:10px;margin-bottom:8px;">
        <input type="checkbox" id="alert-enabled" ${cfg.enabled ? 'checked' : ''} style="width:16px;height:16px;" />
        <label for="alert-enabled" style="font-size:14px;font-weight:500;color:#374151;">Activer l'alerte de retard de production</label>
      </div>
      <p style="color:#9ca3af;font-size:12px;margin:0;">Lorsque désactivée, la section "Retard de production" n'apparaît plus dans l'interface.</p>
    </div>

    <div class="settings-section-card">
      <h4>Titre de la section</h4>
      <div class="settings-form-group">
        <label>Libellé affiché dans le panneau latéral</label>
        <input type="text" id="alert-title" value="${esc(cfg.title || 'Retard de production')}" class="settings-input" style="max-width:400px;" placeholder="Retard de production" />
        <p style="color:#9ca3af;font-size:12px;margin-top:4px;">Ce texte apparaît comme titre de section dans la barre latérale du Kanban. Par défaut : "Retard de production".</p>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Seuil de déclenchement</h4>
      <div class="settings-form-group">
        <label>Retard minimum (jours) avant affichage dans l'alerte</label>
        <input type="number" id="alert-threshold" value="${cfg.delayThresholdDays ?? 0}" min="0" class="settings-input" style="width:100px;" />
        <p style="color:#9ca3af;font-size:12px;margin-top:4px;">
          <strong>0</strong> = tout retard (dès le lendemain de la date prévue).<br>
          <strong>1</strong> = seulement si le retard est d'au moins 1 jour, etc.
        </p>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Affichage par groupe machine</h4>
      <div class="settings-form-group">
        <label>Nombre de dossiers affichés par moteur avant "…+n autres"</label>
        <input type="number" id="alert-max-jobs" value="${cfg.maxJobsPerGroup ?? 3}" min="1" max="50" class="settings-input" style="width:100px;" />
        <p style="color:#9ca3af;font-size:12px;margin-top:4px;">Par défaut : 3 dossiers par groupe.</p>
      </div>
    </div>

    <div class="settings-section-card">
      <h4>Filtrer par moteur d'impression</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">
        Si aucun moteur n'est coché, l'alerte s'applique à <strong>tous les moteurs</strong>.<br>
        Cochez un ou plusieurs moteurs pour limiter l'alerte à ces machines uniquement.
      </p>
      <div style="display:flex;flex-direction:column;max-width:400px;margin-bottom:8px;">
        ${machineCheckboxesHtml}
      </div>
    </div>

    <button id="alert-save" class="btn btn-primary">Enregistrer la configuration</button>
    <div id="alert-msg" style="margin-top:10px;font-size:13px;"></div>
  `;

  panel.querySelector("#alert-save").onclick = async () => {
    const msgEl = panel.querySelector("#alert-msg");
    const enabled = panel.querySelector("#alert-enabled").checked;
    const title = panel.querySelector("#alert-title").value.trim() || "Retard de production";
    const delayThresholdDays = parseInt(panel.querySelector("#alert-threshold").value) || 0;
    const maxJobsPerGroup = parseInt(panel.querySelector("#alert-max-jobs").value) || 3;
    const filterMachines = Array.from(panel.querySelectorAll(".alert-machine-cb:checked")).map(cb => cb.value);

    msgEl.textContent = "⏳ Enregistrement...";
    msgEl.style.color = "#6b7280";

    try {
      const r = await fetch("/api/settings/production-delay-alert", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ enabled, title, delayThresholdDays, maxJobsPerGroup, filterMachines })
      }).then(r => r.json());

      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Configuration enregistrée";
        showNotification("✅ Alerte retard de production mise à jour", "success");
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau : " + e.message;
    }
  };
}
