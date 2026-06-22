# NomNomzBot

> An open-source, **multi-tenant Twitch bot platform**. Run one deployment and serve unlimited
> channels — every streamer gets a fully isolated bot: custom commands, a visual pipeline engine,
> moderation, channel-point rewards, timers, an economy, text-to-speech, song requests, OBS
> overlays, and integrations (Spotify, Discord, YouTube).

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](server/LICENSE)
[![Backend: .NET 10](https://img.shields.io/badge/backend-.NET%2010-512BD4.svg)](#how-it-works)
[![Status: pre-release](https://img.shields.io/badge/status-pre--release-orange.svg)](#project-status)

NomNomzBot is **self-hostable from day one** — point it at your own server and you need zero external
infrastructure or oversight — and the same codebase powers an optional hosted (SaaS) offering. It speaks
to Twitch over Helix, EventSub (WebSocket), and IRC, and exposes a versioned REST API plus real-time
SignalR feeds.

---

## Table of contents

- [Project status](#project-status)
- [Features](#features)
- [How it works](#how-it-works)
- [Getting started (self-host)](#getting-started-self-host)
- [Using NomNomzBot](#using-nomnomzbot)
- [Configuration](#configuration)
- [Production deployment](#production-deployment)
- [Development](#development)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)

---

## Project status

Pre-1.0, under active development. Honest snapshot:

- **Backend (`server/`)** — the live, supported component. Built and covered by tests across identity/auth,
  the Twitch Helix + EventSub integration, moderation, channel-point rewards, the pipeline engine, timers,
  the economy, TTS, roles & permissions, webhooks, and the sandboxed custom-code action.
- **Public web pages (`web/`)** — the viewer **song-request page** and the **OBS overlay** browser source
  are built and served by the bot.
- **Dashboard (`app/`)** — a **Kotlin Multiplatform + Compose** client (one codebase for desktop and web)
  is **planned and not built yet**. Until it lands, the bot is configured through its REST API (interactive
  docs at `/scalar`).

It has not yet been validated in a long-running production deployment, so treat it as early software.

## Features

What a connected channel gets:

- **Commands** — custom and built-in chat commands, each backed by a **visual pipeline** of actions and
  conditions (send/reply, timeout/ban, shoutout, set-variable, wait, play music, and more), with 90+
  template variables (`{{user.name}}`, `{{stream.uptime}}`, `{{args.1}}`, …).
- **Event responses & timers** — automatic reactions to follows, subs, gifts, cheers, raids, and
  redemptions; scheduled recurring messages.
- **Moderation** — bans and timeouts, AutoMod settings, blocked-term lists, Shield Mode, and a mod-action
  log — all mirrored from the real Twitch API.
- **Channel points & live ops** — custom reward management, polls, and predictions.
- **Economy & games** — a per-channel currency with an atomic ledger, a store, cross-channel savings jars,
  leaderboards, and fun-money games (with an optional, off-by-default 18+ gate).
- **Music & song requests** — viewer song requests (Spotify / YouTube) with a fair, rank-based queue and
  trust scoring, plus a public request page that needs no login.
- **Text-to-speech** — Azure Cognitive Services and ElevenLabs voices.
- **Overlays & widgets** — OBS browser-source alerts and a now-playing bar, pushed in real time over
  SignalR.
- **Integrations** — Spotify, Discord, and YouTube via OAuth, requested progressively as you enable them.
- **Multi-tenant & roles** — one deployment, unlimited channels, each isolated; a three-plane authorization
  model (community standing, management roles, and per-action `!permit` delegation).
- **Extensibility** — inbound and outbound webhooks, and a hardened, sandboxed custom-code action.

## How it works

```
┌─ server/  — .NET 10 backend (the bot)
│   PostgreSQL + Redis · Twitch Helix + EventSub (WebSocket) + IRC · SignalR · Serilog
│   Versioned REST API (v1)  ·  interactive docs at /scalar
│
├─ web/     — lightweight public pages the bot serves
│   sr/       song-request page (viewers, token-based, no login)
│   overlay/  OBS browser-source overlays (alerts + now-playing) over SignalR
│
└─ app/     — Kotlin Multiplatform + Compose dashboard (planned; not built yet)
```

The backend is **profile-agnostic and direct-connect**: a client just needs the backend URL and talks
REST + SignalR straight to it. Self-host points at `localhost` or your own exposed URL; the hosted build
points at the SaaS API. There is no central broker.

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 10, C# 14 |
| Framework | ASP.NET Core (Asp.Versioning) |
| ORM | EF Core 10 + Npgsql → PostgreSQL 16 |
| Cache / pub-sub | Redis 7 |
| Real-time | ASP.NET SignalR (WebSocket) |
| Auth | JWT + Twitch OAuth (Authorization Code) |
| Twitch | Helix API, EventSub (WebSocket), IRC |
| Logging | Serilog |
| API docs | OpenAPI + Scalar |

## Getting started (self-host)

### Prerequisites

- **.NET 10 SDK**
- **Docker** (for PostgreSQL + Redis)
- A **Twitch account** for the bot, and a registered **Twitch application**

### 1. Register a Twitch application

Create an app in the [Twitch Developer Console](https://dev.twitch.tv/console/apps) and note its
**Client ID** and **Client Secret**. Redirect URIs are computed at runtime from `App:BaseUrl`, so register
exactly one callback:

```
{App:BaseUrl}/api/v1/auth/twitch/callback
```

For local development that is `http://localhost:5080/api/v1/auth/twitch/callback`. Twitch requires HTTPS
for real logins — see [Production deployment](#production-deployment) for a public HTTPS URL.

### 2. Configure

Put your Twitch credentials in `server/src/NomNomzBot.Api/appsettings.Development.json`:

```json
{
  "Twitch": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "BotUsername": "your_bot_account"
  }
}
```

Everything else falls back to `appsettings.json` defaults. For Docker/production, use `server/.env`
instead (see [Configuration](#configuration)).

### 3. Run

```bash
# Start infrastructure (PostgreSQL + Redis) — compose lives in server/
cd server
docker compose up -d postgres redis

# Optional DB GUI (Adminer) — dev-only override, never run in production:
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d adminer

# Run the API (auto-migrates and seeds on first start) — from server/
cd src/NomNomzBot.Api
dotnet run
```

On first start the API waits for Postgres and Redis, runs migrations, seeds reference data (TTS voices,
permission presets), and connects the Twitch EventSub WebSocket.

### 4. Connect your accounts

Open the interactive docs at **`http://localhost:5080/scalar`** and run the first-run setup (see below).

### Key URLs

| URL | Purpose |
|-----|---------|
| `http://localhost:5080` | API |
| `http://localhost:5080/scalar` | Interactive API docs (Scalar) |
| `http://localhost:5080/health` | Health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe (DB + Redis) |
| `http://localhost:5080/health/version` | Deployed build version |
| `http://localhost:8082` | Adminer (Postgres browser) — dev override only |

## Using NomNomzBot

> The Compose dashboard isn't built yet, so today you drive the bot through its **REST API**. The
> interactive Scalar docs at `/scalar` let you call every endpoint from the browser.

### First-run setup

The API is **self-describing**: `GET /api/v1/system/setup/wizard` returns the whole onboarding flow as
data — each step, what it needs, and the endpoint to call. The flow is:

1. **Twitch application** — confirm your Client ID / Secret are configured.
2. **Streamer account** — authorize your own Twitch account (`GET /api/v1/auth/twitch` → approve on
   Twitch → back to the callback). Scopes are requested progressively as you enable features.
3. **Bot account** — authorize the account the bot posts chat from. This is an *additional registration*
   on top of your account (like connecting Spotify), not a separate login.
4. **Basics** — bot prefix, default language, timezone.
5. **Integrations (optional)** — Spotify, Discord, YouTube; can be added later.

Once a streamer account is configured, the credential setup endpoints lock to admins.

### Configuring your bot

Everything is a versioned endpoint under `/api/v1/` (browse them in `/scalar`):

- Commands, pipelines, event responses, timers
- Moderation, channel-point rewards, polls/predictions, stream info
- Economy (currency, store, jars, leaderboards, games)
- TTS config and voices, music/song-request settings
- Roles & permissions, integrations, webhooks

Responses use `StatusResponseDto<T>` or `PaginatedResponse<T>`; errors are RFC 7807 problem details.

### Public pages (viewers + OBS)

The bot serves two lightweight pages that need no app:

- **Song requests** (`web/sr`) — give viewers your channel's request-page URL (resolved by a rotatable
  per-channel token); they queue tracks with no login.
- **OBS overlay** (`web/overlay`) — add your channel's overlay URL as a **browser source** in OBS to show
  subscription/follow/cheer/raid/gift alerts and a persistent now-playing bar, pushed live over SignalR.

The exact tokenized URLs for your channel are issued through the API (the dashboard will surface them
once it ships).

## Configuration

- **Local dev (`dotnet run`)** — `server/src/NomNomzBot.Api/appsettings.json` (defaults) +
  `appsettings.Development.json` (your secrets).
- **Docker / deploy** — `server/.env` (copy from `server/.env.example`, which documents every variable).
  Config keys map to env vars with `__`, e.g. `Twitch:ClientId` → `TWITCH_CLIENT_ID`; `docker-compose.yml`
  also maps the friendly `API_BASE_URL` onto `App__BaseUrl`.
- **Twitch redirect URI** is computed at runtime from `App:BaseUrl` — register only
  `{App:BaseUrl}/api/v1/auth/twitch/callback`.

Optional integrations activate when their credentials are present: `Spotify`, `Discord`, `YouTube`,
`Azure:Tts`, and `ElevenLabs`. See `server/.env.example` for the full, commented reference.

## Production deployment

The API speaks plain HTTP inside the container (`ASPNETCORE_URLS=http://+:5000`); **TLS is terminated by a
reverse proxy in front of it**. Running the API port directly on the public internet without TLS is not
supported — OAuth tokens and JWTs would travel in cleartext. Two supported paths:

- **Cloudflare Tunnel** — the bundled `cloudflared` service (set `CLOUDFLARE_TUNNEL_TOKEN`) gives a public
  HTTPS hostname with no inbound ports opened.
- **Reverse proxy** — put Caddy or nginx in front (`reverse_proxy localhost:5080`). Caddy provisions
  Let's Encrypt certificates automatically:

  ```caddyfile
  api.example.com {
      reverse_proxy localhost:5080
  }
  ```

The Postgres/Redis ports bind to `127.0.0.1` and Adminer is excluded from the production compose, so only
the proxy reaches the API. Set `App:BaseUrl` to your public HTTPS URL — host-header filtering and the
Twitch OAuth redirect URI are both derived from it. If the proxy reaches the API from a non-loopback
address (e.g. a containerised sidecar), set `TRUSTED_PROXY_NETWORKS` so the real client IP is trusted.

## Development

### Project layout

```
nomnomzbot/
├── server/   .NET 10 backend (Domain → Application → Infrastructure → Api), tests, Docker
├── web/      public pages served by the bot (song-request, OBS overlay)
└── app/      Kotlin Multiplatform + Compose dashboard (planned)
```

The backend follows Clean Architecture (dependencies flow inward) with the `Result<T>` pattern, soft
deletes, per-request multi-tenancy, and auto-discovered DI.

### Build & test

```bash
cd server
dotnet build NomNomzBot.slnx
dotnet test
```

The build treats warnings as errors, and the code is formatted with CSharpier
(`dotnet csharpier format .`) — both are commit gates.

## Contributing

Contributions are welcome. A few conventions keep the tree healthy:

- Work in small, validated vertical slices; keep the working tree clean.
- Match the surrounding style; explicit types (no `var`), file-scoped namespaces, `Result<T>` over
  exceptions, async all the way.
- Before committing, run `dotnet csharpier format .` and ensure `dotnet build` + `dotnet test` are green
  (warnings are errors). Conventional commit messages (`feat:`, `fix:`, `chore:`) are preferred.
- The default branch is `master`.

## Security

Found a vulnerability? Please report it privately via a
[GitHub Security Advisory](https://github.com/NoMercyLabs/nomnomzbot/security/advisories/new) rather than a
public issue. Security fixes ship as GitHub releases tagged `[SECURITY]` and, where warranted, as
advisories — **watch this repository's releases**. Check the build you are running with
`GET /health/version`.

## License

[AGPL-3.0-or-later](server/LICENSE). Copyright © NoMercy Labs.
