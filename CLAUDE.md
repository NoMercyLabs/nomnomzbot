# NomNomzBot — AI Assistant Context

An open-source, multi-tenant Twitch bot platform. One deployment supports unlimited channels — each
streamer gets a full isolated dashboard, pipeline editor, custom commands, event responses, timers,
overlays, and integrations (Spotify, Discord, YouTube, TTS).

Licensed under **AGPL-3.0**. Copyright (C) NoMercy Entertainment.

---

## Critical Rules — Read First

- **Namespace is `NomNomzBot.*`** — never write `NoMercyBot.*` (capital M). This is always wrong.
- **Username is `Stoney_Eagle`** — underscore, not hyphen. Never change this.
- **Figma is the source of truth for design** — file key `MkKBuW2Ee6T5jC8fCtZsM0`. When in doubt, Figma wins over HTML mockups.
- **HTML mockups at `nomnomzbot-design/mockups/`** are a reference implementation of the Figma designs.
- **No `Co-Authored-By` in git commits** — ever.
- **No MediatR** — services are called directly via typed interfaces registered in DI.
- **No Roslyn** — don't use Roslyn for code generation or analysis.
- **Don't ask permission to fix bugs** — find it, fix it, move on.
- **Don't run `npm install --legacy-peer-deps`** without researching compatibility first. The flag silently masks conflicts and can nuke working deps.
- **No fake/seed data for community** — all community/viewer data must come from the real Twitch API. Never fabricate viewer lists, subscriber counts, etc.
- **Don't ask "should I continue?" or "want me to fix this?"** — just do it.
- **Match Figma exactly** — pixel-perfect components, correct colors, correct spacing. Do not approximate.
- **Test every interactive element** — never claim something "works" without full validation.
- **No half-assed work** — seed ALL data, test EVERY button, run parallel where possible.

---

## Repository Layout

```
NoMercyLabs/
├── nomnomzbot-server/       # Backend — .NET 10, PostgreSQL, Redis
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
│   ├── docker-compose.yml
│   ├── .env                 # Created from .env.example; not committed
│   └── .env.example
├── nomnomzbot-app/          # Frontend — Expo (React Native), web + iOS + Android
│   ├── app/                 # Expo Router file-based routes
│   ├── features/            # Feature modules with co-located business logic
│   ├── components/          # Shared UI components
│   ├── hooks/               # Shared hooks (useBreakpoint, useSignalR, etc.)
│   ├── stores/              # Zustand stores
│   ├── lib/                 # HTTP client, utilities
│   ├── .env.development
│   └── .env.production
└── nomnomzbot-design/       # HTML mockups, research docs, architecture specs
    ├── mockups/             # HTML reference implementations of Figma designs
    └── research/            # Architecture decisions, API research, design system docs
```

All three directories are git submodules inside the `NoMercyLabs` monorepo.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend runtime | .NET 10, C# 13 |
| Backend framework | ASP.NET Core with Asp.Versioning.Mvc |
| ORM | EF Core 9 + Npgsql (PostgreSQL 16) |
| Cache / pub-sub | Redis 7 |
| Real-time | ASP.NET SignalR (WebSocket) |
| Auth | JWT + Twitch OAuth (Authorization Code Flow) |
| Logging | Serilog |
| Frontend framework | Expo SDK 55 / React Native 0.83 / React 19 |
| Frontend routing | expo-router 5 (file-based, typed routes) |
| Styling | NativeWind 5 (Tailwind 4 for React Native) + inline styles |
| State | Zustand 5 |
| Server state | TanStack Query 5 |
| HTTP client | axios 1 |
| Forms | react-hook-form 7 + zod 3 |
| i18n | i18next 24 + react-i18next 15 |
| Icons | lucide-react-native |
| Real-time (FE) | @microsoft/signalr 8 |

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
| `AuthService` | Infrastructure | JWT creation, Twitch token exchange, refresh |
| `TwitchApiService` | Infrastructure | Helix API calls (channel info, followers, bans, etc.) |
| `TwitchEventSubService` | Infrastructure | WebSocket EventSub lifecycle (`IHostedService`) |
| `TwitchIrcService` | Infrastructure | IRC bot connection (TLS, reconnect, message send) |
| `SpotifyService` | Infrastructure | Now playing, queue, playback control |
| `DiscordService` | Infrastructure | Guild sync, notifications |
| `TtsService` | Infrastructure | Azure Cognitive Services + ElevenLabs provider |
| `PipelineEngine` | Infrastructure | Executes pipeline action chains |

