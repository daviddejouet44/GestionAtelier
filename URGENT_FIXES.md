# Fixes Urgents — 3 correctifs rapides

## 1. Champs Process/Bascule/Couleurs/CouleursAccompagnement non sauvegardés

### Cause
- Les 4 champs **n'existent pas dans la form-config** (`/api/settings/form-config`)
- `fabrication.js` les charge et les remplit (`populateFabForm()` lignes 516-519)
- Mais ils ne sont pas présents dans le **DOM** car `renderFabForm()` ne connaît que les champs de `config.fields`
- Résultat : `set('process')` ne trouve pas l'élément à remplir

### Solution
**Option A** (recommandée) : Ajouter à la form-config par défaut
- Fichier : `Endpoints/Settings/SettingsFormConfigEndpoints.cs`
- Ajouter une section "Process d'impression" avec 4 champs :
  - `process` : select (Numérique / Offset)
  - `bascule` : text ou select (visible seulement si process=Offset)
  - `couleurs` : select (1 couleur / Bichromie / Trichromie / Quadri)
  - `couleursAccompagnement` : text (champ libre)

**Option B** (fallback) : Injection manuelle dans fabrication.js
- Après `renderFabForm()` (ligne 789), ajouter une section "Process d'impression" au DOM avec les 4 inputs
- Assurer les IDs : `fab-process`, `fab-bascule`, `fab-couleurs`, `fab-couleurs-accompagnement`
- Vérifier `FIELD_HTML_IDS` (lignes 103-106) — ils sont déjà mappés ✅

### Test
1. Renseigner les 4 champs dans une fiche
2. Cliquer "Enregistrer"
3. Fermer la fiche et la rouvrir
4. Les valeurs doivent persister

---

## 2. Supprimer le bouton "Récapitulatif" des commandes web

### Localisation
- Probablement dans `Endpoints/Portal/PortalOrdersEndpoints.cs` (réponse JSON)
- Ou dans une vue JS qui rend les cartes des commandes web
- Chercher : "récapitulatif", "résumé", "summary"

### Action
- Supprimer le bouton entièrement de l'interface
- S'assurer qu'une autre façon d'accéder aux détails reste (exemple : cliquer sur le titre de la commande)

---

## 3. Ajouter bouton "Ouvrir dans PrismaPrepare" sur la tuile PrismaPrepare

### Localisation
- Fichier : `wwwroot_pro/js/kanban/kanban-cards.js` (rendu des tuiles)
- Le système d'actions existe déjà : `openActionsDropdown()` (`kanban-actions.js`)
- L'action `prisma-prepare` est déjà définie

### Action
- Identifier la tuile PrismaPrepare
- Ajouter un bouton principal **"🎯 Ouvrir dans PrismaPrepare"** (ou directement appeler l'action au lieu du dropdown)
- Intégrer avec `handlePrintAction('prisma-prepare', fullPath)`

### Code exemple
```javascript
const btnPrismaPrepare = document.createElement("button");
btnPrismaPrepare.className = "btn btn-sm";
btnPrismaPrepare.textContent = "🎯 PrismaPrepare";
btnPrismaPrepare.onclick = () => handlePrintAction('prisma-prepare', full);
actions.appendChild(btnPrismaPrepare);
```

---

## Fichiers à modifier
1. `Endpoints/Settings/SettingsFormConfigEndpoints.cs` — ajouter les 4 champs à la form-config
2. `wwwroot_pro/js/fabrication.js` — ou vérifier que les champs sont bien rendus
3. `Endpoints/Portal/PortalOrdersEndpoints.cs` (ou JS portail) — supprimer bouton récapitulatif
4. `wwwroot_pro/js/kanban/kanban-cards.js` — ajouter bouton PrismaPrepare

---

## Priorité
1. **Urgent** : Fix des 4 champs (bloquant pour la saisie)
2. **Moyen** : Suppression récapitulatif (nettoyage UI)
3. **Moyen** : Bouton PrismaPrepare (amélioration UX)
