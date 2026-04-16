import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsLogo(panel) {
  panel.innerHTML = `
    <h3>Logos et images de l'application</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Gérez les logos et images utilisés dans l'application.
    </p>

    <h4 style="margin-bottom:8px;color:#374151;">Logo bandeau (en-tête)</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Le logo s'affiche dans le bandeau noir en haut de l'application, à côté du titre "Gestion d'Atelier".
    </p>
    <div class="settings-form-group">
      <label>Logo actuel (bandeau)</label>
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

    <hr style="margin:24px 0;border:none;border-top:1px solid #e5e7eb;" />

    <h4 style="margin-bottom:8px;color:#374151;">Logo page de connexion</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Ce logo s'affiche sur la page de connexion. S'il n'est pas configuré, aucun logo n'apparaît sur la page de connexion.
    </p>
    <div class="settings-form-group">
      <label>Logo actuel (page de connexion)</label>
      <div id="logo-login-preview" style="margin-bottom:12px;">
        <img src="/api/logo-login?v=${Date.now()}" alt="Logo connexion actuel" id="logo-login-current-img"
          style="max-height:60px;max-width:200px;border:1px solid #e5e7eb;border-radius:6px;padding:4px;background:#fff;"
          onerror="this.parentElement.innerHTML='<span style=&quot;color:#9ca3af;font-size:13px;&quot;>Aucun logo de connexion configuré</span>'" />
      </div>
      <input type="file" id="logo-login-file-input" accept=".png,.jpg,.jpeg,.gif,.webp" class="settings-input" style="margin-bottom:8px;" />
      <button id="logo-login-upload-btn" class="btn btn-primary">Enregistrer le logo de connexion</button>
      <button id="logo-login-delete-btn" class="btn" style="margin-left:8px;color:#ef4444;border-color:#ef4444;">Supprimer le logo de connexion</button>
      <div id="logo-login-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

    <hr style="margin:24px 0;border:none;border-top:1px solid #e5e7eb;" />

    <h4 style="margin-bottom:8px;color:#374151;">Image de fond — page de connexion</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Image affichée en arrière-plan de la page de connexion. Si non configurée, le fond par défaut est utilisé.
    </p>
    <div class="settings-form-group">
      <div id="bg-login-preview" style="margin-bottom:12px;">
        <img src="/api/background-login?v=${Date.now()}" alt="Image de fond connexion" id="bg-login-current-img"
          style="max-height:100px;max-width:300px;border:1px solid #e5e7eb;border-radius:6px;padding:4px;background:#fff;"
          onerror="this.parentElement.innerHTML='<span style=&quot;color:#9ca3af;font-size:13px;&quot;>Aucune image de fond configurée</span>'" />
      </div>
      <input type="file" id="bg-login-file-input" accept=".png,.jpg,.jpeg,.gif,.webp" class="settings-input" style="margin-bottom:8px;" />
      <button id="bg-login-upload-btn" class="btn btn-primary">Enregistrer l'image de fond</button>
      <button id="bg-login-delete-btn" class="btn" style="margin-left:8px;color:#ef4444;border-color:#ef4444;">Supprimer</button>
      <div id="bg-login-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>

    <hr style="margin:24px 0;border:none;border-top:1px solid #e5e7eb;" />

    <h4 style="margin-bottom:8px;color:#374151;">Image de bandeau header</h4>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Image affichée dans le bandeau noir en haut de l'application (remplace le fond uni noir).
    </p>
    <div class="settings-form-group">
      <div id="header-banner-preview" style="margin-bottom:12px;">
        <img src="/api/header-banner?v=${Date.now()}" alt="Image bandeau header" id="header-banner-current-img"
          style="max-height:60px;max-width:300px;border:1px solid #e5e7eb;border-radius:6px;padding:4px;background:#fff;"
          onerror="this.parentElement.innerHTML='<span style=&quot;color:#9ca3af;font-size:13px;&quot;>Aucune image de bandeau configurée</span>'" />
      </div>
      <input type="file" id="header-banner-file-input" accept=".png,.jpg,.jpeg,.gif,.webp" class="settings-input" style="margin-bottom:8px;" />
      <button id="header-banner-upload-btn" class="btn btn-primary">Enregistrer l'image de bandeau</button>
      <button id="header-banner-delete-btn" class="btn" style="margin-left:8px;color:#ef4444;border-color:#ef4444;">Supprimer</button>
      <div id="header-banner-msg" style="margin-top:8px;font-size:13px;"></div>
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

  panel.querySelector("#logo-login-upload-btn").onclick = async () => {
    const fileInput = panel.querySelector("#logo-login-file-input");
    const msgEl = panel.querySelector("#logo-login-msg");
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = "#ef4444";
      msgEl.textContent = "Sélectionnez une image";
      return;
    }
    const formData = new FormData();
    formData.append("file", fileInput.files[0]);
    try {
      const r = await fetch("/api/logo-login", {
        method: "POST",
        headers: { "Authorization": `Bearer ${authToken}` },
        body: formData
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Logo de connexion enregistré";
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

  panel.querySelector("#logo-login-delete-btn").onclick = async () => {
    const msgEl = panel.querySelector("#logo-login-msg");
    if (!confirm("Supprimer le logo de connexion ?")) return;
    try {
      const r = await fetch("/api/logo-login", {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        msgEl.style.color = "#16a34a";
        msgEl.textContent = "✅ Logo de connexion supprimé";
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

  // Background login image handlers
  const makeImageHandler = (uploadId, deleteId, msgId, apiPath, onUpdate) => {
    panel.querySelector(`#${uploadId}`).onclick = async () => {
      const fileInput = panel.querySelector(`#${uploadId.replace('-btn', '-input')}`);
      const msgEl = panel.querySelector(`#${msgId}`);
      if (!fileInput.files || fileInput.files.length === 0) {
        msgEl.style.color = "#ef4444"; msgEl.textContent = "Sélectionnez une image"; return;
      }
      const formData = new FormData();
      formData.append("file", fileInput.files[0]);
      try {
        const r = await fetch(apiPath, { method: "POST", headers: { "Authorization": `Bearer ${authToken}` }, body: formData }).then(r => r.json());
        if (r.ok) {
          msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Image enregistrée";
          if (onUpdate) onUpdate();
          panel._loaded = false; await renderSettingsLogo(panel);
        } else { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
      } catch(e) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
    };
    panel.querySelector(`#${deleteId}`).onclick = async () => {
      const msgEl = panel.querySelector(`#${msgId}`);
      if (!confirm("Supprimer cette image ?")) return;
      try {
        const r = await fetch(apiPath, { method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
        if (r.ok) {
          msgEl.style.color = "#16a34a"; msgEl.textContent = "✅ Image supprimée";
          if (onUpdate) onUpdate();
          panel._loaded = false; await renderSettingsLogo(panel);
        } else { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ " + (r.error || "Erreur"); }
      } catch(e) { msgEl.style.color = "#ef4444"; msgEl.textContent = "❌ Erreur réseau"; }
    };
  };

  makeImageHandler("bg-login-upload-btn", "bg-login-delete-btn", "bg-login-msg", "/api/background-login", null);
  makeImageHandler("header-banner-upload-btn", "header-banner-delete-btn", "header-banner-msg", "/api/header-banner", () => {
    // Refresh header banner in the app
    const headerEl = document.querySelector(".app-header, header");
    if (headerEl) {
      const bannerUrl = "/api/header-banner?v=" + Date.now();
      headerEl.style.backgroundImage = `url('${bannerUrl}')`;
      headerEl.style.backgroundSize = "cover";
      headerEl.style.backgroundPosition = "center";
    }
  });
}
