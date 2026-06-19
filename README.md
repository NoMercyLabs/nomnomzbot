# NomNomzBot

Open-source, multi-tenant Twitch bot platform. One deployment serves unlimited channels —
each streamer gets isolated commands, channel-point rewards, moderation, timers, overlays,
a visual pipeline engine, and integrations (Spotify, Discord, YouTube, TTS).

## Status

- **Backend (`server/`)** — live and the only supported component today. .NET 10 / C# clean
  architecture, PostgreSQL + Redis, Twitch OAuth + EventSub + IRC, SignalR, Serilog.
- **Frontend** — the previous Expo/React Native app was removed. A replacement is being
  reconsidered and is **not yet decided**. There is no shipping UI right now; the API is driven
  directly (Scalar docs, HTTP clients, overlays).

## Tech stack

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

## Quick start (local dev)

Requires the **.NET 10 SDK** and **Docker** (for Postgres + Redis).

```bash
# 1. Start infrastructure (Postgres + Redis + Adminer) — compose lives in server/
cd server
docker compose up -d postgres redis adminer

# 2. Run the API (auto-migrates, auto-seeds on first start) — from server/
cd src/NomNomzBot.Api
dotnet run
```

Put your Twitch credentials (`Twitch:ClientId`, `Twitch:ClientSecret`, `Twitch:BotUsername`)
in `server/src/NomNomzBot.Api/appsettings.Development.json`. Everything else falls back to
`appsettings.json` defaults.

### Key URLs

| URL | Purpose |
|-----|---------|
| `http://localhost:5080` | API |
| `http://localhost:5080/scalar` | Interactive API docs (Scalar) |
| `http://localhost:5080/health` | Health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe (DB + Redis) |
| `http://localhost:8082` | Adminer (Postgres browser) |

## Configuration

- **Local dev (`dotnet run`):** `server/src/NomNomzBot.Api/appsettings.json` (defaults) +
  `appsettings.Development.json` (your secrets).
- **Docker / deploy:** `server/.env` (copy from `server/.env.example`). Config keys map to env
  vars with `__`, e.g. `Twitch:ClientId` → `TWITCH_CLIENT_ID`; `docker-compose.yml` also maps the
  friendly `API_BASE_URL` onto `App__BaseUrl`.
- Twitch OAuth redirect URIs are computed at runtime from `App:BaseUrl` — register only
  `{App:BaseUrl}/api/v1/auth/twitch/callback` in the Twitch Developer Console.

## Tests

```bash
cd server
dotnet test
```

## Docs

- [Security Architecture](SECURITY_ARCHITECTURE.md)

## License

AGPL-3.0. Copyright (C) NoMercy Labs.
