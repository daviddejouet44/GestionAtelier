# 9. Profils utilisateurs

GestionAtelier utilise des profils pour contrôler les accès. Chaque utilisateur possède un profil qui détermine ce qu'il peut voir et faire.

## Profils disponibles

| Profil | Niveau | Accès |
|--------|--------|-------|
| **Admin** | 3 | Accès total — paramétrage, comptes, kanban, rapports |
| **Opérateur** | 2 | Kanban, soumission, fabrication, BAT |
| **Planning** | 2 | Vue planning, réassignation des dates |
| **Soumission** | 1 | Soumission uniquement (pas de kanban) |
| **Finition** | 1 | Vue finitions uniquement |
| **Client** | 0 | Portail client uniquement (`/portal/*`) |

## Gestion des comptes

- **Comptes internes** (admin, opérateurs) : **Paramétrages → Comptes & Rôles**
- **Comptes clients** (portail) : **Paramétrages → 🌐 Portail client → 👥 Comptes clients**

## Droits par fonctionnalité

| Fonctionnalité | Admin | Opérateur | Planning | Soumission |
|----------------|-------|-----------|----------|------------|
| Kanban complet | ✅ | ✅ | ✅ | ❌ |
| Soumission | ✅ | ✅ | ❌ | ✅ |
| Paramétrage | ✅ | ❌ | ❌ | ❌ |
| Rapports | ✅ | ❌ | ❌ | ❌ |
| BAT | ✅ | ✅ | ❌ | ❌ |
| Dashboard | ✅ | ✅ | ✅ | ❌ |

## Isolation portail client

Les comptes clients (portail) sont **complètement isolés** des comptes internes. Un client ne voit que ses propres commandes et ne peut jamais accéder à l'interface d'atelier.
