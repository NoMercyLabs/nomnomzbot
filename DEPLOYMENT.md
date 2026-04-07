# NomNomzBot — Deployment Guide

## Deployment Modes

NomNomzBot supports two modes:
- **Self-hosted** — you run it on your own server, for yourself or a small community
- **Hosted SaaS** — multi-tenant, many streamers share one deployment (see Security Architecture for requirements)

For most people, self-hosted is what you want.

## Quick Self-Host (Recommended: Docker + Caddy)

### What you need
- A Linux server (Ubuntu 22.04 LTS recommended)
- Docker + Docker Compose v2
- A domain name pointed at your server
- A Twitch Developer Application

### Steps

1. Clone the monorepo:
   ```bash
   git clone --recursive git@github.com:NoMercyLabs/nomnomzbot.git
   cd nomnomzbot/nomnomzbot-server
   ```

2. Copy and fill in the environment file:
   ```bash
   cp .env.example .env
   ```
   Required variables: `POSTGRES_PASSWORD`, `JWT_SECRET`, `ENCRYPTION_KEY`, `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET`, `TWITCH_BOT_USERNAME`

   Generate secrets:
   ```bash
   openssl rand -base64 32   # use output for JWT_SECRET
   openssl rand -base64 32   # use output for ENCRYPTION_KEY
   ```

3. Set your domain in `APP_BASE_URL`:
   ```env
   APP_BASE_URL=https://bot.yourdomain.com
   ```

4. Set up Caddy as reverse proxy (handles TLS automatically):
   ```
   # /etc/caddy/Caddyfile
   bot.yourdomain.com {
       reverse_proxy localhost:5080
   }
   ```

5. Start everything:
   ```bash
   docker compose up -d
   ```

6. Update Twitch redirect URIs in your Twitch Developer Console:
   ```
   https://bot.yourdomain.com/api/v1/auth/twitch/callback
   https://bot.yourdomain.com/api/v1/auth/twitch/bot/callback
   https://bot.yourdomain.com/api/v1/channels/callback/bot
   ```
   > **Active dev domain:** The shared dev credentials use `bot-dev-api.nomercy.tv` as the base URL. `api.nomnomz.bot` is the planned production domain — will replace this once fully configured.

The API starts on `http://localhost:5080`. Caddy handles TLS and proxies requests.

## Updating

```bash
cd nomnomzbot
git pull --recurse-submodules
cd nomnomzbot-server
docker compose pull
docker compose up -d
```

Migrations run automatically on startup. No manual migration commands needed.

## Environment Variable Reference

See the full table in the root [README.md](README.md#environment-setup).

## Health Checks

| Endpoint | Purpose |
|---|---|
| `GET /health` | Full health status (DB, Redis, Twitch) |
| `GET /health/live` | Liveness probe (is the process up?) |
| `GET /health/ready` | Readiness probe (is the DB connected?) |

## Ports

| Port | Service |
|---|---|
| `5080` | API (HTTP — proxy behind Caddy/nginx) |
| `5432` | PostgreSQL (internal Docker network only) |
| `6379` | Redis (internal Docker network only) |
| `8082` | Adminer DB browser (internal only — do not expose) |

**Never expose ports 5432, 6379, or 8082 to the internet.**

## Security Checklist

Before going live:
- [ ] `POSTGRES_PASSWORD` is not the default `nomnomzbot_dev`
- [ ] `JWT_SECRET` is at least 32 characters and randomly generated
- [ ] `ENCRYPTION_KEY` is 32 bytes, base64-encoded, randomly generated
- [ ] API is behind TLS (Caddy or nginx — not exposed directly on 80/443)
- [ ] Ports 5432, 6379, 8082 are NOT exposed to the internet
- [ ] Twitch redirect URIs use `https://` not `http://`

For the full security architecture and audit, see [SECURITY_ARCHITECTURE.md](SECURITY_ARCHITECTURE.md).

## Troubleshooting

**API won't start — "waiting for database"**
Run `docker compose ps` — make sure `postgres` shows `healthy`. If it's stuck starting, check `docker compose logs postgres`.

**OAuth redirect fails**
The `APP_BASE_URL` in your `.env` must exactly match the base URL you registered in the Twitch Developer Console.

**"Invalid JWT" errors after restart**
`JWT_SECRET` must be the same across restarts. If you regenerated it, all existing sessions are invalidated — users need to log in again.

**Database migrations failed**
Check `docker compose logs api`. The migration error message will tell you which migration failed. Most commonly caused by an incompatible schema from a previous version — check the release notes.
