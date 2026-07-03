# Platform Conventions — Interface Specification

**Subsystem:** cross-cutting platform conventions. API response envelopes + versioning + RFC 9457 problem details; the rate-limiting adapter; health checks; the four SignalR hubs (Dashboard / Overlay / OBSRelay / Admin) + their auth; the deployment-profile detector + adapter registry (`ICacheService` / `IEventBus` / DB provider / `IScriptExecutor` / token-vault / EventSub transport); and `TenantResolutionMiddleware` + RLS / app-filter tenant isolation.

**Status:** implementable. Code from this directly.

**Binding conventions:** C# namespace `NomNomzBot.*`. .NET 10 / C# 14 / EF Core 10. File-scoped namespaces, `Nullable` enabled, async all the way (never `.Result`/`.Wait()`). `Result<T>` over exceptions/null. Repository + `IUnitOfWork` (no raw `DbContext` in controllers). DI via typed interfaces, no MediatR, no Roslyn. Responses are `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`. **Newtonsoft.Json for app JSON value-converters** ([VC:JSON] columns); `System.Text.Json` stays the wire serializer for controllers/SignalR. Surrogate PKs = `Guid` via `Guid.CreateVersion7()`; Twitch ids are indexed attribute columns; tenant key `BroadcasterId` is `Guid`. Soft-delete (`IsDeleted`/`DeletedAt`) global filter.

> **Load-bearing rebuild this subsystem performs (schema §1.1, §1.2):**
> 1. `ITenantScoped.BroadcasterId` is widened `string` → `Guid` (Domain). Every dependent (`CurrentTenantService`, `TenantStampInterceptor`, the EF filter) widens with it.
> 2. The live IDOR in `TenantResolutionMiddleware` is fixed: today it calls `tenantService.SetTenant(userId)` (sets tenant to the *user* id). It must resolve the caller's **channel** id (`Channels.Id` where `OwnerUserId == userId`) or an explicitly-requested channel the caller is authorized for.
> 3. The no-op `ApplyTenantFilter` (`ModelBuilderExtensions`) is replaced by EF 10 **named query filters** that read `ICurrentTenantService.BroadcasterId` at query time; Postgres adds RLS via a `DbConnectionInterceptor` that `SET`/`RESET app.tenant_id`.
> 4. Every adapter (`ICacheService`, `IEventBus`, DB provider, `IScriptExecutor`, token vault, EventSub transport, rate limiter) is selected by the deployment profile in DI, not hard-wired to Postgres/Redis as today.

---

## 0. Persistence / data-access conventions (cross-cutting default)

These govern **every** subsystem's services and controllers, not just this one. Repository + `IUnitOfWork` over the EF Core `AppDbContext`; named query filters (tenant + soft-delete) and the RLS connection interceptor are configured here (schema §1.2). The transaction-boundary rule below is the platform default that every other spec inherits — individual specs need not restate it, but their multi-write methods are bound by it.

**`IUnitOfWork` is the default transaction boundary.** Any operation that performs **more than one write**, or that requires atomicity across writes, runs inside a single unit of work:

- **Inject `IUnitOfWork` + the typed repositories** the operation touches. Never inject or reach for a raw `DbContext` in a service or controller — the `DbContext` is an infrastructure detail behind the repository/UoW seam.
- **Stage all writes, then commit once: exactly one `CompleteAsync()` per logical operation.** No scattered per-call `SaveChanges`/`CompleteAsync` inside one logical op — the intermediate writes are not yet durable and a later step's failure must be able to undo them.
- **Rollback on failure — all-or-nothing.** If any step fails, the whole unit of work is discarded; partial state is never persisted. Return `Result.Failure(...)`; do not leave a half-applied mutation behind.
- **Single transaction per logical operation**, even when it spans several repositories/entities.

**When it applies.** Multi-entity mutations — the canonical case is *debit currency + write the ledger row + update the leaderboard*, which must commit together or not at all; likewise *create + write index*, *apply event + project read model*, *grant + write audit*, *suspend tenant + emit + audit*. Domain events raised by the operation are dispatched against the **committed** state (after `CompleteAsync`), never before.

**When it doesn't change anything.** Pure reads and trivial single-writes still flow through the same repository / `IUnitOfWork` seam — there is no second access path — they simply resolve to one staged write + one `CompleteAsync`. The rule adds no ceremony for the simple case; it only makes the boundary explicit for the compound case.

**Log-first synergy.** `IUnitOfWork` is the atomic primitive on **both** sides of the log-first flow: the command-store **ingest append** (the inbound command/event is written atomically) and the downstream **apply → projection** step (the event is applied and its read-model projection written) are each their own all-or-nothing unit of work — an apply that updates state but fails to project, or vice-versa, rolls back whole. This is what keeps the command store and its projections from diverging.

---

## 1. Entities (owned by this subsystem)

All defined in the LOCKED schema `2026-06-16-database-schema.md`; **referenced, not redefined**. Conventions from schema §1 apply (UUIDv7 `Id`, `CreatedAt`/`UpdatedAt`, `[VC:enum]`/`[VC:JSON]` portable converters, append-only = `CreatedAt` only).

