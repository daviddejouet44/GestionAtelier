## Précisions complémentaires

### Nommage des étapes côté client (point 1.4)
L'**administrateur doit pouvoir personnaliser le libellé** de chaque étape affichée au client. Donc en plus de choisir quelles tuiles/étapes sont visibles, il doit pouvoir saisir un nom personnalisé (label client) pour chacune, distinct du nom interne de la tuile Kanban.

### MailKit (point 2)
Le warning exact remonté à la compilation est :
```
C:\FluxAtelier\GestionAtelier\GestionAtelier.csproj : warning NU1902: Le package 'MailKit' 4.3.0 présente une vulnérabilité de gravité moyenne connue, https://github.com/advisories/GHSA-9j88-vvj5-vhgr.
```
Il s'agit de l'advisory GHSA-9j88-vvj5-vhgr. Mettre à jour MailKit (et MimeKit si nécessaire) vers une version corrigeant cette vulnérabilité (la version 4.3.0 actuellement installée est vulnérable — passer à une version récente non vulnérable, par ex. 4.7.x ou plus récent selon la résolution de l'advisory). Adapter le code si l'API a changé.

### Format d'invitation client (point 1.6)
L'invitation des clients au portail se fait **par e-mail**. Prévoir donc :
- Un bouton "Inviter un client" dans l'admin.
- Un envoi d'e-mail contenant le lien d'accès au portail (et de création/activation de compte ou de définition de mot de passe).
- Un template e-mail dédié pour l'invitation (réutilisable, modifiable depuis l'admin).