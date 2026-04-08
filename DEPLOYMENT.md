# NomNomzBot — Deployment Guide

## Server requirements

**Only Docker is required.** No .NET SDK, no Node.js, no Yarn, no build tools.
The API and frontend both build inside multi-stage Docker containers.

- Linux server (Ubuntu 22.04 LTS recommended)
- Docker + Docker Compose v2 — `deploy.sh` installs these if missing
- A domain name (for TLS + Twitch OAuth)
- A Twitch Developer Application

---

## Deployment Modes

- **Self-hosted** — you run it on your own server, for yourself or a small community
- **Hosted SaaS** — multi-tenant, many streamers share one deployment

Set `DEPLOYMENT_MODE=self-hosted` (default) or `DEPLOYMENT_MODE=saas` in `.env`.

---

## Quick Deploy

### Linux / macOS

```bash
git clone --recursive https://github.com/NoMercyLabs/nomnomzbot.git
cd nomnomzbot
chmod +x deploy.sh
./deploy.sh
```

### Windows

```powershell
git clone --recursive https://github.com/NoMercyLabs/nomnomzbot.git
cd nomnomzbot
.\deploy.ps1
```

Both scripts:
1. Check for Docker, offer to install if missing
2. Generate `JWT_SECRET`, `ENCRYPTION_KEY`, `POSTGRES_PASSWORD`, `REDIS_PASSWORD` via `openssl rand`
3. Prompt for `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET`, `API_BASE_URL`, and `FRONTEND_URL` (with a smart default)
4. Run `docker compose up -d --build`
5. Wait for the health check and print your URLs + Twitch redirect URIs

---

## Manual deploy (without the script)

1. Clone:
   ```bash
   git clone --recursive https://github.com/NoMercyLabs/nomnomzbot.git
   cd nomnomzbot
   ```

2. Create `.env`:
   ```bash
   cp .env.example .env
   ```
   Generate secrets:
   ```bash
   openssl rand -base64 64   # → JWT_SECRET
   openssl rand -base64 32   # → ENCRYPTION_KEY
   openssl rand -hex 32      # → POSTGRES_PASSWORD
   openssl rand -hex 32      # → REDIS_PASSWORD
   ```
   Fill in `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET`, and `API_BASE_URL`.

3. Start:
   ```bash
   docker compose up -d --build
   ```

4. Register the single Twitch redirect URI — all streamer, platform-bot, and channel-bot OAuth flows now use the same callback endpoint:
   ```
   {API_BASE_URL}/api/v1/auth/twitch/callback
   ```

---

## Reverse proxy (TLS)

Twitch OAuth requires HTTPS. Point Caddy or nginx at the containers:

```
# /etc/caddy/Caddyfile
api.yourdomain.com {
    reverse_proxy localhost:5080
}
yourdomain.com {
    reverse_proxy localhost:8081
}
```

Then set in `.env`:
```env
API_BASE_URL=https://api.yourdomain.com
FRONTEND_URL=https://yourdomain.com
```

---

## Updating

```bash
cd nomnomzbot
git pull
docker compose up -d --build
```

Migrations run automatically on startup.

---

## Health Checks

| Endpoint | Purpose |
|---|---|
| `GET /health` | Full health status (DB, Redis, Twitch) |
| `GET /health/live` | Liveness probe (is the process up?) |
| `GET /health/ready` | Readiness probe (is the DB connected?) |

## Ports

| Port | Service |
|---|---|
| `5080` | API |
| `8081` | Web frontend |
| `5432` | PostgreSQL (internal only) |
| `6379` | Redis (internal only) |
| `8082` | Adminer DB browser (internal only) |

**Never expose ports 5432, 6379, or 8082 to the internet.**

---

## Security Checklist

- [ ] `POSTGRES_PASSWORD` is not the default `nomnomzbot_dev`
- [ ] `JWT_SECRET` is randomly generated (≥64 bytes)
- [ ] `ENCRYPTION_KEY` is randomly generated (32 bytes, base64)
- [ ] API and frontend are behind TLS
- [ ] Ports 5432, 6379, 8082 are NOT exposed
- [ ] Twitch redirect URIs use `https://`

---

## Troubleshooting

**API won't start — "waiting for database"**
`docker compose ps` — check `postgres` is `healthy`. If stuck: `docker compose logs postgres`.

**OAuth redirect fails**
`API_BASE_URL` in `.env` must exactly match the base URL registered in your Twitch app.

**"Invalid JWT" errors after restart**
`JWT_SECRET` must stay the same across restarts. Changing it invalidates all sessions.

**Bot token invalid after ENCRYPTION_KEY change**
Rotating `ENCRYPTION_KEY` makes all stored OAuth tokens unreadable. Re-auth the bot after rotating.

**Database migrations failed**
`docker compose logs api` — the error message names the failing migration. Check release notes for schema changes.
