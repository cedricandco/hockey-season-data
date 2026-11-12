# hockey-season-data
A web app that allows us to query data about your preferred hockey team (Quebec).

> **Status:** application shell. The project currently contains a mobile-friendly
> frontend (Ionic + Angular), a .NET 8 Azure Functions API with health endpoints, and an
> MCP server exposing a `get-server-time` tool.

## Monorepo structure

```
hockey-season-data/
├── .env.sample               # PORT=5000 (legacy API listen port)
├── README.md
└── src/
    ├── frontend/             # Angular 22 (zoneless) + Ionic 9 + ngx-translate 18
    │   ├── angular.json
    │   ├── package.json
    │   ├── proxy.conf.cjs    # /api → API_BACKEND_URL or localhost:7071
    │   └── src/
    │       ├── app/
    │       │   ├── app.config.ts      # providers (Ionic, HttpClient, ngx-translate)
    │       │   ├── app.component.ts   # <ion-app> shell
    │       │   ├── home/              # API status page (FR/EN toggle, Ping API)
    │       │   └── core/              # ApiService + models
    │       ├── assets/i18n/           # en.json, fr.json
    │       └── environments/          # apiUrl: '/api'
    └── backend/
        ├── HockeySeason.sln
        ├── README.md
        ├── HockeySeason.Domain/            # ServerTime, ITimeProvider, TimeService
        ├── HockeySeason.Application/      # IHealthService, HealthService, HealthReport
        ├── HockeySeason.Infrastructure/   # UtcTimeProvider
        ├── HockeySeason.Mcp/              # TimeTool → get-server-time
        ├── HockeySeason.Functions/        # Azure Functions API: HTTP triggers, /api/mcp, AG-UI
        ├── HockeySeason.AppHost/          # Aspire orchestration
        └── HockeySeason.Tests/            # xUnit tests
```

## Run locally (backend + frontend)

### Aspire

Install the .NET 8 SDK, Node.js 22 or later and
[Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
(`func`; no Docker needed), then install the frontend
dependencies once:

```bash
cd src/frontend
npm ci
```

From `src/backend`, start both applications and the Aspire dashboard:

```bash
dotnet run --project HockeySeason.AppHost
```

The AppHost runs the Functions host with `func start` (port 7071) and starts
Angular's development server, which builds the frontend. Angular starts only
after the Functions host's `/api/healthz` endpoint responds successfully. Open the
dashboard URL shown in the console, or visit
`http://localhost:4200`. The Angular proxy receives the API address from Aspire.
Ports 4200 and 7071 must be available.

### Separate terminals

1. Start the Functions API (from `src/backend/HockeySeason.Functions`):

   ```bash
   func start
   ```

   Listens on `http://localhost:7071`. Exposes:
   - `GET /api/healthz` → `{ "status": "ok" }`
   - `GET /api/readyz` → `{ "status": "ready" }`
   - `GET /api/health` → `{ "environment": "...", "time": { "utcNow": "...", "timeZoneId": "UTC" } }`
   - `POST /api/mcp` — MCP Streamable HTTP endpoint (tool: `get-server-time`)
   - `POST /api/agui/hockey-season-data.agent` — AG-UI translation agent

2. Start the frontend (from `src/frontend`):

   ```bash
   npm install
   npm start
   ```

3. Open the app (default `http://localhost:4200`). The **Ping API** button calls
   `GET /api/health` (proxied to the API in dev) and displays the server UTC
   time. The header button toggles FR/EN.

## Backend tests

From `src/backend`:

```bash
dotnet test
```

## Team details

The **Teams** tab selects one of the two Hockey Richelieu offices, a league
and a team. It shows the division standings, the last five completed games
and player scoring. The API exposes `GET /api/offices`,
`GET /api/offices/{officeId}/leagues` (optional `seasonId`),
`GET /api/leagues/{leagueId}/teams` and
`GET /api/leagues/{leagueId}/teams/{teamId}`. The last response includes every
completed game's box score and upcoming scheduled games. The MCP and assistant
also expose `GetOffices` and `GetTeamDetail`.

Player statistics are calculated from published goal and penalty events,
using participant IDs. They are **not a complete roster**: players with no
published goal, assist or penalty cannot be identified from these events and
are omitted. `topScorers` and `topAssistProviders` contain all tied leaders,
or are empty when there are no goals or assists. Points equal goals plus
assists; penalty counts include bench penalties. In-progress games are
excluded from the completed-game totals.

## Notes

- Azure Static Web Apps deploys the separate .NET Functions host; configure
  `Agents__HockeySeasonData__OllamaApiKey` as an application setting for Ollama
  Cloud. See the frontend README for deployment details and limitations.
- Default UI language is French with English fallback.
- The Functions host listens on `http://localhost:7071` locally and sits behind
  the managed API integration of Azure Static Web Apps once deployed.
- MCP tools live in `HockeySeason.Mcp` and are plain calls into application
  use cases (Clean Architecture, SOLID).
