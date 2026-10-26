# Deploying NomNomzBot

NomNomzBot is two pieces:

- The **bot** (backend) — runs somewhere: your PC, a home server, or your cloud.
- The **dashboard** (client) — how you control it. It is a Vue web app, bundled into every backend
  artifact: whichever scenario you pick, opening the bot's URL in a browser gives you the full
  dashboard with nothing extra to install or build.

One script per operating system drives every scenario:

- **Linux / macOS:** `./deploy.sh`
- **Windows (PowerShell):** `.\deploy.ps1`

Run yours with no arguments any time to see this guide's short form.

Iterating on the dashboard's UI instead of deploying a build? `./start.sh` (repo root) runs the
API and the dashboard in development mode with hot reload, no Docker — see its `--help`.

## Which scenario am I?

| You want… | Scenario | Linux / macOS | Windows (PowerShell) |
|---|---|---|---|
| The bot on **this machine**, zero dependencies, one file | **desktop** (`self_host_lite`) | `./deploy.sh desktop` | `.\deploy.ps1 desktop` |
| The bot on a **home server** with a real database | **docker** (`self_host_full`) | `./deploy.sh docker` | `.\deploy.ps1 docker` |
| To **host the bot for other streamers** (multi-tenant) — **restricted, see below** | **saas** (`saas`) | `./deploy.sh saas` | `.\deploy.ps1 saas` |

Prefer Docker but want no Postgres or Redis? Use the one-container SQLite stack:
`docker compose -f docker-compose.lite.yml up -d --build`. See
[docs/install-docker-lite.md](docs/install-docker-lite.md).

Rule of thumb: start with **desktop**. Move to **docker** when you want Postgres-grade durability
or the bot lives on a server. **saas** is a **restricted option** — see its section below.

## The dashboard

The dashboard is the **Vue web app** in `dashboard/`. There is nothing to install: every backend
artifact bundles it, and the bot serves it at its own URL. Open that URL in a browser, on this
machine or any other device that can reach the bot. The Docker image builds it with
`npm run build` in a Node stage; the single-file publish builds it with `-p:BundleDashboard=true`.
(The old Kotlin desktop app was removed on 2026-10-21.)

## Scenario: desktop — `self_host_lite`

One self-contained file. No Docker, no database server — the bot keeps all its data (SQLite
database, encryption keys, logs) in **one per-user folder**: `%LOCALAPPDATA%\NomNomzBot` on
Windows, `~/.local/share/NomNomzBot` on Linux, `~/Library/Application Support/NomNomzBot` on
macOS. Back up that folder and you've backed up the bot; set `NOMNOMZ_DATA_DIR` to relocate it.

**Requirements:** the [.NET 10 SDK](https://dot.net) and [Node.js 22](https://nodejs.org) (build only — the
produced binary needs nothing; Node builds the Vue dashboard that the binary bundles).

**Linux / macOS**

```bash
./deploy.sh desktop
```

**Windows (PowerShell)**

```powershell
.\deploy.ps1 desktop
```

The script prints where the binary landed. Copy it anywhere and run it:

**Linux / macOS**

```bash
cp server/src/NomNomzBot.Api/bin/Release/net10.0/<rid>/publish/nomnomz ./nomnomz
./nomnomz
```

**Windows (PowerShell)**

```powershell
Copy-Item server\src\NomNomzBot.Api\bin\Release\net10.0\win-x64\publish\nomnomz.exe .\nomnomz.exe
.\nomnomz.exe
```

First start creates the data folder and walks you through setup in the dashboard — open
**http://localhost:5080** in a browser. **Update** by re-running the scenario and replacing the binary; your data stays in
place. There are no prebuilt binaries yet — this scenario is the build.

## Scenario: docker — `self_host_full`

The full stack in Docker: the API plus PostgreSQL 16, Redis 7, and Adminer (a DB browser,
loopback-only), with healthchecks and auto-migration on boot.