| Schema ref | Entity | Key fields this subsystem reads/writes |
|---|---|---|
| **P.12** | `DeploymentProfile` `[GLOBAL, single-row]` | `Id guid`; `Mode` (`saas`\|`self_host_lite`\|`self_host_full`); `WasAutoDetected bool`; `DbProvider` (`postgres`\|`sqlite`); `CacheProvider` (`redis`\|`in_memory`); `EventSubTransport` (`websocket`\|`conduit_webhook`); `CodeExecutor` (`wasmtime`\|`jint`); `TokenVault` (`kms_envelope`\|`local_aes`); `ExposureModel` (`managed_edge`\|`opt_in_tunnel`); `RlsEnabled bool`; `DefaultGuidanceLevel` (`novice`\|`expert`); `InstanceId guid Unique` |
| **P.13** | `FeatureFlag` `[GLOBAL]` | `Id guid`; `Key string(100) Unique`; `IsEnabledGlobally bool`; `RolloutPercentage int`; `MinTierId guid FK→BillingTier Null`; `MinTierKey string(20) Null`; `RequiresConsent string(50) Null`; `DeploymentMode string(20) Null` |
| **P.13** | `FeatureFlagOverride` `[tenant]` | `Id guid`; `FeatureFlagId guid FK`; `BroadcasterId guid FK→Channels`; `IsEnabled bool`; `Reason string(255) Null`; `ExpiresAt timestamp Null`. Unique `(FeatureFlagId, BroadcasterId)` |
| **P.11** | `AppSetting` `[global+tenant]` | `Id guid`; `BroadcasterId guid FK→Channels Null` (null = global); `Category string(50)`; `Key string(255)`; `Value text Null` [VC:JSON]; `ValueType` (`string`\|`int`\|`bool`\|`json`\|`secret`); `ConfigSchemaVersion int`; `SecureValueCipher string(4096) Null` [PII-shred]; `IsSecret bool`. Unique `(BroadcasterId, Category, Key)`. **Supersedes the int-keyed `Configuration` entity SystemController currently uses.** |
| **Q.3** | `TenantSequences` `[tenant]` | `Id guid`; `BroadcasterId guid FK→Channels`; `SequenceName string(50)`; `NextValue bigint`; `UpdatedAt`. Unique `(BroadcasterId, SequenceName)` |
| **O.9** | `IamAuditLog` `[APPEND-ONLY]` | `Id bigint`; `PrincipalId guid`; `Permission string(60)`; `TargetBroadcasterId guid Null`; `BreakGlass bool`; `Outcome` (`allowed`\|`denied`); `SourceIpCipher string(255) Null`; `OccurredAt timestamp` — written when a hub/middleware grants or denies a cross-tenant / privileged resolution |

`ITenantScoped` (Domain, `BroadcasterId` now `Guid`) is the marker that drives the global query filter + RLS for every tenant-owned table. Twitch-id columns (`Channels.TwitchChannelId`, `Users.TwitchUserId`, `Channels.OverlayToken`) are indexed attributes used by hub auth, never FKs.

---

## 2. Domain events

### 2.0 `DomainEventBase` — the canonical, authoritative event base (cross-cutting)

Defined here because it is cross-cutting: **every** domain event across **every** subsystem inherits this base. This is the single authoritative definition every event-defining spec references; no other spec redefines it.

```csharp
namespace NomNomzBot.Domain.Events;

public abstract record DomainEventBase
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; init; }                 // Guid, NOT string? — the locked UUIDv7 tenant key
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- **`BroadcasterId` is `Guid`** — consistent with the locked UUIDv7 surrogate-key decision and the `ITenantScoped.BroadcasterId` `string`→`Guid` widen (schema §1.1). It is **not** `string?`.
- **ALL domain events inherit `DomainEventBase`** and **must NOT redeclare `BroadcasterId`** (nor `EventId` / `OccurredAt`). Subsystem events add only their own payload fields. Platform-level events (no tenant) leave `BroadcasterId` at its default `Guid.Empty`.

New platform events live in `NomNomzBot.Domain/Events/Platform/` and derive from `DomainEventBase`.

```csharp
namespace NomNomzBot.Domain.Events.Platform;

// Raised once at boot after the profile is detected/loaded and the adapter registry is built.
// Inherits DomainEventBase (EventId / BroadcasterId / OccurredAt); BroadcasterId left default (platform-level).
public sealed record DeploymentProfileResolvedEvent : DomainEventBase
{
    public required Guid InstanceId { get; init; }
    public required string Mode { get; init; }               // saas | self_host_lite | self_host_full
    public required bool WasAutoDetected { get; init; }
    public required string DbProvider { get; init; }
    public required string CacheProvider { get; init; }
    public required string EventSubTransport { get; init; }
    public required string CodeExecutor { get; init; }
    public required string TokenVault { get; init; }
    public required string ExposureModel { get; init; }
    public required bool RlsEnabled { get; init; }
}

// Raised when a feature flag's effective state changes for a tenant (override set/cleared/expired)
// or globally (rollout %, global toggle). Consumers invalidate cached evaluations.
// Inherits DomainEventBase; BroadcasterId default (Guid.Empty) = global definition change, set = tenant override.
public sealed record FeatureFlagChangedEvent : DomainEventBase
{
    public required string FlagKey { get; init; }
    public required bool IsEnabledGlobally { get; init; }
    public required int RolloutPercentage { get; init; }
    public bool? TenantOverrideValue { get; init; }
}

// Raised when a global or per-tenant AppSetting is written. Consumers reload cached config.
// Inherits DomainEventBase; BroadcasterId default (Guid.Empty) = global setting.
public sealed record AppSettingChangedEvent : DomainEventBase
{
    public required string Category { get; init; }
    public required string Key { get; init; }
    public required string ValueType { get; init; }
    public required bool IsSecret { get; init; }
}

