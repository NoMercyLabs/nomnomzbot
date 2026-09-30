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
| D2 | **Target: no per-channel stateful connections → no sharding/leasing, fully stateless nodes.** Inbound events ride each platform's app-global push: Twitch EventSub **Conduits** (`twitch-eventsub.md`), Kick webhooks, YouTube Live Chat polling. Chat outbound rides the `IChatProvider` seam (§6), routed per platform by `ChatPlatformRouter` — Twitch via Helix `Send Chat Message` (`user:write:chat`), Kick and YouTube via their chat-send REST — so there is no per-channel chat socket. **Stateless operation is gated on `EventSub:Conduits:Enabled` (default off).** Off: the per-owner EventSub WebSocket sessions carry Twitch ingest, one session per broadcaster user, so a node holds live per-owner state and the node is not stateless. On: two conduit shards carry Twitch ingest (each running instance holds one shard, an app-level socket rather than a per-channel one), and any node serves any channel; horizontal scale = add nodes behind the load balancer. Conduit mode is opt-in because the first conduit deploy (2026-09-29) left the bot deaf for 18 minutes; it becomes the default only after the blue/green takeover is proven against live Twitch. The EventSub webhook controller and HMAC verifier are not built (owner question). |
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

Three logical tiers, all running the **same binary** (separable by role via config, not by code). The binary is stateless once conduit mode is on (`EventSub:Conduits:Enabled`, D2); until then each node also holds the per-owner EventSub WebSocket sessions:

1. **Edge tier (ingest + API).** Receives HTTP (REST), EventSub notifications (conduit shards when `EventSub:Conduits:Enabled` is on, otherwise the per-owner WebSocket sessions), inbound integration webhooks, and chat events. Does only: authenticate → authorize (Gate-1/Gate-2 or `IPlatformIamService`) → **append `CommandLogEntry`** → ACK. No business logic on the hot path. Scales linearly; behind the load balancer; no sticky sessions.
2. **Worker tier (log-first processors).** Pulls ready `CommandLogEntry` rows via `IFairWorkScheduler`, re-checks invariants, runs the action (pipeline engine, integrations, moderation, economy…), emits domain events, advances projections. Bounded by lane + per-tenant concurrency. Scales by adding workers.
3. **Data tier.** Postgres (primary + replicas), Redis (cluster), object storage (exports/artifacts). §8.

Singletons that must run once cluster-wide (conduit provisioner when conduit mode is on, retention sweep, expiry sweeps) acquire `IRunOnceGuard` (pg `try_advisory_lock` SaaS / no-op self-host) — never sharded, never duplicated.

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

## 6. Chat transport — `IChatProvider` / `IChatPlatform` / `ChatPlatformRouter`

`NomNomzBot.Domain/Chat/Interfaces/IChatProvider.cs`, `IChatPlatform.cs`; implementations in `NomNomzBot.Infrastructure/Chat/`. Outbound chat + moderation with **no per-channel connection on any profile**. One channel has many platform connections (PRODUCT-ALIGNMENT D1), so one router selects the platform on every send. There is no transport axis per deployment profile. Two routing keys exist:

