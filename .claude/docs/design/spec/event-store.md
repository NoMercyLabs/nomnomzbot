# Event Store — Interface Specification

**Status:** Implementable. Code from this directly. No ambiguity intended.
**Owner area:** append-only `EventJournal` (per-tenant `StreamPosition`), read-model projections + checkpoints, replay, snapshots, `EventSubjectKeys`, crypto-shred linkage.

**Grounding (read these, do not re-derive):**
- Locked schema — `docs/design/2026-06-16-database-schema.md` §O (Event Store), §Q (CryptoKey/TenantSequences), §1 (conventions), F.4/F.6/K.3 (read-models that FK the journal).
- Design — `docs/design/2026-06-16-event-store.md`.
- Stack — `docs/design/2026-06-16-stack-and-dependencies.md` (Persistence, Distributed cache + pub/sub, Crypto/secrets, Background jobs).
- Resolved baselines — `docs/design/2026-06-16-decisions-pending-confirmation.md` (#8 `IRunOnceGuard`, #10 crypto-shred completeness); both are decided here in §9.

**Binding conventions for every file in this subsystem:**
- Namespace `NomNomzBot.*`. `.NET 10 / C# 14 / EF Core 10`. File-scoped namespaces. `Nullable` enabled. Async all the way (no `.Result`/`.Wait()`).
- `Result` / `Result<T>` (`NomNomzBot.Application.Common.Models`) over exceptions/null. Never return null.
- Repository + `IUnitOfWork`; no raw `DbContext` in controllers. DI via typed interfaces. No MediatR, no Roslyn.
- App JSON serialization = **Newtonsoft.Json** (per task convention; matches `[VC:JSON]` columns in the schema).
- Surrogate guid PKs via `Guid.CreateVersion7()`. **Exception (binding here):** journals/logs/snapshots use `bigint` identity PKs (`Id`), per schema §1.1. Twitch ids are indexed attribute columns.
- Tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`). **`ITenantScoped.BroadcasterId` is being widened `string`→`Guid`** (schema §1.1) — this subsystem's entities use `Guid? BroadcasterId` (nullable because journal/projection/snapshot rows may be platform-global).
- Append-only tables carry **`CreatedAt` only** — no `UpdatedAt`, no soft-delete. They do **not** inherit `BaseEntity` (which carries `UpdatedAt`).
- Responses: `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]` + `[Route("api/v{version:apiVersion}/...")]`.

> **`Guid? BroadcasterId` note.** `ITenantScoped` (post-widening) exposes a non-null `Guid BroadcasterId`. The journal/projection/snapshot/idempotency rows allow a **platform-global** scope (null), so these entities **do not** implement `ITenantScoped`; they expose `Guid? BroadcasterId` directly and are excluded from the tenant global query filter (the store reads across tenants during replay/projection by design). Tenant isolation for journal *reads* is enforced in the service layer via `ICurrentTenantService` + an explicit `BroadcasterId` predicate, never an ambient filter.

---

## 1. Entities (locked schema — owned by this subsystem)

All defined in `docs/design/2026-06-16-database-schema.md`. Referenced here by id; **do not redefine columns** — map them in EF exactly as the schema lists. Place entity classes in `NomNomzBot.Domain/Entities/EventStore/`; EF configs in `NomNomzBot.Infrastructure/Persistence/Configurations/EventStore/`.

| Schema id | Entity (class) | PK | Key fields / types | Append-only | Notes |
|---|---|---|---|---|---|
| **O.1** | `EventJournal` | `Id bigint` | `EventId Guid` (Unique), `BroadcasterId Guid?`, `StreamPosition long` (Unique-with-`BroadcasterId`), `EventType string(150)`, `EventVersion int`, `Source string(30)` [VC:enum] (`eventsub`\|`domain`\|`irc`\|`import`\|`federation`\|`webhook`), `Payload string` [VC:JSON], `PayloadIsEncrypted bool`, `SubjectKeyId Guid?` (FK→CryptoKey), `CorrelationId Guid?`, `CausationId Guid?`, `ActorUserId Guid?`, `ActorTwitchUserId string(50)?`, `Metadata string` [VC:JSON], `OccurredAt DateTime`, `RecordedAt DateTime` | **yes** | The **outcome/fact** log — the durable record of *what happened*, and the sole replay/projection source of truth (§1.1). Unique `EventId`; **Unique `(BroadcasterId, StreamPosition)`** (idempotent replay). `StreamPosition` app-assigned via `TenantSequences`. `Source="webhook"` = a verified third-party inbound webhook (`webhooks.md`); `Source="eventsub"` = Twitch's first-party ingest (`twitch-eventsub.md`) — distinct sources. |
| **O.1a** | `EventSubjectKey` | `Id Guid` (UUIDv7) | `EventId Guid` (FK→`EventJournal.EventId`), `BroadcasterId Guid?`, `SubjectIdHash string(64)`, `SubjectKeyId Guid` (FK→CryptoKey), `Role string(20)?` | no (`CreatedAt` only) | Multi-subject (gift sub / raid) event→DEK link. Unique `(EventId, SubjectKeyId)`. Enables per-subject shred of a shared payload. |
| **O.2** | `EventSnapshot` | `Id bigint` | `BroadcasterId Guid?`, `AggregateType string(100)`, `AggregateId string(100)`, `StreamPosition long`, `SnapshotVersion int`, `State string` [VC:JSON], `StateIsEncrypted bool`, `SubjectKeyId Guid?` (FK→CryptoKey), `CreatedAt DateTime` | yes | Folded checkpoint so replay needn't start at zero. Unique `(BroadcasterId, AggregateType, AggregateId)`. |
| **O.3** | `ProjectionCheckpoint` | `Id bigint` | `ProjectionName string(150)`, `BroadcasterId Guid?`, `LastPosition long`, `Status string(20)` [VC:enum] (`running`/`rebuilding`/`faulted`/`paused`), `LastError string?`, `LastProcessedAt DateTime?`, `UpdatedAt DateTime` | no | Per-projection consume cursor. Unique `(ProjectionName, BroadcasterId)`. (Carries `UpdatedAt`, not append-only.) |
| **O.4** | `IdempotencyKey` | `Id bigint` | `Scope string(100)`, `Key string(255)`, `BroadcasterId Guid?`, `ResultHash string(64)?`, `ExpiresAt DateTime`, `CreatedAt DateTime` | yes | At-most-once guard for events/webhooks/mutating requests. Unique `(Scope, Key, BroadcasterId)`. |
| **Q.3** | `TenantSequence` | `Id Guid` (UUIDv7) | `BroadcasterId Guid`, `SequenceName string(50)`, `NextValue long`, `UpdatedAt DateTime` | no | App-assigned per-tenant monotonic counter. Unique `(BroadcasterId, SequenceName)`. **This subsystem owns the `event_stream_position` sequence**; the economy subsystem owns `currency_ledger_position`. The append helper is shared (§3 `ITenantSequenceAllocator`). |

**Referenced, NOT owned** (other subsystems own the class + config; this subsystem only writes/reads `EventId` linkage and reads for crypto-shred):
- **Q.1 `CryptoKey`** — owned by `gdpr-crypto.md`. This subsystem holds `SubjectKeyId` FKs and *reads* `Status` to detect shredded keys; it never mutates `CryptoKey`. Shred itself is performed by `gdpr-crypto.md`'s **`ISubjectKeyService.DestroyKeyAsync`** — this subsystem only **supplies the DEK set** to shred (§3 `IEventCryptoShredLinker`).
- **F.4 `TwitchChannelEventLog`**, **F.6 `RewardRedemptions`**, **K.3 `CurrencyLedgerEntries`** — read-models that FK `EventJournal.EventId`. Owned by their domain subsystems (**F.6 by `spec/rewards.md`** — its `RewardRedemptionProjection`; F.4 by analytics; K.3 by economy). This spec defines the **projection contract** they implement (§3 `IProjection`), not their tables. **`K.3 CurrencyLedgerEntries` is the exception in this list:** it FKs `EventJournal.EventId` for lineage but is **not** a derived projection — see §1.1.

### 1.1 Log-first runtime — intake log vs. outcome log; which tables are projections, which are sources of truth

This subsystem is the **outcome/fact** half of the platform's log-first runtime (`scaling-qos.md` §2). Two durable logs exist, with non-overlapping jobs:

- **`CommandLogEntry` (schema O.11, owned by `scaling-qos.md`) is the durable INTAKE/work log.** The edge tier authorizes an inbound action (chat command, EventSub event, API mutation, inbound webhook) and appends one `CommandLogEntry` row as its only synchronous hot-path work, then ACKs. It is the worker tier's single source of pending work; rows are pruned by retention after they reach `done`.
- **`EventJournal` (O.1, owned here) is the durable OUTCOME/fact log.** The worker tier pulls a ready `CommandLogEntry`, re-checks invariants at processing time, runs the action, **emits domain events that this subsystem journals as `EventJournal` rows**, and advances projections.

**Replay and every projection derive from `EventJournal` ONLY.** `CommandLogEntry` is never a replay source — it is transient work, not history. A processed command produces one or more journaled outcome events; rebuilding a read model always re-reads `EventJournal` (or a snapshot folded from it), never the intake log.

**Two tables that FK the journal are NOT projections — they are independent append-only sources of truth, and they must never be rebuilt by `IProjection.ResetAsync`/replay:**

- **`K.3 CurrencyLedgerEntries` (economy ledger) is an independent append-only source of truth, not a derived projection.** It carries its own per-tenant monotonic position (`CurrencyLedgerEntry.TenantPosition`) allocated from `TenantSequences` (Q.3) under the `currency_ledger_position` sequence — a *separate* counter from this subsystem's `event_stream_position`. The economy writes a ledger entry and an `EventJournal` row in the same `IUnitOfWork`, setting `CurrencyLedgerEntry.EventId` for lineage; the journal row records the *fact*, the ledger row holds the *authoritative balance math* (`Amount`, `BalanceAfter`). The ledger is owned by `economy.md` and is excluded from this subsystem's projection runner/replay: replay may rebuild the **balance projection** (`CurrencyAccounts.Balance`) from the ledger, but it never rebuilds the ledger itself.

- **`WatchSessions` (schema M.2) and `WatchStreaks` (schema M.3) are projections rebuilt from `EventJournal`.** They live in the analytics/community read-model subsystem (schema Domain M, alongside `ViewerProfiles` M.1), and each implements `IProjection` (§3.3): `WatchSessionProjection` folds presence/watch-interval outcome events into M.2 append rows; `WatchStreakProjection` folds per-stream attendance into the M.3 `(BroadcasterId, UserId)` upsert. Both reset and rebuild from `EventJournal` via the projection runner like any other read model — they are not orphans and are not sources of truth.

---

## 2. Domain events

Existing convention (keep, extend — do **not** duplicate): events live in `NomNomzBot.Domain/Events/`, implement `IDomainEvent`, inherit `DomainEventBase` (the canonical base owned by `platform-conventions.md` §2.0 — `Guid EventId` UUIDv7, `Guid BroadcasterId` where `Guid.Empty` = platform-level, `DateTimeOffset OccurredAt`; this subsystem **references** it and never redefines or redeclares those members). They are the **bus** contract. The journal is a separate persistence concern keyed on these.

**No new business domain events are introduced by this subsystem.** The event store is a *durable subscriber* to the existing bus (per design doc "rides the event-bus adapter"). It consumes every `IDomainEvent` already published and persists it. Two new events are emitted **by** this subsystem to signal store/replay lifecycle to the dashboard and other handlers:

Place in `NomNomzBot.Domain/Events/EventStore/`. Both inherit `DomainEventBase`.

```csharp
namespace NomNomzBot.Domain.Events;

