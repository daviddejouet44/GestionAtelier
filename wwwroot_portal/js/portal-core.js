// portal-core.js — Shared utilities for the client portal
// ============================================================

export const API_BASE = '/api/portal';
export let authToken = null;
export let currentClient = null;

// ============================================================
// AUTH
// ============================================================

export function getStoredToken() {
  return localStorage.getItem('portalToken');
}

export function storeToken(token) {
  localStorage.setItem('portalToken', token);
  authToken = token;
}

export function clearToken() {
  localStorage.removeItem('portalToken');
  localStorage.removeItem('portalClient');
  authToken = null;
  currentClient = null;
}

export function storeClient(client) {
  localStorage.setItem('portalClient', JSON.stringify(client));
  currentClient = client;
}

export function getStoredClient() {
  try {
    const raw = localStorage.getItem('portalClient');
    return raw ? JSON.parse(raw) : null;
  } catch { return null; }
}

/** Sends an authenticated request to the portal API */
export async function apiRequest(path, options = {}) {
  const token = authToken || getStoredToken();
  const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const resp = await fetch(`${API_BASE}${path}`, { ...options, headers });
  return resp.json();
}

/** Like apiRequest but uses FormData (no Content-Type override) */
export async function apiUpload(path, formData) {
  const token = authToken || getStoredToken();
  const headers = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  const resp = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers,
    body: formData
  });
  return resp.json();
}

/**
 * Validates the stored token against the server.
 * Returns the client object or null on failure.
 */
export async function validateSession() {
  const token = getStoredToken();
  if (!token) return null;
  authToken = token;
  try {
    const r = await apiRequest('/auth/me');
    if (r.ok && r.client) {
      currentClient = r.client;
      storeClient(r.client);
      return r.client;
    }
  } catch { /* network error */ }
  clearToken();
  return null;
}

/**
 * Requires the user to be logged in. If not, redirects to login.
 * Returns the client object.
 */
export async function requireAuth() {
  const client = await validateSession();
  if (!client) {
    window.location.href = '/portal/login.html';
    return null;
  }
  return client;
}

export function logout() {
  clearToken();
  fetch(`${API_BASE}/auth/logout`, { method: 'POST' }).catch(() => {});
  window.location.href = '/portal/login.html';
}

// ============================================================
// STATUS LABELS
// ============================================================
const STATUS_LABELS = {
  draft:       { label: 'Reçue',          badge: 'badge-gray' },
  received:    { label: 'En attente',      badge: 'badge-blue' },
  in_production: { label: 'En production', badge: 'badge-blue' },
  bat_pending: { label: 'BAT à valider',  badge: 'badge-yellow' },
  bat_refused: { label: 'BAT refusé',     badge: 'badge-red' },
  bat_validated:{ label: 'BAT validé',    badge: 'badge-green' },
  completed:   { label: 'Terminée',       badge: 'badge-green' },
  delivered:   { label: 'Livrée',         badge: 'badge-purple' },
};

export function statusBadge(status) {
  const s = STATUS_LABELS[status] || { label: status, badge: 'badge-gray' };
  return `<span class="badge ${s.badge}">${s.label}</span>`;
}

// ============================================================
// DATE FORMATTING
// ============================================================
export function fmtDate(isoStr) {
  if (!isoStr) return '—';
  const d = new Date(isoStr);
  return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

export function fmtDateTime(isoStr) {
  if (!isoStr) return '—';
  const d = new Date(isoStr);
  return d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' })
    + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
}

export function fmtSize(bytes) {
  if (!bytes) return '';
  if (bytes < 1024) return bytes + ' o';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + ' Ko';
  return (bytes / 1024 / 1024).toFixed(1) + ' Mo';
}

// ============================================================
// TOAST NOTIFICATIONS
// ============================================================
let toastContainer = null;

function getToastContainer() {
  if (!toastContainer) {
    toastContainer = document.createElement('div');
    toastContainer.className = 'toast-container';
    document.body.appendChild(toastContainer);
  }
  return toastContainer;
}

export function showToast(message, type = 'info') {
  const c = getToastContainer();
  const t = document.createElement('div');
  t.className = `toast ${type}`;
  t.textContent = message;
  c.appendChild(t);
  setTimeout(() => { t.style.opacity = '0'; t.style.transition = 'opacity .4s'; setTimeout(() => t.remove(), 400); }, 3500);
}

// ============================================================
// HEADER
// ============================================================
export function renderHeader(activePage) {
  const logoEl = document.getElementById('portal-logo');
  if (logoEl) {
    fetch('/api/logo').then(r => { if (r.ok) logoEl.src = '/api/logo'; }).catch(() => {});
  }

  const client = currentClient || getStoredClient();
  const nameEl = document.getElementById('portal-user-name');
  if (nameEl && client) nameEl.textContent = client.displayName || client.email || '';

  // Set active nav link
  document.querySelectorAll('.portal-nav a').forEach(a => {
    a.classList.toggle('active', a.dataset.page === activePage);
  });

  const btnLogout = document.getElementById('btn-portal-logout');
  if (btnLogout) btnLogout.onclick = () => logout();
}

// ============================================================
// ESC helper
// ============================================================
export function esc(str) {
  return (str || '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
