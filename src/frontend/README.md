# Hockey Season Frontend

Angular 22 (standalone, zoneless) responsive frontend with French/English
translation via `@ngx-translate/core` 18. The app has three routed pages:
a chat page (translation agent), a weekly report placeholder, and a
configuration page for the colors.

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

## Authentication gate

Every screen except the login gate requires an OIDC-style JWT: the app asks
for the shared 6-digit access code (`LOGIN` translations), posts it to
`POST /api/auth/login`, and stores the returned bearer token in local storage
(`core/auth-storage.ts`). Each HTTP request adds
`Authorization: Bearer <token>` (`core/auth.interceptor.ts`) and the AG-UI
agent adds the same header in `agent/translation-http-agent.ts`. A rejected
token (401) clears the session and flips the app back to the login screen; the
header button logs out explicitly.

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
through the deployed Static Web App as well as locally. Configure
`Auth__PinCode` and `Auth__SigningKey` in the Static Web App's application
settings so the login gate protects the agent endpoint; access without a valid
access code is rejected with 401.

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
The current backend agent translates messages into English; it does not yet
answer questions about hockey data.

## Weekly report page

The weekly report page (`/rapport`) is intentionally empty for now; its
placeholder text comes from the `REPORT` translation keys.

## Project layout

```
src/
  index.html             # <html lang="fr">, viewport, theme color
  global.scss            # responsive layout, theme variables and login styles
  main.ts                # bootstrapApplication entry
  app/
    app.config.ts        # zoneless, routed, HttpClient(fetch)+interceptors, translation and agent providers
    app.component.ts     # login gate ↔ header + router outlet based on auth state
    app.routes.ts        # /chat, /rapport, /configuration
    chat/chat.component.ts     # agent chat page, composer pinned at the bottom
    report/report.component.ts # weekly report placeholder page
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
