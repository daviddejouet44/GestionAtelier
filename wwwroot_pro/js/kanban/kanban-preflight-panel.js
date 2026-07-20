// kanban-preflight-panel.js -- Panneau d'analyse PDF preflight automatique
import { authToken, showNotification } from '../core.js';

let _autoPreflightEnabled = null;

export async function isAutoPreflightEnabled() {
  if (_autoPreflightEnabled !== null) return _autoPreflightEnabled;
  try {
    const r = await fetch('/api/config/autopreflight', { headers: { "Authorization": `Bearer ${authToken}` } }).then(r => r.json());
    _autoPreflightEnabled = !!(r.ok && r.config && r.config.enabled);
  } catch(e) { _autoPreflightEnabled = false; }
  return _autoPreflightEnabled;
}

function badge(s, text) {
  const C = { ok: ['#dcfce7','#16a34a','🟢'], warning: ['#fef9c3','#ca8a04','🟠'], error: ['#fee2e2','#dc2626','🔴'], na: ['#f3f4f6','#6b7280','—'] };
  const c = C[s] || C.na;
  return '<span style="display:inline-flex;align-items:center;gap:4px;padding:3px 8px;border-radius:12px;font-size:12px;font-weight:600;background:' + c[0] + ';color:' + c[1] + ';">' + c[2] + ' ' + text + '</span>';
}
function fmm(v) { return v != null ? v.toFixed(1) + ' mm' : '—'; }
function fdpi(v) { return v != null ? Math.round(v) + ' dpi' : '—'; }
function fpct(v) { return v != null ? v.toFixed(1) + ' %' : 'Non calculé (Étape 5)'; }

