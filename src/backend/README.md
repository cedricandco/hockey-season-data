# Hockey Season Backend

.NET 8 Minimal API following Clean Architecture, with an integrated MCP
(Model Context Protocol) server exposed over Streamable HTTP, protected by an
OIDC-style JWT security layer (`HockeySeason.Auth`).

## Projects

| Project | Responsibility |
|---|---|
| `HockeySeason.Domain` | Entities (`ServerTime`), `ITimeProvider` abstraction, `TimeService` |
| `HockeySeason.Application` | Use cases: `IHealthService` / `HealthService`, `HealthReport` |
| `HockeySeason.Infrastructure` | `UtcTimeProvider` adapter over the system clock |
| `HockeySeason.Auth` | OIDC-style JWT security: login endpoint, token issue/validate, rate guard |
| `HockeySeason.LeagueApi` | HTTP client for the public Spordle league API (`pub-api.play.spordle.com`) |
| `HockeySeason.Mcp` | MCP tools (`TimeTool`, `LeagueTools`) and `LeagueAgentToolFactory` bridge |
| `HockeySeason.Api` | Composition root: endpoints, CORS, MCP host |
| `HockeySeason.Functions` | Azure Functions host for health and AG-UI endpoints |
| `HockeySeason.AppHost` | Aspire orchestration for the API and Angular frontend |
| `HockeySeason.Tests` | xUnit tests for the application and auth layers |

Dependency direction: `Api` → (`Application`, `Infrastructure`, `Mcp`, `Agents`);
`Mcp` → (`Application`, `Domain`, `LeagueApi`); `Agents` → (`Mcp`, `Application`,
`Domain`); `Application` → `Domain`; `Infrastructure` → `Domain`;
`LeagueApi` references nothing. Domain references nothing.

## Run locally

```bash
dotnet run --project HockeySeason.Api
```

The API listens on `http://localhost:5000` (or `$PORT` when set), binding all interfaces.
For Aspire orchestration of both applications, see the root README.
Relative agent instruction paths are resolved from the API content root first,
then from the application output directory containing the bundled instructions.

## Endpoints

`GET /api/weekly-report` is available in both hosts and requires authentication.
It accepts `startDate` and `endDate` in `yyyy-MM-dd` format, plus repeated
`divisionId` and `teamId` parameters (1–20 positive IDs each). The response
contains raw Spordle arrays and an availability flag per source. Transient
failures are retried up to three times without blocking other sources. Each
division also includes its official league name from `GetLeagues` when available.
See `HockeySeason.Api/weekly-report.http` for example requests.

- `GET /healthz` — liveness: `{ "status": "ok" }`
- `GET /readyz` — readiness: `{ "status": "ready" }`
- `GET /api/health` — health report consumed by the frontend
- `POST /mcp` — MCP Streamable HTTP endpoint
- `POST /api/agui/hockey-season-data.agent` — AG-UI league data agent

## Authentication (HockeySeason.Auth)

The backend is an OAuth2 resource server backed by **Auth0**: the Angular SPA
runs Authorization Code + PKCE against Auth0 Universal Login, then calls the
API with the access token issued for the registered API audience.

1. The SPA acquires a token via `@auth0/auth0-angular` (`getAccessTokenSilently`
   or the HTTP interceptor) with `audience: https://api.hockeyseason.dev`.
2. Requests carry the bearer token twice: `Authorization: Bearer <token>` (JwtBearer middleware) and `X-Authorization` with the same value — mirrored by the SPA for hosts/proxies that drop the standard header. Functions triggers require the mirrored header (`FunctionsAuthorization`).
3. The middleware validates issuer, audience, lifetime and signature; keys
   come from Auth0's JWKS discovery (RS256 only) and no signing secret is
   stored in this repository.
4. `IJwtValidator` performs the same validation directly against the
   authorization header for the Azure Functions host (same JWKS discovery).

### User context (IUserContext)

`HockeySeason.Auth` also resolves the caller's identity-provider profile on demand: `IUserContext` reads the bearer token (standard header first, then `X-Authorization` via `IBearerTokenAccessor`) and calls the provider's `/userinfo` endpoint. The exposed data maps the OIDC claims — `sub` (stable user id), `nickname`, `name`, `picture`, `updated_at`, `email`, `email_verified`.

- The tenant host comes from `Auth:Authority`; `UserInfo:BaseAddress` and `UserInfo:Path` override it for non-standard deployments.
- The Api host exposes it at `GET /api/me`; the Functions host exposes the same route through `MeFunction` (trigger-validated like the other triggers).
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

The managed Azure Functions deployment exposes `/api/health` and the AG-UI
endpoint with the same token validation. It does not host `/mcp`, `/healthz`,
or `/readyz`; these remain available from the local ASP.NET Core host.

## Run tests

```bash
dotnet test
```

## MCP server

The MCP endpoint is mounted at `/mcp` via the `ModelContextProtocol.AspNetCore`
package (Streamable HTTP). Tools live in `HockeySeason.Mcp` and are plain calls
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

Connect with any MCP client using streamable HTTP against `http://localhost:5000/mcp`.
