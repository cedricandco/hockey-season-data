# Hockey Season Frontend

Angular 22 (standalone, zoneless) responsive frontend with French/English
translation via `@ngx-translate/core` 18. The app has four routed pages:
chat, team details, weekly report, and color configuration.

## Prerequisites

- Node.js >= 22
- Backend API running (see `src/backend/README.md`) on `http://localhost:5000`

## Run locally

```bash
npm install
npm start
```

The dev server proxies `/api/*` to `http://localhost:5000` by default (see
`proxy.conf.cjs`), so relative `apiUrl: '/api'` works out of the box. Set
`API_BACKEND_URL` to use another backend address. The Aspire AppHost sets this
automatically; see the root README for running both applications together.

## Build

```bash
npm run build
```

The deployable browser bundle, including `index.html`, lands in
`dist/hockey-frontend/browser`.

## Responsive layout and tests

The same routes and functionality are used on desktop and mobile. Up to
900px, the primary navigation moves to a bottom bar and accounts for device
safe areas. The chat standings show a compact table on narrow screens, with
the remaining statistics under a details control. Other wide tables remain
scrollable horizontally.

Run the existing unit tests and the browser layout checks with:

```bash
npm test
npm run test:responsive
```

The responsive checks start their own frontend server, use simulated API
responses, and run in a locally installed Chrome or Edge; they do not require
the backend. If the browser is not found automatically, set
`PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to its executable. Browser emulation
does not replace checking the virtual keyboard and safe areas on real iOS
and Android devices.

## Authentication gate

Every screen requires an Auth0 session: visitors are handed over to Auth0
Universal Login (Authorization Code + PKCE through `@auth0/auth0-angular` —
`core/login.component.ts`, `app.config.ts`) and the SPA calls the API with the
access token issued for the `https://api.hockeyseason.dev` audience. The HTTP
interceptor (`authHttpInterceptorFn`) adds
`Authorization: Bearer <token>` to each `/api/` request (mirrored on
`X-Authorization` by `core/auth-header.interceptor.ts`) and the AG-UI
agent adds both headers in `agent/hockey-assistant-http-agent.ts`.
rejects invalid tokens with `401 { "error": "invalid_token", ... }`.

## Azure Static Web Apps deployment

The GitHub Actions workflow builds the frontend with Node.js 22 and publishes
the .NET 8 isolated-worker project `HockeySeason.Functions` as the managed
Azure Functions API. It exposes `/api/health` and the AG-UI agent endpoint.
The local ASP.NET Core API remains the backend for development.

Configure `Agents__HockeySeasonData__OllamaApiKey` in the Static Web App's
application settings when using Ollama Cloud; do not put credentials in the
repository or frontend. The default model and endpoint are in the backend's
`appsettings.json` and may also be overridden through application settings.
Azure Static Web Apps limits API requests to 45 seconds. Verify AG-UI streaming
through the deployed Static Web App as well as locally. The managed Functions
host enforces authentication per trigger (issuer/audience/JWKS validation of
the raw Authorization header in `HockeySeason.Functions.FunctionsAuthorization`);
it does not run the ASP.NET Core JWT bearer middleware, because on the managed
host that middleware short-circuits failed authentication before the Functions
coordinator handshake completes and holds requests until the 15s
`FunctionStartTimeoutInSeconds` elapses.

The explicit `@emnapi/core` and `@emnapi/runtime` development dependencies
resolve conflicting WebAssembly build-tool peer dependencies in the lockfile.
Keep them aligned when updating the frontend build dependencies.

## Appearance and colors

The configuration page (`/configuration`) lets each user choose primary and
secondary colors. The choices are saved in the browser's local storage and can
be reset to the defaults, `#0e3a70` and `#3eb838`. Default CSS variables are
defined in `src/global.scss`; keep the defaults in `core/theme.service.ts` in
sync if changing them. The chat displays connection errors and offers a retry.

## Chat page

The chat page (`/chat`) is the default route and fills the viewport below the
header: the transcript scrolls and the composer stays pinned at the bottom of
the page. Enter sends a message and Shift+Enter inserts a new line.
The Hockey Assistant answers questions about league data. Match cards use the
game ID chosen by the agent to load `GET /api/games/{gameId}` directly, so their
goals, penalties and per-period totals do not depend on the model copying them.
Both the local API and Functions host support the endpoint.

## Weekly report page

The weekly report page (`/teams-weekly-report`) loads the previous seven days through today
by default. The date range is adjustable; games are filtered to the configured
team IDs and ordered chronologically. Standings and games are shown per
division, followed by all published division suspensions. Unavailable sources
are clearly identified rather than presented as empty results.
Division cards use the official league names returned by `GetLeagues`. Tracked
teams and their scores are highlighted green for a win, gray for a tie, and red
for a loss.

The initial division and team IDs are defined in `report/report.component.ts`.
The authenticated `GET /api/weekly-report` accepts `startDate` and `endDate`
in `yyyy-MM-dd` format, plus repeated `divisionId` and `teamId` parameters
(up to 20 of each). It returns the original Spordle arrays for standings, games
and suspensions, with an `available` flag per source. Requests are retried up
to three times for transient failures; a failed source does not prevent the
other sources from appearing. Both the local API and Functions host support it.

## Project layout

```
src/
  index.html             # <html lang="fr">, viewport, theme color
  global.scss            # responsive layout, theme variables and login styles
  main.ts                # bootstrapApplication entry
  app/
    app.config.ts        # zoneless, routed, HttpClient(fetch)+interceptors, translation and agent providers
    app.component.ts     # login gate ↔ header + router outlet based on auth state
    app.routes.ts        # /chat, /team, /teams-weekly-report, /configuration
    chat/chat.component.ts     # agent chat page, composer pinned at the bottom
    team/team.component.ts     # association, league and team selection
    report/report.component.ts # weekly report page and current ID selection
    config/config.component.ts # configuration page (colors)
    core/theme.service.ts   # primary/secondary colors, localStorage persistence
    core/login.component.ts # 6-digit access code screen
    core/auth.service.ts    # login/logout against POST /api/auth/login
    core/auth-storage.ts    # bearer token persistence (localStorage)
    core/auth.interceptor.ts # Authorization header attach + 401 → logout
    core/api.service.ts     # HttpClient wrapper for GET /api/health
    core/models.ts          # HealthReport / ServerTime interfaces
  environments/environment.ts  # apiUrl: '/api'
  assets/i18n/en.json     # English translations
  assets/i18n/fr.json     # French translations
```

Default language is French (`fr`) with English fallback (`en`). The header
button toggles between the two and updates `<html lang>`.
