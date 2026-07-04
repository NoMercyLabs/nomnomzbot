# NomNomzBot — AI Assistant Context

An open-source, multi-tenant Twitch bot platform. One deployment supports unlimited channels — each
streamer gets a full isolated dashboard, pipeline editor, custom commands, event responses, timers,
overlays, and integrations (Spotify, Discord, YouTube, TTS).

Licensed under **AGPL-3.0**. Copyright (C) NoMercy Labs.

---

## Critical Rules — Read First

- **Everything is `NomNomzBot.*`** — namespace, folder, assembly, and product all match. Every `.cs` file's `namespace` and every csproj `<RootNamespace>` is `NomNomzBot.*`. (History: the legacy code carried `NoMercyBot.*` from an incomplete earlier rename; the clean-slate rebuild **fully migrated** it to `NomNomzBot.*` via a repo-wide rename — there is no `NoMercyBot` left in code or specs, **do not reintroduce it**. The **copyright holder is still NoMercy Labs** — the company name in license headers is unaffected.)
- **Username is `Stoney_Eagle`** — underscore, not hyphen. Never change this.
- **shadcn/ui (new-york) is the source of truth for the dashboard design** — ported 1:1 to Compose; full spec in `.claude/docs/design/spec/frontend-design-system.md`. The old Figma file (`MkKBuW2Ee6T5jC8fCtZsM0`) is **discarded** (not a viable dashboard); a fresh Figma may be minted from the spec later.
- **HTML mockups at `nomnomzbot-design/mockups/`** are a loose historical reference only — the design system, not the mockups, is authoritative.
- **No `Co-Authored-By` in git commits** — ever.
- **No MediatR** — services are called directly via typed interfaces registered in DI.
- **No Roslyn** — don't use Roslyn for code generation or analysis.
- **Don't ask permission to fix bugs** — find it, fix it, move on.
- **No fake/seed data for community** — all community/viewer data must come from the real Twitch API. Never fabricate viewer lists, subscriber counts, etc.
- **Full external-API coverage — implement, never remove.** Every method and every event of every API we integrate (Twitch Helix + EventSub, Spotify, Discord, YouTube, …) gets implemented; a missing one is a gap to ADD. Beta/restricted surfaces ship with graceful degradation, never get skipped or deleted. Never act on a "deprecated/skip" claim about an external API without re-verifying the live docs first.
- **Don't ask "should I continue?" or "want me to fix this?"** — just do it.
- **Match the design system exactly** — the shadcn (new-york) tokens, component catalogue, and variant tables in `frontend-design-system.md`; correct tokens/spacing/variants. Never hardcode a color or `dp`; do not approximate.
- **Test every interactive element** — never claim something "works" without full validation.
- **No half-assed work** — seed ALL data, test EVERY button, run parallel where possible.
- **Track split** — backend (`server/`) = `Stoney_Eagle` + Claude; frontend (`app/`) = `aaoa-dev` (designer). Commits never cross the boundary; cross-track needs go through `handoff/`. See *Team & Track Ownership*.
- **CI green before sign-off** — run the full test suite before EVERY commit; after EVERY push, `gh run watch <run-id> --exit-status` and fix failures immediately. See *CI Gate*.
- **Check your handoff inbox at session start** — `handoff/for-backend.md` (backend track) / `handoff/for-frontend.md` (frontend track). Open items are picked up automatically, no prompt needed.
- **`saas` mode is a RESTRICTED option** — operating NomNomzBot as a hosted service for third parties is against the project license (reserved to NoMercy Labs); self-hosting your own bot is always free. Every doc/script surface that shows saas instructions must carry this restriction marker — never drop it.

---

## Code Quality Bar — Enforce Without Being Asked

This is a long-lived, top-notch project. Write every file with love and care, not speed — this is not a sprint. Apply these on **every** code change, unprompted:

