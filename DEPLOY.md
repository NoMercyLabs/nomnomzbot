# Deploying NomNomzBot

NomNomzBot is two pieces:

- The **bot** (backend) — runs somewhere: your PC, a home server, or your cloud.
- The **dashboard** (client) — how you control it. 
- The **web dashboard** is bundled into every backend artifact: whichever scenario you pick, opening the bot's URL in a browser gives you the full dashboard with nothing extra to install or build. 
- A **standalone desktop app** with the exact
  same UI is optional on top.

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

Any scenario can also build the **standalone desktop dashboard app** for your OS by adding the
app flag (single dash on Windows — that's PowerShell's native flag style):

- **Linux / macOS:** `./deploy.sh desktop --app`
- **Windows (PowerShell):** `.\deploy.ps1 desktop -App`

Rule of thumb: start with **desktop**. Move to **docker** when you want Postgres-grade durability
or the bot lives on a server. **saas** is a **restricted option** — see its section below.

## The dashboard — web vs desktop app

| | Web dashboard | Desktop app |
|---|---|---|
| Build step | **none — always bundled** | `--app` (Linux/macOS) · `-App` (Windows) |
| Get it | open the bot's URL in a browser | installer in `app/composeApp/build/compose/binaries/main/` |
| Connects to | the bot that served it | any bot — saved connections + automatic LAN discovery (mDNS) |
| Best for | quick access, other devices, no install | daily driving, multiple bots |

They are the **same application** (Kotlin Multiplatform + Compose) — identical screens and
features. Requirements for `--app`: a JDK (21 recommended, [adoptium.net](https://adoptium.net));
on Windows the MSI additionally needs the [WiX Toolset 3.x](https://wixtoolset.org). The installer
is always built for the OS you run the script on.

**Windows first launch:** the desktop app opens no console window (a windowless launcher), which
Windows Defender Firewall treats like any other app making its first outbound connection — expect
one "Windows Defender Firewall has blocked some features of this app" prompt the first time it
reaches the bot over the network. Allow it (Private networks is enough for LAN bots); this is a
one-time OS prompt, not a bot/app misconfiguration.

**Desktop data & logs:** the app's per-user state (saved connections, the encrypted token vault,
window position/size, language/emoji preferences) lives under the OS-standard app-data directory —
`%LOCALAPPDATA%\NomNomzBot` on Windows, `~/Library/Application Support/NomNomzBot` on macOS,
`$XDG_DATA_HOME/NomNomzBot` (falling back to `~/.local/share/NomNomzBot`) on Linux. A rolling
diagnostics log lives at `<that directory>/logs/app.log`, capped at 2 MB (oldest lines are dropped
once it would grow past the cap) — check it first when the app fails to reach a bot or won't start.

## Scenario: desktop — `self_host_lite`

One self-contained file. No Docker, no database server — the bot keeps all its data (SQLite
database, encryption keys, logs) in **one per-user folder**: `%LOCALAPPDATA%\NomNomzBot` on
Windows, `~/.local/share/NomNomzBot` on Linux, `~/Library/Application Support/NomNomzBot` on
macOS. Back up that folder and you've backed up the bot; set `NOMNOMZ_DATA_DIR` to relocate it.

**Requirements:** the [.NET 10 SDK](https://dot.net) (build only — the produced binary needs nothing).

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
**http://localhost:5080** in a browser (or connect the desktop app; it finds LAN bots
automatically). **Update** by re-running the scenario and replacing the binary; your data stays in
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
- **Pull instead of build:** set `API_IMAGE=ghcr.io/nomercylabs/nomnomzbot:latest` in `.env`; the
  script switches to pulling the published image automatically.
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
`API_IMAGE` resolves to a registry ref (e.g. `ghcr.io/nomercylabs/nomnomzbot:latest`, the Proxmox
box's setup) it **pulls**; if it resolves to a bare local tag (the default
`nomnomzbot-api:local` that `docker compose up -d --build` produces, which was never pushed
anywhere and can't be pulled) it **builds** instead — pass `-Build` to force the build path
regardless of the configured tag. A real pull failure (network down, bad credentials, missing tag)
still aborts the script before anything is touched, the same as before. It then starts the idle
colour alongside the live one, waits for the idle colour to pass its own `/health/ready`, and only
then stops the old colour with a stop timeout long enough for it to drain in-flight requests
(SIGTERM → up to 30s → SIGKILL). If the idle colour never becomes ready, the script stops it,
leaves the old colour serving, and exits non-zero — there is never a moment with zero healthy
instances. Re-running the script is safe; it converges from whatever state it finds.

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

## Every combination at a glance

| Backend scenario | Web dashboard | Desktop app (Linux/macOS) | Desktop app (Windows) |
|---|---|---|---|
| desktop | `http://localhost:5080` | `./deploy.sh desktop --app` | `.\deploy.ps1 desktop -App` |
| docker | `http://localhost:5080` (or your URL) | `./deploy.sh docker --app` | `.\deploy.ps1 docker -App` |
| saas *(restricted — see above)* | `https://your-domain` | `./deploy.sh saas --app` | `.\deploy.ps1 saas -App` |

The `--app` build is independent of the backend scenario — you can also run it on a different
machine than the bot (build the app on your PC, point it at the server).
