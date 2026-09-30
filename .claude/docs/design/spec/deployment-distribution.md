# Deployment & Distribution — Operational Specification (rulebook)

How NomNomzBot is **packaged, distributed, and run** in both deployment models — and the two backend seams the
frontend leans on: **serving the wasmJs dashboard from the bot's own API host (P5)** and **advertising the bot on
the LAN for mDNS discovery (P6)**. This is a **rulebook + ops spec**: it defines artifacts, run sequences, the
SaaS scale-up path, KEK bootstrap, and the static-hosting / mDNS hosted services. It composes mechanisms already
owned elsewhere — it adds **no new domain events, no new schema columns, and (almost) no action keys**; the one
net-new dependency is a .NET mDNS library (§7).

Closes gaps **P4 / P5 / P6** (`_GAP-AUDIT.md`).

**Source of truth:** `2026-06-16-deployment-profile.md` (the profile axis + selection + first-run probe),
`spec/platform-conventions.md` §3.3 (`IDeploymentProfileService` — boot detect, host-capabilities probe),
`spec/scaling-qos.md` (stateless instances, `IRunOnceGuard`, vertical knobs, data tier),
`spec/rollout-updates.md` (deploy model, expand-contract migrations, auto-migrate-under-guard),
`spec/gdpr-crypto.md` §3.1/§7 (`IKeyVault` — `OsSecureStoreKeyVault` / `AzureKeyVaultKeyVault` KEK custody),
`spec/frontend.md` §3/§6 (what is served; the connection model; mDNS as a "backend concern"),
`2026-06-17-saas-architecture-flow.md` (the runtime topology this spec turns into infra).

## 0. What this spec owns (and does not)

- **Owns:** the **distribution artifacts** per profile (the self-host single-file binary, the published Docker
  image, the bundled root compose / `.env.example` / `deploy.*`); the **run + first-boot sequence** per profile;
  the **SaaS price-vs-income scale-up phasing** with trigger metrics; the **wasmJs dashboard + public-pages
  static-hosting wiring** (`UseStaticFiles` + SPA fallback, P5); the **self-host mDNS advertiser** hosted service
  (P6); the **first-run KEK bootstrap** sequence; and the **migrate-on-boot / health-gate** ordering as it sits in
  the host startup.
- **Does not own (references only):** the profile axis + adapter selection + host-capabilities probe
  (`deployment-profile` / `platform-conventions.md` §3.3 — `IDeploymentProfileService`); the deploy strategy +
  expand-contract migration discipline + `IRunOnceGuard`-guarded auto-migrate (`rollout-updates.md` §1–§2,
  `scaling-qos.md`); the `IKeyVault` adapters + envelope-encryption data plane (`gdpr-crypto.md` §3.1/§7); the
  stateless topology, fair scheduler, rate limiter, chat transport, data-tier scaling, and vertical knobs
  (`scaling-qos.md`); the frontend's `ConnectionProfile` / `LanDiscovery` / wasmJs single-origin model
  (`frontend.md` §6). This spec **consumes** those; it does not redefine them.

---

## 1. Distribution model — one codebase, three artifacts

The single `DeploymentProfile.Mode` axis (`deployment-profile`) has **three modes** — `self_host_lite`,
`self_host_full`, `saas` — and they map onto **two packaging strategies plus one operator playbook**:

