# Hockey Season Backend

.NET 10 Minimal API following Clean Architecture, with an integrated MCP
(Model Context Protocol) server exposed over Streamable HTTP.

## Projects

| Project | Responsibility |
|---|---|
| `HockeySeason.Domain` | Entities (`ServerTime`), `ITimeProvider` abstraction, `TimeService` |
| `HockeySeason.Application` | Use cases: `IHealthService` / `HealthService`, `HealthReport` |
| `HockeySeason.Infrastructure` | `UtcTimeProvider` adapter over the system clock |
| `HockeySeason.Mcp` | MCP tools (`TimeTool` → `get-server-time`) |
| `HockeySeason.Api` | Composition root: endpoints, CORS, MCP host |
| `HockeySeason.AppHost` | Aspire orchestration for the API and Angular frontend |
| `HockeySeason.Tests` | xUnit tests for the application layer |

Dependency direction: `Api` → (`Application`, `Infrastructure`, `Mcp`);
`Mcp` → (`Application`, `Domain`); `Application` → `Domain`;
`Infrastructure` → `Domain`. Domain references nothing.

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
- `POST /mcp` — MCP Streamable HTTP endpoint (tools: `get-server-time`)

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

Connect with any MCP client using streamable HTTP against `http://localhost:5000/mcp`.
