# Repository Layout

```
nomnomzbot/
├── server/                  # Backend — .NET 10; SQLite (self_host_lite) or PostgreSQL + Redis (full/saas)
│   ├── src/
│   │   ├── NomNomzBot.Domain/          # Entities, domain events, value objects, interfaces
│   │   ├── NomNomzBot.Application/     # Use cases, services, pipeline engine, IEventBus
│   │   ├── NomNomzBot.Infrastructure/  # EF Core, Twitch services, EventSub, SignalR
│   │   ├── NomNomzBot.Migrations.Sqlite/ # SQLite migration assembly (self_host_lite)
│   │   └── NomNomzBot.Api/             # ASP.NET Core host, controllers, hubs, middleware
│   ├── tests/
│   │   ├── NomNomzBot.Domain.Tests/
│   │   ├── NomNomzBot.Application.Tests/
│   │   ├── NomNomzBot.Infrastructure.Tests/
│   │   ├── NomNomzBot.Api.Tests/
│   │   └── NomNomzBot.E2E.Tests/       # Playwright.NET end-to-end tests
│   └── Dockerfile
├── app/                     # Frontend — Kotlin Multiplatform + Compose Multiplatform (desktop + web/Wasm)
│   └── composeApp/          #   src/commonMain: App.kt, core/ (network client, i18n, design system),
│                            #   feature/<domain>/ (screens + state); i18n resources in
│                            #   composeResources/values/strings.xml (en) + values-nl/ (nl)
│                            # Public surfaces (OBS overlays, song-request) = compiled widgets served by the
│                            # bot + CDN-cached for SaaS (widgets-overlays); there is no static web/ folder.
├── docs/                    # Published documentation
├── dist/                    # Build/distribution output
├── scripts/                 # Repo scripts (ship.ps1 etc.)
├── tools/                   # Developer tooling
│   └── streamdeck/          #   Stream Deck plugin
├── .scratch/                # Gitignored throwaway artifacts
├── handoff/                 # Cross-track work orders — unused during the remediation campaign (D7)
├── docker-compose.yml       # Root compose — references ./server
├── DEPLOY.md                # Deployment chooser — desktop / docker / saas × web / desktop app
├── deploy.sh                # One deploy script per OS: <scenario> [--app]
├── deploy.ps1
└── .env.example
```

The HTML mockups / research docs live in the `nomnomzbot-design` repo — an external archived repo,
not in this tree.
