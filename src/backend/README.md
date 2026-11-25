# Hockey Season Backend

.NET 8 Azure Functions (isolated worker) host following Clean Architecture, with an
integrated MCP (Model Context Protocol) server exposed over Streamable HTTP,
protected by an OIDC-style JWT security layer (`HockeySeason.Auth`).

## Projects

| Project | Responsibility |
|---|---|
| `HockeySeason.Domain` | Entities (`ServerTime`), `ITimeProvider` abstraction, `TimeService` |
| `HockeySeason.Application` | Use cases: `IHealthService`/`HealthService`, `IUserProvisioningService`, `ICallerDataScopeService`, `IScopeService`, `IUserService`, `ISubscriptionCatalogService`, `IPreviewService`, `HealthReport` |
| `HockeySeason.Infrastructure` | `UtcTimeProvider` + PostgreSQL persistence: `HockeySeasonDbContext` (EF Core + Npgsql, snake_case), migrations, use-case implementations |
| `HockeySeason.Auth` | OIDC-style JWT security backed by Auth0 (see below) |
| `HockeySeason.LeagueApi` | HTTP client for the public Spordle league API (`pub-api.play.spordle.com`) |
| `HockeySeason.Mcp` | MCP tools (`TimeTool`, `LeagueTools`) and `LeagueAgentToolFactory` bridge |
| `HockeySeason.Functions` | Composition root: Azure Functions HTTP triggers (API, MCP, AG-UI), user provisioning, scope enforcement |
| `HockeySeason.AppHost` | Aspire orchestration for the Functions host (`func start`) and the Angular frontend |
| `HockeySeason.Tests` | xUnit tests for the application, auth and Functions layers |

Dependency direction: `Functions` → (`Application`, `Infrastructure`, `Mcp`, `Agents`);
`Mcp` → (`Application`, `Domain`, `LeagueApi`); `Agents` → (`Mcp`, `Application`,
`Domain`); `Application` → `Domain`; `Infrastructure` → `Domain`;
`LeagueApi` references nothing. Domain references nothing.

## Run locally