- **Think 3× before writing.** Right *place* for the file? Right *structure* for the function? Right *path forward*? When placement or structure isn't obvious, state the decision and the reason. Never slap a file down "somewhere".
- **Placement by responsibility.** Identify the layer (Domain / Application / Infrastructure / Api / platform SDK) and the domain folder, then put the file beside its siblings. No `misc` / `helpers` / `utils` dumping grounds. Organize by **domain**, never by provenance (no `Generated/` folders).
- **One responsibility per file, class, and function.** Keep functions small and single-purpose. A function that needs a comment to explain *what* it does is usually two functions.
- **DRY — strongly encouraged, not absolute.** Extract genuinely shared logic; but don't unify things that merely look alike — wait for the third occurrence (Rule of Three). Clarity beats premature abstraction.
- **SOLID.** Depend on interfaces (the existing convention), constructor-inject, no god classes, no fat interfaces. Extend via new types, not by growing `switch` statements.
- **No bloat / YAGNI.** No speculative params, abstractions, or "just in case" code. No file bloat — split when a file starts doing more than one thing. Generated code stays generated and thin; hand-written code stays minimal.
- **Match the surrounding code.** File-scoped namespaces, `Nullable` enabled, async all the way (never `.Result`/`.Wait()`), `Result<T>` over exceptions/null, existing naming. Don't introduce a second style.
- **Explicit types — never `var`.** Stoney's house style: spell the type on every local (`List<string> ids = ...`, not `var ids = ...`). The *only* exception is when the type is genuinely unnameable — an anonymous-type projection (`new { ... }` from an EF/LINQ `Select`/`GroupBy`/`ToDictionary`), where C# forces `var`. `.editorconfig` flags `var` as an **error** (IDE0008). This is a hard rule and **must be in every agent brief that writes C#** — it has been silently violated before.
- **License header on every source file you create.** This project is **AGPL-3.0** (`server/LICENSE`); copyright holder is **NoMercy Labs**. At the very top, verbatim:
  ```
  // -----------------------------------------------------------------------------
  //  Copyright (c) NoMercy Labs.
  //
  //  This file is part of NomNomzBot, free software licensed under the GNU Affero
  //  General Public License v3.0 or later. You may redistribute and/or modify it
  //  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
  //
  //  SPDX-License-Identifier: AGPL-3.0-or-later
  // -----------------------------------------------------------------------------
  ```
  Use `//` for C#/TS/JS. Skip files that can't carry comments (plain `.json`). For generated files, `// <auto-generated />` must be the **first** line (compiler/analyzer requirement), with this header directly beneath it. Rider renders it as the file header via `server/.editorconfig` `file_header_template` + `IDE0073`.
- **Never leave a temp file behind.** Scratch downloads, throwaway scripts, report dumps, spec copies — delete them the moment the step that needed them is done, or write them outside the repo tree. The working tree stays clean, always.
- **Format with CSharpier before every commit.** After writing or editing any C#, run `dotnet csharpier format .` from `server/`; `dotnet csharpier check .` must pass before committing — a hard gate alongside build + tests. The formatter is pinned in `server/.config/dotnet-tools.json` (`dotnet tool restore` to install). This applies to code written by dispatched agents too — their briefs must include the format step.

### Testing Standard — tests must prove behavior

- **Test data shapes and the consequences of actions, not the surface.** When an action runs, assert the resulting **state change**, the **events/messages emitted**, and the **side effects** — the software actually following through. Verify the *shape* of returned/persisted data (fields, types, invariants), not merely that a call returned non-null or didn't throw.
- **Surface/smoke-only tests are void.** "It returned something", "no exception", a mock asserting it was called — these contribute nothing, give false confidence, and waste time and money. Don't write them; don't count them.
- A test must be able to **fail for the right reason**: if the behavior it describes broke, the test breaks. If it can't, it isn't a test.

---

## Workflow — Vertical Slices, Committed When Validated

Work structured and organized — small, complete vertical slices, not scattered edits. The opposite of "change 10 files, a class here a feature there, commit later."

- **One executable step at a time.** Define the slice's start and finish up front, then build it block by block toward that goal.
- **A slice is the smallest change that delivers a working, testable piece of the full data flow** — end to end. Example: one API endpoint = controller action + service method + data access + test. Not "a controller here, a model there."
- **Commit each validated scope.** When a function / feature / model / endpoint is implemented, tested, and **validated to work to Stoney's standards** (proven, not merely compiling), commit it. One endpoint = one commit (or a few) covering its fully vetted flow. Surface the validation evidence.
- **Never scatter, never pile up.** Finish and validate one slice before starting the next. Don't let the working tree accumulate uncommitted changes — a large changed-file count (e.g. 381) is a failure state, not progress.

---

## Team & Track Ownership — Backend vs Frontend

Two people, one repo, two strictly separated tracks. Detect the active track from `git config user.name`.

| Track | Person | Owns (commit surface) | Never touches |
|-------|--------|-----------------------|---------------|
| **Backend** | `Stoney_Eagle` | `server/`, root infra (`docker-compose.yml`, `deploy.*`, `.env.example`, `.github/`), `CLAUDE.md`, `.claude/` | `app/` |
| **Frontend** | `aaoa-dev` (designer) | `app/` | `server/`, root infra, `CLAUDE.md`, anything security-sensitive |

