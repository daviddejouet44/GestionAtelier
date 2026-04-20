// settings.js — Paramètres admin
import { authToken, currentUser, showNotification, esc } from './core.js';
import { renderSettingsAccounts, refreshSettingsUsersList } from './settings/settings-accounts.js';
import { renderSettingsSchedule, getFrenchPublicHolidays } from './settings/settings-schedule.js';
import { renderSettingsPaths } from './settings/settings-paths.js';
import { renderSettingsPreflight } from './settings/settings-preflight.js';
import { renderSettingsKanbanColumns } from './settings/settings-kanban.js';
import { renderSettingsBatConfig, renderSettingsIntegrations, renderSettingsBatCommand, renderSettingsActionButtons } from './settings/settings-bat.js';
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
        <button class="settings-tab" data-tab="imap-config">Import email (IMAP)</button>
        <button class="settings-tab" data-tab="email-templates">📧 Templates email</button>
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
      <div class="settings-panel hidden" id="settings-panel-imap-config"></div>
      <div class="settings-panel hidden" id="settings-panel-email-templates"></div>
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
    case "integrations":
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
    case "imap-config": await renderSettingsImapConfig(panelEl); break;
    case "email-templates": await renderSettingsEmailTemplates(panelEl); break;
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
