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
    │   ├── proxy.conf.json   # /api → http://localhost:5000 during ng serve
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
        └── HockeySeason.Tests/            # xUnit tests
```

## Run locally (backend + frontend)

1. Start the API (from `src/backend`):

   ```bash
   dotnet run --project HockeySeason.Api
   ```

   Listens on `http://localhost:5000` (or `$PORT`). Exposes:
   - `GET /healthz` → `{ "status": "ok" }`
   - `GET /readyz` → `{ "status": "ready" }`
   - `GET /api/health` → `{ "environment": "...", "time": { "utcNow": "...", "timeZoneId": "UTC" } }`
   - `/mcp` — MCP Streamable HTTP endpoint (tool: `get-server-time`)

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

- Default UI language is French with English fallback.
- The API binds all interfaces (`http://*:$PORT`) so it works in containers
  and behind managed API integrations.
- MCP tools live in `HockeySeason.Mcp` and are plain calls into application
  use cases (Clean Architecture, SOLID).
