# Rollout & Updates — Operational Specification (rulebook)

How NomNomzBot ships changes without downtime: rolling deploys, forward-only **expand-contract** database
migrations, **feature-flag staged rollout**, event-schema evolution via upcasters, and the **zero-friction path
for adding commands/actions**. This is a **rulebook** — it defines *process and discipline*, not new entities,
services, or events. Every mechanism it relies on already exists; this spec is the playbook that composes them.
Closes gap **M3** (`_GAP-AUDIT.md`).

Source of truth: `2026-06-16-deployment-profile.md` (the profile axis), `spec/scaling-qos.md` (stateless
instances, `IRunOnceGuard`), `spec/event-store.md` §3.6 (upcasters), `spec/backend-structure.md` §4–5
(auto-discovery, seed order), `spec/platform-conventions.md` §3.4 (`IFeatureFlagService`) + schema **P.12**
(`DeploymentProfile`) / **P.13** (`FeatureFlag` / `FeatureFlagOverride`), `spec/stream-admin.md` (the flag
admin write surface).

## 0. What this spec owns (and does not)

- **Owns:** the deploy sequence, the migration discipline, the staged-rollout playbook, the event-evolution
  rule, and the "how to add a command/action/projection" path. No schema, no interfaces, no events, no action
  keys.
- **Does not own (references only):** `IFeatureFlagService` + its evaluation precedence + caching
  (`platform-conventions.md` §3.4); the `FeatureFlag`/`FeatureFlagOverride` tables (P.13) + `FeatureFlagChangedEvent`
  (platform-conventions §2); the flag **admin write surface** + `featureflag:write` (stream-admin / Plane-C);
  `IEventUpcaster`/`IEventUpcasterRegistry` (event-store §3.6); the auto-discovery scan (backend-structure §4);
  `DeploymentProfile` (P.12); `IRunOnceGuard` (scaling-qos).

---

## 1. Deploy model — blue/green behind Caddy on compose, restart on desktop

The deploy strategy falls straight out of the stateless design (`scaling-qos.md`): instances hold no
per-tenant state in process, so they are interchangeable and replaceable.

- **Compose deploys (`self_host_full`, and the single-node `saas` shape) — blue/green behind Caddy.** The root
  `docker-compose.yml` has **no `api` service**: Caddy owns the documented host port and fronts two API services,
  `api-blue` and `api-green`; in steady state exactly one runs. `scripts/switchover.ps1` (and the CI `deploy` job,
  which mirrors it over SSH) re-derives the live colour from `docker ps`, brings up the **idle** colour on the new
  image, waits for that colour's **own** `/health/ready` (in-container, not through Caddy), and only then stops the
  old colour with a drain window (SIGTERM → up to 30 s → SIGKILL). Caddy's **active** `/health/ready` poll
  (`Caddyfile`) moves traffic; the script never edits the Caddyfile. If the new colour never becomes ready it is
  stopped and the old one keeps serving — never zero healthy instances. See `deployment-distribution.md` §1.1 / §8.
- **Multi-replica SaaS fleet — rolling update over the stateless pool** (**RESTRICTED** option — NoMercy Labs only;
  see `deployment-distribution.md` §1). Replace instances one (or a batch) at a time: the proxy **drains**
  in-flight requests from the target instance (stops routing new ones, lets open ones finish), the instance is
  replaced with the new image, and it only rejoins the pool once its **readiness probe** passes (`/health/ready`).
