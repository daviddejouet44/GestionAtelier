// kanban/kanban-actions.js — Print dialog, actions dropdown, assign dropdown, façonnage alerts
import { authToken, fnKey, showNotification, assignmentsByPath } from '../core.js';
import { refreshKanban } from './kanban-core.js';

// ======================================================
// DIALOG IMPRESSION — remplacé par openActionsDropdown
// (conservé pour compatibilité)
// ======================================================
export async function openPrintDialog(fullPath) {
  openActionsDropdown(null, fullPath);
}

// ======================================================
// ACTIONS DROPDOWN (En attente)
// ======================================================
export async function openActionsDropdown(btnEl, fullPath) {
  document.querySelectorAll(".actions-dropdown").forEach(d => d.remove());

  const dropdown = document.createElement("div");
  dropdown.className = "actions-dropdown";
  dropdown.style.cssText = `
    position: fixed; background: white; border: 1px solid #e5e7eb;
    border-radius: 10px; box-shadow: 0 8px 24px rgba(0,0,0,0.18);
    z-index: 9999; min-width: 220px; overflow: hidden; padding: 4px 0;
  `;

  const items = [
    { label: "Envoyer vers PrismaSync", action: "prismasync" },
    { label: "Ouvrir dans PrismaPrepare", action: "prisma-prepare" },
    { label: "Impression directe", action: "direct-print" },
    { label: "Envoyer dans Fiery", action: "fiery" }
  ];

  items.forEach(item => {
    const el = document.createElement("div");
    el.style.cssText = "padding: 10px 16px; cursor: pointer; font-size: 13px; color: #111827; transition: background 0.15s; white-space: nowrap;";
    el.textContent = item.label;
    el.onmouseenter = () => el.style.background = "#f3f4f6";
    el.onmouseleave = () => el.style.background = "";
    el.onclick = async () => {
      dropdown.remove();
      await handlePrintAction(item.action, fullPath);
    };
    dropdown.appendChild(el);
  });

  document.body.appendChild(dropdown);

  // Position relative to button if provided, else center
  if (btnEl) {
    const rect = btnEl.getBoundingClientRect();
    const dropW = 220;
    let left = rect.left + window.scrollX;
    if (left + dropW > window.innerWidth) left = window.innerWidth - dropW - 8;
    dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
    dropdown.style.left = left + "px";
  } else {
    dropdown.style.top = "50%";
    dropdown.style.left = "50%";
    dropdown.style.transform = "translate(-50%, -50%)";
  }

  setTimeout(() => {
    document.addEventListener("click", function closeDropdown(e) {
      if (!dropdown.contains(e.target)) {
        dropdown.remove();
        document.removeEventListener("click", closeDropdown);
      }
    });
  }, 10);
}

async function handlePrintAction(action, fullPath) {
  const fileName = fnKey(fullPath);

  try {
    const r = await fetch("/api/jobs/send-to-action", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
      body: JSON.stringify({ fileName, fullPath, action })
    }).then(r => r.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));

    if (r.ok) {
      showNotification(`✅ ${r.message || "Envoi effectué"}`, "success");
      await refreshKanban();
    } else {
      showNotification("❌ " + (r.error || "Erreur"), "error");
    }
  } catch(e) { showNotification("❌ " + e.message, "error"); }
}

// ======================================================
// DROPDOWN AFFECTATION
// ======================================================
export async function openAssignDropdown(btn, fullPath) {
  document.querySelectorAll(".assign-dropdown").forEach(d => d.remove());

  let operators = [];
  try {
    const resp = await fetch("/api/operators").then(r => r.json());
    operators = resp.operators || [];
  } catch(err) {
    showNotification("❌ Impossible de charger les opérateurs", "error");
    return;
  }

  if (operators.length === 0) {
    showNotification("ℹ️ Aucun opérateur disponible", "info");
    return;
  }

  const dropdown = document.createElement("div");
  dropdown.className = "assign-dropdown";
  dropdown.style.cssText = `
    position: absolute; background: white; border: 1px solid #e5e7eb;
    border-radius: 8px; box-shadow: 0 4px 16px rgba(0,0,0,0.15);
    z-index: 9999; min-width: 180px; overflow: hidden;
  `;

  operators.forEach(op => {
    const item = document.createElement("div");
    item.style.cssText = "padding: 10px 14px; cursor: pointer; font-size: 13px; transition: background 0.15s;";
    item.textContent = op.name;
    item.onmouseenter = () => item.style.background = "#f3f4f6";
    item.onmouseleave = () => item.style.background = "";
    item.onclick = async () => {
      dropdown.remove();
      const fileName = fnKey(fullPath);
      const r = await fetch("/api/assignment", {
        method: "PUT",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ fullPath, fileName, operatorId: op.id })
      }).then(r => r.json()).catch(() => ({ ok: false }));

      if (r.ok) {
        const asgn = { fullPath, fileName, operatorName: r.operatorName || op.name, operatorId: op.id };
        assignmentsByPath[fileName] = asgn;
        showNotification(`✅ Job affecté à ${r.operatorName || op.name}`, "success");
        await refreshKanban();
      } else {
        showNotification("❌ Erreur : " + (r.error || ""), "error");
      }
    };
    dropdown.appendChild(item);
  });

  const rect = btn.getBoundingClientRect();
  dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
  dropdown.style.left = (rect.left + window.scrollX) + "px";
  document.body.appendChild(dropdown);

  setTimeout(() => {
    document.addEventListener("click", function closeDropdown() {
      dropdown.remove();
      document.removeEventListener("click", closeDropdown);
    }, { once: true });
  }, 10);
}

