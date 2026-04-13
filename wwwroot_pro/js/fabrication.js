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

// New fields
const fabDonneurNom    = document.getElementById("fab-donneur-nom");
const fabDonneurPrenom = document.getElementById("fab-donneur-prenom");
const fabDonneurTel    = document.getElementById("fab-donneur-tel");
const fabDonneurEmail  = document.getElementById("fab-donneur-email");
const fabPagination    = document.getElementById("fab-pagination");
const fabFormatFeuille = document.getElementById("fab-format-feuille");
const fabNombreFeuilles = document.getElementById("fab-nombre-feuilles");
const fabMedia1Fabricant = document.getElementById("fab-media1-fabricant");
const fabMedia2Fabricant = document.getElementById("fab-media2-fabricant");
const fabMedia3Fabricant = document.getElementById("fab-media3-fabricant");
const fabMedia4Fabricant = document.getElementById("fab-media4-fabricant");
const fabCouvertureSection    = document.getElementById("fab-couverture-section");
const fabCouvertureMediaRow   = document.getElementById("fab-couverture-media-row");
const fabCouvertureFabRow     = document.getElementById("fab-couverture-fab-row");
const fabMediaCouverture      = document.getElementById("fab-media-couverture");
const fabMediaCouvertureFab   = document.getElementById("fab-media-couverture-fabricant");
const fabRainage      = document.getElementById("fab-rainage");
const fabRainageLabel = document.getElementById("fab-rainage-label");
const fabEnnobContainer = document.getElementById("fab-ennoblissement-container");
const fabFaconnageBinding = document.getElementById("fab-faconnage-binding");
const fabPlis         = document.getElementById("fab-plis");
const fabSortie       = document.getElementById("fab-sortie");
const fabImportMailBat   = document.getElementById("fab-import-mail-bat");
const fabMailBatFile     = document.getElementById("fab-mail-bat-file");
const fabMailBatName     = document.getElementById("fab-mail-bat-name");
const fabImportMailDevis = document.getElementById("fab-import-mail-devis");
const fabMailDevisFile   = document.getElementById("fab-mail-devis-file");
const fabMailDevisName   = document.getElementById("fab-mail-devis-name");
const fabPassesDisplay  = document.getElementById("fab-passes-display");
const fabDateDepart     = document.getElementById("fab-date-depart");
const fabDateLivraison  = document.getElementById("fab-date-livraison");
const fabPlanningMachine = document.getElementById("fab-planning-machine");
const fabJustifsQte    = document.getElementById("fab-justifs-qte");
const fabJustifsAdresse = document.getElementById("fab-justifs-adresse");
const fabRepartitionsContainer = document.getElementById("fab-repartitions-container");
const fabRepartitionsAdd = document.getElementById("fab-repartitions-add");

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
// HELPERS — RAINAGE AUTO
// ======================================================
function updateRainageAuto() {
  if (!fabMediaCouverture || !fabRainage) return;
  const mediaName = fabMediaCouverture.value || "";
  const match = mediaName.match(/(\d+)\s*g/i);
  if (match && parseInt(match[1]) > 170) {
    fabRainage.checked = true;
    fabRainage.disabled = true;
    if (fabRainageLabel) fabRainageLabel.textContent = "Oui (auto)";
  } else {
    fabRainage.disabled = false;
    if (fabRainageLabel) fabRainageLabel.textContent = fabRainage.checked ? "Oui" : "Non";
  }
}

// ======================================================
// HELPERS — COUVERTURE CONDITIONNELLE
// ======================================================
let _coverProducts = [];
function updateCouvertureVisibility() {
  const typeTravail = fabType ? fabType.value : "";
  const show = _coverProducts.includes(typeTravail);
  if (fabCouvertureSection)  fabCouvertureSection.style.display  = show ? "" : "none";
  if (fabCouvertureMediaRow) fabCouvertureMediaRow.style.display = show ? "" : "none";
  if (fabCouvertureFabRow)   fabCouvertureFabRow.style.display   = show ? "" : "none";
}

// ======================================================
// HELPERS — NOMBRE DE FEUILLES CALCULÉ
// ======================================================
let _sheetCalcRules = {};
let _deliveryDelayHours = 48;
let _passesConfig = { faconnage: 0, pelliculageRecto: 0, pelliculageRectoVerso: 0, rainage: 0, dorure: 0, dosCarreColle: 0 };