- **No lost or duplicated events across a handover — `IActiveInstanceGate`.** While two colours overlap, only
  **one** is the active bot. The incoming colour starts as a **standby**: it holds a standby claim, retries the
  chat-ingest lease (`eventsub-chat-ingest`) every 2 s, and does **not** open owner sessions or create/delete
  subscriptions (the gate is closed until `StartAsync` has decided, which is what stopped a token-refresh
  recovery on the incoming colour from deleting the live colour's subscriptions). The outgoing colour closes its
  sockets first and releases the lease only after, so the successor never reads the same chat as the predecessor,
  and it never has to wait out a TTL. The outgoing colour also probes the standby claim at shutdown
  (`HasWaitingSuccessorAsync`): held = blue/green handover, free = a real outage.
- **Conduit mode is OFF by default.** Twitch **conduit** delivery (subscriptions on the deployment's conduit, one
  shard per colour) is **opt-in** via `EventSub:Conduits:Enabled`. Left off, the per-owner WebSocket sessions carry
  everything and the standby gate above is the handover mechanism. It was made opt-in after the first conduit deploy
  (2026-09-29) left the bot deaf for 18 minutes; do **not** describe conduits as what makes a roll lossless until the
  takeover is proven against live Twitch.
- **Desktop (`self_host_lite`) — single-process restart.** One process; a restart is acceptable (a brief gap). On
  restart the WebSocket EventSub transport **reconnects with backoff and re-registers subscriptions** (already in
  `twitch-eventsub.md`), and the app **auto-migrates on startup**. No load balancer, no rolling — stop, swap,
  start.
- **Migrations run before new instances take traffic, under a single migrator** (`IRunOnceGuard`, §2). Because
  every migration is backward-compatible (§2), the **old instances keep serving** throughout the rollover —
  that is what makes the roll zero-downtime.

---

## 2. Database migrations — forward-only, expand-contract

The schema evolves by the expand-contract (parallel-change) discipline, as a chain of **incremental migrations in
both assemblies**: Postgres in `NomNomzBot.Infrastructure` (`Platform/Persistence/Migrations`) and SQLite in the
separate `NomNomzBot.Migrations.Sqlite` project. The chain started from one greenfield `..._Initial` migration per
provider (2026-06-29, clean slate, `_BUILD-ORDER.md` Phase 1) and every change since is a new migration added to
**both** sets; this rule governs every change.

**The one rule: a migration must never break the currently-running code.** During a roll, old and new
instances run side by side against one database, so each migration is **additive and backward-compatible**.

A schema change that isn't trivially additive is done across **separate releases** (parallel-change):

1. **Expand** (release N) — add the new shape *alongside* the old: new nullable column / new table / new index.
   Old code ignores it; new code can use it. Backfill + (if needed) dual-write the old and new columns.
2. **Migrate** — backfill historical rows; the running code reads new-or-old, writes both.
3. **Contract** (release N+1, *after* every instance runs the new code) — drop/rename the now-unused old
   column. Destructive DDL is **always a later, deliberate migration**, never in the same release that stopped
   using the column.

Mechanics:
- **Two provider migration sets** (Postgres + SQLite) generated from one EF model, kept in sync — migration
  SQL can't be shared, the model can. A migration is **always added to both assemblies**; a SQLite-only migration
  breaks Postgres deploys and vice versa. Each set has its own design-time factory
  (`SqliteDesignTimeDbContextFactory` in the SQLite project), so `dotnet ef` must be pointed at the matching
  project.
- **Auto-migrate on startup** (`IHost.MigrateAsync()` before `RunAsync()`), guarded by **`IRunOnceGuard`** (pg
  advisory lock on SaaS / no-op on self-host) so exactly **one** instance applies migrations while the others
  wait, then all boot against the migrated schema.
- **Never edit a shipped migration** — append a new one. (The `..._Initial` baseline was the only rewritable
  migration, and only until first deploy.)
- **Upgrade proof, not just fresh-install proof.** Unit/integration tests run against an empty database, so a
  migration that only succeeds on a fresh install still passes them and bricks every existing deployment (the S005
  unique-index incident). `scripts/migration-check.ps1` closes that gap (§6).

---

## 3. Event-schema evolution — upcasters, never rewrite history

The `EventJournal` is permanent and append-only (`event-store.md`); a deploy must never rewrite old rows.

- **Additive payload change** (new optional field) — nothing to do; Newtonsoft tolerates missing/extra
  members, old rows deserialize fine.
- **Breaking payload change** — bump the event type's `EventJournal.EventVersion`, register an
  **`IEventUpcaster`** for `(EventType, FromVersion) → FromVersion+1` (event-store §3.6). The
  `IEventUpcasterRegistry` chains `v1 → … → vN` **on read**, so old rows keep their stored version and replay
  always sees the current shape. Upcasters are pure, registered via auto-discovery (§4), `Singleton`.

This is what lets a deploy change an event's shape without breaking replay or any projection.

---

## 4. Adding commands, actions, and projections — zero friction

Two distinct paths, by who's adding what:

- **User-authored** (commands, timers, event responses) — **pure runtime data**. Created/edited through the
  dashboard (`commands-pipelines.md` CRUD), stored as DB rows, **take effect immediately with no deploy**.
- **System-authored** (a new `ICommandAction`, `IProjection`, `ISeeder`, `IEventUpcaster`,
  `IDomainEventHandler`) — **drop a class** implementing the marker interface; the startup **assembly scan**
  (`backend-structure.md` §4) discovers and registers it with the convention lifetime — **no wiring edit**.
  It ships in the next rolling deploy. New `ICommandAction.Type` keys **auto-surface** in the pipeline-builder
  catalog the frontend renders.
- **A new `IProjection` backfills itself** — on first deploy the projection runner sees an empty checkpoint and
  **replays the journal** (`ResetAsync` + catch-up), so a new read model is built from history with **no data
  migration**. (This is why analytics/rewards read models need no backfill script.)
- **A new system action that's risky or incomplete** ships **behind a feature flag** (§5): the class is
  present but gated off, dark-launched, then ramped.

---

## 5. Feature-flag staged rollout (playbook over `IFeatureFlagService`)

Staged rollout uses the existing `IFeatureFlagService` (platform-conventions §3.4) over `FeatureFlag` /
`FeatureFlagOverride` (P.13). This spec defines **how to drive it**, not the service.

**Evaluation is already specified** (§3.4, do not restate divergently): effective state =
*tenant override (unexpired) > global toggle && rollout-% `hash(BroadcasterId, Key)` > tier floor (`MinTierId`)
> deployment-mode gate > consent gate*, cached `ff:{key}:{broadcasterId}` and invalidated by
`FeatureFlagChangedEvent`. **Correctness dependency:** the rollout-% bucket relies on that `hash` being a
**stable, process-independent** hash (e.g. FNV-1a / xxHash of `BroadcasterId + ":" + Key`) — never
`Object.GetHashCode` (randomized per process), or a channel would flap in/out across instances and restarts.

**The ramp (dark-launch → GA):**
1. **Dark-launch** — ship the feature gated: `FeatureFlag` row with `IsEnabledGlobally = false`,
   `RolloutPercentage = 0`. It's deployed but off for everyone.
2. **Internal/beta** — add per-channel `FeatureFlagOverride(IsEnabled = true)` for your own + beta channels
   (the override beats global, so they get it while everyone else stays off).
3. **Ramp** — raise `RolloutPercentage` (1 → 5 → 25 → 100). Deterministic bucketing means a channel that's in
   stays in as the percentage climbs (monotonic, no flapping).
4. **Gate by tier / deployment / consent** where relevant — `MinTierId` for paid features,
   `DeploymentMode = saas` for SaaS-only features, `RequiresConsent` for opt-in betas.
5. **GA** — `IsEnabledGlobally = true`, `RolloutPercentage = 100`; remove the beta overrides.
6. **Kill-switch** — set `IsEnabledGlobally = false` (or a per-channel `Override(IsEnabled = false)`);
   `FeatureFlagChangedEvent` invalidates the cache **live**, so the feature is off within the cache TTL with no
   deploy.

**Gating a feature in code:** the owning service calls `IFeatureFlagService.IsEnabledAsync(key)` (or
`IsEnabledForAsync(key, broadcasterId)` from a background worker) at the feature's entry point; disabled →
return `FEATURE_DISABLED` (or skip the handler). **Admin writes** (set global state / `%` / overrides) go
through stream-admin's admin surface (`featureflag:write`, Plane-C on SaaS / owner on self-host), which emits
`FeatureFlagAdministeredEvent` (audit) + `FeatureFlagChangedEvent` (invalidation) — this spec consumes that
surface, it does not add to it.

---

## 6. The release gate (what is enforced vs what is review-only)

Every release passes this gate before it rolls — the deploy-time complement to the per-slice green bar
(`_BUILD-ORDER.md`). **There is no single automated "release gate" job.** CI (`.github/workflows/ci.yml`) runs the
`test` and `contract` jobs, builds/pushes the image, then the `deploy` job does the blue/green swap; none of them
runs a migration-safety check. The four checks below are therefore split by how they are actually enforced:

1. **Migrations are expand-only** — no `DROP`/`RENAME` of an in-use column in this release (contract steps are
   their own later release). **PR-review only** — nothing checks it mechanically.
2. **Both provider migration sets** (Postgres + SQLite) are generated and apply cleanly. **Partly tooled, not in CI:**
   `scripts/slice-check.ps1` runs csharpier over the generated migrations of both assemblies, and
   `scripts/migration-check.ps1 [-Provider Sqlite|Postgres] [-SeedSql ...]` proves the **upgrade** path — it migrates
   a throwaway database to the *previous* migration, optionally plants rows the newest migration must cope with,
   then applies the newest and reports whether it survived. It checks **one provider per run** and is a local
   script (the `build-server` skill drives it), not a CI job. That a migration exists in **both** assemblies is
   **PR-review only**.
3. **New feature flags default OFF** (`IsEnabledGlobally = false`, `RolloutPercentage = 0`) — every new
   user-facing capability dark-launches. **PR-review only.**
4. **Readiness probe** gates traffic — **enforced by the deploy path**: `scripts/switchover.ps1` (and the CI
   `deploy` job) will not stop the old colour until the idle colour's own `/health/ready` returns 200
   (DB reachable, migrations applied, cache/bus adapters resolved, EventSub transport up), and Caddy only routes
   to a colour that passes it. A failed migration on boot therefore never takes the live colour down.

**Sequence:** build image → start the **idle colour**, which is the **one migrator** (`IRunOnceGuard`) applying
migrations on boot → wait for its `/health/ready` (old colour keeps serving on the backward-compatible schema; the
new colour waits as a standby, §1) → **stop the old colour** with a drain window → **post-deploy**, ramp the new
feature flags. A multi-replica fleet rolls one batch at a time (proxy drain + readiness gate). Desktop collapses
this to: stop → start (auto-migrate) → ramp.

---

## 7. Dependencies (from the stack doc)

- **`DeploymentProfile.Mode`** (P.12) — selects the adapter set; chosen at boot. Blue/green comes from the compose
  shape (`caddy` + `api-blue`/`api-green` + `scripts/switchover.ps1`), not from the mode.
- **`IRunOnceGuard`** (scaling-qos) — single migrator during a roll (and the conduit provisioner, **only** when
  `EventSub:Conduits:Enabled` is set — off by default).
- **`IActiveInstanceGate`** (`Application/Common/Interfaces`, implemented by `TwitchEventSubHostedService`) — the
  standby/active signal that keeps two overlapping colours from both reading chat or owning subscriptions (§1).
- **`IFeatureFlagService`** + `FeatureFlag`/`FeatureFlagOverride` (P.13) + `FeatureFlagChangedEvent`
  (platform-conventions §3.4 / §2) — the rollout substrate.
- **`IEventUpcaster` / `IEventUpcasterRegistry`** + `EventJournal.EventVersion` (event-store §3.6) — event
  evolution.
- **Auto-discovery scan** + `ISeeder.Order` (backend-structure §4–5) — drop-a-class extensibility.
- **`ICacheService`** — flag-eval cache + `FeatureFlagChangedEvent` invalidation (owned by platform).
- **Twitch EventSub reconnect/backfill** (twitch-eventsub.md) — survives a desktop restart / an instance roll.
- **`scripts/switchover.ps1`, root `Caddyfile`, `scripts/migration-check.ps1`** — the blue/green driver, its
  ingress, and the upgrade-proof migration check.

---

## 8. Decisions (resolved)

1. **Compose deploys are blue/green behind Caddy** (`scripts/switchover.ps1`: idle colour up → its own
   `/health/ready` → old colour drained; `IActiveInstanceGate` keeps the overlap from double-reading chat);
   **multi-replica SaaS rolls** (drain + readiness-gated, over the stateless pool — restricted option);
   **desktop is a single-process restart**. Conduit delivery is opt-in (`EventSub:Conduits:Enabled`, off by default).
2. **Forward-only expand-contract migrations** — additive + backward-compatible per release; destructive DDL
   deferred to a later contract release. Incremental migrations in **both** assemblies (Postgres in Infrastructure,
   SQLite in `NomNomzBot.Migrations.Sqlite`); auto-migrate on startup under `IRunOnceGuard`; shipped migrations are
   never edited; upgrade proof via `scripts/migration-check.ps1` (local, not CI); expand-only and new-flags-OFF are
   PR-review items.
3. **Event evolution via upcasters** — bump `EventVersion` + register an `IEventUpcaster`; journal rows are
   never rewritten; additive changes need no upcaster.
4. **Staged rollout via the existing `IFeatureFlagService`** — dark-launch → beta overrides → ramp `%` →
   tier/deployment/consent gates → GA, with a live kill-switch. Deterministic, process-stable bucketing.
5. **Adding capabilities is zero-friction** — user commands are runtime DB rows (no deploy); system
   actions/projections/upcasters are drop-a-class auto-discovered; new projections backfill via journal
   replay. Risky additions dark-launch behind a flag.
6. **Ownership split:** this spec owns the *playbook*; the feature-flag service/entity/event/admin-surface and
   the upcaster/auto-discovery mechanisms are owned by their existing specs and only referenced here.
