# Soumission PDF + XML couplés

## Vue d'ensemble

La fonctionnalité **Soumission XML couplée** permet de déposer simultanément un **PDF d'impression** et un **fichier XML de métadonnées** dans la boîte de dépôt de l'onglet Soumission. Les champs de la fiche de production sont alors pré-remplis automatiquement à partir du XML, en réutilisant le mapping configuré dans **Paramétrages → Intégrations → 📥 Import XML**.

---

## Prérequis

1. **Configurer le mapping XML** dans `Paramétrages → Intégrations → 📥 Import XML` :
   - Associez chaque balise XML à un champ de la fiche (ex. `<NumCommande>` → `referenceCommande`).
   - Définissez la **clé de déduplication** (champ utilisé pour éviter les doublons).
2. **Activer la détection couplée** dans `Paramétrages → Intégrations → 📎 Soumission XML couplé`.

---

## Flux de traitement

### Dépose dans la boîte Soumission

| Fichiers déposés | Comportement |
|---|---|
| 1 PDF + 1 XML | Couplage automatique ; le formulaire s'ouvre pré-rempli (ou la fiche est créée directement selon le mode configuré). |
| N PDF + 1 XML | Tous les PDF sont rattachés à la même fiche ; les métadonnées proviennent du XML unique. |
| N PDF + N XML | Appariement par **nom de base** (ex. `commande01.pdf` ↔ `commande01.xml`) ; une fiche par paire. |
| PDF seul | Comportement habituel (upload simple, bouton ERP/W2P disponible). |
| XML seul | La fiche est créée sans PDF ; le PDF peut être associé ultérieurement. |

### Mode "Formulaire pré-rempli" (recommandé)

1. GestionAtelier parse le XML et applique le mapping.
2. Le formulaire de soumission s'ouvre **pré-rempli** avec les données extraites.
3. Un bandeau bleu signale que les champs ont été pré-remplis.
4. L'utilisateur vérifie, complète si nécessaire, puis enregistre.

### Mode "Création directe"

1. GestionAtelier parse le XML, applique le mapping et crée la fiche sans passer par le formulaire.
2. Anti-doublon via la clé de déduplication : si la valeur existe déjà, la fiche est mise à jour.
3. Un journal d'import est alimenté (`Paramétrages → Intégrations → 📋 Journal imports`).

---

## Format XML attendu

GestionAtelier reconnaît automatiquement les structures suivantes :

```xml
<!-- Structure plate -->
<Order>
  <NumCommande>CMD-2026-001</NumCommande>
  <NomClient>Imprimerie Dupont</NomClient>
  <TypeTravail>Carte de visite</TypeTravail>
  <Quantite>500</Quantite>
  <FormatFini>85x55mm</FormatFini>
  <DateLivraison>2026-05-15</DateLivraison>
  <Commentaire>Pelliculage mat recto</Commentaire>
</Order>
```

```xml
<!-- Structure enveloppée -->
<Orders>
  <Order>
    <NumCommande>CMD-2026-002</NumCommande>
    ...
  </Order>
  <Order>
    <NumCommande>CMD-2026-003</NumCommande>
    ...
  </Order>
</Orders>
```

Les balises racines acceptées sont `<Order>`, `<Commande>` et `<Job>`. Le nom exact des balises enfants est configurable via le mapping.

---

## Endpoint API

```
POST /api/soumission/upload-with-xml
```

**Content-Type**: `multipart/form-data`

| Champ formulaire | Description |
|---|---|
| `pdf` (multiple) | Un ou plusieurs fichiers PDF |
| `xml` | Un fichier XML de métadonnées (optionnel) |

**Réponse** :

```json
{
  "ok": true,
  "fichePrefill": {
    "referenceCommande": "CMD-2026-001",
    "nomClient": "Imprimerie Dupont",
    "typeTravail": "Carte de visite",
    "quantite": "500"
  },
  "jobIds": [
    { "fileName": "00042_commande01.pdf", "fullPath": "C:\\Flux\\Soumission\\00042_commande01.pdf" }
  ],
  "mode": "prefill",
  "xmlFileName": "commande01.xml"
}
```

---

## Configuration

`Paramétrages → Intégrations → 📎 Soumission XML couplé`

| Option | Description |
|---|---|
| **Activé** | Active/désactive la détection PDF+XML dans la boîte de Soumission. |
| **Comportement** | `Formulaire pré-rempli` (recommandé) : ouvre le formulaire pour validation. `Création directe` : crée la fiche sans passer par le formulaire. |

Le **mapping XML → champs fiche** et la **clé de déduplication** sont partagés avec l'import XML manuel (onglet `📥 Import XML`).

---

## Exemple complet — Pair PDF + XML

Noms de fichiers : `CMD-2026-001.pdf` + `CMD-2026-001.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<Order>
  <NumCommande>CMD-2026-001</NumCommande>
  <NomClient>Société Martin</NomClient>
  <TypeTravail>Flyer A5</TypeTravail>
  <Quantite>1000</Quantite>
  <FormatFini>148x210mm</FormatFini>
  <DateLivraisonSouhaitee>2026-06-01</DateLivraisonSouhaitee>
  <Commentaire>Quadri recto/verso, pelliculage mat</Commentaire>
</Order>
```

Mapping XML configuré :

| Champ fiche | Balise XML |
|---|---|
| `referenceCommande` | `NumCommande` |
| `nomClient` | `NomClient` |
| `typeTravail` | `TypeTravail` |
| `quantite` | `Quantite` |
| `formatFini` | `FormatFini` |
| `dateLivraisonSouhaitee` | `DateLivraisonSouhaitee` |
| `commentaire` | `Commentaire` |
