// settings.js — Paramètres admin
import { authToken, currentUser, showNotification, esc } from './core.js';
import { renderSettingsAccounts, refreshSettingsUsersList } from './settings/settings-accounts.js';
import { renderSettingsSchedule, getFrenchPublicHolidays } from './settings/settings-schedule.js';
import { renderSettingsPaths } from './settings/settings-paths.js';
import { renderSettingsPreflight } from './settings/settings-preflight.js';
import { renderSettingsKanbanColumns } from './settings/settings-kanban.js';
import { renderSettingsBatConfig, renderSettingsBatCommand, renderSettingsActionButtons } from './settings/settings-bat.js';
import { renderSettingsPrintEngines, refreshPrintEnginesPanel, extractEngineName } from './settings/settings-engines.js';
import { renderSettingsWorkTypes, refreshWorkTypesPanel } from './settings/settings-work-types.js';
import { renderSettingsPrintRouting, refreshPrintRoutingPanel, renderSettingsHotfolderRouting, refreshHotfolderRoutingPanel } from './settings/settings-routing.js';
import { renderSettingsFabricationImports } from './settings/settings-fabrication-imports.js';
import { renderSettingsFinitions } from './settings/settings-finitions.js';
import { renderSettingsReports } from './settings/settings-reports.js';
import { renderSettingsLogo } from './settings/settings-logo.js';
import { renderSettingsLogs, loadSettingsLogs } from './settings/settings-logs.js';
import { renderSettingsSheetFormats } from './settings/settings-sheet-formats.js';
import { renderSettingsCoverProducts, renderSettingsSheetCalcRules, renderSettingsDeliveryDelay, renderSettingsPassesConfig, renderSettingsGrammageTimeConfig, renderSettingsJdfConfig } from './settings/settings-production-config.js';
import { renderSettingsFormConfig } from './settings/settings-form-config.js';
import { renderSettingsEmailTemplates } from './settings/settings-email-templates.js';
import { renderSettingsIntegrations } from './settings/settings-integrations.js';
import { renderSettingsPortal } from './settings/settings-portal.js';

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
        <button class="settings-tab" data-tab="preflight">Preflight</button>
        <button class="settings-tab" data-tab="kanban-columns">Tuiles</button>
        <button class="settings-tab" data-tab="bat-config">Configuration BAT</button>
        <button class="settings-tab" data-tab="print-engines">Moteurs d'impression</button>
        <button class="settings-tab" data-tab="work-types">Types de travail</button>
        <button class="settings-tab" data-tab="print-routing">Routage Impression</button>
        <button class="settings-tab" data-tab="fabrication-imports">Import catalogues papiers</button>
        <button class="settings-tab" data-tab="faconnage">Finitions</button>
        <button class="settings-tab" data-tab="reports">Rapports</button>
        <button class="settings-tab" data-tab="sheet-formats">Formats feuille</button>
        <button class="settings-tab" data-tab="cover-products">Produit avec couverture</button>
        <button class="settings-tab" data-tab="sheet-calc-rules">Calcul feuilles</button>
        <button class="settings-tab" data-tab="delivery-delay">Dates clés</button>
        <button class="settings-tab" data-tab="passes-config">Passes</button>
        <button class="settings-tab" data-tab="grammage-time">Pallier grammage</button>
        <button class="settings-tab" data-tab="jdf-config">JDF</button>
        <button class="settings-tab" data-tab="form-config">Fiche de production</button>
        <button class="settings-tab" data-tab="logo">Logos/Images</button>
        <button class="settings-tab" data-tab="planning-colors">🎨 Couleurs Planning</button>
        <button class="settings-tab" data-tab="imap-config">Import email (IMAP)</button>
        <button class="settings-tab" data-tab="email-templates">📧 Templates email</button>
        <button class="settings-tab" data-tab="integrations">🔌 Intégrations</button>
        <button class="settings-tab" data-tab="portal">🌐 Portail client</button>
        <button class="settings-tab" data-tab="logs">Logs</button>
      </div>
      <div class="settings-panel" id="settings-panel-accounts"></div>
      <div class="settings-panel hidden" id="settings-panel-schedule"></div>
      <div class="settings-panel hidden" id="settings-panel-paths"></div>
      <div class="settings-panel hidden" id="settings-panel-preflight"></div>
      <div class="settings-panel hidden" id="settings-panel-kanban-columns"></div>
      <div class="settings-panel hidden" id="settings-panel-bat-config"></div>
      <div class="settings-panel hidden" id="settings-panel-print-engines"></div>
      <div class="settings-panel hidden" id="settings-panel-work-types"></div>
      <div class="settings-panel hidden" id="settings-panel-print-routing"></div>
      <div class="settings-panel hidden" id="settings-panel-fabrication-imports"></div>
      <div class="settings-panel hidden" id="settings-panel-faconnage"></div>
      <div class="settings-panel hidden" id="settings-panel-reports"></div>
      <div class="settings-panel hidden" id="settings-panel-sheet-formats"></div>
      <div class="settings-panel hidden" id="settings-panel-cover-products"></div>
      <div class="settings-panel hidden" id="settings-panel-sheet-calc-rules"></div>
      <div class="settings-panel hidden" id="settings-panel-delivery-delay"></div>
      <div class="settings-panel hidden" id="settings-panel-passes-config"></div>
      <div class="settings-panel hidden" id="settings-panel-grammage-time"></div>
      <div class="settings-panel hidden" id="settings-panel-jdf-config"></div>
      <div class="settings-panel hidden" id="settings-panel-form-config"></div>
      <div class="settings-panel hidden" id="settings-panel-logo"></div>
      <div class="settings-panel hidden" id="settings-panel-planning-colors"></div>
      <div class="settings-panel hidden" id="settings-panel-imap-config"></div>
      <div class="settings-panel hidden" id="settings-panel-email-templates"></div>
      <div class="settings-panel hidden" id="settings-panel-integrations"></div>
      <div class="settings-panel hidden" id="settings-panel-portal"></div>
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
    case "preflight": await renderSettingsPreflight(panelEl); break;
    case "kanban-columns": await renderSettingsKanbanColumns(panelEl); break;
    case "bat-config": await renderSettingsBatConfig(panelEl); break;
    case "print-engines": await renderSettingsPrintEngines(panelEl); break;
    case "work-types": await renderSettingsWorkTypes(panelEl); break;
    case "hotfolder-routing": await renderSettingsBatConfig(panelEl); break;
    case "print-routing": await renderSettingsPrintRouting(panelEl); break;
    case "fabrication-imports": await renderSettingsFabricationImports(panelEl); break;
    case "bat-command": await renderSettingsBatConfig(panelEl); break;
    case "faconnage": await renderSettingsFinitions(panelEl); break;
    case "reports": await renderSettingsReports(panelEl); break;
    case "sheet-formats": await renderSettingsSheetFormats(panelEl); break;
    case "cover-products": await renderSettingsCoverProducts(panelEl); break;
    case "sheet-calc-rules": await renderSettingsSheetCalcRules(panelEl); break;
    case "delivery-delay": await renderSettingsDeliveryDelay(panelEl); break;
    case "passes-config": await renderSettingsPassesConfig(panelEl); break;
    case "grammage-time": await renderSettingsGrammageTimeConfig(panelEl); break;
    case "jdf-config": await renderSettingsJdfConfig(panelEl); break;
    case "form-config": await renderSettingsFormConfig(panelEl); break;
    case "logo": await renderSettingsLogo(panelEl); break;
    case "planning-colors": await renderSettingsPlanningColors(panelEl); break;
    case "imap-config": await renderSettingsImapConfig(panelEl); break;
    case "email-templates": await renderSettingsEmailTemplates(panelEl); break;
    case "integrations": await renderSettingsIntegrations(panelEl); break;
    case "portal": await renderSettingsPortal(panelEl); break;
    case "logs": await renderSettingsLogs(panelEl); break;
  }
  panelEl._loaded = true;
}

