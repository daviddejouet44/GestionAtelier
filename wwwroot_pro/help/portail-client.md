# 7. Portail client

Le portail client est une interface web dédiée à vos clients pour déposer leurs commandes et suivre leur avancement.

## Accès

- URL : **`http(s)://votre-serveur/portal/login.html`**
- Configurable dans **Paramétrages → 🌐 Portail client → URL publique**

## Côté admin — Créer un compte client

1. **Paramétrages → 🌐 Portail client → 👥 Comptes clients**
2. Cliquez **"+ Nouveau client"**
3. Renseignez l'email et le mot de passe (min. 8 caractères)
4. Optionnel : nom, société, téléphone
5. Un email de bienvenue est envoyé automatiquement (si SMTP configuré)

## Côté client — Déposer une commande

1. Se connecter sur `/portal/login.html`
2. Cliquer **"Nouvelle commande"**
3. **Glisser-déposer** un ou plusieurs PDFs
4. Remplir le formulaire (champs configurables par l'admin)
5. Cliquer **"Soumettre la commande"** → confirmation par email

## Côté admin — Traitement

- Les commandes portail apparaissent dans la tuile **"Commandes web"** du kanban.
- Visible aussi dans **Paramétrages → 🌐 Portail client → Commandes**.
- L'admin peut changer le statut, envoyer un BAT, marquer comme terminée.

## Champs du formulaire configurables

Dans **Paramétrages → 🌐 Portail client → 📋 Champs du formulaire**, l'admin peut :
- Afficher/masquer des champs
- Rendre des champs obligatoires
- Personnaliser les libellés et les listes de valeurs autorisées
- Réordonner les champs

## Apparence personnalisable

Dans **Paramétrages → 🌐 Portail client → 🎨 Apparence**, l'admin peut :
- Choisir les couleurs, la police, le nom de l'entreprise
- Personnaliser le header, le footer et le CSS
