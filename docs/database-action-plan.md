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
| `organizations` | `id`, `name`, `external_id` (TEXT UNIQUE), `created_at`, `updated_at` | Associations (ex. Hockey Richelieu, région Spordle 6157) |
| `divisions` | `id`, `organization_id` FK, `name`, `external_id` UNIQUE, `created_at`, `updated_at` | Offices Spordle (6193, 6194) |
| `seasons` | `id`, `name`, `external_id` TEXT UNIQUE, `created_at`, `updated_at` | Saisons Spordle (2026-27, …) |
| `leagues` | `id`, `division_id` FK, `season_id` FK, `name`, `external_id`, `created_at`, `updated_at` | Catégories Spordle (« M13 B », nom français), une par (saison, catégorie) |
| `league_schedules` | `id`, `league_id` FK, `external_id`, `name`, `created_at`, `updated_at` | Horaires Spordle d'une ligue : ids utilisés par les appels classement / matchs / suspensions |
| `teams` | `id`, `league_id` FK, `name`, `external_id`, `created_at`, `updated_at` | Équipes Spordle |
| `users` | `id`, `auth0_user_id` UNIQUE string, `email`, `name`, `first_name`, `last_name`, `stripe_customer_id` (nullable), `is_admin`, `created_at`, `updated_at` | Utilisateurs Auth0 + facturation future |
| `roles` | `id`, `code` UNIQUE (coach/director/president), `name`, `max_followed_entities` | Licence : limite logicielle |
| `user_roles` | `id`, `user_id` FK, `role_id` FK, `created_at` | Attribution des licences (UNIQUE user+role) |
| `team_subscriptions` | `id`, `user_id` FK, `team_id` FK, `created_at`, `updated_at`, `locked_until`, `unlock_requested_at` | Suivi d'équipe (+ verrou 15 j) |
| `league_subscriptions` | `id`, `user_id` FK, `league_id` FK, `created_at`, `updated_at` | Suivi de ligue (rôle DIRECTOR) |
| `user_settings` | `id`, `user_id` UNIQUE FK, `primary_color`, `secondary_color`, `extra` (jsonb), `created_at`, `updated_at` | Préférences visuelles |
| `agent` | `id`, `agent_name` (TEXT UNIQUE) | Agents IA dont l'usage est comptabilisé (l'assistant `hockey-season-data.agent` est inséré par la migration) |
| `message` | `id`, `agent_id` FK, `text` | **Questions** posées par les utilisateurs (les réponses ne sont pas stockées) |
| `token_usage` | `id`, `agent_id` FK, `input_token_count`, `output_token_count`, `total_token_count`, `reasoning_tokens`, `user_id` FK (nullable), `message_id` FK (nullable) | Une ligne par appel d'agent ; `reasoning_tokens` est **inclus** dans l'output |

Règles clés :
- **Unique (division_id, season_id, external_id)** sur `leagues` : l'id Spordle d'une catégorie est partagé par toutes les saisons **et par toutes les divisions qui la jouent**, une ligue est donc le triplet (division, saison, catégorie). Importer une division ne touche jamais la ligue d'une autre.
- **Unique (league_id, external_id)** sur `teams` et sur `league_schedules` : l'id Spordle est unique *dans la ligue*. Une même équipe ou un même horaire peut être publié par deux divisions (un office régional republie les horaires de ses offices enfants), et importer l'une ne déplace jamais la ligne de l'autre.
- **Unique (user_id, team_id)** sur `team_subscriptions` ; **unique (user_id, league_id)** sur `league_subscriptions`.
- **Unique (agent_name)** sur `agent` ; `token_usage.user_id` / `token_usage.message_id` sont
  nullable et `ON DELETE SET NULL` : supprimer un utilisateur ou une question ne doit
  jamais effacer la comptabilité. `reasoning_tokens` fait **partie** de
  `output_token_count` (comptabilité `UsageDetails`), et un compteur non publié par
  le fournisseur reste `NULL`.
