// search.js — Recherche globale ultra-rapide (point 6)
import { authToken, esc } from './core.js';

let _debounce = null;
let _lastQuery = "";

export function initGlobalSearch() {
  const wrap = document.getElementById("global-search");
  const input = document.getElementById("global-search-input");
  const results = document.getElementById("global-search-results");
  if (!wrap || !input || !results) return;

  wrap.style.display = "block";

  input.oninput = () => {
    const q = input.value.trim();
    if (_debounce) clearTimeout(_debounce);
    if (q.length < 2) { hideResults(); return; }
    _debounce = setTimeout(() => runSearch(q), 220);
  };

  input.onkeydown = (e) => {
    if (e.key === "Escape") { input.value = ""; hideResults(); input.blur(); }
  };

  input.onfocus = () => {
    const q = input.value.trim();
    if (q.length >= 2 && results.innerHTML) results.classList.remove("hidden");
  };

  // Fermeture au clic en dehors.
  document.addEventListener("click", (e) => {
    if (!wrap.contains(e.target)) hideResults();
  });
}

function hideResults() {
  const results = document.getElementById("global-search-results");
  if (results) results.classList.add("hidden");
}

async function runSearch(q) {
  const results = document.getElementById("global-search-results");
  if (!results) return;
  _lastQuery = q;
  results.classList.remove("hidden");
  results.innerHTML = '<div style="padding:12px;color:#6b7280;font-size:13px;">Recherche…</div>';

  let data;
  try {
    data = await fetch("/api/search?q=" + encodeURIComponent(q) + "&limit=20", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json());
  } catch (e) {
    results.innerHTML = '<div style="padding:12px;color:#dc2626;font-size:13px;">Erreur de recherche</div>';
    return;
  }

  // Ignore les réponses obsolètes (frappe rapide).
  if (q !== _lastQuery) return;

  if (!data.ok) {
    results.innerHTML = `<div style="padding:12px;color:#dc2626;font-size:13px;">${esc(data.error || "Erreur")}</div>`;
    return;
  }
  const items = Array.isArray(data.results) ? data.results : [];
  if (items.length === 0) {
    results.innerHTML = '<div style="padding:14px;color:#9ca3af;font-size:13px;">Aucun résultat</div>';
    return;
  }

  results.innerHTML = items.map((o, i) => {
    const title = o.numeroDossier ? ('#' + o.numeroDossier) : (o.fileName || '—');
    const sub = [
      o.client, o.reference ? 'Réf ' + o.reference : '', o.moteur, o.operateur,
      o.papier, o.dateImpression ? '🗓️ ' + _frDate(o.dateImpression) : ''
    ].filter(Boolean).map(esc).join(' · ');
    return `<div class="gsr-item" data-path="${esc(o.fullPath || '')}"
      style="padding:9px 12px;border-top:${i === 0 ? '0' : '1px'} solid #f1f5f9;cursor:pointer;">
      <div style="font-size:13px;font-weight:600;color:#111827;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">
        ${esc(title)}${o.typeTravail ? ` <span style="font-weight:400;color:#6b7280;">— ${esc(o.typeTravail)}</span>` : ''}
      </div>
      <div style="font-size:12px;color:#6b7280;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${sub || esc(o.fileName || '')}</div>
    </div>`;
  }).join('');

  results.querySelectorAll('.gsr-item').forEach(el => {
    el.onmouseenter = () => el.style.background = '#f5f3ff';
    el.onmouseleave = () => el.style.background = '';
    el.onclick = () => {
      const path = el.dataset.path;
      hideResults();
      if (path && window._openFabrication) window._openFabrication(path);
    };
  });
}

function _frDate(iso) {
  try { return new Date(iso + 'T00:00:00').toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric' }); }
  catch { return iso; }
}