async function renderSettingsImapConfig(panel) {
  let cfg = {};
  try {
    const r = await fetch('/api/settings/imap').then(res => res.json()).catch(() => ({}));
    if (r.ok && r.settings) cfg = r.settings;
  } catch(e) {}

  panel.innerHTML = `
    <h3>Import email (IMAP)</h3>
    <p style='color:#6b7280;font-size:13px;margin-bottom:20px;'>Configurez les paramètres IMAP pour pré-remplir le formulaire d'import depuis un mail dans la vue Soumission.</p>

    <div class='settings-section-card' style='background:#fef9c3;border:1px solid #fde047;border-radius:8px;padding:12px 16px;margin-bottom:16px;'>
      <h4 style='margin:0 0 8px;font-size:13px;color:#854d0e;'>⚠️ Gmail — Mot de passe d'application</h4>
      <p style='font-size:12px;color:#713f12;margin:0 0 6px;'>Google a progressivement désactivé l'accès IMAP par mot de passe d'application pour les nouveaux comptes. Si la connexion échoue, suivez ces étapes :</p>
      <ul style='font-size:12px;color:#713f12;margin:0;padding-left:18px;'>
        <li>Serveur : <code>imap.gmail.com</code>, Port : <code>993</code>, SSL activé</li>
        <li>Activer la vérification en 2 étapes sur votre compte Google, puis générer un <strong>mot de passe d'application</strong> dans <a href="https://myaccount.google.com/apppasswords" target="_blank" rel="noopener">Google App Passwords</a></li>
        <li>Utiliser le mot de passe d'application à 16 caractères comme mot de passe ici (saisissez-le <strong>sans espaces</strong>)</li>
        <li>Si cela ne fonctionne pas, Google peut bloquer l'accès IMAP — une intégration OAuth2 sera nécessaire dans une prochaine version.</li>
      </ul>
    </div>

    <div class='settings-section-card' style='background:#eff6ff;border:1px solid #93c5fd;border-radius:8px;padding:12px 16px;margin-bottom:16px;'>
      <h4 style='margin:0 0 8px;font-size:13px;color:#1e3a8a;'>ℹ️ Office 365 / Outlook</h4>
      <p style='font-size:12px;color:#1e40af;margin:0 0 6px;'>Pour tester avec Office 365 :</p>
      <ul style='font-size:12px;color:#1e40af;margin:0;padding-left:18px;'>
        <li>Serveur : <code>outlook.office365.com</code>, Port : <code>993</code>, SSL activé</li>
        <li>Un compte <strong>Microsoft 365 Developer</strong> (programme développeur Microsoft, gratuit) permet de tester avec IMAP</li>
        <li>Un compte <strong>Outlook.com gratuit</strong> peut ne pas autoriser l'accès IMAP/mot de passe d'application — vérifiez dans les paramètres du compte Outlook</li>
        <li>Si votre organisation utilise l'authentification moderne (OAuth2), le mot de passe classique ne fonctionnera pas</li>
      </ul>
    </div>

    <div class='settings-section-card'>
      <h4>Configuration serveur IMAP</h4>
      <div style='display:grid;grid-template-columns:1fr 100px;gap:12px;margin-bottom:12px;'>
        <div class='settings-form-group'><label>Serveur IMAP</label><input type='text' id='imap-cfg-host' value='${esc(cfg.host||"")}' class='settings-input settings-input-wide' placeholder='imap.gmail.com' /></div>
        <div class='settings-form-group'><label>Port</label><input type='number' id='imap-cfg-port' value='${cfg.port||993}' class='settings-input' style='width:90px;' /></div>
      </div>
      <div class='settings-form-group' style='margin-bottom:12px;'>
        <label>Email (compte par défaut)</label>
        <input type='email' id='imap-cfg-email' value='${esc(cfg.email||"")}' class='settings-input settings-input-wide' placeholder='votre@email.com' />
      </div>
      <div style='display:flex;align-items:center;gap:8px;margin-bottom:16px;'>
        <input type='checkbox' id='imap-cfg-ssl' ${cfg.useSsl!==false?'checked':''} />
        <label for='imap-cfg-ssl' style='font-size:13px;color:#374151;'>Connexion SSL/TLS</label>
      </div>
      <button id='imap-cfg-save' class='btn btn-primary'>Enregistrer la configuration IMAP</button>
    </div>
  `;

  panel.querySelector('#imap-cfg-save').onclick = async () => {
    const host = panel.querySelector('#imap-cfg-host').value.trim();
    const port = parseInt(panel.querySelector('#imap-cfg-port').value)||993;
    const email = panel.querySelector('#imap-cfg-email').value.trim();
    const useSsl = panel.querySelector('#imap-cfg-ssl').checked;
    const r = await fetch('/api/settings/imap', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
      body: JSON.stringify({ host, port, email, useSsl })
    }).then(res => res.json()).catch(() => ({ ok: false }));
    if (r.ok) showNotification('✅ Configuration IMAP enregistrée', 'success');
    else showNotification('❌ ' + (r.error||'Erreur'), 'error');
  };
}