- **Commits never cross the boundary.** A backend commit contains no `app/` files; a frontend commit contains no `server/` or root-infra files. If the working tree mixes both, stage and commit only your track's files — leave the rest untouched.
- **Never rewrite the other track's history.** No rebase, amend, force-push, or revert of anything the other track has pushed. Rebasing your **own unpushed** commits onto `origin/master` is fine (`git pull --rebase`); everything already on the remote stays as-is. A conflict inside the other track's files → stop and coordinate via a handoff entry, don't resolve it yourself.
- **`aaoa-dev` does not do backend or security — Claude carries that for him.** On the frontend track: never edit server code, secrets, tokens, OAuth, CORS, JWT, or auth logic on your own initiative. If a task seems to need it, use the boundary override below, and explain the backend/security reasoning to him in plain, non-jargon language so he learns why.
- **Boundary override — only via an explicit yes/no question, never silently.** When work on your track genuinely requires a change on the other track's side, ask the user directly with a yes/no `AskUserQuestion` ("This needs a <backend/frontend> change: <what and why>. Make it now?"). **Yes** → make the change, in its own commit(s), scoped to that need. **No** → write the full findings into the other track's handoff file: what is needed, the exact desired change, and your reasoning — then continue your work without crossing. This is self-enforcing: apply it unprompted, every time — needing a reminder is a failure.
- **The API contract is the only bridge.** The frontend consumes the backend exclusively through the typed shared KMP client and the committed `server/openapi/v1.json` snapshot. Contract changes originate on the backend track; the frontend syncs Kotlin DTOs from the snapshot (`ApiContractTest` guards drift).

### Handoff TODOs — cross-track work orders

Two committed files (so they travel through git between machines):

- `handoff/for-backend.md` — frontend leaves work for the backend track here
- `handoff/for-frontend.md` — backend leaves work for the frontend track here

Rules:

1. **At session start and before starting new work, read YOUR inbox.** Open items there are picked up automatically — the user does not need to mention them.
2. **Leaving work:** append an entry to the OTHER track's file using the template inside it (date, from, what, why, where, done-when). Commit it together with the work that produced it.
3. **Completing work:** move the entry to the file's **Done** section with the commit hash(es), committed alongside the fix.
4. **Entries must be self-contained** — the reader has no access to your conversation. Name the files, endpoints, and acceptance criteria explicitly.

---

## CI Gate — a push is not done until CI is green

1. **Before EVERY commit: run the tests.** Backend: `dotnet test` + `dotnet csharpier check .` from `server/`. Frontend: the Gradle test tasks incl. `jvmTest` (`ApiContractTest`). Never commit on red.
2. **After EVERY push: watch the run and block on it.**
   ```bash
   gh run list --limit 1                 # grab the run id for the pushed commit
   gh run watch <run-id> --exit-status   # block until it finishes
   ```
   Watching is part of the push — never end a turn with "CI will probably pass" or "I'll check later".
3. **CI red → fix it now.** Diagnose, fix, commit, push, watch again — before signing off or starting anything else. `master` never stays red.
4. **Known flake:** the Application test suite fails intermittently (~5%). A lone red that doesn't reproduce locally → re-run the job once before digging.

---

## Repository Layout

