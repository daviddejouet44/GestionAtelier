// app/navigation.js — Fonctions de navigation pure (sans dépendances internes à app.js)

import { initDossiersView } from '../dossiers.js';
import { initSettingsView } from '../settings.js';
import { initGlobalProductionView } from '../production-view.js';

export function hideAllViews() {
  document.getElementById("kanban-layout").classList.add("hidden");
  document.getElementById("calendar").classList.add("hidden");
  document.getElementById("submission").classList.add("hidden");
  document.getElementById("production").classList.add("hidden");
  document.getElementById("recycle").classList.add("hidden");
  document.getElementById("dashboard").classList.add("hidden");
  document.getElementById("dossiers").classList.add("hidden");
  document.getElementById("settings-view").classList.add("hidden");
  document.getElementById("bat-view").classList.add("hidden");
  document.getElementById("rapport-view").classList.add("hidden");
  const globalProdEl = document.getElementById("global-production");
  if (globalProdEl) globalProdEl.classList.add("hidden");
  // Hide kanban-specific controls
  const filterBarEl = document.getElementById("kanban-filter-bar");
  if (filterBarEl) filterBarEl.style.display = "none";
  // Restore global-alert visibility (was hidden on kanban, show on other views)
  const globalAlert = document.getElementById("global-alert");
  if (globalAlert) globalAlert.style.display = "";
  // Hide planning-specific controls (only visible in Planning tab)
  const planSwitcher = document.getElementById("planning-view-switcher");
  if (planSwitcher) planSwitcher.style.display = "none";
  const planFinitions = document.getElementById("planning-finitions-view");
  if (planFinitions) planFinitions.style.display = "none";
  const planOperator = document.getElementById("planning-operator-view");
  if (planOperator) planOperator.style.display = "none";
  document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
}

export function showDossiers() {
  hideAllViews();
  document.getElementById("dossiers").classList.remove("hidden");
  document.getElementById("btnViewDossiers").classList.add("active");
  initDossiersView();
}

export function showSettings() {
  hideAllViews();
  document.getElementById("settings-view").classList.remove("hidden");
  initSettingsView();
}

export function showGlobalProduction() {
  hideAllViews();
  const el = document.getElementById("global-production");
  if (el) el.classList.remove("hidden");
  const btn = document.getElementById("btnViewGlobalProd");
  if (btn) btn.classList.add("active");
  initGlobalProductionView();
}