function updateNombreFeuilles() {
  const typeTravail = fabType ? fabType.value : "";
  const quantite = parseInt(fabQuantite ? fabQuantite.value : "0") || 0;
  if (typeTravail && _sheetCalcRules[typeTravail] && quantite > 0) {
    const divisor = _sheetCalcRules[typeTravail];
    const calculated = Math.ceil(quantite / divisor);
    if (fabNombreFeuilles && !fabNombreFeuilles._manuallyEdited) {
      fabNombreFeuilles.value = calculated;
    }
  }
}

// ======================================================
// HELPERS — DATES CALCULÉES
// ======================================================
function updateDateLivraison() {
  if (!fabDateDepart || !fabDateLivraison) return;
  const depart = fabDateDepart.value;
  if (!depart) return;
  const departDate = new Date(depart);
  const livraisonDate = new Date(departDate.getTime() + _deliveryDelayHours * 3600000);
  if (!fabDateLivraison._manuallyEdited) {
    fabDateLivraison.value = livraisonDate.toISOString().split("T")[0];
  }
  updatePlanningMachine();
}

function updatePlanningMachine() {
  // planningMachine is calculated per-machine; leave editable but auto-calc if engine has delay
  // For now, planning machine can be set manually
}

// ======================================================
// HELPERS — PASSES DISPLAY
// ======================================================
function updatePassesDisplay() {
  if (!fabPassesDisplay) return;
  const ennob = getEnnoblissementSelected();
  const faconnageBinding = fabFaconnageBinding ? fabFaconnageBinding.value : "";
  const rainage = fabRainage ? fabRainage.checked : false;

  const lines = [];
  if (faconnageBinding && _passesConfig.faconnage > 0)
    lines.push(`Façonnage : +${_passesConfig.faconnage} feuilles`);
  if (ennob.some(e => e.includes("Pelliculage") && e.includes("recto/verso")) && _passesConfig.pelliculageRectoVerso > 0)
    lines.push(`Pelliculage recto/verso : +${_passesConfig.pelliculageRectoVerso} feuilles`);
  else if (ennob.some(e => e.includes("Pelliculage") && e.includes("recto")) && _passesConfig.pelliculageRecto > 0)
    lines.push(`Pelliculage recto : +${_passesConfig.pelliculageRecto} feuilles`);
  if (rainage && _passesConfig.rainage > 0)
    lines.push(`Rainage : +${_passesConfig.rainage} feuilles`);
  if (ennob.some(e => e.includes("Dorure")) && _passesConfig.dorure > 0)
    lines.push(`Dorure : +${_passesConfig.dorure} feuilles`);
  if (faconnageBinding === "Dos carré collé" && _passesConfig.dosCarreColle > 0)
    lines.push(`Dos carré collé : +${_passesConfig.dosCarreColle} exemplaires`);

  fabPassesDisplay.innerHTML = lines.length > 0
    ? lines.map(l => `<span style="display:inline-block;background:#f3f4f6;padding:3px 8px;border-radius:4px;margin:2px;font-size:12px;">${l}</span>`).join("")
    : '<span style="color:#9ca3af;font-size:12px;">Aucune passe applicable</span>';
}

function getEnnoblissementSelected() {
  if (!fabEnnobContainer) return [];
  return Array.from(fabEnnobContainer.querySelectorAll('.fab-ennob-cb:checked')).map(cb => cb.value);
}

// ======================================================
// HELPERS — RÉPARTITIONS
// ======================================================
function addRepartitionRow(quantite = "", adresse = "") {
  if (!fabRepartitionsContainer) return;
  const row = document.createElement("div");
  row.className = "fab-repartition-row";
  row.style.cssText = "display:flex;gap:8px;align-items:center;margin-bottom:6px;";
  row.innerHTML = `
    <input type="number" class="fab-rep-qte" placeholder="Quantité" value="${quantite}" style="width:100px;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" min="0" />
    <input type="text" class="fab-rep-adresse" placeholder="Adresse de répartition" value="${adresse}" style="flex:1;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />
    <button class="fab-rep-remove btn" style="padding:4px 10px;font-size:12px;color:#ef4444;">×</button>
  `;
  row.querySelector(".fab-rep-remove").onclick = () => row.remove();
  fabRepartitionsContainer.appendChild(row);
}