```
nomnomzbot/
├── server/                  # Backend — .NET 10, PostgreSQL, Redis
│   ├── src/
│   │   ├── NomNomzBot.Domain/          # Entities, domain events, value objects, interfaces
│   │   ├── NomNomzBot.Application/     # Use cases, services, pipeline engine, IEventBus
│   │   ├── NomNomzBot.Infrastructure/  # EF Core, Twitch services, EventSub, SignalR
│   │   └── NomNomzBot.Api/             # ASP.NET Core host, controllers, hubs, middleware
│   ├── tests/
│   │   ├── NomNomzBot.Domain.Tests/
│   │   ├── NomNomzBot.Application.Tests/
│   │   ├── NomNomzBot.Infrastructure.Tests/
│   │   └── NomNomzBot.Api.Tests/
│   └── Dockerfile
├── app/                     # Frontend — Kotlin Multiplatform + Compose Multiplatform (desktop + web/Wasm)
│   └── composeApp/          #   src/commonMain: App.kt, core/ (network client, i18n, design system),
│                            #   feature/<domain>/ (screens + state); i18n resources in
│                            #   composeResources/values/strings.xml (en) + values-nl/ (nl)
│                            # Public surfaces (OBS overlays, song-request) = compiled widgets served by the
│                            # bot + CDN-cached for SaaS (widgets-overlays); there is no static web/ folder.
├── handoff/                 # Cross-track work orders — for-backend.md / for-frontend.md
├── docker-compose.yml       # Root compose — references ./server
├── DEPLOY.md                # Deployment chooser — desktop / docker / saas × web / desktop app
├── deploy.sh                # One deploy script per OS: <scenario> [--app]
├── deploy.ps1
├── .env.example
└── nomnomzbot-design/       # HTML mockups, research docs, architecture specs (separate repo)
    ├── mockups/             # HTML reference implementations of Figma designs
    └── research/            # Architecture decisions, API research, design system docs
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend runtime | .NET 10, C# 14 |
| Backend framework | ASP.NET Core with Asp.Versioning.Mvc |
| ORM | EF Core 10 + Npgsql (PostgreSQL 16) |
| Cache / pub-sub | Redis 7 |
| Real-time | ASP.NET SignalR (WebSocket) |
| Auth | JWT + Twitch **Device Code Flow** login (secret-free) · OAuth code flow for integrations · web refresh token in an HttpOnly cookie |
| Logging | Serilog |
| Frontend (dashboard) | Kotlin Multiplatform (KMP) + Compose Multiplatform — one codebase, **desktop + web (Wasm)** identical UI; mobile later |
| Public surfaces | Widget system (OBS overlays, song-request, OAuth landing) — compiled from source at build time, served by the bot, CDN-cached for SaaS; the build→serve→cache pipeline is being specced |
| Backend comms (FE) | Typed shared KMP client over REST (v1 API) + SignalR (realtime) |

---

## Backend Architecture

### Clean Architecture Layers

Dependencies flow **inward only**. Domain knows nothing about the outside world.

```
NomNomzBot.Api             → Controllers, Hubs, Middleware, JWT, SignalR
NomNomzBot.Infrastructure  → EF Core, Twitch/Spotify/Discord/TTS services
NomNomzBot.Application     → Use cases, service interfaces, pipeline engine, IEventBus
NomNomzBot.Domain          → Entities, domain events, value objects, no external deps
```

### Key Design Decisions

- **No MediatR** — services are injected via typed interfaces (`IAuthService`, `ITwitchApiService`, etc.) and called directly. This keeps the call stack shallow and obvious.
- **`Result<T>` pattern** — operations that can fail return `Result<T>` instead of throwing. Never return null; always return a result with a `Success` flag and optional error.
- **Soft deletes** — entities use `IsDeleted` + EF Core global query filters. Never `DELETE` from the database.
- **Multi-tenancy** — resolved per-request by `TenantResolutionMiddleware` from the JWT `sub` claim. Each user sees only their own channel data.
- **Nullable reference types** — enabled everywhere (`<Nullable>enable</Nullable>`).
- **Global usings** — each project has a `GlobalUsings.cs`.
- **Async all the way** — never `.Result` or `.Wait()`.
- **Repository + IUnitOfWork** — no raw `DbContext` in controllers.

### Key Services

| Service | Location | Purpose |
|---------|----------|---------|
| `AuthService` | Infrastructure | JWT creation, Twitch token exchange (device code + refresh) |
| `ITwitchHelixClient` | Application contract, Infrastructure impl | Typed Helix client — 26 sub-clients covering the full Helix surface |
| `TwitchEventSubHostedService` | Infrastructure (`Platform/Eventing`) | EventSub lifecycle over `WebSocketEventSubTransport`; 74 event translators |
| `HelixChatProvider` | Infrastructure | Chat send (`IChatProvider`) via Helix Send Chat Message — every profile |
| `SpotifyService` | Infrastructure | Now playing, queue, playback control |
| `DiscordService` | Infrastructure | Guild sync, notifications |
| `TtsService` | Infrastructure | Azure Cognitive Services + ElevenLabs provider |
| `PipelineEngine` | Infrastructure | Executes pipeline action chains |

### Controllers (all under `/api/v1/`, source in `NomNomzBot.Api/Controllers/V1/`)

**~56 controllers, one per module** — do not rely on a hand-maintained list; browse them at
`http://localhost:5080/scalar` or in `Controllers/V1/`. Each domain spec's **§5 table** is the
authoritative contract (routes + Gate-2 action keys). Major groups: auth/channels/users,
commands/builtins/pipelines/event-responses/timers/quotes, chat/moderation, rewards/live-ops/stream,
economy (currency, catalog, games, savings jars, leaderboards) , music + public song-request, TTS,
community/analytics/dashboard, integrations + OAuth (Spotify/Discord/YouTube), webhooks (in/out),
widgets, sound clips, code scripts (sandbox), roles/permissions/permits, event store, federation,
billing, platform admin (IAM, feature flags, tenant ops).

