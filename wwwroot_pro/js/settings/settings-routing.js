import { authToken, showNotification, esc } from '../core.js';

export async function renderSettingsPrintRouting(panel) {
  panel.innerHTML = `<h3>Routage Impression</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshPrintRoutingPanel(panel);
}

export async function refreshPrintRoutingPanel(panel) {
  let fieryRoutings = [], prismaSyncRoutings = [], directPrintRoutings = [], prismaPrepareRoutings = [], types = [], engines = [];
  try {
    const [r1, r2, r3, r4, r5, r6] = await Promise.all([
      fetch("/api/config/fiery-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/prismasync-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/direct-print-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => []),
      fetch("/api/config/print-engines").then(r => r.json()).catch(() => []),
      fetch("/api/config/prisma-prepare-routing").then(r => r.json()).catch(() => [])
    ]);
    fieryRoutings = Array.isArray(r1) ? r1 : [];
    prismaSyncRoutings = Array.isArray(r2) ? r2 : [];
    directPrintRoutings = Array.isArray(r3) ? r3 : [];
    types = Array.isArray(r4) ? r4 : [];
    engines = Array.isArray(r5) ? r5 : (r5.engines || []);
    prismaPrepareRoutings = Array.isArray(r6) ? r6 : [];
  } catch(e) { /* use empty */ }

  const typeOptions = types.map(t => `<option value="${t.replace(/"/g,'&quot;')}">${t}</option>`).join("");
  const engineOptions = engines.map(e => {
    const v = typeof e === 'object' ? (e.name || '') : String(e || '');
    return `<option value="${v.replace(/"/g,'&quot;')}">${v}</option>`;
  }).join("");

  panel.innerHTML = `
    <h3>Routage Impression</h3>
    <p style="color:#6b7280;margin-bottom:20px;">Configurez le routage des fichiers pour chaque action du bouton <strong>Actions</strong> de la tuile <em>En attente</em>.</p>

    <!-- Sous-section 1 : Routage PrismaSync -->
    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:24px;">
      <h4 style="margin:0 0 8px;">1. Routage PrismaSync</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Pour l'action <strong>Envoyer vers PrismaSync</strong> : mapping Type de travail + Moteur d'impression + Médias → Contrôleur PrismaSync. Le PDF est déplacé dans la tuile <em>Impression en cours</em>.</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="psync-type" class="settings-input" style="min-width:180px;">
            <option value="">— Sélectionner —</option>${typeOptions}
          </select>
        </div>
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Moteur d'impression</label>
          <select id="psync-engine" class="settings-input" style="min-width:180px;">
            <option value="">— Sélectionner —</option>${engineOptions}
          </select>
        </div>
        <div style="min-width:120px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Média 1</label>
          <input type="text" id="psync-media1" placeholder="Ex: 135g couché" class="settings-input" style="width:100%;" />
        </div>
        <div style="min-width:120px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Média 2</label>
          <input type="text" id="psync-media2" placeholder="(optionnel)" class="settings-input" style="width:100%;" />
        </div>
        <div style="min-width:120px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Média 3</label>
          <input type="text" id="psync-media3" placeholder="(optionnel)" class="settings-input" style="width:100%;" />
        </div>
        <div style="min-width:120px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Média 4</label>
          <input type="text" id="psync-media4" placeholder="(optionnel)" class="settings-input" style="width:100%;" />
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Chemin contrôleur PrismaSync</label>
          <input type="text" id="psync-path" placeholder="Ex: C:\\Flux\\PrismaSync\\Presse1" class="settings-input" style="width:100%;" />
        </div>
        <input type="hidden" id="psync-edit-id" value="" />
        <button id="psync-save" class="btn btn-primary">Enregistrer</button>
      </div>
      <div id="psync-list">
        ${prismaSyncRoutings.length === 0 ? '<p style="color:#9ca3af;">Aucun routage PrismaSync configuré</p>' :
          prismaSyncRoutings.map(r => `
            <div style="display:flex;align-items:center;gap:8px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;flex-wrap:wrap;">
              <div style="flex:0 0 160px;"><strong style="font-size:13px;">${r.typeTravail||'—'}</strong></div>
              <div style="flex:0 0 140px;font-size:12px;color:#6b7280;">${r.moteurImpression||'—'}</div>
              <div style="flex:0 0 120px;font-size:11px;color:#9ca3af;">${[r.media1,r.media2,r.media3,r.media4].filter(Boolean).join(', ')||'—'}</div>
              <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.prismaSyncPath||'—'}</div>
              <button class="btn btn-sm psync-edit"
                data-id="${(r._id||'').replace(/"/g,'&quot;')}"
                data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}"
                data-engine="${(r.moteurImpression||'').replace(/"/g,'&quot;')}"
                data-media1="${(r.media1||'').replace(/"/g,'&quot;')}"
                data-media2="${(r.media2||'').replace(/"/g,'&quot;')}"
                data-media3="${(r.media3||'').replace(/"/g,'&quot;')}"
                data-media4="${(r.media4||'').replace(/"/g,'&quot;')}"
                data-path="${(r.prismaSyncPath||'').replace(/"/g,'&quot;')}">Modifier</button>
              <button class="btn btn-sm psync-delete" data-id="${(r._id||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`).join('')}
      </div>
    </div>

    <!-- Sous-section 2 : Routage PrismaPrepare -->
    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:24px;">
      <h4 style="margin:0 0 8px;">2. Routage PrismaPrepare</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Pour l'action <strong>Ouvrir dans PrismaPrepare</strong> : mapping Type de travail → Chemin hotfolder PrismaPrepare. Le PDF est déplacé dans la tuile <em>PrismaPrepare</em>.</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="pp-type" class="settings-input" style="min-width:200px;">
            <option value="">— Sélectionner —</option>${typeOptions}
          </select>
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Chemin hotfolder PrismaPrepare</label>
          <input type="text" id="pp-path" placeholder="Ex: C:\\Flux\\PrismaPrepare\\Brochures" class="settings-input" style="width:100%;" />
        </div>
        <button id="pp-save" class="btn btn-primary">Enregistrer</button>
      </div>
      <div id="pp-list">
        ${prismaPrepareRoutings.length === 0 ? '<p style="color:#9ca3af;">Aucun routage PrismaPrepare configuré</p>' :
          prismaPrepareRoutings.map(r => `
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;">
              <div style="flex:0 0 200px;"><strong style="font-size:13px;">${r.typeTravail}</strong></div>
              <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.hotfolderPath||'—'}</div>
              <button class="btn btn-sm pp-edit" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-path="${(r.hotfolderPath||'').replace(/"/g,'&quot;')}">Modifier</button>
              <button class="btn btn-sm pp-delete" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`).join('')}
      </div>
    </div>

    <!-- Sous-section 3 : Routage Impression directe -->
    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:24px;">
      <h4 style="margin:0 0 8px;">3. Routage Impression directe</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Pour l'action <strong>Impression directe</strong> : mapping Type de travail + Moteur d'impression → Hotfolder d'impression directe. Le PDF est déplacé dans la tuile <em>Impression en cours</em>.</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="dp-type" class="settings-input" style="min-width:180px;">
            <option value="">— Sélectionner —</option>${typeOptions}
          </select>
        </div>
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Moteur d'impression</label>
          <select id="dp-engine" class="settings-input" style="min-width:180px;">
            <option value="">— Tous —</option>${engineOptions}
          </select>
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Chemin hotfolder</label>
          <input type="text" id="dp-path" placeholder="Ex: C:\\Flux\\PrismaPrepare\\Direct\\Brochures" class="settings-input" style="width:100%;" />
        </div>
        <button id="dp-save" class="btn btn-primary">Enregistrer</button>
      </div>
      <div id="dp-list">
        ${directPrintRoutings.length === 0 ? '<p style="color:#9ca3af;">Aucun routage impression directe configuré</p>' :
          directPrintRoutings.map(r => `
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;">
              <div style="flex:0 0 180px;"><strong style="font-size:13px;">${r.typeTravail}</strong></div>
              <div style="flex:0 0 140px;font-size:12px;color:#6b7280;">${r.printEngine||'(tous)'}</div>
              <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.hotfolderPath||'—'}</div>
              <button class="btn btn-sm dp-edit" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-engine="${(r.printEngine||'').replace(/"/g,'&quot;')}" data-path="${(r.hotfolderPath||'').replace(/"/g,'&quot;')}">Modifier</button>
              <button class="btn btn-sm dp-delete" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-engine="${(r.printEngine||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`).join('')}
      </div>
    </div>

    <!-- Sous-section 4 : Routage Fiery -->
    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:24px;">
      <h4 style="margin:0 0 8px;">4. Routage Fiery</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Pour l'action <strong>Envoyer dans Fiery</strong> : mapping Type de travail → Hotfolder Fiery. Le PDF est déplacé dans la tuile <em>Fiery</em>.</p>
      <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-bottom:12px;">
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="fiery-type" class="settings-input" style="min-width:200px;">
            <option value="">— Sélectionner —</option>${typeOptions}
          </select>
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Chemin hotfolder Fiery</label>
          <input type="text" id="fiery-path" placeholder="Ex: C:\\Flux\\Fiery\\Brochures" class="settings-input" style="width:100%;" />
        </div>
        <button id="fiery-save" class="btn btn-primary">Enregistrer</button>
      </div>
      <div id="fiery-list">
        ${fieryRoutings.length === 0 ? '<p style="color:#9ca3af;">Aucun routage Fiery configuré</p>' :
          fieryRoutings.map(r => `
            <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;">
              <div style="flex:0 0 200px;"><strong style="font-size:13px;">${r.typeTravail}</strong></div>
              <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.hotfolderPath||'—'}</div>
              <button class="btn btn-sm fiery-edit" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-path="${(r.hotfolderPath||'').replace(/"/g,'&quot;')}">Modifier</button>
              <button class="btn btn-sm fiery-delete" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`).join('')}
      </div>
    </div>
  `;

  // PrismaSync save
  panel.querySelector("#psync-save").onclick = async () => {
    const typeTravail = panel.querySelector("#psync-type").value;
    const moteurImpression = panel.querySelector("#psync-engine").value;
    const media1 = panel.querySelector("#psync-media1").value.trim();
    const media2 = panel.querySelector("#psync-media2").value.trim();
    const media3 = panel.querySelector("#psync-media3").value.trim();
    const media4 = panel.querySelector("#psync-media4").value.trim();
    const prismaSyncPath = panel.querySelector("#psync-path").value.trim();
    const editId = panel.querySelector("#psync-edit-id").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!moteurImpression) { alert("Sélectionnez un moteur d'impression"); return; }
    if (!prismaSyncPath) { alert("Entrez un chemin contrôleur PrismaSync"); return; }
    const body = { typeTravail, moteurImpression, media1, media2, media3, media4, prismaSyncPath };
    if (editId) body._id = editId;
    const r = await fetch("/api/config/prismasync-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(body)
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Routage PrismaSync enregistré", "success");
      panel._loaded = false;
      await refreshPrintRoutingPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };
  panel.querySelectorAll(".psync-edit").forEach(btn => {
    btn.onclick = () => {
      panel.querySelector("#psync-type").value = btn.dataset.type;
      panel.querySelector("#psync-engine").value = btn.dataset.engine;
      panel.querySelector("#psync-media1").value = btn.dataset.media1 || "";
      panel.querySelector("#psync-media2").value = btn.dataset.media2 || "";
      panel.querySelector("#psync-media3").value = btn.dataset.media3 || "";
      panel.querySelector("#psync-media4").value = btn.dataset.media4 || "";
      panel.querySelector("#psync-path").value = btn.dataset.path;
      panel.querySelector("#psync-edit-id").value = btn.dataset.id || "";
    };
  });
  panel.querySelectorAll(".psync-delete").forEach(btn => {
    btn.onclick = async () => {
      if (!confirm("Supprimer ce routage PrismaSync ?")) return;
      const r = await fetch(`/api/config/prismasync-routing/${encodeURIComponent(btn.dataset.id)}`, {
        method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) { showNotification("Routage supprimé", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
      else alert("Erreur : " + (r.error || ""));
    };
  });

  // Direct print save
  panel.querySelector("#dp-save").onclick = async () => {
    const typeTravail = panel.querySelector("#dp-type").value;
    const printEngine = panel.querySelector("#dp-engine").value;
    const hotfolderPath = panel.querySelector("#dp-path").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!hotfolderPath) { alert("Entrez un chemin hotfolder"); return; }
    const r = await fetch("/api/config/direct-print-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ typeTravail, printEngine, hotfolderPath })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { showNotification("✅ Routage impression directe enregistré", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
    else alert("Erreur : " + (r.error || ""));
  };
  panel.querySelectorAll(".dp-edit").forEach(btn => {
    btn.onclick = () => {
      panel.querySelector("#dp-type").value = btn.dataset.type;
      panel.querySelector("#dp-engine").value = btn.dataset.engine;
      panel.querySelector("#dp-path").value = btn.dataset.path;
    };
  });
  panel.querySelectorAll(".dp-delete").forEach(btn => {
    btn.onclick = async () => {
      if (!confirm("Supprimer ce routage impression directe ?")) return;
      const r = await fetch(`/api/config/direct-print-routing?typeTravail=${encodeURIComponent(btn.dataset.type)}&printEngine=${encodeURIComponent(btn.dataset.engine)}`, {
        method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) { showNotification("Routage supprimé", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
      else alert("Erreur : " + (r.error || ""));
    };
  });

  // Fiery save
  panel.querySelector("#fiery-save").onclick = async () => {
    const typeTravail = panel.querySelector("#fiery-type").value;
    const hotfolderPath = panel.querySelector("#fiery-path").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!hotfolderPath) { alert("Entrez un chemin hotfolder Fiery"); return; }
    const r = await fetch("/api/config/fiery-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ typeTravail, hotfolderPath })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { showNotification("✅ Routage Fiery enregistré", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
    else alert("Erreur : " + (r.error || ""));
  };
  panel.querySelectorAll(".fiery-edit").forEach(btn => {
    btn.onclick = () => { panel.querySelector("#fiery-type").value = btn.dataset.type; panel.querySelector("#fiery-path").value = btn.dataset.path; };
  });
  panel.querySelectorAll(".fiery-delete").forEach(btn => {
    btn.onclick = async () => {
      if (!confirm(`Supprimer le routage Fiery pour "${btn.dataset.type}" ?`)) return;
      const r = await fetch(`/api/config/fiery-routing/${encodeURIComponent(btn.dataset.type)}`, {
        method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) { showNotification("Routage supprimé", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
      else alert("Erreur : " + (r.error || ""));
    };
  });

  // PrismaPrepare save
  panel.querySelector("#pp-save").onclick = async () => {
    const typeTravail = panel.querySelector("#pp-type").value;
    const hotfolderPath = panel.querySelector("#pp-path").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!hotfolderPath) { alert("Entrez un chemin hotfolder PrismaPrepare"); return; }
    const r = await fetch("/api/config/prisma-prepare-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ typeTravail, hotfolderPath })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) { showNotification("✅ Routage PrismaPrepare enregistré", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
    else alert("Erreur : " + (r.error || ""));
  };
  panel.querySelectorAll(".pp-edit").forEach(btn => {
    btn.onclick = () => { panel.querySelector("#pp-type").value = btn.dataset.type; panel.querySelector("#pp-path").value = btn.dataset.path; };
  });
  panel.querySelectorAll(".pp-delete").forEach(btn => {
    btn.onclick = async () => {
      if (!confirm(`Supprimer le routage PrismaPrepare pour "${btn.dataset.type}" ?`)) return;
      const r = await fetch(`/api/config/prisma-prepare-routing/${encodeURIComponent(btn.dataset.type)}`, {
        method: "DELETE", headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) { showNotification("Routage supprimé", "success"); panel._loaded = false; await refreshPrintRoutingPanel(panel); }
      else alert("Erreur : " + (r.error || ""));
    };
  });
}

export async function renderSettingsHotfolderRouting(panel) {
  panel.innerHTML = `<h3>Routage Hotfolder BAT PrismaPrepare</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshHotfolderRoutingPanel(panel);
}

