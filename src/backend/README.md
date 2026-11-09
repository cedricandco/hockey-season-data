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
- `POST /api/auth/login` — exchanges the shared 6-digit access code for a signed
  JWT: `{ "code": "…6 digits…" }` → `{ "success": true, "accessToken": "…", "expiresInSeconds": 3600, "tokenType": "Bearer" }`
- `GET /api/health` — health report consumed by the frontend (Bearer token required)
- `POST /mcp` — MCP Streamable HTTP endpoint protected by the Bearer token
- `POST /api/agui/hockey-season-data.agent` — AG-UI league data agent (Bearer token required)

Anonymous endpoints: `/healthz`, `/readyz` and `/api/auth/login` only.

## Authentication (HockeySeason.Auth)

Until a real identity provider and user database exist, the backend implements
an OIDC-style flow with a shared access code:

1. The client posts the 6-digit code to `POST /api/auth/login`.
2. `AuthService` compares the code against the configured `Auth:PinCode`
   (constant-time SHA-256 comparison) and `TokenService` mints an HS256 JWT
   carrying `iss`, `sub`, `aud`, `iat`, `exp`, `jti` and an `amr=code` claim.
3. All other endpoints require `Authorization: Bearer <jwt>` (JwtBearer
   middleware validates issuer, audience, lifetime and signature; algorithms
   are pinned to HS256 and the token type to `JWT`).
4. The login endpoint is brute-force guarded: 5 attempts per minute per client
   IP (`LoginRateGuard`), answered with `429` + `Retry-After` beyond that.

JWTs are rejected with `401 { "error": "invalid_token", … }` when missing,
expired, forged or issued for another audience/issuer. An authenticated caller
cannot mint additional tokens without providing the correct code again.

### Configuration

| Key | Meaning | Where |
|---|---|---|
| `Auth:PinCode` | Shared 6-digit access code | user secrets (`dotnet user-secrets set "Auth:PinCode" "202627"`) |
| `Auth:SigningKey` | HS256 signing key (≥ 32 chars) | user secrets / environment variables |
| `Auth:Issuer` | `iss` of issued tokens | `appsettings.json` (`https://hockey-season-data`) |
| `Auth:Audience` | `aud` of issued tokens | `appsettings.json` (`hockey-season-api`) |
| `Auth:AccessTokenMinutes` | Access-token lifetime (minutes) | `appsettings.json` (`60`) |

Never commit `PinCode` or `SigningKey`; configure them with user secrets in
development and environment variables / Key Vault in production. Rotating the
signing key invalidates every previously issued token.

When swapping to a real OIDC provider (Entra ID, Auth0, …), replace the login
endpoint with the provider's authorization-code flow and point
`TokenValidationParameters` at its JWKS — the JwtBearer wiring stays unchanged.

The managed Azure Functions deployment exposes `/api/auth/login`, `/api/health`
and the AG-UI endpoint (same JWT protection as the local API). It does not host
`/mcp`, `/healthz`, or `/readyz`; these remain available from the local
ASP.NET Core host.

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