async function renderSettingsPlanningColors(panel) {
  let engines = [];
  let currentColors = { engines: {}, finitions: {} };
  const finitionTypes = ['Embellissement','Rainage','Pliage','Façonnage','Coupe','Emballage','Départ','Livraison'];
  try {
    const [engResp, colResp] = await Promise.all([
      fetch('/api/config/print-engines').then(r => r.json()).catch(() => []),
      fetch('/api/settings/planning-colors').then(r => r.json()).catch(() => ({ ok: false, colors: {} }))
    ]);
    engines = Array.isArray(engResp) ? engResp.map(e => typeof e === 'object' ? (e.name || '') : String(e || '')).filter(Boolean) : [];
    if (colResp.ok && colResp.colors) currentColors = colResp.colors;
  } catch(e) { /* ignore */ }

  const defaultEngineColor = '#8b5cf6';
  const defaultFinitionColor = '#f59e0b';

  function colorRow(id, label, value, defaultColor) {
    return `<div style="display:flex;align-items:center;gap:12px;padding:8px 0;border-bottom:1px solid #f3f4f6;">
      <input type="color" id="${esc(id)}" value="${esc(value || defaultColor)}" style="width:40px;height:30px;padding:2px;cursor:pointer;border-radius:4px;border:1px solid #d1d5db;" />
      <label for="${esc(id)}" style="font-size:13px;color:#374151;flex:1;">${esc(label)}</label>
    </div>`;
  }

  panel.innerHTML = `<h3>Couleurs du planning</h3>
    <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">Personnalisez les couleurs affichées dans les plannings. La couleur verte "Terminé/Verrouillé" prime toujours sur ces couleurs.</p>
    <div class="settings-section-card">
      <h4>Couleurs par moteur d'impression</h4>
      ${engines.length === 0 ? '<p style="color:#9ca3af;font-size:13px;">Aucun moteur configur&#233;. Allez dans Moteurs d&#39;impression pour en ajouter.</p>' : ''}
      <div id="engine-colors-list">
        ${engines.map(e => colorRow('eng-color-' + e.replace(/\s+/g,'_'), e, (currentColors.engines||{})[e], defaultEngineColor)).join('')}
      </div>
    </div>
    <div class="settings-section-card" style="margin-top:16px;">
      <h4>Couleurs par type de finition</h4>
      <div id="finition-colors-list">
        ${finitionTypes.map(t => colorRow('fin-color-' + t.replace(/\s+/g,'_'), t, (currentColors.finitions||{})[t], defaultFinitionColor)).join('')}
      </div>
    </div>
    <div style="margin-top:16px;">
      <button id="planning-colors-save" class="btn btn-primary">💾 Enregistrer les couleurs</button>
      <span id="planning-colors-msg" style="margin-left:12px;font-size:13px;"></span>
    </div>`;

  panel.querySelector('#planning-colors-save').onclick = async () => {
    const msgEl = panel.querySelector('#planning-colors-msg');
    const enginesPayload = {};
    engines.forEach(e => {
      const input = panel.querySelector('#eng-color-' + e.replace(/\s+/g,'_'));
      if (input) enginesPayload[e] = input.value;
    });
    const finitionsPayload = {};
    finitionTypes.forEach(t => {
      const input = panel.querySelector('#fin-color-' + t.replace(/\s+/g,'_'));
      if (input) finitionsPayload[t] = input.value;
    });
    try {
      const r = await fetch('/api/settings/planning-colors', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'Authorization': `Bearer ${authToken}` },
        body: JSON.stringify({ engines: enginesPayload, finitions: finitionsPayload })
      }).then(res => res.json()).catch(() => ({ ok: false }));
      if (r.ok) { msgEl.style.color = '#16a34a'; msgEl.textContent = '✅ Couleurs enregistrées'; }
      else { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ ' + (r.error || 'Erreur'); }
    } catch(e) { msgEl.style.color = '#ef4444'; msgEl.textContent = '❌ Erreur réseau'; }
  };
}
