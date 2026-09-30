# Frontend Architecture

> **Locked stack.** The frontend is **Kotlin Multiplatform (KMP) + Compose Multiplatform** —
> shared logic *and* shared Compose UI. It is **built and live**: the core dashboard pages exist and
> run against the real API (desktop + web/Wasm from one codebase).
> Authoritative specs in `.claude/docs/design/spec/`: `frontend.md` (stack), `frontend-ia.md`
> (navigation/IA, three-plane shell, role gating), `frontend-structure.md` (module layout),
> `frontend-data-layer.md` (query/cache layer), `frontend-design-system.md` + `.catalogue.md` (shadcn port).

### What's established

- **Dashboard = the KMP + Compose app, one codebase targeting desktop AND web (Wasm)** (mobile
  Android/iOS later) — the desktop and web builds are the **exact same** universal, full-featured
  client.
- **Profile-agnostic, direct-connect** — the dashboard just needs a backend URL and talks REST +
  SignalR straight to it; there is **no central broker/orchestrator**, so a self-host bot needs
  **zero NoMercy infrastructure or oversight**:
  - Self-host → points at `localhost` / LAN, or the operator's own exposed URL for remote.
  - SaaS → points at the SaaS API.
  - The **web build is served by its bot → implicit single host**: it only talks to the origin that
    served it (no host picker, mDNS is a no-op). To use a different bot's web dashboard, open that
    bot's URL. The **native app is multi-origin** — it holds a list of saved server connections (the
    profile-menu switcher), fed by **mDNS LAN auto-discovery** + manual add; switching swaps the
    active backend + its keychain token and reconnects REST + SignalR.
- **The bot serves two web surfaces:** (1) the **Compose/Wasm dashboard** (same app as desktop, for
  no-install / remote access), and (2) the **public surfaces** that viewers/OBS hit without any app:
  - Song-request page (viewers)
  - Overlays / widgets (OBS browser source — the widget system)
  - OAuth callback landing

  These public surfaces are **compiled widgets** — built from source at build time, served by the bot, and
  CDN-cached for SaaS; **not** static files (there is no `web/` folder). Widget source is compiled on the
  server and served by the bot from the compiled-widget cache — see `spec/widget-sdk.md` and
  `spec/widgets-overlays.md`.

### Backend comms

- A **typed shared KMP client** is the single integration point with the backend:
  - **REST** against the v1 API.
  - **SignalR** for realtime (dashboard / overlay hubs).
- Auth token is passed to hubs as `?access_token=<jwt>` (see SignalR Hubs above).

### i18n

- Supported languages: English (`en`), Dutch (`nl`). Never hardcode user-facing strings.
- Strings live in `composeApp/src/commonMain/composeResources/values/strings.xml` (+ `values-nl/`);
  locale switching via `core/i18n/LocalAppLocale`. Every new string gets both languages.

### Running the Frontend

From `app/` (Windows: `.\gradlew.bat` instead of `./gradlew`):

```bash
./gradlew :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t   # web dev server (hot reload), http://localhost:5090
./gradlew :composeApp:run                                         # desktop (dev)
./gradlew :composeApp:wasmJsBrowserDistribution                    # prod web bundle (use --rerun-tasks for a clean prod build)
./gradlew :composeApp:packageDistributionForCurrentOS              # desktop installer (MSI/DMG/DEB)
```

The web dev server listens on `5090`; run it alongside a plain `dotnet run` API (its committed
default, `5080` — see *Running the Backend*) with **no flags or env vars needed** — the dev server's
webpack proxy forwards `/api` + `/hubs` to `http://localhost:5080` by default
(`webpack.config.d/proxy.js`, override with `NNZ_DEV_BACKEND` to point at a different backend). It
does not forward `X-Forwarded-Host`/`X-Forwarded-Proto`, so OAuth redirects (`ResolvePublicOrigin`)
resolve to the API's own origin — register `http://localhost:5080/api/v1/auth/twitch/callback` in
the Twitch Developer Console for local dev. `start.sh` runs both together this way. The prod web
bundle is bundled automatically into the API
publish and Docker image (that build serves everything from one origin — no proxy, no port split); the deploy script's `--app` flag wraps
the installer task (see `DEPLOY.md`).

### Verify before commit

- `:composeApp:jvmTest` — includes the design-system guard suite and `ApiContractTest`.
- `:composeApp:compileKotlinWasmJs` — jvmTest cannot catch Wasm-only breaks.
- `scripts/refresh-openapi.ps1` + `ApiContractTest` / `ApiRouteContractTest` on any contract change.
- One import per Compose string resource (`import ...generated.resources.<key>`) — a missing per-string import breaks the build.