| Mode | Packaging | Acquired as | Infra it needs |
|---|---|---|---|
| `self_host_lite` | **single self-contained per-OS binary** | built locally: `./deploy.sh desktop` / `.\deploy.ps1 desktop` (`dotnet publish`); there are no prebuilt release assets | none — SQLite file + in-process cache/bus + WebSocket EventSub |
| `self_host_full` | **Docker image + bundled compose** | `./deploy.sh docker` (local build, or pull `ghcr.io/nomercylabs/nomnomzbot` via `API_IMAGE`) + root `docker-compose.yml` | Docker; the compose brings Caddy + Postgres + Redis (+ Adminer) |
| `saas` **(RESTRICTED)** | **the same Docker image**, operated as a fleet | `./deploy.sh saas` (the operator's own deploy of the image) | Postgres + Redis + reverse proxy + the §4 phase infra |

> **RESTRICTED:** operating NomNomzBot as a hosted service for third parties is against the project license — that right is reserved to NoMercy Labs. Self-hosting your own bot (desktop or docker) is always free and unrestricted. Every surface that documents `saas` must carry this marker.


The **binary and the image are built from the identical solution** — nothing in `NomNomzBot.*` forks by artifact.
The mode is **auto-detected once at boot** (`IDeploymentProfileService.DetectAndPersistAsync`: Docker / Postgres /
Redis reachable ⇒ full, else lite; an explicit override wins — **two config keys are read, `Deployment:Mode` first,
then `App:DeploymentMode`**, i.e. env `Deployment__Mode` / `App__DeploymentMode`; underscores in the value are
ignored, so `self_host_full` = `SelfHostFull`; the compose maps `DEPLOYMENT_MODE` in `.env` onto `Deployment__Mode`), and every swappable adapter (DB / cache / bus
/ EventSub transport / executor / **token vault** / chat transport) is DI-selected from the resolved snapshot
(`scaling-qos.md` §9, `gdpr-crypto.md` §7). Distribution therefore **does not** decide behavior — it only decides
*how the bits arrive on the box*; the boot probe decides *which adapters wake up*.

### 1.1 Artifacts — described (not authored here)

These are the deliverables this spec governs. The actual files are produced by the build/release pipeline; this
spec is their contract, not their source.

- **Self-host `lite` binary — single-file, self-contained, per-OS, built locally.**
  Produced by `deploy.sh desktop` (`deploy.ps1 desktop` on Windows), which runs
  `dotnet publish server/src/NomNomzBot.Api -c Release -r <rid> --self-contained true`; the csproj sets
  `PublishSingleFile` for a self-contained RID publish and names the output `nomnomz`. The script derives the RID
  from `uname` (`linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`; `win-x64` on Windows) and prints where the file
  landed (`server/src/NomNomzBot.Api/bin/Release/net10.0/<rid>/publish/nomnomz`). One file, **no .NET runtime
  install required**, **no Docker** (the .NET SDK is needed only to *build* it). There is **no GitHub Release
  asset** and no CI job that publishes the binary. It carries the in-process scaling adapters
  (`InProcessRateLimiter`, `InProcessFairWorkScheduler`, in-memory `ICache`/`IEventBus`, WebSocket EventSub; chat
  sends via the per-platform chat provider behind `IChatProvider` — Twitch = `HelixChatProvider` (Helix Send Chat
  Message), IRC retired on every profile) and the embedded SQLite migration set. The wasmJs dashboard is **not**
  embedded in the exe: it is copied into a `wwwroot/` folder beside it by the `BundleWasmDashboard` publish target,
  which runs only when the publish passes `-p:BundleWasm=true` (§5.1). First run creates the per-user data folder
  (`SelfHostDataPaths`: `%LOCALAPPDATA%\NomNomzBot` / `~/.local/share/NomNomzBot` /
  `~/Library/Application Support/NomNomzBot`; `NOMNOMZ_DATA_DIR` overrides) holding `nomnomz.db` (SQLite), the
  `keys/` folder (KEK + generated JWT secret, §7), and logs; everything is one folder, trivially backed up.
- **Docker image — `full` + SaaS.**
  Built from the repo-root `Dockerfile` (CI passes context `.` with no `file:`; `server/Dockerfile` is a hand-build copy that ships nothing), published by the CI `build` job to **GHCR as `ghcr.io/nomercylabs/nomnomzbot`** with
  three kinds of tag: the branch name (`type=ref,event=branch`), the commit (`sha-<short>`), and `latest` (master
  only). There is **no semver tag**, and the build sets no `platforms`, so the image is single-arch (the runner's
  native amd64), not multi-arch. `GIT_SHA` is stamped into the image so `/health/version` reports which build is
  live. The **same image** runs `self_host_full` (auto-detects full because Postgres/Redis are reachable in the
  compose network) and `saas` (N replicas behind a proxy). It carries the Postgres + Redis + conduit/webhook
  adapters. **No KMS/Azure Key Vault code exists in the image** — KEK custody is `OsSecureStoreKeyVault` only (§7).
- **Bundled root `docker-compose.yml` — `full` quickstart, blue/green behind Caddy.**
  The one-command full stack: `caddy` (`caddy:2-alpine`, owns the documented host port `${API_HTTP_PORT:-5080}`) +
  two API services `api-blue` and `api-green` (the same image; exactly one runs in steady state, neither publishes a
  host port) + `postgres:16-alpine` + `redis:7-alpine` + `adminer` (DB browser, loopback-only) on a private network,
  with healthchecks and `depends_on: service_healthy` gating and named volumes. The root `Caddyfile` load-balances
  across both colours with `lb_policy first` and an **active** `/health/ready` poll every 5 s (`health_status 200`),
  plus `lb_try_duration` so a colour that just got SIGTERM is retried on the other upstream instead of returning a
  502. The API image is `${API_IMAGE:-nomnomzbot-api:local}`: unset → the stack **builds locally**
  (`docker compose up -d --build`); set to a registry ref (e.g. `ghcr.io/nomercylabs/nomnomzbot:latest`) → it
  **pulls** the published image. Every secret is read from `.env`. TLS terminates at the operator's own proxy, not in
  this stack.
- **`scripts/switchover.ps1` — the zero-downtime update.**
  Re-derives the live colour from `docker ps` (never trusts a stored value), acquires the new image for the **idle**
  colour (pull when `API_IMAGE` is a registry ref, local build when it is the bare `nomnomzbot-api:local` tag;
  `-Build` forces the build path), starts the idle colour beside the live one, waits for the idle colour's own
  `/health/ready`, and only then stops the old colour with a stop timeout long enough to drain in-flight requests
  (SIGTERM → up to 30 s → SIGKILL). If the idle colour never becomes ready, the script stops it, leaves the old
  colour serving, and exits non-zero — there is never a moment with zero healthy instances. It converges on re-run.
  It never edits the Caddyfile or reloads Caddy: Caddy's health poller sees both transitions. A remote host is
  targeted through `NOMNOMZ_DEPLOY_SSH` / `NOMNOMZ_DEPLOY_KEY` / `NOMNOMZ_DEPLOY_DIR`. No auto-updater
  (`watchtower`) may point at `api-blue`/`api-green` — its stop-then-start is exactly the downtime this removes.
- **`.env.example` — the secrets/config template.**
  A commented template enumerating every variable from `CLAUDE.md`'s env table: required Twitch credentials
  (`TWITCH_CLIENT_ID`/`_SECRET`/`_BOT_USERNAME`), generated secrets (`JWT_SECRET`, `ENCRYPTION_KEY` with the
  `openssl rand -base64 32` hint), DB/Redis settings, optional integration keys (Spotify/Discord/YouTube/TTS),
  `DEPLOYMENT_MODE`, `API_IMAGE`, and ports. Copied to `.env` by the deploy script. **Not committed as `.env`** —
  only `.env.example`.
- **`deploy.sh` / `deploy.ps1` — one script, three scenarios (bash for Linux/macOS, PowerShell for Windows).**
  Idempotent; `--app` (`-App` on Windows) additionally builds the standalone desktop dashboard installer. No
  argument prints the guide.
  - `desktop` (`self_host_lite`) — the local `dotnet publish` above; needs the .NET 10 SDK, no Docker.
  - `docker` (`self_host_full`) — needs Docker + Compose v2. Creates `.env` from `.env.example` if absent,
    **generates `JWT_SECRET`, `ENCRYPTION_KEY`, `POSTGRES_PASSWORD`**, optionally prompts for the Twitch credentials
    (Enter = enter them later in the setup wizard), forces `DEPLOYMENT_MODE=self_host_full`, then `docker compose pull api`
    + `up -d --no-build` when `API_IMAGE` is a registry ref, otherwise `docker compose up -d --build`; polls
    `/health/ready` (up to 3 min) and prints the dashboard, health and Adminer URLs.
  - `saas` (**RESTRICTED** — operating NomNomzBot as a hosted service for third parties is against the project license — that right is reserved to NoMercy Labs. Self-hosting your own bot (desktop or docker) is always free and unrestricted. Every surface that documents `saas` must carry this marker.) — same stack, but fail-closed guards first: `API_BASE_URL` must
    be a public HTTPS origin (not localhost), `JWT_SECRET` and `ENCRYPTION_KEY` must not be the dev defaults; then
    it sets `DEPLOYMENT_MODE=saas` and brings the stack up. `TRUSTED_PROXY_NETWORKS` is set when the proxy reaches the
    API over a Docker network.
  These are convenience over the raw compose, not a required path; updates of a running stack go through
  `switchover.ps1`.

> A streamer's decision tree: **want zero dependencies / a NUC / a single file** → build the `lite` binary (`deploy.sh desktop`).
> **Want Postgres-grade durability / Adminer / room to grow** → `deploy.sh` + the `full` compose. **Running a
> service for others** → operate the image as SaaS (§4). The product never forces Docker on a hobbyist, and never
> forces SQLite on an operator.

---

## 2. Run + first-boot sequence — per profile

All three modes run the **same `NomNomzBot.Api` host** and the **same ordered boot pipeline**; they differ only in
which adapters that pipeline resolves. The ordering below is binding (fail-closed boot — `platform-conventions.md`
§3.3 `Current` throws if read before resolution).

**Ordered boot pipeline (every mode):**

1. **Resolve the profile.** `IDeploymentProfileService.DetectAndPersistAsync` probes infra (Docker/Postgres/Redis
   reachable?), honors the override (`Deployment__Mode`, then `App__DeploymentMode`), **probes host capabilities** (CPU cores + available memory) to size
   the `Scaling:*` knobs unless overridden (`platform-conventions.md` §3.3), upserts the single-row
   `DeploymentProfile` (P.12), emits `DeploymentProfileResolvedEvent`. Runs **before** the host starts.
2. **Bind adapters.** `AddDeploymentAdapters(services, snapshot)` selects DB / cache / bus / EventSub transport /
   executor / **`IKeyVault`** / `IChatProvider` / `IRateLimiter` / `IFairWorkScheduler` / `IRunOnceGuard` from the
   resolved snapshot (`scaling-qos.md` §9, `gdpr-crypto.md` §7).
3. **Bootstrap the KEK** (§6) — first run generates the profile's root KEK into its vault; subsequent runs load it.
4. **Migrate, once.** `IHost.MigrateAsync()` against the **provider's** migration set (Postgres *or* SQLite —
   `rollout-updates.md` §2), guarded by **`IRunOnceGuard`** (pg advisory lock on full/SaaS so exactly one instance
   migrates; no-op on lite). Runs **before** the host takes traffic (§8).