### API Conventions

- All routes: `[Route("api/v{version:apiVersion}/...")]` with `[ApiVersion("1.0")]`
- All responses: `StatusResponseDto<T>` or `PaginatedResponse<T>`
- Pagination: `?page=1&pageSize=25`
- Errors: problem details (RFC 7807) for 4xx/5xx
- Interactive API docs: `http://localhost:5080/scalar`

### SignalR Hubs

| Hub | Path | Purpose |
|-----|------|---------|
| `DashboardHub` | `/hubs/dashboard` | Real-time dashboard updates (chat feed, stats, alerts) |
| `OverlayHub` | `/hubs/overlay` | Browser-source overlays (alerts, now-playing widgets) |
| `OBSRelayHub` | `/hubs/obs` | OBS WebSocket relay |
| `AdminHub` | `/hubs/admin` | Platform admin operations |

The frontend connects through the shared KMP SignalR client. Auth token passed as `?access_token=<jwt>`.

### Authentication Flow

1. **Login = Twitch Device Code Flow (secret-free):** `POST /api/v1/auth/twitch/device` → user approves
   on twitch.tv/activate → `POST /api/v1/auth/twitch/device/poll` returns JWTs. The bot account connects
   the same way (`/api/v1/auth/twitch/bot/device` + poll). Shared public client by default, BYOC encouraged.
2. The authorization-code callback (`/api/v1/auth/twitch/callback`, GET + POST) remains for redirect-based
   flows and integration OAuth.
3. Tokens are AES-encrypted at rest. JWT sent as `Authorization: Bearer <token>`; refresh via
   `POST /api/v1/auth/refresh`. Native clients keep tokens in the OS keychain; the **web build keeps the
   refresh token in an HttpOnly+Secure cookie — never localStorage**.
4. **Progressive scopes** — enabling a feature that needs new scopes triggers the action-required flow
   (chat + dashboard prompt → one-click additive re-grant). Never force a logout for a scope change.

### Running the Backend

Commands are identical on every OS (run from the repo root):

```bash
# Optional — only for the full profile; plain `dotnet run` is self_host_lite on SQLite
docker compose up -d postgres redis adminer

# Run API locally (auto-migrates, auto-seeds on first start)
cd server/src/NomNomzBot.Api
dotnet run
```

On first start the API:
1. Waits for Postgres and Redis to be reachable
2. Runs all EF Core migrations
3. Seeds reference data (TTS voices, permission presets)
4. Starts Twitch EventSub WebSocket

Local dev URLs:
- `http://localhost:5080` — API
- `http://localhost:5080/scalar` — Interactive docs
- `http://localhost:5080/health` — Health check (JSON)
- `http://localhost:8082` — Adminer (DB browser)

### Running Tests

```bash
cd server
dotnet test                                    # all projects
dotnet test tests/NomNomzBot.Domain.Tests      # one project
```

---

## Frontend Architecture

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
  CDN-cached for SaaS; **not** static files (there is no `web/` folder). The compiled-widget build→serve→cache
  pipeline is being specced.

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
./gradlew :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t   # web dev server (hot reload)
./gradlew :composeApp:run                                         # desktop (dev)
./gradlew :composeApp:wasmJsBrowserDistribution                    # prod web bundle (use --rerun-tasks for a clean prod build)
./gradlew :composeApp:packageDistributionForCurrentOS              # desktop installer (MSI/DMG/DEB)
```

The prod web bundle is bundled automatically into the API publish and Docker image; the deploy
script's `--app` flag wraps the installer task (see `DEPLOY.md`).

---

## Twitch Integration

### OAuth Flow

1. Backend redirects user to `https://id.twitch.tv/oauth2/authorize` with required scopes
2. Twitch calls back to `/api/v1/auth/twitch/callback`
3. API exchanges the code, routes the result by `state` (`user`, `bot`, or `channel_bot`), stores encrypted tokens (`ENCRYPTION_KEY`), and returns JWTs or success redirects

**Redirect URIs are computed at runtime from `App:BaseUrl`** — do not set them in config or env vars. All Twitch OAuth flows now share one callback path:
- `{App:BaseUrl}/api/v1/auth/twitch/callback`