### Controllers (all under `/api/v1/`)

- `AuthController` — Twitch OAuth login/callback, JWT refresh, logout
- `ChannelBotController` — Bot account OAuth, bot config
- `ChannelsController` — Channel CRUD, stream info, bot callback
- `ChatController` — Chat messages (read-only; send via IRC bot)
- `CommandsController` — Custom command CRUD, pipeline attachment
- `CommunityController` — Viewer list from Twitch API (no seed data)
- `DashboardController` — Stats aggregation for dashboard widgets
- `IntegrationOAuthController` — Spotify, Discord, YouTube OAuth flows
- `ModerationController` — Bans, timeouts, automod settings
- `RewardsController` — Channel point rewards
- `StreamController` — Stream title/game/tags updates
- `SystemController` — System health, setup wizard status, TTS voices
- `TtsController` — TTS config, preview, queue
- `TimersController` — Scheduled message timers
- `PipelinesController` — Pipeline CRUD + execution

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

Frontend connects via `@microsoft/signalr`. Auth token passed as `?access_token=<jwt>`.

### Authentication Flow

1. `/api/v1/auth/twitch/login` → redirected to Twitch OAuth
2. Twitch calls `/api/v1/auth/twitch/callback` with code
3. API exchanges code for tokens, stores AES-encrypted tokens in Postgres, returns JWT
4. Frontend stores JWT in `expo-secure-store`, sends as `Authorization: Bearer <token>`
5. Progressive scopes — additional permissions requested only when the user enables a feature

### Running the Backend

```bash
# Start infrastructure (first time and after docker restart)
cd nomnomzbot
docker-compose up -d postgres redis adminer

# Run API locally (auto-migrates, auto-seeds on first start)
cd src/NomNomzBot.Api
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
cd nomnomzbot
dotnet test                                    # all projects
dotnet test tests/NomNomzBot.Domain.Tests      # one project
```

---

## Frontend Architecture

### Routing (Expo Router — file-based)

```
app/
├── _layout.tsx              # Root: providers, fonts, global CSS
├── (auth)/                  # No sidebar/tabs
│   ├── login.tsx            # Twitch OAuth entry
│   ├── callback.tsx         # OAuth callback handler
│   └── onboarding.tsx       # Setup wizard
├── (dashboard)/             # Sidebar on web/tablet, bottom tabs on mobile
│   ├── index.tsx            # Dashboard home
│   ├── commands/            # Command list + editor
│   ├── rewards/             # Channel point rewards + leaderboard
│   ├── chat/                # Live chat view + settings
│   ├── moderation/          # Automod, bans, log
│   ├── music/               # Now playing, queue, provider settings
│   ├── widgets/             # Overlay widget editor
│   ├── stream/              # Stream title / game / tags
│   ├── community/           # Viewer list (real Twitch data only)
│   ├── pipelines/           # Pipeline list + editor
│   ├── integrations/        # Connected services
│   ├── permissions/         # Role & permission management
│   ├── settings/            # General settings, TTS, danger zone
│   └── billing/             # Subscription & billing
├── (public)/                # No auth required
│   └── sr/[channel].tsx     # Song request page
└── (admin)/                 # Platform admin
    ├── channels.tsx
    ├── users.tsx
    └── system.tsx
```

### State Management (Zustand 5)

| Store | File | Purpose |
|-------|------|---------|
| `useAuthStore` | `stores/authStore.ts` | JWT token, user profile, auth state |
| `useChannelStore` | `stores/channelStore.ts` | Active channel data, stream info |
| `useAppStore` | `stores/appStore.ts` | Global UI state (sidebar open, theme, locale) |

Stores use `persist` middleware. Native: `expo-secure-store`. Web: `localStorage`.

### Responsive Layout

| Breakpoint | Value | Layout |
|-----------|-------|--------|
| phone | `< 768px` | Bottom tabs navigation |
| tablet | `768px–1023px` | Collapsible sidebar |
| desktop | `≥ 1024px` | Fixed sidebar |

Use the `useBreakpoint()` hook — never hardcode pixel checks.

### Styling

- **NativeWind 5** (Tailwind 4 CSS-first config) for utility classes
- **Inline styles** for dynamic/computed values
- **Never** use `StyleSheet.create` for new components — NativeWind className is preferred
- Design token colors are in `tailwind.config.ts`; Figma's design system maps directly

### Web vs Native Differences

