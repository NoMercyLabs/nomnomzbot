# Frontend Architecture

> **Locked stack.** The frontend is the **Vue 3 dashboard** in `dashboard/` (Vite, TypeScript,
> shadcn-vue components, Tailwind). It is **built and live**: the dashboard pages run against the real
> API, and the bot serves the build from its own origin. The Kotlin Multiplatform + Compose app that
> came before it was removed on 2026-10-21 (Stoney: "get Vue ported over to dev and take down kmp
> fully"); do not recreate `app/`.
> Authoritative specs in `.claude/docs/design/spec/`: `frontend.md` (stack), `frontend-ia.md`
> (navigation/IA, three-plane shell, role gating), `frontend-structure.md` (module layout),
> `frontend-data-layer.md` (query/cache layer), `frontend-design-system.md` + `.catalogue.md` (shadcn port).
> Some of those specs still describe the Compose port; the design tokens and component rules in them
> still apply to the Vue components.

### What's established

- **Dashboard = the Vue app in `dashboard/`**, a web client. It is installable as a PWA.
- **Profile-agnostic, direct-connect** — the dashboard talks REST + SignalR straight to the backend that
  served it; there is **no central broker/orchestrator**, so a self-host bot needs **zero NoMercy
  infrastructure or oversight**:
  - Self-host → the bot serves the dashboard at `localhost` / LAN, or the operator's own exposed URL.
  - SaaS → the SaaS API serves it (NoMercy Labs only, see `DEPLOY.md`).
  - The dashboard only talks to the origin that served it. To use a different bot's dashboard, open
    that bot's URL.
- **The bot serves two web surfaces:** (1) the **Vue dashboard**, and (2) the **public surfaces** that
  viewers/OBS hit without any app:
  - Song-request page (viewers)
  - Overlays / widgets (OBS browser source — the widget system)
  - OAuth callback landing

  These public surfaces are **compiled widgets** — built from source at build time, served by the bot, and
  CDN-cached for SaaS; **not** static files (there is no `web/` folder). Widget source is compiled on the
  server and served by the bot from the compiled-widget cache — see `spec/widget-sdk.md` and
  `spec/widgets-overlays.md`.

### Layout of `dashboard/src`

`api/` (typed client and the generated `schema.d.ts`), `realtime/` (SignalR), `router/`, `stores/`,
`composables/`, `components/` (shared and layout components), `lib/` (helpers, one file per kind),
`i18n/` (shared messages), `layouts/`, `views/` (one folder per page area, each with its own `i18n/`).

### Backend comms

- A **typed API client** (`dashboard/src/api`) is the single integration point with the backend:
  - **REST** against the v1 API; types come from `server/openapi/v1.json` (`npm run gen:api`).
  - **SignalR** for realtime (dashboard / overlay hubs).
- Auth token is passed to hubs as `?access_token=<jwt>` (see SignalR Hubs above).

### i18n

- Supported languages: English (`en`), Dutch (`nl`). Never hardcode user-facing strings.
- Strings live in the `i18n/en.json` and `i18n/nl.json` files next to each view (shared ones in
  `dashboard/src/i18n`). Every new string gets both languages.

### Running the Frontend

From `dashboard/`:

```bash
npm ci
npm run dev          # Vite dev server (hot reload), http://localhost:5090
npm run build-only   # production bundle in dashboard/dist
```

The dev server listens on `5090`; run it alongside a plain `dotnet run` API (its committed
default, `5080` — see *Running the Backend*) with **no flags or env vars needed** — the Vite proxy
forwards `/api` + `/hubs` to `http://localhost:5080` by default (`vite.config.ts`, override with
`NNZ_API` to point at a different backend). OAuth redirects (`ResolvePublicOrigin`) resolve to the
API's own origin — register `http://localhost:5080/api/v1/auth/twitch/callback` in the Twitch
Developer Console for local dev. `start.sh` runs both together this way. The production bundle is built
by the root `Dockerfile` (a Node stage) and, for the single-file publish, by `-p:BundleDashboard=true`;
that build serves everything from one origin — no proxy, no port split.

### Verify before commit

The same commands the CI dashboard job runs, from `dashboard/`:

- `npm run gen:api` then `git diff --exit-code -- src/api/schema.d.ts` — the generated types match the snapshot.
- `npm run type-check`
- `npm run lint` (oxlint, ESLint, spelling, duplicate-code)
- `npx vitest run`
- `npm run build-only`
- `scripts/refresh-openapi.ps1` + `npm run gen:api` on any contract change.