// Raised when a privileged / cross-tenant tenant resolution is allowed or denied
// (drives the O.9 IamAuditLog append). Outcome mirrors IamAuditLog.Outcome.
// Inherits DomainEventBase; BroadcasterId carries the target tenant.
public sealed record TenantAccessEvaluatedEvent : DomainEventBase
{
    public required Guid ActorUserId { get; init; }
    public required string Outcome { get; init; }            // allowed | denied
    public required bool BreakGlass { get; init; }
}
```

---

## 3. Service interfaces

All in `NomNomzBot.Application/Common/Interfaces/` unless noted. Implementations in `NomNomzBot.Infrastructure/Services/Platform/` unless an existing folder is named. Async, `Result<T>` where the call can fail in a domain sense (pure lookups return the value directly).

### 3.1 `ICurrentTenantService` — **EXTEND existing** (`Application/Common/Interfaces/ICurrentTenantService.cs`)

`BroadcasterId` widens `string?` → `Guid?`. `SetTenant` takes a `Guid`.

```csharp
public interface ICurrentTenantService
{
    Guid? BroadcasterId { get; }                 // widened string? -> Guid?
    bool HasTenant { get; }                      // BroadcasterId.HasValue
    void SetTenant(Guid broadcasterId);          // sets the per-scope tenant; idempotent within a scope
    void Clear();                                // drops tenant context (background-service reuse across tenants)
}
```
- `SetTenant` / `Clear` — mutate per-request scope state only; the EF named query filter + RLS interceptor read `BroadcasterId` on the next query. No I/O.

### 3.2 `IChannelAccessService` — **EXTEND existing** (`Application/Common/Interfaces/IChannelAccessService.cs`)

Widen ids to `Guid`; add the owner-channel resolver the middleware needs to fix the IDOR.

```csharp
public interface IChannelAccessService
{
    // True iff userId may act under channelId (owns it, actively moderates it, or is a platform IAM principal with tenant:access).
    Task<bool> CanResolveTenantAsync(Guid userId, Guid channelId, CancellationToken ct = default);

    // The caller's own tenant: Channels.Id where OwnerUserId == userId and not soft-deleted. None if the user owns no channel.
    Task<Result<Guid>> ResolveOwnChannelAsync(Guid userId, CancellationToken ct = default);
}
```
- `CanResolveTenantAsync` — read-only authorization check across `ChannelMemberships` (B.1) / `Channels.OwnerUserId` (A.2) / `IamRoleAssignments` (C.5); a privileged/cross-tenant `true` emits `TenantAccessEvaluatedEvent` and an `IamAuditLog` (O.9) row.
- `ResolveOwnChannelAsync` — read-only; `Failure("CHANNEL_NOT_FOUND")` when the user owns no channel (drives the middleware's no-tenant path for fresh accounts).

### 3.3 `IDeploymentProfileService` — **NEW** (`Application/Common/Interfaces/`)

The boot-time detector + the runtime accessor for the resolved profile.

```csharp
public interface IDeploymentProfileService
{
    DeploymentProfileSnapshot Current { get; }        // the immutable resolved profile (set once at boot)

    // Boot path: detects (Docker/Postgres/Redis reachable? -> full; else lite), honoring an explicit
    // App__DeploymentMode override; persists/updates the single-row DeploymentProfile (P.12); returns it.
    // Emits DeploymentProfileResolvedEvent. Idempotent: re-running returns the persisted row unchanged.
    Task<Result<DeploymentProfileSnapshot>> DetectAndPersistAsync(CancellationToken ct = default);
}

public sealed record DeploymentProfileSnapshot(
    Guid InstanceId,
    DeploymentMode Mode,
    bool WasAutoDetected,
    DbProviderKind DbProvider,
    CacheProviderKind CacheProvider,
    EventSubTransportMode EventSubTransport,
    CodeExecutorKind CodeExecutor,
    TokenVaultKind TokenVault,
    ExposureModelKind ExposureModel,
    bool RlsEnabled,
    GuidanceLevel DefaultGuidanceLevel);
```
- `DetectAndPersistAsync` — the **only** writer of `DeploymentProfile` (P.12). Probes infra, resolves every adapter kind, upserts the single row, emits `DeploymentProfileResolvedEvent`. Called once from `Program` before the host runs; the resolved kinds drive §7 DI branching.
- **Host-capabilities probe (first-run sizing).** As part of the same boot detection, the service probes the **host's CPU core count and available memory** (`Environment.ProcessorCount` + the process/cgroup memory limit) and uses them to **size the worker pools / concurrency / resource limits to fit the machine** — this is what lets a self-host install run well on whatever box it lands on (a 2-core NUC vs a 32-core server) without hand-tuning. The probed sizing is applied to the `Scaling:*` knobs (`scaling-qos.md` §9) **unless the operator set an explicit override**, which always wins. The probed values are advisory inputs to sizing, not a persisted `DeploymentProfile` column.
- `Current` — in-memory accessor; throws if read before `DetectAndPersistAsync` completed (fail-closed boot ordering).

Enums (Domain, `NomNomzBot.Domain/Enums/Deployment/`, all `[VC:enum]` when persisted): `DeploymentMode {Saas, SelfHostLite, SelfHostFull}`, `DbProviderKind {Postgres, Sqlite}`, `CacheProviderKind {Redis, InMemory}`, `EventSubTransportMode {WebSocket, ConduitWebhook}`, `CodeExecutorKind {Wasmtime, Jint}`, `TokenVaultKind {KmsEnvelope, LocalAes}`, `ExposureModelKind {ManagedEdge, OptInTunnel}`, `GuidanceLevel {Novice, Expert}`.

> **`DefaultGuidanceLevel` is set by a first-run "Simple vs Advanced" wizard choice — never a silent default.** The first-run setup wizard explicitly asks the operator to pick a guidance level: **Simple** → `Novice`, **Advanced** → `Expert`. This is a deliberate prompt, not an inferred value; the persisted `DeploymentProfile.DefaultGuidanceLevel` is the answer. If the wizard is bypassed/non-interactive, it **falls back to `Novice` (Simple)** — the safe novice default. This deployment value is only the **seed default** for new users; the live per-user value is `UserPreferences.GuidanceLevel` (schema R.2), adjustable anytime.

> **`EventSubTransportMode` selects PROFILE behavior, not the wire transport.** This deployment-profile enum picks the per-instance EventSub delivery strategy: `self_host_lite` = `WebSocket`, `saas` = `ConduitWebhook`. It is intentionally distinct from `twitch-eventsub`'s 3-member wire enum `EventSubTransportKind {WebSocket, Conduit, Webhook}`, which describes the actual per-subscription transport on the Twitch API. The two-member profile mode and the three-member wire kind do not collide and are not unified — the profile mode maps to a wire-transport choice (`ConduitWebhook` ⇒ a `Conduit` + `Webhook` wire pair), it is not the same type.

### 3.4 `IFeatureFlagService` — **NEW**

Evaluates `FeatureFlag` (P.13) + `FeatureFlagOverride` against the current tenant/tier/deployment.

```csharp
public interface IFeatureFlagService
{
    // Effective on/off for the current tenant. Order: tenant override (unexpired) > global toggle &&
    // rollout-% hash(BroadcasterId,Key) > tier floor (MinTierId) > deployment-mode gate > consent gate.
    Task<bool> IsEnabledAsync(string flagKey, CancellationToken ct = default);

