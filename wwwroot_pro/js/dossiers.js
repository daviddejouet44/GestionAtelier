// dossiers.js — Dossiers de production
import { authToken, showNotification, fnKey } from './core.js';

export function showDossiers() {
  // Navigation handled by app.js
  initDossiersView();
}

export async function initDossiersView() {
  const el = document.getElementById("dossiers");
  el.innerHTML = `
    <div class="settings-container">
      <h2>Dossiers de production</h2>
      <div style="display: flex; gap: 10px; margin-bottom: 16px;">
        <button id="dossiers-refresh" class="btn btn-primary">Rafraîchir</button>
      </div>
      <div id="dossiers-list"><p style="color:#6b7280;">Chargement...</p></div>
    </div>
  `;
  document.getElementById("dossiers-refresh").onclick = loadDossiersList;
  await loadDossiersList();
}

export async function loadDossiersList() {
  const listEl = document.getElementById("dossiers-list");
  if (!listEl) return;
  try {
    const folders = await fetch("/api/production-folders", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);

    if (!Array.isArray(folders) || folders.length === 0) {
      listEl.innerHTML = '<p style="color:#9ca3af;text-align:center;padding:40px;">Aucun dossier de production</p>';
      return;
    }

    listEl.innerHTML = "";
    const grid = document.createElement("div");
    grid.style.cssText = "display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px;";

    folders.forEach(folder => {
      const folderName = folder.fileName || '';
      const card = document.createElement("div");
      card.className = "dossier-card";
      card.style.cssText = "background: white; border: 1px solid #e5e7eb; border-radius: 16px; padding: 20px; box-shadow: 0 2px 12px rgba(0,0,0,0.08); cursor: pointer; transition: all 0.2s;";
      card.onmouseenter = () => { card.style.boxShadow = "0 4px 20px rgba(0,0,0,0.15)"; card.style.transform = "translateY(-2px)"; };
      card.onmouseleave = () => { card.style.boxShadow = "0 2px 12px rgba(0,0,0,0.08)"; card.style.transform = ""; };
      card.innerHTML = `
        <div style="display:flex;align-items:flex-start;gap:12px;margin-bottom:12px;min-width:0;">
          ${folder.numeroDossier ? `<div style="font-size:28px;font-weight:800;color:#111827;min-width:56px;font-family:monospace;line-height:1;">${folder.numeroDossier}</div>` : ''}
          <div style="min-width:0;flex:1;">
            <div class="dossier-card-name" title="${folderName}" style="font-weight:600;font-size:14px;color:#374151;word-break:break-word;">${folderName}</div>
            <div style="font-size:12px;color:#6b7280;margin-top:2px;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : ''}</div>
          </div>
        </div>
        <div style="display:flex;justify-content:space-between;align-items:center;">
          <span style="background:#dbeafe;color:#1e40af;padding:4px 10px;border-radius:20px;font-size:12px;font-weight:500;">${folder.currentStage || 'Début de production'}</span>
          <span style="color:#6b7280;font-size:12px;">${typeof folder.files === 'number' ? folder.files : 0} fichier(s)</span>
        </div>
      `;
      card.onclick = (e) => { if (e.target.closest(".btn-danger")) return; openDossierDetail(folder._id || folder.id); };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-danger btn-sm";
      btnDelete.textContent = "Supprimer";
      btnDelete.style.cssText = "margin-top:12px;width:100%;";
      btnDelete.onclick = async (e) => {
        e.stopPropagation();
        if (!confirm(`Supprimer le dossier "${folderName}" et tous ses fichiers ?`)) return;
        const r = await fetch(`/api/production-folder?path=${encodeURIComponent(folder.path || folder.folderPath || "")}`, { method: "DELETE" }).then(r=>r.json()).catch(()=>({ok:false,error:"Erreur réseau"}));
        if (r.ok) { showNotification("Dossier supprimé", "success"); loadDossiersList(); }
        else showNotification("Erreur: " + (r.error || ""), "error");
      };
      card.appendChild(btnDelete);

      grid.appendChild(card);
    });

    listEl.appendChild(grid);
  } catch (err) {
    listEl.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

export async function openDossierDetail(dossierId) {
  try {
    const folder = await fetch(`/api/production-folders/${dossierId}`, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null);

    if (!folder) {
      showNotification("❌ Dossier introuvable", "error");
      return;
    }

    const fabFilePath = folder.originalFilePath || folder.currentFilePath || "";
    const fabFileName = folder.fileName || "";
    let fab = {};
    if (fabFileName) {
      try {
        const fabResp = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fabFileName), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    if (!fab.client && !fab.numeroDossier && fabFilePath) {
      try {
        const fabResp = await fetch("/api/fabrication?fullPath=" + encodeURIComponent(fabFilePath), {
          headers: { "Authorization": `Bearer ${authToken}` }
        }).then(r => r.json());
        if (fabResp && fabResp.ok !== false) fab = fabResp;
      } catch(e) { /* use empty */ }
    }
    if (!fab.client && !fab.numeroDossier) {
      const embedded = folder.fabricationSheet || {};
      if (Object.keys(embedded).length > 0) fab = embedded;
    }

    const overlay = document.createElement("div");
    overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:flex-start;justify-content:center;padding:40px 20px;overflow-y:auto;";

    const modal = document.createElement("div");
    modal.style.cssText = "background:white;border-radius:16px;padding:32px;width:100%;max-width:800px;box-shadow:0 20px 60px rgba(0,0,0,0.3);";

    const numeroDossier = fab.numeroDossier || folder.numeroDossier || String(folder.number||0).padStart(3,'0');
    modal.innerHTML = `
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:24px;">
        <h2 style="margin:0;font-size:22px;color:#111827;">Dossier ${numeroDossier} — ${folder.fileName||''}</h2>
        <button id="dossier-close" style="background:none;border:none;font-size:24px;cursor:pointer;color:#6b7280;">✕</button>
      </div>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px;">
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">ÉTAPE ACTUELLE</label>
          <span style="background:#dbeafe;color:#1e40af;padding:6px 12px;border-radius:20px;font-size:13px;font-weight:500;">${folder.currentStage||'Début de production'}</span>
        </div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">DATE DE CRÉATION</label>
          <span style="font-size:14px;color:#111827;">${folder.createdAt ? new Date(folder.createdAt).toLocaleDateString("fr-FR") : '—'}</span>
        </div>
      </div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fiche de fabrication</h3>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:24px;">
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">DÉLAI</label><input id="df-delai" type="date" value="${fab.delai ? (() => { try { return new Date(fab.delai).toISOString().split('T')[0]; } catch(e) { return ''; } })() : ''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">N° DOSSIER</label><input id="df-numero-dossier" type="text" value="${fab.numeroDossier||folder.numeroDossier||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">CLIENT</label><input id="df-client" type="text" value="${fab.client||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">QUANTITÉ</label><input id="df-quantite" type="number" value="${fab.quantite||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">FORMAT</label><input id="df-format" type="text" value="${fab.format||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">MOTEUR D'IMPRESSION</label><input id="df-moteur" type="text" value="${fab.moteurImpression||fab.machine||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">TYPE DE TRAVAIL</label><input id="df-type-travail" type="text" value="${fab.typeTravail||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">RECTO/VERSO</label><input id="df-recto-verso" type="text" value="${fab.rectoVerso||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">FAÇONNAGE</label><input id="df-faconnage" type="text" value="${fab.faconnage||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">PAPIER / MÉDIA 1</label><input id="df-media1" type="text" value="${fab.media1||fab.papier||''}" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;" /></div>
        <div style="grid-column:1/-1;"><label style="font-size:12px;color:#6b7280;font-weight:600;display:block;margin-bottom:4px;">NOTES</label><textarea id="df-notes" rows="2" style="width:100%;padding:8px;border:1px solid #e5e7eb;border-radius:8px;font-size:13px;">${fab.notes||''}</textarea></div>
      </div>
      <button id="df-save" class="btn btn-primary" style="margin-bottom:24px;">Enregistrer la fiche</button>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Fichiers par étape</h3>
      <div id="df-files" style="margin-bottom:24px;"></div>

      <h3 style="font-size:16px;color:#111827;margin-bottom:12px;">Ajouter un fichier</h3>
      <div style="border:2px dashed #e5e7eb;border-radius:12px;padding:20px;text-align:center;cursor:pointer;" id="df-upload-zone">
        <p style="color:#6b7280;margin:0;">Cliquez ou déposez un fichier (PDF, Excel, Word, PSD, InDesign...)</p>
        <input type="file" id="df-upload-input" style="display:none;" multiple />
      </div>
    `;

    const filesEl = modal.querySelector("#df-files");
    const files = folder.files || [];
    if (files.length === 0) {
      filesEl.innerHTML = '<p style="color:#9ca3af;font-size:13px;">Aucun fichier dans ce dossier</p>';
    } else {
      files.forEach(f => {
        const row = document.createElement("div");
        row.style.cssText = "display:flex;align-items:center;gap:12px;padding:10px 14px;background:#f9fafb;border-radius:8px;margin-bottom:8px;";
        row.innerHTML = `
          <span style="font-size:13px;font-weight:700;color:#BC0024;font-family:monospace;">PDF</span>
          <div style="flex:1;">
            <div style="font-size:13px;font-weight:600;color:#111827;">${f.fileName||''}</div>
            <div style="font-size:11px;color:#6b7280;">${f.stage||''} · ${f.addedAt ? new Date(f.addedAt).toLocaleDateString("fr-FR") : ''}</div>
          </div>
          <a href="/api/production-folders/${dossierId}/files/${encodeURIComponent(f.fileName||'')}" target="_blank" class="btn btn-sm">Télécharger</a>
        `;
        filesEl.appendChild(row);
      });
    }

    overlay.appendChild(modal);
    document.body.appendChild(overlay);

    modal.querySelector("#dossier-close").onclick = () => overlay.remove();
    overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };

    modal.querySelector("#df-save").onclick = async () => {
      const savePayload = {
        fullPath: fabFilePath || fabFileName || "",
        fileName: fabFileName,
        delai: modal.querySelector("#df-delai").value || null,
        numeroDossier: modal.querySelector("#df-numero-dossier").value || null,
        client: modal.querySelector("#df-client").value,
        quantite: parseInt(modal.querySelector("#df-quantite").value, 10) || null,
        format: modal.querySelector("#df-format").value,
        moteurImpression: modal.querySelector("#df-moteur").value,
        machine: modal.querySelector("#df-moteur").value,
        typeTravail: modal.querySelector("#df-type-travail").value,
        rectoVerso: modal.querySelector("#df-recto-verso").value,
        faconnage: modal.querySelector("#df-faconnage").value,
        media1: modal.querySelector("#df-media1").value,
        notes: modal.querySelector("#df-notes").value
      };
      const r = await fetch("/api/fabrication", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify(savePayload)
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("✅ Fiche enregistrée", "success");
      } else {
        showNotification("❌ Erreur : " + (r.error || ""), "error");
      }
    };

    const uploadZone = modal.querySelector("#df-upload-zone");
    const uploadInput = modal.querySelector("#df-upload-input");
    uploadZone.onclick = () => uploadInput.click();
    uploadZone.addEventListener("dragover", e => { e.preventDefault(); uploadZone.style.background = "#f3f4f6"; });
    uploadZone.addEventListener("dragleave", () => { uploadZone.style.background = ""; });
    uploadZone.addEventListener("drop", async (e) => {
      e.preventDefault();
      uploadZone.style.background = "";
      const files = Array.from(e.dataTransfer.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
    });
    uploadInput.addEventListener("change", async (e) => {
      const files = Array.from(e.target.files || []);
      if (files.length) await uploadDossierFiles(dossierId, files, overlay);
      uploadInput.value = "";
    });

  } catch (err) {
    showNotification("❌ " + err.message, "error");
  }
}

async function uploadDossierFiles(dossierId, files, overlay) {
  for (const file of files) {
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch(`/api/production-folders/${dossierId}/upload`, {
      method: "POST",
      headers: { "Authorization": `Bearer ${authToken}` },
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${file.name} ajouté`, "success");
    } else {
      showNotification(`❌ Erreur upload ${file.name}`, "error");
    }
  }
  overlay.remove();
  openDossierDetail(dossierId);
}
