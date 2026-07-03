# Deployment Profile — Design (DRAFT)

Source: design dialogue 2026-06-16. The central axis that selects every swappable adapter.

## Two profiles, one codebase
- **Self-hosted (lite):** SQLite + in-memory cache/pub-sub + WebSocket EventSub. **No Docker required.**
- **SaaS (full):** Postgres + Redis + conduits/webhooks EventSub. Managed infra.

## Selection
- **Auto-detect** (Docker / Postgres / Redis reachable? → full; else lite) **+ explicit override** (`App__DeploymentMode`). Auto-detect picks the sensible default; the operator can force either.

## First-run setup
- **Host-capabilities probe.** On first run the system detects host capabilities (**CPU cores, memory**) and **sizes worker pools / concurrency / limits to fit the machine** — especially important for self-host on arbitrary hardware so it runs well from a small NUC to a big server. Any explicit `Scaling:*` override always wins. (Sizing detail: `spec/scaling-qos.md` §9; probe owner: `spec/platform-conventions.md` §3.3.)
- **Guidance level — Simple vs Advanced.** The first-run wizard explicitly asks **Simple vs Advanced** (no silent default): **Simple → `novice`**, **Advanced → `expert`**, persisted as `DeploymentProfile.DefaultGuidanceLevel`. Bypassed/non-interactive setup **falls back to Simple (`novice`)**. This is only the per-user seed default; the live value is per-user `UserPreferences.GuidanceLevel` (adjustable anytime).

## Data layer
- **One provider-agnostic EF model** — shared entities, **avoid provider-specific column types** (no Postgres-only `jsonb`/array columns in the shared model; use portable types + value converters). Provider selected at runtime.
- **Migrations:** EF needs provider-specific migrations → maintain **two migration sets** (Postgres + SQLite) generated from the same model. (Honest: migration SQL can't be shared; keep both in sync.)
- **Capability matrix:** a few things are profile-specific by nature — e.g. **Postgres RLS is SaaS-only**; self-host (SQLite) falls back to app-level tenant filters (see GDPR doc). Design to the lowest common denominator + profile-specific enhancements.

## Cache / pub-sub
- An abstraction (`ICache` / `IEventBus`) with **Redis impl (SaaS)** + **in-memory impl (self-host)**. Self-host needs **no Redis**. Same adapter pattern as the event bus.

## Everything rides this one switch
Selected by DI at boot from the profile:
- DB provider (Postgres / SQLite)
- Cache + pub-sub (Redis / in-memory)
- EventSub transport (conduits+webhooks / WebSocket)
- Code executor (Wasmtime / Jint)
- Token vault (envelope-KMS / local-AES)
- Exposure model (managed edge / opt-in tunnel)
- Bot identity (custom per-channel name on hosted **Pro+** / shared platform bot on hosted `base`; self-host always custom)

**One axis, one boot-time decision, the whole system reconfigures.**