| Feature | Web | Mobile |
|---------|-----|--------|
| Layout | Sidebar | Bottom tabs |
| Pipeline builder | Full @dnd-kit drag-drop canvas | View-only step list |
| Code editor | Monaco Editor | Hidden |
| Auth flow | Redirect-based OAuth | `expo-web-browser` popup |
| Storage | `localStorage` | `expo-secure-store` |
| Critical buttons | `div` + `onClick` (Pressable unreliable on web) | `Pressable` or `TouchableOpacity` |

### i18n

- `i18next` with lazy-loaded namespaces
- Supported languages: English (`en`), Dutch (`nl`)
- Translation files: `locales/en/`, `locales/nl/`
- Always use `t('key')` — never hardcode English strings in components

### Running the Frontend

```bash
cd nomnomzbot-app
yarn install          # first time only
yarn web              # opens http://localhost:8081
yarn ios              # requires Xcode (macOS only)
yarn android          # requires Android Studio
yarn typecheck        # TypeScript check (no emit)
yarn lint             # ESLint
yarn test             # Jest
```

Set `EXPO_PUBLIC_API_URL=http://localhost:5080` in `.env.development` first.

---

## Twitch Integration

### OAuth Flow

1. Backend redirects user to `https://id.twitch.tv/oauth2/authorize` with required scopes
2. Twitch calls back to `/api/v1/auth/twitch/callback`
3. API exchanges code, stores encrypted tokens (`ENCRYPTION_KEY`), returns JWT to frontend
4. Separate bot account OAuth: `/api/v1/auth/twitch/bot/callback`

**Redirect URIs to register in Twitch Developer Console:**
```
http://localhost:5080/api/v1/auth/twitch/callback
http://localhost:5080/api/v1/auth/twitch/bot/callback
http://localhost:5080/api/v1/channels/callback/bot
```
Replace `localhost:5080` with your tunnel/production URL as needed.

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

### IRC Bot

- Bot connects via TLS to `irc.chat.twitch.tv:6697`
- Uses bot account OAuth token (`chat:read` + `chat:edit`)
- `TwitchIrcService` runs as `IHostedService`
- All chat messages sent through IRC (not Helix API)
- **Note:** If `ENCRYPTION_KEY` changes, stored bot token becomes unreadable — bot needs to re-auth

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

### Frontend — `nomnomzbot-app/.env.development`

| Variable | Required | Description |
|----------|----------|-------------|
| `EXPO_PUBLIC_API_URL` | **yes** | Backend URL. Use `http://localhost:5080` for local dev |
| `EXPO_PUBLIC_PROJECT_ID` | native builds only | Expo project ID for EAS builds + push notifications. Not needed for `yarn web` |
| `EXPO_PUBLIC_TWITCH_CLIENT_ID` | no | Twitch client ID (used in `app.config.ts` extras) |

### `appsettings.json` structure (config hierarchy)

```json
{
  "ConnectionStrings": { "DefaultConnection": "...", "Redis": "..." },
  "Jwt": { "Secret": "", "Issuer": "nomnomzbot", "Audience": "nomnomzbot", "ExpiryMinutes": 60 },
  "Encryption": { "Key": "" },
  "Twitch": { "ClientId": "", "ClientSecret": "", "BotUsername": "", "RedirectUri": "", "BotRedirectUri": "", "ChannelBotRedirectUri": "" },
  "Spotify": { "ClientId": "", "ClientSecret": "" },
  "Discord": { "ClientId": "", "ClientSecret": "" },
  "YouTube": { "ClientId": "", "ClientSecret": "" },
  "Azure": { "Tts": { "ApiKey": "", "Region": "westeurope" } },
  "ElevenLabs": { "ApiKey": "" },
  "Cors": { "Origins": ["http://localhost:8081", "http://localhost:19006", "https://bot-dev.nomercy.tv"] }
}
```

---

## Common Tasks

### Adding a New API Endpoint

1. Define interface in `NomNomzBot.Application/Common/Interfaces/`
2. Implement in `NomNomzBot.Infrastructure/Services/`
3. Register in `InfrastructureServiceExtensions.cs`
4. Create controller in `NomNomzBot.Api/Controllers/` with `[ApiVersion("1.0")]` and `[Route("api/v{version:apiVersion}/...")]`
5. Return `StatusResponseDto<T>` or `PaginatedResponse<T>`

### Adding a New Dashboard Page

1. Create `app/(dashboard)/your-feature/index.tsx`
2. Add to sidebar nav in `(dashboard)/_layout.tsx`
3. Add i18n key in `locales/en/` and `locales/nl/`
4. Use `useBreakpoint()` for responsive layout; sidebar on desktop/tablet, bottom tab on mobile
5. Use `@tanstack/react-query` for data fetching, Zustand for shared state

