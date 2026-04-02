// kanban.js — Tableau Kanban complet
import { authToken, currentUser, deliveriesByPath, assignmentsByPath, fnKey, normalizePath, isLight, darkenColor, fmtBytes, daysDiffFromToday, formatDateTime, showNotification, FOLDER_FIN_PRODUCTION } from './core.js';
import { openBatChoiceModal } from './bat.js';

const kanbanDiv = document.getElementById("kanban");
const searchInput = document.getElementById("searchInput");
const sortBy = document.getElementById("sortBy");
const _columnCache = {};

// ======================================================
// BUILD KANBAN
// ======================================================
export async function buildKanban() {
  const folderConfig = [
    { folder: "Début de production", label: "Début de production", color: "#5fa8c4" },
    { folder: "Corrections", label: "Corrections", color: "#e0e0e0" },
    { folder: "Corrections et fond perdu", label: "Corrections et fond perdu", color: "#e0e0e0" },
    { folder: "Rapport", label: "Rapport", color: "#cccccc" },
    { folder: "Prêt pour impression", label: "Prêt pour impression", color: "#b8b8b8" },
    { folder: "BAT", label: "BAT", color: "#a3a3a3" },
    { folder: "PrismaPrepare", label: "PrismaPrepare", color: "#8f8f8f" },
    { folder: "Fiery", label: "Fiery", color: "#8f8f8f" },
    { folder: "Impression en cours", label: "Impression en cours", color: "#7a7a7a" },
    { folder: "Façonnage", label: "Façonnage", color: "#666666" },
    { folder: "Fin de production", label: "Fin de production", color: "#22c55e" }
  ];

  kanbanDiv.innerHTML = "";
  kanbanDiv.style.gridTemplateColumns = "repeat(3, 1fr)";
  kanbanDiv.style.gap = "20px";
  kanbanDiv.style.padding = "20px";
  if (window._updateGlobalAlert) window._updateGlobalAlert();

  for (const cfg of folderConfig) {
    const col = document.createElement("div");
    col.className = "kanban-col-operator";
    col.dataset.folder = cfg.folder;
    const title = document.createElement("div");
    title.className = "kanban-col-operator__title";
    title.style.background = `linear-gradient(135deg, ${cfg.color} 0%, ${darkenColor(cfg.color, 15)} 100%)`;
    title.style.color = isLight(cfg.color) ? '#1D1D1F' : '#FFFFFF';
    title.textContent = cfg.label;
    const counter = document.createElement("span");
    counter.className = "kanban-col-counter";
    counter.textContent = "0";
    title.appendChild(counter);
    col.appendChild(title);

    const drop = document.createElement("div");
    drop.className = "kanban-col-operator__drop";
    drop.dataset.folder = cfg.folder;
    col.appendChild(drop);

    if (cfg.folder === "Corrections" || cfg.folder === "Corrections et fond perdu") {
      const acrobatBtn = document.createElement("button");
      acrobatBtn.className = "btn btn-acrobat";
      acrobatBtn.textContent = "Ouvrir dans Acrobat Pro";
      acrobatBtn.style.cssText = "margin: 0 15px 10px 15px; width: calc(100% - 30px);";
      acrobatBtn.onclick = async () => {
        try {
          const resp = await fetch("/api/acrobat", { method: "POST" });
          if (!resp.ok) throw new Error("Erreur au lancement d'Acrobat");
          alert("Acrobat Pro est en cours de lancement...");
        } catch (err) {
          alert("❌ " + err.message);
        }
      };
      col.insertBefore(acrobatBtn, drop);
    }

    drop.addEventListener("dragover", e => {
      e.preventDefault();
      drop.classList.add("drag-over");
    });
    drop.addEventListener("dragleave", () => {
      drop.classList.remove("drag-over");
    });
    drop.addEventListener("drop", async (e) => {
      e.preventDefault();
      drop.classList.remove("drag-over");

      if (e.dataTransfer && e.dataTransfer.files?.length) {
        if (window._handleDesktopDrop) window._handleDesktopDrop(e, cfg.folder);
        return;
      }

      const srcFull = normalizePath(e.dataTransfer.getData("text/plain"));
      if (!srcFull) return;

      const destFolder = cfg.folder;
      const moveResp = await fetch("/api/jobs/move", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ source: srcFull, destination: destFolder, overwrite: true })
      }).then(r => r.json()).catch(() => ({ ok: false }));

      if (!moveResp.ok) {
        alert("Erreur : " + (moveResp.error || ""));
        return;
      }

      if (destFolder === FOLDER_FIN_PRODUCTION) {
        const srcFk = fnKey(srcFull);
        if (deliveriesByPath[srcFk]) {
          const remove = confirm("Retirer du planning ?");
          if (remove) {
            await fetch("/api/delivery?fileName=" + encodeURIComponent(srcFk), { method: "DELETE" });
            delete deliveriesByPath[srcFk];
            delete deliveriesByPath[srcFk + "_time"];
          }
        }
      }

      if (window._loadDeliveries) await window._loadDeliveries();
      if (window._loadAssignments) await window._loadAssignments();
      if (window._updateGlobalAlert) window._updateGlobalAlert();
      await refreshKanban();
      if (window._refreshSubmissionView) await window._refreshSubmissionView();
      if (window._calendar) window._calendar.refetchEvents();
      if (window._submissionCalendar) window._submissionCalendar.refetchEvents();
    });

    kanbanDiv.appendChild(col);
  }

  // Summary bar
  let summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) {
    summaryEl = document.createElement("div");
    summaryEl.id = "kanban-summary";
    summaryEl.className = "kanban-summary";
    kanbanDiv.parentNode?.insertBefore(summaryEl, kanbanDiv);
  }

  await refreshKanban();

  if (searchInput) searchInput.oninput = () => refreshKanban();
  if (sortBy) sortBy.onchange = () => refreshKanban();
}

