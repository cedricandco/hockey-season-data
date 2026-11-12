# Plan d'action — Base de données PostgreSQL + EF Core

> Contexte : le backend (`HockeySeason.Functions`, .NET 8, Clean Architecture ; l'ancien
> projet `HockeySeason.Api` a été supprimé) fonctionne
> aujourd'hui sans base de données. On ajoute une persistance PostgreSQL
> (hébergée) pour gérer les utilisateurs (Auth0), leurs licences, leurs abonnements
> (équipes / ligues / associations) et leurs préférences visuelles. Les données de
> référence (associations, divisions, ligues, équipes) sont **synchronisées** depuis
> l'API publique Spordle déjà intégrée (`HockeySeason.LeagueApi`).

## Décisions validées

| Sujet | Décision |
|---|---|
| Fournisseur | PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`), nommage `snake_case` |
| PK | `id INT` (identity), `external_id TEXT` pour les identifiants Spordle |
| Migrations | Appliquées **automatiquement au démarrage** de l'API (hébergement mutualisé) |
| Provisionnement user | Upsert idempotent à la **première requête authentifiée** |
| Licences | Rôles `COACH`, `DIRECTOR`, `PRESIDENT` (`user_roles` + `role_limit`) |
| Limits | `roles.max_followed_entities` : 5 équipes (coach), 5 ligues (director) |
| Table de lien | `TeamSubscription` (user ↔ team), plus `LeagueSubscription` (user ↔ league) |
| Verrou | Verrou 15 jours après ajout, bypass par admin (`locked_until` + `unlock_requested_at`) |
| Admin | Colonne `is_admin` sur `users` **et** claim Auth0 (défense en profondeur) |
| Secret | Chaîne de connexion en **user-secrets** (dev) / env var (prod) — jamais dans le repo |
| Saison | Table `Season` + `season_id` sur `League` (le leagueId Spordle est saison-spécifique) |
| Settings | Une ligne par utilisateur (couleurs primaires/secondaires + clés génériques) |
| Données de référence | Endpoint admin d'import à la demande depuis Spordle |

---

## 1. Schéma de base de données (cible)

```text
users ─┬─< user_roles >─┬─ roles            (licence : COACH / DIRECTOR / PRESIDENT)
       ├─ 1 user_settings          (couleurs, clés génériques)
       ├─< team_subscriptions >─┬─ teams ─┬─> leagues ─> divisions ─> organizations
       │                        └─ locked until (verrou 15 j)   │
       └─< league_subscriptions >──────────────────────────────┘  (directors: 5 ligues)
                                     └─> seasons