Register only that single callback URL in the Twitch Developer Console using your actual API base URL (e.g. `https://bot-dev-api.nomercy.tv` for dev, `https://api.nomnomz.bot` for prod).

**Progressive scopes** — don't request everything up front. Request scopes when the user enables the relevant feature (e.g., `channel:manage:raids` when they enable raid responses).

### Streamer Account Scopes

```
user:read:email
channel:read:subscriptions
channel:read:redemptions
channel:manage:redemptions
channel:manage:raids
channel:manage:broadcast
channel:manage:polls
channel:manage:predictions
moderator:read:followers
moderator:manage:banned_users
moderator:manage:chat_messages
moderator:manage:automod
bits:read
```

### Bot Account Scopes

```
user:write:chat
user:read:chat
chat:read
chat:edit
```

### EventSub (WebSocket — not webhooks)

The bot uses `wss://eventsub.wss.twitch.tv/ws` — **no public HTTPS URL required** during local dev.

- `TwitchEventSubService` runs as `IHostedService`
- Manages WebSocket lifecycle automatically
- Reconnects with exponential backoff on disconnect
- Re-registers all subscriptions after reconnect
- Twitch sends a `reconnect` message every ~5 minutes (normal behavior, not a bug)

**Key EventSub topics subscribed:**
- `stream.online` / `stream.offline`
- `channel.follow`
- `channel.subscribe` / `channel.subscription.gift`
- `channel.cheer`
- `channel.raid`
- `channel.channel_points_custom_reward_redemption.add`
- `channel.poll.begin` / `channel.poll.end`
- `channel.prediction.begin` / `channel.prediction.end`
- `channel.chat.message` (requires bot `user:read:chat` scope)

### Chat Send & Read (Helix everywhere — IRC retired)

- Chat **send** via `IChatProvider` → `HelixChatProvider`: **Helix Send Chat Message** (`POST /helix/chat/messages`, `user:write:chat`) on **every** profile — stateless, no per-channel socket, no sharding. Whispers via `POST /helix/whispers`.
- Chat **read** via EventSub `channel.chat.message` (bot `user:read:chat` scope) on every profile (`spec/scaling-qos.md` §6).
- IRC is **fully retired** — there is no `TwitchIrcService` and no TLS IRC socket; no chat flows over IRC on any profile (decision: Helix everywhere). The bot **types** via Helix Send Chat Message on its own token (`user:write:chat`) and **reads** via EventSub (`user:read:chat`); the bot OAuth also still requests the legacy IRC `chat:read`/`chat:edit` scopes (see Bot Account Scopes), which the Helix path does not exercise.
- **Note:** If `ENCRYPTION_KEY` changes, the stored bot token becomes unreadable — the bot needs to re-auth.

### Cloudflare Tunnel (for OAuth redirects)

Twitch requires HTTPS redirect URIs. For local dev:

```bash
cloudflared tunnel --url http://localhost:5080
```

Then update `App__BaseUrl` in `appsettings.Development.json` and add the tunnel URL to your Twitch app's redirect URIs.

A shared dev tunnel at `bot-dev-api.nomercy.tv` is pre-configured in `appsettings.Development.json`. This is the **active domain** — `api.nomnomz.bot` is the planned production domain and will replace it once fully configured.

---

## Environment Variables