5. **Seed reference data** (TTS voices, permission presets, feature-flag rows) via the ordered `ISeeder` scan
   (`backend-structure.md` §4–5) — idempotent, also guarded so it runs once cluster-wide.
6. **Start hosted services** — the auto-scanned hosted workers, the EventSub transport
   (the WebSocket loop by default; the conduit provisioner runs **only** when `EventSub:Conduits:Enabled` is set —
   it is **off by default**, and when on it runs under `IRunOnceGuard`), and — **on self-host only** — the
   **mDNS advertiser** (§5.3 wiring lives at §5; advertiser at the bottom of this list because it announces the
   listening port). On SaaS the advertiser is a no-op.
7. **Map the pipeline** — REST controllers, SignalR hubs, **`UseStaticFiles` + the SPA fallback** for the dashboard
   and public pages (§5), `/health/*` endpoints.
8. **Go ready.** `/health/ready` flips green only when DB is reachable, migrations are applied, the cache/bus
   adapters resolved, and the EventSub transport is up (`rollout-updates.md` §6).

**Per-mode collapse of that pipeline:**

- **`self_host_lite` (binary).** `./nomnomz` → step 1 resolves *lite* → SQLite opened/created in the per-user data folder (`SelfHostDataPaths`) →
  KEK loaded/created per §7 (DPAPI on Windows, `Encryption:Key` elsewhere) → SQLite migrations applied (no guard — single process) → seed →
  WebSocket EventSub connects + reconnects with backoff (`twitch-eventsub.md`) → **mDNS advertiser announces
  `_nomnomz._tcp`** → static files + SPA served → ready. A restart is a clean stop→start (a brief gap is
  acceptable — `rollout-updates.md` §1).
