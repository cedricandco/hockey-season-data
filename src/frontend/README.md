# Hockey Season Frontend

Angular 22 (standalone, zoneless) responsive frontend with French/English
translation via `@ngx-translate/core` 18. The app has five routed pages:
chat, team details, weekly report, configuration (colors + followed teams), and
administration (administrators only), plus the first-run team selection shown
to a brand-new coach account. A visitor who is not signed in sees the public
landing page instead.

## Prerequisites

- Node.js >= 22
- Backend API running (see `src/backend/README.md`) on `http://localhost:7071`

## Run locally

```bash
npm install
npm start
```

The dev server proxies `/api/*` to `http://localhost:7071` by default (see
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

`npm run test:responsive` drives the whole app in Chrome/Edge. The app
authenticates through Auth0 Universal Login, so the suite serves a fake tenant
(`mockAuth0` in `tests/responsive.mjs`): `/authorize` redirects straight back,
`/oauth/token` mints RS256 tokens signed by a key generated at startup, and
`/.well-known/jwks.json` publishes the matching public key so the SDK's signature
check passes. Each width runs the public landing page (preview selectors, the
`localStorage` preview counter, the account benefits), then the login, chat,
game card, team page (including the games block), weekly report and
configuration page assertions.

Run the unit tests with:

```bash
npm test
```

The browser layout checks (`npm run test:responsive`) start their own frontend
server, use simulated API responses, and run in a locally installed Chrome or
Edge; they do not require the backend. `VIEWPORTS=320,1280 npm run
test:responsive` restricts the run to those widths. If the browser is not found
automatically, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to its executable.
Browser emulation does not replace checking the virtual keyboard and safe areas
on real iOS and Android devices.

## Authentication gate and public landing page

The application root (`app.component.ts`) is a public/private switch: until
Auth0 reports a session, it renders the **landing page**
(`landing/landing.component.ts`); afterwards it renders the full shell with the
tab pages. Every routed page requires the session, and the SPA calls the API
with the access token issued for the `https://api.hockeyseason.dev` audience.
The HTTP interceptor (`authHttpInterceptorFn`) adds
`Authorization: Bearer <token>` to each `/api/` request (mirrored on
`X-Authorization` by `core/auth-header.interceptor.ts`) and the AG-UI
agent adds both headers in `agent/hockey-assistant-http-agent.ts`.
The agent waits for Auth0 to supply or renew the token on each request; a token
acquisition failure stops the request instead of sending it anonymously. The
backend rejects invalid tokens with `401 { "error": "invalid_token", ... }`.

### What the landing page offers

The landing page explains the site and lets a visitor try it before creating an
account:

- **Free preview** — association → division → league → team, then the team's
  statistics. The preview reads the anonymous `/api/preview/*` routes (the only
  unauthenticated API surface, see the backend README) and renders the result
  with `team/team-stats.component.ts`, the *same* component as the signed-in
  team page, so the figures are identical. In guest mode the opponent links and
  the box-score button are hidden — those routes need an account.
- **Three free attempts** — `landing/preview-trials.ts` (unit tested) counts
  them in `localStorage` under `hockey-season-preview-trials`. Re-viewing the
  team already on screen costs no attempt; the fourth view replaces the preview
  button with the free-account call to action. The counter is a product rule,
  not a security boundary: it is per device and can be cleared.
- **Why create a free account** — the benefits section states that an account
  keeps your teams within reach, that an **association president** can sign up
  and receive a weekly recap of every team of their league, and that a
  **category director** receives a weekly recap of every team of their
  category.

"Create a free account" opens Auth0 Universal Login with
`screen_hint=signup`; "Sign in" opens the regular login. Both land back on the
chat page (`appState.target = '/chat'`).

The anonymous preview must not go through the authenticated interceptor: the
`uriMatcher` of `httpInterceptor.allowedList` in `app.config.ts` excludes
`/api/preview/`, so a visitor without a session reads the preview without the
SDK attempting a silent authentication first (adding the route with
`allowAnonymous` still triggers that attempt).

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

## First run: team selection

A newly connected caller is provisioned by the backend as a **coach** (see the
backend README) and follows no team yet. `GET /api/users/me` reports
`teamCount`, so the landing route (`/chat`) is guarded by
`core/team-onboarding.guard.ts`: an account with `teamCount === 0` and a licence
that allows following teams is sent to `/team-selection`
(`team-selection/team-selection.component.ts`), which walks the same
association → division → league → team path as the configuration page and
follows the picked team. "Continue" enters the app; "Later" is remembered for
the browser session (`sessionStorage`, cleared on logout) so the guard does not
loop. Administrators and accounts that cannot follow teams are never
redirected.

## Configuration page

The configuration page (`/configuration`) is open to every signed-in user and
has two sections.
### Appearance and colors

Each user chooses primary and secondary colors. The choices are saved in the
browser's local storage and can be reset to the defaults, `#00124A` and
`#22C5FD`. Default CSS variables are defined in `src/global.scss`; keep the
defaults in `core/theme.service.ts` in sync if changing them. The chat displays
connection errors and offers a retry.

The header chrome is not driven by the theme variables: both the signed-in
`.site-header` and the mobile bottom navigation use the fixed brand gradient
`#000B3A → #00124A → #001B66`, exposed as `--brand-gradient` in
`src/global.scss`. The headers of the team detail and game detail cards reuse
that variable so a detail card looks like an extension of the site header.

### Brand assets

The `Game Tracker` logo lives in `src/assets/logo/`:

- `game-tracker.png` — the original square artwork, kept as the source of truth.
- `game-tracker-mark.png` — the transparent `G` emblem, used by the signed-in
  header over its navy gradient.
- `game-tracker-badge.png` — the emblem on a navy badge, used by the public
  landing header where the page itself is light.
- `favicon.ico`, `favicon-32x32.png`, `apple-touch-icon.png` — browser icons,
  generated from the same emblem and referenced from `src/index.html`.

The generated files are derivatives: crop and rescale the emblem out of
`game-tracker.png` rather than hand-editing them, so the mark, badge and icons
stay consistent.

### Followed teams

The page lists the teams the caller follows and lets them follow a new one by
walking the reference data — **association → division → league → team** — each
list staying disabled until its parent is picked (`GET /api/subscriptions/*`).
The selected team is added with `POST /api/users/me/team-subscriptions`.

The add respects the licence of the caller's role: the badge shows the
consumption (`3 / 5`) and comes from `limits[].{max,used,canAddMore}` of
`GET /api/users/me/subscriptions` (`max: null` means unlimited). The button is
disabled when the cap is reached, and the backend re-checks it — a concurrent
`409` surfaces as "limit reached". A followed team can be removed with
`DELETE /api/users/me/team-subscriptions/{teamId}`, except during its 15-day
lock: the page shows the unlock date and the backend answers `423 Locked`.

### Association president (base name)

A caller holding the PRESIDENT licence also sees an **Association president**
card at the top of the section. It picks an association and takes a team **base
name** (a datalist suggests the base names already followed); the backend lists
`GET /api/subscriptions/associations/{id}/teams?baseName=…` — every team of the
association whose name starts with it, in all divisions, leagues and categories
of the latest imported season, matching on whole words and ignoring case and
accents (`GRIZZLY 1 STE-JULIE` matches "grizzly", not "grizzlys").

"Follow all these teams" sends one `POST /api/users/me/president-team-subscriptions`
(`{ associationId, baseName }`). Under the president scope the team cap and the
15-day removal lock are lifted, so the batch can be pruned immediately: the
followed list below stays the single place where a team is removed, and the
teams covered by the typed base name are highlighted there. Duplicates and
unknown ids are reported in the response, not refused (`403` when the caller
has no PRESIDENT licence — the card is hidden in that case).

### Category director (one team name of a category)

A caller holding the DIRECTOR licence sees a **Category director** card built
the same way: it picks an association, a **category** — `M11`, `M13`, `M15`,
`M17`, `M19` or `Junior`, listed by
`GET /api/subscriptions/associations/{id}/categories` with the number of teams
of each — then types a **team name** ("Grizzly"). "Show the teams" lists them
(`GET /api/subscriptions/associations/{id}/categories/teams?category=M13&baseName=Grizzly`)
and "Follow all these teams" sends one
`POST /api/users/me/director-team-subscriptions`
(`{ associationId, category, baseName }`). The letters refining a category
(A, B, AA…) are folded into the level, so the director covers every letter at
once; the team cap and the 15-day removal lock are lifted exactly like the
president flow.

A category is followed **one name at a time**. Once a name of the level is
followed (the team list reports each followed team's `category`), the card
shows a notice naming it and disables the add button until those teams are
removed — the backend enforces the same rule with `409 category_name_locked`.

## Chat page

The chat page (`/chat`) is the default route and fills the viewport below the
header: the transcript scrolls and the composer stays pinned at the bottom of
the page. Enter sends a message and Shift+Enter inserts a new line.
The Hockey Assistant answers questions about league data. Match cards use the
game ID chosen by the agent to load `GET /api/games/{gameId}` directly, so their
goals, penalties and per-period totals do not depend on the model copying them.
Both the local API and Functions host support the endpoint.

## Team page

The team page (`/team`) shows two blocks over the teams the caller follows
(`GET /api/nav/teams`):

1. **Teams I follow** — every followed team with its league, division and
   association names; picking one loads its detail.
2. **Selector** — association → division → league → team, each level listing
   only the values that own at least one followed team (an association,
   division or league without a followed team is never offered).

Every identifier is a **database id** (`organizations.id`, `divisions.id`,
`leagues.id`, `teams.id`). The selected team loads `GET /api/teams/{teamId}`:
the backend validates the follow and resolves the published Spordle schedule
from the database, so the page never handles a Spordle id. The response also
carries `teamLinks`, the mapping from the Spordle team ids of the games to the
database ids the page navigates with — the games themselves are the league's,
keyed on Spordle ids. A caller who follows nothing sees a hint with a link to
the Configuration page instead of the two blocks. The selection is mirrored in
the query string (`association`, `division`, `league`, `team`); the page watches
those parameters instead of reading them once, so an in-page link to another
team re-applies the selection without a reload. A level of the URL that does not
exist in the follows stops the restore at the deepest valid one
(`team-route.ts`, unit tested).

The statistics themselves are rendered by `team/team-stats.component.ts`, which
wraps the summary, the games block and their layout. The landing page's free
preview uses the same component in **guest mode** (`[guest]="true"`): identical
figures, but the opponent links and the box-score buttons are hidden because
`/team` and `/api/games/*` require an account.

### Games of the team

Below the key figures the page shows two blocks (`team-games.component.ts`):

- **Last five games**, most recent first. "Show 5 more games" adds five each
  time and disappears once the whole schedule is shown. Each played game has a
  **Game detail** button that expands its box score in place — the same
  `app-game-detail` card the chat renders for the `show_game_detail` tool, loaded
  through `GET /api/games/{gameId}`.
- **Next two games**, in playing order. The opponent of each is a link to that
  opponent's team page, carrying the association/division/league of the current
  team plus the opponent's `teams.id`; a team the league mapping does not know is
  shown as plain text rather than a broken link.

The chat card (`game-card.component.ts`) keeps only the tool-call plumbing: it
loads the game and delegates rendering to `app-game-detail`, whose styles live in
`global.scss` because the card is mounted both inside the transcript and on the
page.

## Weekly report page

The weekly report page (`/teams-weekly-report`) loads the previous seven days
through today by default. The date range is adjustable. The page sends **only**
the two dates: the backend builds the report from the teams the caller follows,
so nothing is hardcoded and no id is passed by the client. A caller who follows
nothing gets an empty report instead of another league's data.

Games are ordered chronologically. Standings and games are shown per followed
league and schedule, followed by all published suspensions of that league.
Unavailable sources are clearly identified rather than presented as empty
results. Division cards use the league, division and schedule names stored in
the database. Tracked teams and their scores are highlighted green for a win,
gray for a tie, and red for a loss.

The authenticated `GET /api/weekly-report` accepts `startDate` and `endDate` in
`yyyy-MM-dd` format only — the followed teams come from the database. It returns
the original Spordle arrays for standings, games and suspensions, with an
`available` flag per source. Requests are retried up to three times for
transient failures; a failed source does not prevent the other sources from
appearing. Both the local API and Functions host support it.

## Project layout

```
src/
  index.html             # <html lang="fr">, viewport, theme color
  global.scss            # responsive layout, theme variables and login styles
  main.ts                # bootstrapApplication entry
  app/
    app.config.ts        # zoneless, routed, HttpClient(fetch)+interceptors, translation and agent providers
    app.component.ts     # public landing page ↔ header + router outlet based on auth state
    app.routes.ts        # /chat, /team-selection, /team, /teams-weekly-report, /configuration, /admin, /admin/reference
    landing/landing.component.ts # public landing page (hero, free preview, account benefits)
    landing/preview-trials.ts    # localStorage preview counter (unit tested)
    team-selection/team-selection.component.ts # first-run team picker (new coach accounts)
    chat/chat.component.ts     # agent chat page, composer pinned at the bottom
    team/team.component.ts     # association, division, league and team selection (followed only)
    team/team-stats.component.ts # team statistics block shared by the team page and the landing preview
    team/team-games.component.ts # last games (paged by five, box score in place) and next two games
    team/team-summary.component.ts # team header, figures, players and standings (shared with the chat card)
    team/team-route.ts         # pure query-string restore of the selection (unit tested)
    team/team-results.ts       # pure game helpers: last/next games, opponent mapping (unit tested)
    chat/game-detail.component.ts # box score card shared by the chat renderer and the team page
    report/report.component.ts # weekly report page (followed leagues, no hardcoded ids)
    config/config.component.ts # configuration page (colors + followed teams)
    config/subscription.models.ts # models of the follow-team selector and subscription list
    config/subscription-helpers.ts # pure helpers of that section (unit tested)
    admin/admin.component.ts   # admin page: user list + role (licence) assignment
    admin/admin-reference.component.ts # admin page: divisions/leagues/teams import
    admin/reference-helpers.ts # pure helpers of the reference page (unit tested)
    core/account.service.ts    # local account (roles + admin flag), cached for the session
    core/admin.guard.ts        # route guard: administrators only
    core/team-onboarding.ts    # first-run helpers (unit tested): needsTeamSelection, "later" choice
    core/team-onboarding.guard.ts # route guard: sends a new coach account to /team-selection
    core/theme.service.ts   # primary/secondary colors, localStorage persistence
    core/auth.service.ts    # logout (provider session + local fallback)
    core/auth-logout.ts     # pure logout strategy helpers (unit tested)
    core/api.service.ts     # HttpClient wrapper for the API endpoints
    core/models.ts          # HealthReport / ServerTime interfaces
  environments/environment.ts  # apiUrl: '/api'
  assets/i18n/en.json     # English translations
  assets/i18n/fr.json     # French translations
```

Default language is French (`fr`) with English fallback (`en`). The header
button toggles between the two and updates `<html lang>`.

## Admin page

The administration page (`/admin`) lists the local accounts (`GET /api/users`)
and lets an administrator tick the licence roles of each user, saved with
`PUT /api/users/{userId}/roles` (the whole role set is replaced, stored in
`user_roles`). The roles come from `GET /api/roles`.

It is restricted to administrators: the navigation entry only renders when the
local account returned by `GET /api/users/me` has `isAdmin`, and the
`adminGuard` redirects everyone else to `/chat`. The backend re-checks the
administrator flag (`users.is_admin` or the Auth0 `roles=admin` claim) on every
admin request, so hiding the UI is never the only protection.

### Divisions, leagues and teams (`/admin/reference`)

The second administration tab defines the reference data of an association:
pick the association from the `organizations` table
(`GET /api/reference/organizations`), type the division number — the
`divisions.external_id`, e.g. 6193 or 6194 — and pick the season
(`GET /api/reference/divisions/{divisionId}/seasons`). One call to
`POST /api/reference/import` then creates/updates the division, the leagues
(Spordle categories, stored under their **French** name, e.g. "M13 B") and
their teams, and deletes the leagues/teams that disappeared from Spordle
together with the subscriptions pointing at them. The page shows what was
created, updated and deleted; warnings report the schedules whose teams could
not be read.

### Logout

The user menu calls `AuthService.logout()`, which clears the cached local
account, asks Auth0 (`/v2/logout`) to end the **identity-provider** session and,
if that redirect fails or is blocked within a few seconds, clears the SDK token
cache and reloads into the login gate. A plain local clear would leave Auth0
signed in, so the next login would silently reuse the same account.