### Backend — `nomnomzbot/.env`

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `POSTGRES_USER` | no | `nomnomzbot` | PostgreSQL username |
| `POSTGRES_PASSWORD` | prod: yes | `nomnomzbot_dev` | PostgreSQL password |
| `JWT_SECRET` | prod: yes | `dev-secret-key-at-least-32-characters-long!!` | JWT signing key (≥32 chars). Generate: `openssl rand -base64 32` |
| `JWT_ISSUER` | no | `nomnomzbot` | JWT issuer claim |
| `JWT_AUDIENCE` | no | `nomnomzbot` | JWT audience claim |
| `ENCRYPTION_KEY` | prod: yes | `ZGV2...` (base64) | AES key for OAuth token storage. Generate: `openssl rand -base64 32`. **Changing this invalidates all stored tokens.** |
| `TWITCH_CLIENT_ID` | **yes** | — | From Twitch Developer Console |
| `TWITCH_CLIENT_SECRET` | **yes** | — | From Twitch Developer Console |
| `TWITCH_BOT_USERNAME` | **yes** | — | Twitch username of the bot account |
| `REDIS_CONNECTION_STRING` | no | `redis:6379` | Redis (uses Docker service name inside stack) |
| `SPOTIFY_CLIENT_ID` | no | — | Enables Spotify music integration |
| `SPOTIFY_CLIENT_SECRET` | no | — | Enables Spotify music integration |
| `DISCORD_CLIENT_ID` | no | — | Enables Discord integration |
| `DISCORD_CLIENT_SECRET` | no | — | Enables Discord integration |
| `YOUTUBE_CLIENT_ID` | no | — | Enables YouTube music provider |
| `YOUTUBE_CLIENT_SECRET` | no | — | Enables YouTube music provider |
| `AZURE_TTS_API_KEY` | no | — | Azure Cognitive Services key for TTS |
| `AZURE_TTS_REGION` | no | `westeurope` | Azure region for TTS service |
| `ELEVENLABS_API_KEY` | no | — | ElevenLabs key for TTS |
| `API_HTTP_PORT` | no | `5080` | Host port for API container |
| `API_HTTPS_PORT` | no | `5081` | Host HTTPS port |
| `POSTGRES_PORT` | no | `5432` | Host port for Postgres |
| `REDIS_PORT` | no | `6379` | Host port for Redis |
| `ADMINER_PORT` | no | `8082` | Host port for Adminer |

For local `dotnet run` dev (not Docker): put Twitch credentials in `appsettings.Development.json` instead. All other settings fall back to `appsettings.json` defaults.

### Frontend

The KMP + Compose dashboard is **profile-agnostic** — its only required configuration is the
**backend URL**. The web build talks to the origin that served it (no picker); the native app keeps
a list of saved server connections (mDNS LAN discovery + manual add) with per-server tokens in the
OS keychain, switchable from the profile menu.

### `appsettings.json` structure (config hierarchy)

```json
{
  "ConnectionStrings": { "DefaultConnection": "...", "Redis": "..." },
  "Jwt": { "Secret": "", "Issuer": "nomnomzbot", "Audience": "nomnomzbot", "ExpiryMinutes": 60 },
  "Encryption": { "Key": "" },
  "Twitch": { "ClientId": "", "ClientSecret": "", "BotUsername": "" },
  "Spotify": { "ClientId": "", "ClientSecret": "" },
  "Discord": { "ClientId": "", "ClientSecret": "" },
  "YouTube": { "ClientId": "", "ClientSecret": "" },
  "Azure": { "Tts": { "ApiKey": "", "Region": "westeurope" } },
  "ElevenLabs": { "ApiKey": "" },
  "Cors": { "Origins": ["https://bot-dev.nomercy.tv"] }
}
```

---

## Common Tasks

### Adding a New API Endpoint

1. Define the service interface in `NomNomzBot.Application/<Module>/Services/`
2. Implement it in `NomNomzBot.Infrastructure/<Module>/`
3. Register it in `NomNomzBot.Infrastructure/DependencyInjection.cs`
4. Create controller in `NomNomzBot.Api/Controllers/V1/` with `[ApiVersion("1.0")]` and `[Route("api/v{version:apiVersion}/...")]`
5. Return `StatusResponseDto<T>` or `PaginatedResponse<T>`

### Adding a New Dashboard Page

1. Screen + state under `app/composeApp/src/commonMain/.../feature/<domain>/`; navigation and page
   placement per `frontend-ia.md` (the definitive page inventory).
2. Fetch/mutate exclusively through the **typed shared KMP client** (`core/network`, REST + SignalR) —
   never call the API ad hoc. New DTOs register in `ApiContractTest`; refresh `server/openapi/v1.json`
   on any contract change.
