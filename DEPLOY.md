# Deploying NomNomzBot

NomNomzBot is two pieces:

- The **bot** (backend) — runs somewhere: your PC, a home server, or your cloud.
- The **dashboard** (client) — how you control it. 
- The **web dashboard** is bundled into every backend artifact: whichever scenario you pick, opening the bot's URL in a browser gives you the full dashboard with nothing extra to install or build. 
- A **standalone desktop app** with the exact
  same UI is optional on top.

One script drives every scenario — `./deploy.sh` on Linux/macOS, `.\deploy.ps1` on Windows.
Run it with no arguments any time to see this guide's short form.

## Which scenario am I?

| You want… | Scenario | Command (Linux/macOS · Windows) |
|---|---|---|
| The bot on **this machine**, zero dependencies, one file | **desktop** | `./deploy.sh desktop` · `.\deploy.ps1 desktop` |
| The bot on a **home server** with a real database | **docker** | `./deploy.sh docker` · `.\deploy.ps1 docker` |
| To **host the bot for other streamers** (multi-tenant) | **saas** | `./deploy.sh saas` · `.\deploy.ps1 saas` |

Add `--app` (Windows: `-App`) to any of them to also build the **standalone desktop dashboard
app** for your OS — e.g. `./deploy.sh desktop --app`.

Rule of thumb: start with **desktop**. Move to **docker** when you want Postgres-grade durability
or the bot lives on a server. **saas** is only for operators running a public service.

## The dashboard — web vs desktop app

| | Web dashboard | Desktop app |
|---|---|---|
| Build step | **none — always bundled** | `--app` / `-App` |
| Get it | open the bot's URL in a browser | installer in `app/composeApp/build/compose/binaries/main/` |
| Connects to | the bot that served it | any bot — saved connections + automatic LAN discovery (mDNS) |
| Best for | quick access, other devices, no install | daily driving, multiple bots |

They are the **same application** (Kotlin Multiplatform + Compose) — identical screens and
features. Requirements for `--app`: a JDK (21 recommended, [adoptium.net](https://adoptium.net));
on Windows the MSI additionally needs the [WiX Toolset 3.x](https://wixtoolset.org). The installer
is always built for the OS you run the script on.

## Scenario: desktop — `self_host_lite`

One self-contained file. No Docker, no database server — the bot keeps all its data (SQLite
database, encryption keys, logs) in **one per-user folder**: `%LOCALAPPDATA%\NomNomzBot` on
Windows, `~/.local/share/NomNomzBot` on Linux, `~/Library/Application Support/NomNomzBot` on
macOS. Back up that folder and you've backed up the bot; set `NOMNOMZ_DATA_DIR` to relocate it.

**Requirements:** the [.NET 10 SDK](https://dot.net) (build only — the produced binary needs nothing).

```bash
./deploy.sh desktop            # Linux / macOS
```
```powershell
.\deploy.ps1 desktop           # Windows
```

The script prints where the binary landed. Copy it anywhere and run it:

```bash
cp server/src/NomNomzBot.Api/bin/Release/net10.0/<rid>/publish/nomnomz ./nomnomz
./nomnomz
```

First start creates the data folder and walks you through setup in the dashboard — open
**http://localhost:5080** in a browser (or connect the desktop app; it finds LAN bots
automatically). **Update** by re-running the scenario and replacing the binary; your data stays in
place. Prebuilt per-OS binaries will ship as GitHub Release assets once tagged releases begin —
until then, this scenario is the build.

## Scenario: docker — `self_host_full`

The full stack in Docker: the API plus PostgreSQL 16, Redis 7, and Adminer (a DB browser,
loopback-only), with healthchecks and auto-migration on boot.

**Requirements:** [Docker](https://docs.docker.com/get-docker/) (with Compose v2).

```bash
./deploy.sh docker             # Linux / macOS
```
```powershell
.\deploy.ps1 docker            # Windows
```

On the first run the script creates `.env` from the template, **generates strong secrets for you**
(`JWT_SECRET`, `ENCRYPTION_KEY`, `POSTGRES_PASSWORD`), and asks for your Twitch app credentials —
press Enter to skip and enter them in the dashboard's setup wizard instead. It then builds the
image (web dashboard included), starts the stack, waits until the API reports **ready**, and
prints your URLs.

- Dashboard/API: `http://localhost:5080` — Adminer: `http://localhost:8082`
- **Pull instead of build:** set `API_IMAGE=ghcr.io/nomercylabs/nomnomzbot:latest` in `.env`; the
  script switches to pulling the published image automatically.
- **Update** by re-running the scenario. **Logs:** `docker compose logs -f api`.
- **Backup:** the `postgres_data` and `api_data` volumes plus your `.env`.

## Scenario: saas — multi-tenant fleet mode

The same Docker stack switched to `saas` mode — multi-tenant, built to sit behind **your** HTTPS
reverse proxy. This is for operators running NomNomzBot as a service; a single streamer never
needs it.

**Requirements:** Docker, a public domain, and a reverse proxy terminating TLS (Caddy, nginx, or a
Cloudflare Tunnel — see the [README's production deployment section](README.md#production-deployment)).

```bash
./deploy.sh saas               # Linux / macOS
```
```powershell
.\deploy.ps1 saas              # Windows
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

## Every combination at a glance

| Backend scenario | Web dashboard | Standalone desktop app |
|---|---|---|
| desktop | `http://localhost:5080` | `./deploy.sh desktop --app` |
| docker | `http://localhost:5080` (or your URL) | `./deploy.sh docker --app` |
| saas | `https://your-domain` | `./deploy.sh saas --app` |

The `--app` build is independent of the backend scenario — you can also run it on a different
machine than the bot (build the app on your PC, point it at the server).
