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

## 1. Deploy model — rolling on SaaS, restart on self-host

The deploy strategy falls straight out of the stateless design (`scaling-qos.md`): instances hold no
per-tenant state in process, so they are interchangeable and replaceable.

- **SaaS — rolling update over the stateless pool.** Replace instances one (or a batch) at a time: nginx
  **drains** in-flight requests from the target instance (stops routing new ones, lets open ones finish),
  the instance is replaced with the new image, and it only rejoins the pool once its **readiness probe**
  passes (`/health/ready`). Because instances are stateless and EventSub is conduit-delivered (survives
  instance churn, `twitch-eventsub.md`), no work is lost mid-roll. **No blue-green** — the statelessness makes
  a second full stack unnecessary cost.
- **Self-host — single-process restart.** One process; a restart is acceptable (a brief gap). On restart the
  WebSocket EventSub transport **reconnects with backoff and re-registers subscriptions** (already in
  `twitch-eventsub.md`), and the app **auto-migrates on startup**. No load balancer, no rolling — stop, swap,
  start.
- **Migrations run before new instances take traffic, under a single migrator** (`IRunOnceGuard`, §2). Because
  every migration is backward-compatible (§2), the **old instances keep serving** throughout the rollover —
  that is what makes the SaaS roll zero-downtime.

---

## 2. Database migrations — forward-only, expand-contract

The schema evolves **post-launch** by the expand-contract (parallel-change) discipline. The initial schema is
**one greenfield migration per provider** (Postgres + SQLite — clean slate, `_BUILD-ORDER.md` Phase 1); this
rule governs every change after that.

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
  SQL can't be shared, the model can.
- **Auto-migrate on startup** (`IHost.MigrateAsync()` before `RunAsync()`), guarded by **`IRunOnceGuard`** (pg
  advisory lock on SaaS / no-op on self-host) so exactly **one** instance applies migrations while the others
  wait, then all boot against the migrated schema.
- **Never edit a shipped migration** — append a new one. The greenfield baseline is the only rewritable
  migration, and only until first deploy.

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

## 6. The release gate (CI → rolling release)

Every release passes this gate before it rolls — the deploy-time complement to the per-slice green bar
(`_BUILD-ORDER.md`):

1. **Migrations are expand-only** — no `DROP`/`RENAME` of an in-use column in this release (contract steps are
   their own later release). Reviewed at PR time.
2. **Both provider migration sets** (Postgres + SQLite) are generated and apply cleanly.
3. **New feature flags default OFF** (`IsEnabledGlobally = false`, `RolloutPercentage = 0`) — every new
   user-facing capability dark-launches.
4. **Readiness probe** gates traffic — a rolled instance joins the pool only after `/health/ready` passes
   (DB reachable, migrations applied, cache/bus adapters resolved, EventSub transport up).

**Sequence:** build image → **one migrator** applies migrations (`IRunOnceGuard`) → **roll instances** one
batch at a time (nginx drain + readiness gate; old instances keep serving on the backward-compatible schema)
→ **post-deploy**, ramp the new feature flags. Self-host collapses this to: stop → start (auto-migrate) →
ramp.

---

## 7. Dependencies (from the stack doc)

- **`DeploymentProfile.Mode`** (P.12) — selects rolling (SaaS) vs single-process (self-host); chosen at boot.
- **`IRunOnceGuard`** (scaling-qos) — single migrator + single conduit-provisioner during a roll.
- **`IFeatureFlagService`** + `FeatureFlag`/`FeatureFlagOverride` (P.13) + `FeatureFlagChangedEvent`
  (platform-conventions §3.4 / §2) — the rollout substrate.
- **`IEventUpcaster` / `IEventUpcasterRegistry`** + `EventJournal.EventVersion` (event-store §3.6) — event
  evolution.
- **Auto-discovery scan** + `ISeeder.Order` (backend-structure §4–5) — drop-a-class extensibility.
- **`ICacheService`** — flag-eval cache + `FeatureFlagChangedEvent` invalidation (owned by platform).
- **Twitch EventSub reconnect/backfill** (twitch-eventsub.md) — survives a self-host restart / a SaaS instance
  roll.

---

## 8. Decisions (resolved)

1. **Rolling deploys on SaaS** (drain + readiness-gated, over the stateless pool); **single-process restart on
   self-host**. No blue-green.
2. **Forward-only expand-contract migrations** — additive + backward-compatible per release; destructive DDL
   deferred to a later contract release. Two provider sets; auto-migrate on startup under `IRunOnceGuard`;
   shipped migrations are never edited.
3. **Event evolution via upcasters** — bump `EventVersion` + register an `IEventUpcaster`; journal rows are
   never rewritten; additive changes need no upcaster.
4. **Staged rollout via the existing `IFeatureFlagService`** — dark-launch → beta overrides → ramp `%` →
   tier/deployment/consent gates → GA, with a live kill-switch. Deterministic, process-stable bucketing.
5. **Adding capabilities is zero-friction** — user commands are runtime DB rows (no deploy); system
   actions/projections/upcasters are drop-a-class auto-discovered; new projections backfill via journal
   replay. Risky additions dark-launch behind a flag.
6. **Ownership split:** this spec owns the *playbook*; the feature-flag service/entity/event/admin-surface and
   the upcaster/auto-discovery mechanisms are owned by their existing specs and only referenced here.
