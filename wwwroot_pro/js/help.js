// help.js — Panneau d'aide global
// Fournit un bouton ❓ dans le header et un drawer d'aide en Markdown.
// Usage : import { initHelpPanel } from './help.js';
//         initHelpPanel();

const HELP_SECTIONS = [
  { id: 'index',             icon: '🏠', title: 'Vue d\'ensemble' },
  { id: 'soumission-manuelle', icon: '📝', title: 'Soumission manuelle' },
  { id: 'pdf-xml',           icon: '📑', title: 'PDF + XML couplés' },
  { id: 'import-xml',        icon: '📥', title: 'Import XML / Lookup ERP' },
  { id: 'hotfolder',         icon: '📂', title: 'Hotfolder local' },
  { id: 'sources-auto',      icon: '☁️', title: 'Sources automatiques' },
  { id: 'lookup-erp',        icon: '🔗', title: 'Lookup ERP / W2P' },
  { id: 'portail-client',    icon: '🌐', title: 'Portail client' },
  { id: 'bat-workflow',      icon: '✅', title: 'Workflow BAT' },
  { id: 'profils',           icon: '👥', title: 'Profils utilisateurs' },
];

const HELP_BASE = '/pro/help/';

/** Minimal Markdown → HTML renderer (no external lib required) */
function renderMarkdown(md) {
  let html = (md || '')
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    // Code blocks
    .replace(/```[\w]*\n?([\s\S]*?)```/g, '<pre><code>$1</code></pre>')
    // Inline code
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    // Bold
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    // Italic
    .replace(/\*([^*]+)\*/g, '<em>$1</em>')
    // Headers
    .replace(/^### (.+)$/gm, '<h3>$1</h3>')
    .replace(/^## (.+)$/gm, '<h2>$1</h2>')
    .replace(/^# (.+)$/gm, '<h1>$1</h1>')
    // HR
    .replace(/^---$/gm, '<hr />')
    // Tables (basic — pipes only)
    .replace(/^\|(.+)\|$/gm, (_, row) => {
      const cells = row.split('|').map(c => c.trim());
      return '<tr>' + cells.map(c => (c.match(/^[-: ]+$/) ? '' : `<td>${c}</td>`)).join('') + '</tr>';
    })
    // Links
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="#$2" class="help-internal-link" data-section="$2">$1</a>')
    // Unordered lists (simple single-level)
    .replace(/^[-*] (.+)$/gm, '<li>$1</li>')
    // Numbered lists
    .replace(/^\d+\. (.+)$/gm, '<oli>$1</oli>')
    // Paragraphs (blank lines)
    .replace(/\n{2,}/g, '\n</p><p>\n');

  // Wrap consecutive <li>
  html = html.replace(/(<li>.*<\/li>\n?)+/g, match => `<ul>${match}</ul>`);
  html = html.replace(/(<oli>.*<\/oli>\n?)+/g, match => `<ol>${match.replace(/<\/?oli>/g, tag => tag === '<oli>' ? '<li>' : '</li>')}</ol>`);

  // Wrap <tr> in <table>
  html = html.replace(/(<tr>.*<\/tr>\n?)+/g, match => `<table>${match}</table>`);

  // Wrap in paragraphs
  html = `<p>${html}</p>`;
  // Remove empty paragraphs
  html = html.replace(/<p>\s*<\/p>/g, '');

  return html;
}

let drawerEl = null;
let overlayEl = null;
let _currentSection = null;
const _cache = {};

function createDrawer() {
  overlayEl = document.createElement('div');
  overlayEl.id = 'help-overlay';
  overlayEl.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.4);z-index:1000;display:none;';
  overlayEl.onclick = closeHelp;

  drawerEl = document.createElement('div');
  drawerEl.id = 'help-drawer';
  drawerEl.style.cssText = `
    position:fixed;top:0;right:-520px;width:520px;max-width:95vw;height:100vh;
    background:#fff;z-index:1001;display:flex;flex-direction:column;
    box-shadow:-4px 0 24px rgba(0,0,0,.15);transition:right .28s ease;
  `;

  drawerEl.innerHTML = `
    <div style="display:flex;align-items:center;justify-content:space-between;padding:14px 18px;border-bottom:1px solid #e5e7eb;background:#1d4ed8;">
      <div style="display:flex;align-items:center;gap:10px;">
        <span style="font-size:20px;">❓</span>
        <strong style="color:#fff;font-size:15px;">Aide — Gestion d'Atelier</strong>
      </div>
      <button id="help-close-btn" style="background:none;border:none;color:rgba(255,255,255,.9);font-size:20px;cursor:pointer;padding:4px 8px;border-radius:4px;" title="Fermer">✕</button>
    </div>

    <div style="padding:10px 16px;border-bottom:1px solid #e5e7eb;">
      <input id="help-search" type="search" placeholder="Rechercher dans l'aide…"
        style="width:100%;padding:7px 12px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;outline:none;" />
    </div>

    <div style="display:flex;flex:1;overflow:hidden;">
      <nav id="help-nav" style="width:180px;border-right:1px solid #e5e7eb;overflow-y:auto;flex-shrink:0;padding:8px 0;background:#f9fafb;">
        ${HELP_SECTIONS.map(s => `
          <button class="help-nav-btn" data-section="${s.id}" style="display:flex;align-items:center;gap:7px;width:100%;padding:8px 12px;background:none;border:none;text-align:left;font-size:12px;cursor:pointer;color:#374151;border-radius:0;">
            <span>${s.icon}</span><span>${s.title}</span>
          </button>
        `).join('')}
      </nav>
      <div id="help-content" style="flex:1;overflow-y:auto;padding:20px;font-size:14px;line-height:1.6;color:#374151;">
        <p style="color:#9ca3af;">Choisissez une section dans le sommaire.</p>
      </div>
    </div>

    <div style="padding:8px 16px;border-top:1px solid #e5e7eb;display:flex;gap:8px;background:#f9fafb;">
      <button id="help-print-btn" onclick="window.print()" style="font-size:12px;padding:5px 12px;background:#fff;border:1px solid #d1d5db;border-radius:4px;cursor:pointer;color:#374151;">🖨️ Imprimer</button>
      <span style="font-size:11px;color:#9ca3af;align-self:center;">Astuce : utilisez ?help=section dans l'URL pour ouvrir directement une section</span>
    </div>
  `;

  document.body.appendChild(overlayEl);
  document.body.appendChild(drawerEl);

  document.getElementById('help-close-btn').onclick = closeHelp;

  drawerEl.querySelectorAll('.help-nav-btn').forEach(btn => {
    btn.onclick = () => loadSection(btn.dataset.section);
  });

  // Internal links
  drawerEl.addEventListener('click', e => {
    const a = e.target.closest('.help-internal-link');
    if (a) { e.preventDefault(); loadSection(a.dataset.section); }
  });

  // Search
  let searchTimeout;
  document.getElementById('help-search').oninput = e => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => performSearch(e.target.value), 250);
  };
}