### Adding a New Twitch EventSub Subscription

1. Add event type to `TwitchEventTypes` enum in Domain
2. Add subscription in `TwitchEventSubService.RegisterSubscriptionsAsync()`
3. Add handler in `TwitchEventSubService.HandleMessageAsync()` switch
4. Fire domain event or call service method from handler

### Adding a New Integration (OAuth pattern)

1. Add `{Provider}Controller` in Api with `OAuth`, `Callback`, `Disconnect` actions
2. Add `I{Provider}Service` interface in Application
3. Implement `{Provider}Service` in Infrastructure
4. Add `{Provider}:ClientId/ClientSecret` to `appsettings.json` and `.env.example`
5. Add integration card to `app/(dashboard)/integrations/index.tsx`
6. Gate feature in frontend with integration connection state from Zustand

### Adding a New Pipeline Action

1. Create class in `NomNomzBot.Infrastructure/Pipeline/Actions/` implementing `ICommandAction`
2. Set `Type` property to a unique snake_case string
3. Register in `InfrastructureServiceExtensions`
4. Add DTO to `NomNomzBot.Application/Contracts/Pipeline/`
5. Add action card to pipeline builder UI in `app/(dashboard)/pipelines/`

---

## Pipeline Engine

Commands and event responses use a visual pipeline system. Each pipeline is a list of **actions** with optional **conditions**.

**Built-in actions:** `SendMessage`, `SendReply`, `Timeout`, `Ban`, `Shoutout`, `SetVariable`, `Wait`, `PlayMusic`, `Stop`, and more.

**Conditions:** `UserRole` (broadcaster/mod/sub/vip/everyone), `Random` (percentage), variable comparisons.

**Template variables** (90+): `{{user.name}}`, `{{channel.title}}`, `{{stream.uptime}}`, `{{args.1}}`, `{{random.number:1:100}}`, etc.

All action blocks are compiled C# classes — no scripting engine.

---

## Design System

- **Figma file key:** `MkKBuW2Ee6T5jC8fCtZsM0`
- Background colors: `#0a0b0f` (app bg), `#12131a` (card bg), `#1a1b24` (elevated)
- Accent: Twitch purple `#9146FF`
- Text: `#ffffff` (primary), `#a0a0b0` (muted)
- 56 components, 28 desktop pages, 12 modals documented in Figma
- HTML mockups at `nomnomzbot-design/mockups/` are reference implementations — cross-reference when Figma intent is unclear, but Figma takes precedence

---

## Known Issues / Current State (as of 2026-04-07)

| Issue | Notes |
|-------|-------|
| Chat messages 403 error | Bot token may need re-auth or `user:write:chat` scope re-requested |
| Subscriber count always 0 | Helix endpoint requires `channel:read:subscriptions` — check scope grant |
| No emote picker / FrankerFaceZ / BTTV | Not yet implemented |
| `Pressable` unreliable on web | Use `div` + `onClick` for critical interactive elements on web |
| Commands show 0 on existing installs | Channel join/registration flow may have skipped command seeding |
| EventSub reconnects every ~5 min | Normal Twitch behavior — server sends a `reconnect` message |
| IRC bot token invalid after key change | `ENCRYPTION_KEY` rotation requires bot re-auth |

---

## Useful Local Dev URLs

| URL | Description |
|-----|-------------|
| `http://localhost:5080/scalar` | Interactive API docs (Scalar UI) |
| `http://localhost:5080/health` | Full health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe |
| `http://localhost:8081` | Frontend web app (Expo) |
| `http://localhost:8082` | Adminer — Postgres browser |

---

## First-Time Setup Wizard

The app detects when no streamer account is configured and routes to `/setup` (or `(auth)/onboarding`). The wizard:

1. **Connect Twitch account** — OAuth with initial streamer scopes
2. **Connect bot account** — separate OAuth for the bot identity
3. **Configure basics** — bot prefix, default language, timezone
4. **Enable integrations** — Spotify, Discord, etc. (can skip and do later from Settings)

After completion, lands on `/` (dashboard home).

---

## Git Conventions

- No `Co-Authored-By` in commits — ever
- Conventional commit messages preferred (`feat:`, `fix:`, `chore:`, etc.)
- Main branch: `main`
- Feature branches: `feat/description` or `fix/description`
- Each submodule (`nomnomzbot`, `nomnomzbot-app`, `nomnomzbot-design`) has its own git history
