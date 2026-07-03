# Onboarding & First-Run — Design (DRAFT)

Source: design dialogue 2026-06-16 (decisions via Q&A). Replaces the deleted `setup.mjs`/deploy flow.

## Surface (graceful degradation)
- **Desktop user → the desktop app** (KMP) — the nicest path.
- **Browser / no desktop app → web wizard** (local page for self-host setup; signup for SaaS).
- **Headless Linux server → CLI** wizard.
- One onboarding logic, three presentations; pick the best available.

## Adaptive guidance level
- Auto-detect the environment, **pre-select the recommended option**, always let the user override.
- **Docker present = sophistication signal** → default to a more technical guidance level; no Docker → novice hand-holding.
- A global, **user-adjustable "guidance / info level"** (novice ↔ expert) tuning explanation density, tooltips, and defaults — across the **whole app**, not just onboarding. Elevate or downgrade anytime.

## DB / infra (self-host)
- Auto-detect Docker → recommend **Postgres + Docker** (pre-selected); else **SQLite, no Docker**. User confirms or switches. (Dual-DB deployment profile.)

## SaaS first-run value
- Connect Twitch → **the bot joins chat immediately**, responding to **default-enabled built-in commands** (e.g. `!followage` — every channel needs it). Instant out-of-box value; deeper config later.

## Bot identity (two-account model)
- **Shared bot** (the NomNomzBot account): default, frictionless, **free on SaaS**.
- **Custom bot name** (the user's own bot account): **premium on SaaS**, **free on self-host**.
- Built on the existing streamer-account + bot-account OAuth flow (progressive scopes).