- **`self_host_full` (compose).** `deploy.sh docker` (or `docker compose up -d`) brings Postgres + Redis up first
  (healthchecked), then the live colour (`api-blue`) and `caddy`: step 1 resolves *full* → Postgres + Redis adapters
  → KEK derived from `ENCRYPTION_KEY` (`Encryption__Key`, HKDF — §7) → Postgres migrations applied under the
  advisory-lock guard → seed → WebSocket EventSub (conduit only if opted in) → mDNS advertiser (still self-host) →
  serve → ready; Caddy routes to the colour only once its `/health/ready` passes. Later updates run
  `scripts/switchover.ps1` (§8): the idle colour boots against the already-migrated schema and takes over while the
  old one drains. Adminer is available for DB inspection.
- **`saas` (fleet) — RESTRICTED (NoMercy Labs only; see §1).** Each api replica runs the identical pipeline; the **guarded** steps (migrate, seed,
  conduit-provision) execute on exactly one replica (`IRunOnceGuard` pg advisory lock) while the others wait, then
  all boot against the migrated schema and join the proxy pool **only after `/health/ready`** (`rollout-updates.md`
  §1 rolling deploy). The mDNS advertiser is a **no-op** (cloud has no LAN to discover on). Static-file serving is
  on but, in the multi-replica/edge topology, the dashboard is typically fronted by the reverse proxy / CDN (§5.2).

---

## 3. SaaS infrastructure baseline (Phase 1 concrete shape)

SaaS **starts on Docker for cost** — no Kubernetes, no managed services, no premium edge — and grows only when a
trigger fires (§4). The Phase-1 baseline that the §2 `saas` sequence runs on:

> **RESTRICTED:** operating NomNomzBot as a hosted service for third parties is against the project license — that right is reserved to NoMercy Labs. Self-hosting your own bot (desktop or docker) is always free and unrestricted. Every surface that documents `saas` must carry this marker.


- **Reverse proxy** — **Caddy** (automatic HTTPS, the default) or **nginx**, terminating TLS and load-balancing
  across the api replicas **with no sticky sessions** (the instances are stateless — `scaling-qos.md` D2 / the SaaS
  topology). It **drains** an instance on rollout (stop new requests, let in-flight finish) and gates a replacement
  on `/health/ready` (`rollout-updates.md` §1).
- **N stateless api replicas** — the GHCR image, with `WorkerCount`/pool sizes from the `Scaling:*` knobs sized up
  per node (`scaling-qos.md` §9). The shipped compose runs one live colour at a time (blue/green, §1.1); further
  replicas use the same image and migrate safely on their own (exactly one takes the lock). Any replica serves any tenant; unique state lives in Postgres/Redis, never in-process.
- **Self-managed Postgres + Redis** — a Postgres container/VM (primary; a read replica added at Phase 2) and a
  Redis container/VM (cache + buckets + fair-scheduler counters + pub-sub — `scaling-qos.md` §7). On a single VM
  these are compose services beside the api; the moment they move off-box they become §4's managed services.
- **The cluster-singletons** — the **conduit-provisioner** and the retention/expiry sweeps acquire `IRunOnceGuard`
  (pg `try_advisory_lock`) so exactly one replica owns each, regardless of replica count (`scaling-qos.md` §1).
- **Secrets** — `JWT_SECRET`, `ENCRYPTION_KEY`, Twitch + integration creds delivered as environment/secret
  files to each replica (Phase 1). The KEK is derived from `ENCRYPTION_KEY` (HKDF, §7); a cloud-KMS adapter is **not
  built** (§4 Phase 3 / §7).

This is deliberately the **cheapest topology that is correct**: one VM can run the proxy + N small api replicas +
Postgres + Redis with `docker compose`, and the design's statelessness means scaling out is *adding replicas*, not
re-architecting.

---

## 4. SaaS scale-up path — price-vs-income phasing with triggers

> **RESTRICTED:** operating NomNomzBot as a hosted service for third parties is against the project license — that right is reserved to NoMercy Labs. Self-hosting your own bot (desktop or docker) is always free and unrestricted. Every surface that documents `saas` must carry this marker.

The fleet grows **only as revenue justifies it**, each phase tied to a concrete trigger metric, so infra cost
tracks income rather than leading it. The statelessness (`scaling-qos.md`) makes every step **additive** — no
rewrite, just more/managed boxes.