function openHelp(section) {
  if (!drawerEl) createDrawer();
  overlayEl.style.display = 'block';
  drawerEl.style.right = '0';
  document.body.style.overflow = 'hidden';
  loadSection(section || 'index');
}

function closeHelp() {
  if (!drawerEl) return;
  drawerEl.style.right = '-520px';
  overlayEl.style.display = 'none';
  document.body.style.overflow = '';
}

async function loadSection(section) {
  if (!HELP_SECTIONS.some(s => s.id === section)) section = 'index';
  _currentSection = section;

  // Highlight nav
  drawerEl.querySelectorAll('.help-nav-btn').forEach(btn => {
    const active = btn.dataset.section === section;
    btn.style.background = active ? '#eff6ff' : 'none';
    btn.style.color = active ? '#1d4ed8' : '#374151';
    btn.style.fontWeight = active ? '600' : 'normal';
    btn.style.borderLeft = active ? '3px solid #1d4ed8' : '3px solid transparent';
  });

  const contentEl = document.getElementById('help-content');
  if (_cache[section]) { contentEl.innerHTML = _cache[section]; return; }

  contentEl.innerHTML = '<p style="color:#9ca3af;">Chargement…</p>';

  try {
    const r = await fetch(`${HELP_BASE}${section}.md?t=${Date.now()}`);
    if (!r.ok) throw new Error('not found');
    const md = await r.text();
    const html = renderMarkdown(md);
    _cache[section] = `<div class="help-article">${html}</div>`;
    contentEl.innerHTML = _cache[section];
  } catch {
    contentEl.innerHTML = `<p style="color:#ef4444;">Section non trouvée : <code>${section}</code></p>`;
  }
}