/// <summary>Emitted when a replay or backfill run changes state (started, progressed, completed, faulted).</summary>
public sealed record ReplayStatusChangedEvent : DomainEventBase
{
    public required Guid ReplayId { get; init; }
    public required string ProjectionName { get; init; }
    public required string Status { get; init; }          // queued|running|completed|faulted|cancelled
    public required long FromPosition { get; init; }
    public required long ToPosition { get; init; }
    public required long ProcessedCount { get; init; }
    public string? Error { get; init; }
}

/// <summary>Emitted after a subject's crypto-shred DEK set has been linked/destroyed for journal payloads.</summary>
public sealed record EventPayloadShreddedEvent : DomainEventBase
{
    public required string SubjectIdHash { get; init; }
    public required int EventsAffected { get; init; }      // journal rows whose payload became unreadable
    public required int KeysDestroyed { get; init; }       // DEKs marked destroyed for this subject
    public Guid? ErasureRequestId { get; init; }
}
```

> **Why these two only.** The store does not invent per-action events (those already exist on the bus). It only needs to surface *its own* long-running operations (replay) and its *compliance side effect* (payload shred) so the dashboard activity feed and the compliance audit pipeline can react. Everything else flows through the existing event catalogue.

---

## 3. Service interfaces

All interfaces in `NomNomzBot.Application/Contracts/EventStore/`. Implementations in `NomNomzBot.Infrastructure/EventStore/`. Every fallible op returns `Result`/`Result<T>`.

### 3.1 `IEventJournal` — append + ordered read (the core)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IEventJournal
{
    // Persists one event as the next per-tenant StreamPosition. Allocates the position via
    // ITenantSequenceAllocator under the per-tenant lock IN THE SAME transaction as the insert.
    // Idempotent on EventId: a duplicate EventId is a no-op success returning the existing record.
    // Side effect: one EventJournal row inserted (CreatedAt/RecordedAt set); no domain event re-published.
    Task<Result<EventRecord>> AppendAsync(AppendEventRequest request, CancellationToken cancellationToken = default);

    // Atomically appends a batch in one transaction with contiguous StreamPositions for the tenant.
    // All-or-nothing: any failure rolls back the whole batch. Idempotent per EventId within the batch.
    // State change: N EventJournal rows; sequence advanced by the count of non-duplicate events.
    Task<Result<IReadOnlyList<EventRecord>>> AppendBatchAsync(IReadOnlyList<AppendEventRequest> requests, CancellationToken cancellationToken = default);

    // Reads a forward-ordered slice of one tenant's stream by StreamPosition (exclusive afterPosition).
    // Read-only. Used by projections/replay. Returns at most `limit` records ordered by StreamPosition asc.
    Task<Result<IReadOnlyList<EventRecord>>> ReadStreamAsync(Guid? broadcasterId, long afterPosition, int limit, CancellationToken cancellationToken = default);

    // Reads the global stream by the bigint Id (cross-tenant order) — used for platform-global projections.
    // Read-only. Ordered by Id asc, exclusive afterId, at most `limit`.
    Task<Result<IReadOnlyList<EventRecord>>> ReadAllAsync(long afterId, int limit, CancellationToken cancellationToken = default);

    // Looks up a single event by its EventId (dedupe/lineage/trace). Failure NOT_FOUND if absent.
    Task<Result<EventRecord>> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);

    // Current head StreamPosition for a tenant (0 if no events). Read-only; drives "up to date" checks.
    Task<Result<long>> GetHeadPositionAsync(Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Filtered/paged read for the audit UI (by EventType/time-range/actor). Read-only.
    Task<Result<PagedList<EventRecord>>> QueryAsync(EventJournalQuery query, CancellationToken cancellationToken = default);
}
```

