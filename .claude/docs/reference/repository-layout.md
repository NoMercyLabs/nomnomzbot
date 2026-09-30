# Repository Layout

```
nomnomzbot/
├── server/                  # Backend — .NET 10; SQLite (self_host_lite) or PostgreSQL + Redis (full/saas)
│   ├── src/
│   │   ├── NomNomzBot.Domain/          # Entities, domain events, value objects, interfaces
│   │   ├── NomNomzBot.Application/     # Use cases, services, pipeline engine, IEventBus
│   │   ├── NomNomzBot.Infrastructure/  # EF Core, Twitch services, EventSub
│   │   │   └── Platform/Persistence/Migrations/  # PostgreSQL migration set
│   │   ├── NomNomzBot.Migrations.Sqlite/ # SQLite migration set (self_host_lite) — always kept in step with Postgres
│   │   └── NomNomzBot.Api/             # ASP.NET Core host, controllers, middleware
│   │       └── Hubs/                   #   SignalR hubs
│   ├── tests/
│   │   ├── NomNomzBot.Domain.Tests/
│   │   ├── NomNomzBot.Application.Tests/
│   │   ├── NomNomzBot.Infrastructure.Tests/
│   │   ├── NomNomzBot.Api.Tests/
│   │   └── NomNomzBot.E2E.Tests/       # Playwright.NET end-to-end tests
│   ├── i18n/                # Backend i18n key manifest (schema-i18n-keys.manifest.json)
│   ├── openapi/             # Committed OpenAPI snapshot v1.json (contract drift guard)
│   ├── Dockerfile           # NOT the shipped image — the root Dockerfile is; this one builds the API alone by hand
│   └── docker-compose.yml, docker-compose.dev.yml   # server-local compose files (not the deployed stack)
├── app/                     # Frontend — Kotlin Multiplatform + Compose Multiplatform (desktop + web/Wasm)
│   └── composeApp/          #   src/commonMain: App.kt, core/ (network client, i18n, design system),
│                            #   feature/<domain>/ (screens + state); i18n resources in
│                            #   composeResources/values/strings.xml (en) + values-nl/ (nl)
│                            # Public surfaces (OBS overlays, song-request) = compiled widgets served by the
│                            # bot + CDN-cached for SaaS (widgets-overlays); there is no static web/ folder.
├── docs/                    # Published documentation
├── scripts/                 # Repo scripts — index below
├── tools/                   # Developer tooling
│   └── streamdeck/          #   Stream Deck plugin
├── devbox/                  # Containerised work environment (browser VS Code / Dev Containers / Remote-SSH)
├── .devcontainer/           # VS Code Dev Containers entry point for devbox
├── .githooks/               # commit-msg hook (rejects Co-Authored-By); enable with core.hooksPath
├── .github/                 # CI workflows (ci.yml, Stream Deck release workflows)
├── .claude/                 # Specs, reference docs, skills, agent config
├── handoff/                 # Cross-track work orders — unused during the remediation campaign (D7)
├── dist/                    # Build/distribution output — gitignored
├── .scratch/                # Throwaway artifacts — gitignored
├── docker-compose.yml       # Root compose — caddy + api-blue/api-green, built from the ROOT Dockerfile
├── Dockerfile               # The shipped image (CI builds it; the deployed stack runs it)
├── Caddyfile                # Caddy front for the blue/green pair
├── start.sh                 # Run API + dashboard from source in Development (SQLite, hot reload)
├── README.md
├── FEATURES.md              # Feature inventory
├── FEATURES-BY-SIDEBAR.md   # Feature inventory grouped by dashboard sidebar entry
├── DEPLOY.md                # Deployment chooser — desktop / docker / saas × web / desktop app
├── deploy.sh                # One deploy script per OS: <scenario> [--app]
├── deploy.ps1
└── .env.example
```

The HTML mockups / research docs live in the `nomnomzbot-design` repo — an external archived repo,
not in this tree.

## scripts/

| Script | Purpose |
|--------|---------|
| `close-slice.ps1` | Delete a shipped slice's bullet from the execution plan and commit the deletion |
| `dev-api.ps1` | Start / stop / status of the local dev API (waits for `/health`) |
| `free-disk-and-recover.ps1` | Disk-full Postgres crash-loop recovery: prune dangling images, verify recovery |
| `guard-single-color.sh` | Runs on the deploy host via cron; keeps only the healthy blue/green colour up |
| `land-worktree.ps1` | Merge a finished worktree-agent branch, sanity-check `v1.json` / `strings.xml`, push and watch |
| `migration-check.ps1` | Prove a migration works on an upgrade (database that already holds rows) |
| `mint-jwt.py` | Mint an HS256 access JWT to drive a deployed instance as an authenticated user |
| `spam-enforcement-mutation-check.ps1` | Spam-enforcement mutation harness: delete one guard in SpamEnforcementExecutor, run its tests, record which tests notice, restore |
| `proxmox-triage.ps1` | Read-only "what is wrong with the deployed bot" triage |
| `push-and-watch.ps1` | Push to origin/master and block on the CI verdict |
| `refresh-openapi.ps1` | Regenerate `server/openapi/v1.json` from a running API |
| `ship.ps1` | Post-push pipeline: watch CI, deploy on green, verify health and image freshness |
| `slice-check.ps1` | Per-slice gate: build, scoped tests, csharpier, style cleanup, ReSharper inspection |
| `switchover.ps1` | Zero-downtime blue/green deploy step |
| `verify-tree.ps1` | Full-tree, all-suites check that HEAD is green |
