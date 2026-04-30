# Portail client — GestionAtelier

## Vue d'ensemble

Le portail client est un espace web accessible à l'adresse `/portal/` permettant à vos clients de :

- Se connecter avec un email + mot de passe dédié (compte séparé des comptes atelier)
- Déposer des PDF de commande avec un mini-formulaire
- Suivre l'avancement de leurs commandes (statut, historique)
- **Valider ou refuser les BAT** directement en ligne (y compris depuis un smartphone)
- Gérer leurs informations de compte (email, adresse de livraison, mot de passe)

Le portail est **totalement isolé** de l'interface atelier. Les tokens clients ne donnent accès qu'aux routes `/api/portal/*` et uniquement aux données de leur propre compte.

---

## Activation du portail

1. Connectez-vous à l'interface atelier en tant qu'**admin**.
2. Allez dans **Paramétrages → 🌐 Portail client**.
3. Cochez **Portail activé**.
4. Renseignez l'**URL publique du portail** (ex. `https://votredomaine.com`).
   - Cette URL est utilisée dans les emails envoyés aux clients.
5. Cliquez **Enregistrer la configuration**.

---

## Configuration SMTP (emails)

Le portail envoie des emails automatiques (confirmation de commande, notification BAT, réinitialisation de mot de passe). Configurez le SMTP dans **Paramétrages → 🌐 Portail client** → section **Configuration SMTP** :

| Champ | Exemple |
|---|---|
| Serveur SMTP | `smtp.gmail.com` |
| Port | `587` (STARTTLS) ou `465` (SSL) |
| Utilisateur | `portail@votreSoc.fr` |
| Mot de passe | Mot de passe ou App Password |
| Email expéditeur | `portail@votreSoc.fr` |
| Nom expéditeur | `Portail Client` |
| Email de notification atelier | `atelier@votreSoc.fr` |

> **Gmail** : Utilisez un mot de passe d'application (Google App Passwords) si la vérification en 2 étapes est activée.

---

## Création d'un compte client

1. Allez dans **Paramétrages → 🌐 Portail client** → section **Comptes clients**.
2. Cliquez **+ Nouveau client**.
3. Remplissez l'email, mot de passe (min. 8 caractères), nom, société.
4. Cliquez **Enregistrer** → un email de bienvenue est automatiquement envoyé.

### Gérer les comptes

- **Modifier** : mettre à jour les informations du compte.
- **Désactiver / Réactiver** : empêcher / autoriser la connexion sans supprimer les données.
- **Reset MDP** : générer un nouveau mot de passe et le transmettre au client.

---

## URL publique et HTTPS

L'accès au portail se fait via `/portal/` sur le même serveur que l'interface atelier.

**En production, HTTPS est obligatoire** pour :
- Protéger les credentials à la connexion
- Garantir l'intégrité des fichiers PDF uploadés
- Permettre l'affichage des BAT dans les iframes (mixed-content bloqué en HTTP)

### Configuration recommandée (reverse proxy Nginx / IIS) :