// ======================================================
// ALERTES FAÇONNAGE — popup
// ======================================================
export async function showFaconnageAlerts() {
  const data = await fetch("/api/alerts/faconnage").then(r => r.json()).catch(() => ({ ok: false }));

  const overlay = document.createElement("div");
  overlay.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:10000;";

  const panel = document.createElement("div");
  panel.style.cssText = "background:white;border-radius:12px;padding:24px;max-width:680px;width:92%;max-height:85vh;overflow-y:auto;box-shadow:0 10px 40px rgba(0,0,0,.3);";

  let html = `<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;">
    <h3 style="margin:0;font-size:18px;font-weight:700;">📋 Productions à venir</h3>
    <button id="fa-close" style="background:none;border:none;font-size:20px;cursor:pointer;color:#6b7280;">✕</button>
  </div>`;

  if (!data.ok || !Array.isArray(data.alerts) || data.alerts.length === 0) {
    html += '<p style="color:#9ca3af;text-align:center;padding:20px;">Aucun job en impression en cours</p>';
  } else {
    // Group by all finition types (faconnage, ennoblissement, rainage, finitionsChecked)
    const grouped = {}; // { optionName: [{fileName, numeroDossier, quantite, allFinitions}] }
    let jobsWithNoFinitions = [];

    for (const item of data.alerts) {
      const quantite = item.quantite ? parseInt(item.quantite) : null;
      // Build full finition list from all sources
      const allFinitions = [];
      if (Array.isArray(item.faconnage) && item.faconnage.length > 0)
        allFinitions.push(...item.faconnage);
      if (Array.isArray(item.ennoblissement) && item.ennoblissement.length > 0)
        allFinitions.push(...item.ennoblissement);
      if (item.rainage) allFinitions.push('Rainage');
      if (Array.isArray(item.finitionsChecked) && item.finitionsChecked.length > 0)
        allFinitions.push(...item.finitionsChecked);

      if (allFinitions.length > 0) {
        const seen = new Set();
        for (const opt of allFinitions) {
          if (!opt || seen.has(opt)) continue;
          seen.add(opt);
          if (!grouped[opt]) grouped[opt] = [];
          grouped[opt].push({ fileName: item.fileName, numeroDossier: item.numeroDossier, quantite, allFinitions: [...new Set(allFinitions)] });
        }
      } else {
        jobsWithNoFinitions.push({ fileName: item.fileName, numeroDossier: item.numeroDossier, quantite });
      }
    }

    html += `<p style="font-size:13px;color:#6b7280;margin-bottom:16px;">${data.alerts.length} job(s) en impression en cours</p>`;
    html += '<div style="display:flex;flex-direction:column;gap:14px;">';

    const optionNames = Object.keys(grouped).sort();
    for (const opt of optionNames) {
      const jobs = grouped[opt];
      const totalQty = jobs.reduce((s, j) => s + (j.quantite || 0), 0);
      const jobRows = jobs.map(j => {
        const dossier = j.numeroDossier || '—';
        const pdfName = j.fileName || '—';
        const finStr = Array.isArray(j.allFinitions) && j.allFinitions.length > 1 ? ` — [${j.allFinitions.join(', ')}]` : '';
        const qty = j.quantite != null ? ` (${j.quantite.toLocaleString("fr-FR")} ex.)` : "";
        return `<div style="font-size:12px;color:#374151;padding:3px 0 3px 12px;border-left:3px solid #fde68a;"><strong>${dossier}</strong> — ${pdfName}${finStr}${qty}</div>`;
      }).join("");
      const totalLine = totalQty > 0 ? `<div style="font-size:12px;font-weight:700;color:#374151;margin-top:6px;">Total : ${totalQty.toLocaleString("fr-FR")} exemplaires</div>` : "";
      html += `<div style="background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:14px;">
        <div style="font-weight:700;font-size:14px;color:#92400e;margin-bottom:8px;">✂️ ${opt} — ${jobs.length} job(s) à venir</div>
        ${jobRows}
        ${totalLine}
      </div>`;
    }

    if (jobsWithNoFinitions.length > 0) {
      const jobRows = jobsWithNoFinitions.map(j => {
        const dossier = j.numeroDossier || '—';
        const pdfName = j.fileName || '—';
        return `<div style="font-size:12px;color:#6b7280;padding:3px 0 3px 12px;border-left:3px solid #e5e7eb;"><strong>${dossier}</strong> — ${pdfName}</div>`;
      }).join("");
      html += `<div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:14px;">
        <div style="font-weight:700;font-size:14px;color:#9ca3af;margin-bottom:8px;">Sans finition — ${jobsWithNoFinitions.length} job(s)</div>
        ${jobRows}
      </div>`;
    }

    html += '</div>';
  }

  panel.innerHTML = html;
  overlay.appendChild(panel);
  document.body.appendChild(overlay);

  panel.querySelector("#fa-close").onclick = () => overlay.remove();
  overlay.addEventListener("click", (e) => { if (e.target === overlay) overlay.remove(); });
}