| Phase | Topology | What changes | Cost shape | Promote when (any trigger) |
|---|---|---|---|---|
| **1 — Single VM, vertical** | one VM: Caddy/nginx + N api replicas + Postgres + Redis, all `docker compose` | scale up = bigger VM + more replicas + bigger `Scaling:*` pools (`scaling-qos.md` §9) | one VM bill; near-zero fixed cost | instance **CPU saturation sustained ≥ 70%** at peak, **or** the box can't hold Postgres + Redis + replicas comfortably, **or** **tenant count ≳ 50–100 active channels**, **or** the backpressure controller hits **Amber regularly** (`scaling-qos.md` §5) |
| **2 — Multi-VM compose + managed data** | 2–3 api VMs behind the proxy; **Postgres → managed (RDS/Cloud SQL/managed PG)** with a **read replica** (`IReadDbContext` → replica, `scaling-qos.md` §7); **Redis → managed**; object storage for exports/artifacts | data tier leaves the app VMs; app VMs become pure stateless compute; journal partitioning + read-replica reads switch on | a few VM bills + managed Postgres/Redis (the first real step-up) | **journal write throughput** strains a single self-managed Postgres (replication lag / IOPS ceiling), **or** **tenant count ≳ 500**, **or** **MRR clears the managed-data bill with margin**, **or** a single VM's failure is now an unacceptable blast radius (need HA Postgres) |
| **3 — Kubernetes + managed services + autoscaling** | **K8s (Helm chart)** running the api `Deployment` with an **HPA** (scale replicas on CPU / queue depth), managed Postgres (multi-AZ + replicas) + managed Redis, managed/edge TLS + CDN for static assets; the singletons become a `Deployment` of 1 (still `IRunOnceGuard`-guarded as belt-and-suspenders) | orchestration + autoscaling + HA + edge; **KEK → cloud KMS** (envelope; **not built yet** — §7) | K8s control plane + managed everything (highest fixed cost — only past clear profitability) | **HPA-worthy load** — replica count varies enough hour-to-hour that manual VM scaling wastes money or risks under-provisioning, **or** **tenant count ≳ several thousand**, **or** **revenue comfortably funds the managed/orchestrated bill**, **or** an uptime/SLA commitment now requires multi-AZ HA + zero-downtime autoscaling |

**Reading the table:** every promote-trigger is *a real signal you can watch* — CPU%, the Amber/Red pressure state
(`scaling-qos.md` §5, published on `SystemPressureChangedEvent`), active-tenant count, Postgres write throughput /
replication lag, and **MRR vs the next phase's bill**. The rule is **don't promote on vanity** — promote when a
metric *or* the revenue headroom says the current phase is the bottleneck. Each phase **inherits** the prior phase's
code unchanged (same image, same boot pipeline); only the *placement* of Postgres/Redis, the *orchestrator*, and the
*KEK custody* (§6) change.

---

## 5. Serving the wasmJs dashboard + public web pages (P5)

The bot's **own API host serves the frontend** — both the Compose/Wasm dashboard (the full app, `frontend.md` §3)
and the lightweight public pages (song-request, OBS overlays/widgets, OAuth-callback landing). This is what makes
`frontend.md` §6's "the web build is served first-party by its own bot → single origin" true on the backend side.

### 5.1 Static hosting + SPA fallback

- **Build inputs.** The wasmJs dashboard is built (`composeApp` `wasmJsBrowserDistribution`, `frontend.md` §1) to a
  static bundle (`index.html` + `.wasm` + JS + `composeResources`) and lands in the api's `wwwroot/`: the Docker
  image copies it there from its `wasm-build` stage; the `lite` publish copies it into `<publish>/wwwroot` beside the
  exe when run with `-p:BundleWasm=true`. (`wwwroot/` is gitignored build output; the host creates it empty on a
  clean checkout so an API-only run still boots.) There is **no `web/` folder** — the public pages are served by
  API endpoints, not by a separate static tree. The bot therefore serves the dashboard with **zero extra hosting**.
- **Wiring (in the host pipeline, §2 step 7).** `UseStaticFiles()` (with the explicit `.wasm` MIME mapping) serves
  the dashboard bundle. A **SPA fallback** (`MapFallbackToFile("index.html")`, served `no-store`) maps any **non-API, non-hub, non-health, non-static** GET to the
  dashboard's `index.html` (so client-side routes like `/commands` deep-link and survive refresh — `frontend.md`
  §5 maps routes to browser history). The fallback is **scoped**: requests under `/api/`, `/hubs/`, `/health`, and
  the public-page roots (`/songs`, `/overlay`, `/oauth`) are **excluded** and never rewritten to the SPA shell.