```

### Tables

| Table | Colonnes | Rôle |
|---|---|---|
| `organizations` | `id`, `name`, `external_id` (TEXT UNIQUE), `created_at`, `updated_at` | Associations (ex. Hockey Richelieu) |
| `divisions` | `id`, `organization_id` FK, `name`, `external_id` UNIQUE, `created_at`, `updated_at` | Offices Spordle (6193, 6194) |
| `seasons` | `id`, `name`, `external_id` TEXT UNIQUE, `created_at`, `updated_at` | Saisons Spordle (2026-27, …) |
| `leagues` | `id`, `division_id` FK, `season_id` FK, `name`, `external_id` UNIQUE, `created_at`, `updated_at` | Ligues Spordle (saison-spécifiques) |
| `teams` | `id`, `league_id` FK, `name`, `external_id` UNIQUE, `created_at`, `updated_at` | Équipes Spordle |
| `users` | `id`, `auth0_user_id` UNIQUE string, `email`, `name`, `first_name`, `last_name`, `stripe_customer_id` (nullable), `is_admin`, `created_at`, `updated_at` | Utilisateurs Auth0 + facturation future |
| `roles` | `id`, `code` UNIQUE (coach/director/president), `name`, `max_followed_entities` | Licence : limite logicielle |
| `user_roles` | `id`, `user_id` FK, `role_id` FK, `created_at` | Attribution des licences (UNIQUE user+role) |
| `team_subscriptions` | `id`, `user_id` FK, `team_id` FK, `created_at`, `updated_at`, `locked_until`, `unlock_requested_at` | Suivi d'équipe (+ verrou 15 j) |
| `league_subscriptions` | `id`, `user_id` FK, `league_id` FK, `created_at`, `updated_at` | Suivi de ligue (rôle DIRECTOR) |
| `user_settings` | `id`, `user_id` UNIQUE FK, `primary_color`, `secondary_color`, `extra` (jsonb), `created_at`, `updated_at` | Préférences visuelles |

Règles clés :
- **Unique (user_id, team_id)** sur `team_subscriptions` ; **unique (user_id, league_id)** sur `league_subscriptions`.
- `locked_until` = `created_at + 15 jours` par défaut (colonne calculée par l'app, pas par la BD) ;
  `unlock_requested_at` sert de trace d'audit pour l'interface admin.
- Le périmètre visible d'un utilisateur = **union** de :
  1. ses `team_subscriptions` → équipes suivies ;
  2. ses `league_subscriptions` → toutes les équipes des ligues suivies ;
  3. les ligues/divisions/organizations **parents** des entités ci-dessus (lecture seule, en cascade).
- Un utilisateur **PRESIDENT** détient une `league_subscription` sur chaque ligue de son
  association, ou mieux : un abonnement direct à l'**organization** — à décider
  (voir ⚠ Question ouverte A).

### ⚠ Question ouverte A — l'abonnement PRESIDENT

Deux options :
- **A1** : au provisionnement d'un rôle PRESIDENT, l'admin
  (ou un job) crée une `league_subscription` par ligue de l'association — simple,
  mais duplique les lignes et casse si on ajoute une ligue ensuite.
- **A2 (choisi)** : table `organization_subscriptions` (user_id FK, organization_id FK,
  UNIQUE(user, org)), périmètre = toutes les ligues de l'organisation suivie.
  Implémentée en Phase 1 et consommée par le `ScopeService` en Phase 3.

---

## 2. Plan d'action avec cases à cocher

### Phase 0 — Infrastructure de données (0,5 j)
- [x] Créer le projet `HockeySeason.Infrastructure` : référence EF Core + Npgsql
  ([doc Npgsql EF](https://www.npgsql.org/efcore/)) — actuellement ce projet ne
  contient que `UtcTimeProvider` : on y ajoute l'accès aux données.
- [x] `HockeySeasonDbContext` (namespace `HockeySeason.Infrastructure.Data`) avec
  snake_case via `UseSnakeCaseNamingConvention()` (package `EFCore.NamingConventions`).
- [x] Chaîne de connexion : `ConnectionStrings:Postgres` lue depuis user-secrets
  (dev) / env var `ConnectionStrings__Postgres` (prod). Password réel de
  `pg8001.site4now.net` **jamais** committé.
- [x] Configuration `MigrateDatabaseOnStartup` (default `false` en dev, `true` en prod) —
  implémentée sous la clé `Database:MigrateOnStartup`.
- [x] `GET /readyz` vérifie `Database.CanConnectAsync()` en plus de la dispo API.

### Phase 1 — Entités et migrations (1 j)
- [x] Entités EF : `User`, `Role`, `UserRole`, `UserSetting`, `Organization`, `Division`,
  `Season`, `League`, `Team`, `TeamSubscription`, `LeagueSubscription`
  — plus `OrganizationSubscription` (décision A2 confirmée). (nommage C# PascalCase ↔ colonnes snake_case).
- [x] Config Fluent API : FK, cascades, index uniques (external_id, auth0_user_id,
  unique(user,team), unique(user,league)), max_followed_entities defaults 5/5.
- [x] Migration initiale `InitialCreate` générée et commitée (`Migrations/`).
- [x] (Optionnel) Script SQL généré pour revue manuelle + rollback :
  `docs/database-schema.sql` (idempotent, régénéré via `dotnet ef migrations script`).

### Phase 2 — Provisionnement utilisateurs (0,5 j)
- [x] Service `IUserProvisioningService.ProvisionAsync(ApplicationUserClaims)` : extrait
  `sub`/`email`/`given_name`/`family_name` du JWT, upsert idempotent (aucune ligne
  créée si déjà présent, aucun email écrasé).
- [x] **À la connexion** : middleware global `UserProvisioningMiddleware`
  (après `UseAuthentication`) — le premier appel authentifié d'un utilisateur
  enregistre sa ligne `users`, quel que soit l'endpoint contacté.
- [x] Résolveur par requête (`RequestUserResolver` dans l'hôte Api) appelé au début
  de chaque endpoint utilisateur — idempotent par construction (SELECT avant INSERT).
- [x] Sync du statut admin : `is_admin` = (colonne DB) **OU** claim Auth0 `roles`
  contient `admin`, exposé via `GET /api/users/me` avec ses rôles + limites.

### Phase 3 — Périmètre et autorisations API (1,5 j)
- [x] `IScopeService.GetScopeAsync(userId)` — union des team_subscriptions
  (directes), des équipes des league_subscriptions, et (A2) des ligues de
  l'org suivie. Résultat **mis en cache** (IMemoryCache, 60 s) par userId.
- [x] Brancher le service sur `/api/weekly-report` : les `divisionId`/`teamId`
  fournis par le client sont **filtrés/validés** contre le périmètre (IDs hors
  périmètre ignorés, erreur 403 si rien d'autorisé).
- [x] Brancher `/api/games/{id}`, `/api/leagues/{id}/teams`, `/api/leagues/{id}/teams/{id}`
  : 403 si ligue/team hors périmètre (défense en profondeur : jamais de confiance
  dans les paramètres de query).
- [x] Tests unitaires : matrice (rôle × périmètre × endpoint), 401/403/200.

### Phase 4 — Endpoints utilisateurs & abonnements (1 j)
- [x] `GET /api/users/me` — profil + rôles + limites + settings.
- [x] `PUT /api/users/me/settings` — couleurs primaires/secondaires (+ extras).
- [x] `POST /api/users/me/team-subscriptions` / `DELETE /api/users/me/team-subscriptions/{teamId}` — avec vérification de la limite du rôle (5 max coach).
- [x] `POST /api/users/me/league-subscriptions` / DELETE équivalent (5 max director).
- [x] Logique de verrou : `locked_until = created_at + 15 j` à la création ;
  DELETE refusé si verrou actif **sauf** admin (ou `unlock_requested_at` tracé,
  via `POST /api/users/me/team-subscriptions/{teamId}/unlock-request`).
- [x] Tests unitaires sur la logique de verrou (admin bypass, tentative sous verrou).

### Phase 5 — Admin : import des données de référence (1 j)
- [x] Endpoints réservés admin (flag `is_admin` **ou** claim `roles=admin`) :
  `POST /api/admin/import/offices`, `/leagues`, `/teams` (synchronisation Spordle
  upsert par external_id).
- (optionnel) UI admin minimal : liste + boutons d'import. À planifier plus tard,
  l'import peut aussi être déclenché via Swagger/curl.

---

## 3. Diagramme de schéma (Mermaid)

```mermaid
erDiagram
    ORGANIZATIONS ||--o{ DIVISIONS : contains
    DIVISIONS ||--o{ LEAGUES : contains
    SEASONS   ||--o{ LEAGUES : scopes
    LEAGUES   ||--o{ TEAMS : contains
    USERS     ||--o{ USER_ROLES }o--|| ROLES
    USERS     ||--o| USER_SETTINGS
    USERS     ||--o{ TEAM_SUBSCRIPTIONS }o--|| TEAMS
    USERS     ||--o{ LEAGUE_SUBSCRIPTIONS }o--|| LEAGUES
    USERS     ||--o{ ORGANIZATION_SUBSCRIPTIONS }o--|| ORGANIZATIONS
```

## 4. Ordre de livraison

1. Phase 0 + 1 : l'API démarre avec la BD migrée (aucune fonctionnalité utilisateur).
2. Phase 2 + 3 : provisionnement + périmètre appliqué aux endpoints existants.
3. Phase 4 + 5 : gestion des abonnements + import admin.

---

## 5. Livrables

- `docs/database-action-plan.md` — ce plan (cases à cocher).
- `docs/database-schema.sql` — schéma SQL de référence (revue manuelle).
- Véritable source de vérité : les **migrations EF Core** dans
  `HockeySeason.Infrastructure/Migrations/`, appliquées au démarrage.

## 6. Suivi

| Date | Étape | Statut |
|---|---|---|
| 2026-11-10 | Schéma + plan validés avec Cédric | ✅ Revu |
| 2026-11-11 | Phase 0 — DbContext Npgsql, connexion user-secrets, migration au démarrage, readyz BD | ✅ Implémenté |
| 2026-11-11 | Phase 1 — Entités + Fluent (A2 : `organization_subscriptions`), migration `InitialCreate`, script SQL de revue | ✅ Implémenté |
| 2026-11-11 | Phase 2 — Provisionnement idempotent (`RequestUserResolver`, `UserProvisioningService`) | ✅ Implémenté |
| 2026-11-11 | Phase 3 — Périmètre (`ScopeService`, cache 60 s) branché sur les endpoints Spordle | ✅ Implémenté |
| 2026-11-11 | Phase 4 — Endpoints `/api/users/me` (profil, settings, abonnements, verrou 15 j) | ✅ Implémenté |
| 2026-11-11 | Phase 5 — Import admin Spordle (`/api/admin/import/offices|leagues|teams`) | ✅ Implémenté |
| 2026-11-11 | Tests : 58 xUnit verts (incl. `PersistenceServiceTests` + `UserProvisioningMiddlewareTests`) | ✅ Vérifié |
| À faire | Créer le serveur PostgreSQL et set le user-secret `ConnectionStrings:Postgres` puis `dotnet ef database update` | ➡️ Prochaine étape |
| À faire | Attribuer les rôles (licences) aux utilisateurs + statut admin dans la BD / Auth0 | ➡️ Prochaine étape |
| 2026-11-11 | Accès ouvert temporaire (`Authorization:OpenAccess=true`) : périmètre, licences, verrou 15 j et garde admin désactivés pour tout utilisateur authentifié | ✅ Implémenté |