3. Design-system components only (`frontend-design-system.md` + catalogue) — no raw hex/`dp`.
4. i18n keys for both `en` and `nl`; never hardcode user-facing strings.
5. Role-gate per `frontend-ia.md` §7 — hide pages below the read floor; **disable** (don't hide)
   actions below the manage floor, with a reason tooltip.

### Adding a New Twitch EventSub Subscription

Per the `twitch-eventsub.md` spec: add the topic to the subscription catalogue and write a
translator beside the 74 existing ones in `NomNomzBot.Infrastructure/Platform/Eventing/` —
`TwitchEventSubHostedService` re-registers the full set on every (re)connect, and the translator
turns the wire payload into a domain event on the bus.

### Adding a New Integration (OAuth pattern)

1. Add `{Provider}Controller` in Api with `OAuth`, `Callback`, `Disconnect` actions
2. Add `I{Provider}Service` interface in Application
3. Implement `{Provider}Service` in Infrastructure
4. Add `{Provider}:ClientId/ClientSecret` to `appsettings.json` and `.env.example`
5. Surface the integration in the dashboard's Integrations screen (`feature/integrations`); gate the feature in the frontend on the integration's connection state (placement per `frontend-ia.md`).

### Adding a New Pipeline Action

1. Create the action implementing `ICommandAction` in `NomNomzBot.Infrastructure/Platform/Pipeline/CoreActions/` (core) or `NomNomzBot.Infrastructure/<Module>/PipelineActions/` (side-effecting)
2. Set `Type` property to a unique snake_case string
3. Register in `NomNomzBot.Infrastructure/DependencyInjection.cs`
4. Add the contract/DTO to `NomNomzBot.Application/Abstractions/Pipeline/`
5. Surface the action in the dashboard's pipeline builder block palette (`feature/pipelines`).

---

## Pipeline Engine

Commands and event responses use a visual pipeline system. Each pipeline is a list of **actions** with optional **conditions**.

**Built-in actions:** `SendMessage`, `SendReply`, `Timeout`, `Ban`, `Shoutout`, `SetVariable`, `Wait`, `PlayMusic`, `Stop`, and more.

**Conditions:** `UserRole` (broadcaster/mod/sub/vip/everyone), `Random` (percentage), variable comparisons.

**Template variables** (90+): `{{user.name}}`, `{{channel.title}}`, `{{stream.uptime}}`, `{{args.1}}`, `{{random.number:1:100}}`, etc.

All action blocks are compiled C# classes — no scripting engine.

---

## Design System

- **Source of truth:** shadcn/ui (new-york), ported 1:1 to Compose — full spec in `.claude/docs/design/spec/frontend-design-system.md`.
- **Tokens:** shadcn's closed **OKLCH** contract (Tailwind v4), **neutral base**; the accent is **dynamic — derived at runtime from the signed-in user's Twitch chat color** (subtle, light + dark).
- **Components:** a closed catalogue, variants-as-data, each on the most-correct primitive (Material3-wrapped or Compose Foundation). **Icons:** the designer's pack (`IconKey`/`IconSet`), Line style, Lucide fallback.
- **Enforcement:** a detekt linter bans raw hex/`dp`, off-catalogue components, and hardcoded strings.
- The old Figma (`MkKBuW2Ee6T5jC8fCtZsM0`) is discarded; HTML mockups at `nomnomzbot-design/mockups/` are a loose historical reference only.

---

## Known Issues / Current State (as of 2026-07-04)

| Issue | Notes |
|-------|-------|
| EventSub reconnects every ~5 min | Normal Twitch behavior — server sends a `reconnect` message |
| Bot token invalid after key change | `ENCRYPTION_KEY` rotation requires bot re-auth |
| Application test suite rare flake | ~5% intermittent failure; a lone red that won't reproduce locally → re-run once before digging |

---

## Useful Local Dev URLs

| URL | Description |
|-----|-------------|
| `http://localhost:5080/scalar` | Interactive API docs (Scalar UI) |
| `http://localhost:5080/health` | Full health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe |
| `http://localhost:8082` | Adminer — Postgres browser |

---

## First-Time Setup Wizard

The app detects when no streamer account is configured and routes to the setup wizard. The wizard:

1. **Connect Twitch account** — OAuth with initial streamer scopes
2. **Connect bot account** — separate OAuth for the bot identity
3. **Configure basics** — bot prefix, default language, timezone
4. **Enable integrations** — Spotify, Discord, etc. (can skip and do later from Settings)

After completion, lands on the dashboard home. The wizard is implemented in the dashboard
(device-code onboarding); returning users get quick login or a remembered-session restore —
never a repeat of the device-code dance.

---

## Git Conventions

- No `Co-Authored-By` in commits — ever
- Conventional commit messages preferred (`feat:`, `fix:`, `chore:`, etc.)
- Main branch: `master` (never `main`)
- Remotes: `origin` = `NoMercyLabs/nomnomzbot` (canonical, push here); `fork` = personal `StoneyEagle/nomnomzbot`
- Feature branches: `feat/description` or `fix/description`
- All code lives in this monorepo (`server/` backend, `app/` KMP + Compose frontend)
- Backend and frontend are separate tracks — commits never mix `server/` and `app/` files, and neither track rewrites the other's history (see *Team & Track Ownership*)
- Tests pass before every commit; every push is followed by `gh run watch <run-id> --exit-status` (see *CI Gate*)
