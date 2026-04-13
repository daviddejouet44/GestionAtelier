// kanban/kanban-cards.js — Kanban card rendering
import { currentUser, deliveriesByPath, assignmentsByPath, fnKey, normalizePath, daysDiffFromToday, showNotification } from '../core.js';
import { openBatChoiceModal } from '../bat.js';
import { state, refreshKanban } from './kanban-core.js';
import { openAssignDropdown, openActionsDropdown } from './kanban-actions.js';

// Returns true if the action should be visible for the given folder
function isActionVisible(folderName, actionId) {
  const allowed = state.visibleActionsMap[folderName];
  if (!allowed) return true; // null = show all (retrocompat)
  return allowed.includes(actionId);
}

// ======================================================
// COLONNE KANBAN (opérateur)
// ======================================================
export async function refreshKanbanColumnOperator(folderName, q, sort, col, readOnly = false, folderPath = null) {
  try {
    const jobsUrl = folderPath
      ? `/api/jobs?folder=${encodeURIComponent(folderName)}&folderPath=${encodeURIComponent(folderPath)}`
      : `/api/jobs?folder=${encodeURIComponent(folderName)}`;
    const jobs = await fetch(jobsUrl)
      .then(r => r.json())
      .catch(() => []);

    const fingerprint = JSON.stringify(jobs.map(j => {
      const fn = fnKey(j.fullPath || j.name || '');
      return (j.name || '') + '|' + j.modified + '|' + j.size
        + '|' + ((assignmentsByPath[fn] || {}).operatorName || '')
        + '|' + (deliveriesByPath[fn] || '');
    })) + '|' + state.dateFilter + '|' + (state.operatorFilter || 'all');
    const cacheKey = folderName + '|' + q + '|' + sort;
    if (state.columnCache[cacheKey] === fingerprint) return;
    state.columnCache[cacheKey] = fingerprint;

    const drop = col.querySelector(".kanban-col-operator__drop");
    drop.innerHTML = "";

    let filtered = jobs;

    // Text search filter
    if (q) {
      filtered = filtered.filter(j => (j.name || "").toLowerCase().includes(q.toLowerCase()));
    }

    // Date filter — only show jobs whose deliveryDate matches selected date
    if (state.dateFilter) {
      filtered = filtered.filter(j => {
        const fn = fnKey(j.fullPath || j.name || '');
        const iso = deliveriesByPath[fn];
        return iso && iso === state.dateFilter;
      });
    }

    // Operator filter
    if (state.operatorFilter && state.operatorFilter !== "all") {
      filtered = filtered.filter(j => {
        const fn = fnKey(j.fullPath || j.name || '');
        const asgn = assignmentsByPath[fn];
        if (!asgn) return false;
        if (state.operatorFilter === "mine") {
          // Only match if current user has a non-empty identity
          const myId = currentUser?.id || "";
          const myLogin = currentUser?.login || "";
          const myName = currentUser?.name || "";
          if (!myId && !myLogin && !myName) return false;
          return (myId && asgn.operatorId === myId)
            || (myLogin && asgn.operatorId === myLogin)
            || (myName && asgn.operatorName === myName)
            || (myLogin && asgn.operatorName === myLogin);
        }
        return asgn.operatorId === state.operatorFilter;
      });
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
      const jobFileName = fnKey(full);
      const assignment = assignmentsByPath[jobFileName];
      const iso = deliveriesByPath[jobFileName];

      // Card layout: vignette left, center info, right delivery+operator
      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        if (window._renderPdfThumbnail) window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      // Center: dossier number (big) + PDF name + import date
      const centerDiv = document.createElement("div");
      centerDiv.style.cssText = "flex: 1; min-width: 0;";

      const dossierEl = document.createElement("div");
      dossierEl.className = "kanban-card-dossier";
      dossierEl.textContent = "—";
      centerDiv.appendChild(dossierEl);

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      centerDiv.appendChild(title);

      const sub = document.createElement("p");
      sub.className = "kanban-card-operator-info";
      sub.textContent = new Date(job.modified).toLocaleDateString("fr-FR");
      centerDiv.appendChild(sub);

      layout.appendChild(centerDiv);

      // Right: delivery date + operator
      if (iso || assignment) {
        const rightDiv = document.createElement("div");
        rightDiv.className = "kanban-card-operator-right";

        if (iso) {
          const deliveryEl = document.createElement("div");
          deliveryEl.className = "kanban-card-operator-status";
          const daysLeft = daysDiffFromToday(iso);
          if (daysLeft <= 1) deliveryEl.classList.add("urgent");
          else if (daysLeft <= 3) deliveryEl.classList.add("warning");
          deliveryEl.textContent = new Date(iso).toLocaleDateString("fr-FR");
          rightDiv.appendChild(deliveryEl);
        }

        if (assignment) {
          const badge = document.createElement("div");
          badge.className = "assignment-badge";
          badge.textContent = assignment.operatorName;
          rightDiv.appendChild(badge);
        }

        layout.appendChild(rightDiv);
      }

      card.appendChild(layout);

      // Load dossier number and press name asynchronously
      fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName))
        .then(r => r.json()).then(d => {
          if (d && d.numeroDossier) dossierEl.textContent = d.numeroDossier;
          if (d && d.moteurImpression) {
            const pressEl = document.createElement("p");
            pressEl.style.cssText = "margin:2px 0 0;font-size:11px;color:#6b7280;";
            pressEl.textContent = d.moteurImpression;
            centerDiv.appendChild(pressEl);
          }
        }).catch(() => {});

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

      if (folderName === "Début de production") {
        if (isActionVisible(folderName, "ouvrirFichier")) actions.appendChild(btnOpen);
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        const btnPlan = document.createElement("button");
        btnPlan.className = "btn btn-sm";
        btnPlan.textContent = "📅 Planifier";
        btnPlan.onclick = () => { if (window._openPlanificationCalendar) window._openPlanificationCalendar(full); };
        if (isActionVisible(folderName, "planifier")) actions.appendChild(btnPlan);

        // Bouton Preflight conditionnel — visible uniquement si les tuiles Preflight sont masquées
        if (state.preflightColumnsHidden) {
          const btnPreflightDirect = document.createElement("button");
          btnPreflightDirect.className = "btn btn-sm btn-primary";
          btnPreflightDirect.textContent = "▶ Preflight ▾";
          btnPreflightDirect.title = "Lancer le Preflight avec le droplet de votre choix";
          btnPreflightDirect.onclick = async (e) => {
            e.stopPropagation();
            document.querySelectorAll(".preflight-direct-dropdown").forEach(d => d.remove());

            // Fetch available droplets
            let droplets = [];
            try {
              const dr = await fetch("/api/config/preflight/droplets").then(r => r.json()).catch(() => null);
              if (dr && dr.ok && Array.isArray(dr.droplets)) droplets = dr.droplets;
            } catch(ex) { /* ignore */ }

            if (droplets.length === 0) {
              showNotification("❌ Aucun droplet configuré. Configurez-les dans Paramétrage > Preflight.", "error");
              return;
            }

            const dropdown = document.createElement("div");
            dropdown.className = "preflight-direct-dropdown";
            dropdown.style.cssText = "position:fixed;background:white;border:1px solid #e5e7eb;border-radius:10px;box-shadow:0 8px 24px rgba(0,0,0,0.18);z-index:9999;min-width:200px;overflow:hidden;padding:4px 0;";

            droplets.forEach(dp => {
              const item = document.createElement("div");
              item.style.cssText = "padding:10px 16px;cursor:pointer;font-size:13px;color:#111827;transition:background 0.15s;white-space:nowrap;";
              item.textContent = dp.name || dp.path;
              item.onmouseenter = () => item.style.background = "#f3f4f6";
              item.onmouseleave = () => item.style.background = "";
              item.onclick = async () => {
                dropdown.remove();
                const fileName = full.split(/[\\/]/).pop();
                btnPreflightDirect.disabled = true;
                btnPreflightDirect.textContent = "⏳ Preflight...";
                showNotification(`⏳ Preflight en cours pour ${fileName}...`, "info");
                try {
                  const r = await fetch("/api/acrobat/preflight", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ fullPath: full, dropletPath: dp.path })
                  }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
                  if (r.ok) {
                    showNotification(`✅ Preflight terminé — ${fileName} déplacé vers Prêt pour impression`, "success");
                    await refreshKanban();
                  } else {
                    showNotification("❌ Preflight : " + (r.error || "Erreur inconnue"), "error");
                    btnPreflightDirect.disabled = false;
                    btnPreflightDirect.textContent = "▶ Preflight ▾";
                  }
                } catch (err) {
                  showNotification("❌ Preflight : " + err.message, "error");
                  btnPreflightDirect.disabled = false;
                  btnPreflightDirect.textContent = "▶ Preflight ▾";
                }
              };
              dropdown.appendChild(item);
            });

            document.body.appendChild(dropdown);
            const rect = btnPreflightDirect.getBoundingClientRect();
            const dropW = 200;
            let left = rect.left + window.scrollX;
            if (left + dropW > window.innerWidth) left = window.innerWidth - dropW - 8;
            dropdown.style.top = (rect.bottom + window.scrollY + 4) + "px";
            dropdown.style.left = left + "px";

            setTimeout(() => {
              document.addEventListener("click", function closePfDropdown(ev) {
                if (!dropdown.contains(ev.target)) {
                  dropdown.remove();
                  document.removeEventListener("click", closePfDropdown);
                }
              });
            }, 10);
          };
          if (isActionVisible(folderName, "preflight")) actions.appendChild(btnPreflightDirect);
        }

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Prêt pour impression") {
        if (isActionVisible(folderName, "ouvrirFichier")) actions.appendChild(btnOpen);
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        // Planning button — opens the planning dialog
        const btnPlan = document.createElement("button");
        btnPlan.className = "btn btn-sm";
        btnPlan.textContent = "📅 Planifier";
        btnPlan.onclick = () => { if (window._openPlanificationCalendar) window._openPlanificationCalendar(full); };
        if (isActionVisible(folderName, "planifier")) actions.appendChild(btnPlan);

        // BAT button — ouvre popup BAT complet / BAT simple
        const btnBAT = document.createElement("button");
        btnBAT.className = "btn btn-sm btn-primary";
        btnBAT.innerHTML = "→ BAT";
        btnBAT.onclick = () => {
          openBatChoiceModal(full, async () => {
            await refreshKanban();
          });
        };
        if (isActionVisible(folderName, "bat")) actions.appendChild(btnBAT);

        const btnPrint = document.createElement("button");
        btnPrint.className = "btn btn-sm btn-primary";
        btnPrint.innerHTML = "Actions ▾";
        btnPrint.onclick = (e) => { e.stopPropagation(); openActionsDropdown(btnPrint, full); };
        if (isActionVisible(folderName, "actions")) actions.appendChild(btnPrint);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Corrections" || folderName === "Corrections et fond perdu") {
        if (isActionVisible(folderName, "ouvrirFichier")) actions.appendChild(btnOpen);
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        // Bouton Preflight automatique
        const btnPreflight = document.createElement("button");
        btnPreflight.className = "btn btn-sm btn-primary";
        btnPreflight.textContent = "▶ Preflight";
        btnPreflight.title = "Lancer le Preflight en arrière-plan et déplacer vers Prêt pour impression";
        btnPreflight.onclick = async (e) => {
          e.stopPropagation();
          const fileName = full.split(/[\\/]/).pop();
          btnPreflight.disabled = true;
          btnPreflight.textContent = "⏳ Preflight...";
          showNotification(`⏳ Preflight en cours pour ${fileName}...`, "info");
          try {
            const r = await fetch("/api/acrobat/preflight", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full, folder: folderName })
            }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
            if (r.ok) {
              showNotification(`✅ Preflight terminé — ${fileName} déplacé vers Prêt pour impression`, "success");
              await refreshKanban();
            } else {
              showNotification("❌ Preflight : " + (r.error || "Erreur inconnue"), "error");
              btnPreflight.disabled = false;
              btnPreflight.textContent = "▶ Preflight";
            }
          } catch (err) {
            showNotification("❌ Preflight : " + err.message, "error");
            btnPreflight.disabled = false;
            btnPreflight.textContent = "▶ Preflight";
          }
        };
        if (isActionVisible(folderName, "preflight")) actions.appendChild(btnPreflight);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "PrismaPrepare") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        // BAT button — ouvre popup BAT complet / BAT simple
        const btnBATprisma = document.createElement("button");
        btnBATprisma.className = "btn btn-sm btn-primary";
        btnBATprisma.innerHTML = "→ BAT";
        btnBATprisma.onclick = () => {
          openBatChoiceModal(full, async () => {
            await refreshKanban();
          });
        };
        if (isActionVisible(folderName, "bat")) actions.appendChild(btnBATprisma);

        const btnPrisma = document.createElement("button");
        btnPrisma.className = "btn btn-sm btn-primary";
        btnPrisma.textContent = "Ouvrir dans PrismaPrepare";
        btnPrisma.onclick = async (e) => {
          e.stopPropagation();
          const r = await fetch("/api/jobs/open-in-prismaprepare", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: full })
          }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
          if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        if (isActionVisible(folderName, "prismaPrepare")) actions.appendChild(btnPrisma);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnImpressionLancee = document.createElement("button");
          btnImpressionLancee.className = "btn btn-sm btn-primary";
          btnImpressionLancee.textContent = "▶ Impression lancée";
          btnImpressionLancee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Impression en cours", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Impression en cours", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "impressionLancee")) actions.appendChild(btnImpressionLancee);
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Fiery") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        const btnFiery = document.createElement("button");
        btnFiery.className = "btn btn-sm btn-primary";
        btnFiery.textContent = "Ouvrir dans Fiery";
        btnFiery.onclick = async (e) => {
          e.stopPropagation();
          const r = await fetch("/api/jobs/open-in-fiery", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ fullPath: full })
          }).then(res => res.json()).catch(() => ({ ok: false, error: "Erreur réseau" }));
          if (!r.ok) showNotification("❌ " + (r.error || "Erreur"), "error");
        };
        if (isActionVisible(folderName, "fiery")) actions.appendChild(btnFiery);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnLancerImpression = document.createElement("button");
          btnLancerImpression.className = "btn btn-sm btn-primary";
          btnLancerImpression.textContent = "▶ Lancer l'impression";
          btnLancerImpression.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Impression en cours", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Impression lancée", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "impressionLancee")) actions.appendChild(btnLancerImpression);
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Impression en cours") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnImpTerminee = document.createElement("button");
          btnImpTerminee.className = "btn btn-sm btn-primary";
          btnImpTerminee.textContent = "✅ Impression terminée";
          btnImpTerminee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Façonnage", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Façonnage", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "impressionTerminee")) actions.appendChild(btnImpTerminee);
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Façonnage") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        // Façonnage badges — loaded asynchronously
        const badgesDiv = document.createElement("div");
        badgesDiv.style.cssText = "display:flex;flex-wrap:wrap;gap:4px;margin-top:4px;";
        card.appendChild(badgesDiv);
        const jfn = jobFileName;
        fetch("/api/fabrication?fileName=" + encodeURIComponent(jfn))
          .then(r => r.json()).then(d => {
            if (Array.isArray(d.faconnage)) {
              d.faconnage.forEach(opt => {
                const badge = document.createElement("span");
                badge.style.cssText = "background:#fef9c3;color:#92400e;border:1px solid #fde68a;border-radius:4px;padding:1px 6px;font-size:10px;font-weight:600;";
                badge.textContent = opt;
                badgesDiv.appendChild(badge);
              });
            }
          }).catch(() => {});

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3 || currentUser.profile === 4)) {
          const btnTerminee = document.createElement("button");
          btnTerminee.className = "btn btn-sm btn-primary";
          btnTerminee.textContent = "✅ Terminée";
          btnTerminee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Fin de production", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Fin de production", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "faconnageTermine")) actions.appendChild(btnTerminee);
        }
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Fin de production") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          const btnTermine = document.createElement("button");
          btnTermine.className = "btn btn-sm btn-primary";
          btnTermine.textContent = "🔒 Terminé";
          btnTermine.title = "Verrouille le fichier et marque la tâche comme terminée (vert dans le calendrier)";
          btnTermine.onclick = async (e) => {
            e.stopPropagation();
            if (!confirm("Marquer comme terminé et verrouiller ce fichier ?")) return;
            const r = await fetch("/api/jobs/lock", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) {
              showNotification("✅ Fichier verrouillé — tâche terminée", "success");
              card.draggable = false;
              btnTermine.disabled = true;
              btnTermine.textContent = "🔒 Verrouillé";
              if (window._calendar) window._calendar.refetchEvents();
            } else {
              showNotification("❌ " + (r.error || "Erreur"), "error");
            }
          };
          if (isActionVisible(folderName, "verrouiller")) actions.appendChild(btnTermine);

          const btnArchiver = document.createElement("button");
          btnArchiver.className = "btn btn-sm";
          btnArchiver.textContent = "📦 Archiver";
          btnArchiver.onclick = async (e) => {
            e.stopPropagation();
            if (!confirm("Archiver ce fichier dans le dossier de production ?")) return;
            const r = await fetch("/api/jobs/archive", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Archivé", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "archiver")) actions.appendChild(btnArchiver);
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else {
        if (isActionVisible(folderName, "ouvrirFichier")) actions.appendChild(btnOpen);
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);
        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3)) {
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      }

      card.appendChild(actions);

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
