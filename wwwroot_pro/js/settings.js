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
import { renderSettingsFaconnage } from './settings/settings-faconnage.js';
import { renderSettingsLogo } from './settings/settings-logo.js';
import { renderSettingsLogs, loadSettingsLogs } from './settings/settings-logs.js';

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
        <button class="settings-tab" data-tab="kanban-columns">Tuiles Kanban</button>
        <button class="settings-tab" data-tab="bat-config">Configuration BAT</button>
        <button class="settings-tab" data-tab="print-engines">Moteurs d'impression</button>
        <button class="settings-tab" data-tab="work-types">Types de travail</button>
        <button class="settings-tab" data-tab="print-routing">Routage Impression</button>
        <button class="settings-tab" data-tab="fabrication-imports">Imports fiche</button>
        <button class="settings-tab" data-tab="faconnage">Façonnage</button>
        <button class="settings-tab" data-tab="logo">Logo</button>
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
      <div class="settings-panel hidden" id="settings-panel-logo"></div>
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
    case "faconnage": await renderSettingsFaconnage(panelEl); break;
    case "logo": await renderSettingsLogo(panelEl); break;
    case "logs": await renderSettingsLogs(panelEl); break;
  }
  panelEl._loaded = true;
}
