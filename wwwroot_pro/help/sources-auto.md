# 5. Sources de commandes automatiques

Récupérez automatiquement des fichiers depuis des espaces de stockage cloud ou SFTP distants.

## Sources disponibles

- **SFTP** — Connexion par mot de passe ou clé SSH
- **Dropbox** — Via OAuth2 (app Dropbox requise)
- **Google Drive** — Via OAuth2 (app Google requise)
- **Box** — Via OAuth2 (app Box requise)
- **OneDrive / Office 365** — Via Microsoft Graph API

## Configuration

1. **Paramétrages → 🔌 Intégrations → Sources de commandes**
2. Cliquez **"+ Ajouter une source"**
3. Choisissez le type, renseignez les credentials et le dossier à surveiller.
4. Définissez un **mapping client** : les commandes détectées dans ce dossier sont associées à ce client.

## Fonctionnement

- Un **worker de polling** vérifie les sources à l'intervalle configuré (défaut : 5 min).
- Les fichiers déjà traités sont **anti-doublonnés** par hash SHA-256.
- Les journaux d'imports sont disponibles dans **Paramétrages → Intégrations → Journal des imports**.

## Convention de nommage

Pour les sources SFTP/Cloud, respectez la même convention que le hotfolder local :
```
[NumCommande]_[Titre].pdf
[NumCommande]_[Titre].xml  (optionnel)
```