    // Same, for an explicit tenant (background services that have no ambient ICurrentTenantService).
    Task<bool> IsEnabledForAsync(string flagKey, Guid broadcasterId, CancellationToken ct = default);

    Task<IReadOnlyList<FeatureFlagStateDto>> GetAllForCurrentTenantAsync(CancellationToken ct = default);

    // Sets/clears a per-tenant override (upsert on (FeatureFlagId, BroadcasterId)); emits FeatureFlagChangedEvent.
    Task<Result> SetOverrideAsync(SetFeatureFlagOverrideRequest request, CancellationToken ct = default);
}
```
- `IsEnabledAsync` / `IsEnabledForAsync` — read-only evaluation; result cached via `ICacheService` (key `ff:{key}:{broadcasterId}`) and invalidated by `FeatureFlagChangedEvent`. No write.
- `SetOverrideAsync` — upserts a `FeatureFlagOverride` row, soft-respecting `ExpiresAt`; emits `FeatureFlagChangedEvent` (invalidates cache). `Failure("NOT_FOUND")` if `flagKey` unknown.

### 3.5 `IAppSettingsService` — **NEW** (supersedes ad-hoc `Configuration` access in `SystemController`)

Typed read/write over `AppSetting` (P.11), global and per-tenant, with secret handling routed to the token vault.

```csharp
public interface IAppSettingsService
{
    Task<Result<T>> GetGlobalAsync<T>(string category, string key, CancellationToken ct = default);
    Task<Result<T>> GetForTenantAsync<T>(string category, string key, CancellationToken ct = default); // current tenant
    Task<Result> SetGlobalAsync(SetAppSettingRequest request, CancellationToken ct = default);
    Task<Result> SetForTenantAsync(SetAppSettingRequest request, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string category, string key, CancellationToken ct = default); // decrypts SecureValueCipher
}
```
- `Get*<T>` — reads `Value`, deserializes by `ValueType` (Newtonsoft.Json for `json`); `Failure("NOT_FOUND")` when absent. Never returns a decrypted secret (use `GetSecretAsync`).
- `Set*` — upsert on the unique key; when `IsSecret`, the plaintext is encrypted into `SecureValueCipher` via the token-vault adapter (never persisted to `Value`); emits `AppSettingChangedEvent`.
- `GetSecretAsync` — decrypts `SecureValueCipher` through the active `TokenVaultKind` adapter; null when unset.

### 3.6 `ITenantSequenceAllocator` — portable per-tenant monotonic counter (schema §1.4, Q.3)

> **Owned by `event-store.md` §3.7** (full contract there: `Task<Result<long>> NextAsync(...)` + `Task<Result<long>> NextBlockAsync(...)`, provider-branched `SELECT … FOR UPDATE` / `BEGIN IMMEDIATE`). `economy.md` §7 also consumes this same interface (`PostgresTenantSequenceAllocator` / `SqliteTenantSequenceAllocator`). Listed here only because the deployment-profile adapter registry selects its provider-branched impl — this subsystem does not author it. Name and shape are the event-store's; the earlier draft's `ITenantSequenceService`/`Task<long>` form is superseded by the `Result<long>` contract.

Serializes per `(BroadcasterId, SequenceName)`; mutates the `TenantSequences` row inside the consuming write's `IUnitOfWork` transaction so the increment and the insert commit atomically (no double-position collision under concurrency).

### 3.7 `IRateLimiterPartitionStore` — **NEW**, profile adapter (`Application/Common/Interfaces/`)

The ASP.NET Core `RateLimiter` policies (§5 host wiring) read the partition counter through this so SaaS is distributed. In-box limiter glue stays in the host; only the counter backing is abstracted.

```csharp
public interface IRateLimiterPartitionStore
{
    // Atomically increments the window counter for partitionKey and reports whether the
    // request is permitted under permitLimit for the given window. Distributed on SaaS.
    Task<RateLimitLease> AcquireAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken ct = default);
}

