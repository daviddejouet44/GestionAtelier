# Panneau d'aide global — GestionAtelier

## Vue d'ensemble

Le panneau d'aide est un drawer latéral accessible depuis n'importe où dans l'application via le bouton **❓ Aide** dans le header.

Il permet à tous les utilisateurs de comprendre les différents flux de commandes et fonctionnalités de l'application sans quitter leur contexte.

---

## Accès

- **Bouton ❓ Aide** dans la barre supérieure de l'app (à droite, à côté de Déconnexion)
- **URL avec paramètre** : `?help=nom-section` — ouvre directement une section
  - Exemple : `http://localhost:5080/pro/?help=portail-client`

---

## Sections disponibles

| Section | ID | Description |
|---------|-----|-------------|
| Vue d'ensemble | `index` | Présentation des flux |
| Soumission manuelle | `soumission-manuelle` | Créer une fiche manuellement |
| PDF + XML couplés | `pdf-xml` | Déposer PDF+XML simultanément |
| Import XML / Lookup ERP | `import-xml` | Import XML seul + lookup ERP |
| Hotfolder local | `hotfolder` | Dossier surveillé local |
| Sources automatiques | `sources-auto` | SFTP, Dropbox, Google Drive, Box, OneDrive |
| Lookup ERP / W2P | `lookup-erp` | Récupérer depuis Pressero/MDSF/ERP |
| Portail client | `portail-client` | Commandes clients en ligne |
| Workflow BAT | `bat-workflow` | Envoi et validation de BAT |
| Profils utilisateurs | `profils` | Droits par profil |

---

## Modifier le contenu

Les pages d'aide sont de simples fichiers **Markdown** dans :

```
wwwroot_pro/help/
├── index.md              (vue d'ensemble)
├── soumission-manuelle.md
├── pdf-xml.md
├── import-xml.md
├── hotfolder.md
├── sources-auto.md
├── lookup-erp.md
├── portail-client.md
├── bat-workflow.md
└── profils.md
```

### Syntaxe Markdown supportée

| Syntaxe | Rendu |
|---------|-------|
| `# Titre` | `<h1>` |
| `## Titre` | `<h2>` |
| `### Titre` | `<h3>` |
| `**texte**` | **gras** |
| `*texte*` | *italique* |
| `- liste` | liste à puces |
| `1. liste` | liste numérotée |
| `` `code` `` | code inline |
| ` ```...``` ` | bloc de code |
| `[texte](section)` | lien interne vers une section |
| `---` | séparateur horizontal |
| `\| col \| col \|` | tableau |

> **Note** : les liens `[texte](nom-section)` permettent la navigation entre sections dans le panneau.

### Ajouter une nouvelle section

1. Créez un fichier `wwwroot_pro/help/ma-section.md`
2. Ajoutez une entrée dans `HELP_SECTIONS` dans `wwwroot_pro/js/help.js` :
   ```js
   { id: 'ma-section', icon: '📖', title: 'Ma nouvelle section' },
   ```
3. Redéployez l'application (pas de redémarrage serveur nécessaire — les fichiers `.md` sont statiques).

---

## Recherche full-text

Le champ **Rechercher dans l'aide…** en haut du panneau effectue une recherche full-text dans tous les fichiers `.md`. Les résultats affichent les sections correspondantes avec un extrait de contexte.

---

## Impression

Le bouton **🖨️ Imprimer** en bas du panneau déclenche l'impression de la section active. Une feuille de styles CSS `@media print` masque tous les éléments de l'app sauf le contenu de l'aide.

---

## Implémentation technique

- **Composant** : `wwwroot_pro/js/help.js`
- **Contenu** : `wwwroot_pro/help/*.md`
- **Rendu Markdown** : mini-renderer maison (pas de dépendance externe)
- **Initialisation** : `initHelpPanel()` appelée depuis `app.js` après la connexion
- **Styles print** : injectés dynamiquement dans le `<head>` au chargement du module
