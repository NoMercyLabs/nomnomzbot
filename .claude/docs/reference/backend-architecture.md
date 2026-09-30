# Backend Architecture

### Clean Architecture Layers

Dependencies flow **inward only**. Domain knows nothing about the outside world.

```
NomNomzBot.Api             → Controllers, Hubs, Middleware, JWT, SignalR
NomNomzBot.Infrastructure  → EF Core, Twitch/Spotify/Discord/TTS services, pipeline engine (Platform/Pipeline), event bus (Platform/Eventing/EventBus.cs), EventSub
NomNomzBot.Application     → Use cases, service interfaces, pipeline and event-bus interfaces (IPipelineEngine, ICommandAction, IEventBus)
NomNomzBot.Domain          → Entities, domain events, value objects, no external deps
```

### Key Design Decisions

- **No MediatR** — services are injected via typed interfaces (`IAuthService`, `ITwitchApiService`, etc.) and called directly. This keeps the call stack shallow and obvious.
- **`Result<T>` pattern** — operations that can fail return `Result<T>` instead of throwing. Never return null; always return a result with a `Success` flag and optional error.
- **Soft deletes** — entities use `IsDeleted` + EF Core global query filters. Never `DELETE` from the database.
- **Multi-tenancy** — `TenantResolutionMiddleware` resolves the tenant per request from an explicit channel target (route `{channelId}` → `X-Channel-Id` header → `channelId` query), falling back to the caller's own channel (JWT `sub`) only when none is given — so an operator can act on any channel they moderate, not just their own. Gate-1 entry (`CanResolveTenantAsync`) admits any authenticated caller to any existing channel; per-action `[RequireAction]` (Gate-2) enforces their role there. A global EF query filter scopes every read to the resolved tenant.
- **Nullable reference types** — enabled everywhere (`<Nullable>enable</Nullable>`).
- **Implicit usings** — src projects use `<ImplicitUsings>enable</ImplicitUsings>` (`Directory.Build.props`).
- **Async all the way** — never `.Result` or `.Wait()`.
- **Repository + IUnitOfWork** — no raw `DbContext` in controllers.

### Key Services

