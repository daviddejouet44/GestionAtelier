// constants.js — Constantes partagées (source unique de vérité pour le frontend)

// Pourcentages d'avancement par étape (source canonique, alignée avec le backend StageConstants)
export const STAGE_PROGRESS = {
  "Début de production": 0,
  "Corrections": 15,
  "Corrections et fond perdu": 15,
  "Prêt pour impression": 25,
  "BAT": 35,
  "PrismaPrepare": 50,
  "Fiery": 50,
  "Impression en cours": 65,
  "Façonnage": 80,
  "Fin de production": 100
};

export function getStageProgress(stage) {
  if (!stage) return 0;
  const key = Object.keys(STAGE_PROGRESS).find(k => stage.toLowerCase().includes(k.toLowerCase()));
  return key !== undefined ? STAGE_PROGRESS[key] : 0;
}

// Libellés d'affichage des étapes kanban
export const STAGE_DISPLAY_LABELS = {
  "Début de production": "Jobs à traiter",
  "Corrections": "Preflight",
  "Corrections et fond perdu": "Preflight avec fond perdu",
  "Prêt pour impression": "En attente",
  "Façonnage": "Finitions"
};

export function getStageLabelDisplay(stage, batStatus) {
  if (stage === 'BAT') {
    if (batStatus === 'refuse') return 'BAT — ❌ Refusé';
    if (batStatus === 'valide') return 'BAT — ✅ Validé';
    if (batStatus === 'envoye') return 'BAT — 📤 Envoyé';
  }
  return STAGE_DISPLAY_LABELS[stage] || stage;
}

// Ordre des étapes (du moins avancé au plus avancé)
export const STAGE_ORDER = [
  "Début de production", "Corrections", "Corrections et fond perdu",
  "Prêt pour impression", "BAT", "PrismaPrepare", "Fiery",
  "Impression en cours", "Façonnage", "Fin de production"
];

// Couleur associée à un pourcentage d'avancement
export function getStageColor(progress) {
  if (progress === 0) return "#6b7280";
  if (progress <= 25) return "#f59e0b";
  if (progress <= 35) return "#8b5cf6";
  if (progress <= 50) return "#3b82f6";
  if (progress <= 65) return "#f97316";
  if (progress <= 80) return "#06b6d4";
  return "#22c55e";
}