// ======================================================
// REFRESH KANBAN
// ======================================================
export async function refreshKanban() {
  const q = (searchInput?.value || "").trim().toLowerCase();
  const sort = (sortBy?.value || "date_desc");

  const cols = kanbanDiv.querySelectorAll(".kanban-col-operator");
  for (const col of cols) {
    await refreshKanbanColumnOperator(col.dataset.folder, q, sort, col);
  }
  await updateKanbanSummary();

  fetch("/api/jobs/cleanup-corrections", { method: "POST" }).catch(() => {});
}

// ======================================================
// SUMMARY BAR
// ======================================================
export async function updateKanbanSummary() {
  const summaryEl = document.getElementById("kanban-summary");
  if (!summaryEl) return;

  try {
    const folders = ["Début de production","Corrections","Corrections et fond perdu","Rapport","Prêt pour impression","BAT","PrismaPrepare","Fiery","Impression en cours","Façonnage","Fin de production"];
    const counts = {};
    for (const f of folders) {
      const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(f)}`).then(r => r.json()).catch(() => []);
      counts[f] = Array.isArray(jobs) ? jobs.length : 0;
    }

    const labelMap = {
      "Début de production": "Début prod",
      "Corrections": "Corrections",
      "Corrections et fond perdu": "Corr. fp",
      "Rapport": "Rapport",
      "Prêt pour impression": "Prêt impr.",
      "BAT": "BAT",
      "PrismaPrepare": "Prisma",
      "Fiery": "Fiery",
      "Impression en cours": "Impression",
      "Façonnage": "Façonnage",
      "Fin de production": "Fin prod"
    };

    const today = new Date(); today.setHours(0,0,0,0);
    const urgent = Object.entries(deliveriesByPath)
      .filter(([k]) => !k.endsWith("_time"))
      .map(([name, date]) => {
        const d = new Date(date + "T00:00:00");
        const diff = Math.ceil((d - today) / 86400000);
        return { name, date, diff };
      })
      .filter(x => x.diff >= 0 && x.diff <= 3)
      .sort((a,b) => a.diff - b.diff);

    const urgentHtml = urgent.length === 0 ? '<span style="color:#9ca3af;font-size:14px;">Aucune urgence</span>' :
      urgent.map(x => {
        const cls = x.diff === 0 ? "urgent-j0" : x.diff === 1 ? "urgent-j1" : x.diff === 2 ? "urgent-j2" : "urgent-j3";
        const label = x.diff === 0 ? "Aujourd'hui" : `J+${x.diff}`;
        return `<span class="urgent-badge ${cls}" title="${x.name}">${label}: ${x.name}</span>`;
      }).join("");

    summaryEl.innerHTML = `
      <div class="kanban-summary-urgent"><strong style="font-size:16px;color:#374151;margin-right:8px;">🚨 Urgences:</strong>${urgentHtml}</div>
    `;
  } catch(e) { console.error("Erreur summary:", e); }
}

// ======================================================
// DIALOG IMPRESSION
// ======================================================
export async function openPrintDialog(fullPath) {
  const fab = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fnKey(fullPath))).then(r=>r.json()).catch(()=>({}));

  const modal = document.createElement("div");
  modal.style.cssText = "position:fixed;inset:0;background:rgba(0,0,0,0.5);z-index:10000;display:flex;align-items:center;justify-content:center;";
  modal.innerHTML = `
    <div style="background:white;border-radius:16px;padding:32px;max-width:440px;width:100%;box-shadow:0 20px 60px rgba(0,0,0,0.3);">
      <h3 style="margin:0 0 20px;font-size:18px;color:#111;">Options d'impression</h3>
      <p style="color:#6b7280;font-size:13px;margin-bottom:20px;">Fichier: <strong>${fullPath.split("\\").pop()}</strong></p>
      <div style="display:flex;flex-direction:column;gap:10px;">
        <button class="btn btn-primary print-opt" data-action="controller">Envoyer vers le contrôleur</button>
        <button class="btn btn-primary print-opt" data-action="prisma">Envoyer vers PrismaPrepare</button>
        <button class="btn btn-primary print-opt" data-action="print">Envoyer en impression</button>
        <button class="btn btn-primary print-opt" data-action="modify">Modification</button>
        <button class="btn btn-primary print-opt" data-action="fiery">Envoyer sur Fiery</button>
      </div>
      <button id="print-dialog-close" class="btn" style="margin-top:16px;width:100%;">Annuler</button>
    </div>
  `;

  const printActions = {
    controller: { endpoint: "/api/commands/send-controller" },
    prisma: { endpoint: "/api/commands/send-prisma" },
    print: { endpoint: "/api/commands/send-print" },
    modify: { endpoint: "/api/commands/modify" },
    fiery: { endpoint: "/api/commands/send-fiery" }
  };

  modal.querySelectorAll(".print-opt").forEach(btn => {
    btn.onclick = async () => {
      const action = btn.dataset.action;
      const cfg = printActions[action];
      try {
        const r = await fetch(cfg.endpoint, {
          method: "POST",
          headers: {"Content-Type":"application/json"},
          body: JSON.stringify({ filePath: fullPath, product: fab.typeTravail || "", fabricationData: fab })
        }).then(r => r.json()).catch(()=>({ok:false,error:"Erreur réseau"}));

        if (r.ok) {
          showNotification(`✅ Action lancée`, "success");
          modal.remove();
          await refreshKanban();
        } else {
          showNotification("❌ " + (r.error || "Erreur"), "error");
        }
      } catch(e) {
        showNotification("❌ " + e.message, "error");
      }
    };
  });

  modal.querySelector("#print-dialog-close").onclick = () => modal.remove();
  document.body.appendChild(modal);
}

// ======================================================
// COLONNE KANBAN (opérateur)
// ======================================================
export async function refreshKanbanColumnOperator(folderName, q, sort, col, readOnly = false) {
  try {
    const jobs = await fetch(`/api/jobs?folder=${encodeURIComponent(folderName)}`)
      .then(r => r.json())
      .catch(() => []);

    const fingerprint = JSON.stringify(jobs.map(j => {
      const fn = fnKey(j.fullPath || j.name || '');
      return (j.name || '') + '|' + j.modified + '|' + j.size
        + '|' + ((assignmentsByPath[fn] || {}).operatorName || '')
        + '|' + (deliveriesByPath[fn] || '');
    }));
    const cacheKey = folderName + '|' + q + '|' + sort;
    if (_columnCache[cacheKey] === fingerprint) return;
    _columnCache[cacheKey] = fingerprint;

    const drop = col.querySelector(".kanban-col-operator__drop");
    drop.innerHTML = "";

    let filtered = jobs;
    if (q) {
      filtered = jobs.filter(j => (j.name || "").toLowerCase().includes(q.toLowerCase()));
    }

    if (sort === "name_asc") filtered.sort((a, b) => (a.name || "").localeCompare(b.name || ""));
    else if (sort === "name_desc") filtered.sort((a, b) => (b.name || "").localeCompare(a.name || ""));
    else if (sort === "size_asc") filtered.sort((a, b) => (a.size || 0) - (b.size || 0));
    else if (sort === "size_desc") filtered.sort((a, b) => (b.size || 0) - (a.size || 0));
    else filtered.sort((a, b) => new Date(b.modified) - new Date(a.modified));

    for (const job of filtered) {
      const card = document.createElement("div");
      card.className = "kanban-card-operator";
      if (!readOnly) card.draggable = true;
      card.dataset.fullPath = normalizePath(job.fullPath || "");
      card.dataset.folder = folderName;

      const full = normalizePath(job.fullPath || "");

      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        if (window._renderPdfThumbnail) window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      const textDiv = document.createElement("div");
      textDiv.style.cssText = "flex: 1; min-width: 0;";

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      textDiv.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = `${new Date(job.modified).toLocaleDateString("fr-FR")} · ${fmtBytes(job.size)}`;
      textDiv.appendChild(sub);

      const jobFileName = fnKey(full);
      const assignment = assignmentsByPath[jobFileName];
      if (assignment) {
        const badge = document.createElement("div");
        badge.className = "assignment-badge";
        badge.textContent = `${assignment.operatorName}`;
        textDiv.appendChild(badge);
      }

      const iso = deliveriesByPath[jobFileName];
      if (iso) {
        const status = document.createElement("div");
        status.className = "kanban-card-operator-status";
        const daysLeft = daysDiffFromToday(iso);
        if (daysLeft <= 1) status.classList.add("urgent");
        else if (daysLeft <= 3) status.classList.add("warning");
        status.textContent = new Date(iso).toLocaleDateString("fr-FR");
        textDiv.appendChild(status);
      }

      layout.appendChild(textDiv);
      card.appendChild(layout);

      const actions = document.createElement("div");
      actions.className = "kanban-card-operator-actions";

      const btnOpen = document.createElement("button");
      btnOpen.className = "btn btn-sm";
      btnOpen.textContent = "Ouvrir";
      btnOpen.onclick = () => window.open("/api/file?path=" + encodeURIComponent(full), "_blank", "noopener");

      const btnFiche = document.createElement("button");
      btnFiche.className = "btn btn-sm";
      btnFiche.textContent = "Fiche";
      btnFiche.onclick = () => { if (window._openFabrication) window._openFabrication(full); };

      const btnAssign = document.createElement("button");
      btnAssign.className = "btn btn-sm btn-assign";
      btnAssign.textContent = "Affecter à";
      btnAssign.onclick = (e) => { e.stopPropagation(); openAssignDropdown(btnAssign, full); };

      const btnDelete = document.createElement("button");
      btnDelete.className = "btn btn-sm";
      btnDelete.textContent = "Corbeille";
      btnDelete.onclick = () => { if (window._deleteFile) window._deleteFile(full); };

      if (folderName === "Rapport") {
        actions.appendChild(btnOpen);
        const btnAcrobat = document.createElement("button");
        btnAcrobat.className = "btn btn-sm";
        btnAcrobat.innerHTML = "Acrobat Pro";
        btnAcrobat.onclick = () => {
          fetch("/api/acrobat/open", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fullPath: full })
          }).then(r => r.json()).then(r => {
            if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
          });
        };
        actions.appendChild(btnAcrobat);

        const btnDelSrc = document.createElement("button");
        btnDelSrc.className = "btn btn-sm";
        btnDelSrc.style.cssText = "color:#dc2626;border-color:#dc2626;";
        btnDelSrc.innerHTML = "Supprimer source";
        btnDelSrc.onclick = async () => {
          if (!confirm("Supprimer le fichier source dans Corrections / Corrections et fond perdu ?")) return;
          const r = await fetch("/api/jobs/delete-corrections-source", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fileName: jobFileName })
          }).then(r => r.json()).catch(() => ({ok: false}));
          if (r.ok) { showNotification("✅ Source supprimée", "success"); await refreshKanban(); }
          else showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        actions.appendChild(btnDelSrc);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "BAT") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnAssign);
        const btnAcrobatBat = document.createElement("button");
        btnAcrobatBat.className = "btn btn-sm";
        btnAcrobatBat.innerHTML = "Acrobat";
        btnAcrobatBat.onclick = () => {
          fetch("/api/acrobat/open", {
            method: "POST",
            headers: {"Content-Type": "application/json"},
            body: JSON.stringify({ fullPath: full })
          }).then(r => r.json()).then(r => {
            if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
          });
        };
        actions.appendChild(btnAcrobatBat);
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else if (folderName === "Prêt pour impression") {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);

        // BAT button (remplace PrismaPrepare) — ouvre popup BAT complet / BAT simple
        const btnBAT = document.createElement("button");
        btnBAT.className = "btn btn-sm btn-primary";
        btnBAT.innerHTML = "→ BAT";
        btnBAT.onclick = () => {
          openBatChoiceModal(full, async () => {
            await refreshKanban();
          });
        };
        actions.appendChild(btnBAT);

        const btnPrint = document.createElement("button");
        btnPrint.className = "btn btn-sm btn-primary";
        btnPrint.innerHTML = "Imprimer";
        btnPrint.onclick = () => openPrintDialog(full);
        actions.appendChild(btnPrint);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      } else {
        actions.appendChild(btnOpen);
        actions.appendChild(btnFiche);
        actions.appendChild(btnAssign);
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          actions.appendChild(btnDelete);
        }
      }

      card.appendChild(actions);

      // BAT tracking pour la colonne BAT
      if (folderName === "BAT") {
        const batTracking = document.createElement("div");
        batTracking.className = "bat-tracking";
        batTracking.innerHTML = '<span style="color:var(--text-tertiary);font-size:10px;">Chargement...</span>';
        fetch(`/api/bat/status?path=${encodeURIComponent(full)}`)
          .then(r => r.json())
          .then(status => {
            batTracking.innerHTML = "";

            const btnSent = document.createElement("button");
            btnSent.className = "bat-status-badge bat-sent" + (status.sentAt ? " active" : "");
            btnSent.innerHTML = status.sentAt ? `ENVOYÉ ${formatDateTime(status.sentAt)}` : "MARQUER ENVOYÉ";
            btnSent.onclick = (e) => { e.stopPropagation(); delete _columnCache[cacheKey]; fetch("/api/bat/send",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            const btnValidate = document.createElement("button");
            btnValidate.className = "bat-status-badge bat-validated" + (status.validatedAt ? " active" : "");
            btnValidate.innerHTML = status.validatedAt ? `VALIDÉ ${formatDateTime(status.validatedAt)}` : "VALIDER";
            btnValidate.onclick = (e) => { e.stopPropagation(); delete _columnCache[cacheKey]; fetch("/api/bat/validate",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            const btnReject = document.createElement("button");
            btnReject.className = "bat-status-badge bat-rejected" + (status.rejectedAt ? " active" : "");
            btnReject.innerHTML = status.rejectedAt ? `REFUSÉ ${formatDateTime(status.rejectedAt)}` : "REFUSER";
            btnReject.onclick = (e) => { e.stopPropagation(); delete _columnCache[cacheKey]; fetch("/api/bat/reject",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({fullPath:full})}).then(()=>refreshKanban()); };

            batTracking.appendChild(btnSent);
            batTracking.appendChild(btnValidate);
            batTracking.appendChild(btnReject);

            if (status.sentAt && !status.validatedAt && !status.rejectedAt) {
              const sentDate = new Date(status.sentAt);
              const now = new Date();
              const diffDays = (now - sentDate) / (1000 * 60 * 60 * 24);
              if (diffDays >= 2) {
                const alertJ2 = document.createElement("div");
                alertJ2.className = "bat-alert-j2";
                alertJ2.textContent = "⚠️ BAT envoyé depuis plus de 2 jours !";
                batTracking.appendChild(alertJ2);
              }
            }
          }).catch(() => { batTracking.innerHTML = ""; });
        card.appendChild(batTracking);
      }

      if (!readOnly) {
        card.addEventListener("dragstart", (e) => {
          e.dataTransfer.effectAllowed = "move";
          e.dataTransfer.setData("text/plain", card.dataset.fullPath);
        });
      }

      drop.appendChild(card);
    }

    const counterEl = col.querySelector(".kanban-col-counter");
    if (counterEl) counterEl.textContent = filtered.length;
  } catch (err) {
    console.error("Erreur refresh kanban operator:", err);
  }
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
