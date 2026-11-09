# Hockey Season Frontend

Angular 22 (standalone, zoneless) responsive frontend with French/English
translation via `@ngx-translate/core` 18. The main page connects directly to
the existing AG-UI translation agent.

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
through the deployed Static Web App as well as locally. The agent endpoint is
anonymous, so anyone who can reach the site can use the configured model and
potentially incur charges.

The explicit `@emnapi/core` and `@emnapi/runtime` development dependencies
resolve conflicting WebAssembly build-tool peer dependencies in the lockfile.
Keep them aligned when updating the frontend build dependencies.

## Appearance and chat

The appearance button in the header lets each user choose primary and secondary
colors. The choices are saved in the browser's local storage and can be reset
to the defaults, `#0e3a70` and `#3eb838`. Default CSS variables are defined in
`src/global.scss`; keep the defaults in `home.component.ts` in sync if changing
them. The current backend agent translates messages into English; it does not
yet answer questions about hockey data. Enter sends a message and Shift+Enter
inserts a new line. The chat displays connection errors and offers a retry.

## Project layout

```
src/
  index.html             # <html lang="fr">, viewport, theme color
  global.scss            # responsive layout and theme variables
  main.ts                # bootstrapApplication entry
  app/
    app.config.ts        # zoneless, HttpClient(fetch), translation and agent providers
    app.component.ts     # application shell rendering the home page
    home/home.component.ts  # agent chat, language toggle, and theme settings
    core/api.service.ts   # HttpClient wrapper for GET /api/health
    core/models.ts        # HealthReport / ServerTime interfaces
  environments/environment.ts  # apiUrl: '/api'
  assets/i18n/en.json     # English translations
  assets/i18n/fr.json     # French translations
```

Default language is French (`fr`) with English fallback (`en`). The header
button toggles between the two and updates `<html lang>`.