### 3.2 `IEventStoreSubscriber` — durable bus subscriber (write path)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IEventStoreSubscriber
{
    // Maps a bus IDomainEvent to an AppendEventRequest and appends it (idempotent on the event's EventId).
    // This is the single integration point between IEventBus delivery and the journal.
    // Side effect: one journal row (or no-op if already persisted). Encrypts payload PII per IEventPayloadProtector.
    Task<Result<EventRecord>> CaptureAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent;
}
```

> Wiring note: a single generic `IEventHandler<IDomainEvent>`-style capture is **not** possible because handlers are resolved per concrete `TEvent`. Capture is invoked from the `EventBus` publish path via a registered `JournalingEventBusDecorator` (§7), not via 50 hand-written handlers.

#### `IJournalPostCommitHook` — post-commit observer seam (the decorator's only extension point)

Some subsystems must react to **every** journaled event without writing one handler per concrete `TEvent` (the same per-`TEvent` constraint above blocks a generic handler) — e.g. `webhooks.md`'s outbound fan-out, which enqueues a delivery for any event whose `EventType` an endpoint subscribes to. The `JournalingEventBusDecorator` is the only place that already sees every event, so it exposes a single, EventType-agnostic post-commit seam. The decorator, **after** `CaptureAsync` commits the journal row, invokes every registered hook in order (failures are isolated and logged — a faulting hook never rolls back the commit or blocks delegation to bus handlers):

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IJournalPostCommitHook
{
    // Invoked once per successfully-committed journal row, after the StreamPosition is assigned and the txn committed,
    // before the decorator delegates to the live bus handlers. Read-only w.r.t. the journal (the row is immutable).
    // Side effects belong to the hook's own subsystem (e.g. enqueue an outbound webhook delivery). Must be idempotent
    // on EventRecord.EventId (the decorator may re-invoke after a transient downstream failure). Failures are isolated.
    Task<Result> OnCommittedAsync(EventRecord committed, CancellationToken cancellationToken = default);
}
```

Register hooks as `services.AddScoped<IJournalPostCommitHook, …>()` (multi-register, like `IProjection`); the decorator resolves `IEnumerable<IJournalPostCommitHook>` per publish scope. This is the **binding** wiring for `webhooks.md` §9's `OutboundWebhookFanoutHandler`.

### 3.3 `IProjection` / `IProjectionRunner` — read-model build + checkpoints

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

// Implemented by each read-model (F.4 event log, F.6 redemptions, leaderboards, viewer profiles,
// WatchSessions M.2, WatchStreaks M.3). NOTE: K.3 CurrencyLedgerEntries is NOT a projection — it is an
// independent append-only source of truth (§1.1); only its derived balance projection rebuilds via replay.
public interface IProjection
{
    // Stable unique name; matches ProjectionCheckpoint.ProjectionName. Constant per implementation.
    string Name { get; }

    // True if this projection consumes the global cross-tenant stream (BroadcasterId == null checkpoint),
    // false if it runs once per tenant. Drives which checkpoint row(s) the runner manages.
    bool IsGlobal { get; }