- **Tenant-keyed (`IChatProvider`).** Used by sends with no inbound message to answer: pipelines, timers, announcements, the dashboard composer. The router picks the platform from the tenant channel's `Channel.Provider` (cached per scope; a null provider means Twitch).
- **Origin-keyed (`IInboundOriginChatSender`).** Used by the hot chat path to answer on the platform an inbound message arrived on (a command typed on Kick is answered on Kick, never on the channel's primary platform).

```csharp
namespace NomNomzBot.Domain.Chat.Interfaces;

// Tenant-keyed chat + moderation. broadcasterId is the tenant Guid; the implementation resolves the channel's
// platform connection before any API call. Send methods return false when the message could NOT be sent (no
// connection, dead token, platform rejected) and never throw for an expected send failure.
public interface IChatProvider
{
    Task<bool> SendMessageAsync(Guid broadcasterId, string message, CancellationToken cancellationToken = default);
    // As the streamer's own account even when a dedicated bot account is connected (e.g. sub-only emotes the bot cannot render).
    Task<bool> SendMessageAsBroadcasterAsync(Guid broadcasterId, string message, CancellationToken cancellationToken = default);
    Task<bool> SendReplyAsync(Guid broadcasterId, string replyToMessageId, string message, CancellationToken cancellationToken = default);
    Task TimeoutUserAsync(Guid broadcasterId, string userId, int durationSeconds, string? reason = null, CancellationToken cancellationToken = default);
    Task BanUserAsync(Guid broadcasterId, string userId, string? reason = null, CancellationToken cancellationToken = default);
    Task<ChatUnbanOutcome> UnbanUserAsync(Guid broadcasterId, string userId, CancellationToken cancellationToken = default);
    Task DeleteMessageAsync(Guid broadcasterId, string messageId, CancellationToken cancellationToken = default);
}

// NotFound = nothing to lift (not an error); Failed = the unban did not go through.
public enum ChatUnbanOutcome { Success, NotFound, Failed }

// One platform's chat surface: the full IChatProvider set plus the Channel.Provider key it serves.
public interface IChatPlatform : IChatProvider
{
    string Provider { get; }    // "twitch", "kick", "youtube"
}

// --- NomNomzBot.Application/Chat/Services/IInboundOriginChatSender.cs (namespace NomNomzBot.Application.Chat.Services) ---
// Answers on the platform an inbound message arrived on. An unregistered provider is an honest failure
// (Result error code "unsupported_provider"), never a silent fall-through to Twitch.
public interface IInboundOriginChatSender
{
    Task<Result> SendMessageAsync(Guid broadcasterId, string provider, string message, CancellationToken cancellationToken = default);
    Task<Result> SendReplyAsync(Guid broadcasterId, string provider, string replyToMessageId, string message, CancellationToken cancellationToken = default);
}
```

- **Implementations (every profile, stateless — any node sends for any channel):** `HelixChatProvider` (Twitch — Helix `chat/messages` with `user:write:chat`, moderation via the Helix moderation endpoints, rate-limited via §4.2), `KickChatPlatform` (Kick Public API chat-send + moderation), `YouTubeChatPlatform` (Live Chat send + moderation). Registered as a scoped multi-bound `IChatPlatform` set. **No X chat platform exists yet**; a channel whose `Channel.Provider` names an unregistered platform gets no send (below).
- **`ChatPlatformRouter`** is the registered `IChatProvider` **and** `IInboundOriginChatSender` — one scoped instance behind both interfaces, so one per-request provider and prefix cache. A provider with no registered `IChatPlatform` is logged as a warning and the operation is dropped (`false` / `ChatUnbanOutcome.Failed`, or `unsupported_provider` on the origin-keyed path). It never falls through to Twitch. There is no fan-out method: a send with no inbound origin reaches the platform named by the tenant's `Channel.Provider` only.
- **Send pipeline (router, every message send).** In order: (1) the bot-line prefix, below; (2) `BotEmittedLine.Stamp`, an invisible loop-guard marker on every bot line (`OperatorChatSender`, a human operator's own composer send, is a separate path and is never stamped); (3) `IOutboundChatShaper.Shape` — chunks the line at word boundaries to the platform's visible-character budget from `ChatPlatformLineLimits` (**Twitch 500, Kick 500, YouTube 200, X/`twitter` 280**, unknown provider 500) and appends a zero-width variation marker when the line repeats the previous one verbatim on that queue; (4) `IChatSendQueue.EnqueueAsync` — see below. A chunk the platform rejects is logged and folds the whole call to `false`; a partially delivered line never reports success.
- **Pacing — `TokenBucketChatSendQueue` (singleton, in-process).** One token bucket per `{broadcasterId}:{provider}`. Default capacity 20 tokens refilled over 30 s (the Twitch non-moderator limit). A send waits for a token and is never dropped. Concurrent sends with the same coalesce key (same channel, platform, and text, plus the parent id for replies) join one in-flight send instead of posting the same line N times. The bucket is per node and not role-aware: it is not yet an `IRateLimiter` caller (§4), it has no separate moderator budget, and it has no queue-wait cap or typed `RATE_LIMITED` result.
- **Bot-line prefix (PRODUCT-ALIGNMENT D5).** `Channel.BotLinePrefix` (`[MaxLength(16)]`; a short marker such as `*` or one emoji; null or empty = none) is a visible, opt-in courtesy marker. The router prepends it to the message body **before** shaping, so it appears exactly once, on the first chunk, and counts toward the platform's character budget. It is skipped for `SendMessageAsBroadcasterAsync` (that is the streamer's own voice) and skipped when a dedicated bot account is connected (an active per-channel `ChannelBotAuthorization`, or a connected shared platform `BotAccount`), because that account's own username already tells viewers apart. It is resolved once per tenant per scope. It is separate from the invisible `BotEmittedLine` marker, which applies to every bot line.
- Chat **read** is the platform's push/poll ingest (Twitch EventSub `channel.chat.message` on the bot's `user:read:chat`; Kick `chat.message.sent` webhook; YouTube Live Chat polling) — one ingest seam per platform (`twitch-eventsub.md` §3.1 `IEventSource`).

Registered once (§9); there is no profile-selected transport.

---

## 7. Data-tier scaling