public readonly record struct RateLimitLease(bool IsAcquired, int Remaining, TimeSpan RetryAfter);
```
- `AcquireAsync` — Redis adapter (SaaS): atomic `INCR`+`EXPIRE` (or a fixed-window Lua script) so the 120/min + 10/min limits hold cluster-wide. In-memory adapter (lite): wraps the in-box partitioned counter; per-instance is correct for single node.

### 3.8 `IRunOnceGuard` — **NEW**, profile adapter

Gate hosted-service work that must fire once across a multi-node SaaS cluster. No-op on lite.

```csharp
public interface IRunOnceGuard
{
    // Tries to acquire a named lease (e.g. "timers:tick", "token-refresh"). Releases on dispose.
    // Lite: always granted. SaaS: pg_try_advisory_lock / DistributedLock.Postgres.
    Task<IAsyncDisposable?> TryAcquireAsync(string resourceName, TimeSpan ttl, CancellationToken ct = default);
}
```
- `TryAcquireAsync` — returns a disposable lease on success, `null` when another instance holds it. Existing `BackgroundService`s (`TimerSchedulerService`, token refresh, stream poll) wrap their tick in this; the guard is a hard prerequisite for any multi-instance SaaS deployment (single-node lite is unaffected — the lease is always granted).

### 3.9 `IScriptExecutor` — **consumed, not redefined** (owner: `custom-code.md` §3.1)

Declared here only because the deployment-profile registry **selects** the adapter; this subsystem registers the impl, it does not author the interface or the execution semantics. The authoritative contract is `custom-code.md` §3.1 — do **not** show a diverging body here:

```csharp
// AUTHORITATIVE: custom-code.md §3.1 (NomNomzBot.Application.Contracts.CustomCode). Reference only.
public interface IScriptExecutor
{
    ScriptRuntimeKind Runtime { get; }   // "jint" | "wasmtime" — diagnostics/health
    Task<Result<ScriptCompilation>> CompileAsync(string sourceCode, CancellationToken cancellationToken = default);
    Task<Result<ScriptExecutionOutcomeResult>> ExecuteAsync(
        ScriptExecutionRequest request, ScriptCapabilityGrant grant, IScriptHostBridge bridge,
        CancellationToken cancellationToken = default);
}
```
- Selected by `DeploymentProfileSnapshot.CodeExecutor`: SaaS = `WasmtimeScriptExecutor` (x86_64-Cranelift only); lite = `JintScriptExecutor`. Behavior/threat-model and the `ScriptCompilation`/`ScriptExecutionRequest`/`ScriptCapabilityGrant`/`ScriptExecutionOutcomeResult` types live in `custom-code.md`.
- **Two distinct enums, intentionally:** `CodeExecutorKind` (this subsystem's deployment-profile enum, P.12 — `{Wasmtime, Jint}`) selects *which adapter to register*; `ScriptRuntimeKind` (custom-code's runtime-identity enum) is what the registered adapter *reports about itself* via `Runtime`. They are not unified; the profile field maps to the executor choice, the interface property reports it.

### 3.10 `ICacheService` / `IEventBus` — **EXTEND existing** (profile adapters)

`ICacheService` (`Application/Common/Interfaces/ICacheService.cs`) and `IEventBus` (`Domain/Interfaces/IEventBus.cs`) keep their **current signatures** (no surface change). What changes is the **DI-selected implementation** per §7: in-memory vs Redis (`ICacheService` re-based on `Microsoft.Extensions.Caching.Hybrid`), in-process `EventBus` vs `RedisEventBus`. No interface edit required; listed so the registry wiring is complete.

### 3.11 Time is read through `TimeProvider` (the single clock)

There is **one** clock for the whole backend: the in-box .NET 10 `System.TimeProvider`. `TimeProvider.System` is registered as a **singleton**; every service / handler / `BackgroundService` that reads the current time injects `TimeProvider` and calls `GetUtcNow()`. This makes timers, cooldowns, TTL sweeps, billing periods, and rate-limit windows deterministically testable.

- **Direct `DateTimeOffset.UtcNow` / `DateTime.UtcNow` / `DateTime.Now` are banned outside the composition root** — they make time un-fakeable. The single tolerated default is `DomainEventBase.OccurredAt`'s initializer (§2.0); publishers needing determinism, and **all tests**, stamp `OccurredAt` from the injected `TimeProvider`.
- **All scheduling / expiry logic computes against the injected `TimeProvider`:** timer next-fire, cooldown `ExpiresAt`, cache/queue TTL, billing `PeriodStart`/`End`, and rate-limit windows. Tests drive these with a controlled/fake clock (`FakeTimeProvider`) — no real-clock waits.
- **Dependency:** `TimeProvider` is in-box BCL — no package (§8).

---

## 4. DTOs / contracts

In `NomNomzBot.Application/Contracts/Platform/` (new folder). Records, `Nullable` enabled. `StatusResponseDto<T>` / `PaginatedResponse<T>` (existing, `Application/DTOs/`) remain the envelopes — **not redefined here**.

```csharp
// --- Deployment profile ---
public sealed record DeploymentProfileDto(
    Guid InstanceId, string Mode, bool WasAutoDetected, string DbProvider, string CacheProvider,
    string EventSubTransport, string CodeExecutor, string TokenVault, string ExposureModel,
    bool RlsEnabled, string DefaultGuidanceLevel);

// --- Feature flags ---
public sealed record FeatureFlagStateDto(
    string Key, string? Description, bool Enabled, bool IsOverridden,
    int RolloutPercentage, string? MinTierKey, string? RequiresConsent, DateTimeOffset? OverrideExpiresAt);

