// app/dashboard.js — Vue tableau de bord

import { currentUser, authToken } from '../core.js';

export async function initDashboardView() {
  const dashEl = document.getElementById("dashboard");
  dashEl.innerHTML = `
    <div class="settings-container">
      <h2>Dashboard</h2>
      <div id="dashboard-content"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  await loadDashboardData();
}

export async function loadDashboardData() {
  const contentEl = document.getElementById("dashboard-content");
  if (!contentEl) return;

  // Load PrismaSync URL setting
  let prismaSyncUrl = "";
  try {
    const r = await fetch("/api/config/prismasync-url").then(r => r.json()).catch(() => ({}));
    if (r.ok && r.url) prismaSyncUrl = r.url;
  } catch(e) { /* use default */ }

  // Check if a dashboard image exists
  let dashboardImageExists = false;
  try {
    const imgResp = await fetch("/api/dashboard-image", { method: "HEAD" }).catch(() => null);
    dashboardImageExists = imgResp?.status === 200;
  } catch(e) { /* no image */ }

  // Build the dashboard content (no iframe — replaced by image)
  let html = `<div style="display:flex;flex-direction:column;gap:16px;">`;

  // PrismaSync link + settings button
  html += `<div style="display:flex;align-items:center;gap:10px;flex-shrink:0;flex-wrap:wrap;">`;
  if (prismaSyncUrl) {
    html += `<a href="${prismaSyncUrl}" target="_blank" rel="noopener" class="btn btn-primary" style="font-size:13px;font-weight:600;text-decoration:none;">🔗 Ouvrir PrismaSync ↗</a>`;
  }
  if (currentUser && currentUser.profile === 3) {
    html += `<button id="prismasync-settings-btn" class="btn btn-sm" style="font-size:12px;">⚙️ Modifier l'URL PrismaSync</button>`;
    // Use a label wrapping a hidden file input for maximum browser compatibility
    html += `<label id="dashboard-image-upload-btn" class="btn btn-sm" style="font-size:12px;cursor:pointer;display:inline-flex;align-items:center;gap:4px;" for="dashboard-file-input">🖼️ ${dashboardImageExists ? 'Modifier l\'image' : 'Ajouter une image'}</label>`;
    html += `<input type="file" id="dashboard-file-input" accept="image/*,.png,.jpg,.jpeg,.gif,.webp" style="display:none;" />`;
    if (dashboardImageExists) {
      html += `<button id="dashboard-image-delete-btn" class="btn btn-sm" style="font-size:12px;color:#ef4444;border-color:#ef4444;">🗑 Supprimer l\'image</button>`;
    }
  }
  html += `</div>`;

  // Dashboard image section
  if (dashboardImageExists) {
    html += `<div style="width:100%;max-height:calc(100vh - 220px);overflow:hidden;border-radius:12px;border:1px solid #e5e7eb;box-shadow:0 2px 8px rgba(0,0,0,0.08);">
      <img src="/api/dashboard-image?v=${Date.now()}" alt="Image du dashboard" style="width:100%;height:auto;display:block;max-height:calc(100vh - 220px);object-fit:contain;" />
    </div>`;
  } else if (currentUser && currentUser.profile === 3) {
    // Admin placeholder with Prismalytics links
    html += `<div style="background:#f9fafb;border:2px dashed #e5e7eb;border-radius:12px;padding:40px;text-align:center;color:#9ca3af;">
      <p style="font-size:16px;font-weight:600;margin:0 0 8px;">Image du dashboard non configurée</p>
      <p style="font-size:13px;margin:0;">Cliquez sur "Ajouter une image" pour uploader une image à afficher ici.</p>
    </div>
    <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:16px;">
      <a href="https://prismalytics-eu.cpp.canon/accounting#" target="_blank" rel="noopener"
         style="display:flex;flex-direction:column;gap:10px;background:var(--bg-card);border:1px solid var(--border-light);border-radius:var(--radius-lg);padding:24px;text-decoration:none;color:inherit;box-shadow:var(--shadow-md);">
        <span style="font-size:36px;">📊</span>
        <strong style="font-size:16px;font-weight:700;">Accounting</strong>
        <span style="font-size:12px;color:var(--text-secondary);">Suivi des impressions et facturation Canon Prismalytics</span>
        <span style="font-size:12px;color:var(--primary);font-weight:600;margin-top:4px;">Ouvrir ↗</span>
      </a>
      <a href="https://prismalytics-eu.cpp.canon/dashboard#" target="_blank" rel="noopener"
         style="display:flex;flex-direction:column;gap:10px;background:var(--bg-card);border:1px solid var(--border-light);border-radius:var(--radius-lg);padding:24px;text-decoration:none;color:inherit;box-shadow:var(--shadow-md);">
        <span style="font-size:36px;">📈</span>
        <strong style="font-size:16px;font-weight:700;">Dashboard</strong>
        <span style="font-size:12px;color:var(--text-secondary);">Vue d'ensemble et statistiques de production Prismalytics</span>
        <span style="font-size:12px;color:var(--primary);font-weight:600;margin-top:4px;">Ouvrir ↗</span>
      </a>
    </div>`;
  } else {
    html += `<div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:12px;padding:40px;text-align:center;color:#9ca3af;">
      <p style="font-size:16px;font-weight:600;margin:0;">Image du dashboard non configurée</p>
    </div>`;
  }

  html += `</div>`;
  contentEl.innerHTML = html;

  // Wire up buttons
  const settingsBtn = contentEl.querySelector("#prismasync-settings-btn");
  if (settingsBtn) settingsBtn.onclick = () => _showPrismaSyncUrlEditor(contentEl);

  // File input change handler — triggers when user selects a file via the label/input
  const fileInput = contentEl.querySelector("#dashboard-file-input");
  if (fileInput) {
    fileInput.onchange = async () => {
      const file = fileInput.files && fileInput.files[0];
      if (!file) return;
      const uploadLabel = contentEl.querySelector("#dashboard-image-upload-btn");
      const originalText = uploadLabel ? uploadLabel.textContent : '';
      if (uploadLabel) { uploadLabel.style.opacity = '0.6'; uploadLabel.style.pointerEvents = 'none'; uploadLabel.textContent = '⏳ Upload...'; }
      try {
        const formData = new FormData();
        formData.append("file", file);
        const r = await fetch("/api/dashboard-image", {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` },
          body: formData
        }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
        if (r.ok) {
          await loadDashboardData();
        } else {
          alert("Erreur upload : " + (r.error || "Inconnu"));
          if (uploadLabel) { uploadLabel.style.opacity = ''; uploadLabel.style.pointerEvents = ''; uploadLabel.textContent = originalText; }
        }
      } catch(e) {
        alert("Erreur réseau lors de l'upload");
        if (uploadLabel) { uploadLabel.style.opacity = ''; uploadLabel.style.pointerEvents = ''; uploadLabel.textContent = originalText; }
      }
      // Reset so the same file can be selected again
      fileInput.value = '';
    };
  }

  const deleteBtn = contentEl.querySelector("#dashboard-image-delete-btn");
  if (deleteBtn) {
    deleteBtn.onclick = async () => {
      if (!confirm("Supprimer l'image du dashboard ?")) return;
      const r = await fetch("/api/dashboard-image", {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(res => res.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        await loadDashboardData();
      } else {
        alert("Erreur : " + (r.error || ""));
      }
    };
  }
}

function _showPrismaSyncUrlEditor(contentEl) {
  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.4);display:flex;align-items:center;justify-content:center;z-index:9999;";
  const box = document.createElement("div");
  box.style.cssText = "background:white;border-radius:12px;padding:24px;min-width:380px;max-width:520px;box-shadow:0 8px 32px rgba(0,0,0,.2);";
  box.innerHTML = `
    <h4 style="margin:0 0 14px;font-size:15px;font-weight:700;">URL PrismaSync</h4>
    <input type="url" id="prismasync-url-input" placeholder="https://prismasync.example.com/dashboard" class="settings-input" style="width:100%;margin-bottom:12px;" />
    <div style="display:flex;gap:8px;justify-content:flex-end;">
      <button id="prismasync-url-cancel" class="btn">Annuler</button>
      <button id="prismasync-url-save" class="btn btn-primary">Enregistrer</button>
    </div>
  `;
  overlay.appendChild(box);
  document.body.appendChild(overlay);
  overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };
  box.querySelector("#prismasync-url-cancel").onclick = () => overlay.remove();
  box.querySelector("#prismasync-url-save").onclick = async () => {
    const url = box.querySelector("#prismasync-url-input").value.trim();
    const r = await fetch("/api/config/prismasync-url", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ url })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      overlay.remove();
      await loadDashboardData();
    } else {
      alert("Erreur : " + (r.error || ""));
    }
  };
}