function getRepartitions() {
  if (!fabRepartitionsContainer) return [];
  return Array.from(fabRepartitionsContainer.querySelectorAll(".fab-repartition-row")).map(row => ({
    quantite: parseInt(row.querySelector(".fab-rep-qte").value) || null,
    adresse: row.querySelector(".fab-rep-adresse").value.trim() || null
  })).filter(r => r.quantite || r.adresse);
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
    // Small delay to ensure MongoDB write is committed before reading it back for PDF
    await new Promise(r => setTimeout(r, 300));
    const fabCurrentFileName = fnKey(fabCurrentPath);
    try {
      const r = await fetch("/api/fabrication/pdf?fileName=" + encodeURIComponent(fabCurrentFileName) + "&fullPath=" + encodeURIComponent(fabCurrentPath) + "&save=true", {
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

  // Rainage auto update when couverture media changes
  if (fabMediaCouverture) {
    fabMediaCouverture.addEventListener("change", () => {
      updateRainageAuto();
      updatePassesDisplay();
    });
  }

  // Rainage label update
  if (fabRainage) {
    fabRainage.addEventListener("change", () => {
      if (fabRainageLabel) fabRainageLabel.textContent = fabRainage.checked ? "Oui" : "Non";
      updatePassesDisplay();
    });
  }

  // Nombre de feuilles: mark as manually edited when user changes it
  if (fabNombreFeuilles) {
    fabNombreFeuilles.addEventListener("input", () => {
      fabNombreFeuilles._manuallyEdited = true;
    });
  }

  // Update nombre feuilles when type or quantite changes
  if (fabType) fabType.addEventListener("change", () => { updateNombreFeuilles(); updateCouvertureVisibility(); });
  if (fabQuantite) fabQuantite.addEventListener("input", updateNombreFeuilles);

  // Date de livraison auto calculation
  if (fabDateDepart) {
    fabDateDepart.addEventListener("change", updateDateLivraison);
  }
  if (fabDateLivraison) {
    fabDateLivraison.addEventListener("input", () => { fabDateLivraison._manuallyEdited = true; });
  }

  // Passes display update on ennoblissement/faconnage change
  if (fabEnnobContainer) {
    fabEnnobContainer.addEventListener("change", updatePassesDisplay);
  }
  if (fabFaconnageBinding) {
    fabFaconnageBinding.addEventListener("change", updatePassesDisplay);
  }

  // Import mail devis
  if (fabImportMailDevis) {
    fabImportMailDevis.onclick = () => fabMailDevisFile && fabMailDevisFile.click();
  }
  if (fabMailDevisFile) {
    fabMailDevisFile.addEventListener("change", async () => {
      const file = fabMailDevisFile.files[0];
      if (!file) return;
      const fileName = fnKey(fabCurrentPath);
      const formData = new FormData();
      formData.append("file", file);
      formData.append("fileName", fileName);
      try {
        const r = await fetch("/api/fabrication/import-mail-devis", {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` },
          body: formData
        }).then(r => r.json());
        if (r.ok) {
          if (fabMailDevisName) fabMailDevisName.textContent = file.name;
          showNotification("✅ Mail devis importé", "success");
        } else {
          showNotification("❌ " + (r.error || "Erreur d'import"), "error");
        }
      } catch(e) {
        showNotification("❌ Erreur réseau", "error");
      }
    });
  }

  // Import mail BAT
  if (fabImportMailBat) {
    fabImportMailBat.onclick = () => fabMailBatFile && fabMailBatFile.click();
  }
  if (fabMailBatFile) {
    fabMailBatFile.addEventListener("change", async () => {
      const file = fabMailBatFile.files[0];
      if (!file) return;
      const fileName = fnKey(fabCurrentPath);
      const formData = new FormData();
      formData.append("file", file);
      formData.append("fileName", fileName);
      try {
        const r = await fetch("/api/fabrication/import-mail-bat", {
          method: "POST",
          headers: { "Authorization": `Bearer ${authToken}` },
          body: formData
        }).then(r => r.json());
        if (r.ok) {
          if (fabMailBatName) fabMailBatName.textContent = file.name;
          showNotification("✅ Mail BAT importé", "success");
        } else {
          showNotification("❌ " + (r.error || "Erreur d'import"), "error");
        }
      } catch(e) {
        showNotification("❌ Erreur réseau", "error");
      }
    });
  }

  // Répartitions — add row button
  if (fabRepartitionsAdd) {
    fabRepartitionsAdd.onclick = () => addRepartitionRow();
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
  const [j, engines, types, papers, faconnageOptions, stageData, sheetFormats, coverProducts, sheetCalcRulesResp, deliveryDelayResp, passesConfigResp] = await Promise.all([
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
    }).then(r => r.json()).catch(() => null),
    fetch("/api/settings/sheet-formats").then(r => r.json()).catch(() => []),
    fetch("/api/settings/cover-products").then(r => r.json()).catch(() => []),
    fetch("/api/settings/sheet-calculation-rules").then(r => r.json()).catch(() => ({ rules: {} })),
    fetch("/api/settings/delivery-delay").then(r => r.json()).catch(() => ({ delayHours: 48 })),
    fetch("/api/settings/passes-config").then(r => r.json()).catch(() => ({ config: {} }))
  ]);

  const d = (j && j.ok === false) ? {} : (j || {});

  // Store settings
  _coverProducts = Array.isArray(coverProducts) ? coverProducts : [];
  _sheetCalcRules = (sheetCalcRulesResp && sheetCalcRulesResp.rules) ? sheetCalcRulesResp.rules : {};
  _deliveryDelayHours = (deliveryDelayResp && deliveryDelayResp.delayHours) ? deliveryDelayResp.delayHours : 48;
  _passesConfig = (passesConfigResp && passesConfigResp.config) ? passesConfigResp.config : { faconnage: 0, pelliculageRecto: 0, pelliculageRectoVerso: 0, rainage: 0, dorure: 0, dosCarreColle: 0 };

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
  [fabMedia1, fabMedia2, fabMedia3, fabMedia4, fabMediaCouverture].forEach(sel => {
    if (sel) sel.innerHTML = paperHtml;
  });

  // Populate format feuille en machine
  if (fabFormatFeuille) {
    fabFormatFeuille.innerHTML = '<option value="">— Sélectionner —</option>' +
      (Array.isArray(sheetFormats) ? sheetFormats : []).map(f => `<option value="${f}">${f}</option>`).join("");
  }

  // Populate existing field values
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

  // New fields
  if (fabDonneurNom)    fabDonneurNom.value    = d.donneurOrdreNom    || "";
  if (fabDonneurPrenom) fabDonneurPrenom.value = d.donneurOrdrePrenom || "";
  if (fabDonneurTel)    fabDonneurTel.value    = d.donneurOrdreTelephone || "";
  if (fabDonneurEmail)  fabDonneurEmail.value  = d.donneurOrdreEmail  || "";
  if (fabPagination)    fabPagination.value    = d.pagination  || "";
  if (fabFormatFeuille) fabFormatFeuille.value = d.formatFeuille || "";
  if (fabMedia1Fabricant) fabMedia1Fabricant.value = d.media1Fabricant || "";
  if (fabMedia2Fabricant) fabMedia2Fabricant.value = d.media2Fabricant || "";
  if (fabMedia3Fabricant) fabMedia3Fabricant.value = d.media3Fabricant || "";
  if (fabMedia4Fabricant) fabMedia4Fabricant.value = d.media4Fabricant || "";
  if (fabMediaCouverture) fabMediaCouverture.value = d.mediaCouverture || "";
  if (fabMediaCouvertureFab) fabMediaCouvertureFab.value = d.mediaCouvertureFabricant || "";
  if (fabRainage) {
    fabRainage.checked = !!d.rainage;
    fabRainage.disabled = false;
    if (fabRainageLabel) fabRainageLabel.textContent = d.rainage ? "Oui" : "Non";
  }

  // Ennoblissement
  if (fabEnnobContainer) {
    const checked = Array.isArray(d.ennoblissement) ? d.ennoblissement : [];
    fabEnnobContainer.querySelectorAll('.fab-ennob-cb').forEach(cb => {
      cb.checked = checked.includes(cb.value);
    });
  }

  if (fabFaconnageBinding) fabFaconnageBinding.value = d.faconnageBinding || "";
  if (fabPlis) fabPlis.value = d.plis || "";
  if (fabSortie) fabSortie.value = d.sortie || "";

  // Mail import filenames
  if (fabMailDevisName) fabMailDevisName.textContent = d.mailDevisFileName || "";
  if (fabMailBatName) fabMailBatName.textContent = d.mailBatFileName || "";

  // Dates
  if (fabDateDepart) { fabDateDepart.value = d.dateDepart ? new Date(d.dateDepart).toISOString().split("T")[0] : ""; }
  if (fabDateLivraison) { fabDateLivraison.value = d.dateLivraison ? new Date(d.dateLivraison).toISOString().split("T")[0] : ""; fabDateLivraison._manuallyEdited = !!d.dateLivraison; }
  if (fabPlanningMachine) { fabPlanningMachine.value = d.planningMachine ? new Date(d.planningMachine).toISOString().slice(0, 16) : ""; }

  // Nombre de feuilles
  if (fabNombreFeuilles) {
    fabNombreFeuilles.value = d.nombreFeuilles || "";
    fabNombreFeuilles._manuallyEdited = !!d.nombreFeuilles;
  }

  // Justifs clients
  if (fabJustifsQte)    fabJustifsQte.value    = d.justifsClientsQuantite != null ? d.justifsClientsQuantite : "";
  if (fabJustifsAdresse) fabJustifsAdresse.value = d.justifsClientsAdresse || "";

  // Répartitions
  if (fabRepartitionsContainer) {
    fabRepartitionsContainer.innerHTML = "";
    const reps = Array.isArray(d.repartitions) ? d.repartitions : [];
    reps.forEach(r => addRepartitionRow(r.quantite || "", r.adresse || ""));
  }

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

  // Update conditional fields
  updateCouvertureVisibility();
  updateRainageAuto();
  if (!d.nombreFeuilles) updateNombreFeuilles();
  updatePassesDisplay();

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
    media4: fabMedia4 ? fabMedia4.value || null : null,

    // New fields
    donneurOrdreNom:       fabDonneurNom    ? fabDonneurNom.value    || null : null,
    donneurOrdrePrenom:    fabDonneurPrenom ? fabDonneurPrenom.value || null : null,
    donneurOrdreTelephone: fabDonneurTel    ? fabDonneurTel.value    || null : null,
    donneurOrdreEmail:     fabDonneurEmail  ? fabDonneurEmail.value  || null : null,
    pagination:    fabPagination    ? fabPagination.value    || null : null,
    formatFeuille: fabFormatFeuille ? fabFormatFeuille.value || null : null,
    media1Fabricant: fabMedia1Fabricant ? fabMedia1Fabricant.value || null : null,
    media2Fabricant: fabMedia2Fabricant ? fabMedia2Fabricant.value || null : null,
    media3Fabricant: fabMedia3Fabricant ? fabMedia3Fabricant.value || null : null,
    media4Fabricant: fabMedia4Fabricant ? fabMedia4Fabricant.value || null : null,
    mediaCouverture:         fabMediaCouverture    ? fabMediaCouverture.value    || null : null,
    mediaCouvertureFabricant: fabMediaCouvertureFab ? fabMediaCouvertureFab.value || null : null,
    rainage: fabRainage ? fabRainage.checked : null,
    ennoblissement: fabEnnobContainer
      ? Array.from(fabEnnobContainer.querySelectorAll('.fab-ennob-cb:checked')).map(cb => cb.value)
      : [],
    faconnageBinding: fabFaconnageBinding ? fabFaconnageBinding.value || null : null,
    plis:             fabPlis   ? fabPlis.value   || null : null,
    sortie:           fabSortie ? fabSortie.value || null : null,
    nombreFeuilles:   fabNombreFeuilles ? parseInt(fabNombreFeuilles.value) || null : null,
    dateDepart:       fabDateDepart      ? fabDateDepart.value      || null : null,
    dateLivraison:    fabDateLivraison   ? fabDateLivraison.value   || null : null,
    planningMachine:  fabPlanningMachine ? fabPlanningMachine.value || null : null,
    justifsClientsQuantite: fabJustifsQte    ? parseInt(fabJustifsQte.value) || null : null,
    justifsClientsAdresse:  fabJustifsAdresse ? fabJustifsAdresse.value || null : null,
    repartitions: getRepartitions()
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

