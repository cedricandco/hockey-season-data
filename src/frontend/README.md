# Hockey Season Frontend

Ionic 9 + Angular 22 (standalone, zoneless) frontend with French/English
translation via `@ngx-translate/core` 18.

## Prerequisites

- Node.js >= 22
- Backend API running (see `src/backend/README.md`) on `http://localhost:5000`

## Run locally

```bash
npm install
npm start
```

The dev server proxies `/api/*` to `http://localhost:5000` (see `proxy.conf.json`),
so relative `apiUrl: '/api'` works out of the box.

## Build

```bash
npm run build
```

The deployable browser bundle, including `index.html`, lands in
`dist/hockey-frontend/browser`.

## Azure Static Web Apps deployment

The GitHub Actions workflow builds the frontend with Node.js 22 and deploys
`dist/hockey-frontend/browser` as prebuilt static assets. It does not deploy the
backend: the .NET Minimal API is not an Azure Functions project. The frontend's
`/api/health` request requires the backend to be hosted and routed separately;
it will not work with the static deployment alone.

## Project layout

```
src/
  index.html             # <html lang="fr">, viewport, theme color
  global.scss            # Ionic core styles + small app styles
  main.ts                # bootstrapApplication entry
  app/
    app.config.ts         # zoneless, Ionic, HttpClient(fetch), ngx-translate providers
    app.component.ts     # <ion-app> shell rendering the home page
    home/home.component.ts  # API status page (ping button, language toggle)
    core/api.service.ts   # HttpClient wrapper for GET /api/health
    core/models.ts        # HealthReport / ServerTime interfaces
  environments/environment.ts  # apiUrl: '/api'
  assets/i18n/en.json     # English translations
  assets/i18n/fr.json     # French translations
```

## Translation keys

`APP.TITLE`, `HOME.TITLE`, `HOME.DESCRIPTION`, `HOME.PING_BUTTON`,
`HOME.SERVER_TIME`, `HOME.STATUS.OK`, `HOME.STATUS.ERROR`, `LANGUAGE.SWITCH`.

Default language is French (`fr`) with English fallback (`en`). The header
button toggles between the two and updates `<html lang>`.