public sealed record SetFeatureFlagOverrideRequest(string FlagKey, bool IsEnabled, string? Reason, DateTimeOffset? ExpiresAt);

// --- App settings ---
public sealed record SetAppSettingRequest(string Category, string Key, string Value, string ValueType, bool IsSecret);
public sealed record AppSettingDto(string Category, string Key, string? Value, string ValueType, bool IsSecret, int ConfigSchemaVersion);

// --- Health (read model surfaced at /health) ---
public sealed record HealthReportDto(string Status, IReadOnlyList<HealthEntryDto> Checks, int TotalDurationMs);
public sealed record HealthEntryDto(string Name, string Status, string? Description, int DurationMs, IReadOnlyList<string> Tags);

// --- Problem details (RFC 9457) emitted by the exception handler; informational shape ---
// type, title, status, detail, instance, traceId — produced by AddProblemDetails(); not a hand-rolled record.
```

---

## 5. Controller endpoints

New `PlatformController` in `NomNomzBot.Api/Controllers/V1/`, extends `BaseController`, `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/platform")]`. Health/profile-read endpoints stay minimal-API maps in `Program` (existing pattern) — listed for completeness.

**Role gate.** Gate 1 = `[Authorize]` + tenant resolution (entry; any management level ≥ `Moderator`). Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the Gate-2 action-key column before the service call (403 `FORBIDDEN` when below). Plane-C rows = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, …)` (platform IAM, `IamRoleAssignments`/`IamPermissions`, C.x; no community/management role); the ASP.NET `[Authorize(Policy="<key>")]` policy-name **is** the permission key verbatim. The keys are seeded global `ActionDefinition`s (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/api/v1/platform/profile` | GET | — | `StatusResponseDto<DeploymentProfileDto>` | platform · `tenant:read` (operators; self-host owner allowed) |
| `/api/v1/platform/feature-flags` | GET | — | `StatusResponseDto<IReadOnlyList<FeatureFlagStateDto>>` | management / Broadcaster · `platform:feature-flags:read` |
| `/api/v1/platform/feature-flags/override` | PUT | `SetFeatureFlagOverrideRequest` | `StatusResponseDto<object>` | platform · `featureflag:write` |
| `/api/v1/platform/settings` | GET | `?category=&key=` (query) | `StatusResponseDto<AppSettingDto>` | management / Broadcaster · `platform:settings:read` (Plane C `audit:read` for global) |
| `/api/v1/platform/settings` | PUT | `SetAppSettingRequest` | `StatusResponseDto<object>` | management / Broadcaster · `platform:settings:write` (Plane C `iam:manage` for global) |
| `/health` | GET | — | `HealthReportDto` (JSON) | — (anonymous, existing `Program` map) |
| `/health/live` | GET | — | `{ status }` | — (anonymous, liveness, no deps) |
| `/health/ready` | GET | — | `HealthReportDto` (ready-tagged) | — (anonymous, readiness gate) |

All error paths flow through `BaseController.ResultResponse(...)` → the existing `ErrorCode` → HTTP map (`FORBIDDEN`→403, `NOT_FOUND`→404, `VALIDATION_FAILED`→400, `RATE_LIMITED`→429, …). Rate limiting: `PlatformController` inherits `[EnableRateLimiting("api")]` from `BaseController`.

---

## 6. Pipeline actions

None. This subsystem owns no `ICommandAction` / `ICommandCondition`. (Pipeline actions are owned by the pipeline-engine subsystem.)

---

## 7. DI registration

Profile-independent registrations augment the existing `AddInfrastructure(...)` (`Infrastructure/DependencyInjection.cs`); profile-dependent ones move behind a new `AddDeploymentAdapters(IServiceCollection, DeploymentProfileSnapshot)` called from `Program` **after** `IDeploymentProfileService.DetectAndPersistAsync` resolves the profile.

**Profile-independent (always):**

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `ICurrentTenantService` | `CurrentTenantService` | Scoped | EXISTING; `BroadcasterId` widened to `Guid?` |
| `IChannelAccessService` | `ChannelAccessService` | Scoped | EXISTING; widened to `Guid`, owner-channel resolver added |
| `IDeploymentProfileService` | `DeploymentProfileService` | Singleton | detector + `Current` accessor |
| `IFeatureFlagService` | `FeatureFlagService` | Scoped | reads tenant from `ICurrentTenantService` |
| `IAppSettingsService` | `AppSettingsService` | Scoped | secret read/write via token-vault adapter |
| `ITenantSequenceAllocator` | `TenantSequenceAllocator` | Scoped | owned by event-store §3.7; shares the request `IUnitOfWork` transaction (registered once, consumed by event-store + economy) |
| `TenantStampInterceptor` | (self) | Scoped | EXISTING; `Guid` widen |
| `TenantRlsConnectionInterceptor` | (self) | Scoped | NEW; Postgres-only `SET/RESET app.tenant_id` (registered only when `RlsEnabled`) |
| `TenantResolutionMiddleware` | (self) | per-request | EXISTING; IDOR fix |

**Profile-dependent adapters (selected by `DeploymentProfileSnapshot`):**

