// settings.js — Paramètres admin
import { authToken, currentUser, showNotification, esc } from './core.js';

// calendar and submissionCalendar are accessed via window globals set by app.js

// ======================================================
// NAVIGATION
// ======================================================
export function showSettings() {
  // Navigation handled by app.js
  initSettingsView();
}

export async function initSettingsView() {
  const settingsEl = document.getElementById("settings-view");
  settingsEl.innerHTML = `
    <div class="settings-container">
      <h2>Paramétrage</h2>
      <div class="settings-tabs">
        <button class="settings-tab active" data-tab="accounts">Comptes &amp; Rôles</button>
        <button class="settings-tab" data-tab="schedule">Plages horaires</button>
        <button class="settings-tab" data-tab="paths">Chemins d'accès</button>
        <button class="settings-tab" data-tab="integrations">Workflow BAT</button>
        <button class="settings-tab" data-tab="print-engines">Moteurs d'impression</button>
        <button class="settings-tab" data-tab="work-types">Types de travail</button>
        <button class="settings-tab" data-tab="hotfolder-routing">Routage Hotfolder BAT PrismaPrepare</button>
        <button class="settings-tab" data-tab="print-routing">Routage Impression</button>
        <button class="settings-tab" data-tab="fabrication-imports">Imports fiche</button>
        <button class="settings-tab" data-tab="bat-command">Commande BAT</button>
        <button class="settings-tab" data-tab="faconnage">Façonnage</button>
        <button class="settings-tab" data-tab="logs">Logs</button>
      </div>
      <div class="settings-panel" id="settings-panel-accounts"></div>
      <div class="settings-panel hidden" id="settings-panel-schedule"></div>
      <div class="settings-panel hidden" id="settings-panel-paths"></div>
      <div class="settings-panel hidden" id="settings-panel-integrations"></div>
      <div class="settings-panel hidden" id="settings-panel-print-engines"></div>
      <div class="settings-panel hidden" id="settings-panel-work-types"></div>
      <div class="settings-panel hidden" id="settings-panel-hotfolder-routing"></div>
      <div class="settings-panel hidden" id="settings-panel-print-routing"></div>
      <div class="settings-panel hidden" id="settings-panel-fabrication-imports"></div>
      <div class="settings-panel hidden" id="settings-panel-bat-command"></div>
      <div class="settings-panel hidden" id="settings-panel-faconnage"></div>
      <div class="settings-panel hidden" id="settings-panel-logs"></div>
    </div>
  `;

  settingsEl.querySelectorAll(".settings-tab").forEach(tab => {
    tab.onclick = () => {
      settingsEl.querySelectorAll(".settings-tab").forEach(t => t.classList.remove("active"));
      settingsEl.querySelectorAll(".settings-panel").forEach(p => p.classList.add("hidden"));
      tab.classList.add("active");
      const panel = settingsEl.querySelector(`#settings-panel-${tab.dataset.tab}`);
      if (panel) {
        panel.classList.remove("hidden");
        loadSettingsPanel(tab.dataset.tab, panel);
      }
    };
  });

  await loadSettingsPanel("accounts", settingsEl.querySelector("#settings-panel-accounts"));
}

export async function loadSettingsPanel(tabName, panelEl) {
  if (!panelEl) return;
  if (panelEl._loaded) return;
  switch (tabName) {
    case "accounts": await renderSettingsAccounts(panelEl); break;
    case "schedule": await renderSettingsSchedule(panelEl); break;
    case "paths": await renderSettingsPaths(panelEl); break;
    case "integrations": await renderSettingsIntegrations(panelEl); break;
    case "print-engines": await renderSettingsPrintEngines(panelEl); break;
    case "work-types": await renderSettingsWorkTypes(panelEl); break;
    case "hotfolder-routing": await renderSettingsHotfolderRouting(panelEl); break;
    case "print-routing": await renderSettingsPrintRouting(panelEl); break;
    case "fabrication-imports": await renderSettingsFabricationImports(panelEl); break;
    case "bat-command": await renderSettingsBatCommand(panelEl); break;
    case "faconnage": await renderSettingsFaconnage(panelEl); break;
    case "logs": await renderSettingsLogs(panelEl); break;
  }
  panelEl._loaded = true;
}

