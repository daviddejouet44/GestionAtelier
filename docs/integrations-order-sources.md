# Sources de commandes automatiques — Documentation

## Vue d'ensemble

La fonctionnalité **Sources de commandes automatiques** permet à GestionAtelier de récupérer automatiquement
des commandes (PDF + métadonnées) depuis des serveurs distants sans intervention manuelle.

### Sources supportées

| Type       | Protocole       | Authentification                |
|------------|-----------------|----------------------------------|
| **SFTP**   | SSH/SFTP        | Mot de passe ou clé privée PEM  |
| **Dropbox**| API Dropbox v2  | OAuth2 (refresh token)           |

> **Architecture extensible** : WebDAV, OneDrive/SharePoint, Google Drive, S3, FTPS peuvent être ajoutés
> en implémentant `IOrderSourceProvider`.

---

## Convention de dossiers

Pour chaque source configurée, la structure attendue sur le serveur distant est :

```
{baseDir}/
  clients/
    {clientId}/
      in/            ← Nouveaux fichiers à traiter
      processed/     ← Fichiers traités (déplacés avec horodatage)
      error/         ← Fichiers en erreur (+ fichier .error.txt)
```

Le `clientId` (nom du dossier) sert de mapping vers un client GestionAtelier. Ce mapping se configure
dans le formulaire de chaque source (onglet **Sources de commandes**).

Exemple :
- `/flux/clients/dupont/in/` → fichiers du client "Dupont"
- `/flux/clients/smith/in/`  → fichiers du client "Smith"

---

## Configuration d'une source SFTP

### Prérequis

- Un serveur SFTP accessible depuis la machine GestionAtelier
- Un compte utilisateur avec droits lecture/écriture sur le répertoire configuré
- La bibliothèque **SSH.NET** (`Renci.SshNet`) est déjà incluse

### Étapes

1. Ouvrez **Paramétrages → Intégrations → 📡 Sources de commandes**
2. Cliquez sur **+ Ajouter une source**
3. Sélectionnez le type **SFTP**
4. Renseignez :
   - **Nom** : libellé lisible (ex : "SFTP Impression Lyon")
   - **Serveur** : hostname ou IP (ex : `sftp.impression-lyon.fr`)
   - **Port** : 22 par défaut
   - **Utilisateur** : nom de compte SSH
   - **Mot de passe** OU **Clé privée PEM** (format OpenSSH/PEM)
     - Si clé privée : collez le contenu PEM complet (y compris les lignes `-----BEGIN/END-----`)
     - Passphrase optionnelle si la clé est protégée
   - **Répertoire de base** : chemin racine sur le serveur (ex : `/flux/atelier`)
   - **Empreinte hôte** (optionnel) : vérification de l'empreinte du serveur pour éviter les attaques MITM
5. Configurez le **mapping dossiers → clients**
6. Définissez l'**intervalle de polling** (minimum 1 minute, défaut 5 minutes)
7. Cliquez sur **💾 Enregistrer**
8. Testez la connexion via le bouton **🔌 Tester**

### Authentification par clé privée

Format attendu (PEM RSA) :
```
-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEA...
...
-----END RSA PRIVATE KEY-----
```

Format OpenSSH (nécessite conversion avec `ssh-keygen -p -m PEM`) :
```
-----BEGIN OPENSSH PRIVATE KEY-----
...
-----END OPENSSH PRIVATE KEY-----
```

---

## Connexion Dropbox (OAuth2)

### Prérequis