| Interface | `saas` impl | `self_host_*` impl | Lifetime | Switch field |
|---|---|---|---|---|
| `AppDbContext` provider | `UseNpgsql` (+ RLS interceptor) | `UseSqlite` | Scoped (DbContext) | `DbProvider` |
| `ICacheService` | `HybridCacheService` (L1+Redis L2) | `HybridCacheService` (L1 only) | Singleton | `CacheProvider` |
| `IEventBus` | `RedisEventBus` | `EventBus` (in-process) | Singleton | `CacheProvider` |
| `IScriptExecutor` | `WasmtimeScriptExecutor` | `JintScriptExecutor` | Singleton | `CodeExecutor` |
| token-vault | `kms_envelope` (Azure Key Vault) | `local_aes` (file keystore) | Singleton | `TokenVault` |
| EventSub transport | conduit+webhook | `ClientWebSocket` | Singleton/Hosted | `EventSubTransport` |
| `IRateLimiterPartitionStore` | `RedisRateLimiterPartitionStore` | `InMemoryRateLimiterPartitionStore` | Singleton | `CacheProvider` |
| `IRunOnceGuard` | `PostgresRunOnceGuard` | `NoOpRunOnceGuard` | Singleton | `Mode` |

> `IScriptExecutor` is **Singleton** (not Scoped): the executor pools/pre-instantiates its runtime engines
> (one hardened Wasmtime `Engine`/`Config` + module cache; the Jint `JintEngineFactory`) and reuses them across
> calls — per-execution state lives in the fresh Wasmtime `Store` / grant, not the executor. Owner spec:
> `custom-code.md` §7 / `code-execution-sandbox.md` §11.1.