**Requirements:** [Docker](https://docs.docker.com/get-docker/) (with Compose v2).

**Linux / macOS**

```bash
./deploy.sh docker
```

**Windows (PowerShell)**

```powershell
.\deploy.ps1 docker
```

On the first run the script creates `.env` from the template, **generates strong secrets for you**
(`JWT_SECRET`, `ENCRYPTION_KEY`, `POSTGRES_PASSWORD`), and asks for your Twitch app credentials —
press Enter to skip and enter them in the dashboard's setup wizard instead. It then builds the
image (web dashboard included), starts the stack, waits until the API reports **ready**, and
prints your URLs.

- Dashboard/API: `http://localhost:5080` — Adminer: `http://localhost:8082`
- **Pull instead of build:** set `API_IMAGE=ghcr.io/nomercylabs/nomnomzbot:stable` in `.env`; the
  script switches to pulling the published image automatically. Pick your release channel by tag:
  `stable` (the Release button), `beta` (every Monday) or `nightly` (every night). The dashboard's
  **Admin → System** page tells you when a newer stable release exists.
- **First bring-up** (or a full reset): re-run the scenario, or `docker compose up -d`. **Logs:**
  `docker compose logs -f api-blue api-green`.
- **Backup:** the `postgres_data` and `api_data` volumes plus your `.env`.

### Zero-downtime updates — blue/green behind Caddy

Host port 5080 is owned by the `caddy` service, not by an API container directly. Two API
services sit behind it, `api-blue` and `api-green`; in steady state exactly one is running (the
"live" colour) and Caddy actively polls its `/health/ready` before routing any traffic to it. This
is what makes an update possible without the port ever going dark — the *proxy* stays up the
whole time, only the API instance behind it changes.

**Deploy an update:**

```powershell
.\scripts\switchover.ps1
```

The script works out which colour is currently live from `docker ps` (never take its word for
it — it always re-derives this), then acquires the new image for the **idle** colour: if
`API_IMAGE` resolves to a registry ref (e.g. `ghcr.io/nomercylabs/nomnomzbot:stable`, the Proxmox
box's setup) it **pulls**; if it resolves to a bare local tag (the default
`nomnomzbot-api:local` that `docker compose up -d --build` produces, which was never pushed
anywhere and can't be pulled) it **builds** instead — pass `-Build` to force the build path
regardless of the configured tag. A real pull failure (network down, bad credentials, missing tag)
still aborts the script before anything is touched, the same as before. It then starts the idle
colour alongside the live one, waits for the idle colour to pass its own `/health/ready`, and only
then stops the old colour with a stop timeout long enough for it to drain in-flight requests
(SIGTERM → up to 30s → SIGKILL). If the idle colour never becomes ready, the script stops it,
leaves the old colour serving, and exits non-zero — there is never a moment with zero healthy
instances. Re-running the script is safe; it converges only when at most one colour runs.
It refuses when both colours already run (resolve manually or let `guard-single-color.sh` decide).

By default it targets the local compose stack (`docker-compose.yml` in the repo root) and uses
whatever image `.env`'s `API_IMAGE` resolves to (unset → local build path). Point it at a remote
host the same way `ship.ps1` does, by setting `NOMNOMZ_DEPLOY_SSH` (`user@host`),
`NOMNOMZ_DEPLOY_KEY` (SSH key path), and optionally `NOMNOMZ_DEPLOY_DIR` (default
`/opt/nomnomzbot`) — set `API_IMAGE` in that host's `.env` to the registry ref to use the pull path
there.

**Do not** point `watchtower` (or any other auto-updater) at `api-blue`/`api-green` — its
stop-then-start update is exactly the downtime this setup removes, and it does not know how to
wait for `/health/ready` before killing the live colour. `watchtower`, if present on a host at
all, is a separate container the operator added by hand outside this compose file; it was never
declared here and updates must go through `switchover.ps1` instead.

## Scenario: saas — `saas` multi-tenant fleet mode (restricted)

> **⚠ Restricted option.** Operating NomNomzBot as a hosted service for other people is **against
> the project license** — that right is reserved to **NoMercy Labs** (the official cloud offering).
> Self-hosting your own bot for your own channel(s) — desktop or docker — is always free and
> unrestricted. This section documents the mode for the official cloud deployment.

The same Docker stack switched to `saas` mode (`DEPLOYMENT_MODE=saas`) — multi-tenant, built to sit
behind **your** HTTPS reverse proxy. A single streamer never needs it.

**Requirements:** Docker, a public domain, and a reverse proxy terminating TLS (Caddy, nginx, or a
Cloudflare Tunnel — see the [README's production deployment section](README.md#production-deployment)).

**Linux / macOS**

```bash
./deploy.sh saas
```

**Windows (PowerShell)**

```powershell
.\deploy.ps1 saas
```

The script refuses to start until `.env` is production-shaped, and tells you exactly what to fix:

- `API_BASE_URL` must be your **public HTTPS origin** (not `localhost`) — Twitch OAuth redirect
  URIs and host-header filtering both derive from it.
- `JWT_SECRET` and `ENCRYPTION_KEY` must not be the dev defaults.
- Set `TRUSTED_PROXY_NETWORKS` in `.env` when the proxy reaches the API over a Docker network
  (e.g. `172.16.0.0/12`) so the real client IP is trusted.

It then sets `DEPLOYMENT_MODE=saas` in `.env` and brings the stack up. This gives you a
**single-node** SaaS deployment; running multiple API replicas behind the proxy uses the same
image and migrates safely on its own (exactly one replica takes the migration lock), scaled out
with your own orchestration.

## Discord live-role setup checklist

The Discord live role gives a member a role while their channel is live and removes it when the
stream ends. Discord's own rules put these steps on the server owner. The bot cannot do them.

- [ ] Install NomNomzBot in the Discord server and grant it **Manage Roles**.
- [ ] In Server Settings > Roles, drag the bot's own role **above** the role it hands out. A new bot
      role lands at the bottom by default, and another bot working on the same role proves nothing.
- [ ] Link the streamer's Discord account to their channel. The bot needs it to find the member to
      mark live.
- [ ] If another bot already drives that role or posts go-live announcements, turn that off for the
      channel first, or point NomNomzBot at a different role while testing. Two bots on one role
      fight over it and double-post.
- [ ] Accept one link per friend's channel. Each channel is its own tenant, and no shortcut keeps
      tenant isolation.

## Every combination at a glance

| Backend scenario | Dashboard (open in a browser) |
|---|---|
| desktop | `http://localhost:5080` |
| docker | `http://localhost:5080` (or your URL) |
| saas *(restricted — see above)* | `https://your-domain` |

The dashboard is the same Vue build in every scenario. Any device on your network can open it.

The public site at `https://nomnomz.bot` is a separate static build served by GitHub Pages, not by
the bot. See [docs/site-hosting.md](docs/site-hosting.md).
