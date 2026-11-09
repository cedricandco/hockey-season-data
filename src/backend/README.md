# Hockey Season Backend

.NET 8 Minimal API following Clean Architecture, with an integrated MCP
(Model Context Protocol) server exposed over Streamable HTTP.

## Projects

| Project | Responsibility |
|---|---|
| `HockeySeason.Domain` | Entities (`ServerTime`), `ITimeProvider` abstraction, `TimeService` |
| `HockeySeason.Application` | Use cases: `IHealthService` / `HealthService`, `HealthReport` |
| `HockeySeason.Infrastructure` | `UtcTimeProvider` adapter over the system clock |
| `HockeySeason.LeagueApi` | HTTP client for the public Spordle league API (`pub-api.play.spordle.com`) |
| `HockeySeason.Mcp` | MCP tools (`TimeTool`, `LeagueTools`) and `LeagueAgentToolFactory` bridge |
| `HockeySeason.Api` | Composition root: endpoints, CORS, MCP host |
| `HockeySeason.Functions` | Azure Functions host for health and AG-UI endpoints |
| `HockeySeason.AppHost` | Aspire orchestration for the API and Angular frontend |
| `HockeySeason.Tests` | xUnit tests for the application layer |

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

- `GET /healthz` — liveness: `{ "status": "ok" }`
- `GET /readyz` — readiness: `{ "status": "ready" }`
- `GET /api/health` — health report consumed by the frontend:
  `{ "environment": "Development", "time": { "utcNow": "...", "timeZoneId": "UTC" } }`
- `POST /mcp` — MCP Streamable HTTP endpoint (tools: `get-server-time`, `GetLeagues`,
  `GetLeagueStanding`, `GetLeagueTeams`, `GetTeamGames`, `GetLeagueSuspension`)
- `POST /api/agui/hockey-season-data.agent` — AG-UI league data agent

The managed Azure Functions deployment exposes `/api/health` and the AG-UI
endpoint. It does not host `/mcp`, `/healthz`, or `/readyz`; these remain
available from the local ASP.NET Core host.

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
