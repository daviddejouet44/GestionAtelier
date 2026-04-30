# 4. Hotfolder local

Déposez un PDF dans un dossier surveillé sur votre serveur pour créer automatiquement une fiche de production.

## Configuration

1. **Paramétrages → Chemins d'accès** — Définissez le chemin du dossier racine (ex. `C:\Flux`).
2. Les sous-dossiers correspondent aux colonnes du kanban (`Début de production`, `Corrections`, etc.).

## Fonctionnement

- Tout fichier PDF déposé dans le dossier **`Début de production`** est automatiquement détecté.
- Une fiche est créée avec le nom du fichier comme intitulé.
- Le fichier est déplacé dans le hotfolder d'archivage une fois traité.

## Nommage des fichiers

Pour un meilleur préremplissage automatique, nommez vos fichiers :
```
[NumCommande]_[Titre]_[Quantite].pdf
```
Exemple : `2025-001_FlierA5_1000.pdf`

## Hotfolder + XML

Si un fichier XML du même nom est présent dans le dossier (ex. `2025-001_FlierA5_1000.xml`), les deux sont traités ensemble pour un préremplissage complet.
