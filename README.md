# NomNomzBot

Open-source, multi-tenant Twitch bot platform. One deployment serves unlimited channels —
each streamer gets isolated commands, channel-point rewards, moderation, timers, overlays,
a visual pipeline engine, and integrations (Spotify, Discord, YouTube, TTS).

## Status

- **Backend (`server/`)** — the live, supported component. .NET 10 / C# clean architecture,
  PostgreSQL + Redis, Twitch OAuth + Helix + EventSub (WebSocket) + IRC, SignalR, Serilog.
  Built and tested: identity/auth, the Twitch Helix + EventSub integration, moderation,
  channel-point rewards, the visual pipeline engine, timers, TTS, widgets/overlays, community,
  and dashboard. **Roles & permissions** — a full three-plane authorization subsystem (community
  standing ∪ channel-management role ∪ `!permit` delegation on one numeric ladder; Gate-1 tenant
  resolution + Gate-2 per-action gating via `[RequireAction]`; platform IAM) — is complete. The
  **economy** is complete: the atomic currency ledger, earning rules, the store catalog,
  cross-channel savings jars, leaderboards, and fun-money games (with an optional, off-by-default
  18+ gate that auto-passes adults by Twitch account age / staff type) are all built and proven
  against a real database; the full REST surface (currency, catalog, jars, leaderboards, games
  controllers, each per-action gated) is wired; and the pipeline actions (grant/deduct currency,
  check balance, jar contribute, play game) let the economy ride the command/event pipeline.
  **Platform / commerce** (Phase 7) is underway — the **monetization & billing** subsystem is
  built end to end: tiers + entitlement resolution (self-host is always unlimited), metered
  quotas with a usage-quota-exceeded signal, the subscription lifecycle with inbound Stripe
  webhook appliers, and founders/invite codes that grant a badge and/or tier; the full REST
  surface (owner-only tenant billing, the platform-admin invite console, and an HMAC-verified
  Stripe webhook) plus a `require_tier` pipeline gate are wired. The outbound Stripe gateway
  (hosted checkout + self-serve billing portal, on Stripe.net) is built and wired — fail-closed
  when unconfigured — and just needs live/test Stripe keys to transact. The
  **federation** trust plane is also built: a global peer directory with a default-deny
  trust lifecycle, per-channel opt-ins (a channel shares/accepts nothing until it explicitly
  enables it), and rsa-sha256 per-message signing/verification (in-box crypto, fail-closed) —
  AuthN federates while every authorization decision stays local. Its cross-instance transport
  (mTLS handshake, remote event bus) and the OpenIddict OIDC issuer are the deferred,
  infrastructure-bound pieces. The **webhooks** subsystem is built end to end, both directions.
  Inbound: a tenant configures an endpoint (Ko-fi / GitHub / generic adapter, opaque URL token,
  AEAD-sealed verification secret), and a real third-party POST flows through the public ingest
  endpoint → per-provider signature verification → endpoint-salted dedup → the event journal →
  fan-out, with in-box HMAC and a no-amplification rule for unknown tokens. Outbound: endpoints
  pin to the shared egress allowlist, deliver through an SSRF-hardened client (resolve-then-pin,
  https-only, internal/metadata IPs blocked), sign per Standard Webhooks with rotation overlap,
  and retry with exponential backoff + auto-disable via a background drain. The **custom-code /
  sandbox** subsystem is functional end to end: authors version TypeScript scripts (validate-on-
  save, immutable versions, hot-swap), and the `run_code` pipeline action runs them in a hardened
  Jint sandbox — a fresh engine per run, CLR interop off, no code-from-string (eval/Function), and
  statement/memory/time/recursion budgets, with side-effecting host calls gated behind a per-
  execution, capability-keyed bridge and the shared SSRF-hardened egress client. **Analytics**
  rebuilds read models from the event journal (per-channel daily aggregate, per-viewer profiles +
  engagement, derived watch-session presence) behind channel + viewer + SaaS-platform read APIs.
  A **feature-flag** system (deterministic FNV-1a rollout buckets, per-tenant overrides, tier +
  deployment gates, an admin write surface with cache invalidation) backs staged rollout and the
  `FEATURE_DISABLED` gating used across the platform. All six Phase-7 subsystems are complete,
  including the custom-code capability broker and the full `bot.*` host surface — chat, economy,
  music, and a 256 KiB-capped, SSRF-hardened `http.fetch`, every catalogued capability dispatching
  through the per-execution, capability-gated bridge. **Phase 8 — the public web pages — is built**,
  so the bot is a working, headless-on-Twitch product end to end. The bot serves its own lightweight
  pages (`UseStaticFiles`): the public **song-request** page (a rotatable per-channel token resolves
  the channel and accepts requests with no login) is wired front to back, and the **OBS overlay**
  browser-source renders seven event feeds pushed from domain events over SignalR — subscription,
  resub, follow, cheer, raid, and gift alerts plus a persistent now-playing bar — each fanned only
  to the widgets that subscribe to it and styled by saved per-widget settings. The **SaaS Wasmtime
  sandbox** executor's hardened isolation boundary is built too (wasmtime-dotnet, Cranelift-only,
  fuel + epoch, every risky proposal off, WASI unlinked, mandatory Store limits) and proven against
  real wasm; its JS-in-WASM engine module is the one remaining SaaS-deploy artifact. The remaining
  infrastructure-bound externals are live Stripe keys, that engine module, and the OIDC issuer +
  federation cross-instance transport. ~1103 tests green across four suites.
