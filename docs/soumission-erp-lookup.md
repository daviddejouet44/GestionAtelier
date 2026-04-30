# Soumission PDF + Lookup ERP / W2P

## Vue d'ensemble

La fonctionnalité **Import PDF + ERP/W2P** permet, lors d'un dépôt de PDF dans l'onglet Soumission, de récupérer les métadonnées de la commande depuis un **ERP** ou depuis un **W2P** (Pressero, MDSF) en saisissant ou en détectant automatiquement le numéro de commande. Le formulaire de soumission est alors pré-rempli avec les informations récupérées.

---

## Sources supportées

| Source | Description |
|---|---|
| **Pressero** | Web-to-Print. Réutilise la configuration `Paramétrages → Intégrations → 🌐 Pressero`. |
| **MDSF** | Market Direct StoreFront. Réutilise `Paramétrages → Intégrations → 🌐 MDSF`. |
| **ERP générique** | Endpoint HTTP configurable (REST GET, JSON ou XML), avec authentification Basic/Bearer/API Key. |

---

## Utilisation

### Bouton "🔗 ERP/W2P" en Soumission

Après dépôt d'un PDF dans la boîte Soumission :

1. Cliquez sur le bouton **🔗 ERP/W2P** de la carte du fichier.
2. Une popup s'ouvre :
   - Sélectionnez la **source** (Pressero, MDSF, ou ERP générique configuré).
   - Saisissez la **référence / n° commande** (pré-remplie si détectée automatiquement depuis le nom du fichier).
   - Cliquez sur **🔍 Rechercher**.
3. Les métadonnées récupérées s'affichent.
4. Cliquez sur **✅ Appliquer** : le formulaire de fiche s'ouvre pré-rempli avec ces données.

### Détection automatique de la référence

Si une **regex de détection** est configurée, GestionAtelier l'applique au nom du fichier PDF dès le dépôt pour extraire la référence commande (ex. `CMD-2026-001` depuis `CMD-2026-001_RECTO.pdf`).

Si l'**auto-lookup** est activé, la recherche est lancée automatiquement sans clic utilisateur.

---

## Configuration

`Paramétrages → Intégrations → 🔗 PDF + ERP/W2P`

| Option | Description |
|---|---|
| **Activé** | Affiche ou masque le bouton "🔗 ERP/W2P" sur les cartes de Soumission. |
| **Source par défaut** | Source présélectionnée dans la popup de lookup. |
| **Regex de détection** | Expression régulière appliquée au nom de fichier PDF. Le 1er groupe capturant est utilisé comme référence. |
| **Auto-lookup** | Si activé + regex configurée, lance la recherche automatiquement au drop. |

---

## Sources ERP génériques

Pour chaque source ERP, configurez :

| Champ | Description |
|---|---|
| **Nom** | Libellé affiché dans la liste (ex. "Mon ERP"). |
| **URL** | Endpoint REST. Utilisez `{ref}` comme placeholder (ex. `https://erp.example.com/api/orders/{ref}`). |
| **Authentification** | `Aucune`, `Basic Auth`, `Bearer Token`, ou `API Key (header)`. |
| **Format réponse** | `JSON` (par défaut) ou `XML`. |

---

## Endpoints API

### Lookup d'une commande

```
POST /api/external/{provider}/lookup
```

| Paramètre URL | Valeur |
|---|---|
| `provider` | `pressero`, `mdsf`, ou l'`id` / `name` d'une source ERP générique |

**Body JSON** :
```json
{ "ref": "CMD-2026-001" }
```

**Réponse** :
```json
{
  "ok": true,
  "fiche": {
    "referenceCommande": "CMD-2026-001",
    "nomClient": "Société Martin",
    "typeTravail": "Carte de visite",
    "quantite": "500",
    "formatFini": "85x55mm",
    "dateLivraisonSouhaitee": "2026-06-01"
  },
  "raw": { ... }
}
```

**Réponse erreur** :
```json
{
  "ok": false,
  "error": "Pressero: HTTP 404"
}
```

### Détection de référence dans un nom de fichier

```
GET /api/external/detect-ref?filename=CMD-2026-001_RECTO.pdf
```

**Réponse** :
```json
{
  "ok": true,
  "detected": "CMD-2026-001"
}
```

---

## Normalisation des réponses

### Pressero

Les champs suivants sont reconnus automatiquement :

| Champ Pressero | Champ fiche |
|---|---|
| `orderId` / `orderNumber` | `referenceCommande` |
| `customerName` | `nomClient` |
| `companyName` | `client` |
| `productName` | `typeTravail` |
| `quantity` / `qty` | `quantite` |
| `format` / `size` | `formatFini` |
| `requiredDate` / `dueDate` | `dateLivraisonSouhaitee` |
| `comments` / `notes` | `commentaire` |

### MDSF

| Champ MDSF | Champ fiche |
|---|---|
| `orderId` / `orderNumber` | `referenceCommande` |
| `customerName` | `nomClient` |
| `companyName` | `client` |
| `productDescription` | `typeTravail` |
| `quantity` | `quantite` |
| `trimSize` | `formatFini` |
| `deliveryDate` | `dateLivraisonSouhaitee` |
| `specialInstructions` | `commentaire` |

### ERP générique (JSON)

Le mapping JSON → fiche est configurable via un dictionnaire `ficheField → jsonPath` (notation pointée, ex. `order.customerName`).

### ERP générique (XML)

Le mapping réutilise la même logique que l'Import XML manuel (balise XML → champ fiche).

---

## Exemple de regex de détection

| Regex | Exemple de nom de fichier | Référence extraite |
|---|---|---|
| `^([A-Z0-9-]+)_.*\.pdf$` | `CMD-2026-001_recto.pdf` | `CMD-2026-001` |
| `ORDER-(\d+)` | `ORDER-12345_client.pdf` | `12345` |
| `(\d{5,})` | `facture_00042_dupont.pdf` | `00042` |