// ======================================================
// COMPTES & RÔLES
// ======================================================
async function renderSettingsAccounts(panel) {
  panel.innerHTML = `
    <h3>Gestion des comptes et des rôles</h3>
    <div class="accounts-new-user" style="margin-bottom: 20px;">
      <h4>Créer un nouveau compte</h4>
      <div style="display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 8px;">
        <input type="text" id="sa-login" placeholder="Login" class="settings-input" />
        <input type="password" id="sa-password" placeholder="Mot de passe" class="settings-input" />
        <input type="text" id="sa-name" placeholder="Nom complet" class="settings-input" />
        <select id="sa-profile" class="settings-input">
          <option value="1">Profil 1 — Soumission</option>
          <option value="2">Profil 2 — Opérateur</option>
          <option value="3">Profil 3 — Admin</option>
        </select>
        <button id="sa-create" class="btn btn-primary">Créer</button>
      </div>
      <div id="sa-error" style="color: #ef4444; font-size: 13px; display: none;"></div>
    </div>
    <h4>Comptes existants</h4>
    <div id="sa-users-list"></div>
  `;

  document.getElementById("sa-create").onclick = async () => {
    const login = document.getElementById("sa-login").value.trim();
    const password = document.getElementById("sa-password").value.trim();
    const name = document.getElementById("sa-name").value.trim();
    const profile = parseInt(document.getElementById("sa-profile").value);
    const errorEl = document.getElementById("sa-error");

    if (!login || !password || !name) {
      errorEl.textContent = "Tous les champs sont requis";
      errorEl.style.display = "block";
      return;
    }

    const r = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ login, password, name, profile })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      errorEl.style.display = "none";
      showNotification("✅ Compte créé", "success");
      document.getElementById("sa-login").value = "";
      document.getElementById("sa-password").value = "";
      document.getElementById("sa-name").value = "";
      await refreshSettingsUsersList();
    } else {
      errorEl.textContent = r.error || "Erreur";
      errorEl.style.display = "block";
    }
  };

  await refreshSettingsUsersList();
}

async function refreshSettingsUsersList() {
  const listEl = document.getElementById("sa-users-list");
  if (!listEl) return;
  const resp = await fetch("/api/auth/users", {
    headers: { "Authorization": `Bearer ${authToken}` }
  }).then(r => r.json());
  listEl.innerHTML = "";
  if (resp.ok && resp.users) {
    const profileLabel = { 1: "Soumission", 2: "Opérateur", 3: "Admin" };
    resp.users.forEach(u => {
      const div = document.createElement("div");
      div.style.cssText = "display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: white; border-radius: 6px; margin-bottom: 6px; border: 1px solid #e5e7eb;";
      div.innerHTML = `
        <div style="flex: 1;">
          <strong>${u.login}</strong> — ${u.name}
          <small style="display: block; color: #6b7280;">Profil ${u.profile} — ${profileLabel[u.profile] || u.profile}</small>
        </div>
        <button class="btn btn-sm" data-id="${u.id}" style="color: #ef4444; border-color: #ef4444;">Supprimer</button>
      `;
      div.querySelector("button").onclick = async () => {
        if (!confirm(`Supprimer l'utilisateur "${u.login}" ?`)) return;
        await fetch(`/api/auth/users/${u.id}`, {
          method: "DELETE",
          headers: { "Authorization": `Bearer ${authToken}` }
        });
        showNotification("✅ Utilisateur supprimé", "success");
        const panel = document.getElementById("settings-panel-accounts");
        if (panel) { panel._loaded = false; await renderSettingsAccounts(panel); }
      };
      listEl.appendChild(div);
    });
  }
}

