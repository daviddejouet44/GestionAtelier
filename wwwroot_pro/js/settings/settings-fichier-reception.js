// settings-fichier-reception.js — Paramétrage alerte date de réception du fichier
import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsFichierReception(panel) {
  let alertHours = 24;
  try {
    const r = await fetch("/api/config/fichier-reception", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({}));
    if (r.ok && r.config) alertHours = r.config.alertHours ?? 24;
  } catch {}

  panel.innerHTML = `
    <h3>⏰ Alerte date de réception du fichier</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:24px;">
      Configurez l'alerte affichée dans le bandeau global lorsqu'une fiche « sans PDF » a une
      <strong>date de réception du fichier</strong> imminente ou dépassée.
    </p>
    <div class="settings-section-card">
      <h4>Seuil d'alerte</h4>
      <div class="settings-form-group">
        <label>Alerte avant la date de réception du fichier (heures)</label>
        <input type="number" id="fiche-rec-alert-hours" min="1" value="${alertHours}" class="settings-input" style="width:120px;" />
        <p style="color:#6b7280;font-size:12px;margin-top:4px;">
          Signale une fiche lorsque sa date de réception du fichier est à moins de <em>x</em> heures ou déjà dépassée. Défaut : 24h.
        </p>
      </div>
      <button id="fiche-rec-save" class="btn btn-primary">Enregistrer</button>
      <span id="fiche-rec-saved" style="display:none;color:#16a34a;font-size:13px;margin-left:10px;">✅ Enregistré</span>
    </div>
  `;

  panel.querySelector("#fiche-rec-save").onclick = async () => {
    const hours = Math.max(1, parseInt(panel.querySelector("#fiche-rec-alert-hours").value || "24", 10) || 24);
    const r = await fetch("/api/config/fichier-reception", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ alertHours: hours })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      const savedEl = panel.querySelector("#fiche-rec-saved");
      if (savedEl) { savedEl.style.display = ''; setTimeout(() => { savedEl.style.display = 'none'; }, 2500); }
      showNotification("✅ Seuil alerte réception fichier enregistré", "success");
    } else showNotification("❌ " + (r.error || "Erreur"), "error");
  };
}