1. Créez une application sur [Dropbox App Console](https://www.dropbox.com/developers/apps) :
   - Type : **Scoped access** → **App folder** ou **Full Dropbox**
   - Permissions : `files.content.read`, `files.content.write`, `account_info.read`
2. Dans les paramètres Dropbox de l'app, ajoutez l'URL de callback :
   `http://votre-serveur:5080/api/integrations/dropbox/callback`

### Configuration globale

1. Ouvrez **Paramétrages → Intégrations → Sources de commandes → ☁️ Config Dropbox globale**
2. Renseignez l'**App Key** et l'**App Secret** de votre application Dropbox
3. Vérifiez l'**URL de callback** (doit correspondre exactement à ce qui est configuré dans Dropbox)
4. Cliquez sur **💾 Enregistrer**

### Connexion d'une source Dropbox

1. Créez une source de type **Dropbox** (sans les credentials OAuth — ceux-ci sont globaux)
2. Enregistrez la source
3. Cliquez sur **✏️ Modifier** puis **🔗 Connecter Dropbox**
4. Une fenêtre Dropbox s'ouvre → autorisez l'accès
5. Dropbox redirige vers GestionAtelier avec un code d'autorisation
6. Le **refresh token** est automatiquement stocké chiffré en base de données
7. Testez via **🔌 Tester**

---

## Détection du type de fichier

Le service identifie automatiquement les fichiers déposés dans `/clients/{clientId}/in/` :

| Fichier             | Traitement                                                    |
|---------------------|---------------------------------------------------------------|
| `commande.pdf`      | Fiche créée en **brouillon** avec préremplissage minimal      |
| `commande.pdf` + `commande.xml` | Fiche créée avec métadonnées XML (mapping XML import) |
| `commande.pdf` + `commande.json` | Fiche créée avec métadonnées JSON               |
| `commande.pdf` + `commande.csv` | Fiche créée avec métadonnées CSV (1ère ligne = entêtes) |

> Le PDF est stocké dans le hotfolder `Soumission`, comme un import manuel.

---

## Format des métadonnées XML/JSON

### XML (réutilise le mapping de l'import XML)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<Commande>
  <referenceCommande>CMD-2024-001</referenceCommande>
  <nomClient>Dupont Imprimerie</nomClient>
  <quantite>500</quantite>
  <formatFini>A4</formatFini>
  <typeTravail>Brochure</typeTravail>
  <dateReceptionSouhaitee>2024-12-15</dateReceptionSouhaitee>
  <commentaire>Recto-verso, 4 couleurs</commentaire>
</Commande>
```

### JSON

```json
{
  "referenceCommande": "CMD-2024-001",
  "nomClient": "Dupont Imprimerie",
  "quantite": 500,
  "formatFini": "A4",
  "typeTravail": "Brochure",
  "dateReceptionSouhaitee": "2024-12-15",
  "commentaire": "Recto-verso, 4 couleurs"
}
```

### CSV (séparateur `;`)

```csv
referenceCommande;nomClient;quantite;formatFini
CMD-2024-001;Dupont Imprimerie;500;A4
```

> Le mapping des champs se configure dans **Paramétrages → Intégrations → 📥 Import XML → Mapping**.

---

## Anti-doublon

Chaque fichier est identifié par son **hash SHA-256**. Si le même fichier (même contenu) est soumis
plusieurs fois, il est automatiquement déplacé dans `/error/` avec le motif "Doublon détecté" et
aucune fiche n'est créée.

---

## Journal des imports

Accessible via **Paramétrages → Intégrations → Sources de commandes → 📋 Journal des imports** :

- Date/heure, source, dossier client, fichier, statut (succès / erreur / doublon)
- Lien vers la fiche de production créée
- Message d'erreur détaillé
- Export CSV

---

## API REST

Toutes les routes sont protégées par le rôle **admin** (header `Authorization: Bearer <token>`).

| Méthode | Route | Description |
|---------|-------|-------------|
| `GET`   | `/api/integrations/order-sources` | Liste des sources |
| `POST`  | `/api/integrations/order-sources` | Créer une source |
| `PUT`   | `/api/integrations/order-sources/{id}` | Modifier une source |
| `DELETE`| `/api/integrations/order-sources/{id}` | Supprimer une source |
| `POST`  | `/api/integrations/order-sources/{id}/test` | Tester la connexion |
| `POST`  | `/api/integrations/order-sources/{id}/run` | Lancer un cycle manuel |
| `GET`   | `/api/integrations/order-sources/{id}/logs` | Logs de cette source |
| `GET`   | `/api/integrations/order-sources/logs` | Journal global |
| `GET`   | `/api/integrations/dropbox/authorize?sourceId=…` | URL OAuth2 Dropbox |
| `GET`   | `/api/integrations/dropbox/callback` | Callback OAuth2 Dropbox |
| `GET`   | `/api/integrations/dropbox/global-config` | Config globale Dropbox |
| `PUT`   | `/api/integrations/dropbox/global-config` | Modifier config Dropbox |

---

## Sécurité

- Les **credentials** (mots de passe, clés privées, tokens) sont **chiffrés en AES-256** avant stockage
  en base de données. Ils ne sont jamais exposés dans les API ni dans les logs.
- La clé de chiffrement peut être personnalisée via la variable d'environnement `GA_ENCRYPTION_KEY`
  (chaîne d'au moins 32 caractères). En production, utilisez une valeur aléatoire forte.
- Les logs ne contiennent jamais de credentials.

### Variable d'environnement recommandée (production)

```bash
export GA_ENCRYPTION_KEY="votre-cle-de-32-caracteres-minimum"
```

---

## Dépannage

### SFTP — Connexion refusée

- Vérifiez que le port 22 (ou celui configuré) est ouvert sur le firewall
- Vérifiez les identifiants utilisateur / mot de passe
- En cas de clé privée : assurez-vous que la clé est au format PEM (et non OpenSSH)
  → Conversion : `ssh-keygen -p -m PEM -f ~/.ssh/id_rsa`

### SFTP — Dossier `/in/` non trouvé

Le service crée automatiquement les dossiers manquants (`in/`, `processed/`, `error/`).
Assurez-vous que l'utilisateur SFTP a les droits de **création de dossiers**.

### Dropbox — Erreur "Token expiré"

Le service utilise un **refresh token** (permanent). Si le token est révoqué (déconnexion depuis
le compte Dropbox ou révocation de l'app), re-cliquez sur **🔗 Connecter Dropbox**.

### Dropbox — "App Key non configurée"

Configurez d'abord l'App Key et l'App Secret dans **Config Dropbox globale**.

### Les fichiers ne sont pas traités

1. Vérifiez que la source est **active** (statut Actif)
2. Vérifiez les logs via **📋 Journal des imports**
3. Lancez un cycle manuel via **▶ Lancer** pour voir immédiatement
4. Consultez les logs du serveur (`dotnet run` / console) pour les erreurs détaillées
