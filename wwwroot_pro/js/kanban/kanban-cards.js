// kanban/kanban-cards.js — Kanban card rendering
import { currentUser, authToken, deliveriesByPath, assignmentsByPath, fnKey, normalizePath, daysDiffFromToday, showNotification } from '../core.js';
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

      // Top row: dossier N° + presse (loaded async below)
      const topRow = document.createElement("div");
      topRow.className = "kanban-card-top-row";

      const dossierEl = document.createElement("div");
      dossierEl.className = "kanban-card-dossier";
      dossierEl.textContent = "—";
      topRow.appendChild(dossierEl);

      const presseEl = document.createElement("div");
      presseEl.className = "kanban-card-presse";
      presseEl.textContent = "";
      topRow.appendChild(presseEl);

      card.appendChild(topRow);

      // Main row: thumbnail + info stack
      const layout = document.createElement("div");
      layout.className = "kanban-card-operator-layout";

      const thumbDiv = document.createElement("div");
      thumbDiv.className = "kanban-card-operator-thumb";
      thumbDiv.textContent = "PDF";
      layout.appendChild(thumbDiv);
      if ((job.name || "").toLowerCase().endsWith(".pdf")) {
        if (window._renderPdfThumbnail) window._renderPdfThumbnail(full, thumbDiv).catch(() => {});
      }

      // Right info stack
      const infoStack = document.createElement("div");
      infoStack.className = "kanban-card-info-stack";

      const title = document.createElement("p");
      title.className = "kanban-card-operator-title";
      title.textContent = job.name || "Sans nom";
      infoStack.appendChild(title);

      // Operator row (filled async or from assignment)
      const operatorEl = document.createElement("p");
      operatorEl.className = "kanban-card-operator-info";
      operatorEl.textContent = assignment ? assignment.operatorName : "";
      infoStack.appendChild(operatorEl);

      // "Sur machine le" row (from fabrication.planningMachine, loaded async)
      const machineEl = document.createElement("p");
      machineEl.className = "kanban-card-operator-info";
      infoStack.appendChild(machineEl);

      // "Livraison le" row
      const livraisonEl = document.createElement("p");
      livraisonEl.className = "kanban-card-operator-info";
      if (iso) {
        const daysLeft = daysDiffFromToday(iso);
        const livrText = new Date(iso).toLocaleDateString("fr-FR");
        livraisonEl.textContent = "Livraison: " + livrText;
        if (daysLeft <= 1) livraisonEl.style.cssText = "color:#991B1B;font-weight:700;";
        else if (daysLeft <= 3) livraisonEl.style.cssText = "color:#9A3412;font-weight:600;";
      }
      infoStack.appendChild(livraisonEl);

      layout.appendChild(infoStack);
      card.appendChild(layout);

      // Load dossier number, presse and planningMachine asynchronously
      fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName))
        .then(r => r.json()).then(d => {
          if (d && d.numeroDossier) dossierEl.textContent = "N° " + d.numeroDossier;
          if (d && d.moteurImpression) presseEl.textContent = d.moteurImpression;
          if (d && d.planningMachine) {
            const dt = new Date(d.planningMachine);
            machineEl.textContent = "Machine: " + dt.toLocaleDateString("fr-FR");
          }
          if (d && d.operateur && !assignment) {
            operatorEl.textContent = d.operateur;
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
          // Mail début de production
          const btnMailDebut = document.createElement("button");
          btnMailDebut.className = "btn btn-sm";
          btnMailDebut.textContent = "✉️ Mail début";
          btnMailDebut.title = "Envoyer mail de début de production";
          btnMailDebut.onclick = async (e) => {
            e.stopPropagation();
            try {
              const [tmplResp, fabResp] = await Promise.all([
                fetch("/api/config/mail-template-production-start").then(r => r.json()).catch(() => ({})),
                fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName)).then(r => r.json()).catch(() => ({}))
              ]);
              const tmpl = (tmplResp.ok && tmplResp.template) ? tmplResp.template : null;
              const fab = fabResp || {};
              if (tmpl && (tmpl.subject || tmpl.body)) {
                const rv = (s) => (s || '')
                  .replace(/\{\{numeroDossier\}\}/g, fab.numeroDossier || '')
                  .replace(/\{\{nomClient\}\}/g, fab.nomClient || '')
                  .replace(/\{\{nomFichier\}\}/g, job.name || '')
                  .replace(/\{\{typeTravail\}\}/g, fab.typeTravail || '')
                  .replace(/\{\{quantite\}\}/g, fab.quantite || '')
                  .replace(/\{\{operateur\}\}/g, fab.operateur || '')
                  .replace(/\{\{dateLivraison\}\}/g, fab.dateLivraison ? new Date(fab.dateLivraison).toLocaleDateString('fr-FR') : '');
                const to = tmpl.to || fab.mailClient || '';
                window.open(`mailto:${to}?subject=${encodeURIComponent(rv(tmpl.subject))}&body=${encodeURIComponent(rv(tmpl.body))}`);
              } else {
                showNotification("⚠️ Configurez le template 'Mail début de production' dans Paramétrage > Configuration BAT", "warning");
              }
            } catch(err) { showNotification("❌ " + err.message, "error"); }
          };
          if (isActionVisible(folderName, "mailDebutProduction")) actions.appendChild(btnMailDebut);

          // Mail fin de production
          const btnMailFin = document.createElement("button");
          btnMailFin.className = "btn btn-sm";
          btnMailFin.textContent = "✉️ Mail fin";
          btnMailFin.title = "Envoyer mail de fin de production";
          btnMailFin.onclick = async (e) => {
            e.stopPropagation();
            try {
              const [tmplResp, fabResp] = await Promise.all([
                fetch("/api/config/mail-template-production-end").then(r => r.json()).catch(() => ({})),
                fetch("/api/fabrication?fileName=" + encodeURIComponent(jobFileName)).then(r => r.json()).catch(() => ({}))
              ]);
              const tmpl = (tmplResp.ok && tmplResp.template) ? tmplResp.template : null;
              const fab = fabResp || {};
              if (tmpl && (tmpl.subject || tmpl.body)) {
                const rv = (s) => (s || '')
                  .replace(/\{\{numeroDossier\}\}/g, fab.numeroDossier || '')
                  .replace(/\{\{nomClient\}\}/g, fab.nomClient || '')
                  .replace(/\{\{nomFichier\}\}/g, job.name || '')
                  .replace(/\{\{typeTravail\}\}/g, fab.typeTravail || '')
                  .replace(/\{\{quantite\}\}/g, fab.quantite || '')
                  .replace(/\{\{operateur\}\}/g, fab.operateur || '')
                  .replace(/\{\{dateLivraison\}\}/g, fab.dateLivraison ? new Date(fab.dateLivraison).toLocaleDateString('fr-FR') : '');
                const to = tmpl.to || fab.mailClient || '';
                window.open(`mailto:${to}?subject=${encodeURIComponent(rv(tmpl.subject))}&body=${encodeURIComponent(rv(tmpl.body))}`);
              } else {
                showNotification("⚠️ Configurez le template 'Mail fin de production' dans Paramétrage > Configuration BAT", "warning");
              }
            } catch(err) { showNotification("❌ " + err.message, "error"); }
          };
          if (isActionVisible(folderName, "mailFinProduction")) actions.appendChild(btnMailFin);

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
            if (r.ok) { showNotification("✅ Déplacé vers Finitions", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Erreur"), "error");
          };
          if (isActionVisible(folderName, "impressionTerminee")) actions.appendChild(btnImpTerminee);
          if (isActionVisible(folderName, "supprimer")) actions.appendChild(btnDelete);
        }
      } else if (folderName === "Façonnage") {
        if (isActionVisible(folderName, "fiche")) actions.appendChild(btnFiche);
        if (isActionVisible(folderName, "affecter")) actions.appendChild(btnAssign);

        // Finitions steps panel — loaded asynchronously
        const finitionsPanel = document.createElement("div");
        finitionsPanel.style.cssText = "margin-top:8px;border:1px solid #e5e7eb;border-radius:8px;background:#fafafa;padding:8px;";
        card.appendChild(finitionsPanel);

        const STEP_LABELS = {
          embellissement: "✨ Embellissement",
          rainage:        "📐 Rainage",
          pliage:         "📄 Pliage",
          faconnage:      "📚 Façonnage (reliure)",
          coupe:          "✂️ Coupe",
          emballage:      "📦 Emballage",
          depart:         "🚪 Départ",
          livraison:      "🚚 Livraison"
        };

        const jobId = encodeURIComponent(full);
        const jfn   = jobFileName;

        // Load finition steps + fabrication data in parallel
        Promise.all([
          fetch(`/api/fabrication/${jobId}/finition-steps`).then(r => r.json()).catch(() => ({ ok: false })),
          fetch("/api/fabrication?fileName=" + encodeURIComponent(jfn)).then(r => r.json()).catch(() => ({}))
        ]).then(([stepsData, fabData]) => {
          const steps = stepsData.ok ? stepsData.finitionSteps : {};

          // Determine applicable steps from fab data
          const hasEnnob  = Array.isArray(fabData.ennoblissement) && fabData.ennoblissement.length > 0;
          const hasRainage = fabData.rainage === true;
          const hasPlis   = fabData.plis && fabData.plis.trim() !== "";
          const hasFaconnage = (fabData.faconnageBinding && fabData.faconnageBinding.trim() !== "") ||
                               (Array.isArray(fabData.faconnage) && fabData.faconnage.length > 0);

          const applicable = {
            embellissement: hasEnnob,
            rainage:        hasRainage,
            pliage:         hasPlis,
            faconnage:      hasFaconnage,
            coupe:          true,
            emballage:      true,
            depart:         true,
            livraison:      true
          };

          finitionsPanel.innerHTML = '<div style="font-size:11px;font-weight:700;color:#4b5563;margin-bottom:6px;">Étapes finitions</div>';

          const allApplicableDone = Object.entries(applicable).every(([k, isApplicable]) => {
            if (!isApplicable) return true;
            return steps[k]?.done === true;
          });

          Object.keys(STEP_LABELS).forEach(key => {
            const isApplicable = applicable[key];
            const stepData = steps[key] || {};
            const isDone = isApplicable ? stepData.done === true : true; // non-applicable = auto done

            const row = document.createElement("div");
            row.style.cssText = "display:flex;align-items:flex-start;gap:6px;padding:3px 0;font-size:11px;";

            if (!isApplicable) {
              row.innerHTML = `<span style="color:#9ca3af;text-decoration:line-through;">${STEP_LABELS[key]} (N/A)</span>`;
            } else {
              const cb = document.createElement("input");
              cb.type = "checkbox";
              cb.checked = stepData.done === true;
              cb.disabled = readOnly;
              cb.style.cssText = "margin-top:2px;flex-shrink:0;cursor:pointer;";

              const labelDiv = document.createElement("div");
              labelDiv.style.cssText = "flex:1;";

              const nameSpan = document.createElement("span");
              nameSpan.style.cssText = isDone ? "color:#16a34a;font-weight:600;" : "color:#374151;";
              nameSpan.textContent = STEP_LABELS[key];
              labelDiv.appendChild(nameSpan);

              if (isDone && stepData.doneAt) {
                const ts = document.createElement("div");
                ts.style.cssText = "color:#6b7280;font-size:10px;";
                const dt = new Date(stepData.doneAt);
                ts.textContent = `${stepData.doneBy || ""} — ${dt.toLocaleDateString('fr-FR')} ${dt.toLocaleTimeString('fr-FR', {hour:'2-digit',minute:'2-digit'})}`;
                labelDiv.appendChild(ts);
              }

              // Extra fields
              if (key === "emballage" && isDone && stepData.conditionnement) {
                const cond = document.createElement("div");
                cond.style.cssText = "color:#4b5563;font-size:10px;";
                cond.textContent = `Conditionnement: ${stepData.conditionnement}`;
                labelDiv.appendChild(cond);
              }
              if (key === "livraison" && isDone && stepData.tracking) {
                const trk = document.createElement("div");
                trk.style.cssText = "color:#4b5563;font-size:10px;";
                trk.textContent = `Suivi: ${stepData.tracking}`;
                labelDiv.appendChild(trk);
              }

              if (!readOnly) {
                cb.onchange = async (e) => {
                  e.stopPropagation();
                  const newDone = cb.checked;
                  let conditionnement = null;
                  let tracking = null;

                  if (key === "emballage" && newDone) {
                    conditionnement = prompt("Conditionnement (obligatoire) :");
                    if (!conditionnement || conditionnement.trim() === "") {
                      cb.checked = false;
                      return;
                    }
                  }
                  if (key === "livraison" && newDone) {
                    tracking = prompt("Numéro de suivi (optionnel) :");
                  }

                  const body = { step: key, done: newDone };
                  if (conditionnement) body.conditionnement = conditionnement;
                  if (tracking) body.tracking = tracking;

                  const authHeader = authToken || "";
                  const r = await fetch(`/api/fabrication/${jobId}/finition-step`, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json", "Authorization": "Bearer " + authHeader },
                    body: JSON.stringify(body)
                  }).then(res => res.json()).catch(() => ({ ok: false }));

                  if (r.ok) {
                    // Reload the card
                    await refreshKanban();
                  } else {
                    cb.checked = !newDone;
                    showNotification("❌ " + (r.error || "Erreur"), "error");
                  }
                };
              }

              row.appendChild(cb);
              row.appendChild(labelDiv);
            }
            finitionsPanel.appendChild(row);
          });
        }).catch(() => {});

        if (!readOnly && (currentUser.profile === 2 || currentUser.profile === 3 || currentUser.profile === 4)) {
          const btnTerminee = document.createElement("button");
          btnTerminee.className = "btn btn-sm btn-primary";
          btnTerminee.textContent = "✅ Terminée";
          btnTerminee.title = "Déplacer vers Fin de production (toutes les étapes doivent être validées)";
          btnTerminee.onclick = async (e) => {
            e.stopPropagation();
            const r = await fetch("/api/jobs/move", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ source: full, destination: "Fin de production", overwrite: true })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) { showNotification("✅ Déplacé vers Fin de production", "success"); await refreshKanban(); }
            else showNotification("❌ " + (r.error || "Étapes de finition non validées"), "error");
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
            const r = await fetch("/api/jobs/lock", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ fullPath: full })
            }).then(res => res.json()).catch(() => ({ ok: false }));
            if (r.ok) {
              showNotification("✅ Fichier verrouillé — tâche terminée", "success");
              card.draggable = false;
              card.style.opacity = "0.6";
              card.style.filter = "grayscale(0.5)";
              card.style.pointerEvents = "none";
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