- **Single-origin invariant.** The served dashboard talks **only to the origin that served it**
  (`window.location.origin` — `frontend.md` §6): same host, same scheme, so REST (`/api/v1/*`) and SignalR
  (`/hubs/*?access_token=`) need **no CORS** for the served build. The CORS allow-list (`appsettings` `Cors:Origins`)
  exists only for **other** origins (a desktop build, a different bot's dashboard) — never for the first-party
  served one.
- **Public pages stay lightweight.** The song-request page, overlay/widget browser-source pages, and the OAuth
  landing are plain assets/endpoints — **not** the Compose/Wasm app (`frontend.md` §4 scopes them out of the
  dashboard). They are served from the same host so OBS and viewers hit `https://<bot>/overlay/...` /
  `https://<bot>/songs` with no app install.

### 5.2 Edge/CDN on SaaS (Phase 2–3)

On `saas`, the static bundle is identical, but as the fleet grows the dashboard `.wasm`/JS are fronted by the
reverse proxy and, at Phase 3, a **CDN/edge cache** (§4) — the api replicas stop being the bandwidth path for cold
static assets. The **SPA fallback + single-origin contract is unchanged**; only *who hands back the cached bytes*
moves. Self-host always serves them straight from the api host (no CDN).

### 5.3 The wasmJs async-load contract (served side)

Because wasmJs resolves resources asynchronously (`frontend.md` §7), the served `index.html` is the **stable
fallback target** for every client route — the host must never 404 a deep link, or the app can't boot. The SPA
fallback (§5.1) is exactly what guarantees that.

---

## 6. LAN discovery — smart listen-port handling + `_nomnomz._tcp` mDNS (P6)

Self-host binds a **loopback** listener the desktop / web dashboard talks to, then advertises it on the LAN so the
native app finds it with zero configuration. Two intertwined backend concerns: **how the port is chosen and kept
stable (§6.1)** and **how the bot is announced (§6.2)**. **Self-host does both; SaaS does neither** (it binds a
wildcard behind the operator's proxy and has no LAN to discover on).

### 6.1 Smart listen-port handling + OAuth port lock

A fixed default port can collide (another app, or a stale copy of the bot), so on **self-host only** the host
resolves its listen port at boot **before Kestrel binds** — `ListenPortBootstrap` runs pre-host so it can rewrite
`Urls` — via `ListenPortResolver`:

- **First boot (nothing locked yet).** Prefer the configured port (`Urls`, default `5080`). **Free** → take it.
  Held by **a stale duplicate of the bot itself** (the owning PID's process name equals ours) → **kill that one**
  (one canonical bot) and take the port. Held by **an unrelated application** → bind a free **ephemeral** port
  instead. Whatever it binds is then **locked** — persisted to `<data>/listen-port`
  (`SelfHostDataPaths.ListenPortFile`).
- **Kill safety.** A process is killed **only** when its name positively matches our own exe. Any uncertainty —
  unknown/unreadable owner, unsupported OS, or a kill that doesn't free the port in time — is treated as "another
  app": the bot steps aside (first boot) or aborts (locked), and **never kills on a guess**.
- **Every later boot (locked).** The bot is handed the locked port and **must keep it**: the OAuth redirect URLs
  registered with Twitch / Spotify / Discord / YouTube are tied to it (`{App:BaseUrl}/api/v1/auth/.../callback`), so
  moving would break every saved login. It still **reclaims** the locked port from a stale duplicate of itself, but
  if an **unrelated** app holds it the bot does **not** move to a different port — it **aborts boot** with a clear,
  actionable message (`ListenPortLockedException`, surfaced in a dialog by the windowless single binary) telling the
  operator to free that port.
- **`App:BaseUrl` follows the bound port.** After resolution the bootstrap rewrites `Urls` to the resolved port
  **and** sets `App:BaseUrl` to `http://localhost:<port>` so the computed OAuth callback matches the port Kestrel
  actually bound — **unless** an explicit **external** `App:BaseUrl` (a domain / tunnel the operator fronts the bot
  with) is configured, which wins (it owns the public URL; the port lock keeps that tunnel's local target stable).
- **The UI doesn't care which number.** §6.2's mDNS advertises the **actual** bound port, so the native app
  discovers the bot wherever it landed — the locked port only has to be **stable**, not a specific value.

### 6.2 mDNS advertising — `_nomnomz._tcp` on the LAN

The backend counterpart to `frontend.md` §6's `LanDiscovery` (the native app browses `_nomnomz._tcp`; web is a
no-op). **Self-host advertises; SaaS does not.**

- **`MdnsAdvertiserHostedService`** — an `IHostedService` (Infrastructure) that, on self-host only, registers an
  mDNS/DNS-SD service of type **`_nomnomz._tcp`** on the local link, advertising the **bot's display name**
  (instance name), the **host/IP**, and the **resolved (locked) API port** (§6.1), with a TXT record carrying the **base path/scheme** and a
  short **`instance` id** (the `DeploymentProfile.InstanceId`, P.12) so the native switcher can de-dupe and label
  the discovered bot. It re-announces on network-interface change and de-registers cleanly on shutdown.
- **Profile gate.** Registered **only** when `DeploymentProfileSnapshot.Mode` is `self_host_lite` /
  `self_host_full`; on `saas` the service is **not added** (a cloud bot has no LAN to be discovered on — there is no
  no-op shim to run, it simply isn't registered). This mirrors the `LanDiscovery` web-side no-op (`frontend.md` §6).
- **What the native app does with it.** The discovered service surfaces in the desktop connection switcher as a
  `Discovered` `ConnectionProfile` (`frontend.md` §6) — `displayName` from the advertised instance name, `baseUrl`
  from host + port + scheme — giving the **zero-friction LAN onboarding** the frontend promises (find your bot on
  the network, click, connect). The web build ignores it (single-origin).
- **Library.** A managed .NET mDNS/DNS-SD library — **Makaretu.Dns** (`Makaretu.Dns.Multicast`) — does the multicast
  announce/respond. It is the **one net-new dependency** this spec introduces; it loads **only in the self-host
  advertiser path** (not referenced on SaaS, and present in the `lite` binary because self-host is its whole reason
  to exist).

---

## 7. KEK bootstrap & secret custody — per profile

Secret/KEK custody sits behind the `IKeyVault` seam (`gdpr-crypto.md` §7). The design intends it to be
profile-selected by `DeploymentProfile.TokenVault`; **as built only one adapter exists and it is registered for every
mode** (below). This spec owns only the **first-run bootstrap sequence** that puts a root KEK in the right place; the
wrap/unwrap data plane and the adapters are owned by `gdpr-crypto.md`.

**As built: one adapter for every mode.** `OsSecureStoreKeyVault` (`Provider = local_aes`) is the **only** `IKeyVault`,
registered unconditionally as a singleton. There is no `kms_envelope` branch: `AzureKeyVaultKeyVault` does not exist,
no Azure SDK is referenced, and no `TokenVault`-based switch selects an adapter. The root KEK (32 bytes) is resolved
lazily on first use, in this order:

1. **`Encryption:Key` is set** (env `ENCRYPTION_KEY` → `Encryption__Key`; the compose and `deploy.sh docker`/`saas`
   generate/require it) → the KEK is **derived deterministically** from it with HKDF-SHA256 (info = the wrap-context
   string). This is the path for Docker/Linux, CI, dev and headless hosts, and the only KEK path off Windows. The
   `kekReference` recorded with each wrapped DEK is `config:hkdf`. `StartupSecretGuard` refuses the bundled dev key
   outside Development.
2. **No key set, Windows** → `DpapiKekStore`: a random KEK is generated once and stored **DPAPI-protected, current-user
   scope**, with fixed entropy, at `<data>/keys/root-kek.dpapi` (`SelfHostDataPaths.KeysDirectory`); later boots
   unprotect the same file. `kekReference` = `os-secure-store:dpapi`. This is what a Windows `lite` binary uses out
   of the box.
3. **No key set, not Windows** → boot fails with `InvalidOperationException` ("No KEK custody available … macOS
   Keychain / Linux libsecret backends are pending"). There is no encrypted-file (0600) or Keychain/libsecret
   fallback today. **The missing platforms — macOS Keychain, Linux libsecret, and a headless file fallback — are
   tracked as `S-KEK-LINUX-MAC`.** Until it lands, a Linux/macOS `lite` binary needs `ENCRYPTION_KEY` set.

The wrap is AES-256-GCM under a fixed wrap-context AAD; the wrapped DEKs and all ciphertext live in the DB, so the
root-KEK custody and the AES-256-GCM-under-DEK data plane compose into envelope encryption (`gdpr-crypto.md` §7).

The JWT signing secret follows a sibling first-run rule (`SelfHostSecretStore`): on self-host, when no strong
`Jwt:Secret` is configured, a 384-bit secret is generated once and persisted beside the KEK (`keys/jwt-secret.bin`,
DPAPI on Windows, a user-only file elsewhere). SaaS must supply its own.

**Not built yet (do not describe as shipped):** the SaaS Phase 3 **cloud-KMS** custody (`kms_envelope` via
`AzureKeyVaultKeyVault`, EU Managed-HSM). It is a future adapter behind the same `IKeyVault` seam; when it lands the
upgrade is a **KEK re-wrap of existing DEKs**, not a data re-encryption, because only root-KEK custody changes.
SaaS-on-VMs today therefore uses the same path as self-host: `ENCRYPTION_KEY` delivered as a secret.

**First-run KEK bootstrap.** The KEK is resolved (§2 step 3) **before** migrations/seeding because the token-vault
and any encrypted seed data depend on it. On the DPAPI path the first boot **generates** the KEK
(`RandomNumberGenerator`, 32 bytes) and writes it sealed; subsequent boots **load** it. On the `Encryption:Key` path
nothing is generated or stored: the KEK is re-derived from the key on every boot. **Rotating or losing the KEK
(or the `ENCRYPTION_KEY`) invalidates every DB-stored DEK** (and therefore the OAuth tokens they
protect) — the documented re-auth consequence (`CLAUDE.md` "ENCRYPTION_KEY rotation requires bot re-auth"); KEK
rotation cadence itself is owned by the auth/persistence slices (`gdpr-crypto.md` §7 note), not here.

---

## 8. Migrate-on-boot & health — where they sit in the host

This spec doesn't redefine the migration discipline (`rollout-updates.md` §2 owns expand-contract + the two
provider sets + auto-migrate-under-`IRunOnceGuard`); it pins **where those steps sit in the run sequence** so the
distribution story is complete.

- **Auto-migrate on startup, under the guard.** Migrations run at §2 step 4 — **after** profile resolution + KEK
  bootstrap, **before** the host takes traffic. The **provider set is the resolved one** (Postgres for full/SaaS,
  SQLite for lite). On full/SaaS, `IRunOnceGuard` (pg advisory lock) ensures **exactly one** instance migrates while
  the others wait; on lite it's a no-op (one process). Because every migration is backward-compatible
  (`rollout-updates.md` §2), old SaaS instances keep serving across a roll.
- **Health gates traffic.** `/health/live` (liveness — process up) and `/health/ready` (readiness — DB reachable,
  migrations applied, cache/bus resolved, EventSub transport up) are the **same endpoints** the compose healthchecks
  poll, the deploy scripts wait on, and the SaaS proxy gates rollout on (`rollout-updates.md` §6). A rolled/restarted
  instance joins the pool (SaaS) or signals "ready" (self-host) **only** when `/health/ready` is green — the single
  readiness contract across all three modes.
- **Desktop restart, compose blue/green.** The desktop (`lite`) binary is **stop → start → auto-migrate → ready**
  (a brief gap is fine). The compose stack (`self_host_full`, and the single-node `saas` shape) updates
  **blue/green behind Caddy** through `scripts/switchover.ps1` (§1.1): start the idle colour, wait for its own
  `/health/ready`, then stop the old colour with a drain timeout; Caddy's active `/health/ready` poll moves traffic
  with no port change and no Caddyfile reload, and a colour that never turns ready is stopped while the old one
  keeps serving. A multi-replica SaaS fleet uses the same drain + readiness-gated roll over the stateless pool
  (`rollout-updates.md` §1).

---

## 9. Dependencies

| Use | Package / API | Party | Notes |
|---|---|---|---|
| mDNS / DNS-SD advertise (`_nomnomz._tcp`) | **`Makaretu.Dns.Multicast`** (Makaretu.Dns) | 3rd (MIT) | **NEW** — the only net-new dependency; loaded **only** in the self-host mDNS advertiser path (§6); present in the `lite` binary |
| Single-file self-contained publish | `dotnet publish -p:PublishSingleFile -r <rid> --self-contained` | 1st (.NET 10 SDK) | the `lite` binary; no NuGet add |
| Static hosting + SPA fallback | `Microsoft.AspNetCore.StaticFiles` / `MapFallbackToFile` | 2nd (in-box ASP.NET Core) | §5; in-box, no add |
| KEK custody (every mode, as built) | `OsSecureStoreKeyVault` (`Encryption:Key` HKDF, or Windows DPAPI) | owned by `gdpr-crypto.md` §7 | referenced, not re-declared; Keychain/libsecret/file = `S-KEK-LINUX-MAC` |
| KEK custody (SaaS Phase 3) | `AzureKeyVaultKeyVault` (`Azure.Security.KeyVault.Keys`) | owned by `gdpr-crypto.md` §7 | **not built** — no package reference exists |
| Blue/green ingress | `caddy:2-alpine` + root `Caddyfile` + `scripts/switchover.ps1` | 3rd (Apache-2.0) | §1.1; the compose's front door |
| Container image base + compose services | `Dockerfile`, `postgres:16-alpine`, `redis:7-alpine`, `adminer` | 3rd (existing) | the `full`/SaaS image + bundled compose (§1.1) |
| Profile detect + host-capabilities probe | `IDeploymentProfileService` | owned by `platform-conventions.md` §3.3 | referenced |
| Cluster-singleton guard (migrate/seed/provision) | `IRunOnceGuard` (pg advisory lock / no-op) | owned by `scaling-qos.md` | referenced |

**No new schema, no new domain events, no new action keys.** The mDNS lib is the sole net-new dependency; every
other mechanism (profile resolution, adapter selection, migrations, KEK adapters, static hosting, health) is owned
by an existing spec and only **composed** here.

---

## 10. Decisions (resolved)

1. **Three artifacts, two packaging strategies, one codebase.** `self_host_lite` = a **single self-contained per-OS
   binary** built locally by `deploy.sh desktop` (`./nomnomz`, no Docker, no runtime install, no release asset);
   `self_host_full` = the **Docker image** (built locally, or `ghcr.io/nomercylabs/nomnomzbot` via `API_IMAGE`) +
   bundled root `docker-compose.yml` (caddy + api-blue/api-green + postgres + redis + adminer) + `.env.example` +
   `deploy.sh`/`deploy.ps1` + `scripts/switchover.ps1`; `saas` (**RESTRICTED** — NoMercy Labs only) = the **same
   image** operated as a stateless fleet. Mode is auto-detected at boot; the overrides are `Deployment:Mode` (wins)
   and `App:DeploymentMode`.
2. **SaaS starts cheap on Docker and scales on triggers.** Phase 1 single VM (proxy + N stateless replicas +
   self-managed Postgres+Redis, the opt-in conduit provisioner an `IRunOnceGuard` singleton) → Phase 2 multi-VM + **managed**
   Postgres/Redis + read replica → Phase 3 **Kubernetes (Helm) + HPA + managed services + edge**. Each promotion is
   tied to a watchable trigger (instance CPU saturation, backpressure Amber/Red, tenant count, journal write
   throughput, **MRR vs the phase's bill**) — promote on a real signal, never on vanity. Every phase inherits the
   prior code unchanged.
3. **The bot serves its own frontend (P5).** `UseStaticFiles` + a **scoped SPA fallback** (excluding `/api`,
   `/hubs`, `/health`, public-page roots) serve the wasmJs dashboard (from `wwwroot/`) from the api host; the
   lightweight public pages are API endpoints. The served dashboard is **single-origin** (talks only to `window.location.origin`, no
   CORS for the first-party build). SaaS fronts the static bundle with the proxy/CDN at scale; the contract is
   unchanged.
4. **Self-host advertises on the LAN (P6).** `MdnsAdvertiserHostedService` announces **`_nomnomz._tcp`** (name +
   host + port + `instance`-id TXT) via **Makaretu.Dns** — registered **only** on self-host, **not added** on SaaS.
   It feeds the native app's `Discovered` connection profiles (`frontend.md` §6); web ignores it.
5. **KEK custody: one adapter today, KMS later.** Every mode uses `local_aes` via `OsSecureStoreKeyVault`: the KEK
   is HKDF-derived from `Encryption:Key` when set, else Windows DPAPI (current user, `keys/root-kek.dpapi`), else
   boot fails (macOS/Linux OS-store backends = `S-KEK-LINUX-MAC`). SaaS **may later add `kms_envelope`** (cloud KMS,
   not built) at Phase 3 — a KEK re-wrap, not a data re-encryption, because envelope encryption composes.
6. **Migrate-on-boot + one readiness contract.** Auto-migrate runs after KEK bootstrap and before traffic, against
   the resolved provider's set, under `IRunOnceGuard` (one migrator on full/SaaS, no-op on lite). `/health/ready`
   (DB + migrations + cache/bus + EventSub up) is the single gate the compose healthchecks, deploy scripts, and the
   SaaS rolling proxy all rely on.
7. **Ownership split.** This spec owns the *artifacts, run/first-boot sequences, the SaaS phasing, the static-hosting
   + mDNS hosted services, and the KEK-bootstrap ordering*. The profile axis + probe, the migration discipline +
   guard, the `IKeyVault` adapters + data plane, the stateless topology, and the frontend connection model are owned
   by their existing specs and only referenced here. **No new schema, events, or action keys; one net-new dependency
   (Makaretu.Dns).**
