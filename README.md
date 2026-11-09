# hockey-season-data
A web app that allows us to query data about your preferred hockey team (Quebec).

> **Status:** application shell. The project currently contains a mobile-friendly
> frontend (Ionic + Angular), a .NET 10 Minimal API with health endpoints, and an
> MCP server exposing a `get-server-time` tool. No database yet.

## Monorepo structure

```
hockey-season-data/
├── .env.sample               # PORT=5000 (API listen port)
├── README.md
└── src/
    ├── frontend/             # Angular 22 (zoneless) + Ionic 9 + ngx-translate 18
    │   ├── angular.json
    │   ├── package.json
    │   ├── proxy.conf.cjs    # /api → API_BACKEND_URL or localhost:5000
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
        ├── HockeySeason.Api/              # Minimal API host + /mcp endpoint
        ├── HockeySeason.Functions/        # Azure Functions health + AG-UI host
        ├── HockeySeason.AppHost/          # Aspire orchestration
        └── HockeySeason.Tests/            # xUnit tests
```

## Run locally (backend + frontend)

### Aspire

Install the .NET 10 SDK and Node.js 22 or later, then install the frontend
dependencies once:

```bash
cd src/frontend
npm ci
```

From `src/backend`, start both applications and the Aspire dashboard:

```bash
dotnet run --project HockeySeason.AppHost
```

The AppHost builds the backend and starts Angular's development server, which
builds the frontend. Angular starts only after the API's `/readyz` endpoint
responds successfully. Open the dashboard URL shown in the console, or visit
`http://localhost:4200`. The Angular proxy receives the API address from Aspire.
Port 4200 must be available.

### Separate terminals

1. Start the API (from `src/backend`):

   ```bash
   dotnet run --project HockeySeason.Api
   ```

   Listens on `http://localhost:5000` (or `$PORT`). Exposes:
   - `GET /healthz` → `{ "status": "ok" }`
   - `GET /readyz` → `{ "status": "ready" }`
   - `GET /api/health` → `{ "environment": "...", "time": { "utcNow": "...", "timeZoneId": "UTC" } }`
   - `/mcp` — MCP Streamable HTTP endpoint (tool: `get-server-time`)
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

## Notes

- Azure Static Web Apps deploys the separate .NET Functions host; configure
  `Agents__HockeySeasonData__OllamaApiKey` as an application setting for Ollama
  Cloud. See the frontend README for deployment details and limitations.
- Default UI language is French with English fallback.
- The API binds all interfaces (`http://*:$PORT`) so it works in containers
  and behind managed API integrations.
- MCP tools live in `HockeySeason.Mcp` and are plain calls into application
  use cases (Clean Architecture, SOLID).