    // The EventTypes this projection cares about; the runner skips others. Empty = all types.
    IReadOnlySet<string> SubscribedEventTypes { get; }

    // Applies one event to the read model. MUST be idempotent (safe to re-apply during replay):
    // upsert keyed on EventId/natural key, never blind insert. Mutates the read-model table only.
    Task<Result> ApplyAsync(EventRecord @event, CancellationToken cancellationToken = default);

    // Resets the read model to empty for the given scope (null = all tenants) before a rebuild-from-zero.
    // Side effect: deletes/truncates this projection's derived rows for the scope.
    Task<Result> ResetAsync(Guid? broadcasterId, CancellationToken cancellationToken = default);
}

public interface IProjectionRunner
{
    // Advances ONE projection from its checkpoint to the stream head, applying events in order and
    // persisting the new LastPosition after each committed batch. Updates ProjectionCheckpoint.Status.
    // Returns the number of events applied. Faults set Status=faulted + LastError (no checkpoint advance past the bad event).
    Task<Result<long>> RunOnceAsync(string projectionName, Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Reads a projection's checkpoint (position, status, lag, last error). Read-only.
    Task<Result<ProjectionCheckpointDto>> GetCheckpointAsync(string projectionName, Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Lists all projection checkpoints (optionally one tenant) for the ops dashboard. Read-only.
    Task<Result<IReadOnlyList<ProjectionCheckpointDto>>> ListCheckpointsAsync(Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Sets Status=paused so the background driver skips it (manual intervention). No event replay.
    Task<Result> PauseAsync(string projectionName, Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Clears paused/faulted back to running so the driver resumes from LastPosition.
    Task<Result> ResumeAsync(string projectionName, Guid? broadcasterId, CancellationToken cancellationToken = default);
}
```

### 3.3a `IEventUpcaster` — event schema evolution (versioning + upcasting)

A **schema change is never a domain event** and never enters `EventJournal`; it is an ordered EF Core **migration** (the migrations history is its own replayable sequence). The journal is **immutable** — historical rows keep their original `EventVersion` and `Payload` forever. When the *shape* of an event type changes, replay still has to read the old rows, handled two ways:

- **Additive change (default):** add only optional fields. Newtonsoft tolerates missing/extra members, so old rows deserialize into the new shape with no upcaster — bump nothing.
- **Breaking change:** raise the type's current version and register an `IEventUpcaster` that rewrites an old payload into the next version's shape **on read**. The store chains upcasters (`v1→v2→v3`) until the stored payload reaches the current version, so `IProjection.ApplyAsync` and all replay only ever see the current shape. Old rows are never rewritten. (The `IEventUpcaster` contract is defined once, in §3.6.)

**Binding wiring:** each event type declares its current version (a `const` on the event class); the append path stamps `EventJournal.EventVersion` with it. Upcasters register multi-instance — `services.AddSingleton<IEventUpcaster, …>()` — and the read path (`IEventStore` deserialization behind `EventRecord`) applies the matching chain. **Snapshots (O.2) carry `SnapshotVersion`** and follow the same rule: a snapshot whose `SnapshotVersion` is stale is ignored and the projection folds forward from an earlier snapshot or from zero, so a state-shape change never requires rewriting stored snapshots.

### 3.4 `IReplayService` — backfill / DR / heal

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IReplayService
{
    // Rebuilds ONE projection from zero (or from a snapshot if available): ResetAsync, set checkpoint=0,
    // then replay the whole stream for the scope. Long-running; runs under IRunOnceGuard on SaaS.
    // Emits ReplayStatusChangedEvent at start/progress/finish. Returns a handle to poll.
    Task<Result<ReplayHandle>> RebuildProjectionAsync(RebuildProjectionRequest request, CancellationToken cancellationToken = default);

    // Re-applies a bounded position range to a projection WITHOUT reset (retrigger/heal a missed window).
    // Idempotent via IProjection.ApplyAsync upserts. Emits ReplayStatusChangedEvent.
    Task<Result<ReplayHandle>> ReplayRangeAsync(ReplayRangeRequest request, CancellationToken cancellationToken = default);

    // Re-publishes a set of events back onto IEventBus (heal a handler/widget that missed them).
    // Does NOT touch the journal. Side effect: events delivered to live handlers again.
    Task<Result<ReplayHandle>> RepublishAsync(RepublishRequest request, CancellationToken cancellationToken = default);

    // Status of a running/finished replay (poll target for ReplayHandle). Read-only.
    Task<Result<ReplayStatusDto>> GetStatusAsync(Guid replayId, CancellationToken cancellationToken = default);

    // Requests cooperative cancellation of an in-flight replay. Sets status=cancelled at the next batch boundary.
    Task<Result> CancelAsync(Guid replayId, CancellationToken cancellationToken = default);
}
```

### 3.5 `ISnapshotStore` — optional fold checkpoints (perf)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface ISnapshotStore
{
    // Upserts the snapshot for (BroadcasterId, AggregateType, AggregateId) at a StreamPosition.
    // State is serialized [VC:JSON]; encrypted under SubjectKeyId when StateIsEncrypted is requested.
    // Side effect: one EventSnapshot row (insert or replace).
    Task<Result> SaveAsync(SaveSnapshotRequest request, CancellationToken cancellationToken = default);

    // Loads the latest snapshot for an aggregate (so replay starts at StreamPosition, not 0). NOT_FOUND if none.
    Task<Result<SnapshotRecord>> GetLatestAsync(Guid? broadcasterId, string aggregateType, string aggregateId, CancellationToken cancellationToken = default);

    // Deletes snapshots for an aggregate (forces replay-from-zero, or compliance cleanup). Idempotent.
    Task<Result> DeleteAsync(Guid? broadcasterId, string aggregateType, string aggregateId, CancellationToken cancellationToken = default);
}
```

### 3.6 `IEventUpcaster` / `IEventUpcasterRegistry` — schema evolution

Upcasters are **compiled code, keyed by `(EventType, EventVersion)`** (schema audit F3 — no DB table). The journal stores raw `EventVersion`; on read, records are upcast to current before `ApplyAsync`.

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IEventUpcaster
{
    string EventType { get; }
    int FromVersion { get; }    // transforms FromVersion -> FromVersion + 1
    // Pure transform of the JSON payload from one version to the next. No side effects.
    Result<string> Upcast(string payloadJson);
}

public interface IEventUpcasterRegistry
{
    // Applies the chain of registered upcasters to bring a payload from `fromVersion` to the current
    // version for `eventType`. Returns the payload unchanged when already current. Read-only/pure.
    Result<UpcastResult> UpcastToCurrent(string eventType, int fromVersion, string payloadJson);

    // Current (highest) known version for an event type — the version new appends are stamped with.
    int CurrentVersion(string eventType);
}
```

### 3.7 `ITenantSequenceAllocator` — per-tenant monotonic positions (Q.3)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface ITenantSequenceAllocator
{
    // Reads-and-increments TenantSequences.NextValue for (BroadcasterId, SequenceName) UNDER A ROW LOCK
    // (SELECT ... FOR UPDATE on Postgres; BEGIN IMMEDIATE write-lock on SQLite), in the AMBIENT transaction
    // (caller's IUnitOfWork). Creates the row at NextValue=1 if absent. Returns the value handed out.
    // MUST be called inside an open transaction so the allocation commits atomically with the consuming insert.
    Task<Result<long>> NextAsync(Guid broadcasterId, string sequenceName, CancellationToken cancellationToken = default);

    // Reserves a contiguous block of `count` values in one increment (batch append). Returns the first value;
    // the caller assigns first..first+count-1. Same locking/transaction rules as NextAsync.
    Task<Result<long>> NextBlockAsync(Guid broadcasterId, string sequenceName, int count, CancellationToken cancellationToken = default);
}
```

> Constant: `public const string EventStreamPositionSequence = "event_stream_position";` (define on the implementation; the economy subsystem defines `currency_ledger_position`).

### 3.8 `IEventPayloadProtector` — PII encryption at append / decryption at read

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IEventPayloadProtector
{
    // If the request carries PII subjects, encrypts the payload under the resolved per-subject DEK(s)
    // via the crypto subsystem (AES-256-GCM, AAD = tenantId‖eventType‖subjectIdHash‖keyVersion), sets
    // PayloadIsEncrypted=true, and returns the SubjectKeyId set to persist (single -> EventJournal.SubjectKeyId;
    // multi -> EventSubjectKeys rows). No-op pass-through when the event has no PII.
    Task<Result<ProtectedPayload>> ProtectAsync(AppendEventRequest request, CancellationToken cancellationToken = default);

    // Decrypts an encrypted journal payload for replay/projection. If any required DEK is destroyed
    // (crypto-shred), returns a SUCCESS result with IsReadable=false and a tombstoned payload — replay
    // must continue past shredded events, never fault. Plaintext payloads pass through unchanged.
    Task<Result<DecryptedPayload>> UnprotectAsync(EventRecord @event, CancellationToken cancellationToken = default);
}
```

### 3.9 `IEventCryptoShredLinker` — crypto-shred linkage (GDPR)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IEventCryptoShredLinker
{
    // Records a subject's per-event DEK in EventSubjectKeys for a multi-subject event (gift sub: gifter+
    // recipient; raid: raider+raided). Idempotent on (EventId, SubjectKeyId). Side effect: one link row.
    Task<Result> LinkSubjectKeyAsync(LinkSubjectKeyRequest request, CancellationToken cancellationToken = default);

    // Resolves the full DEK set that encrypts a subject's PII across journal payloads (EventJournal.SubjectKeyId
    // for single-subject events + EventSubjectKeys for shared ones). Read-only; returned to gdpr-crypto's
    // ISubjectKeyService.DestroyKeyAsync, which actually destroys the keys. Does NOT mutate CryptoKey.
    Task<Result<IReadOnlyList<Guid>>> ResolveSubjectKeysAsync(string subjectIdHash, Guid? broadcasterId, CancellationToken cancellationToken = default);

    // Called AFTER the crypto subsystem destroys the DEKs: emits EventPayloadShreddedEvent with counts for
    // the compliance audit pipeline (feeds ComplianceAuditLog.KeysShredded). No journal mutation (immutable).
    Task<Result> ReportShredAsync(ReportShredRequest request, CancellationToken cancellationToken = default);
}
```

### 3.10 `IIdempotencyGuard` — at-most-once (O.4)

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

public interface IIdempotencyGuard
{
    // Atomically claims (Scope, Key, BroadcasterId): inserts the row if new (returns IsFirst=true) or
    // returns the prior ResultHash (IsFirst=false) so the caller can short-circuit. Honors ExpiresAt.
    Task<Result<IdempotencyClaim>> TryClaimAsync(IdempotencyClaimRequest request, CancellationToken cancellationToken = default);

    // Stores the result hash for a claimed key (so a retry can return the same outcome). Side effect: update O.4 row.
    Task<Result> CompleteAsync(string scope, string key, Guid? broadcasterId, string resultHash, CancellationToken cancellationToken = default);
}
```

### 3.11 Repository (Infrastructure)

`EventJournalRepository : GenericRepository<EventJournal>` — extends the existing `GenericRepository<T>` (no new generic abstraction). Adds append-specific reads:

```csharp
namespace NomNomzBot.Infrastructure.Persistence.Repositories;

public sealed class EventJournalRepository : GenericRepository<EventJournal>
{
    public EventJournalRepository(AppDbContext db) : base(db) { }

    // Forward slice by StreamPosition for one tenant (exclusive afterPosition), ordered asc, max `limit`.
    public Task<IReadOnlyList<EventJournal>> ReadStreamAsync(Guid? broadcasterId, long afterPosition, int limit, CancellationToken ct = default);

    // Forward slice of the global stream by Id (exclusive afterId), ordered asc, max `limit`.
    public Task<IReadOnlyList<EventJournal>> ReadAllAsync(long afterId, int limit, CancellationToken ct = default);

    // Max StreamPosition for a tenant (0 when empty).
    public Task<long> GetHeadPositionAsync(Guid? broadcasterId, CancellationToken ct = default);

    // Single by EventId (dedupe).
    public Task<EventJournal?> GetByEventIdAsync(Guid eventId, CancellationToken ct = default);
}
```

> `ProjectionCheckpoint`, `EventSnapshot`, `EventSubjectKey`, `IdempotencyKey`, `TenantSequence` are accessed via the service implementations' `IApplicationDbContext` `DbSet`s + `IUnitOfWork` — no dedicated repository each (YAGNI; they're simple keyed upserts). Register their `DbSet<>` on `IApplicationDbContext`/`AppDbContext`.

---

## 4. DTOs / contracts

All in `NomNomzBot.Application/Contracts/EventStore/` (records, `Nullable` enabled). Newtonsoft.Json for the `[VC:JSON]` payload string fields; DTOs themselves are plain records.

```csharp
namespace NomNomzBot.Application.Contracts.EventStore;

// ---- Append ----
public sealed record AppendEventRequest(
    Guid EventId,
    Guid? BroadcasterId,
    string EventType,
    int EventVersion,
    string Source,                       // eventsub|domain|irc|import|federation|webhook
                                         //   webhook: a verified third-party inbound webhook (webhooks.md §3.2) — built as
                                         //   AppendEventRequest(Source="webhook", EventType="webhook.<provider>.<kind>",
                                         //   EventId = WebhookEventId(broadcasterId, endpointId, providerEventId) — a deterministic
                                         //   UUIDv5 SALTED with endpoint+tenant (webhooks.md §3.2.1), NOT the bare provider id.
                                         //   The salt is mandatory: the global Unique(EventJournal.EventId) would otherwise COLLIDE
                                         //   across tenants/endpoints receiving the same small/guessable provider id (e.g. GitHub
                                         //   delivery id, Ko-fi sequential txn id), aliasing one tenant's append onto another's row
                                         //   or silently swallowing it. This differs from eventsub (safe to derive from the bare
                                         //   Twitch message-id only because those are globally-unique UUIDs; webhook provider ids are not).
    string PayloadJson,                  // serialized via Newtonsoft.Json by the caller
    string MetadataJson,
    DateTime OccurredAt,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    Guid? ActorUserId = null,
    string? ActorTwitchUserId = null,
    IReadOnlyList<EventPiiSubject>? PiiSubjects = null); // drives encryption + EventSubjectKeys linkage

public sealed record EventPiiSubject(string SubjectIdHash, string? Role); // Role: gifter|recipient|raider|raided|...

public sealed record EventRecord(
    long Id,
    Guid EventId,
    Guid? BroadcasterId,
    long StreamPosition,
    string EventType,
    int EventVersion,
    string Source,
    string PayloadJson,
    bool PayloadIsEncrypted,
    Guid? SubjectKeyId,
    Guid? CorrelationId,
    Guid? CausationId,
    Guid? ActorUserId,
    string? ActorTwitchUserId,
    string MetadataJson,
    DateTime OccurredAt,
    DateTime RecordedAt);

// ---- Journal query (audit UI) ----
public sealed record EventJournalQuery(
    Guid? BroadcasterId,
    string? EventType,
    DateTime? FromUtc,
    DateTime? ToUtc,
    Guid? ActorUserId,
    int Page,
    int PageSize);

// ---- Projections ----
public sealed record ProjectionCheckpointDto(
    string ProjectionName,
    Guid? BroadcasterId,
    long LastPosition,
    long HeadPosition,
    long Lag,                            // HeadPosition - LastPosition
    string Status,                       // running|rebuilding|faulted|paused
    string? LastError,
    DateTime? LastProcessedAt,
    DateTime UpdatedAt);

// ---- Replay ----
public sealed record RebuildProjectionRequest(string ProjectionName, Guid? BroadcasterId, bool UseSnapshot = true);
public sealed record ReplayRangeRequest(string ProjectionName, Guid? BroadcasterId, long FromPosition, long ToPosition);
public sealed record RepublishRequest(Guid? BroadcasterId, long FromPosition, long ToPosition, IReadOnlyList<string>? EventTypes);
public sealed record ReplayHandle(Guid ReplayId, string Status);
public sealed record ReplayStatusDto(
    Guid ReplayId,
    string Kind,                         // rebuild|range|republish
    string ProjectionName,
    string Status,                       // queued|running|completed|faulted|cancelled
    long FromPosition,
    long ToPosition,
    long ProcessedCount,
    string? Error,
    DateTime StartedAt,
    DateTime? FinishedAt);

// ---- Snapshots ----
public sealed record SaveSnapshotRequest(
    Guid? BroadcasterId, string AggregateType, string AggregateId,
    long StreamPosition, int SnapshotVersion, string StateJson, bool Encrypt);
public sealed record SnapshotRecord(
    long Id, Guid? BroadcasterId, string AggregateType, string AggregateId,
    long StreamPosition, int SnapshotVersion, string StateJson, bool StateIsEncrypted, DateTime CreatedAt);

// ---- Upcasting ----
public sealed record UpcastResult(string PayloadJson, int ToVersion, bool Changed);

// ---- Payload protection ----
public sealed record ProtectedPayload(string PayloadJson, bool IsEncrypted, Guid? SubjectKeyId, IReadOnlyList<LinkSubjectKeyRequest> MultiSubjectLinks);
public sealed record DecryptedPayload(string PayloadJson, bool IsReadable);  // IsReadable=false => DEK shredded, tombstoned

// ---- Crypto-shred linkage ----
public sealed record LinkSubjectKeyRequest(Guid EventId, Guid? BroadcasterId, string SubjectIdHash, Guid SubjectKeyId, string? Role);
public sealed record ReportShredRequest(string SubjectIdHash, Guid? BroadcasterId, int EventsAffected, int KeysDestroyed, Guid? ErasureRequestId);

// ---- Idempotency ----
public sealed record IdempotencyClaimRequest(string Scope, string Key, Guid? BroadcasterId, DateTime ExpiresAt);
public sealed record IdempotencyClaim(bool IsFirst, string? PriorResultHash);
```

> Reuse existing `PagedList<T>` and `PaginationParams` from `NomNomzBot.Application.Common.Models` (already used by `GenericRepository`/`BaseController`). Do not introduce a parallel paging type.

---

## 5. Controller endpoints

One controller: `EventStoreController` in `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/event-store")]`, `[Authorize]`, inherits `BaseController`, returns via `ResultResponse(...)` / `GetPaginatedResponse(...)`.

**Role gate** — tenant-scoped routes are **management plane**; cross-tenant/global routes are **platform IAM (Plane C)**. `[Authorize]` + tenant resolution yields only **Gate 1** (pure entry — any authenticated caller, channel must exist). The per-route floor is enforced in **Gate 2** by calling `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` on the action key in the table's gate column **before** the service call — returning `FORBIDDEN` (403) when the caller's resolved effective level is below the floor. Tenant-scoped operations (journal read, replay of *own* channel projections) floor at **`Broadcaster`** (replay/rebuild is destructive to derived data → owner-only; not delegatable below `Broadcaster`). Plane-C rows are authorized per-action via `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, targetBroadcasterId, ...)`; the ASP.NET `[Authorize(Policy="<key>")]` policy name **is** the permission key verbatim — `audit:read` for reads (global checkpoint listing, cross-tenant replay status), `iam:manage` for the sensitive projection pause/resume mutations — and the policy's handler (owned by the IAM subsystem) delegates to `AuthorizePlatformAsync` with the same key. Use the flat key form (`audit:read`, never `iam:audit:read`). Every floor is the action's seeded global `ActionDefinition` (schema B.3); a broadcaster may raise it via `ChannelActionOverride` but not below the seeded `FloorLevel`. `channelId` in tenant routes is validated against the caller via the existing `IChannelAccessService`.

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/event-store/channels/{channelId}/journal` | GET | `EventJournalQuery` (`[FromQuery]`) | `PaginatedResponse<EventRecord>` | management / Broadcaster · `eventstore:journal:read` |
| `/event-store/channels/{channelId}/journal/{eventId}` | GET | — (`eventId` route) | `StatusResponseDto<EventRecord>` | management / Broadcaster · `eventstore:journal:read` |
| `/event-store/channels/{channelId}/projections` | GET | — | `StatusResponseDto<IReadOnlyList<ProjectionCheckpointDto>>` | management / Broadcaster · `eventstore:projection:read` |
| `/event-store/channels/{channelId}/projections/{name}/rebuild` | POST | `RebuildProjectionRequest` (body; `BroadcasterId` bound from route) | `StatusResponseDto<ReplayHandle>` | management / Broadcaster · `eventstore:projection:rebuild` (destructive) |
| `/event-store/channels/{channelId}/replay/range` | POST | `ReplayRangeRequest` | `StatusResponseDto<ReplayHandle>` | management / Broadcaster · `eventstore:replay:write` |
| `/event-store/channels/{channelId}/replay/republish` | POST | `RepublishRequest` | `StatusResponseDto<ReplayHandle>` | management / Broadcaster · `eventstore:replay:republish` |
| `/event-store/replays/{replayId}` | GET | — | `StatusResponseDto<ReplayStatusDto>` | platform · `audit:read` (replay-owner tenant match, else Plane-C) |
| `/event-store/replays/{replayId}/cancel` | POST | — | `StatusResponseDto<object>` | platform · `audit:read` (replay-owner tenant match, else Plane-C) |
| `/event-store/projections` | GET | `?broadcasterId=` optional | `StatusResponseDto<IReadOnlyList<ProjectionCheckpointDto>>` | platform · `audit:read` (cross-tenant/global) |
| `/event-store/projections/{name}/pause` | POST | `?broadcasterId=` optional | `StatusResponseDto<object>` | platform · `iam:manage` (sensitive mutation) |
| `/event-store/projections/{name}/resume` | POST | `?broadcasterId=` optional | `StatusResponseDto<object>` | platform · `iam:manage` (sensitive mutation) |

> No endpoint exposes append — appends happen only via the bus subscriber (server-internal). No endpoint exposes raw decrypted PII payloads beyond what the journal stores (payloads already hold ids/refs, PII encrypted); the audit `EventRecord` returns `PayloadJson` as stored (ciphertext when encrypted) — decryption is never an API surface.

---

## 6. Pipeline actions

**None.** This subsystem is infrastructure beneath the pipeline engine, not a pipeline action. Pipeline actions (`SendMessage`, etc.) emit domain events that the store captures via the bus; the store adds no `ICommandAction`.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs` `AddInfrastructure(...)` (extend the existing method; group under a `// Event store` block). Lifetimes mirror existing conventions (scoped where they touch `IApplicationDbContext`/`IUnitOfWork`; singleton only for stateless/registry types).

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `IEventJournal` | `EventJournal` *(service — rename to `EventJournalService` to avoid clashing with the entity)* | Scoped | Touches DbContext + `ITenantSequenceAllocator` in a transaction. |
| `IEventStoreSubscriber` | `EventStoreSubscriber` | Scoped | Resolved per publish scope. |
| `IProjectionRunner` | `ProjectionRunner` | Scoped | Iterates registered `IProjection`s. |
| `IProjection` (multi) | `TwitchChannelEventLogProjection`, `RewardRedemptionProjection`, `CurrencyBalanceProjection`, `WatchSessionProjection`, `WatchStreakProjection`, … | Scoped (multi-register, same as `ICommandAction`) | Each read-model registers one; runner resolves `IEnumerable<IProjection>`. Owned by their subsystems but registered here or via `AddEventHandlersFromAssembly`-style scan. The economy **ledger** (K.3) is a source of truth, not a projection (§1.1); only the economy **balance** read model (`CurrencyAccounts.Balance`) projects via `CurrencyBalanceProjection`. |
| `IReplayService` | `ReplayService` | Scoped | Uses `IRunOnceGuard` (no-op lite / `pg_try_advisory_lock` SaaS) to avoid double-run on multi-instance SaaS. |
| `ISnapshotStore` | `SnapshotStore` | Scoped | |
| `IEventUpcaster` (multi) | per-event upcasters | Singleton | Stateless/pure; multi-register. |
| `IEventUpcasterRegistry` | `EventUpcasterRegistry` | Singleton | Builds the `(EventType, FromVersion)` chain map from injected `IEnumerable<IEventUpcaster>`. |
| `ITenantSequenceAllocator` | `TenantSequenceAllocator` | Scoped | Per-tenant row-lock allocator; ambient-transaction aware. |
| `IEventPayloadProtector` | `EventPayloadProtector` | Scoped | Delegates to `gdpr-crypto.md`'s `IFieldCipher` (AEAD) + `ISubjectKeyService` (DEK lifecycle / shred). |
| `IEventCryptoShredLinker` | `EventCryptoShredLinker` | Scoped | Reads keys; emits `EventPayloadShreddedEvent` via `IEventBus`. |
| `IIdempotencyGuard` | `IdempotencyGuard` | Scoped | |
| `EventJournalRepository` | (self) | Scoped | Registered like the existing `ChannelRepository` etc. |
| `JournalingEventBusDecorator` | wraps `IEventBus` | Singleton (decorator) | Decorates the existing singleton `EventBus`: on publish, captures to journal (via a created scope, same pattern as `EventBus` handler resolution), then **invokes every registered `IJournalPostCommitHook.OnCommittedAsync` for the committed row (failures isolated/logged — never blocks the commit or delegation)**, then delegates to bus handlers. Registered by replacing the `IEventBus` singleton registration with the decorator over `EventBus`. |
| `IJournalPostCommitHook` (multi) | `OutboundWebhookFanoutHandler` (owned by `webhooks.md`), … | Scoped (multi-register, like `IProjection`) | Post-commit observers the decorator invokes per journaled row (§3.2). EventType-agnostic seam; each hook filters internally. |
| `EventStoreProjectionDriver` | `BackgroundService` | Hosted (singleton) | Periodically calls `IProjectionRunner.RunOnceAsync` for non-paused projections (`PeriodicTimer`; guarded by `IRunOnceGuard` on SaaS). Mirrors existing `TimerSchedulerService` registration. |

**Deployment-profile adapter variants** (DI-selected by `DeploymentProfile`/`App__DeploymentMode`, per stack doc — choose the branch the same way the schema's §1.4 adapter selects DB provider):
- **DB provider** — `ITenantSequenceAllocator` SQL differs per provider: `SELECT … FOR UPDATE` (Npgsql) vs `BEGIN IMMEDIATE` write-lock (SQLite). One interface, provider-branched implementation (or an `IRowLockStrategy` injected). No second public interface.
- **Run-once guard** — `IReplayService` + `EventStoreProjectionDriver` take `IRunOnceGuard`: **lite** = no-op; **SaaS** = `DistributedLock.Postgres` / `pg_try_advisory_lock`. (Owned by the background-jobs subsystem; consumed here.)
- **KEK custody for payload encryption** — `IEventPayloadProtector` rides `gdpr-crypto.md`'s `IKeyVault` `kms_envelope` (Azure Key Vault) vs `local_aes` branch; this subsystem does not pick the branch, it just calls `IFieldCipher`/`ISubjectKeyService`.
- **Bus transport** — `RepublishAsync` publishes via whichever `IEventBus` is wired (in-process `EventBus` lite / `RedisEventBus` SaaS); no event-store-specific branch.

---

## 8. Dependencies (stack-doc libs used)

| Lib | Party | Use here |
|---|---|---|
| `Microsoft.EntityFrameworkCore` 10.0.9 + provider (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.2 / `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9) | 2nd / 3rd | Journal/snapshot/checkpoint/sequence persistence; named query filters for soft-delete on the non-append-only tables; provider-branched row lock for `TenantSequences`. EF10 — **not** `ToJson()`/`jsonb`. |
| `SQLitePCLRaw.bundle_e_sqlite3` ≥ 3.0.3 | 2nd | Self-host engine (patched SQLite); required by the SQLite provider. |
| **Newtonsoft.Json** | — | Serialize/deserialize `[VC:JSON]` payload/metadata/state strings and DTO payload bodies (per task convention). Used via the hand-rolled `ValueConverter<T,string>` convention for the `[VC:JSON]` columns. |
| `System.Security.Cryptography` (AesGcm, HKDF, RNG) | 2nd (in-box) | *Indirect* — via `gdpr-crypto.md`'s `IFieldCipher`/`ISubjectKeyService` for payload encrypt/decrypt + crypto-shred. This subsystem calls those interfaces, never the primitives directly. |
| `System.Threading` (`PeriodicTimer`, `Channels`) | 1st (in-box) | `EventStoreProjectionDriver` background loop; bounded replay batching. |
| `DistributedLock.Postgres` 1.3.1 *(SaaS only)* | 3rd | Behind `IRunOnceGuard` so a replay/projection driver runs once across SaaS instances (stack §Background jobs, default #8). No-op on lite. |
| `Microsoft.Extensions.Logging` (`ILogger` + `[LoggerMessage]`) + OpenTelemetry | 2nd | Structured logs/traces for append/replay; `tenant_id` as a low-cardinality scope. Never log payload PII (stack §Logging). |

**Explicitly NOT used:** MediatR, Roslyn, MassTransit, Quartz/Hangfire, EFCore.NamingConventions, any JSON converter package (hand-rolled `ValueConverter` per stack §Persistence).

---

## 9. Decisions (resolved)

These are part of the plan. Each carries its cross-subsystem dependency, stated as a dependency (the implementation order belongs to the task board, not to this spec).

1. **Plaintext snapshot scrub + one-subject-per-event enforcement is the design** (decisions #10). O(1) crypto-shred holds for `[PII-shred]` ciphertext (the journal `Payload`); `[PII-scrub]` plaintext snapshot columns in read-models and any not-yet-linked multi-subject events are erased by row-level erasure. This spec models `EventSubjectKeys` linkage (§3.9), so the shred path is complete for newly-appended events. Backfilling links for historical multi-subject rows is a distinct vertical slice that this slice does not include; it depends on the linkage seam defined here.
2. **The replay/driver run-once guard is `IRunOnceGuard`** (decisions #8), owned by the background-jobs subsystem and consumed here (§7). Single-instance deployments run correctly without it; multi-instance SaaS replay **depends on** the background-jobs subsystem providing `IRunOnceGuard` (`pg_try_advisory_lock`). This is a stated dependency on that subsystem, not an open interface question — the consuming surface (`IReplayService` + `EventStoreProjectionDriver` taking `IRunOnceGuard`) is fixed.
