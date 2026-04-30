# 6. Lookup ERP / W2P

Saisissez un numéro de commande pour récupérer automatiquement les métadonnées depuis votre ERP ou votre W2P (Web-to-Print).

## Systèmes supportés

- **Pressero** (Aleyant)
- **MDSF** (Mediasurface)
- **ERP générique** (endpoint REST configurable)

## Depuis la Soumission

1. Dans l'onglet **Soumission**, cliquez sur **"🔗 Récupérer depuis ERP/W2P"**.
2. Entrez le numéro de commande.
3. Sélectionnez la source dans la liste déroulante.
4. Cliquez **"Récupérer"** — les champs de la fiche se préremplissent.
5. Ajoutez le PDF si nécessaire, puis soumettez.

## Configuration

- **Paramétrages → 🔌 Intégrations → ERP / W2P**
- Renseignez l'URL de l'API, les credentials et le mapping des champs.

## Détection automatique

Si vous activez la détection automatique par regex (ex. `^P\d{6}$` pour Pressero), tout fichier soumis dont le nom correspond au pattern déclenche automatiquement le lookup avant création de fiche.