// ======================================================
// PLAGES HORAIRES
// ======================================================
async function renderSettingsSchedule(panel) {
  panel.innerHTML = `<h3>Plages horaires et jours fériés</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { workStart: "08:00", workEnd: "18:00", holidays: [] };
  try {
    const resp = await fetch("/api/config/schedule", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  const holidays = Array.isArray(cfg.holidays) ? cfg.holidays : [];

  panel.innerHTML = `
    <h3>Plages horaires et jours fériés</h3>
    <div class="settings-form-group">
      <label>Début journée</label>
      <input type="time" id="sch-start" value="${cfg.workStart || '08:00'}" class="settings-input" />
    </div>
    <div class="settings-form-group">
      <label>Fin journée</label>
      <input type="time" id="sch-end" value="${cfg.workEnd || '18:00'}" class="settings-input" />
    </div>
    <button id="sch-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les plages</button>
    <hr style="margin: 20px 0;" />
    <h4>Jours fériés</h4>
    <div style="display: flex; gap: 8px; margin-bottom: 10px; flex-wrap: wrap;">
      <input type="date" id="sch-holiday-date" class="settings-input" />
      <button id="sch-add-holiday" class="btn btn-primary">Ajouter</button>
      <button id="sch-add-french-holidays" class="btn">Ajouter jours fériés français</button>
    </div>
    <div id="sch-holidays-list">
      ${holidays.length === 0 ? '<p style="color:#9ca3af;">Aucun jour férié configuré</p>' : holidays.map(h => `
        <div style="display: flex; align-items: center; gap: 10px; padding: 6px 10px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 4px;">
          <span style="flex:1;">${new Date(h + "T00:00:00").toLocaleDateString("fr-FR", { weekday: "long", day: "2-digit", month: "long", year: "numeric" })}</span>
          <button class="btn btn-sm" data-date="${h}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
        </div>
      `).join("")}
    </div>
  `;

  document.getElementById("sch-save").onclick = async () => {
    const workStart = document.getElementById("sch-start").value;
    const workEnd = document.getElementById("sch-end").value;
    const r = await fetch("/api/config/schedule", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ workStart, workEnd })
    }).then(r => r.json());
    if (r.ok) {
      const [h, m] = workEnd.split(":").map(Number);
      const bufferedEnd = `${String(Math.min(h + 1, 24)).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
      if (window._calendar) {
        window._calendar.setOption("slotMinTime", workStart);
        window._calendar.setOption("slotMaxTime", bufferedEnd);
      }
      if (window._submissionCalendar) {
        window._submissionCalendar.setOption("slotMinTime", workStart);
        window._submissionCalendar.setOption("slotMaxTime", bufferedEnd);
      }
      showNotification("✅ Plages horaires enregistrées", "success");
    } else alert("Erreur : " + r.error);
  };

  document.getElementById("sch-add-holiday").onclick = async () => {
    const dateVal = document.getElementById("sch-holiday-date").value;
    if (!dateVal) { alert("Sélectionnez une date"); return; }
    const r = await fetch("/api/config/schedule/holidays", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ date: dateVal })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("✅ Jour férié ajouté", "success");
      panel._loaded = false;
      await renderSettingsSchedule(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("sch-add-french-holidays").onclick = async () => {
    const year = new Date().getFullYear();
    const frenchHolidays = getFrenchPublicHolidays(year);
    let added = 0;
    for (const date of frenchHolidays) {
      const r = await fetch("/api/config/schedule/holidays", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ date })
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) added++;
    }
    showNotification(`✅ ${added} jours fériés français ajoutés pour ${year}`, "success");
    panel._loaded = false;
    await renderSettingsSchedule(panel);
  };

  document.querySelectorAll("#sch-holidays-list button[data-date]").forEach(btn => {
    btn.onclick = async () => {
      const dateToRemove = btn.dataset.date;
      const r = await fetch(`/api/config/schedule/holidays?date=${encodeURIComponent(dateToRemove)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("✅ Jour férié supprimé", "success");
        panel._loaded = false;
        await renderSettingsSchedule(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}

function getFrenchPublicHolidays(year) {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31);
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  const easter = new Date(year, month - 1, day);

  function addDays(d, n) {
    const r = new Date(d);
    r.setDate(r.getDate() + n);
    return r.toISOString().split("T")[0];
  }
  function fmt(y, m, d) {
    return `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
  }

  return [
    fmt(year, 1, 1),
    addDays(easter, 1),
    fmt(year, 5, 1),
    fmt(year, 5, 8),
    addDays(easter, 39),
    addDays(easter, 50),
    fmt(year, 7, 14),
    fmt(year, 8, 15),
    fmt(year, 11, 1),
    fmt(year, 11, 11),
    fmt(year, 12, 25)
  ];
}

// ======================================================
// CHEMINS D'ACCÈS
// ======================================================
async function renderSettingsPaths(panel) {
  panel.innerHTML = `<h3>Chemins d'accès aux dossiers</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { hotfoldersRoot: "C:\\Flux", recycleBinPath: "" };
  try {
    const resp = await fetch("/api/config/paths", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Chemins d'accès aux dossiers</h3>
    <div class="settings-form-group">
      <label>Racine des hotfolders (GA_HOTFOLDERS_ROOT)</label>
      <input type="text" id="paths-hotfolders" value="${cfg.hotfoldersRoot || 'C:\\\\Flux'}" class="settings-input" style="width: 100%; max-width: 500px;" />
    </div>
    <div class="settings-form-group">
      <label>Chemin corbeille</label>
      <input type="text" id="paths-recycle" value="${cfg.recycleBinPath || ''}" class="settings-input" style="width: 100%; max-width: 500px;" placeholder="Ex: C:\\Corbeille" />
    </div>
    <button id="paths-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("paths-save").onclick = async () => {
    const hotfoldersRoot = document.getElementById("paths-hotfolders").value.trim();
    const recycleBinPath = document.getElementById("paths-recycle").value.trim();
    const r = await fetch("/api/config/paths", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ hotfoldersRoot, recycleBinPath })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}

// ======================================================
// WORKFLOW BAT
// ======================================================
async function renderSettingsIntegrations(panel) {
  panel.innerHTML = `<h3>Workflow BAT</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { tempCopyPath: "" };
  try {
    const resp = await fetch("/api/config/integrations", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Workflow BAT</h3>

    <div style="border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 20px; background: #f9fafb;">
      <h4 style="margin-top: 0; margin-bottom: 12px;">Dossier temporaire TEMP_COPY</h4>
      <p style="font-size:12px;color:#6b7280;margin-bottom:8px;">Dossier dans lequel PrismaPrepare dépose le fichier <code>Epreuve.pdf</code>. Le backend surveille ce dossier et renomme automatiquement le fichier en <code>BAT_nom.pdf</code> avant de le déplacer dans la tuile BAT.</p>
      <div class="settings-form-group">
        <label>Chemin TEMP_COPY</label>
        <input type="text" id="int-temp-copy" value="${(cfg.tempCopyPath || '').replace(/"/g,'&quot;')}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\FluxAtelier\\Base\\TEMP_COPY" />
      </div>
    </div>

    <button id="int-save" class="btn btn-primary">Enregistrer</button>
  `;

  document.getElementById("int-save").onclick = async () => {
    const tempCopyPath = document.getElementById("int-temp-copy").value.trim();
    const r = await fetch("/api/config/integrations", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ tempCopyPath })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Configuration Workflow BAT enregistrée", "success");
    else alert("Erreur : " + (r.error || ""));
  };
}

// ======================================================
// MOTEURS D'IMPRESSION
// ======================================================
async function renderSettingsPrintEngines(panel) {
  panel.innerHTML = `<h3>Moteurs d'impression</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshPrintEnginesPanel(panel);
}

function extractEngineName(e) {
  return (typeof e === "object" && e !== null) ? (e.name || "") : String(e || "");
}

async function refreshPrintEnginesPanel(panel) {
  let engines = [];
  try {
    const resp = await fetch("/api/config/print-engines").then(r => r.json());
    engines = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Moteurs d'impression</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des moteurs d'impression disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="pe-new-name" placeholder="Nom du moteur" class="settings-input" style="max-width:200px;" />
      <input type="text" id="pe-new-ip" placeholder="IP / URL (optionnel)" class="settings-input" style="max-width:250px;" />
      <button id="pe-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        Importer CSV
        <input type="file" id="pe-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="pe-list">
      ${engines.length === 0
        ? '<p style="color:#9ca3af;">Aucun moteur configuré</p>'
        : engines.map(e => {
            const name = extractEngineName(e);
            const safeName = name.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `
          <div style="display: flex; align-items: center; gap: 10px; padding: 8px 12px; background: white; border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 6px;">
            <span style="flex: 1; font-size: 13px;">${name}</span>
            <button class="btn btn-sm pe-delete" data-name="${safeName}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
          </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("pe-add").onclick = async () => {
    const name = document.getElementById("pe-new-name").value.trim();
    const ip   = document.getElementById("pe-new-ip").value.trim();
    if (!name) { alert("Entrez un nom"); return; }
    const r = await fetch("/api/config/print-engines", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ name, ip })
    }).then(r => r.json());
    if (r.ok) {
      showNotification("Moteur ajouté", "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
  };

  document.getElementById("pe-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const text = await file.text();
    const lines = text.split(/[\r\n]+/).filter(Boolean);
    const enginesList = [];
    for (const line of lines) {
      const parts = line.split(";");
      const name = (parts[0] || "").trim();
      const ip   = (parts[1] || "").trim();
      const knownHeaders = ["presse", "nom", "name", "moteur", "engine"];
      if (!name || knownHeaders.includes(name.toLowerCase())) continue;
      enginesList.push({ name, ip });
    }
    if (enginesList.length === 0) { alert("Aucun moteur trouvé dans le fichier"); return; }
    const r = await fetch("/api/config/print-engines/import", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ engines: enginesList })
    }).then(r => r.json());
    if (r.ok) {
      showNotification(`${r.count || enginesList.length} moteurs importés`, "success");
      panel._loaded = false;
      await refreshPrintEnginesPanel(panel);
    } else { alert("Erreur : " + r.error); }
    e.target.value = "";
  };

  panel.querySelectorAll(".pe-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/print-engines/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json());
      if (r.ok) {
        showNotification("Moteur supprimé", "success");
        panel._loaded = false;
        await refreshPrintEnginesPanel(panel);
      } else { alert("Erreur : " + r.error); }
    };
  });
}

// ======================================================
// TYPES DE TRAVAIL
// ======================================================
async function renderSettingsWorkTypes(panel) {
  panel.innerHTML = `<h3>Types de travail</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshWorkTypesPanel(panel);
}

async function refreshWorkTypesPanel(panel) {
  let types = [];
  try {
    const resp = await fetch("/api/config/work-types").then(r => r.json());
    types = Array.isArray(resp) ? resp : [];
  } catch(e) { /* use empty */ }

  panel.innerHTML = `
    <h3>Types de travail</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Gérez la liste des types de travail disponibles dans la fiche de fabrication.</p>

    <div style="display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; align-items: center;">
      <input type="text" id="wt-new-name" placeholder="Nouveau type" class="settings-input" style="max-width:300px;" />
      <button id="wt-add" class="btn btn-primary">Ajouter</button>
      <label style="cursor:pointer; background:#f3f4f6; border:1px solid #e5e7eb; padding:6px 14px; border-radius:6px; font-size:13px;">
        📥 Importer CSV
        <input type="file" id="wt-csv-input" accept=".csv,.txt" style="display:none;" />
      </label>
    </div>

    <div id="wt-list">
      ${types.length === 0
        ? '<p style="color:#9ca3af;">Aucun type configuré</p>'
        : types.map(t => {
            const escapedType = t.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
            return `<div style="display:flex;align-items:center;gap:10px;padding:8px 12px;background:white;border:1px solid #e5e7eb;border-radius:6px;margin-bottom:6px;">
              <span style="flex:1;font-size:13px;">${t}</span>
              <button class="btn btn-sm wt-delete" data-name="${escapedType}" style="color:#ef4444;border-color:#ef4444;">Supprimer</button>
            </div>`;
          }).join("")
      }
    </div>
  `;

  document.getElementById("wt-add").onclick = async () => {
    const name = document.getElementById("wt-new-name").value.trim();
    if (!name) { alert("Entrez un type de travail"); return; }
    const formData = new FormData();
    const blob = new Blob([name], { type: "text/plain" });
    formData.append("file", blob, "type.csv");
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification("✅ Type ajouté", "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
  };

  document.getElementById("wt-csv-input").onchange = async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    const formData = new FormData();
    formData.append("file", file);
    const r = await fetch("/api/config/work-types/import", {
      method: "POST",
      body: formData
    }).then(r => r.json()).catch(() => ({ ok: false }));
    if (r.ok) {
      showNotification(`✅ ${r.count || 0} type(s) importé(s)`, "success");
      panel._loaded = false;
      await refreshWorkTypesPanel(panel);
    } else { alert("Erreur : " + (r.error || "")); }
    e.target.value = "";
  };

  panel.querySelectorAll(".wt-delete").forEach(btn => {
    btn.onclick = async () => {
      const name = btn.dataset.name;
      if (!confirm(`Supprimer "${name}" ?`)) return;
      const r = await fetch(`/api/config/work-types/${encodeURIComponent(name)}`, {
        method: "DELETE",
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        showNotification("Type supprimé", "success");
        panel._loaded = false;
        await refreshWorkTypesPanel(panel);
      } else { alert("Erreur : " + (r.error || "")); }
    };
  });
}

// ======================================================
// ROUTAGE HOTFOLDER (NOUVEAU)
// ======================================================
async function renderSettingsHotfolderRouting(panel) {
  panel.innerHTML = `<h3>Routage Hotfolder BAT PrismaPrepare</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshHotfolderRoutingPanel(panel);
}

async function refreshHotfolderRoutingPanel(panel) {
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

// ======================================================
// ROUTAGE IMPRESSION (Actions du bouton "Actions" dans la tuile "En attente")
// ======================================================
async function renderSettingsPrintRouting(panel) {
  panel.innerHTML = `<h3>Routage Impression</h3><p style="color:#6b7280;">Chargement...</p>`;
  await refreshPrintRoutingPanel(panel);
}

async function refreshPrintRoutingPanel(panel) {
  let fieryRoutings = [], prismaSyncRoutings = [], directPrintRoutings = [], types = [], engines = [];
  try {
    const [r1, r2, r3, r4, r5] = await Promise.all([
      fetch("/api/config/fiery-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/prismasync-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/direct-print-routing").then(r => r.json()).catch(() => []),
      fetch("/api/config/work-types").then(r => r.json()).catch(() => []),
      fetch("/api/config/print-engines").then(r => r.json()).catch(() => [])
    ]);
    fieryRoutings = Array.isArray(r1) ? r1 : [];
    prismaSyncRoutings = Array.isArray(r2) ? r2 : [];
    directPrintRoutings = Array.isArray(r3) ? r3 : [];
    types = Array.isArray(r4) ? r4 : [];
    engines = Array.isArray(r5) ? r5 : (r5.engines || []);
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

    <!-- Sous-section 2 : Chemin PrismaPrepare -->
    <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:10px;padding:20px;margin-bottom:24px;">
      <h4 style="margin:0 0 8px;">2. Chemin PrismaPrepare</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:8px;">Pour l'action <strong>Ouvrir dans PrismaPrepare</strong> : le routage utilise le Type de travail de la fiche pour trouver le hotfolder PrismaPrepare (même table que le routage BAT PrismaPrepare). Le PDF est déplacé dans la tuile <em>PrismaPrepare</em>.</p>
      <p style="font-size:13px;color:#374151;background:#dbeafe;border:1px solid #bfdbfe;border-radius:6px;padding:10px;">ℹ️ Configurez les chemins hotfolder PrismaPrepare par type de travail dans l'onglet <strong>Routage Hotfolder BAT PrismaPrepare</strong>.</p>
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
}

// ======================================================
// IMPORTS FICHE DE FABRICATION
// ======================================================
async function renderSettingsFabricationImports(panel) {
  panel.innerHTML = `<h3>Gestion des imports — Fiche de fabrication</h3><p style="color:#6b7280;">Chargement...</p>`;
  let cfg = { media1Path: "", media2Path: "", media3Path: "", media4Path: "", typeDocumentPath: "" };
  try {
    const resp = await fetch("/api/config/fabrication-imports", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
    if (resp.ok && resp.config) cfg = resp.config;
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Gestion des imports — Fiche de fabrication</h3>
    <p style="color:#6b7280; margin-bottom: 16px;">Configurez les chemins vers les fichiers XML utilisés pour les imports automatiques dans la fiche de fabrication.</p>
    <div class="settings-form-group"><label>Chemin Média 1 (XML)</label><input type="text" id="fi-media1" value="${cfg.media1Path || ''}" class="settings-input" style="width:100%;max-width:500px;" placeholder="Ex: C:\\Flux\\media1.xml" /></div>
    <div class="settings-form-group"><label>Chemin Média 2 (XML)</label><input type="text" id="fi-media2" value="${cfg.media2Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 3 (XML)</label><input type="text" id="fi-media3" value="${cfg.media3Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Média 4 (XML)</label><input type="text" id="fi-media4" value="${cfg.media4Path || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <div class="settings-form-group"><label>Chemin Type de document</label><input type="text" id="fi-typedoc" value="${cfg.typeDocumentPath || ''}" class="settings-input" style="width:100%;max-width:500px;" /></div>
    <button id="fi-save" class="btn btn-primary" style="margin-top: 10px;">Enregistrer les chemins</button>
  `;

  document.getElementById("fi-save").onclick = async () => {
    const r = await fetch("/api/config/fabrication-imports", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({
        media1Path: document.getElementById("fi-media1").value.trim(),
        media2Path: document.getElementById("fi-media2").value.trim(),
        media3Path: document.getElementById("fi-media3").value.trim(),
        media4Path: document.getElementById("fi-media4").value.trim(),
        typeDocumentPath: document.getElementById("fi-typedoc").value.trim()
      })
    }).then(r => r.json());
    if (r.ok) showNotification("✅ Chemins d'import enregistrés", "success");
    else alert("Erreur : " + r.error);
  };
}

// ======================================================
// COMMANDE BAT
// ======================================================
async function renderSettingsBatCommand(panel) {
  let cmd = "";
  let alertDelayHours = 48;
  try {
    const r = await fetch("/api/config/bat-command").then(r => r.json());
    if (r.ok) { cmd = r.command || ""; alertDelayHours = r.batAlertDelayHours ?? 48; }
  } catch(e) { /* use default */ }
  panel.innerHTML = `
    <h3>Commande BAT</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:12px;">Utilisez <code>{filePath}</code>, <code>{type}</code> et <code>{qty}</code> comme variables.</p>
    <div class="settings-form-group">
      <label>Commande</label>
      <input type="text" id="bat-cmd-input" value="${(cmd || '').replace(/"/g,'&quot;')}" class="settings-input" style="width:100%;max-width:600px;" />
    </div>
    <div class="settings-form-group" style="margin-top:16px;">
      <label>Délai alerte BAT sans réponse (heures)</label>
      <input type="number" id="bat-alert-delay-input" value="${alertDelayHours}" min="1" class="settings-input" style="width:120px;" />
      <p style="color:#6b7280;font-size:12px;margin-top:4px;">Affiche une alerte dans le bandeau si un BAT reste sans validation/refus après ce délai. Défaut : 48h.</p>
    </div>
    <button id="bat-cmd-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("bat-cmd-save").onclick = async () => {
    const command = document.getElementById("bat-cmd-input").value;
    const rawDelay = parseInt(document.getElementById("bat-alert-delay-input").value);
    const batAlertDelayHours = (rawDelay > 0) ? rawDelay : 48;
    const r = await fetch("/api/config/bat-command", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ command, batAlertDelayHours })
    }).then(r => r.json());
    if (r.ok) showNotification("Commande BAT enregistrée", "success");
    else alert("Erreur");
  };
}

// ======================================================
// BOUTONS D'ACTION
// ======================================================
async function renderSettingsActionButtons(panel) {
  let buttons = {};
  try {
    const r = await fetch("/api/config/action-buttons").then(r => r.json());
    if (r.ok) buttons = r.buttons || {};
  } catch(e) { /* use defaults */ }

  panel.innerHTML = `
    <h3>Boutons d'action</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Configurez les chemins d'exécutables pour chaque bouton d'action.</p>
    <div class="settings-form-group"><label>Contrôleur</label><input type="text" id="ab-controller" value="${esc(buttons.controller)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>PrismaPrepare</label><input type="text" id="ab-prisma" value="${esc(buttons.prismaPrepare)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Impression</label><input type="text" id="ab-print" value="${esc(buttons.print)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Modification</label><input type="text" id="ab-modification" value="${esc(buttons.modification)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <div class="settings-form-group"><label>Fiery (hotfolder)</label><input type="text" id="ab-fiery" value="${esc(buttons.fiery)}" class="settings-input" style="width:100%;max-width:600px;" /></div>
    <button id="ab-save" class="btn btn-primary" style="margin-top:10px;">Enregistrer</button>
  `;
  document.getElementById("ab-save").onclick = async () => {
    const data = {
      buttons: {
        controller: document.getElementById("ab-controller").value,
        prismaPrepare: document.getElementById("ab-prisma").value,
        print: document.getElementById("ab-print").value,
        modification: document.getElementById("ab-modification").value,
        fiery: document.getElementById("ab-fiery").value
      }
    };
    const r = await fetch("/api/config/action-buttons", {
      method: "PUT",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify(data)
    }).then(r => r.json());
    if (r.ok) showNotification("Boutons d'action enregistrés", "success");
    else alert("Erreur");
  };
}

// ======================================================
// LOGS
// ======================================================
async function renderSettingsLogs(panel) {
  panel.innerHTML = `
    <h3>Journaux d'activité utilisateurs</h3>
    <div style="display: flex; gap: 8px; margin-bottom: 16px; align-items: center;">
      <input type="date" id="logs-date-filter" class="settings-input" />
      <button id="logs-refresh" class="btn btn-primary">Rafraîchir</button>
    </div>
    <div id="logs-table-container"><p style="color:#9ca3af;">Chargement...</p></div>
  `;

  document.getElementById("logs-refresh").onclick = () => loadSettingsLogs();
  await loadSettingsLogs();
}

async function loadSettingsLogs() {
  const container = document.getElementById("logs-table-container");
  if (!container) return;

  const dateFilter = document.getElementById("logs-date-filter")?.value || "";
  const url = "/api/admin/activity-logs" + (dateFilter ? `?date=${encodeURIComponent(dateFilter)}` : "");

  try {
    const resp = await fetch(url, {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());

    const logs = resp.logs || [];
    if (logs.length === 0) {
      container.innerHTML = '<p style="color:#9ca3af;">Aucune activité enregistrée</p>';
      return;
    }

    container.innerHTML = `
      <table style="width:100%; border-collapse: collapse; font-size: 13px;">
        <thead>
          <tr style="background: #f3f4f6; text-align: left;">
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Date</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Utilisateur</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Action</th>
            <th style="padding: 8px 12px; border-bottom: 2px solid #e5e7eb;">Détails</th>
          </tr>
        </thead>
        <tbody>
          ${logs.map(l => `
            <tr style="border-bottom: 1px solid #e5e7eb;">
              <td style="padding: 6px 12px; white-space: nowrap; color: #6b7280;">${new Date(l.timestamp).toLocaleString("fr-FR")}</td>
              <td style="padding: 6px 12px; font-weight: 600;">${l.userName || l.userLogin || "—"}</td>
              <td style="padding: 6px 12px;"><span style="background: #dbeafe; color: #1e40af; padding: 2px 6px; border-radius: 4px; font-size: 11px; font-weight: 600;">${l.action || ""}</span></td>
              <td style="padding: 6px 12px; color: #374151;">${l.details || ""}</td>
            </tr>
          `).join("")}
        </tbody>
      </table>
    `;
  } catch (err) {
    container.innerHTML = `<p style="color:#ef4444;">Erreur : ${err.message}</p>`;
  }
}

// ======================================================
// FAÇONNAGE — Import CSV
// ======================================================
async function renderSettingsFaconnage(panel) {
  panel.innerHTML = `<h3>Options de façonnage</h3><p style="color:#6b7280;">Chargement...</p>`;

  let options = [];
  try {
    options = await fetch("/api/settings/faconnage-options", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []);
  } catch(e) { /* ignore */ }

  const listHtml = options.length === 0
    ? '<span style="color:#9ca3af;font-size:13px;">Aucune option définie</span>'
    : options.map(o => `<span style="display:inline-block;background:#f3f4f6;color:#374151;padding:4px 10px;border-radius:6px;font-size:13px;margin:3px;">${esc(o)}</span>`).join("");

  panel.innerHTML = `
    <h3>Options de façonnage</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
      Importez un fichier CSV contenant une option par ligne (ou par colonne). Les options existantes seront remplacées.
    </p>
    <div class="settings-form-group">
      <label>Importer un CSV</label>
      <input type="file" id="faconnage-csv-input" accept=".csv,.txt" class="settings-input" />
      <button id="faconnage-csv-import" class="btn btn-primary" style="margin-top:8px;">Importer</button>
      <div id="faconnage-import-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
    <div style="margin-top:20px;">
      <h4 style="margin-bottom:10px;">Options actuelles (${options.length})</h4>
      <div id="faconnage-options-list">${listHtml}</div>
    </div>
  `;

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
        await renderSettingsFaconnage(panel);
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