export async function openPreflightAnalysisPanel(fullPath, fileName, onLaunchDone) {
  const overlay = document.createElement('div');
  overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.55);display:flex;align-items:center;justify-content:center;z-index:10000;overflow:auto;padding:20px;';
  const modal = document.createElement('div');
  modal.style.cssText = 'background:white;border-radius:14px;padding:28px 32px;min-width:520px;max-width:820px;width:100%;box-shadow:0 16px 48px rgba(0,0,0,.3);position:relative;max-height:90vh;overflow-y:auto;';
  modal.innerHTML = [
    '<button id="pf-panel-close" style="position:absolute;top:14px;right:14px;background:none;border:none;cursor:pointer;font-size:18px;color:#6b7280;" title="Fermer">&times;</button>',
    '<h3 style="margin:0 0 4px;font-size:16px;color:#111827;">📄 Analyse PDF</h3>',
    '<div style="font-size:12px;color:#6b7280;margin-bottom:18px;word-break:break-all;" id="pf-file-name"></div>',
    '<div id="pf-panel-body" style="min-height:80px;display:flex;align-items:center;justify-content:center;"><span style="color:#6b7280;font-size:13px;">⏳ Analyse en cours…</span></div>',
  ].join('');
  modal.querySelector('#pf-file-name').textContent = fileName;
  overlay.appendChild(modal);
  document.body.appendChild(overlay);
  modal.querySelector('#pf-panel-close').onclick = () => overlay.remove();
  overlay.onclick = (e) => { if (e.target === overlay) overlay.remove(); };
  const bodyEl = modal.querySelector('#pf-panel-body');

  let analysisResult = null;
  try {
    const r = await fetch('/api/preflight/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ fullPath })
    });
    analysisResult = await r.json();
  } catch(e) {
    bodyEl.innerHTML = '<div style="color:#dc2626;font-size:13px;">❌ Erreur réseau: ' + e.message + '</div>';
    return;
  }
  if (!analysisResult.ok) {
    bodyEl.innerHTML = '<div style="color:#dc2626;font-size:13px;">❌ ' + (analysisResult.error || 'Erreur analyse') + '</div>';
    return;
  }

  const rep = analysisResult.report;
  const rec = analysisResult.recommendation;

  const bleedSt = () => rep.bleedMm == null ? 'warning' : rep.bleedMm >= 3 ? 'ok' : rep.bleedMm > 0 ? 'warning' : 'error';
  const rows = [
    { label: 'Taille / Format',    statusFn: () => rep.trimBox?.present ? 'ok' : 'warning',
      valueFn: () => rep.finishedFormat || (rep.trimBox?.widthMm != null ? rep.trimBox.widthMm.toFixed(0) + '×' + rep.trimBox.heightMm.toFixed(0) + ' mm' : '—') },
    { label: 'Fond perdu',         statusFn: bleedSt, valueFn: () => fmm(rep.bleedMm) },
    { label: 'TrimBox',            statusFn: () => rep.trimBox?.present ? 'ok' : 'warning', valueFn: () => rep.trimBox?.present ? 'Présente' : 'Absente' },
    { label: 'Espace couleur',     statusFn: () => rep.usesRgb ? 'error' : 'ok', valueFn: () => [rep.usesRgb?'RVB':'',rep.usesCmyk?'CMJN':'',rep.usesGray?'Gris':''].filter(Boolean).join(', ')||'—' },
    { label: 'Tons directs',       statusFn: () => rep.spotColors?.length ? 'warning' : 'ok', valueFn: () => rep.spotColors?.length ? rep.spotColors.join(', ') : 'Aucun' },
    { label: 'TAC',               statusFn: () => rep.totalInkCoveragePercent==null?'na':(rec?.corrections?.some(c=>c.ruleId==='tac_reduction')?'error':'ok'), valueFn: () => fpct(rep.totalInkCoveragePercent) },
    { label: 'Polices manquantes', statusFn: () => rep.hasMissingFonts?'error':'ok', valueFn: () => rep.hasMissingFonts?(rep.missingFonts?.join(', ')||'Oui'):'Aucune' },
    { label: 'Résolution images',  statusFn: () => rep.minImageDpi==null?'na':(rep.imagesBelow300DpiCount>0?'warning':'ok'), valueFn: () => rep.minImageDpi!=null ? fdpi(rep.minImageDpi)+' min ('+rep.imagesBelow300DpiCount+' < seuil)' : '—' },
    { label: 'Surimpression',      statusFn: () => rep.hasOverprint==null?'na':(rep.hasOverprint?'warning':'ok'), valueFn: () => rep.hasOverprint==null?'Non analysé':(rep.hasOverprint?'Détectée':'Aucune') },
  ];

  let availableDroplets = [];
  try { const dr = await fetch('/api/config/preflight/droplets').then(r=>r.json()).catch(()=>null); if(dr&&dr.ok&&Array.isArray(dr.droplets)) availableDroplets=dr.droplets; } catch(e) {}
  let selectedDropletPath = rec?.selectedDroplet?.path || (availableDroplets[0]?.path || '');

  bodyEl.style.cssText = '';
  bodyEl.innerHTML = '';

  // Checks table
  const bl = s => s==='ok'?'OK':s==='warning'?'Attention':s==='error'?'Erreur':'N/A';
  const tableWrap = document.createElement('div');
  tableWrap.style.cssText = 'margin-bottom:18px;';
  tableWrap.innerHTML = '<h4 style="margin:0 0 10px;font-size:14px;color:#374151;">Contrôles</h4><table style="width:100%;border-collapse:collapse;font-size:13px;"><thead><tr style="background:#f3f4f6;"><th style="text-align:left;padding:6px 10px;font-weight:600;color:#374151;border-bottom:1px solid #e5e7eb;">Contrôle</th><th style="text-align:left;padding:6px 10px;font-weight:600;color:#374151;border-bottom:1px solid #e5e7eb;">Valeur</th><th style="text-align:left;padding:6px 10px;font-weight:600;color:#374151;border-bottom:1px solid #e5e7eb;">Statut</th></tr></thead><tbody id="pf-checks-tbody"></tbody></table>';
  bodyEl.appendChild(tableWrap);
  const tbody = tableWrap.querySelector('#pf-checks-tbody');
  rows.forEach(row => {
    const st = row.statusFn(); const val = row.valueFn();
    const tr = document.createElement('tr');
    tr.style.borderBottom = '1px solid #f3f4f6';
    tr.innerHTML = '<td style="padding:6px 10px;color:#374151;font-weight:500;">' + row.label + '</td><td style="padding:6px 10px;color:#6b7280;max-width:220px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" title="' + val + '">' + val + '</td><td style="padding:6px 10px;">' + badge(st, bl(st)) + '</td>';
    tbody.appendChild(tr);
  });

  // Recommendation section
  const recoDiv = document.createElement('div');
  recoDiv.style.cssText = 'background:#f0f9ff;border:1px solid #bae6fd;border-radius:10px;padding:14px 16px;margin-bottom:18px;';
  recoDiv.innerHTML = '<h4 style="margin:0 0 8px;font-size:14px;color:#0369a1;">Recommandation du moteur</h4><div id="pf-reco-body"></div>';
  bodyEl.appendChild(recoDiv);
  const recoEl = recoDiv.querySelector('#pf-reco-body');
  if (!rec || !rec.isActive) {
    recoEl.innerHTML = '<p style="font-size:13px;color:#6b7280;margin:0;">Le moteur est désactivé. Activez-le dans <em>Paramétrage &gt; Preflight</em>.</p>';
  } else if (!rec.corrections || rec.corrections.length === 0) {
    recoEl.innerHTML = '<p style="font-size:13px;color:#16a34a;margin:0;">✅ Aucune correction requise.</p>';
  } else {
    const corrHtml = rec.corrections.map(c => '<li style="margin-bottom:4px;"><strong>' + c.label + '</strong>' + (c.description ? ' — ' + c.description : '') + '</li>').join('');
    const dpHtml = rec.selectedDroplet ? '<p style="font-size:12px;color:#0369a1;margin:8px 0 0;"><strong>Droplet présélectionné :</strong> ' + (rec.selectedDroplet.name || rec.selectedDroplet.path) + '</p>' : '';
    recoEl.innerHTML = '<p style="font-size:13px;color:#374151;margin:0 0 6px;"><strong>Preflight conseillé :</strong> ' + (rec.advisedPreflightLabel || 'Standard') + '</p><p style="font-size:13px;font-weight:600;color:#374151;margin:0 0 4px;">Corrections proposées :</p><ul style="margin:0;padding-left:18px;font-size:13px;color:#374151;">' + corrHtml + '</ul>' + dpHtml;
  }

  // Launch section
  const launchDiv = document.createElement('div');
  launchDiv.style.cssText = 'border-top:1px solid #e5e7eb;padding-top:16px;';
  launchDiv.innerHTML = '<h4 style="margin:0 0 10px;font-size:14px;color:#374151;">Lancer le Preflight</h4><div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;"><label style="font-size:13px;color:#374151;white-space:nowrap;">Droplet :</label><select id="pf-droplet-select" class="settings-input" style="flex:1;min-width:200px;max-width:360px;font-size:13px;"></select><button id="pf-launch-btn" class="btn btn-primary" style="min-width:140px;">▶ Lancer le Preflight</button></div><div id="pf-launch-status" style="margin-top:10px;font-size:13px;min-height:18px;"></div>';
  bodyEl.appendChild(launchDiv);
  const selectEl = launchDiv.querySelector('#pf-droplet-select');
  const launchBtn = launchDiv.querySelector('#pf-launch-btn');
  const statusEl = launchDiv.querySelector('#pf-launch-status');
  if (availableDroplets.length === 0) {
    selectEl.innerHTML = '<option value="">Aucun droplet configuré &mdash; Paramétrage &gt; Preflight</option>';
    launchBtn.disabled = true;
  } else {
    availableDroplets.forEach(d => {
      const opt = document.createElement('option');
      opt.value = d.path;
      opt.textContent = d.name || d.path;
      if (d.path === selectedDropletPath) opt.selected = true;
      selectEl.appendChild(opt);
    });
  }

  launchBtn.onclick = async () => {
    const dropletPath = selectEl.value;
    if (!dropletPath) { statusEl.textContent = '⚠️ Sélectionnez un droplet.'; return; }
    launchBtn.disabled = true;
    launchBtn.textContent = '⏳ Preflight en cours…';
    statusEl.textContent = '';
    try {
      const r = await fetch('/api/acrobat/preflight', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ fullPath, dropletPath })
      }).then(res => res.json()).catch(() => ({ ok: false, error: 'Erreur réseau' }));
      if (r.ok) {
        launchBtn.textContent = '✅ Preflight terminé';
        statusEl.style.color = '#16a34a';
        statusEl.textContent = 'Le fichier a été déplacé vers Prêt pour impression.';
        if (onLaunchDone) setTimeout(onLaunchDone, 800);
      } else {
        launchBtn.disabled = false;
        launchBtn.textContent = '▶ Lancer le Preflight';
        statusEl.style.color = '#dc2626';
        statusEl.textContent = '❌ ' + (r.error || 'Erreur inconnue');
      }
    } catch(err) {
      launchBtn.disabled = false;
      launchBtn.textContent = '▶ Lancer le Preflight';
      statusEl.style.color = '#dc2626';
      statusEl.textContent = '❌ ' + err.message;
    }
  };
}