- **`IReadDbContext`** — a read-only `DbContext` bound to the **Postgres read-replica** connection (SaaS) / the same SQLite file (self-host). All projection reads, dashboard queries, leaderboards, and list endpoints use `IReadDbContext`; writes use `IApplicationDbContext` (primary). Reads scale with replicas; the primary handles only writes + the log append.
- **Event journal partitioning** — `EventJournal` and `CommandLogEntry` are **range-partitioned monthly** on `CreatedAt` (Postgres declarative partitioning, SaaS); old partitions detached/archived by retention (`gdpr-crypto.md` `IRetentionService`). Self-host (SQLite) is unpartitioned (single streamer volume is small).
- **Redis cluster** — cache (`ICache`), rate buckets (§4), fair-scheduler in-flight counters (§3), pub-sub (`RedisEventBus`). Keys are tenant-prefixed; no cross-slot multi-key ops.
- **Connection pooling** — `Scaling:Db:MaxPool` (default 100/node), `Scaling:Redis:MaxPool` (default 50/node), Npgsql multiplexing on.

---

## 8. Per-unit resource budgets — `LimitedResourceRegistry` (as shipped)

This subsystem owns the *between-tenant* fairness (§3–§5); the *within-unit* ceilings on countable/meterable resources are owned by one declaration point, `LimitedResourceRegistry` (`NomNomzBot.Application/Contracts/Billing/`). Every limited resource declares a **key** and a **`ResourceClass`**, and the class — not the resource's identity — decides whether it ever carries a tier ceiling. This is the design's core, driven by the owner's binding governance: **limits exist to recover real marginal cost, never to manufacture upsell pressure.**

### 8.1 The two `ResourceClass` values

- **`NEAR_FREE`** — the resource is one cheap DB row (commands, timers, event responses, response variations). It gets a **generous safety floor against abuse** and **never a paid ceiling**. Advertising a paid gate on a resource that costs the platform nothing to serve is a thing this project does not ship. The billing seeder no longer writes per-tier rows for `NEAR_FREE` keys (12 rows removed) — the tier catalogue no longer advertises a limit nothing enforces.
- **`COST_DRIVING`** — the resource maps to a real bill, so it is **tier-scaled** (`base` < `pro` < `premium`, D11): `tts_max_characters`, `sandbox_exec_ms`, `sound_clip_storage_bytes`, `channel_asset_storage_bytes`. These read the same `TierLimit` rows as §3.2/§4.3, sourced from `IBillingService.GetEntitlementAsync` (`monetization-billing.md`).

`[CountedResource]` marks the entities that count against a registry key. A **reflection guard** fails the build when a `[CountedResource]`-marked entity has no matching registry entry — the resource inventory is enumerated structurally off the codebase, never from a hand-maintained list, so a new countable entity cannot silently ship unmetered.

### 8.2 Enforcement path — one method, write and read agree

Enforcement happens at the **write path** through `IResourceQuotaService.GetCurrentCountAsync`, and `GET /api/v1/billing/usage` (Gate-2 `billing:read`) **reports through the same method** — the number a user is shown can never disagree with the number that refuses them. The storage write paths (sound clips, channel assets) are wired to the same service, with a **per-file size cap kept as an abuse guard that survives any tier** (it is not a `TierLimit` row — it applies uniformly).

**Self-host enforces only the safety baseline** (the `NEAR_FREE` abuse floor, and the `COST_DRIVING` per-execution/per-file guards) — **never a commercial ceiling** — because self-host runs on the operator's own hardware and there is no bill to protect.

### 8.3 Explicitly not metered (known gaps, not oversights)

- **Bandwidth / egress** — no request-byte counter exists anywhere in the stack. Not metered.
- **Retained `EventJournal` rows** — no retention policy exists to hang a limit on (§7 partitioning is storage layout, not a retention/eviction policy). Not metered.

Both are recorded here as known gaps rather than implied coverage; closing either is a separate, undecided piece of work with no fork to resolve today.

### 8.4 Dashboard surface

The dashboard's resource-limits section renders "X of Y used" from real counts returned by `GET /api/v1/billing/usage`. `NEAR_FREE` floors render as **abuse guards with no upgrade prompt** (there is nothing to upgrade). Create surfaces disable at-limit **with the reason** and warn when approaching the limit. A missing/failed usage report **never blocks a create** — it fails open, consistent with the platform's backpressure posture (§5: no request type blocks on a non-critical dependency being unavailable).

### 8.5 Still owned by their subsystems

The following remain **mandatory prerequisites** of this design, owned and detailed by their own specs; this section only states how they plug into `LimitedResourceRegistry`'s class split:

