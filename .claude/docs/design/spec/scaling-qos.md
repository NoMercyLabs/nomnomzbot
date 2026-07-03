# Interface Specification — `scaling-qos` Subsystem (SaaS scaling, fairness & QoS)

**Status:** Implementable spec. Code from this directly. **Every item below is a final, binding decision — nothing deferred.**
**Area:** horizontal/vertical scaling model · log-first command/event runtime · per-tenant fair work scheduling · distributed + per-tenant rate limiting · priority lanes · backpressure & load shedding · stateless chat transport · data-tier scaling. The cross-cutting QoS layer that guarantees no single request, channel, or workflow can saturate the system.
**Grounding:** `2026-06-16-deployment-profile.md` (the adapter axis) · `event-store.md` (log-first journal/projections) · `twitch-eventsub.md` (Conduits) · `twitch-helix.md` (`ITwitchRateLimiter`) · `commands-pipelines.md` (pipeline admission/watchdog/budgets) · `code-execution-sandbox.md` (`ScriptResourceBudget`) · `monetization-billing.md` (`TierLimit`).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, NO MediatR/Roslyn; surrogate PK `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; Newtonsoft.Json for app JSON. Everything in this spec is **profile-adapter-selected** on the single `DeploymentProfile.Mode` axis (SaaS = Postgres/Redis/distributed; self-host = SQLite/in-process/single-node).

---

## 0. Decisions (all final, binding)

| # | Decision |
|---|---|
| D1 | **Log-first runtime.** Every inbound action (chat command, EventSub event, API mutation, inbound webhook) is: (1) authorized at the edge, (2) appended as a durable `CommandLogEntry` (O(1)), (3) ACKed. Async workers pull lazily, **re-check invariants at processing time**, execute, emit domain events, advance projections. The append is the only synchronous work on the hot path. |
| D2 | **No per-channel stateful connections → no sharding/leasing.** EventSub inbound rides app-global **Conduits + webhooks** (`twitch-eventsub.md`); chat outbound rides `IChatProvider` → **`HelixChatProvider`** (Helix `Send Chat Message`, `user:write:chat`) on **every** profile — IRC is retired, so there is no per-channel chat socket anywhere. Chat *read* is EventSub `channel.chat.message` on both. App nodes are therefore **fully stateless**; horizontal scale = add nodes behind the load balancer. |
| D3 | **Stateful in-memory state is a rebuildable cache.** Per-channel runtime state (song fair-queue, pipeline state, trust cache) is always reconstructable from Postgres (`music-sr.md` §3.8 deterministic rebuild). Any node serves any channel; no affinity. |
| D4 | **Distributed rate limiting** via the `IRateLimiter` adapter (Redis SaaS / in-process self-host): a **global Helix client-id bucket** with **per-channel fair sub-budgets**, plus **tier-weighted per-tenant inbound buckets**. |
| D5 | **Per-tenant fair work scheduling** via `IFairWorkScheduler` — Bamo's rank fairness (`IFairQueue<T>`) keyed by `BroadcasterId`, bounded by per-tenant concurrency caps. No channel starves another. |
| D6 | **Three priority lanes** — `critical` (chat replies, moderation actions), `standard` (commands, integrations), `background` (analytics, projection rebuilds, retention). Background sheds first. |
| D7 | **Backpressure, never collapse.** Concrete high-water marks (§5) defer `background`, then shed work beyond a tenant's fair share, then 429 new inbound. The synchronous path is always a bounded append. |
| D8 | **Data tier:** Postgres primary + read replicas (reads/projections via `IReadDbContext` → replica), event journal **partitioned monthly**, Redis cluster for cache/buckets/pub-sub. Tenant isolation by RLS. |
| D9 | **Vertical scaling** is config-tuned density (`Scaling:*` knobs in §9): worker-pool size, DB/Redis pool sizes, batch sizes. Self-host runs one node, vertical-only; SaaS adds nodes horizontally. |
| D10 | Self-host collapses every distributed mechanism to its single-node in-process form via the same boot switch — **identical code paths, one impl swap each.** |
| D11 | **Governing limits principle.** Every quota / rate / throughput / concurrency limit in this spec is a **safety-first baseline that protects the platform** (a hard floor that applies to **all** hosted tenants and can never be exceeded), **plus tier-scaled headroom on top** where the limit is tier-relevant — `base` < `pro` < `premium`. There is no free hosted tier; the hosted tiers are `base`/`pro`/`premium` (`monetization-billing.md`). **Self-host is not tiered — it is sized to its detected host** (CPU cores / memory probed at setup, `platform-conventions.md` / `2026-06-16-deployment-profile.md`), with `-1` (unlimited) on the per-tenant caps since it is single-tenant. Every limit table below reads `base`/`pro`/`premium` (+ self-host = host-sized), never `free`. |

---

## 1. Runtime topology

Three logical tiers, all running the **same stateless binary** (separable by role via config, not by code):

1. **Edge tier (ingest + API).** Receives HTTP (REST), EventSub conduit webhooks, inbound integration webhooks, and chat events. Does only: authenticate → authorize (Gate 1/2 or `IPlatformIamService`) → **append `CommandLogEntry`** → ACK. No business logic on the hot path. Scales linearly; behind the load balancer; no sticky sessions.
2. **Worker tier (log-first processors).** Pulls ready `CommandLogEntry` rows via `IFairWorkScheduler`, re-checks invariants, runs the action (pipeline engine, integrations, moderation, economy…), emits domain events, advances projections. Bounded by lane + per-tenant concurrency. Scales by adding workers.
3. **Data tier.** Postgres (primary + replicas), Redis (cluster), object storage (exports/artifacts). §8.

Singletons that must run once cluster-wide (conduit provisioner, retention sweep, expiry sweeps) acquire `IRunOnceGuard` (pg `try_advisory_lock` SaaS / no-op self-host) — never sharded, never duplicated.

---

## 2. Log-first command/event pipeline

### 2.1 Entity — `CommandLogEntry` (schema addition, Domain O)

The durable intake record. Append-only; the worker tier's single source of work.

`Id bigint` PK **[APPEND-ONLY, monotonic]** · `BroadcasterId Guid?` (tenant; null = platform op) · `Lane string(10)` [VC:enum `WorkLane`] (`critical`|`standard`|`background`) · `Kind string(40)` (`chat_command`|`eventsub`|`api_mutation`|`webhook_in`|`scheduled`) · `PayloadJson text` **[VC:JSON]** · `SourceEventId Guid?` (FK→`EventJournal.EventId` when raised from an event) · `IdempotencyKey string(120)?` (Unique-filtered) · `Status string(12)` [VC:enum] (`pending`|`claimed`|`done`|`failed`|`dead`) · `Attempts int` · `ClaimedByNode string(64)?` · `ClaimedAt timestamp?` · `VisibleAt timestamp` (delay/retry backoff; index) · `CreatedAt`. **Indexes:** `(Status, Lane, VisibleAt)` (claim scan), `(BroadcasterId, Status)` (fair-share count), partial-unique `(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL`.

> **Relationship to `EventJournal`.** `CommandLogEntry` is the **intake/work** log (commands awaiting processing); `EventJournal` (`event-store.md`) is the **outcome/fact** log (what happened, for replay/projection). A processed command emits one or more journaled events. Replay rebuilds projections from `EventJournal` only; `CommandLogEntry` rows are pruned by retention after `done`.

### 2.2 `ICommandLog` — append + claim

`NomNomzBot.Application/Common/Interfaces/Scaling/ICommandLog.cs`. The append is the hot-path primitive.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Scaling;

public interface ICommandLog
{
    // HOT PATH. Authorizes nothing (caller already did) — durably appends one entry, dedupes on IdempotencyKey,
    // assigns Lane, returns its id. One INSERT under IUnitOfWork. Never blocks on processing.
    Task<Result<long>> AppendAsync(CommandLogAppend entry, CancellationToken ct = default);

    // Claims up to `max` ready entries for `nodeId` honoring fair scheduling (§3): sets Status=claimed,
    // ClaimedByNode/At, bumps Attempts. Atomic (Redis SaaS / UPDATE…RETURNING self-host). The ONLY worker entry point.
    Task<Result<IReadOnlyList<CommandLogEntryDto>>> ClaimAsync(string nodeId, WorkLane lane, int max, CancellationToken ct = default);

    // Marks an entry done. On failure: re-queue with exponential backoff (VisibleAt = now + 2^Attempts·base, capped),
    // or Status=dead after MaxAttempts → dead-letter (surfaced to ops; emits CommandDeadLetteredEvent).
    Task<Result> CompleteAsync(long entryId, CommandOutcome outcome, CancellationToken ct = default);
}
```

`CommandLogAppend(Guid? BroadcasterId, WorkLane Lane, string Kind, string PayloadJson, Guid? SourceEventId, string? IdempotencyKey)`; `CommandOutcome(bool Success, string? FailureReason, bool Retryable)`. `MaxAttempts = 8`, backoff base `2s`, cap `5m` (config `Scaling:Retry:*`).

### 2.3 Worker host — `LogProcessorHostedService`

One `IHostedService` per node; runs `WorkerCount` (config) concurrent loops **per lane** (critical loops > standard > background). Each loop: `ClaimAsync(lane)` → dispatch to the registered `ICommandHandler` for `Kind` → `CompleteAsync`. Fail-closed: an unhandled `Kind` ⇒ `CompleteAsync(Retryable:false)` → dead-letter. Re-checks invariants (permission still valid, queue still open, balance still sufficient) **at processing time**, since state may have changed since append (D1).

---

## 3. Per-tenant fair work scheduling

### 3.1 `IFairWorkScheduler`

`NomNomzBot.Application/Common/Interfaces/Scaling/IFairWorkScheduler.cs`. Decides **which tenant's** entry a free worker slot serves next, so a high-volume channel cannot monopolize the pool. Backs `ICommandLog.ClaimAsync`.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Scaling;

public interface IFairWorkScheduler
{
    // Returns the next BroadcasterIds to serve for `lane`, fairly ordered: tenants with the FEWEST in-flight
    // (claimed, not done) entries first — Bamo's rank fairness (IFairQueue) keyed by BroadcasterId — skipping any
    // tenant already at its per-tenant concurrency cap (§3.2). Pure ordering decision; ICommandLog does the claim.
    Task<Result<IReadOnlyList<Guid?>>> NextTenantsAsync(WorkLane lane, int slots, CancellationToken ct = default);

    // Current in-flight (claimed) count for a tenant in a lane — drives the cap check + observability.
    Task<Result<int>> InFlightAsync(Guid? broadcasterId, WorkLane lane, CancellationToken ct = default);
}
```

**Algorithm.** Group ready entries by `BroadcasterId`; order tenants by ascending in-flight count then oldest-waiting (`IFairQueue<Guid>` rank = tenant's in-flight + 1, FIFO within rank). If N channels each have work, all N get a slot before any channel gets a 2nd — identical fairness to the song queue (`music-sr.md` §3.8), applied to work. In-flight counts are tracked in Redis (`scaling:inflight:{lane}:{tenant}`, SaaS) / in-memory (self-host).

### 3.2 Per-tenant concurrency caps (tier-weighted)

Max concurrent `claimed` entries per tenant per lane. Per D11 this is a **safety baseline (no hosted tenant may exceed it) plus tier-scaled headroom** (`base` < `pro` < `premium`). A tenant at its cap is skipped by `NextTenantsAsync` until a slot frees. From `IBillingService.GetEntitlementAsync` (`monetization-billing.md`); `TierLimit` key **`worker_concurrency`**:

| Tier | `worker_concurrency` (concurrent claimed entries) |
|---|---|
| Base | 5 |
| Pro | 15 |
| Premium | 40 |
| Self-host | `-1` (unlimited — single tenant, host-sized pool) |

Global ceiling = `Scaling:WorkerCount × NodeCount`; the fair scheduler distributes it. Critical lane reserves `Scaling:CriticalReserveFraction = 0.5` of slots so chat/mod latency is never blocked by a `background` storm.

---

## 4. Rate limiting — `IRateLimiter`

### 4.1 Interface

`NomNomzBot.Application/Common/Interfaces/Scaling/IRateLimiter.cs`. One abstraction, two scopes (outbound Helix coordination + inbound tenant limits), two impls.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Scaling;

public interface IRateLimiter
{
    // Atomically tries to consume `cost` tokens from the named bucket (token bucket; refill = Limit/Window).
    // Returns Allowed + RetryAfter when denied. SaaS: a single Redis Lua script (atomic across nodes).
    // Self-host: in-process System.Threading.RateLimiter. NEVER a read-modify-write race.
    Task<Result<RateDecision>> TryAcquireAsync(RateBucketKey bucket, int cost = 1, CancellationToken ct = default);
}

public sealed record RateBucketKey(string Scope, string Id, int Limit, TimeSpan Window);
public sealed record RateDecision(bool Allowed, int Remaining, TimeSpan RetryAfter);
```

### 4.2 Outbound — global Helix coordination

The Twitch Helix limit is **per client-id (≈800 points / 60 s)**, shared by every channel. `ITwitchRateLimiter` (`twitch-helix.md`) becomes a **thin caller of `IRateLimiter`** on SaaS (Redis), in-process on self-host:

- **Global bucket** `helix:app` — `Limit = 720` (90 % of 800, headroom), `Window = 60s`. Every Helix call acquires here.
- **Per-channel sub-budget** `helix:ch:{broadcasterId}` — `Limit = max(8, floor(720 / activeChannels))`, `Window = 60s`. Caps any one channel's share so it cannot drain the global bucket or trip a 429 for others. `activeChannels` recomputed each minute from `IChannelRegistry`.
- **Priority:** `TwitchCallPriority.UserTriggered` calls acquire ahead of `Background` (two-band wait queue inside `TwitchRateLimiter`). A real 429 → exponential backoff + `TwitchHelixRateLimitedEvent(WasHardLimited:true)`.

### 4.3 Inbound — per-tenant tier-weighted buckets

Applied at the edge (D1) before append. Over-limit ⇒ `429` + `Retry-After`. Per D11 each row is a **safety baseline (applies to every hosted tenant) plus tier-scaled headroom** (`base` < `pro` < `premium`); self-host is host-sized (`-1`, single tenant). `TierLimit` keys; buckets `in:{scope}:{broadcasterId}`:

| Scope (`TierLimit` key) | Base | Pro | Premium | Self-host | Window |
|---|---|---|---|---|---|
| `rate_api_per_min` | 300 | 1 200 | 3 000 | `-1` | 60 s |
| `rate_command_per_min` | 120 | 600 | 2 000 | `-1` | 60 s |
| `rate_webhook_in_per_min` | 300 | 1 200 | 6 000 | `-1` | 60 s |
| `rate_song_request_per_min` | 30 | 60 | 120 | `-1` | 60 s |

`-1` ⇒ `IRateLimiter` short-circuits `Allowed=true` (self-host, single tenant). Platform/IAM endpoints are exempt (operator plane).

---

## 5. Backpressure & load shedding

Continuous, automatic, three-stage — driven by two live signals: **lane depth** (`COUNT(*) WHERE Status=pending` per lane, cached 1 s) and **worker saturation** (`claimed / capacity`).

| Stage | Trigger (high-water) | Action | Recovery (low-water) |
|---|---|---|---|
| **Green** | saturation < 0.85 | normal — all lanes drain | — |
| **Amber** | saturation ≥ 0.85 **or** `critical` depth > 1 000 | `background` lane paused (projection rebuilds, analytics, retention defer); standard + critical unaffected | saturation < 0.70 |
| **Red** | saturation ≥ 0.95 **or** `critical` depth > 5 000 | shed: tenants beyond their fair share get `standard` work deferred; **inbound 429** for any tenant over its §4.3 bucket; critical still drains (its reserved fraction, §3.2) | saturation < 0.80 |

Thresholds are `Scaling:Backpressure:*` config. State (`green`/`amber`/`red`) is published to `IEventBus` (`SystemPressureChangedEvent`) and the admin dashboard. **Critical lane is never shed** — chat replies and moderation actions always process; their reserved slots (§3.2) guarantee it. No request type ever blocks the synchronous append path (it is a single bounded INSERT).

---

## 6. Chat transport — `IChatProvider` (Helix everywhere)

`NomNomzBot.Domain/Chat/Interfaces/IChatProvider.cs`. Outbound chat + moderation with **no per-channel connection on any profile** — IRC is retired, so every profile sends over Helix and there is no transport axis.

```csharp
namespace NomNomzBot.Domain.Chat.Interfaces;

public interface IChatProvider
{
    // broadcasterId is the tenant Guid; the impl resolves it to the Twitch channel id before any Helix call.
    Task SendMessageAsync(Guid broadcasterId, string message, CancellationToken ct = default);
    Task SendReplyAsync(Guid broadcasterId, string replyToMessageId, string message, CancellationToken ct = default);
    Task TimeoutUserAsync(Guid broadcasterId, string userId, int durationSeconds, string? reason = null, CancellationToken ct = default);
    Task BanUserAsync(Guid broadcasterId, string userId, string? reason = null, CancellationToken ct = default);
    Task UnbanUserAsync(Guid broadcasterId, string userId, CancellationToken ct = default);
    Task DeleteMessageAsync(Guid broadcasterId, string messageId, CancellationToken ct = default);
}
```

- **`HelixChatProvider` (every profile):** send via Helix `POST /helix/chat/messages` (`user:write:chat`); moderation (ban / timeout / unban / delete) via the Helix moderation endpoints. **Stateless** — any node sends for any channel; no IRC socket; rate-limited via §4.2. This is what removes the last sharding problem **on self-host and SaaS alike**.
- Chat **read** is EventSub `channel.chat.message` on every profile (the bot's `user:read:chat` scope) — the single ingest path; the legacy IRC `chat:read` is not used.

Registered once (`services.AddScoped<IChatProvider, HelixChatProvider>()`); there is no profile-selected transport. (Supersedes both the legacy "all chat via IRC" assumption and the earlier Helix-on-SaaS / IRC-on-self-host split — IRC fully retired, Helix everywhere; CLAUDE.md updated.)

---

## 7. Data-tier scaling

- **`IReadDbContext`** — a read-only `DbContext` bound to the **Postgres read-replica** connection (SaaS) / the same SQLite file (self-host). All projection reads, dashboard queries, leaderboards, and list endpoints use `IReadDbContext`; writes use `IApplicationDbContext` (primary). Reads scale with replicas; the primary handles only writes + the log append.
- **Event journal partitioning** — `EventJournal` and `CommandLogEntry` are **range-partitioned monthly** on `CreatedAt` (Postgres declarative partitioning, SaaS); old partitions detached/archived by retention (`gdpr-crypto.md` `IRetentionService`). Self-host (SQLite) is unpartitioned (single streamer volume is small).
- **Redis cluster** — cache (`ICache`), rate buckets (§4), fair-scheduler in-flight counters (§3), pub-sub (`RedisEventBus`). Keys are tenant-prefixed; no cross-slot multi-key ops.
- **Connection pooling** — `Scaling:Db:MaxPool` (default 100/node), `Scaling:Redis:MaxPool` (default 50/node), Npgsql multiplexing on.

---

## 8. Per-unit resource budgets (the inner guards, reaffirmed)

This subsystem owns the *between-tenant* fairness; the *within-unit* ceilings are owned by their subsystems and are **mandatory prerequisites** of this design:

- **Pipeline** (`commands-pipelines.md` §3.3): per-channel + global concurrency admission, wall-clock-including-host watchdog, host-call budget, step-count cap, cumulative `Wait` cap. A runaway workflow self-terminates.
- **Sandbox** (`code-execution-sandbox.md`): `ScriptResourceBudget` (CPU/mem/wall-clock), distributed admission, egress caps. A runaway `run_code` self-terminates. Per D11, **both** the sandbox per-execution budget **and** the sandbox rate/concurrency limits are **tier-scaled on top of the safety baseline** — the three budget/limit levels map to `base`/`pro`/`premium` (the `sandbox_exec_ms` `TierLimit` quota and the per-execution `ScriptResourceBudget` both scale with tier), and self-host is host-sized (`-1` quota, budget from detected host capacity). The per-execution clamp values and the `sandbox_exec_ms` reserve-then-settle metering remain **owned by `code-execution-sandbox.md` / `custom-code.md`**; this spec only states the tier-scaling governance.
- **Webhooks** (`webhooks.md`): payload size cap, pre-resolution rate limit (now an `IRateLimiter` caller), retry/dead-letter, self-amplification guard.
- **Regex** (`commands-pipelines.md` §6.4): `NonBacktracking` + ~50 ms match timeout.

A single request/workflow is bounded by these; a single channel is bounded by §3.2 caps + §4.3 buckets; the platform is bounded by §5 backpressure. The three layers compose — no escape hatch.

---

## 9. DI registration & profile adapters

`NomNomzBot.Infrastructure/DependencyInjection.cs`, on the `DeploymentProfile.Mode` switch (mirrors DB/cache/bus/executor selection). The seven abstractions each have exactly two impls:

```csharp
// Log-first runtime (both profiles — same code, different store via IApplicationDbContext provider)
services.AddScoped<ICommandLog, CommandLog>();
services.AddHostedService<LogProcessorHostedService>();

if (profile.Mode == DeploymentMode.Saas)
{
    services.AddSingleton<IRateLimiter, RedisRateLimiter>();            // Redis Lua token buckets
    services.AddScoped<IFairWorkScheduler, RedisFairWorkScheduler>();  // Redis in-flight counters
    services.AddScoped<IReadDbContext>(sp => sp.GetRequiredService<ReplicaDbContextFactory>().Create());
}
else
{
    services.AddSingleton<IRateLimiter, InProcessRateLimiter>();       // System.Threading.RateLimiter
    services.AddScoped<IFairWorkScheduler, InProcessFairWorkScheduler>();
    services.AddScoped<IReadDbContext>(sp => (IReadDbContext)sp.GetRequiredService<IApplicationDbContext>());
}

// Chat send is profile-independent — Helix on every profile (IRC retired), registered once.
services.AddScoped<IChatProvider, HelixChatProvider>();

// Cluster-singleton guard (pg advisory lock SaaS / no-op self-host) — provisioner, sweeps
services.AddSingleton<IRunOnceGuard>(/* profile-selected */);
```

**Vertical knobs (`appsettings`, `Scaling:` section):** `WorkerCount` (per-lane loop count, default 8/4/2 critical/standard/background), `CriticalReserveFraction` (0.5), `Db:MaxPool` (100), `Redis:MaxPool` (50), `Backpressure:{AmberSat:0.85, RedSat:0.95, CriticalDepthAmber:1000, CriticalDepthRed:5000}`, `Retry:{MaxAttempts:8, BaseSeconds:2, CapSeconds:300}`. **Self-host worker-pool / concurrency defaults are sized at first run to the detected host capabilities (CPU cores, memory) by the setup host-capabilities probe (`platform-conventions.md` / `2026-06-16-deployment-profile.md`), honoring any explicit `Scaling:*` override**; SaaS nodes are sized up + replicated.

---

## 10. Dependencies

| Use | Package / API | Party |
|---|---|---|
| Distributed buckets + counters + locks | `StackExchange.Redis` 2.13.17 (Lua `EVALSHA` for atomic token bucket) | 3rd (existing) |
| In-process limiter (self-host) | `System.Threading.RateLimiter` (in-box .NET 10 BCL) | 1st |
| Fair ordering | existing `NomNomzBot.Domain.Interfaces.IFairQueue<T>` (`music-sr.md` §3.8 reuse) | 1st |
| Persistence / partitioning | `Microsoft.EntityFrameworkCore` 10.0.9 (+ Npgsql declarative partitioning / Sqlite) | 2nd/3rd |
| Cluster-singleton | pg `pg_try_advisory_lock` via `IRunOnceGuard` (existing) | — |
| Chat send | hand-rolled Helix (`IChatProvider` → `HelixChatProvider`), every profile — IRC retired | 1st |
| Events | in-box `IEventBus` (`SystemPressureChangedEvent`, `CommandDeadLetteredEvent`) | 1st |

**No new third-party dependency** beyond the already-accepted stack. Every distributed mechanism degrades to an in-process equivalent on self-host through the single boot switch.