async function performSearch(query) {
  if (!query || query.length < 2) {
    const contentEl = document.getElementById('help-content');
    if (_currentSection) await loadSection(_currentSection);
    return;
  }

  const contentEl = document.getElementById('help-content');
  contentEl.innerHTML = '<p style="color:#9ca3af;">Recherche…</p>';

  const results = [];
  for (const s of HELP_SECTIONS) {
    try {
      const r = await fetch(`${HELP_BASE}${s.id}.md`);
      if (!r.ok) continue;
      const md = await r.text();
      const lower = md.toLowerCase();
      const idx = lower.indexOf(query.toLowerCase());
      if (idx >= 0) {
        const snippet = md.substring(Math.max(0, idx - 60), idx + 120).replace(/\n/g, ' ');
        results.push({ section: s, snippet });
      }
    } catch {}
  }

  if (!results.length) {
    contentEl.innerHTML = `<p style="color:#6b7280;">Aucun résultat pour "<strong>${query}</strong>".</p>`;
    return;
  }

  const html = `
    <h3>Résultats pour "${query}" (${results.length})</h3>
    <ul>
      ${results.map(r => `
        <li style="margin-bottom:12px;">
          <a href="#" class="help-internal-link" data-section="${r.section.id}" style="font-weight:600;color:#1d4ed8;text-decoration:none;">
            ${r.section.icon} ${r.section.title}
          </a>
          <p style="font-size:12px;color:#6b7280;margin:4px 0 0;">…${r.snippet}…</p>
        </li>
      `).join('')}
    </ul>
  `;
  contentEl.innerHTML = html;
}

/** Injecte le bouton ❓ dans le header de l'app et gère ?help=... dans l'URL */
export function initHelpPanel() {
  // Inject button in header
  const headerRight = document.querySelector('#app-container .header-right');
  if (headerRight && !document.getElementById('btn-help')) {
    const btn = document.createElement('button');
    btn.id = 'btn-help';
    btn.className = 'btn';
    btn.title = 'Aide';
    btn.textContent = '❓ Aide';
    btn.style.cssText = 'font-size:13px;';
    btn.onclick = () => openHelp();
    // Insert before "Déconnexion"
    const btnLogout = document.getElementById('btn-logout');
    if (btnLogout) headerRight.insertBefore(btn, btnLogout);
    else headerRight.appendChild(btn);
  }

  // Handle ?help=section URL param
  const params = new URLSearchParams(window.location.search);
  const helpSection = params.get('help');
  if (helpSection) openHelp(helpSection);
}

// Add print styles
const printStyle = document.createElement('style');
printStyle.textContent = `
  @media print {
    body > *:not(#help-drawer) { display: none !important; }
    #help-drawer { position:static !important; width:100% !important; height:auto !important; box-shadow:none !important; }
    #help-overlay, #help-close-btn, #help-nav, #help-search, #help-print-btn { display:none !important; }
    #help-content { padding:0; }
  }
  .help-article h1 { font-size:20px; font-weight:700; margin:0 0 12px; color:#111827; }
  .help-article h2 { font-size:16px; font-weight:600; margin:16px 0 8px; color:#1d4ed8; }
  .help-article h3 { font-size:14px; font-weight:600; margin:12px 0 6px; }
  .help-article p  { margin:0 0 8px; }
  .help-article ul { margin:0 0 8px 18px; }
  .help-article ol { margin:0 0 8px 18px; }
  .help-article li { margin:3px 0; }
  .help-article code { background:#f3f4f6; padding:1px 5px; border-radius:3px; font-family:monospace; font-size:12px; }
  .help-article pre { background:#f3f4f6; padding:12px; border-radius:6px; overflow-x:auto; font-size:12px; margin:8px 0; }
  .help-article pre code { background:none; padding:0; }
  .help-article table { border-collapse:collapse; width:100%; margin:8px 0; font-size:13px; }
  .help-article table td, .help-article table th { border:1px solid #e5e7eb; padding:6px 10px; }
  .help-article hr { border:none; border-top:1px solid #e5e7eb; margin:16px 0; }
  .help-internal-link { color:#1d4ed8; text-decoration:none; }
  .help-internal-link:hover { text-decoration:underline; }
`;
document.head.appendChild(printStyle);