- **Pipeline** (`commands-pipelines.md` §3.3): per-channel + global concurrency admission, wall-clock-including-host watchdog, host-call budget, step-count cap, cumulative `Wait` cap. A runaway workflow self-terminates. Not a `LimitedResourceRegistry` key — an admission control, not a count.
- **Sandbox** (`code-execution-sandbox.md`): `ScriptResourceBudget` (CPU/mem/wall-clock), distributed admission, egress caps. A runaway `run_code` self-terminates. `sandbox_exec_ms` is `COST_DRIVING` (§8.1) — both the `TierLimit` quota and the per-execution `ScriptResourceBudget` scale with tier; self-host is host-sized (`-1` quota, budget from detected host capacity). The per-execution clamp values and the reserve-then-settle metering remain owned by `code-execution-sandbox.md` / `custom-code.md`.
- **Webhooks** (`webhooks.md`): payload size cap, pre-resolution rate limit (an `IRateLimiter` caller, §4), retry/dead-letter, self-amplification guard.
- **Regex** (`commands-pipelines.md` §6.4): `NonBacktracking` + ~50 ms match timeout.

A single request/workflow is bounded by these; a single channel is bounded by §3.2 caps + §4.3 buckets + §8's resource registry; the platform is bounded by §5 backpressure. The layers compose — no escape hatch.

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

// Chat send is profile-independent — one stateless IChatPlatform per platform behind the router (§6), registered once.
services.AddSingleton<IOutboundChatShaper, OutboundChatShaper>();
services.AddSingleton<IChatSendQueue, TokenBucketChatSendQueue>();
services.AddScoped<IChatPlatform, HelixChatProvider>();
services.AddScoped<IChatPlatform, YouTubeChatPlatform>();
services.AddScoped<IChatPlatform, KickChatPlatform>();
services.AddScoped<ChatPlatformRouter>();
services.AddScoped<IChatProvider>(sp => sp.GetRequiredService<ChatPlatformRouter>());
services.AddScoped<IInboundOriginChatSender>(sp => sp.GetRequiredService<ChatPlatformRouter>());

// Cluster-singleton guard (pg advisory lock SaaS / no-op self-host) — provisioner, sweeps
services.AddSingleton<IRunOnceGuard>(/* profile-selected */);

// EventSub conduit mode — the precondition for stateless nodes (D2). Off by default: without it the
// per-owner WebSocket sessions carry Twitch ingest. The coordinator is not registered when the flag is off.
if (configuration.GetValue<bool>("EventSub:Conduits:Enabled"))
    services.AddSingleton<IEventSubConduitShardCoordinator, EventSubConduitShardCoordinator>();
```

**Stateless-node gate:** `EventSub:Conduits:Enabled` (default `false`) — horizontal scale-out with any-node-serves-any-channel holds only when it is on (D2). **Vertical knobs (`appsettings`, `Scaling:` section):** `WorkerCount` (per-lane loop count, default 8/4/2 critical/standard/background), `CriticalReserveFraction` (0.5), `Db:MaxPool` (100), `Redis:MaxPool` (50), `Backpressure:{AmberSat:0.85, RedSat:0.95, CriticalDepthAmber:1000, CriticalDepthRed:5000}`, `Retry:{MaxAttempts:8, BaseSeconds:2, CapSeconds:300}`. **Self-host worker-pool / concurrency defaults are sized at first run to the detected host capabilities (CPU cores, memory) by the setup host-capabilities probe (`platform-conventions.md` / `2026-06-16-deployment-profile.md`), honoring any explicit `Scaling:*` override**; SaaS nodes are sized up + replicated.

---

## 10. Dependencies

| Use | Package / API | Party |
|---|---|---|
| Distributed buckets + counters + locks | `StackExchange.Redis` 2.13.17 (Lua `EVALSHA` for atomic token bucket) | 3rd (existing) |
| In-process limiter (self-host) | `System.Threading.RateLimiter` (in-box .NET 10 BCL) | 1st |
| Fair ordering | existing `NomNomzBot.Domain.Interfaces.IFairQueue<T>` (`music-sr.md` §3.8 reuse) | 1st |
| Persistence / partitioning | `Microsoft.EntityFrameworkCore` 10.0.9 (+ Npgsql declarative partitioning / Sqlite) | 2nd/3rd |
| Cluster-singleton | pg `pg_try_advisory_lock` via `IRunOnceGuard` (existing) | — |
| Chat send | hand-rolled platform clients (`IChatPlatform` → `HelixChatProvider` / `KickChatPlatform` / `YouTubeChatPlatform` behind `ChatPlatformRouter`), every profile | 1st |
| Events | in-box `IEventBus` (`SystemPressureChangedEvent`, `CommandDeadLetteredEvent`) | 1st |

**No new third-party dependency** beyond the already-accepted stack. Every distributed mechanism degrades to an in-process equivalent on self-host through the single boot switch.
