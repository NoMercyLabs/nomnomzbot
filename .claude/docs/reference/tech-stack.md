# Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend runtime | .NET 10, C# 14 |
| Backend framework | ASP.NET Core with Asp.Versioning.Mvc |
| ORM | EF Core 10 + Npgsql (PostgreSQL 16) |
| Cache / pub-sub | Redis 7 |
| Real-time | ASP.NET SignalR (WebSocket) |
| Auth | JWT + Twitch **Device Code Flow** login (secret-free) · OAuth code flow for integrations · web refresh token in an HttpOnly cookie |
| Logging | Serilog |
| Frontend (dashboard) | Kotlin Multiplatform (KMP) + Compose Multiplatform — one codebase, **desktop + web (Wasm)** identical UI; mobile later |
| Public surfaces | Widget system (OBS overlays, song-request, OAuth landing) — compiled from source at build time, served by the bot, CDN-cached for SaaS; the build→serve→cache pipeline is being specced |
| Backend comms (FE) | Typed shared KMP client over REST (v1 API) + SignalR (realtime) |
