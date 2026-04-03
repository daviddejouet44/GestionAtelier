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
const fabDelete = document.getElementById("fab-delete");
const fabMoteur = document.getElementById("fab-moteur");
const fabOperateur = document.getElementById("fab-operateur");
const fabQuantite = document.getElementById("fab-quantite");
const fabType = document.getElementById("fab-type");
const fabFormat = document.getElementById("fab-format");
const fabRectoVerso = document.getElementById("fab-recto-verso");
const fabClient = document.getElementById("fab-client");
const fabNumeroDossier = document.getElementById("fab-numero-dossier");
const fabNotes = document.getElementById("fab-notes");
const fabDelai = document.getElementById("fab-delai");
const fabFaconnage = document.getElementById("fab-faconnage");
const fabMedia1 = document.getElementById("fab-media1");
const fabMedia2 = document.getElementById("fab-media2");
const fabMedia3 = document.getElementById("fab-media3");
const fabMedia4 = document.getElementById("fab-media4");
const fabHistory = document.getElementById("fab-history");
const fabRemove = document.getElementById("fab-delivery-remove");
const fabStageBanner = document.getElementById("fab-stage-banner");

export let fabCurrentPath = null;

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

    fabModal.classList.add("hidden");
    alert("Fin de production marquée");
    // Trigger kanban refresh via global callback
    if (window._refreshKanban) await window._refreshKanban();
    if (window._refreshSubmissionView) await window._refreshSubmissionView();
  };

  // Masquer le bouton PrismaPrepare (remplacé par le bouton BAT dans la tuile kanban)
  if (fabPrisma) {
    fabPrisma.style.display = "none";
  }

  if (fabDelete) {
    fabDelete.onclick = async () => {
      if (!fabCurrentPath) return;
      if (!confirm("Supprimer ce fichier et le déplacer vers la corbeille ?")) return;
      const r = await fetch("/api/jobs/delete", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
        body: JSON.stringify({ fullPath: fabCurrentPath })
      }).then(r => r.json()).catch(() => ({ ok: false }));
      if (r.ok) {
        fabModal.classList.add("hidden");
        showNotification("✅ Fichier déplacé vers la corbeille", "success");
        if (window._refreshKanban) await window._refreshKanban();
      } else {
        showNotification("❌ Erreur : " + (r.error || "Impossible de supprimer"), "error");
      }
    };
  }
}

// ======================================================
// OUVERTURE DE LA FICHE
// ======================================================
export async function openFabrication(fullPath) {
  fabCurrentPath = normalizePath(fullPath);
  const fabCurrentFileName = fnKey(fabCurrentPath);

  const r = await fetch("/api/fabrication?fileName=" + encodeURIComponent(fabCurrentFileName));
  const j = await r.json();
  const d = j.ok === false ? {} : j;

  // Print engines
  try {
    const engines = await fetch("/api/config/print-engines").then(r => r.json()).catch(() => []);
    fabMoteur.innerHTML = '<option value="">— Sélectionner —</option>';
    engines.forEach(e => {
      const name = (typeof e === "object" && e !== null) ? (e.name || "") : String(e || "");
      const opt = document.createElement("option");
      opt.value = name;
      opt.textContent = name;
      fabMoteur.appendChild(opt);
    });
  } catch(err) { console.warn("Erreur print-engines:", err); }

  // Work types
  try {
    const types = await fetch("/api/config/work-types").then(r => r.json()).catch(() => []);
    fabType.innerHTML = '<option value="">— Sélectionner —</option>';
    types.forEach(t => {
      const opt = document.createElement("option");
      opt.value = t;
      opt.textContent = t;
      fabType.appendChild(opt);
    });
  } catch(e) { console.warn("Erreur work-types:", e); }

  // Paper catalog
  try {
    const papers = await fetch("/api/config/paper-catalog").then(r => r.json()).catch(() => []);
    [fabMedia1, fabMedia2, fabMedia3, fabMedia4].forEach(sel => {
      if (!sel) return;
      sel.innerHTML = '<option value="">— Sélectionner —</option>';
      papers.forEach(p => {
        const opt = document.createElement("option");
        opt.value = p;
        opt.textContent = p;
        sel.appendChild(opt);
      });
    });
  } catch(e) { console.warn("Paper catalog error:", e); }

  fabMoteur.value = d.moteurImpression || d.machine || "";
  fabOperateur.value = d.operateur || "";
  fabQuantite.value = d.quantite || "";
  fabType.value = d.typeTravail || "";
  if (fabType) { fabType.style.borderColor = ""; fabType.style.boxShadow = ""; }
  fabFormat.value = d.format || "";
  fabRectoVerso.value = d.rectoVerso || "";
  fabClient.value = d.client || "";
  if (fabNumeroDossier) { fabNumeroDossier.value = d.numeroDossier || ""; fabNumeroDossier.style.borderColor = ""; fabNumeroDossier.style.boxShadow = ""; }
  fabNotes.value = d.notes || "";
  fabFaconnage.value = d.faconnage || "";
  if (fabMedia1) fabMedia1.value = d.media1 || "";
  if (fabMedia2) fabMedia2.value = d.media2 || "";
  if (fabMedia3) fabMedia3.value = d.media3 || "";
  if (fabMedia4) fabMedia4.value = d.media4 || "";

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

  if (fabStageBanner) {
    fabStageBanner.style.display = "none";
    fetch("/api/file-stage?fileName=" + encodeURIComponent(fabCurrentFileName))
      .then(r => r.json())
      .then(s => {
        if (s.ok && s.folder) {
          fabStageBanner.textContent = "📍 Étape actuelle : " + s.folder;
          fabStageBanner.style.display = "block";
          if (s.fullPath) fabCurrentPath = normalizePath(s.fullPath);
        }
      })
      .catch(() => {});
  }

  fabModal.classList.remove("hidden");
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
    quantite: parseInt(fabQuantite.value) || null,
    typeTravail: fabType.value,
    format: fabFormat.value,
    rectoVerso: fabRectoVerso.value,
    client: fabClient.value,
    numeroDossier: fabNumeroDossier ? fabNumeroDossier.value || null : null,
    notes: fabNotes.value,
    faconnage: fabFaconnage.value,
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
