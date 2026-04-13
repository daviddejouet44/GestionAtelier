import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsLogo(panel) {
  panel.innerHTML = `
    <h3>Logo de l'application</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Le logo s'affiche dans le bandeau noir en haut de l'application, à côté du titre "Gestion d'Atelier".
    </p>
    <div class="settings-form-group">
      <label>Logo actuel</label>
      <div id="logo-preview" style="margin-bottom:12px;">
        <img src="/api/logo?v=${Date.now()}" alt="Logo actuel" id="logo-current-img"
          style="max-height:60px;max-width:200px;border:1px solid #e5e7eb;border-radius:6px;padding:4px;background:#fff;"
          onerror="this.parentElement.innerHTML='<span style=&quot;color:#9ca3af;font-size:13px;&quot;>Aucun logo configuré</span>'" />
      </div>
      <input type="file" id="logo-file-input" accept=".png,.jpg,.jpeg,.gif,.webp" class="settings-input" style="margin-bottom:8px;" />
      <button id="logo-upload-btn" class="btn btn-primary">Enregistrer le logo</button>
      <button id="logo-delete-btn" class="btn" style="margin-left:8px;color:#ef4444;border-color:#ef4444;">Supprimer le logo</button>
      <div id="logo-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  panel.querySelector("#logo-upload-btn").onclick = async () => {
    const fileInput = panel.querySelector("#logo-file-input");
    const msgEl = panel.querySelector("#logo-msg");
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "Sélectionnez une image";
      return;
    }
    const formData = new FormData();
    formData.append("file", fileInput.files[0]);
    try {
      const r = await fetch("/api/logo", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: formData
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Logo enregistré";
        const img = document.getElementById("app-logo");
        if (img) { img.src = "/api/logo?v=" + Date.now(); img.style.display = "inline-block"; }
        panel._loaded = false;
        await renderSettingsLogo(panel);
      } else {
        msgEl.style.color = "#ef4444";
        msgEl.textContent = "❌ " + (r.error || "Erreur");
      }
    } catch(e) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "❌ Erreur réseau";
    }
  };

  panel.querySelector("#logo-delete-btn").onclick = async () => {
    const msgEl = panel.querySelector("#logo-msg");
    if (!confirm("Supprimer le logo ?")) return;
    try {
      const r = await fetch("/api/logo", {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Logo supprimé";
        const img = document.getElementById("app-logo");
        if (img) img.style.display = "none";
        panel._loaded = false;
        await renderSettingsLogo(panel);
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