**Host wiring (`Program.cs`, replacing today's hard-wired blocks):**
- Replace `GlobalExceptionMiddleware` with `AddProblemDetails()` + an `IExceptionHandler` + `UseExceptionHandler()` → RFC 9457 (still serialized as `StatusResponseDto`-compatible problem JSON; keep `traceId`).
- `AddApiVersioning(...).AddMvc().AddApiExplorer(o => o.GroupNameFormat = "'v'VVV")` (missing `AddApiExplorer` is added).
- `AddRateLimiter` policies `api` (120/min) + `auth` (10/min) read counters via `IRateLimiterPartitionStore`.
- Health: drop `AddNpgSql` hard dependency → register `AddCheck` keyed by `DbProvider` (Npgsql probe for Postgres, `AddDbContextCheck<AppDbContext>` for SQLite) + the Redis check only when `CacheProvider == Redis`. Tags `db`/`cache`/`ready` preserved.
- SignalR: `AddSignalR().AddMessagePackProtocol()`; `AddStackExchangeRedis()` backplane **only** when `CacheProvider == Redis`.
- Middleware order unchanged: ExceptionHandler → RequestLogging → CORS → RateLimiter → Authentication → Authorization → `TenantResolutionMiddleware` → endpoints.

**SignalR hub registration + auth (existing maps in `Program`, behavior locked here):**

| Hub | Path | Auth | Tenant binding |
|---|---|---|---|
| `DashboardHub` | `/hubs/dashboard` | `[Authorize]` (user JWT via `?access_token=`) | `JoinChannel(Guid)` validates via `IChannelAccessService.CanResolveTenantAsync` before group-add (fixes today's unchecked join); group `channel-{broadcasterId}` |
| `OverlayHub` | `/hubs/overlay` | per-channel **OverlayToken** (query `?token=`), validated once at connect against `Channels.OverlayToken`; **not** the user JWT | `Context.Items["BroadcasterId"] = Channels.Id`; group `widget-{broadcasterId}-{widgetId}` |
| `OBSRelayHub` | `/hubs/obs` | `[Authorize]` | resolves to the caller's **own channel** (`ResolveOwnChannelAsync`), not the raw `userId` (fixes today's `obs-{userId}` group) |
| `AdminHub` | `/hubs/admin` | `[Authorize]` + Plane C role (replaces `[Authorize(Roles="admin")]` with an IAM policy `iam:manage`) | platform-wide; no tenant group |

Token scrubbing: `access_token` / overlay `token` are stripped from logs (`RequestLoggingMiddleware` + SignalR). `WithStatefulReconnect` + fresh-token-on-reconnect for overlay sockets.

**Private & role-scoped lanes (DashboardHub).** Channel groups give tenant isolation; they do **not** separate per-user or mod-only traffic. So a `DashboardHub` connection joins up to three lanes, and every realtime event is emitted to the **narrowest** one it is allowed to use:

- `user-{userId}` — joined on connect. **Private to one person** (e.g. "your data export is ready", a personal alert). `Clients.Group($"user-{userId}")`.
- `channel-{broadcasterId}` — joined on `JoinChannel` after `CanResolveTenantAsync`. **Channel-wide, non-sensitive** feed (chat, stats, public alerts).
- `channel-{broadcasterId}:mods` — joined **only** when the caller's resolved effective `ManagementRole ≥ Moderator` for that channel (`IActionAuthorizationService`/`IRoleResolver`). **Mod-only** events: moderation queue, viewer reports, AutoMod held-message queue — anything carrying viewer PII or moderation context. A delegated analytics-only viewer is not in this group and never receives it.

**Audience routing.** Every realtime event declares an `Audience` (`Public` | `Channel` | `Mods` | `User`); its **hub broadcaster** (`Api/Hubs/Broadcasters`, per `backend-structure.md`) emits to exactly that lane — **never `Clients.All`, never broader than the audience**. PII-bearing events MUST be `Mods` or `User`; `Public` is overlay-only (`OverlayHub`, no PII). The broadcaster fails closed: an event with no declared audience is not sent.

```csharp
public enum RealtimeAudience { Public, Channel, Mods, User }

// A hub broadcaster routes by the event's declared audience — never wider.
public interface IRealtimeEnvelope
{
    RealtimeAudience Audience { get; }
    Guid BroadcasterId { get; }   // the channel lane
    Guid? UserId { get; }         // required when Audience == User
}
```

---

## 8. Dependencies (from the stack doc)

- **Microsoft.AspNetCore.*** (in-box .NET 10): controllers, CORS, **RateLimiting**, **HealthChecks**, **ProblemDetails**, host. 2nd-party.
- **Asp.Versioning.Mvc + .Mvc.ApiExplorer** 10.0.0 — versioning + per-version OpenAPI groups.
- **Microsoft.AspNetCore.OpenApi** 10.0.9 + **Scalar.AspNetCore** 2.14.14 (dev UI) — API docs.
- **Microsoft.AspNetCore.SignalR** + **.Protocols.MessagePack** 10.0.9; **.StackExchangeRedis** 10.0.9 (SaaS backplane only).
- **Microsoft.Extensions.Caching.Hybrid** 10.7.0 (`ICacheService` L1/L1+L2) + **Microsoft.Extensions.Caching.StackExchangeRedis** 10.0.8 (SaaS L2).
- **StackExchange.Redis** 2.13.17 (pinned) — `RedisEventBus`, `RedisRateLimiterPartitionStore`, SignalR backplane, run-once.
- **Microsoft.EntityFrameworkCore** 10.0.9 — **named query filters** (tenant + soft-delete); **Npgsql.EntityFrameworkCore.PostgreSQL** 10.0.2 (Postgres + RLS) / **Microsoft.EntityFrameworkCore.Sqlite** 10.0.9 (lite); **SQLitePCLRaw.bundle_e_sqlite3 ≥ 3.0.3**.
- **DistributedLock.Postgres** 1.3.1 (SaaS `IRunOnceGuard`; or ~20 LOC `pg_try_advisory_lock`).
- **Newtonsoft.Json** — `[VC:JSON]` value-converter serialization (per binding conventions); `System.Text.Json` stays the controller/SignalR wire serializer.
- **System.Threading.RateLimiter** (in-box) — partitioned limiter the host policies sit on.
- **System.TimeProvider** (in-box BCL, no package) — the single clock (§3.11); `TimeProvider.System` singleton, `GetUtcNow()` everywhere; `FakeTimeProvider` in tests.
- **OpenTelemetry** (`ILogger` + `[LoggerMessage]` + OTLP) — replaces Serilog; `tenant_id` low-cardinality scope, no PII/token logging.
- `IScriptExecutor` adapters pull **Wasmtime** 44.0.0 (SaaS) / **Jint** 4.9.2 (lite) — registered here, authored by the sandbox subsystem.

---

## 9. Decisions (resolved)

This subsystem fixes five live defects, all specified above: the IDOR in `TenantResolutionMiddleware`, the no-op tenant query filter, the `string`→`Guid` `BroadcasterId` widen, the hard-wired Postgres/Redis adapters, and the unchecked `DashboardHub.JoinChannel`.

The ten cross-cutting design decisions are settled as follows:

- **Rate limiting is a profile adapter.** The host policies sit on the in-box partitioned limiter and read their counters through `IRateLimiterPartitionStore` (§3.7) — Redis-backed and cluster-wide on SaaS, in-memory per-instance on lite.
- **`IRunOnceGuard` (§3.8) is the cluster-singleton primitive.** It is a hard prerequisite for any multi-instance SaaS deployment and a no-op on lite (the lease is always granted).
- **Lite logging is console-only.** OpenTelemetry (`ILogger` + `[LoggerMessage]` + OTLP) is the logging stack; lite emits to console, SaaS exports OTLP. No Serilog.
- **DataProtection uses the EF Core key ring on `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` ≥ 10.0.7**, persisting keys to the database so all instances share one ring.
- **Tenant isolation is RLS + named query filter on Postgres, app-filter-only on SQLite.** `RlsEnabled` (P.12) is true only for the Postgres profile; the `TenantRlsConnectionInterceptor` is registered solely then.
- **Secrets route through the token-vault adapter** selected by `TokenVaultKind`: KMS envelope (Azure Key Vault) on SaaS, local AES file keystore on lite. Plaintext never lands in `AppSetting.Value`.
- **EventSub transport is profile-selected:** conduit+webhook on SaaS, `ClientWebSocket` on lite (§7).
- **The SignalR backplane and Redis health check are conditional on `CacheProvider == Redis`** — present on SaaS, absent on lite.
- **The script executor is profile-selected and Singleton:** Wasmtime on SaaS, Jint on lite (§3.9, §7).
- **The deployment profile is auto-detected once at boot** by `IDeploymentProfileService.DetectAndPersistAsync`, honoring an explicit `App__DeploymentMode` override, and is immutable for the process lifetime (§3.3).
- **Host capabilities are probed at setup and drive first-run sizing.** On first run `DetectAndPersistAsync` probes CPU cores + available memory and sizes the worker pools / concurrency / resource limits (`Scaling:*`, `scaling-qos.md` §9) to fit the host — especially for self-host on arbitrary hardware — always yielding to an explicit operator override (§3.3).
- **Setup guidance level is a first-run "Simple vs Advanced" wizard choice, never a silent default.** Simple → `Novice`, Advanced → `Expert`; the answer is persisted as `DeploymentProfile.DefaultGuidanceLevel` (the per-user seed default), falling back to `Novice` (Simple) only when the wizard is bypassed (§3.3).

**Dependency, not a caveat:** the HS256→RS256/ES256 signing migration is a prerequisite for federation across instances. This subsystem's hub/middleware auth works on either signing scheme and does not block on that migration — federation does.