```nginx
server {
    listen 443 ssl;
    server_name votredomaine.com;

    ssl_certificate     /etc/letsencrypt/live/votredomaine.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/votredomaine.com/privkey.pem;

    location / {
        proxy_pass http://localhost:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

---

## Workflow BAT côté client

1. **L'atelier envoie un BAT** : depuis la fiche atelier, utiliser `POST /api/admin/portal/orders/{orderId}/send-bat` avec le chemin du fichier BAT. Le statut de la commande passe à `bat_pending` et un email est envoyé au client.

2. **Le client reçoit un email** avec un lien vers `/portal/order.html?id=...`.

3. **Sur la page détail** :
   - Un bandeau jaune "BAT à valider" apparaît en tête de page.
   - Le PDF du BAT est affiché dans une visionneuse intégrée.
   - Le client clique **Valider** ou **Refuser**.
   - En cas de refus : un champ "Motif" obligatoire s'affiche, avec possibilité de joindre un fichier.

4. **Après la décision** :
   - L'action est persistée dans la collection `client_bat_actions`.
   - Le statut de la commande est mis à jour (`in_production` ou `bat_refused`).
   - Un email de notification est envoyé à l'atelier.

---

## Configuration du formulaire de commande

Dans **Paramétrages → 🌐 Portail client** → section **Options du formulaire de commande** :

- **Formats** : liste des formats disponibles (un par ligne)
- **Supports / Papiers** : liste des papiers disponibles
- **Finitions** : liste des finitions proposées

Ces listes sont chargées dynamiquement sur le formulaire de nouvelle commande.

---

## Templates email

Les emails du portail utilisent des templates personnalisables dans **Paramétrages → 🌐 Portail client**. Les clés disponibles :

| Clé | Déclencheur |
|---|---|
| `client_welcome` | Création d'un compte client |
| `client_password_reset` | Demande de réinitialisation de mot de passe |
| `client_order_received` | Confirmation de nouvelle commande |
| `client_bat_available` | BAT envoyé au client |
| `client_order_status_changed` | Changement de statut d'une commande |
| `atelier_client_bat_validated` | Client a validé un BAT |
| `atelier_client_bat_refused` | Client a refusé un BAT (avec motif) |
| `atelier_new_client_order` | Nouvelle commande web reçue |

### Variables disponibles dans les templates :

| Variable | Description |
|---|---|
| `{clientName}` | Nom du client |
| `{orderNumber}` | N° de commande |
| `{orderTitle}` | Intitulé de la commande |
| `{batLink}` | Lien direct vers la page BAT |
| `{portalLink}` | URL du portail |
| `{motif}` | Motif de refus (pour les templates atelier refus) |
| `{companyName}` | Société du client |
| `{resetLink}` | Lien de réinitialisation de mot de passe |

---

## Sécurité

- **Mots de passe** : hashés avec BCrypt (work factor 11).
- **Tokens** : base64 encodé, audience `portal:`, non-interchangeables avec les tokens atelier.
- **Isolation** : vérification systématique du `clientAccountId` avant toute lecture/écriture.
- **Verrouillage de compte** : après N tentatives échouées (configurable, défaut 5), le compte est verrouillé pour X minutes (défaut 30).
- **Upload** : vérification de l'extension ET de la signature magique PDF (`%PDF`).
- **Filename sanitization** : caractères non-alphanumériques remplacés pour éviter le path traversal.
- **Isolation des routes** : `/api/portal/*` ne retourne jamais de données d'un autre client.

---

## Modèle de données (MongoDB)

| Collection | Description |
|---|---|
| `client_accounts` | Comptes clients (email, passwordHash, enabled, etc.) |
| `client_orders` | Commandes déposées via le portail |
| `client_bat_actions` | Historique des BAT envoyés et des décisions client |

Les paramètres du portail sont stockés dans la collection `settings` :
- `portalSettings` — configuration générale
- `portalSmtp` — configuration SMTP
- `portalEmailTemplate_{key}` — templates email

---

## API REST

### Routes client (`/api/portal/`)

```
POST /api/portal/auth/login           — Connexion
POST /api/portal/auth/logout          — Déconnexion
POST /api/portal/auth/forgot-password — Demander un lien de reset
POST /api/portal/auth/reset-password  — Réinitialiser le mot de passe
GET  /api/portal/auth/me              — Info compte courant
GET  /api/portal/me                   — Info compte courant
PUT  /api/portal/me                   — Modifier compte / mot de passe
GET  /api/portal/orders               — Liste des commandes
GET  /api/portal/orders/{id}          — Détail d'une commande + BAT
POST /api/portal/orders               — Créer une commande
POST /api/portal/orders/{id}/files    — Uploader des PDF
GET  /api/portal/orders/{id}/files/{name} — Télécharger un fichier
GET  /api/portal/orders/{id}/bat      — Liste des BAT
GET  /api/portal/orders/{id}/bat/{batId}/file — Voir le PDF du BAT
POST /api/portal/orders/{id}/bat/{batId}/validate — Valider un BAT
POST /api/portal/orders/{id}/bat/{batId}/refuse   — Refuser un BAT
GET  /api/portal/config/form-options  — Options du formulaire (formats, papiers, finitions)
```

### Routes admin (`/api/admin/portal/`)

```
GET  /api/admin/portal/settings            — Lire la configuration
PUT  /api/admin/portal/settings            — Modifier la configuration
GET  /api/admin/portal/clients             — Liste des clients
POST /api/admin/portal/clients             — Créer un client
PUT  /api/admin/portal/clients/{id}        — Modifier un client
DELETE /api/admin/portal/clients/{id}      — Désactiver un client
POST /api/admin/portal/clients/{id}/reset-password — Reset mot de passe
POST /api/admin/portal/orders/{id}/send-bat — Envoyer un BAT au client
PUT  /api/admin/portal/orders/{id}/status  — Mettre à jour le statut depuis l'atelier
GET  /api/admin/portal/orders              — Liste de toutes les commandes portail
GET  /api/admin/portal/email-templates     — Lire les templates email portail
PUT  /api/admin/portal/email-templates/{key} — Modifier un template email portail
```

---

## Champs du formulaire configurables

L'admin peut configurer exactement quels champs de la fiche sont exposés dans le formulaire "Nouvelle commande" du portail.

### Accès

**Paramétrages → 🌐 Portail client → 📋 Champs du formulaire**

### Fonctionnalités

- **Visible** : afficher ou masquer chaque champ côté portail
- **Obligatoire** : rendre le champ requis (les champs critiques ne peuvent pas être rendus optionnels)
- **Libellé personnalisé** : override le libellé interne affiché au client
- **Aide / placeholder** : texte court affiché sous le champ
- **Valeurs autorisées** : pour les champs à options (formats, papiers, finitions) — restreindre la sélection client
- **Ordre** : utiliser les boutons ↑ ↓ pour réordonner les champs

### Champs critiques 🔒

Ces champs sont obligatoires et ne peuvent pas être masqués :
- **Intitulé du job** (`title`)
- **Quantité** (`quantity`)
- **Mode de livraison** (`delivery-mode`)

### Endpoints API

```
GET  /api/admin/portal/form-fields          — Lire la configuration des champs (admin)
PUT  /api/admin/portal/form-fields          — Modifier la configuration des champs (admin)
GET  /api/portal/config/form-fields         — Champs visibles pour le formulaire client (public)
```

---

## Apparence et mise en forme

L'admin peut personnaliser l'apparence de toutes les pages du portail client.

### Accès

**Paramétrages → 🌐 Portail client → 🎨 Apparence**

### Options disponibles

- **Couleurs** : couleur principale, foncée, fond, texte (sélecteur + saisie hex)
- **Typographie** : choix d'une police (Inter, Roboto, Open Sans, Lato, Arial, système)
- **En-tête** : nom de l'entreprise, slogan, lien "Nous contacter"
- **Pied de page** : mentions légales, CGV, copyright
- **Page "Mes commandes"** : texte personnalisé
- **CSS personnalisé** : code CSS appliqué uniquement sur `/portal/*`

### Endpoints API

```
GET  /api/admin/portal/theme                — Lire la configuration du thème (admin)
PUT  /api/admin/portal/theme                — Modifier la configuration du thème (admin)
GET  /api/portal/config/theme               — Thème actif (public, utilisé par les pages portail)
```

### Variables CSS injectées

Les couleurs sont appliquées via des variables CSS sur `:root` :
- `--color-primary` — Couleur principale
- `--color-primary-dark` — Couleur principale foncée (hover)
- `--color-primary-light` — Couleur principale claire (fonds)
- `--color-gray-50` — Couleur de fond
- `--color-gray-700` — Couleur du texte
