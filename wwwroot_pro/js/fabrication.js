// fabrication.js — Fiche de fabrication
import { authToken, deliveriesByPath, fnKey, normalizePath, showNotification, FIN_PROD_FOLDER } from './core.js';
import { calendar, submissionCalendar } from './calendar.js';

// ======================================================
// RÉFÉRENCES DOM
// ======================================================
const fabModal = document.getElementById("fab-modal");
const fabClose = document.getElementById("fab-close");
const fabSave = document.getElementById("fab-save");
const fabPdf = document.getElementById("fab-pdf");
const fabFinProd = document.getElementById("fab-finprod");
const fabPrisma = document.getElementById("fab-prisma");

const fabMoteur = document.getElementById("fab-moteur");
const fabOperateur = document.getElementById("fab-operateur");
const fabQuantite = document.getElementById("fab-quantite");
const fabType = document.getElementById("fab-type");
const fabFormat = document.getElementById("fab-format");
const fabRectoVerso = document.getElementById("fab-recto-verso");
const fabFormeDecoupe = document.getElementById("fab-forme-decoupe");
const fabBat = document.getElementById("fab-bat");
const fabRetraitLivraison = document.getElementById("fab-retrait-livraison");
const fabAdresseLivraison = document.getElementById("fab-adresse-livraison");
const fabClient = document.getElementById("fab-client");
const fabNumeroDossier = document.getElementById("fab-numero-dossier");
const fabNotes = document.getElementById("fab-notes");
const fabDelai = document.getElementById("fab-delai");
const fabFaconnageContainer = document.getElementById("fab-faconnage-container");
const fabMedia1 = document.getElementById("fab-media1");
const fabMedia2 = document.getElementById("fab-media2");
const fabMedia3 = document.getElementById("fab-media3");
const fabMedia4 = document.getElementById("fab-media4");
const fabHistory = document.getElementById("fab-history");
const fabRemove = document.getElementById("fab-delivery-remove");
const fabStageBanner = document.getElementById("fab-stage-banner");

export let fabCurrentPath = null;

// ======================================================
// CLIENT-SIDE CACHE (TTL: 5 minutes)
// ======================================================
const _fabCache = {};
const FAB_CACHE_TTL = 5 * 60 * 1000;

async function fetchCached(url) {
  const now = Date.now();
  if (_fabCache[url] && now - _fabCache[url].ts < FAB_CACHE_TTL) {
    return _fabCache[url].data;
  }
  const data = await fetch(url).then(r => r.json()).catch(() => []);
  _fabCache[url] = { data, ts: now };
  return data;
}

// ======================================================
// INIT ÉVÉNEMENTS
// ======================================================
export function initFabrication() {
  fabClose.onclick = () => fabModal.classList.add("hidden");
  document.addEventListener("keydown", e => { if (e.key === "Escape") fabModal.classList.add("hidden"); });

  fabSave.onclick = async () => {
    if (!fabCurrentPath) return;

    // Validate required fields
    let hasError = false;
    if (!fabNumeroDossier || !fabNumeroDossier.value.trim()) {
      if (fabNumeroDossier) {
        fabNumeroDossier.style.borderColor = "#ef4444";
        fabNumeroDossier.style.boxShadow = "0 0 0 3px rgba(239,68,68,0.2)";
      }
      hasError = true;
    } else {
      if (fabNumeroDossier) { fabNumeroDossier.style.borderColor = ""; fabNumeroDossier.style.boxShadow = ""; }
    }
    if (!fabType || !fabType.value) {
      if (fabType) {
        fabType.style.borderColor = "#ef4444";
        fabType.style.boxShadow = "0 0 0 3px rgba(239,68,68,0.2)";
      }
      hasError = true;
    } else {
      if (fabType) { fabType.style.borderColor = ""; fabType.style.boxShadow = ""; }
    }
    if (hasError) {
      showNotification("❌ Numéro de dossier et Type de travail sont obligatoires", "error");
      return;
    }

    const ok = await saveFabrication();
    if (ok) {
      fabModal.classList.add("hidden");
      showNotification("✅ Fiche enregistrée", "success");
    }
  };

  fabPdf.onclick = async () => {
    if (!fabCurrentPath) return;
    await saveFabrication();
    const fabCurrentFileName = fnKey(fabCurrentPath);
    try {
      const r = await fetch("/api/fabrication/pdf?fileName=" + encodeURIComponent(fabCurrentFileName) + "&save=true", {
        headers: { "Authorization": `Bearer ${authToken}` }
      });
      if (r.ok) {
        const blob = await r.blob();
        const url = URL.createObjectURL(blob);
        window.open(url, "_blank");
        showNotification("✅ PDF généré et enregistré dans le dossier de production", "success");
      } else {
        const err = await r.json().catch(() => ({}));
        showNotification("❌ Erreur : " + (err.error || "Impossible de générer le PDF"), "error");
      }
    } catch(err) {
      showNotification("❌ Erreur réseau", "error");
    }
  };

  fabFinProd.onclick = async () => {
    if (!fabCurrentPath) { alert("Erreur : chemin introuvable"); return; }
    if (!confirm("Marquer comme 'Fin de production' ?")) return;

    const moveResp = await fetch("/api/jobs/move", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ source: fabCurrentPath, destination: FIN_PROD_FOLDER, overwrite: true })
    }).then(r => r.json()).catch(() => ({ ok: false }));

    if (!moveResp.ok) { alert("Erreur : " + (moveResp.error || "")); return; }

    // Lock the fabrication sheet so calendar shows it as completed (green, non-draggable)
    const movedPath = moveResp.moved || fabCurrentPath;
    await fetch("/api/jobs/lock", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullPath: movedPath })
    }).catch(err => console.warn("[fabrication] Lock failed:", err));

    fabModal.classList.add("hidden");
    alert("Fin de production marquée");
    // Trigger kanban refresh via global callback
    if (window._refreshKanban) await window._refreshKanban();
    if (window._refreshSubmissionView) await window._refreshSubmissionView();
    // Refresh calendar to reflect the locked (completed) status
    if (typeof calendar !== "undefined" && calendar) calendar.refetchEvents();
    if (typeof submissionCalendar !== "undefined" && submissionCalendar) submissionCalendar.refetchEvents();
  };

  // Masquer le bouton PrismaPrepare (remplacé par le bouton BAT dans la tuile kanban)
  if (fabPrisma) {
    fabPrisma.style.display = "none";
  }
}