Prerequisites: .NET 8 SDK and [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
(`func`). No Docker, no Azurite and no storage account are needed: every trigger
is HTTP. From `src/backend/HockeySeason.Functions`:

```bash
func start
```

The API listens on `http://localhost:7071` (routes under `/api`). For Aspire
orchestration of the Functions host and the frontend, see the root README.
Configuration comes from `appsettings.json` / `appsettings.Development.json`
and, in development, the user secrets of the Functions project.
Relative agent instruction paths are resolved from the content root first,
then from the application output directory containing the bundled instructions.

## Endpoints

`GET /api/weekly-report` requires authentication.
It accepts `startDate` and `endDate` in `yyyy-MM-dd` format only: the leagues
and teams of the report are the **teams the caller follows**
(`team_subscriptions`), resolved from the database — nothing is passed by the
client and nothing is hardcoded. Every followed team contributes its league,
and each schedule of that league is fetched, so a category played in several
schedules ("Saison Rives", "Phase 2"…) reports all of its figures. The
response contains raw Spordle arrays and an availability flag per source.
Transient failures are retried up to three times without blocking other
sources. Each card also carries the league, division and schedule names read
from the database. See `HockeySeason.Functions/weekly-report.http` for an
example request.

- `GET /api/healthz` — liveness: `{ "status": "ok" }`
- `GET /api/readyz` — readiness: includes the PostgreSQL state (`database=ok|not_configured`, 503 when configured but unreachable)
- `GET /api/health` — health report consumed by the frontend
- `POST /api/mcp` — MCP Streamable HTTP endpoint (stateless)
- `POST /api/agui/hockey-season-data.agent` — AG-UI league data agent

### Anonymous landing-page preview

`HockeySeason.Functions/PreviewFunctions.cs` exposes the only **unauthenticated**
surface of the API, used by the public landing page before sign-in. It never
calls `FunctionsAuthorization` and never resolves a caller:

| Endpoint | Purpose |
|---|---|
| `GET /api/preview/associations` | Associations that own imported data |
| `GET /api/preview/associations/{id}/divisions` | Divisions (Spordle offices) of one association |
| `GET /api/preview/divisions/{id}/leagues` | Leagues of a division for the latest imported season |
| `GET /api/preview/leagues/{leagueId}/teams` | Teams of one league, keyed on database ids |
| `GET /api/preview/teams/{teamId}` | Team detail — the exact JSON of `GET /api/teams/{teamId}`, `teamLinks` included |

The preview reads only the reference data imported from Spordle plus the public
schedule of a team (`IPreviewService` in Infrastructure, which deliberately does
not enforce follows). The published schedule ids are resolved from the database,
never taken from the query string. A host without persistence lists nothing
rather than failing. The visitor's three free attempts are counted in the
browser (`localStorage`, see the frontend README), not by these endpoints: the
gate is a product rule, and creating the free account is what keeps the data
available.

## PostgreSQL persistence (HockeySeason.Infrastructure)

The API keeps an Auth0-backed local account per caller plus licence/subscription
data. Schema reference lives in `docs/database-schema.sql` (regenerated with
`dotnet ef migrations script`); the true source of truth is the EF Core migration
in `HockeySeason.Infrastructure/Migrations/`.

| Key | Meaning | Where |
|---|---|---|
| `ConnectionStrings:Postgres` | PostgreSQL connection string | user-secrets (dev) / `ConnectionStrings__Postgres` env var (hosted) |
| `Database:MigrateOnStartup` | Apply EF Core migrations at host startup (default `false`) | appsettings / env var `Database__MigrateOnStartup` |
| `Authorization:OpenAccess` | Temporary full access for any authenticated caller (default `false` in code, `true` in appsettings for now) | appsettings / env var `Authorization__OpenAccess` |

Apply the database schema locally:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "Host=...;Username=...;Password=...;Database=hockey_season" --project src/backend/HockeySeason.Functions
cd src/backend/HockeySeason.Infrastructure
dotnet ef database update --startup-project ../HockeySeason.Functions
```

The startup migration runs only when `Database:MigrateOnStartup=true` (hosted
deployments). `GET /api/readyz` reports `database=ok` once the schema is
reachable **and up to date**: it returns `503` with the pending migration names
when the server is behind the bundled EF Core migrations, so a deployment whose
schema was never updated does not look ready (a missing table otherwise fails
only at query time with PostgreSQL `42P01`).

### User registration on connect

Every authenticated invocation passes through `UserProvisioningMiddleware` (a
Functions worker middleware): the first request of a newly
connected caller — whatever the endpoint — upserts their `users` row and
grants the default **COACH** licence (`user_roles` → `roles.id = 1`, 5 teams).
Idempotent by design (SELECT before INSERT, existing rows never mutated, emails
never overwritten), so subsequent requests are pure lookups. When persistence is
not configured the middleware is a no-op. A provisioning failure never blocks
the invocation.

The profile — email, **first name and last name** — comes from the JWT claims
when the access token carries them. Auth0 access tokens only publish `email`,
`given_name` and `family_name` when those claims were added to the API, so the
resolver completes them from the provider's `/userinfo` profile: this happens
only while the stored row still misses one of the three fields (never on every
request), and the display name of a database (email/password) signup is split
into first/last name. A value already stored is never overwritten.

`GET /api/users/me` also returns `teamCount`: a freshly provisioned account
follows no team, which is what sends the SPA to the team-selection page on
first visit.

### User account endpoints (all require a valid bearer token)

| Endpoint | Purpose |
|---|---|
| `GET /api/users/me` | Local profile + licence roles + limits + visual settings + `teamCount` (provisions the row on first call, granting the default COACH licence) |
| `PUT /api/users/me/settings` | Update primary/secondary colors and generic extras |
| `GET /api/users/me/subscriptions` | Followed teams (with their 15-day lock), leagues and associations, plus the licence headroom of each kind (`{ kind, max, used, canAddMore }`; `max: null` = unlimited) |
| `POST /api/users/me/team-subscriptions` | Follow a team — bounded by the COACH role's `roles.max_followed_entities` (5 by default), locked 15 days against removal |
| `DELETE /api/users/me/team-subscriptions/{teamId}` | Unfollow — `423 Locked` while the 15-day window is active (admins bypass) |
| `POST /api/users/me/team-subscriptions/{teamId}/unlock-request` | Trace an unlock request for the admin interface |
| `POST /api/users/me/league-subscriptions` | Follow a league (Spordle category id, a guid) — DIRECTOR licence, 5 max |
| `DELETE /api/users/me/league-subscriptions/{leagueId}` | Unfollow a league (guid route value) |
| `POST /api/users/me/organization-subscriptions` | Follow a whole association — PRESIDENT scope (all leagues of the org) |
| `DELETE /api/users/me/organization-subscriptions/{organizationId}` | Unfollow an association |
| `POST /api/users/me/president-team-subscriptions` | Body `{ "associationId": 1, "baseName": "Grizzly" }` — follow **every** team of the association whose name starts with that base name, in all divisions, leagues and categories (PRESIDENT licence: no team cap, no 15-day lock; duplicates and unknown ids are reported, not refused — `403` when the caller has no PRESIDENT role) |

Read-only navigation for the "follow a team" selector of the configuration
page (association → division → league → team). Any authenticated caller may
read it — it is only the reference data mirrored from Spordle, which every
user already sees — and each level returns the identifier the next one expects:

| Endpoint | Purpose |
|---|---|
| `GET /api/subscriptions/associations` | Associations that own at least one division (`organizations.id` + name) |
| `GET /api/subscriptions/associations/{associationId}/divisions` | Divisions of an association; `id` is the Spordle office number (`divisions.external_id`, e.g. 6193) |
| `GET /api/subscriptions/associations/{associationId}/teams?baseName=Grizzly` | Teams of the association whose name starts with the base name (case- and accent-insensitive, whole words), across every division/league/category of the latest imported season — the association-president preview |
| `GET /api/subscriptions/divisions/{divisionId}/leagues[?seasonId=2026-27]` | Leagues of a division for a season (latest imported when omitted); `id` is the Spordle category guid |
| `GET /api/subscriptions/leagues/{leagueId}/teams` | Teams of a league, by Spordle team id (the value `POST /api/users/me/team-subscriptions` expects) |

Admin-only user directory and licence assignment (`is_admin` column **or**
`roles=admin` claim; the frontend admin page is hidden and guarded for the rest):

| Endpoint | Purpose |
|---|---|
| `GET /api/users` | List local accounts with their granted roles |
| `GET /api/roles` | List the assignable roles (`roles` reference table) |
| `PUT /api/users/{userId}/roles` | Replace the whole role set of a user (`user_roles`); body `{ "roleIds": [1, 3] }` |

(`admin/*` is reserved by the Functions host, hence the plain `users` /
`roles` routes.) See `HockeySeason.Functions/admin-users.http` for sample
requests. These endpoints stay closed to non-administrators even while
`Authorization:OpenAccess` is on: granting licences is a privilege operation.

Admin-only reference-data import (Spordle → PostgreSQL, upsert by external id).
`POST /api/reference/import` is the supported entry point: one call imports a
whole office/season — the division (office), the season, every league (Spordle
category, stored under its **French** name, e.g. "M13 B"), the **schedules** of
each league (the Spordle ids the standings/games/suspensions calls are keyed on,
`league_schedules`) and every team — with an idempotent upsert that also deletes
what disappeared from Spordle:

| Endpoint | Purpose |
|---|---|
| `GET /api/reference/organizations` | The `organizations` table (association drop-down) |
| `GET /api/reference/divisions/{divisionId}/seasons` | Seasons Spordle publishes for a division (`divisions.external_id`), plus the imported ones |
| `POST /api/reference/import` | Body `{ "organizationId": 1, "divisionId": "6193", "seasonId": "2026-27" }` |

`organizationId` is a row of `organizations` (the association picked in the
page) and `divisionId` is the division number typed by the administrator, i.e.
`divisions.external_id`.

Reconciliation rules: a league is the `(season, Spordle category id)` pair, a
team is its Spordle team id and a schedule is its Spordle schedule id, so none
of them is duplicated across divisions or seasons and a team or schedule moved
between categories is re-parented. Deletions are
strictly scoped: only leagues, schedules and teams of **the division being
updated** (and of the imported season) are pruned when Spordle no longer
publishes them — a sibling division, another association or a past season is
never touched. Their `team_subscriptions` / `league_subscriptions` go with them
(ON DELETE CASCADE). Categories without a published team are skipped. The
import is administrator only and stays closed even while
`Authorization:OpenAccess` is on.

Schedules differ in what they publish, so the standings reader tolerates the
variations instead of failing the whole import: a team may have
`"shortName": null`, a schedule may omit `division`/`gender`, and a tournament
schedule publishes neither `powerplayPct`/`penaltyKillPct` nor the game
counters (those default to zero, the percentages stay `null`). A schedule whose
payload still cannot be read (unexpected JSON shape, HTTP error, timeout) is
skipped with a warning in `warnings` naming that schedule; the rest of the
import completes.

Because the import is idempotent, re-running it **backfills** what an older
import did not store — this is how `league_schedules` was filled for reference
data imported before the table existed. After applying a migration that adds
reference data, re-run the import once per division/season.

Legacy aliases kept for existing scripts, both delegating to the same use case:

| Endpoint | Purpose |
|---|---|
| `POST /api/import/offices?seasonId=2026-27` | Import every configured office for a season (alias of the import above) |
| `POST /api/import/leagues`, `POST /api/import/teams` | Superseded: leagues and teams are imported together, these return 400 `use_reference_import` |

(`admin/*` is reserved by the Functions host, hence the `import/` prefix.)

Business endpoints are limited to the caller's follows, resolved from the
database (`ICallerDataScopeService`): following a team opens its whole league
(every team of the league is readable, the user needs it to browse the
opponents), a followed league exposes all its teams, a followed association
exposes all its divisions and leagues, and the division and association of a
followed team come along as read-only parents. Nothing else is listed or
readable.

| Endpoint | Purpose |
|---|---|
| `GET /api/weekly-report?startDate=…&endDate=…` | Standings, games and suspensions of the **teams followed** (Spordle figures, schedule ids from `league_schedules`); empty when nothing is followed |
| `GET /api/nav/teams` | Every team the caller follows, with the database ids and names of its league, division and association (team page follow list) |
| `GET /api/nav/associations` | Associations that own at least one followed team (`organizations.id`) |
| `GET /api/nav/associations/{associationId}/divisions` | Divisions of that association that own a followed team (`divisions.id`) |
| `GET /api/nav/divisions/{divisionId}/leagues` | Leagues of that division that own a followed team (`leagues.id`) |
| `GET /api/nav/leagues/{leagueId}/teams` | Every team of that league once one of its teams is followed (`teams.id`) — following a team opens its whole league |
| `GET /api/teams/{teamId}` | Detail of a readable team — a followed one or any team of a league that contains one — keyed on `teams.id`; the schedule is resolved from the database — 403 outside the opened leagues. Also returns `teamLinks`, mapping the Spordle team ids of the games to the page's `teams.id`, so the page can open an opponent from a score line without handling a Spordle id |
| `GET /api/leagues/{scheduleId}/teams` | Teams of a schedule, read from Spordle — 403 outside the follows |
| `GET /api/leagues/{scheduleId}/teams/{teamId}` | Team detail keyed on Spordle ids (chat card / agent output) — 403 when the team is not readable |
| `GET /api/games/{gameId}` | Box score — 403 when neither team is readable |

The team-page navigation returns **database ids** and lists only the values
that own at least one followed team at the association, division and league
levels — but once a league is opened, all of its teams are offered: following a
team opens its whole league. A league with no follow is never offered, so an
unfollowed division stays unreachable; the Spordle ids stay behind the API.

When no database is configured these endpoints run unfiltered rather than
failing (database-less host, tests) — except the team page and the weekly
report, which are about the caller's own teams and stay empty when the follows
cannot be resolved.

### Temporary open access (Authorization:OpenAccess)

While the roles (licences) and admin flags are still being provisioned, the
`Authorization:OpenAccess` switch (currently `true` in `appsettings.json`) gives
every **authenticated** caller full access, whatever the database or their role
contains: no follow filtering (the data scope returns the whole reference
data), no COACH/DIRECTOR licence limit, no 15-day team lock, and the admin
import endpoints accept any caller. Authentication itself (a valid bearer token)
stays required. Close the phase with `Authorization:OpenAccess=false` (env var
`Authorization__OpenAccess`) — the follow scope, licence and admin guards then
apply again (`ICallerDataScopeService`, `UserService`,
`AdminImportFunctions`).

### Association president: follow a base name

A caller holding the **PRESIDENT** licence (`roles.id = 3`) can follow a whole
team family in one call: it picks an association and types a base name
("Grizzly"), and every team of that association whose published name starts
with it — across all divisions, leagues and categories of the latest imported
season — is followed. The match is case- and accent-insensitive and works on
whole words (`TeamNameMatcher`), so "grizzly" selects `GRIZZLY 1 STE-JULIE`
and `GRIZZLY 2 ST-HYACINTHE` but never `GRIZZLYS 3 LONGUEUIL`.

Two rules make the flow usable:

- the team licence limit is lifted for the PRESIDENT role (the `team` limit is
  reported with `max: null`, like open access), so a president is not bounded
  by the COACH cap of 5;
- the batch follows are stored with an already elapsed 15-day lock, so the
  president can prune a team of the batch immediately with the regular
  `DELETE /api/users/me/team-subscriptions/{teamId}`.

The flow is exposed to the PRESIDENT licence only (`UserService` refuses the
batch with `403` otherwise); the preview endpoint stays readable by any
authenticated caller because it only exposes shared reference data.

## Authentication (HockeySeason.Auth)

The backend is an OAuth2 resource server backed by **Auth0**: the Angular SPA
runs Authorization Code + PKCE against Auth0 Universal Login, then calls the
API with the access token issued for the registered API audience.

1. The SPA acquires a token via `@auth0/auth0-angular` (`getAccessTokenSilently`
   or the HTTP interceptor) with `audience: https://api.hockeyseason.dev`.
2. Requests carry the bearer token twice: `Authorization: Bearer <token>` (JwtBearer middleware) and `X-Authorization` with the same value — mirrored by the SPA for hosts/proxies that drop the standard header. Functions triggers require the mirrored header (`FunctionsAuthorization`).
   Agent code can resolve `AgentCallerContext` from DI to read the current
   request's `X-Authorization` bearer token. It is request-local, not included
   in prompts or logs; identifying the user from it is not implemented yet.
3. `IJwtValidator` validates issuer, audience, lifetime and signature; keys
   come from Auth0's JWKS discovery (RS256 only) and no signing secret is
   stored in this repository.
4. Each trigger enforces it through `FunctionsAuthorization`, which stores the
   validated `ClaimsPrincipal` for the invocation (user provisioning, `admin`
   role check, scope enforcement).

### User context (IUserContext)

`HockeySeason.Auth` also resolves the caller's identity-provider profile on demand: `IUserContext` reads the bearer token (standard header first, then `X-Authorization` via `IBearerTokenAccessor`) and calls the provider's `/userinfo` endpoint. The exposed data maps the OIDC claims — `sub` (stable user id), `nickname`, `name`, `picture`, `updated_at`, `email`, `email_verified`.

- The tenant host comes from `Auth:Authority`; `UserInfo:BaseAddress` and `UserInfo:Path` override it for non-standard deployments.
- The Functions host exposes it at `GET /api/me` through `MeFunction` (trigger-validated like the other triggers).
- Registration: `AddHockeySeasonUserContext()` in each composition root.

JWTs are rejected with `401 { "error": "invalid_token", … }` when missing,
expired, forged or issued for another audience/issuer.

### Configuration

| Key | Meaning | Where |
|---|---|---|
| `Auth:Authority` | OIDC authority base URL, e.g. `https://dev-xyz.us.auth0.com/` | `appsettings.json` |
| `Auth:Issuer` | Expected `iss` claim (defaults to the authority) | `appsettings.json` |
| `Auth:Audience` | Registered API identifier (`aud`) | `appsettings.json` (`https://api.hockeyseason.dev`) |
| `Auth:AllowHttpAuthority` | Dev-only escape hatch for http:// authorities (tests) | `appsettings.json` (default `false`) |
| `UserInfo:BaseAddress` | Overrides the /userinfo host (defaults to `Auth:Authority`) | `appsettings.json` |
| `UserInfo:Path` | Relative userinfo path (default `userinfo`) | `appsettings.json` |

No client secret is involved: the SPA is a public client and the API only
verifies tokens. The Auth0 tenant owns user accounts (email + password),
signup/password-reset flows and brute-force protection.

The Azure Functions host (local `func start` and the managed Azure deployment)
exposes every endpoint above with the same token validation. `/api/mcp` accepts
the standard `Authorization` header as well as `X-Authorization`.

## Run tests

```bash
dotnet test
```

## MCP server

The MCP endpoint is served at `POST /api/mcp` by `McpFunction`: a Functions HTTP
trigger bound to a stateless Streamable HTTP transport from the
`ModelContextProtocol` package (`MapMcp` cannot run inside the Functions host).
There is no session: `GET`/`DELETE` return 405. Tools live in `HockeySeason.Mcp` and are plain calls
into application use cases:

```json
{ "name": "get-server-time", "description": "Returns the current server time in UTC" }
```

### League tools

The reference data — associations, divisions, leagues, teams and their
schedules — comes from the **database** and is limited to what the caller
follows (`ICallerDataScopeService`, resolved from the request's bearer token);
following a team opens its whole league, so every team of it is readable too.
Only the live figures still come from the public Spordle API, always with the
`league_schedules.external_id` ids read from the database. A tool call for a
league, schedule, team or game outside the caller's scope is refused with an
MCP error, so the assistant cannot answer about entities the user does not
follow.

Configuration lives in the `LeagueApi` section (`appsettings.json`):

| Key | Meaning | Default |
|---|---|---|
| `BaseAddress` | Public API root | `https://pub-api.play.spordle.com/api/` |
| `ApiKey` | API key sent as `API-Key` authorization | — |
| `DefaultLeagueId` | League used by legacy import aliases | `194908` |
| `DefaultOfficeId` | Office used by the legacy import | `6193` |
| `DefaultSeasonId` | Season used by the legacy import | `2026-27` |
| `HonourManualRanking` | Use league-published manual ranking | `false` |

| Tool | Params | Source |
|---|---|---|
| `GetAssociations` | — | database: followed associations |
| `GetDivisions` | `associationId?` | database: followed divisions |
| `GetLeagues` | `divisionId?` | database: followed leagues + `scheduleIds` |
| `GetLeagueTeams` | `leagueId` (category guid) | database: every team of an opened league (followed, or holding a followed team) |
| `GetTeamDetail` | `teamId` | Spordle, keyed on the stored schedule id |
| `GetLeagueStanding` | `scheduleId` | `GET sp/schedules/{scheduleId}/teamStats` (60 s cache) |
| `GetTeamGames` | `scheduleId`, `startDate`, `endDate`, `teamId?` | `GET sp/games?filter=<json>` |
| `GetLeagueSuspension` | `scheduleId` | `GET sp/schedules/{scheduleId}/suspensions` |
| `GetGameDetail` | `gameId` | `GET sp/games/{id}` + `boxScore` (both teams must be visible) |

The same tools are given to the `hockey-season-data.agent` AI agent through
`LeagueAgentToolFactory`, which turns the methods carrying
`[McpServerTool]` into Microsoft.Extensions.AI `AIFunction`s.

Connect with any MCP client using streamable HTTP against `http://localhost:7071/api/mcp`.