- `locked_until` = `created_at + 15 jours` par défaut (colonne calculée par l'app, pas par la BD) ;
  `unlock_requested_at` sert de trace d'audit pour l'interface admin.
- Le périmètre visible d'un utilisateur = **union** de :
  1. ses `team_subscriptions` → équipes suivies ;
  2. ses `league_subscriptions` → toutes les équipes des ligues suivies ;
  3. les ligues/divisions/organizations **parents** des entités ci-dessus (lecture seule, en cascade).
- Un utilisateur **PRESIDENT** détient une `league_subscription` sur chaque ligue de son
  association, ou mieux : un abonnement direct à l'**organization** — à décider
  (voir ⚠ Question ouverte A).

### Import de référence — portée des suppressions

Un import ne supprime **que** dans la division qu'il met à jour, et pour la
saison importée : les ligues, horaires et équipes d'une autre division, d'une
autre association ou d'une saison passée ne sont jamais touchés. C'est la
contrainte qui a dicté les clés ci-dessus — sans la division dans la clé, un
import retrouvait la ligue d'une autre division (même guid de catégorie),
la reparentait, puis supprimait ses équipes devenues orphelines.

Deux garde-fous complètent la règle :
- une catégorie sans équipe publiée est ignorée ;
- un import **incomplet** (Spordle n'a pas répondu pour une catégorie ou pour un
  horaire, ou n'a publié aucune catégorie) **ne supprime rien** : les lignes
  manquantes sont inconnues, pas disparues.

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
  créée si déjà présent ; le profil est rafraîchi à chaque requête, Auth0 étant
  la source de vérité — voir 2026-11-26).
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
- [x] `POST /api/users/me/team-subscriptions` / `DELETE /api/users/me/team-subscriptions/{teamId}` — avec vérification de la limite du rôle (`roles.max_followed_entities`, 5 pour COACH) ; le retrait sous verrou répond `423 Locked`.
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
    AGENT     ||--o{ MESSAGE : receives
    AGENT     ||--o{ TOKEN_USAGE : accounted_for
    MESSAGE   ||--o| TOKEN_USAGE : answers
    USERS     ||--o{ TOKEN_USAGE : consumes
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
| 2026-11-18 | Page d'admin : liste des utilisateurs + attribution des rôles (`GET /api/users`, `GET /api/roles`, `PUT /api/users/{userId}/roles`, page `/admin` réservée aux `is_admin`) | ✅ Implémenté |
| 2026-11-11 | Accès ouvert temporaire (`Authorization:OpenAccess=true`) : périmètre, licences, verrou 15 j et garde admin désactivés pour tout utilisateur authentifié | ✅ Implémenté |
| 2026-11-18 | Admin : import de référence par association/division/saison (`POST /api/reference/import`) — les ligues sont les **catégories Spordle** (nom français), les équipes de tous leurs horaires y sont rattachées, et ce qui disparaît de Spordle est supprimé avec ses abonnements | ✅ Implémenté |
| 2026-11-18 | Page Configuration ouverte à tous : suivre une équipe par association → division → ligue → équipe (`GET /api/subscriptions/*`), avec limite `roles.max_followed_entities` du rôle (badge `3 / 5`), liste des équipes suivies et retrait (`423 Locked` sous verrou 15 j) | ✅ Implémenté |
| 2026-11-18 | Tout le reste est limité aux suivis (`ICallerDataScopeService` depuis la BD) : rapport hebdomadaire et page équipe sans ids codés en dur (`GET /api/nav/*`), MCP tools de référence lus en BD (associations/divisions/ligues/équipes) et refus hors périmètre, seuls classement/matchs/feuilles de match/suspensions appelant encore Spordle ; nouvelle table `league_schedules` (ids d'horaires Spordle) | ✅ Implémenté |
| 2026-11-18 | Déploiement de `league_schedules` : migrations `LeagueIsSeasonCategory` et `AddLeagueSchedules` appliquées à la BD hôte, puis **backfill** des horaires par un nouvel import idempotent (les 21 ligues existantes n'avaient pas d'horaires : 40 lignes créées). `/readyz` signale désormais les migrations en attente (503 + `pendingMigrations`) au lieu de répondre « ready » sur une BD en retard de schéma | ✅ Corrigé |
| 2026-11-18 | Import de référence robuste aux variations Spordle : `shortName` nul, `division`/`gender` absents, et surtout `powerplayPct`/`penaltyKillPct` **omis** par les horaires de tournoi (les compteurs absents valent 0, les pourcentages restent `null`) ; un horaire illisible (JSON inattendu, HTTP, timeout) est désormais ignoré avec un avertissement au lieu d'annuler tout l'import | ✅ Corrigé |
| 2026-11-19 | À la connexion : profil OIDC complété depuis `/userinfo` (nom, prénom, courriel — uniquement tant que la ligne `users` est incomplète), et tout nouveau compte reçoit automatiquement la licence **COACH** (`user_roles`) ; `GET /api/users/me` expose `teamCount` pour la page de sélection des équipes du premier accès | ✅ Implémenté |
| 2026-11-26 | **Profil jamais enregistré (diagnostic + correctif)** : les 7 lignes `users` avaient `email`/`first_name`/`last_name` à `NULL` avec `updated_at = created_at`, preuve que la complétion n'avait jamais rien écrit — le token d'API ne porte pas ces claims par défaut et le secours `/userinfo` n'était jamais exploité. Le profil est désormais lu **dans le token** (claims namespacés écrits par une Action Auth0, `Auth:ProfileClaimsNamespace`), et rafraîchi à **chaque** requête authentifiée (Auth0 = source de vérité) ; un claim absent du token ne remplace rien, et `/userinfo` reste en secours tant que la ligne est incomplète | ✅ Corrigé |
| 2026-11-24 | Suivre une équipe ouvre sa ligue entière (décision 2026-11-24) : toutes les équipes de la ligue deviennent lisibles — sélecteur de la page équipe (`GET /api/nav/leagues/{id}/teams`), détail (`GET /api/teams/{teamId}` via `GetSelectableTeamAsync`), outils MCP (`GetLeagueTeams`, `GetTeamDetail`) — les associations/divisions restent limitées aux suivis ; la page équipe et le rapport hebdomadaire continuent de ne couvrir que les équipes suivies | ✅ Implémenté |
| 2026-11-25 | Directeur de catégorie : suivre une catégorie (M11, M13, M15, M17, M19, Junior) **un nom d'équipe à la fois** — `GET /api/subscriptions/associations/{id}/categories[/teams?category=…&baseName=…]` et `POST /api/users/me/director-team-subscriptions` (`{ associationId, category, baseName }`) ; le niveau est lu dans le nom français de la ligue (`LeagueCategoryLevel`), la limite d'équipes/le verrou 15 j sont levés pour le rôle DIRECTOR comme pour le président, et un second nom de la même catégorie est refusé (`409`) tant que les équipes du premier sont suivies — `GET /api/users/me/subscriptions` expose la `category` de chaque équipe suivie | ✅ Implémenté |
| 2026-11-26 | Importer une division ne supprime plus les ligues/divisions des autres (bug « j'ai importé 5956 et ça a effacé mes autres ligues ») : un guid de catégorie Spordle est **partagé entre les divisions** (13 des 17 catégories de 5956 le sont avec 6193), l'import retrouvait donc la ligue d'une autre division, la reparentait puis supprimait ses équipes. `leagues` est désormais unique sur `(division_id, season_id, external_id)`, `teams` et `league_schedules` sur `(league_id, external_id)` (migration `ScopeLeaguesToDivision`), l'import ne reparente plus rien et un import **incomplet** ne supprime rien ; `GET /api/subscriptions/leagues/{id}/teams` et `/api/preview/leagues/{id}/teams` acceptent `divisionId` pour lever l'ambiguïté | ✅ Corrigé |
| 2026-11-26 | Comptabilité des tokens des agents : tables `agent` (`agent_name` unique, l'assistant `hockey-season-data.agent` inséré par la migration `AddAgentTokenUsage`), `message` (les **questions** uniquement) et `token_usage` (`agent_id`, `input_token_count`, `output_token_count`, `total_token_count`, `reasoning_tokens`, `user_id`, `message_id`). `TokenUsageRecordingChatClient` capture l'usage de chaque appel du modèle et `AgentUsageMiddleware` l'écrit à la fin de l'appel, avec la question posée | ✅ Implémenté |