// ======================================================
// OUVERTURE DE LA FICHE
// ======================================================
export async function openFabrication(fullPath) {
  fabCurrentPath = normalizePath(fullPath);
  const fabCurrentFileName = fnKey(fabCurrentPath);

  // Show modal immediately with spinner to reduce perceived latency
  const formGrid = fabModal.querySelector(".fab-form-grid");
  if (formGrid) {
    formGrid.style.opacity = "0.5";
    formGrid.style.pointerEvents = "none";
  }
  if (fabStageBanner) fabStageBanner.style.display = "none";
  fabModal.classList.remove("hidden");

  // Parallelize all API calls at once
  const [j, engines, types, papers, faconnageOptions, stageData] = await Promise.all([
    fetch("/api/fabrication?fileName=" + encodeURIComponent(fabCurrentFileName), {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => ({})),
    fetchCached("/api/config/print-engines"),
    fetchCached("/api/config/work-types"),
    fetchCached("/api/config/paper-catalog"),
    fetch("/api/settings/faconnage-options", {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => []),
    fetch("/api/file-stage?fileName=" + encodeURIComponent(fabCurrentFileName), {
      headers: { "Authorization": `Bearer ${authToken}` }
    }).then(r => r.json()).catch(() => null)
  ]);

  const d = (j && j.ok === false) ? {} : (j || {});

  // Populate print engines
  fabMoteur.innerHTML = '<option value="">— Sélectionner —</option>';
  (Array.isArray(engines) ? engines : []).forEach(e => {
    const name = (typeof e === "object" && e !== null) ? (e.name || "") : String(e || "");
    const opt = document.createElement("option");
    opt.value = name;
    opt.textContent = name;
    fabMoteur.appendChild(opt);
  });

  // Populate work types
  fabType.innerHTML = '<option value="">— Sélectionner —</option>';
  (Array.isArray(types) ? types : []).forEach(t => {
    const opt = document.createElement("option");
    opt.value = t;
    opt.textContent = t;
    fabType.appendChild(opt);
  });

  // Populate paper catalog
  const paperHtml = '<option value="">— Sélectionner —</option>' +
    (Array.isArray(papers) ? papers : []).map(p => `<option value="${p}">${p}</option>`).join("");
  [fabMedia1, fabMedia2, fabMedia3, fabMedia4].forEach(sel => {
    if (sel) sel.innerHTML = paperHtml;
  });

  fabMoteur.value = d.moteurImpression || d.machine || "";
  fabOperateur.value = d.operateur || "";
  fabQuantite.value = d.quantite || "";
  fabType.value = d.typeTravail || "";
  if (fabType) { fabType.style.borderColor = ""; fabType.style.boxShadow = ""; }
  fabFormat.value = d.format || "";
  fabRectoVerso.value = d.rectoVerso || "";
  if (fabFormeDecoupe) fabFormeDecoupe.value = d.formeDecoupe || "";
  if (fabBat) fabBat.value = d.bat || "";
  if (fabRetraitLivraison) fabRetraitLivraison.value = d.retraitLivraison || "";
  if (fabAdresseLivraison) fabAdresseLivraison.value = d.adresseLivraison || "";
  fabClient.value = d.client || "";
  if (fabNumeroDossier) { fabNumeroDossier.value = d.numeroDossier || ""; fabNumeroDossier.style.borderColor = ""; fabNumeroDossier.style.boxShadow = ""; }
  fabNotes.value = d.notes || "";

  if (fabMedia1) fabMedia1.value = d.media1 || "";
  if (fabMedia2) fabMedia2.value = d.media2 || "";
  if (fabMedia3) fabMedia3.value = d.media3 || "";
  if (fabMedia4) fabMedia4.value = d.media4 || "";

  // Façonnage checkboxes
  if (fabFaconnageContainer) {
    let checked = [];
    if (Array.isArray(d.faconnage)) {
      checked = d.faconnage;
    } else if (typeof d.faconnage === 'string' && d.faconnage.startsWith('[')) {
      try { checked = JSON.parse(d.faconnage); } catch(e) { /* ignore */ }
    }
    const opts = Array.isArray(faconnageOptions) ? faconnageOptions : [];
    if (opts.length === 0) {
      fabFaconnageContainer.innerHTML = '<span style="color:#9ca3af;font-size:12px;">Aucune option — importer un CSV dans Paramétrage &gt; Façonnage</span>';
    } else {
      fabFaconnageContainer.innerHTML = "";
      opts.forEach(opt => {
        const label = document.createElement("label");
        label.style.cssText = "display:inline-flex;align-items:center;gap:5px;padding:4px 10px;background:#f3f4f6;border-radius:6px;font-size:13px;cursor:pointer;border:1px solid #e5e7eb;";
        const cb = document.createElement("input");
        cb.type = "checkbox";
        cb.className = "fab-faconnage-cb";
        cb.value = opt;
        cb.checked = checked.includes(opt);
        label.appendChild(cb);
        label.appendChild(document.createTextNode(opt));
        fabFaconnageContainer.appendChild(label);
      });
    }
  }

  const deliveryDate = deliveriesByPath[fabCurrentFileName];
  if (d.delai) {
    fabDelai.value = new Date(d.delai).toISOString().split("T")[0];
  } else if (deliveryDate) {
    fabDelai.value = deliveryDate;
  } else {
    fabDelai.value = "";
  }

  fabHistory.innerHTML = "";
  (d.history || []).forEach(h => {
    const div = document.createElement("div");
    div.textContent = `${new Date(h.date).toLocaleDateString("fr-FR", {day:"2-digit",month:"2-digit",year:"numeric",hour:"2-digit",minute:"2-digit"})} — ${h.user} — ${h.action}`;
    fabHistory.appendChild(div);
  });

  // Stage banner
  if (fabStageBanner && stageData && stageData.ok && stageData.folder) {
    fabStageBanner.textContent = "📍 Étape actuelle : " + stageData.folder;
    fabStageBanner.style.display = "block";
    if (stageData.fullPath) fabCurrentPath = normalizePath(stageData.fullPath);
  }

  fabRemove.onclick = async () => {
    if (!fabCurrentFileName) return;
    if (!confirm("Retirer du planning ?")) return;

    const resp = await fetch("/api/delivery?fileName=" + encodeURIComponent(fabCurrentFileName), { method: "DELETE" }).then(r => r.json()).catch(() => ({ ok: false }));
    if (!resp.ok) { alert("Erreur"); return; }

    delete deliveriesByPath[fabCurrentFileName];
    delete deliveriesByPath[fabCurrentFileName + "_time"];
    calendar?.refetchEvents();
    submissionCalendar?.refetchEvents();
    if (window._refreshKanban) await window._refreshKanban();
    if (window._updateGlobalAlert) window._updateGlobalAlert();
    alert("Retiré du planning");
  };

  // Restore form opacity
  if (formGrid) {
    formGrid.style.opacity = "";
    formGrid.style.pointerEvents = "";
  }
}

// ======================================================
// SAUVEGARDE DE LA FICHE
// ======================================================
export async function saveFabrication() {
  if (!fabCurrentPath) return false;
  const fileName = fnKey(fabCurrentPath);

  const payload = {
    fullPath: fabCurrentPath,
    fileName: fileName,
    moteurImpression: fabMoteur.value,
    machine: fabMoteur.value,
    operateur: fabOperateur ? fabOperateur.value || null : null,
    quantite: parseInt(fabQuantite.value) || null,
    typeTravail: fabType.value,
    format: fabFormat.value,
    rectoVerso: fabRectoVerso.value,
    formeDecoupe: fabFormeDecoupe ? fabFormeDecoupe.value || null : null,
    bat: fabBat ? fabBat.value || null : null,
    retraitLivraison: fabRetraitLivraison ? fabRetraitLivraison.value || null : null,
    adresseLivraison: fabAdresseLivraison ? fabAdresseLivraison.value || null : null,
    client: fabClient.value,
    numeroDossier: fabNumeroDossier ? fabNumeroDossier.value || null : null,
    notes: fabNotes.value,
    faconnage: fabFaconnageContainer
      ? Array.from(fabFaconnageContainer.querySelectorAll('.fab-faconnage-cb:checked')).map(cb => cb.value)
      : [],
    delai: fabDelai.value || null,
    media1: fabMedia1 ? fabMedia1.value || null : null,
    media2: fabMedia2 ? fabMedia2.value || null : null,
    media3: fabMedia3 ? fabMedia3.value || null : null,
    media4: fabMedia4 ? fabMedia4.value || null : null
  };

  const r = await fetch("/api/fabrication", {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
    body: JSON.stringify(payload)
  }).then(r => r.json());

  if (!r.ok) {
    alert("Erreur : " + r.error);
    return false;
  }
  return true;
}
