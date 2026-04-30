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

// ============================================================
// PORTAL THEME
// ============================================================

/**
 * Loads the portal theme config from the server and injects CSS variables + layout customizations.
 * Call once per page at startup (non-blocking).
 */
export async function applyPortalTheme() {
  try {
    const r = await fetch('/api/portal/config/theme').then(r => r.json()).catch(() => null);
    if (!r || !r.ok || !r.theme) return;
    const t = r.theme;

    // Inject CSS variables into :root
    const styleId = 'portal-theme-vars';
    let style = document.getElementById(styleId);
    if (!style) { style = document.createElement('style'); style.id = styleId; document.head.appendChild(style); }

    const vars = [
      t.primaryColor     ? `--color-primary: ${t.primaryColor};`          : '',
      t.primaryDarkColor ? `--color-primary-dark: ${t.primaryDarkColor};` : '',
      t.primaryLightColor? `--color-primary-light: ${t.primaryLightColor};`: '',
      t.backgroundColor  ? `--color-gray-50: ${t.backgroundColor};`       : '',
      t.textColor        ? `--color-gray-700: ${t.textColor};`             : '',
      t.fontFamily       ? `--portal-font: ${t.fontFamily};`               : '',
    ].filter(Boolean).join('\n  ');

    style.textContent = vars ? `:root {\n  ${vars}\n}` : '';

    // Apply custom font
    if (t.fontFamily) {
      document.body.style.fontFamily = t.fontFamily;
      // Load Google Fonts if needed
      const knownGoogleFonts = ['Inter', 'Roboto', 'Open Sans', 'Lato'];
      const fontName = t.fontFamily.replace(/['"]/g, '').split(',')[0].trim();
      if (knownGoogleFonts.includes(fontName) && !document.querySelector(`link[data-font="${fontName}"]`)) {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.dataset.font = fontName;
        link.href = `https://fonts.googleapis.com/css2?family=${encodeURIComponent(fontName)}:wght@400;500;600;700&display=swap`;
        document.head.appendChild(link);
      }
    }

    // Apply company name in header
    if (t.companyName) {
      const h1 = document.querySelector('.portal-header-brand h1');
      if (h1) h1.textContent = t.companyName;
      const loginH2 = document.querySelector('.portal-login-logo h2');
      if (loginH2) loginH2.textContent = t.companyName;
    }

    // Tagline below company name
    if (t.tagline) {
      const brand = document.querySelector('.portal-header-brand');
      if (brand && !brand.querySelector('.portal-tagline')) {
        const tag = document.createElement('span');
        tag.className = 'portal-tagline';
        tag.style.cssText = 'font-size:11px;color:rgba(255,255,255,.7);display:block;margin-top:1px;';
        tag.textContent = t.tagline;
        brand.appendChild(tag);
      }
    }

    // Contact link
    if (t.contactLink) {
      const headerRight = document.querySelector('.portal-header-right');
      if (headerRight && !headerRight.querySelector('.portal-contact-link')) {
        const a = document.createElement('a');
        a.className = 'portal-contact-link btn btn-secondary btn-sm';
        a.href = t.contactLink;
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        a.textContent = 'Nous contacter';
        headerRight.insertBefore(a, headerRight.firstChild);
      }
    }

    // Footer
    if (t.footerText) {
      const existing = document.querySelector('.portal-footer');
      if (!existing) {
        const footer = document.createElement('footer');
        footer.className = 'portal-footer';
        footer.style.cssText = 'text-align:center;padding:16px;font-size:12px;color:var(--color-gray-500);border-top:1px solid var(--color-gray-200);margin-top:32px;';
        footer.textContent = t.footerText;
        document.body.appendChild(footer);
      }
    }

    // Custom CSS
    if (t.customCss) {
      const cssId = 'portal-custom-css';
      let customStyle = document.getElementById(cssId);
      if (!customStyle) { customStyle = document.createElement('style'); customStyle.id = cssId; document.head.appendChild(customStyle); }
      customStyle.textContent = t.customCss;
    }
  } catch { /* non-blocking */ }
}
