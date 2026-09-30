# Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend runtime | .NET 10, C# 14 |
| Backend framework | ASP.NET Core with Asp.Versioning.Mvc |
| ORM | EF Core 10 + Npgsql (PostgreSQL 16) |
| Embedded DB | SQLite (EF Core Sqlite) — `self_host_lite`, the default dev/desktop runtime |
| Cache / pub-sub | Redis 7 |
| Real-time | ASP.NET SignalR (WebSocket) |
| Auth | JWT · provider-generic sign-in (D2): device code or auth code + PKCE per platform · OAuth code flow for integrations · web refresh token in an HttpOnly cookie |
| Logging | Serilog |
| Frontend (dashboard) | Kotlin Multiplatform (KMP) + Compose Multiplatform — one codebase, **desktop + web (Wasm)** identical UI; mobile later |
| Public surfaces | Widget system (OBS overlays, song-request, OAuth landing) — compiled from source, served by the bot, CDN-cached for SaaS |
| Widgets | Vue SFC compiled by a pooled Jint + @vue/compiler-sfc (`JintVueSfcCompiler`), bundled by the esbuild binary, served by `OverlayVueRuntimeController` |
| Backend comms (FE) | Typed shared KMP client over REST (v1 API) + SignalR (realtime) |
| Payments | Stripe.net (present in `Directory.Packages.props`) |
| Scripting | Jint (present in `Directory.Packages.props`) |
| WASM runtime | Wasmtime (present in `Directory.Packages.props`) |
| Validation | FluentValidation (present in `Directory.Packages.props`) |
| Mapping | Mapster (present in `Directory.Packages.props`) |
| JSON | Newtonsoft.Json (present in `Directory.Packages.props`) — the serializer and dead-reference policy is an open owner question |
| IDs | Ulid (present in `Directory.Packages.props`) |
