# Hockey Season Backend

.NET 8 Azure Functions (isolated worker) host following Clean Architecture, with an
integrated MCP (Model Context Protocol) server exposed over Streamable HTTP,
protected by an OIDC-style JWT security layer (`HockeySeason.Auth`).

## Projects

| Project | Responsibility |
|---|---|
| `HockeySeason.Domain` | Entities (`ServerTime`), `ITimeProvider` abstraction, `TimeService` |
| `HockeySeason.Application` | Use cases: `IHealthService`/`HealthService`, `IUserProvisioningService`, `IScopeService`, `IUserService`, `HealthReport` |
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
It accepts `startDate` and `endDate` in `yyyy-MM-dd` format, plus repeated
`divisionId` and `teamId` parameters (1–20 positive IDs each). The response
contains raw Spordle arrays and an availability flag per source. Transient
failures are retried up to three times without blocking other sources. Each
division also includes its official league name from `GetLeagues` when available.
See `HockeySeason.Functions/weekly-report.http` for example requests.

- `GET /api/healthz` — liveness: `{ "status": "ok" }`
- `GET /api/readyz` — readiness: includes the PostgreSQL state (`database=ok|not_configured`, 503 when configured but unreachable)
- `GET /api/health` — health report consumed by the frontend
- `POST /api/mcp` — MCP Streamable HTTP endpoint (stateless)
- `POST /api/agui/hockey-season-data.agent` — AG-UI league data agent

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
deployments). `GET /api/readyz` reports `database=ok` once the schema is reachable.

### User registration on connect

Every authenticated invocation passes through `UserProvisioningMiddleware` (a
Functions worker middleware): the first request of a newly
connected caller — whatever the endpoint — upserts their `users` row from the
JWT claims (`sub`, `email`, `given_name`, `family_name`). Idempotent by design
(SELECT before INSERT, existing rows never mutated, emails never overwritten),
so subsequent requests are pure lookups. When persistence is not configured
the middleware is a no-op. A provisioning failure never blocks the invocation.

### User account endpoints (all require a valid bearer token)

| Endpoint | Purpose |
|---|---|
| `GET /api/users/me` | Local profile + licence roles + limits + visual settings (provisions the row on first call) |
| `PUT /api/users/me/settings` | Update primary/secondary colors and generic extras |
| `GET /api/users/me/subscriptions` | Followed teams (with their 15-day lock) and leagues |
| `POST /api/users/me/team-subscriptions` | Follow a team — COACH licence, 5 max, locked 15 days against removal |
| `DELETE /api/users/me/team-subscriptions/{teamId}` | Unfollow — refused while locked unless admin (409/403) |
| `POST /api/users/me/team-subscriptions/{teamId}/unlock-request` | Trace an unlock request for the admin interface |
| `POST /api/users/me/league-subscriptions` | Follow a league — DIRECTOR licence, 5 max |
| `DELETE /api/users/me/league-subscriptions/{leagueId}` | Unfollow a league |
| `POST /api/users/me/organization-subscriptions` | Follow a whole association — PRESIDENT scope (all leagues of the org) |
| `DELETE /api/users/me/organization-subscriptions/{organizationId}` | Unfollow an association |

Admin-only reference-data import (Spordle → PostgreSQL, upsert by external id):

| Endpoint | Purpose |
|---|---|
| `POST /api/import/offices` | Import supported offices as organizations + divisions |
| `POST /api/import/leagues?seasonId=2026-27` | Import leagues of every supported office for a season |
| `POST /api/import/teams` | Import teams of every known league (partial on Spordle errors) |

(`admin/*` is reserved by the Functions host, hence the `import/` prefix.)

Business endpoints scope-filter by subscriptions: `/api/weekly-report` drops
division/team ids outside the caller's scope (403 when nothing is authorized)
and `/api/games/{id}`, `/api/leagues/{id}/teams`, `/api/leagues/{id}/teams/{teamId}`
return 403 outside scope. When no database is configured
these endpoints run unfiltered rather than failing.

### Temporary open access (Authorization:OpenAccess)

While the roles (licences) and admin flags are still being provisioned, the
`Authorization:OpenAccess` switch (currently `true` in `appsettings.json`) gives
every **authenticated** caller full access, whatever the database or their role
contains: no subscription-scope filtering, no COACH/DIRECTOR licence limit, no
15-day team lock, and the admin import endpoints accept any caller.
Authentication itself (a valid bearer token) stays required. Close the phase
with `Authorization:OpenAccess=false` (env var `Authorization__OpenAccess`) —
the scope, licence and admin guards then apply again (`CallerScope`,
`UserService`, `AdminImportFunctions`).

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

`LeagueTools` reads the public Spordle league API through
`HockeySeason.LeagueApi.Http.LeagueApiService`. Configuration lives in the
`LeagueApi` section (`appsettings.json`):

| Key | Meaning | Default |
|---|---|---|
| `BaseAddress` | Public API root | `https://pub-api.play.spordle.com/api/` |
| `ApiKey` | API key sent as `API-Key` authorization | — |
| `DefaultLeagueId` | League used when none is supplied | `194908` |
| `DefaultOfficeId` | Office used when none is supplied | `6193` |
| `DefaultSeasonId` | Season used when none is supplied | `2026-27` |
| `HonourManualRanking` | Use league-published manual ranking | `false` |

| Tool | Params | Source |
|---|---|---|
| `GetLeagues` | `officeId?`, `seasonId?` | `GET sp/schedules?officeId=&seasonId=` |
| `GetLeagueStanding` | `leagueId` | `GET sp/schedules/{leagueId}/teamStats` (60 s cache) |
| `GetLeagueTeams` | `leagueId` | derived from the standings endpoint |
| `GetTeamGames` | `leagueId`, `startDate`, `endDate`, `teamId?` | `GET sp/games?filter=<json>` |
| `GetLeagueSuspension` | `leagueId` | `GET sp/schedules/{leagueId}/suspensions` |

The same tools are given to the `hockey-season-data.agent` AI agent through
`LeagueAgentToolFactory`, which turns the methods carrying
`[McpServerTool]` into Microsoft.Extensions.AI `AIFunction`s.

Connect with any MCP client using streamable HTTP against `http://localhost:7071/api/mcp`.
