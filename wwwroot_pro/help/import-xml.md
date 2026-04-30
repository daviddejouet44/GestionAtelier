# 3. Import XML seul

Importez un fichier XML pour créer ou mettre à jour une fiche de production, sans PDF immédiat.

## Étapes

1. Allez dans **Paramétrages → 🔌 Intégrations → Import XML**.
2. **Cliquez sur "Importer un XML"** et sélectionnez votre fichier.
3. L'application crée la fiche avec les métadonnées du XML.
4. Le PDF d'impression sera associé **ultérieurement** :
   - Via le **hotfolder** (Paramétrages → Chemins d'accès)
   - Via une **source automatique** (SFTP, Dropbox…)
   - Manuellement depuis la fiche (bouton "Ajouter un fichier")

## Déduplication

Si un XML contient un numéro de commande (`<NumOrder>`) déjà présent dans la base, la fiche existante est **mise à jour** plutôt que créée en double.

## Lookup depuis la soumission

Depuis l'onglet **Soumission**, le bouton **"🔗 Récupérer depuis ERP/W2P"** permet de saisir un numéro de commande et de récupérer automatiquement les métadonnées depuis :
- **Pressero** (via API)
- **MDSF** (via API)
- **ERP générique** (via endpoint configuré dans Paramétrage → Intégrations)