export async function refreshHotfolderRoutingPanel(panel) {
  let routings = [];
  let types = [];
  try {
    const [r1, r2] = await Promise.all([
      fetch("/api/config/hotfolder-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => [])
    ]);
    routings = Array.isArray(r1) ? r1 : [];
    types = Array.isArray(r2) ? r2 : [];
  } catch(e) { /* use empty */ }

  const typeOptions = types.map(t => `<option value="${t.replace(/"/g,'&quot;')}">${t}</option>`).join("");

  panel.innerHTML = `
    <h3>Routage Hotfolder BAT PrismaPrepare</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">
      Configurez le chemin du hotfolder PrismaPrepare pour chaque type de travail.
      Quand un BAT Complet est lancé, le fichier est copié vers le hotfolder correspondant au type de travail de la fiche.
    </p>

    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:20px;">
      <h4 style="margin-top:0;">Ajouter / modifier un routage</h4>
      <div style="display: flex; gap: 8px; flex-wrap: wrap; align-items: flex-end;">
        <div>
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Type de travail</label>
          <select id="hfr-type" class="settings-input" style="min-width:200px;">
            <option value="">— Sélectionner —</option>
            ${typeOptions}
          </select>
        </div>
        <div style="flex:1;min-width:250px;">
          <label style="font-size:12px;color:#6b7280;display:block;margin-bottom:4px;">Chemin hotfolder PrismaPrepare</label>
          <input type="text" id="hfr-path" placeholder="Ex: C:\\Flux\\PrismaPrepare\\Brochures" class="settings-input" style="width:100%;" />
        </div>
        <button id="hfr-save" class="btn btn-primary">Enregistrer</button>
      </div>
    </div>

    <h4>Routages configurés</h4>
    <div id="hfr-list">
      ${routings.length === 0
        ? '<p style="color:#9ca3af;">Aucun routage configuré</p>'
        : routings.map(r => `
          <div style="display:flex;align-items:center;gap:10px;padding:10px 14px;background:white;border:1px solid #e5e7eb;border-radius:8px;margin-bottom:6px;">
            <div style="flex:0 0 200px;">
              <strong style="font-size:13px;color:#111827;">${r.typeTravail}</strong>
            </div>
            <div style="flex:1;font-size:12px;color:#6b7280;font-family:monospace;word-break:break-all;">${r.hotfolderPath || '—'}</div>
            <button class="btn btn-sm hfr-edit" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" data-path="${(r.hotfolderPath||'').replace(/"/g,'&quot;')}">Modifier</button>
            <button class="btn btn-sm hfr-delete" data-type="${(r.typeTravail||'').replace(/"/g,'&quot;')}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
          </div>
        `).join("")
      }
    </div>
  `;

  document.getElementById("hfr-save").onclick = async () => {
    const typeTravail = document.getElementById("hfr-type").value;
    const hotfolderPath = document.getElementById("hfr-path").value.trim();
    if (!typeTravail) { alert("Sélectionnez un type de travail"); return; }
    if (!hotfolderPath) { alert("Entrez un chemin hotfolder"); return; }
    const r = await fetch("/api/config/hotfolder-routing", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ typeTravail, hotfolderPath })
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Routage enregistré", "success");
      panel._loaded = false;
      await refreshHotfolderRoutingPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };

  panel.querySelectorAll(".hfr-edit").forEach(btn => {
    btn.onclick = () => {
      document.getElementById("hfr-type").value = btn.dataset.type;
      document.getElementById("hfr-path").value = btn.dataset.path;
    };
  });

  panel.querySelectorAll(".hfr-delete").forEach(btn => {
    btn.onclick = async () => {
      const typeTravail = btn.dataset.type;
      if (!confirm(`Supprimer le routage pour "${typeTravail}" ?`)) return;
      const r = await fetch(`/api/config/hotfolder-routing/${encodeURIComponent(typeTravail)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("Routage supprimé", "success");
        panel._loaded = false;
        await refreshHotfolderRoutingPanel(panel);
      } else { alert("Erreur : " + (r.error || "")); }
    };
  });
}
