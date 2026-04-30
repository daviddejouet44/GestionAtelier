# 2. Soumission PDF + XML couplés

Déposez simultanément un fichier PDF et son XML de métadonnées pour préremplir automatiquement la fiche de production.

## Étapes

1. **Préparez vos fichiers** : assurez-vous que le PDF et le XML ont le **même nom de base** (ex. `commande_1234.pdf` et `commande_1234.xml`).
2. **Glissez-déposez les deux fichiers** en même temps dans la zone de soumission.
3. L'application **détecte automatiquement** la paire PDF+XML et applique le mapping XML → champs fiche.
4. **Vérifiez le préremplissage** dans le formulaire et corrigez si nécessaire.
5. **Cliquez sur "Soumettre"**.

## Mapping XML

Le mapping entre les balises XML et les champs de la fiche se configure dans :  
**Paramétrages → 🔌 Intégrations → Mapping XML**.

Exemple :
```xml
<Job>
  <NumOrder>12345</NumOrder>
  <Titre>Flyer A5</Titre>
  <Quantite>1000</Quantite>
</Job>
```

## Avantages

- Aucune saisie manuelle pour les commandes ERP/W2P.
- Traçabilité : le numéro de commande ERP est conservé dans la fiche.
- Compatible avec les exports Pressero, MDSF, ERP générique.
