# NomNomzBot

> An open-source, **multi-tenant Twitch bot platform**. Run one deployment and serve unlimited
> channels — every streamer gets a fully isolated bot: custom commands, a visual pipeline engine,
> moderation, channel-point rewards, timers, an economy, text-to-speech, song requests, OBS
> overlays, and integrations (Spotify, Discord, YouTube).

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](server/LICENSE)
[![Backend: .NET 10](https://img.shields.io/badge/backend-.NET%2010-512BD4.svg)](#how-it-works)
[![Status: pre-release](https://img.shields.io/badge/status-pre--release-orange.svg)](#project-status)

You run NomNomzBot through its **dashboard app**, which gives you a **guided setup** — it connects your
Twitch and bot accounts and collects everything the bot needs through proper input fields, so you never
edit a config file. The bot itself is **self-hostable from day one** (point the dashboard at your own
server — zero external infrastructure or oversight), and the same codebase powers an optional hosted
(SaaS) offering. Under the hood it speaks Twitch over Helix, EventSub (WebSocket), and IRC, and exposes a
versioned REST API plus real-time SignalR feeds.

---

## Table of contents

- [Project status](#project-status)
- [Features](#features)
- [How it works](#how-it-works)
- [Getting started](#getting-started)
  - [Set up with the dashboard](#set-up-with-the-dashboard)
  - [Run your own backend (Docker)](#run-your-own-backend-docker)
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
  the economy, TTS, roles & permissions, server-side chat decoration (emotes, badges, cheermotes), webhooks,
  and the sandboxed custom-code action.
- **Public web pages (`web/`)** — the viewer **song-request page** and the **OBS overlay** browser source
  are built and served by the bot.
- **Dashboard (`app/`)** — the **Kotlin Multiplatform + Compose** client (one codebase for desktop and web)
  that provides the guided setup described below is **in active development**. Until it ships, the same
  onboarding flow runs through the bot's self-describing REST API (interactive docs at `/scalar`).

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
- **Rich chat** — server-side message decoration: Twitch **and** BTTV / FFZ / 7TV emotes, badge and
  cheermote images, coloured mentions, and opt-in link previews, emitted ready-to-render to the dashboard.
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
└─ app/     — Kotlin Multiplatform + Compose dashboard (in active development)
```

The backend is **profile-agnostic and direct-connect**: the dashboard just needs the backend URL and talks
REST + SignalR straight to it. Self-host points it at `localhost` or your own exposed URL; the hosted build
points it at the SaaS API. There is no central broker.

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
| Dashboard | Kotlin Multiplatform + Compose (desktop + web) |
| API docs | OpenAPI + Scalar |

## Getting started

There are two pieces: the **dashboard**, which you set the bot up and run it from, and the **backend**, the
server the dashboard talks to. On the hosted offering the backend is run for you, so you only need the
dashboard. To **self-host**, run your own backend (Docker) and point the dashboard at it.

### Set up with the dashboard

The dashboard is the normal way to run NomNomzBot — **no config files, no API calls by hand.** You point it
at a backend URL (`localhost` or your own server for self-host, the hosted API for SaaS) and a **setup
wizard** walks you through everything, with a labelled input for each value the bot needs:

1. **Connect your Twitch account** — sign in and authorize. Scopes are requested progressively, only as you
   enable the features that need them.
2. **Connect your bot account** — authorize the account the bot posts chat from. This is an *additional
   connection* on top of your account (like linking Spotify), not a second login.
3. **Enter your app credentials** *(self-host)* — paste the Twitch **Client ID / Secret** from your Twitch
   developer app into the wizard's fields. On the hosted offering this is already configured for you.
4. **Basics** — bot prefix, default language, timezone.
5. **Integrations (optional)** — Spotify, Discord, YouTube; enable any now or later from settings.

When you finish, you land on the dashboard home with the bot live in your channel.

> **In development.** The Compose dashboard isn't released yet. In the meantime the **exact same guided
> flow** is available through the bot's self-describing API: `GET /api/v1/system/setup/wizard` returns each
> step, what it needs, and the endpoint to call, and you can run every step from the interactive docs at
> **`/scalar`**. See [Using NomNomzBot](#using-nomnomzbot).

### Run your own backend (Docker)

Skip this section if you're using the hosted offering. To self-host, run the backend — the dashboard then
connects to it.

**Prerequisites:** **Docker**, and a registered **Twitch application** ([Twitch Developer
Console](https://dev.twitch.tv/console/apps)) for its Client ID / Secret. Redirect URIs are computed at
runtime from `App:BaseUrl`, so register exactly one callback — `{App:BaseUrl}/api/v1/auth/twitch/callback`
(locally, `http://localhost:5080/api/v1/auth/twitch/callback`).

```bash
cd server
cp .env.example .env          # then set TWITCH_CLIENT_ID / TWITCH_CLIENT_SECRET / TWITCH_BOT_USERNAME
docker compose up -d          # PostgreSQL, Redis, and the API
```

The Twitch **Client ID / Secret** are the one value the backend needs to boot (self-hosters can also enter
them in the dashboard wizard once it ships). On first start the API waits for Postgres and Redis, runs
migrations, seeds reference data (TTS voices, permission presets), and connects the Twitch EventSub
WebSocket. Point your dashboard at `http://localhost:5080` (or your exposed URL) and run the setup above.

For local backend development without Docker (`dotnet run`), see [Development](#development).

#### Key URLs

| URL | Purpose |
|-----|---------|
| `http://localhost:5080` | API (point the dashboard here) |
| `http://localhost:5080/scalar` | Interactive API docs (Scalar) |
| `http://localhost:5080/health` | Health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe (DB + Redis) |
| `http://localhost:5080/health/version` | Deployed build version |
| `http://localhost:8082` | Adminer (Postgres browser) — dev override only |

> Adminer is a development-only convenience: `docker compose -f docker-compose.yml -f docker-compose.dev.yml
> up -d adminer`. It is excluded from the production compose.

## Using NomNomzBot

Day to day you drive the bot from the dashboard. While the Compose dashboard is in development, the same
operations are available through the **REST API** — the interactive Scalar docs at `/scalar` let you call
every endpoint from the browser.

### What the guided setup does

The onboarding flow (the dashboard wizard, or `GET /api/v1/system/setup/wizard` today) is:

1. **Twitch application** — your Client ID / Secret (entered in the wizard, or set in the backend env).
2. **Streamer account** — authorize your own Twitch account (`GET /api/v1/auth/twitch` → approve on
   Twitch → back to the callback). Scopes are requested progressively as you enable features.
3. **Bot account** — authorize the account the bot posts chat from — an *additional connection*, not a
   separate login.
4. **Basics** — bot prefix, default language, timezone.
5. **Integrations (optional)** — Spotify, Discord, YouTube; can be added later.

Once a streamer account is configured, the credential-setup endpoints lock to admins.

### Configuring your bot

Everything the dashboard surfaces is a versioned endpoint under `/api/v1/` (browse them in `/scalar`):

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

The dashboard surfaces the exact tokenized URLs for your channel (today they're issued through the API).

## Configuration

The dashboard collects most settings for you; these are the backend's own knobs:

- **Docker / deploy** — `server/.env` (copy from `server/.env.example`, which documents every variable).
  Config keys map to env vars with `__`, e.g. `Twitch:ClientId` → `TWITCH_CLIENT_ID`; `docker-compose.yml`
  also maps the friendly `API_BASE_URL` onto `App__BaseUrl`.
- **Local dev (`dotnet run`)** — `server/src/NomNomzBot.Api/appsettings.json` (defaults) +
  `appsettings.Development.json` (your secrets). Everything else falls back to the defaults.
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
└── app/      Kotlin Multiplatform + Compose dashboard (in active development)
```

The backend follows Clean Architecture (dependencies flow inward) with the `Result<T>` pattern, soft
deletes, per-request multi-tenancy, and auto-discovered DI.

### Run the backend locally

```bash
cd server
docker compose up -d postgres redis        # just the infrastructure
cd src/NomNomzBot.Api
dotnet run                                  # auto-migrates and seeds on first start
```

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