- **Frontend** — **Kotlin Multiplatform + Compose Multiplatform** (one codebase, desktop + web/Wasm
  identical UI; mobile later). The previous Expo/React Native app was removed. The dashboard app is
  **not built yet** — today the API is driven directly (Scalar docs, HTTP clients, overlays), and the
  public viewer/OBS pages (song-request, overlays) are the lightweight `web/` pages served by the bot.

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
# 1. Start infrastructure (Postgres + Redis) — compose lives in server/
cd server
docker compose up -d postgres redis
# Optional DB GUI (Adminer) — dev-only override, never run in production:
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d adminer

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
| `http://localhost:8082` | Adminer (Postgres browser) — dev override only |

## Configuration

- **Local dev (`dotnet run`):** `server/src/NomNomzBot.Api/appsettings.json` (defaults) +
  `appsettings.Development.json` (your secrets).
- **Docker / deploy:** `server/.env` (copy from `server/.env.example`). Config keys map to env
  vars with `__`, e.g. `Twitch:ClientId` → `TWITCH_CLIENT_ID`; `docker-compose.yml` also maps the
  friendly `API_BASE_URL` onto `App__BaseUrl`.
- Twitch OAuth redirect URIs are computed at runtime from `App:BaseUrl` — register only
  `{App:BaseUrl}/api/v1/auth/twitch/callback` in the Twitch Developer Console.

## Production deployment (TLS)

The API speaks plain HTTP inside the container (`ASPNETCORE_URLS=http://+:5000`); **TLS is terminated at
a reverse proxy in front of it** — running the API port directly on the public internet without TLS is
not supported, since OAuth tokens and JWTs would travel in cleartext. Two supported paths:

- **Cloudflare Tunnel** — the bundled `cloudflared` service (set `CLOUDFLARE_TUNNEL_TOKEN`) gives a
  public HTTPS hostname with no inbound ports opened.
- **Reverse proxy** — put Caddy or nginx in front (`reverse_proxy localhost:5080`). Caddy provisions
  Let's Encrypt certificates automatically; example:

  ```caddyfile
  api.example.com {
      reverse_proxy localhost:5080
  }
  ```

The Postgres/Redis ports are bound to `127.0.0.1` and Adminer is excluded from the production compose,
so only the proxy reaches the API. Set `App:BaseUrl` to your public HTTPS URL — host-header filtering and
the Twitch OAuth redirect URI are both derived from it.

## Tests

```bash
cd server
dotnet test
```

## Docs

- [Security Architecture](SECURITY_ARCHITECTURE.md)

## License

AGPL-3.0. Copyright (C) NoMercy Labs.