| Service | Location | Purpose |
|---------|----------|---------|
| `AuthService` | Infrastructure | JWT creation, Twitch token exchange (device code + refresh) |
| `ITwitchHelixClient` | Application contract, Infrastructure impl | Typed Helix client — 26 sub-clients covering the full Helix surface |
| `TwitchEventSubHostedService` | Infrastructure (`Platform/Eventing`) | EventSub lifecycle over `WebSocketEventSubTransport`; 74 event translators |
| `IChatProvider` = `ChatPlatformRouter` | Infrastructure | Dispatches to per-platform `IChatPlatform` implementations (`HelixChatProvider` for Twitch, `KickChatPlatform`, YouTube live chat); also implements `IInboundOriginChatSender` (replies go to the message's origin platform) |
| `MusicService` + `SpotifyMusicProvider` | Infrastructure (`Music/`) | Now playing, queue, playback control (provider-backed) |
| `DiscordGuildService` / `DiscordNotificationConfigService` / `DiscordGuildDirectoryService` / `DiscordNotificationRoleService` | Infrastructure (`Discord/`) | Guild sync, notification config, guild directory, role buttons |
| `TtsService` | Infrastructure | Azure Cognitive Services + ElevenLabs provider |
| `PipelineEngine` | Infrastructure | Executes pipeline action chains |

### Controllers (all under `/api/v1/`, source in `NomNomzBot.Api/Controllers/V1/`)

**About 110 controllers (`Controllers/V1` plus host/relay controllers); do not rely on a count — read the folder.** Browse them at
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
- Errors use the `StatusResponseDto` error envelope `{status:"error", message, code, args}`; `GlobalExceptionMiddleware` writes the same envelope for 500s; only the tenant-suspended 403 in `TenantResolutionMiddleware` is RFC 7807 `problem+json`.
- Interactive API docs: `http://localhost:5080/scalar`

### SignalR Hubs

| Hub | Path | Purpose |
|-----|------|---------|
| `DashboardHub` | `/hubs/dashboard` | Real-time dashboard updates (chat feed, stats, alerts) |
| `OverlayHub` | `/hubs/overlay` | Browser-source overlays (alerts, now-playing widgets) |
| `OBSRelayHub` | `/hubs/obs` | OBS WebSocket relay |
| `AdminHub` | `/hubs/admin` | Platform admin operations |

For the full event list, see `Api/Hubs/Clients/IDashboardClient.cs`.

The frontend connects through the shared KMP SignalR client. Auth token passed as `?access_token=<jwt>`.

### Authentication Flow

1. **Login is provider-generic (D2):** `auth/{provider}/device[/poll]` via `ILoginProviderRegistry`, and
   `auth/{provider}/authorize` + `/callback` for code-flow providers. Twitch device code (secret-free,
   approve on twitch.tv/activate) is the `provider=twitch` case. The per-channel bot login is
   `auth/twitch/channels/{channelId}/bot/device` (+ poll). Shared public client by default, BYOC encouraged.
2. The authorization-code callback (`/api/v1/auth/twitch/callback`, GET + POST) remains for redirect-based
   flows and integration OAuth.
3. Tokens are AES-encrypted at rest. JWT sent as `Authorization: Bearer <token>`; refresh via
   `POST /api/v1/auth/refresh`. Native clients keep tokens in the OS keychain; the **web build keeps the
   refresh token in an HttpOnly+Secure cookie — never localStorage**.
4. **Progressive scopes** — enabling a feature that needs new scopes triggers the action-required flow
   (chat + dashboard prompt → one-click additive re-grant). Never force a logout for a scope change.

**Impersonation (act-as)** is a full identity swap.
- The web build holds the act-as token in the httpOnly `nnz_act_as` cookie; native clients send `actAsToken`.
- `POST /auth/refresh` re-mints the impersonated user for the life of the support session.
- `POST /auth/impersonation/exit` ends it and returns the operator session.

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
1. Resolves the deployment profile; Development with no Postgres/Redis reachable → `SelfHostLite` on SQLite (the appsettings Postgres connection is unused in that mode)
2. Runs all EF Core migrations
3. Seeds reference data (TTS voices, permission presets)
4. Starts Twitch EventSub WebSocket

Local dev URLs — the API always runs on its committed, documented default (`5080`), whether or not
the dashboard dev server is also running:
- `http://localhost:5080` — API
- `http://localhost:5080/scalar` — Interactive docs
- `http://localhost:5080/health` — Health check (JSON)
- `http://localhost:5080/health/version` — running build/commit
- `http://localhost:8082` — Adminer (DB browser)

The dashboard dev server (`wasmJsBrowserDevelopmentRun`) listens on its own port, `5090`
(`build.gradle.kts`), so it never collides with the API's `5080`; its dev-only webpack proxy
(`webpack.config.d/proxy.js`) targets `http://localhost:5080` when an API is actually listening there,
and otherwise falls back to the deployed dev backend so a frontend-only dev never needs to run `dotnet`
(override with `NNZ_DEV_BACKEND`; the chosen target is printed at startup). The two documented commands (`dotnet run` + `wasmJsBrowserDevelopmentRun`) run together with
**zero flags and zero env vars** on a fresh clone. See *Running the Frontend* below.

**OAuth redirect URI for local dev — always `http://localhost:5080/api/v1/auth/twitch/callback`.**
`ResolvePublicOrigin` picks the public origin by this precedence: (1) an explicit non-loopback
`App:BaseUrl` wins outright; (2) `X-Forwarded-Host`/`X-Forwarded-Proto`; (3) the loopback default.
The local two-port result falls out of rule 2: the dev proxy forwards no headers, so the API reports its
own origin (`5080`), not the dashboard dev-server port (`5090`). The redirect must byte-match what is
registered in the Twitch Developer Console, and the owner registers `5080`. Real reverse-proxy
deployments (Cloudflare Tunnel, Proxmox) set `App:BaseUrl` or forward the headers, so the forwarded
origin is the one used there.

### Deploy Topology

Caddy owns host port `5080`; `api-blue` / `api-green` publish nothing. `scripts/switchover.ps1` is the
zero-downtime path.

### Migrations (two sets)

Postgres migrations live in `Infrastructure/Platform/Persistence/Migrations`; SQLite migrations live in
`NomNomzBot.Migrations.Sqlite`. A schema change needs both. Prove it with `scripts/migration-check.ps1`.

### Docs Exposure

`/scalar` is served only in Development or when `Api:ExposeDocs=true`; the docker default is `false`.

### Running Tests

```bash
cd server
dotnet test                                    # all projects
dotnet test tests/NomNomzBot.Domain.Tests      # one project
```
