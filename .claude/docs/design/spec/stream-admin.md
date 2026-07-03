# Interface Specification — `stream-admin` subsystem

Directly-implementable interface contract. Owner codes from this first-try. No ambiguity intended.

**Scope:** three cohesive areas.
- **Stream tools** — title/game/tag edits with a role floor (Twitch defines our defaults: on Twitch, Editors edit stream info while Moderators only moderate chat, so title/game/tags floor at Editor), per-game/per-segment presets, scheduled changes (mid-stream segment switch). Helix-backed.
- **Platform admin console (Plane C IAM)** — operator IAM (roles/permissions/principals/assignments), audited support access (`tenant:access`, logged), tenant suspend/read, feature-flag administration. Default-deny, least-privilege, every cross-tenant act audited.
- **IPC developer mode** — opt-in (off by default), key-gated, local-socket-only tokenless dev hook-in. Never exposed remotely.

**Binding conventions (apply to every type below):** C# namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (never `.Result`/`.Wait`); `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` + `[Route("api/v{version:apiVersion}/...")]`; **Newtonsoft.Json** for app JSON (every `[VC:JSON]` column is a hand-rolled `ValueConverter<T,string>` + `ValueComparer`); surrogate PKs = `Guid` via `Guid.CreateVersion7()`; Twitch ids are indexed attribute columns; tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`); soft-delete (`IsDeleted`/`DeletedAt`) global filter; deployment-profile adapters chosen by DI.

> **Convention reconciliation (load-bearing — read once).** The locked schema widens `BroadcasterId` `string`→`Guid` and demotes raw-Twitch-id PKs to indexed `Twitch*Id` attribute columns (schema §1.1). The **current** code still uses `string channelId` / `string broadcasterId` throughout (`StreamController`, `IAdminService`, `IPermissionService`, `ITwitchApiService`, `PipelineExecutionContext.BroadcasterId`). **This spec is written to the locked post-rebuild model: every tenant key is `Guid`.** Where a method must call an existing string-keyed Twitch surface (`ITwitchApiService.UpdateChannelInfoAsync(string broadcasterId, …)`), the implementation resolves `Channels.TwitchChannelId` from the `Guid` `broadcasterId` before the Helix call. New service interfaces below take `Guid`; do **not** reintroduce `string` tenant keys.

---

## 1. Entities

All tables are defined in the **LOCKED** schema `docs/design/2026-06-16-database-schema.md`. This subsystem **owns** (reads + writes) the following; it does not redefine them — fields/types are authoritative there. Key columns restated only for the surface this spec touches.

### Stream tools
- **`StreamPresets`** (schema F.10) `[soft-delete, tenant]` — saved title/game/tag presets.
  Key fields: `Id Guid PK`, `BroadcasterId Guid FK→Channels`, `Name string(100)`, `Title string(255)?`, `GameId string(50)?`, `GameName string(255)?`, `Language string(8)?`, `Tags text? [VC:JSON List<string>]`, `ContentLabels text? [VC:JSON List<string>]`, `IsBrandedContent bool`, `SortOrder int`. **Unique** `(BroadcasterId, Name)`.
- **`ScheduledStreamChanges`** (schema F.11) `[soft-delete, tenant]` — queued title/game/tag changes applied at `ScheduledFor`.
  Key fields: `Id Guid PK`, `BroadcasterId Guid FK→Channels`, `StreamPresetId Guid? FK→StreamPresets`, `Title string(255)?`, `GameId string(50)?`, `GameName string(255)?`, `Tags text? [VC:JSON]`, `ContentLabels text? [VC:JSON]`, `ScheduledFor timestamp`, `Status string(20) [VC:enum] pending|applied|failed|canceled`, `AppliedAt timestamp?`, `LastError string(1000)?`, `CreatedByUserId Guid? FK→Users`. **Index** `(BroadcasterId, ScheduledFor)`, `(Status, ScheduledFor)` (due-now sweep).
- **`Channels`** (schema A.2) — read/write **current** values only: `Title`, `GameId`, `GameName`, `Language`, `Tags [VC:JSON]`, `ContentLabels [VC:JSON]`, `IsBrandedContent`, `Status`, `SuspendedAt`, `SuspendedReason`. (Presets/scheduled changes never live here; the scheduler writes `Channels` current values when it applies.)
- **`ActionDefinitions`** (schema B.3) `[GLOBAL, seed]` — read-only here: the role-floor catalog. Relevant rows: `channel:title:write`, `channel:game:write`, `channel:tags:write`, `channel:ccl:write`, `channel:language:write`, `channel:brandedcontent:write`, `channel:extensions:write` — all seeded `FloorLevel = Editor` (Twitch defines our defaults: native Editors edit stream info, Moderators only moderate chat). The role floor is resolved through this table, never hard-coded. (CCL / `BroadcasterLanguage` / branded-content carry the same `channel:manage:broadcast` Twitch scope and the same Editor floor as title/game/tags; `channel:extensions:write` gates the extensions-config write, Twitch scope `user:edit:broadcast`.)
- **`ChannelActionOverrides`** (schema B.4) — read-only here: per-channel raise/lower of the above action levels (floor-clamped).

### Platform admin (Plane C IAM)
- **`IamPermissions`** (C.1) `[GLOBAL, seed]` — `Key` e.g. `tenant:read`, `tenant:suspend`, `tenant:access`, `featureflag:write`, `audit:read`, `iam:manage`.
- **`IamRoles`** (C.2) `[GLOBAL, soft-delete]`, **`IamRolePermissions`** (C.3) M2M, **`IamPrincipals`** (C.4) `[GLOBAL, soft-delete]` (employee/service_account), **`IamRoleAssignments`** (C.5) — bind principal→role, optional `ScopeChannelId` (one-tenant narrow), `ExpiresAt` (break-glass).
- **`Channels`** (A.2) — admin writes `Status`/`SuspendedAt`/`SuspendedReason` for suspend; reads for tenant listing.
- **`FeatureFlag`** + **`FeatureFlagOverride`** (P.13) — global flag definition (rollout %, `MinTierId` FK→`BillingTier`, consent/deployment gating) + per-tenant override `(FeatureFlagId, BroadcasterId)` unique.
- **`IamAuditLog`** (O.9) `[APPEND-ONLY]` — the canonical Plane-C accountability log: `PrincipalId`, `Permission`, `TargetBroadcasterId?`, `TargetResource?`, `Justification?`, `BreakGlass bool`, `Outcome allowed|denied`, `SourceIpCipher? [PII-shred]`, `OccurredAt`. Every privileged/cross-tenant operator action (incl. `tenant:access`) writes one row.
- **`ComplianceAuditLog`** (O.10) `[APPEND-ONLY]` — read-only here (admin audit-search surface includes erasure/export/consent/retention rows).

### IPC developer mode
- **`IpcDevModeKeys`** (A.5) `[soft-delete]` — `Id Guid PK`, `KeyHash string(64) Unique`, `Label string(100)?`, `IsEnabled bool` (off by default), `CreatedByUserId Guid? FK→Users`, `ExpiresAt timestamp?`. Hashed local-IPC gate key; never remote.
- **`DeploymentProfile`** (P.12) `[GLOBAL, single-row]` — read-only here: `ExposureModel` and `Mode` gate whether the IPC socket may even start (lite/self-host only).

---

## 2. Domain events

All inherit `NomNomzBot.Domain.Events.DomainEventBase` — the canonical, authoritative base (platform-conventions §2.0): it provides `Guid EventId` (UUIDv7), `Guid BroadcasterId` (the locked UUIDv7 tenant key; `Guid.Empty` = platform-level), and `DateTimeOffset OccurredAt`. Events **must NOT redeclare** `EventId`/`BroadcasterId`/`OccurredAt` — they add only their own payload fields, and the publisher sets the inherited `BroadcasterId` for tenant-scoped events. Match the existing init-property style (`FeatureToggledEvent`, `ChannelUpdatedEvent`). New events live in `NomNomzBot.Domain/Events/`. Namespace `NomNomzBot.Domain.Events`.

```csharp
// Fired after stream title/game/tags successfully pushed to Twitch + persisted to Channels.
// Tenant-scoped: publisher sets the inherited DomainEventBase.BroadcasterId (do not redeclare it).
public sealed record StreamMetadataUpdatedEvent : DomainEventBase
{
    public string? NewTitle { get; init; }
    public string? NewGameId { get; init; }
    public string? NewGameName { get; init; }
    public List<string>? NewTags { get; init; }
    public required string Source { get; init; }          // "manual" | "preset" | "scheduled"
    public Guid? AppliedPresetId { get; init; }            // set when Source == "preset"|"scheduled"
    public Guid? ActorUserId { get; init; }                // null for scheduler-driven
}

// Fired when a scheduled change is applied (success or failure terminal).
// Tenant-scoped: publisher sets the inherited DomainEventBase.BroadcasterId (do not redeclare it).
public sealed record ScheduledStreamChangeAppliedEvent : DomainEventBase
{
    public required Guid ScheduledChangeId { get; init; }
    public required bool Succeeded { get; init; }
    public string? Error { get; init; }                    // set when Succeeded == false
}

// Fired when an operator begins/ends audited support access to a tenant (tenant:access).
// Platform-scoped (Plane-C operator action): inherited DomainEventBase.BroadcasterId stays Guid.Empty;
// the affected tenant rides in the TargetBroadcasterId payload field.
public sealed record TenantAccessGrantedEvent : DomainEventBase
{
    public required Guid PrincipalId { get; init; }
    public required Guid TargetBroadcasterId { get; init; }
    public required string Justification { get; init; }
    public required bool BreakGlass { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

// Fired when an operator suspends / un-suspends a tenant (tenant:suspend).
// Platform-scoped (Plane-C operator action): inherited DomainEventBase.BroadcasterId stays Guid.Empty;
// the affected tenant rides in the TargetBroadcasterId payload field.
public sealed record TenantSuspensionChangedEvent : DomainEventBase
{
    public required Guid PrincipalId { get; init; }
    public required Guid TargetBroadcasterId { get; init; }
    public required string NewStatus { get; init; }        // "active" | "suspended" | "platform_banned"
    public string? Reason { get; init; }
}

// Fired when an OPERATOR administers a feature flag (global definition or per-tenant override) from the admin
// console — carries operator identity for the Plane-C audit trail. Distinct from the platform-conventions
// `FeatureFlagChangedEvent` (cache-invalidation event: FlagKey/IsEnabledGlobally/RolloutPercentage/
// TenantOverrideValue, no operator id). Renamed to avoid the name collision; this one is the admin-action event.
// Platform-scoped (Plane-C operator action): inherited DomainEventBase.BroadcasterId stays Guid.Empty;
// a per-tenant override's target rides in the OverrideBroadcasterId payload field.
public sealed record FeatureFlagAdministeredEvent : DomainEventBase
{
    public required Guid PrincipalId { get; init; }
    public required string FlagKey { get; init; }
    public Guid? OverrideBroadcasterId { get; init; }      // null = global definition change
    public required bool IsEnabled { get; init; }
}
```

> Existing `FeatureToggledEvent` (per-channel feature on/off, key+bool) is a **different, narrower** event already consumed by the pipeline; do not collapse `FeatureFlagAdministeredEvent` into it. Likewise it is **not** the platform-conventions `FeatureFlagChangedEvent` (which `IFeatureFlagService` raises to invalidate cached evaluations) — this one carries operator identity + override scope for the admin audit trail. The admin service emits **both**: `FeatureFlagAdministeredEvent` (audit) and the platform-conventions `FeatureFlagChangedEvent` (cache invalidation).

---

## 3. Service interfaces

All in `NomNomzBot.Application/Services/` (interface) implemented in `NomNomzBot.Infrastructure/Services/<area>/`. All async, `Result<T>`, `CancellationToken ct = default` last. Implementations use repositories + `IUnitOfWork`, never raw `DbContext` from a controller path.

### 3.1 `IStreamToolsService` — `NomNomzBot.Application.Services`

```csharp
public interface IStreamToolsService
{
    // ── Live metadata edits (role-floor enforced) ────────────────────────────
    Task<Result<StreamMetadataDto>> GetCurrentMetadataAsync(
        Guid broadcasterId, CancellationToken ct = default);

    Task<Result<StreamMetadataDto>> UpdateMetadataAsync(
        Guid broadcasterId, Guid actorUserId, UpdateStreamMetadataRequest request,
        CancellationToken ct = default);

    Task<Result<StreamMetadataDto>> UpdateTitleAsync(
        Guid broadcasterId, Guid actorUserId, string title, CancellationToken ct = default);

    Task<Result<StreamMetadataDto>> UpdateGameAsync(
        Guid broadcasterId, Guid actorUserId, string gameName, CancellationToken ct = default);

    Task<Result<StreamMetadataDto>> UpdateTagsAsync(
        Guid broadcasterId, Guid actorUserId, List<string> tags, CancellationToken ct = default);

    Task<Result<IReadOnlyList<StreamCategoryDto>>> SearchCategoriesAsync(
        string query, CancellationToken ct = default);

    // ── Channel extensions config (Helix Update User Extensions) ──────────────
    Task<Result<ChannelExtensionsDto>> GetExtensionsAsync(
        Guid broadcasterId, CancellationToken ct = default);

    Task<Result<ChannelExtensionsDto>> UpdateExtensionsAsync(
        Guid broadcasterId, Guid actorUserId, UpdateChannelExtensionsRequest request,
        CancellationToken ct = default);

    // ── Presets (per-game / per-segment templates) ───────────────────────────
    Task<Result<IReadOnlyList<StreamPresetDto>>> ListPresetsAsync(
        Guid broadcasterId, CancellationToken ct = default);

    Task<Result<StreamPresetDto>> CreatePresetAsync(
        Guid broadcasterId, Guid actorUserId, CreateStreamPresetRequest request,
        CancellationToken ct = default);

    Task<Result<StreamPresetDto>> UpdatePresetAsync(
        Guid broadcasterId, Guid presetId, UpdateStreamPresetRequest request,
        CancellationToken ct = default);

    Task<Result> DeletePresetAsync(
        Guid broadcasterId, Guid presetId, CancellationToken ct = default);

    Task<Result<StreamMetadataDto>> ApplyPresetAsync(
        Guid broadcasterId, Guid actorUserId, Guid presetId, CancellationToken ct = default);

    // ── Scheduled changes ────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<ScheduledStreamChangeDto>>> ListScheduledChangesAsync(
        Guid broadcasterId, CancellationToken ct = default);

    Task<Result<ScheduledStreamChangeDto>> ScheduleChangeAsync(
        Guid broadcasterId, Guid actorUserId, ScheduleStreamChangeRequest request,
        CancellationToken ct = default);

    Task<Result> CancelScheduledChangeAsync(
        Guid broadcasterId, Guid scheduledChangeId, CancellationToken ct = default);
}
```

Behavior notes (one line each):
- `GetCurrentMetadataAsync` — reads current `Channels` values (enriched with live viewer count via `ITwitchApiService.GetStreamInfoAsync` when live); no write, no event.
- `UpdateMetadataAsync` — enforces the role floor per field (see floor table below) via `IStreamEditAuthorizer`; resolves `gameName`→`gameId` through `ITwitchApiService.SearchCategoriesAsync`; pushes to Helix via `UpdateChannelInfoAsync` (which carries `ContentLabels`/`Language`/`IsBrandedContent` — `UpdateChannelInfoRequest`, twitch-helix §4.1); persists to `Channels`; emits `StreamMetadataUpdatedEvent(Source="manual")`. **Per changed field** the matching action key is floor-checked: `Title`→`channel:title:write`, `GameName`→`channel:game:write`, `Tags`→`channel:tags:write`, `ContentLabels`→`channel:ccl:write`, `Language`→`channel:language:write`, `IsBrandedContent`→`channel:brandedcontent:write` (all Editor floor). Fails `FORBIDDEN` if actor under floor for any changed field, `VALIDATION_FAILED` on empty title.
- `UpdateTitleAsync`/`UpdateGameAsync`/`UpdateTagsAsync` — single-field convenience wrappers over `UpdateMetadataAsync`; each independently floor-checked (title/game/tags all floor at Editor — Twitch defines our defaults: Editors edit stream info, Moderators only moderate chat).
- `SearchCategoriesAsync` — proxies `ITwitchApiService.SearchCategoriesAsync`; read-only, no floor (autocomplete).
- `GetExtensionsAsync` — reads the channel's active panel/overlay/component extension slots via Helix `GET /users/extensions` (broadcaster token); read-only, no floor (Gate 1 only). No state change, no event.
- `UpdateExtensionsAsync` — floor-checks `channel:extensions:write` via `IStreamEditAuthorizer`; writes the requested slot activations via Helix `PUT /users/extensions` (Twitch scope `user:edit:broadcast` — progressive, requested when the operator first opens the extensions surface). Idempotent slot replace; `FORBIDDEN` under floor, `VALIDATION_FAILED` on an unknown extension/slot. Low-priority surface; no domain event (extensions are Twitch-side panel config, not a streamed metadata change).
- `ListPresetsAsync` — tenant-scoped `StreamPresets` ordered by `SortOrder`; read-only.
- `CreatePresetAsync` — inserts `StreamPresets` row (`Id = Guid.CreateVersion7()`); `ALREADY_EXISTS` on `(BroadcasterId, Name)` collision; no Helix call, no event.
- `UpdatePresetAsync` — mutates an owned preset; `NOT_FOUND` if not owned/soft-deleted.
- `DeletePresetAsync` — soft-deletes the preset; refuses (`VALIDATION_FAILED`) if referenced by a `pending` `ScheduledStreamChanges` row (the schedule keeps a meaningful preset name; the operator must cancel the schedule first).
- `ApplyPresetAsync` — loads preset, applies its fields as a metadata update (same floor enforcement + Helix push + `Channels` persist) immediately; emits `StreamMetadataUpdatedEvent(Source="preset", AppliedPresetId)`.
- `ListScheduledChangesAsync` — tenant-scoped, ordered by `ScheduledFor`; read-only.
- `ScheduleChangeAsync` — floor-checks the resulting fields **at schedule time** (fail fast); inserts a `pending` `ScheduledStreamChanges` row; no Helix call yet; no metadata event (the sweep emits on apply). `VALIDATION_FAILED` if `ScheduledFor` is in the past.
- `CancelScheduledChangeAsync` — sets `Status=canceled` on a `pending` row; `NOT_FOUND` if not pending/owned.

**Stream-edit role floor** — resolved via `IStreamEditAuthorizer`, never hard-coded:

```csharp
public interface IStreamEditAuthorizer  // NomNomzBot.Application.Services
{
    // Returns Success if actor's resolved level ≥ clamp(override ?? default, floor) for the action key.
    Task<Result> AuthorizeAsync(
        Guid broadcasterId, Guid actorUserId, string actionKey, CancellationToken ct = default);
}
```
- `AuthorizeAsync` — a **sanctioned thin mapper**, not a parallel gate: it resolves the per-field action key (`channel:title:write`/`channel:game:write`/`channel:tags:write`/`channel:ccl:write`/`channel:language:write`/`channel:brandedcontent:write` — and `channel:extensions:write` for the extensions-config write — all floor at Editor — Twitch defines our defaults: native Editors edit stream info, Moderators only moderate chat) and **delegates the actual decision to the canonical Plane-B `IActionAuthorizationService.AuthorizeActionAsync`** (which itself reads `ActionDefinitions[actionKey]`, applies any `ChannelActionOverrides`, and compares the actor's `IRoleResolver`-resolved `ChannelMemberships.LevelValue` against the floor-clamped level). It performs **no** level comparison of its own. Surfaces `FORBIDDEN` on insufficient level and `NOT_FOUND` if `actionKey` is unknown, both propagated from `IActionAuthorizationService`. This is a Plane-B (own-channel) gate — never Plane-C; it adds only the title/game/tags key-selection convenience, reusing the shared authorizer rather than duplicating it.

### 3.2 `IPlatformAdminService` — `NomNomzBot.Application.Services`

> Plane-C operations. The coarse `[Authorize(Roles="admin")]` model on the live `AdminController` is the legacy gate being **replaced** by permission-checked, audited operator actions (each method gates on `IPlatformIamService.AuthorizePlatformAsync` with the per-action key). Self-host collapses to "owner = full" (IAM tables empty → the `OwnerIsFullIamService` adapter returns allow). Extends the existing `IAdminService` (stats/list/health stay there); this interface adds the privileged tenant/flag/access surface.

```csharp
public interface IPlatformAdminService
{
    // ── Tenant management ────────────────────────────────────────────────────
    Task<Result<PagedList<AdminTenantDto>>> ListTenantsAsync(
        Guid principalId, AdminTenantQuery query, PaginationParams pagination,
        CancellationToken ct = default);

    Task<Result<AdminTenantDetailDto>> GetTenantAsync(
        Guid principalId, Guid broadcasterId, CancellationToken ct = default);

    Task<Result> SuspendTenantAsync(
        Guid principalId, Guid broadcasterId, SuspendTenantRequest request,
        CancellationToken ct = default);

    Task<Result> ReinstateTenantAsync(
        Guid principalId, Guid broadcasterId, string justification,
        CancellationToken ct = default);

    // ── Audited support access (tenant:access) ───────────────────────────────
    Task<Result<TenantAccessGrantDto>> BeginTenantAccessAsync(
        Guid principalId, Guid broadcasterId, BeginTenantAccessRequest request,
        CancellationToken ct = default);

    Task<Result> EndTenantAccessAsync(
        Guid principalId, Guid accessGrantId, CancellationToken ct = default);

    // ── Feature flags ────────────────────────────────────────────────────────
    Task<Result<IReadOnlyList<FeatureFlagDto>>> ListFeatureFlagsAsync(
        Guid principalId, CancellationToken ct = default);

    Task<Result<FeatureFlagDto>> UpsertFeatureFlagAsync(
        Guid principalId, UpsertFeatureFlagRequest request, CancellationToken ct = default);

    Task<Result> SetFeatureFlagOverrideAsync(
        Guid principalId, string flagKey, Guid broadcasterId, SetFlagOverrideRequest request,
        CancellationToken ct = default);

    // ── Audit search ─────────────────────────────────────────────────────────
    Task<Result<PagedList<IamAuditEntryDto>>> SearchAuditAsync(
        Guid principalId, AuditSearchQuery query, PaginationParams pagination,
        CancellationToken ct = default);
}
```

Behavior notes:
- `ListTenantsAsync` — requires `tenant:read`; returns paged `Channels` projection (no per-tenant viewer data fabricated). Writes one `IamAuditLog` row `Outcome=allowed` only when `query` targets cross-tenant scope (list itself is `tenant:read`). `FORBIDDEN` (+ `denied` audit row) if principal lacks permission.
- `GetTenantAsync` — requires `tenant:read`; returns tenant detail (status, tier, owner, counts). Audited as above.
- `SuspendTenantAsync` — requires `tenant:suspend`; sets `Channels.Status=suspended|platform_banned`, `SuspendedAt=now`, `SuspendedReason=request.Reason`; emits `TenantSuspensionChangedEvent`; writes `IamAuditLog(Permission="tenant:suspend", TargetBroadcasterId, Justification, Outcome)`. `FORBIDDEN`+denied-audit if lacking.
- `ReinstateTenantAsync` — requires `tenant:suspend`; sets `Status=active`, clears `SuspendedAt`/`SuspendedReason`; emits `TenantSuspensionChangedEvent(NewStatus="active")`; audited.
- `BeginTenantAccessAsync` — requires `tenant:access`; grants support access by creating a time-boxed `IamRoleAssignment` (schema C.5) narrowed to `ScopeChannelId=broadcasterId`, with `AssignedByPrincipalId=principalId`, `ExpiresAt=request.ExpiresAt`, and `Reason=request.Justification`; the returned `TenantAccessGrantDto.Id` is that assignment's `Id`. Emits `TenantAccessGrantedEvent`; writes `IamAuditLog(Permission="tenant:access", BreakGlass=request.BreakGlass, Justification, Outcome=allowed)`. `request.Justification` required → `VALIDATION_FAILED` if blank.
- `EndTenantAccessAsync` — revokes the access grant by setting the `IamRoleAssignment.RevokedAt=now` (`accessGrantId` is the assignment `Id`); writes a closing `IamAuditLog` row; `NOT_FOUND` if the assignment is not owned by the principal or not active (already revoked/expired).
- `ListFeatureFlagsAsync` — requires `featureflag:write` (read implies the admin flag surface) ; returns global `FeatureFlag` defs with `MinTierKey`.
- `UpsertFeatureFlagAsync` — requires `featureflag:write`; inserts/updates a global `FeatureFlag` (resolves `MinTierKey`→`MinTierId` FK); emits `FeatureFlagAdministeredEvent(OverrideBroadcasterId=null)` (audit) + the platform-conventions `FeatureFlagChangedEvent` (cache invalidation); audited.
- `SetFeatureFlagOverrideAsync` — requires `featureflag:write`; upserts `FeatureFlagOverride(FlagId, BroadcasterId)`; emits `FeatureFlagAdministeredEvent(OverrideBroadcasterId)` (audit) + the platform-conventions `FeatureFlagChangedEvent`; audited. `NOT_FOUND` if `flagKey` unknown.
- `SearchAuditAsync` — requires `audit:read`; returns paged `IamAuditLog` (and optionally `ComplianceAuditLog`) projection filtered by principal/tenant/permission/time; read-only but itself audited (`audit:read` viewed).

**Authorization gate (the Plane-C resolver)** — **consumed, not redefined** (owner: `roles-permissions.md` §3.7):

> **Single public Plane-C entry point: `IPlatformIamService.AuthorizePlatformAsync`** (owned by
> `roles-permissions.md`, which owns Domain C). Every method above calls it first. The canonical signature:
> ```csharp
> // roles-permissions.md §3.7 — authorizes AND audits in ONE call (an authz decision can never go un-audited).
> // Default-deny; ALWAYS writes IamAuditLog (allowed|denied) + emits IamAccessEvaluatedEvent.
> // Self-host (no IamPrincipals) → owner = full (true), audit no-op. Profile adapters
> // PlatformIamService (SaaS) / OwnerIsFullIamService (self-host).
> Task<Result<bool>> AuthorizePlatformAsync(
>     Guid principalId, string permissionKey, Guid? targetBroadcasterId,
>     bool breakGlass, string? justification, CancellationToken cancellationToken = default);
> ```
> This subsystem does **not** define a second public authorization interface (the earlier split
> `IIamAuthorizationService` "pure decision, caller audits separately" path is dropped — it let an authz
> decision go un-audited, which `AuthorizePlatformAsync` structurally prevents by combining the two).

`IIamAuditWriter` is retained **only** as the internal append-only `IamAuditLog` sink that
`IPlatformIamService` writes *through* — it is **not** a second public authorization or audit entry point, and
callers in this subsystem never invoke it directly (they call `AuthorizePlatformAsync`, which audits for them):

```csharp
public interface IIamAuditWriter  // NomNomzBot.Application.Services — INTERNAL sink; written through by IPlatformIamService only
{
    // Append-only IamAuditLog write; never throws on the hot path — failures are logged + swallowed
    // (audit-write must not block the audited action's own result).
    Task WriteAsync(IamAuditEntry entry, CancellationToken ct = default);
}

public sealed record IamAuditEntry(
    Guid PrincipalId, string PrincipalType, string Permission, Guid? TargetBroadcasterId,
    string? TargetResource, string? Justification, bool BreakGlass, string Outcome,
    string? SourceIpCipher);
```
- `IPlatformIamService.AuthorizePlatformAsync` — the sole public Plane-C gate; returns the allow/deny decision **and** writes the `IamAuditLog` row (via the internal `IIamAuditWriter`) in one call. No caller is responsible for a separate audit write.
- `IIamAuditWriter.WriteAsync` — internal: inserts one `IamAuditLog` row; resilient (audit failure must not fail the operation). Not exposed as an authorization path.

### 3.3 `IIpcDevModeService` — `NomNomzBot.Application.Services`

> Opt-in local developer hook-in. The socket listener is an `IHostedService` in Infrastructure gated by `DeploymentProfile` + at least one enabled key; this service owns the key registry + connection authentication.

```csharp
public interface IIpcDevModeService
{
    Task<Result<bool>> IsEnabledAsync(CancellationToken ct = default);

    Task<Result<IpcDevModeKeyDto>> CreateKeyAsync(
        Guid actorUserId, CreateIpcKeyRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<IpcDevModeKeyDto>>> ListKeysAsync(CancellationToken ct = default);

    Task<Result> RevokeKeyAsync(Guid keyId, CancellationToken ct = default);

    // Local-socket auth: constant-time compare of presented key against stored KeyHash.
    Task<Result> AuthenticateConnectionAsync(string presentedKey, CancellationToken ct = default);
}
```
- `IsEnabledAsync` — true only when `DeploymentProfile.Mode != saas` AND at least one non-deleted, non-expired `IpcDevModeKeys` row has `IsEnabled=true`; off by default.
- `CreateKeyAsync` — generates a random key, stores `KeyHash` (SHA-256, never plaintext) + `Label`/`ExpiresAt`; returns the plaintext **once** in `IpcDevModeKeyDto.PlaintextKey` (null on every later read).
- `ListKeysAsync` — metadata only (`Id`, `Label`, `IsEnabled`, `ExpiresAt`, `CreatedAt`); never returns key material.
- `RevokeKeyAsync` — soft-deletes / sets `IsEnabled=false`; `NOT_FOUND` if absent.
- `AuthenticateConnectionAsync` — `CryptographicOperations.FixedTimeEquals` over the hash; returns `Success` only for an enabled, unexpired key; `FORBIDDEN` otherwise. **Refuses outright** (returns `Failure("…","FORBIDDEN")`) if `IsEnabledAsync` is false — never authenticates when dev mode is off or profile is SaaS.

---

## 4. DTOs / contracts

All `public sealed record`, in `NomNomzBot.Application/DTOs/StreamTools/`, `…/DTOs/PlatformAdmin/`, `…/DTOs/Ipc/`. App JSON uses Newtonsoft.Json; property names PascalCase (existing `AdminDtos` convention).

### Stream tools
```csharp
public sealed record StreamMetadataDto(
    string? Title, string? GameId, string? GameName, List<string> Tags,
    List<string> ContentLabels, bool IsBrandedContent, string? Language,
    bool IsLive, int ViewerCount, DateTime? StartedAt);

public sealed record StreamCategoryDto(string Id, string Name, string? BoxArtUrl);

public sealed record UpdateStreamMetadataRequest(
    string? Title, string? GameName, List<string>? Tags,
    List<string>? ContentLabels, bool? IsBrandedContent, string? Language);

public sealed record StreamPresetDto(
    Guid Id, string Name, string? Title, string? GameId, string? GameName,
    string? Language, List<string> Tags, List<string> ContentLabels,
    bool IsBrandedContent, int SortOrder, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateStreamPresetRequest(
    string Name, string? Title, string? GameName, string? Language,
    List<string>? Tags, List<string>? ContentLabels, bool IsBrandedContent, int SortOrder);

public sealed record UpdateStreamPresetRequest(
    string? Name, string? Title, string? GameName, string? Language,
    List<string>? Tags, List<string>? ContentLabels, bool? IsBrandedContent, int? SortOrder);

public sealed record ScheduledStreamChangeDto(
    Guid Id, Guid? StreamPresetId, string? PresetName, string? Title, string? GameId,
    string? GameName, List<string> Tags, List<string> ContentLabels,
    DateTime ScheduledFor, string Status, DateTime? AppliedAt, string? LastError,
    DateTime CreatedAt);

public sealed record ScheduleStreamChangeRequest(
    Guid? StreamPresetId, string? Title, string? GameName,
    List<string>? Tags, List<string>? ContentLabels, DateTime ScheduledFor);

// Channel extensions (Helix Get/Update User Extensions). Slot key = "panel:1"|"overlay:1"|"component:1"… .
public sealed record ChannelExtensionSlotDto(
    string Slot, bool Active, string? ExtensionId, string? Version, string? Name);

public sealed record ChannelExtensionsDto(IReadOnlyList<ChannelExtensionSlotDto> Slots);

public sealed record UpdateChannelExtensionsRequest(IReadOnlyList<ChannelExtensionSlotDto> Slots);
```

### Platform admin
```csharp
public sealed record AdminTenantDto(
    Guid Id, string Name, string TwitchChannelId, string Status,
    string BillingTierKey, bool IsLive, DateTime CreatedAt, DateTime? SuspendedAt);

public sealed record AdminTenantDetailDto(
    Guid Id, string Name, string TwitchChannelId, string Status, string? SuspendedReason,
    string BillingTierKey, string DeploymentMode, Guid OwnerUserId, string OwnerDisplayName,
    int MembershipCount, DateTime CreatedAt, DateTime? SuspendedAt);

public sealed record AdminTenantQuery(string? Search, string? Status, bool? IsLive);

public sealed record SuspendTenantRequest(string NewStatus, string Reason);  // suspended | platform_banned

public sealed record BeginTenantAccessRequest(string Justification, bool BreakGlass, DateTime? ExpiresAt);

public sealed record TenantAccessGrantDto(
    Guid Id, Guid PrincipalId, Guid TargetBroadcasterId, string Justification,
    bool BreakGlass, DateTime GrantedAt, DateTime? ExpiresAt, DateTime? RevokedAt);

public sealed record FeatureFlagDto(
    Guid Id, string Key, string? Description, bool IsEnabledGlobally, int RolloutPercentage,
    string? MinTierKey, string? RequiresConsent, string? DeploymentMode);

public sealed record UpsertFeatureFlagRequest(
    string Key, string? Description, bool IsEnabledGlobally, int RolloutPercentage,
    string? MinTierKey, string? RequiresConsent, string? DeploymentMode);

public sealed record SetFlagOverrideRequest(bool IsEnabled, string? Reason, DateTime? ExpiresAt);

public sealed record AuditSearchQuery(
    Guid? PrincipalId, Guid? TargetBroadcasterId, string? Permission,
    string? Outcome, DateTime? From, DateTime? To);

public sealed record IamAuditEntryDto(
    long Id, Guid PrincipalId, string PrincipalType, string Permission,
    Guid? TargetBroadcasterId, string? TargetResource, string? Justification,
    bool BreakGlass, string Outcome, DateTime OccurredAt);
```

### IPC
```csharp
public sealed record CreateIpcKeyRequest(string? Label, DateTime? ExpiresAt);

public sealed record IpcDevModeKeyDto(
    Guid Id, string? Label, bool IsEnabled, DateTime? ExpiresAt, DateTime CreatedAt,
    string? PlaintextKey);  // non-null ONLY in the CreateKeyAsync response; null on every list/get
```

---

## 5. Controller endpoints

All controllers extend `BaseController` (`NomNomzBot.Api.Controllers`), are `[ApiVersion("1.0")]`, `[Authorize]`, return via `ResultResponse(...)` / `GetPaginatedResponse(...)`. Tenant key on the route is the `Guid` `broadcasterId`.

### `StreamToolsController : BaseController`
`[Route("api/v{version:apiVersion}/channels/{broadcasterId:guid}/stream")]` `[Tags("Stream")]`
**Role gate** — all write routes are **management plane (Plane B)**. **Gate 1** = `[Authorize]` + tenant resolution (entry; any management level ≥ Moderator) — it only proves authentication + tenant access, not the write floor. **Gate 2** = the per-route floor named in the Gate-2 action-key column, enforced before the service call via `IStreamEditAuthorizer` (a sanctioned thin mapper that selects the per-field action key and delegates the decision to `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)`), returning `403 FORBIDDEN` when the caller's resolved level is below the action's effective floor. Title/game/tags all floor at Editor — Twitch defines our defaults: native Editors edit stream info, Moderators only moderate chat. Each floor is the action's seeded global `ActionDefinitions` (schema B.3) default; a broadcaster may raise it via `ChannelActionOverride` but never below the seeded `FloorLevel`. Read/autocomplete routes carry no Gate-2 key (Gate 1 only).

> **Supersedes/merges the existing `StreamController`.** The current `StreamController` (`GET/PUT`, `PATCH title|game|tags`, `GET status|categories`) is rewritten to delegate to `IStreamToolsService` instead of calling `ITwitchApiService` directly and to use `Guid` route keys. Keep the existing route paths; add presets + scheduled-change routes.

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|------|-------|-------------|--------------|-----------------------------------|
| GET | `/` | — | `StatusResponseDto<StreamMetadataDto>` | management · (Gate 1 only) |
| PUT | `/` | `UpdateStreamMetadataRequest` | `StatusResponseDto<StreamMetadataDto>` | management / Editor · `channel:title:write` (per changed field; game/tags/ccl/language/brandedcontent · `channel:game:write`/`channel:tags:write`/`channel:ccl:write`/`channel:language:write`/`channel:brandedcontent:write`, all floor Editor) |
| PATCH | `/title` | `{ "title": string }` | `StatusResponseDto<StreamMetadataDto>` | management / Editor · `channel:title:write` |
| PATCH | `/game` | `{ "gameName": string }` | `StatusResponseDto<StreamMetadataDto>` | management / Editor · `channel:game:write` |
| PATCH | `/tags` | `{ "tags": string[] }` | `StatusResponseDto<StreamMetadataDto>` | management / Editor · `channel:tags:write` |
| GET | `/status` | — | `StatusResponseDto<StreamStatusDto>` | management · (Gate 1 only) |
| GET | `/categories?query=` | — | `StatusResponseDto<List<StreamCategoryDto>>` | management · (Gate 1 only) |
| GET | `/extensions` | — | `StatusResponseDto<ChannelExtensionsDto>` | management · (Gate 1 only) |
| PUT | `/extensions` | `UpdateChannelExtensionsRequest` | `StatusResponseDto<ChannelExtensionsDto>` | management / Editor · `channel:extensions:write` |
| GET | `/presets` | — | `StatusResponseDto<List<StreamPresetDto>>` | management · (Gate 1 only) |
| POST | `/presets` | `CreateStreamPresetRequest` | `StatusResponseDto<StreamPresetDto>` | management / Editor · `stream:preset:write` |
| PUT | `/presets/{presetId:guid}` | `UpdateStreamPresetRequest` | `StatusResponseDto<StreamPresetDto>` | management / Editor · `stream:preset:write` |
| DELETE | `/presets/{presetId:guid}` | — | `StatusResponseDto<object>` | management / Editor · `stream:preset:write` |
| POST | `/presets/{presetId:guid}/apply` | — | `StatusResponseDto<StreamMetadataDto>` | management / Editor · `channel:title:write` (per-field; game/tags floor Editor) |
| GET | `/scheduled` | — | `StatusResponseDto<List<ScheduledStreamChangeDto>>` | management · (Gate 1 only) |
| POST | `/scheduled` | `ScheduleStreamChangeRequest` | `StatusResponseDto<ScheduledStreamChangeDto>` | management / Editor · `channel:title:write` (per-field floor at schedule time; game/tags floor Editor) |
| DELETE | `/scheduled/{scheduledChangeId:guid}` | — | `StatusResponseDto<object>` | management / Editor · `stream:schedule:write` |

`StreamStatusDto` reused from the existing `StreamController` (`record StreamStatusDto(bool IsLive, int ViewerCount)`) — relocate to `DTOs/StreamTools/`.

### `PlatformAdminController : BaseController`
`[Route("api/v{version:apiVersion}/admin")]` `[Tags("Admin")]`
**Role gate** — these are **Plane C (platform IAM)** rows, default-deny, **no community/management role**. The class-level `[Authorize(Roles="admin")]` is replaced by per-action permission checks inside `IPlatformAdminService`, each resolved through `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)` (owner `roles-permissions.md` §3.7 — authorizes and audits in one call; the ASP.NET `[Authorize(Policy="<key>")]` policy name **is** the permission key verbatim). The controller passes the resolved `Guid principalId` (from `ICurrentUserService` → `IamPrincipals.Id`). This **extends** the existing `AdminController` (stats/channels/users/system/health/events stay, now routed through the IAM gate); the privileged routes below are added.

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|------|-------|-------------|--------------|-----------------------------------|
| GET | `/tenants` | `AdminTenantQuery` (query) + `PageRequestDto` | `PaginatedResponse<AdminTenantDto>` | platform · `tenant:read` |
| GET | `/tenants/{broadcasterId:guid}` | — | `StatusResponseDto<AdminTenantDetailDto>` | platform · `tenant:read` |
| POST | `/tenants/{broadcasterId:guid}/suspend` | `SuspendTenantRequest` | `StatusResponseDto<object>` | platform · `tenant:suspend` |
| POST | `/tenants/{broadcasterId:guid}/reinstate` | `{ "justification": string }` | `StatusResponseDto<object>` | platform · `tenant:suspend` |
| POST | `/tenants/{broadcasterId:guid}/access` | `BeginTenantAccessRequest` | `StatusResponseDto<TenantAccessGrantDto>` | platform · `tenant:access` |
| DELETE | `/access/{accessGrantId:guid}` | — | `StatusResponseDto<object>` | platform · `tenant:access` |
| GET | `/feature-flags` | — | `StatusResponseDto<List<FeatureFlagDto>>` | platform · `featureflag:write` |
| PUT | `/feature-flags` | `UpsertFeatureFlagRequest` | `StatusResponseDto<FeatureFlagDto>` | platform · `featureflag:write` |
| PUT | `/feature-flags/{flagKey}/overrides/{broadcasterId:guid}` | `SetFlagOverrideRequest` | `StatusResponseDto<object>` | platform · `featureflag:write` |
| GET | `/audit` | `AuditSearchQuery` (query) + `PageRequestDto` | `PaginatedResponse<IamAuditEntryDto>` | platform · `audit:read` |

Self-host (IAM tables empty): `IPlatformIamService.AuthorizePlatformAsync` returns allow (owner = full) via the `OwnerIsFullIamService` adapter, so these endpoints work without operator seeding.

### `IpcDevModeController : BaseController`
`[Route("api/v{version:apiVersion}/system/ipc")]` `[Tags("System")]` `[Authorize]`
Manages the **key registry** only (the socket itself is process-local, never HTTP). Gated to owner/self-host: returns `503 ServiceUnavailable` when `DeploymentProfile.Mode == saas`.

| Verb | Route | Request DTO | Response DTO | Auth |
|------|-------|-------------|--------------|------|
| GET | `/` | — | `StatusResponseDto<bool>` (enabled?) | authenticated owner |
| GET | `/keys` | — | `StatusResponseDto<List<IpcDevModeKeyDto>>` | authenticated owner |
| POST | `/keys` | `CreateIpcKeyRequest` | `StatusResponseDto<IpcDevModeKeyDto>` (plaintext once) | authenticated owner |
| DELETE | `/keys/{keyId:guid}` | — | `StatusResponseDto<object>` | authenticated owner |

---

## 6. Pipeline actions

Pipeline actions in this build implement the **single canonical `ICommandAction`** defined in `commands-pipelines.md` §3.13 (`Application/Pipeline`): `string Type` (+ `Category`/`Description`); `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`. They live in `NomNomzBot.Infrastructure/Pipeline/Actions/` and read params from `context.Parameters` (the step's resolved `ConfigJson`). Match the re-targeted `ShoutoutAction`. (The pre-consolidation Infrastructure shape — `ActionType`/`ExecuteAsync(PipelineExecutionContext, ActionDefinition)` — is collapsed away per commands-pipelines §0; do not target it.)

### `SetStreamMetadataAction`
- **Type string:** `set_stream_metadata`
- **Config (`context.Parameters` keys — the step's resolved `ConfigJson`):**
  - `title` (string, optional, supports `{variable}` substitution)
  - `game` (string, optional — game name, resolved to id via Helix search)
  - `tags` (string, optional — comma-separated)
  - `preset_id` (string GUID, optional — apply a saved preset instead of inline fields; inline fields override preset on conflict)
- **Behavior:** reads the tenant from `context.BroadcasterId` (already a `Guid`); applies the metadata via `IStreamToolsService.UpdateMetadataAsync` / `ApplyPresetAsync` (so the **same role floor + Helix push + `Channels` persist + `StreamMetadataUpdatedEvent`** apply); the acting identity is the channel/broadcaster (event triggers run with broadcaster authority, so the floor passes). Returns `ActionResult.Success("metadata updated")` or `ActionResult.Failure(...)` on Helix/validation failure. Fail-closed: unknown/empty config → `Failure`.

> No admin/IPC pipeline actions — Plane-C ops and IPC dev mode are out-of-band of the per-channel pipeline engine.

---

## 7. DI registration

All registrations added to `NomNomzBot.Infrastructure/DependencyInjection.cs` (the single `AddInfrastructure`). Match existing lifetimes: stateless app services = `Scoped`; pipeline actions = `Transient`; hosted listeners = `Singleton` + `AddHostedService`.

```csharp
// Stream tools
services.AddScoped<IStreamToolsService, StreamToolsService>();          // Infrastructure/Services/Stream
services.AddScoped<IStreamEditAuthorizer, StreamEditAuthorizer>();      // Infrastructure/Services/Stream

// Platform admin (Plane C IAM)
services.AddScoped<IPlatformAdminService, PlatformAdminService>();      // Infrastructure/Services/Admin
// IPlatformIamService (the public Plane-C gate) is registered by roles-permissions.md §7 — NOT here.
//   PlatformIamService (SaaS) / OwnerIsFullIamService (self-host), profile-selected. This subsystem consumes it.
services.AddScoped<IIamAuditWriter, IamAuditWriter>();                  // Infrastructure/Services/Identity — INTERNAL audit sink written through by IPlatformIamService

// IPC developer mode
services.AddScoped<IIpcDevModeService, IpcDevModeService>();            // Infrastructure/Services/Ipc

// Pipeline action (transient — stateless), registered alongside the existing ICommandAction list
services.AddTransient<ICommandAction, SetStreamMetadataAction>();

// Repositories (match existing AddScoped<XRepository>() pattern)
services.AddScoped<StreamPresetRepository>();
services.AddScoped<ScheduledStreamChangeRepository>();
services.AddScoped<IpcDevModeKeyRepository>();
// IAM tables read/written via IApplicationDbContext + IUnitOfWork (no dedicated repo unless a hot path warrants one)
```

**Deployment-profile adapter variants (chosen by DI, per the stack doc):**
- **Scheduler** — `ScheduledStreamChangeSchedulerService : BackgroundService` (Infrastructure/BackgroundServices) sweeps `ScheduledStreamChanges` where `Status=pending AND ScheduledFor<=now` and applies them via `IStreamToolsService`. Registered `AddHostedService<...>()`. **Wrap the sweep in `IRunOnceGuard`** (stack-doc tension #8): no-op on lite (single instance), `pg_try_advisory_lock`/`DistributedLock.Postgres` on SaaS, so multi-node does not double-apply. `ScheduledStreamChanges` rows are one-shot (`ScheduledFor` is an absolute instant); the sweep applies each once and moves it to a terminal `Status`. Any recurring-schedule next-fire arithmetic uses **Cronos** (stack §1c) — never hand-rolled.
- **IPC socket listener** — `IpcDevModeListenerService : IHostedService` (Infrastructure) bound to a **local socket only** (Unix domain socket / named pipe per OS); registered **only when** `DeploymentProfile.Mode != saas` AND `ExposureModel` permits — guarded in the DI branch so the SaaS binary never opens it. Authenticates each connection via `IIpcDevModeService.AuthenticateConnectionAsync`. Never binds a TCP/remote endpoint.
- **IAM authorizer** — consumed from `roles-permissions.md` as `IPlatformIamService` (profile-selected: `PlatformIamService` SaaS / `OwnerIsFullIamService` self-host, the latter owner-allow with audit no-op). This subsystem registers no authorizer of its own.
- **Audit/feature-flag persistence** — provider-agnostic via EF Core (Postgres/SQLite chosen by the existing `DbProvider` adapter); `IamAuditLog` is `[APPEND-ONLY]` (`bigint` identity PK, insert-only).

---

## 8. Dependencies (stack-doc libs used)

| Need | Library (party) | Where |
|------|-----------------|-------|
| Web host, controllers, versioning, ProblemDetails | Microsoft.AspNetCore.* + Asp.Versioning.Mvc (2nd) | all three controllers |
| ORM, named query filters (soft-delete + tenant), two providers | EF Core 10 + Npgsql.EntityFrameworkCore.PostgreSQL (3rd) / EFCore.Sqlite (2nd) by DI | all persistence |
| App JSON for `[VC:JSON]` columns (`Tags`, `ContentLabels`) | **Newtonsoft.Json** via hand-rolled `ValueConverter<T,string>`+`ValueComparer` | `StreamPresets`, `ScheduledStreamChanges` |
| Constant-time key compare, key hashing | `System.Security.Cryptography` (`CryptographicOperations.FixedTimeEquals`, SHA-256) in-box (1st) | `IpcDevModeService` |
| Background scheduler + run-once on SaaS | `BackgroundService` + `PeriodicTimer` (in-box) + `IRunOnceGuard` (`DistributedLock.Postgres` 3rd / no-op lite); **Cronos** (3rd) for recurring-cron next-fire arithmetic | `ScheduledStreamChangeSchedulerService` |
| Local IPC transport | `System.Net.Sockets` / named-pipe in-box (1st) | IPC listener |
| Helix calls (update channel info, category search) | existing hand-rolled `ITwitchApiService` over `HttpClient` + Microsoft.Extensions.Http.Resilience (2nd) | stream metadata push |
| Audit/feature-flag JSON-config columns | Newtonsoft.Json `[VC:JSON]` converters | `IamAuditLog.MetadataJson` (read), flag config |
| Logging | `ILogger` + `[LoggerMessage]` source-gen + OpenTelemetry (2nd) | all services |

No **new** third-party dependency is introduced by this subsystem. `DistributedLock.Postgres` / `Cronos` are already accepted in the stack doc (§1c); `DistributedLock.Postgres` runs on the SaaS profile (no-op on lite), and `Cronos` covers recurring-cron next-fire arithmetic.

---

## 9. Decisions (resolved)

- **Preset deletion vs. pending schedules.** Deleting a `StreamPresets` row referenced by a `pending` `ScheduledStreamChanges` row is **refused** with `VALIDATION_FAILED`; the operator cancels the dependent schedule first. This wins over nulling `StreamPresetId` because it keeps the schedule's preset name meaningful and makes operator intent explicit. (`DeletePresetAsync` enforces this; schema permits the nullable `StreamPresetId` form, but the design does not use it for deletion.)
- **Channel-info field gating (CCL / language / branded-content).** `ContentClassificationLabels`, `BroadcasterLanguage`, and branded-content already ride in `UpdateStreamMetadataRequest` / `UpdateChannelInfoRequest` (twitch-helix §4.1) and persist to `Channels`. Each is gated by its own seeded `ActionDefinitions` key — `channel:ccl:write`, `channel:language:write`, `channel:brandedcontent:write` — held to the **same Twitch scope (`channel:manage:broadcast`) and the same Editor floor** as title/game/tags (one key per field, not folded, so a broadcaster can raise one field's floor via `ChannelActionOverride` without touching the others). `UpdateMetadataAsync` floor-checks only the fields actually present in the request.
- **Extensions config — included, low priority.** Channel extensions panel/overlay/component activation (Helix `GET`/`PUT /users/extensions`, Twitch scope `user:edit:broadcast`) is exposed via `IStreamToolsService.Get/UpdateExtensionsAsync` and the `/extensions` routes, gated by `channel:extensions:write` (Editor floor). The scope is progressive (requested when the operator first opens the extensions surface). No domain event — extensions are Twitch-side panel config, not a streamed-metadata change.
- **Whispers — included, gated + rate-limited; lives in chat/messaging, not here.** Sending whispers (Helix `POST /whispers`, Twitch scope `user:manage:whispers`) is a **chat/messaging** capability (spam/ban risk → must be gated + rate-limited), not a stream-admin concern. It is **not** implemented in this subsystem. Its home is the chat/messaging surface (`IChatTransport` / `commands-pipelines.md` chat-send path); its action key is **`chat:whisper:send`** (management plane, `Low` danger tier, Editor floor, rate-limited via the same per-channel send budget as chat sends). **Orchestrator pointer:** assign `chat:whisper:send` + the `POST /whispers` method to the chat/messaging spec (it has no dedicated home yet — twitch-helix §3.3 covers moderation writes but not chat-send; chat-send lives behind `IChatTransport`).
- **Guest Star — skipped.** Twitch **deprecated and shut down the Guest Star API** (and the product) — no endpoints are built on it. Do not spec Guest Star management against a dead API.
- **Charity & Goals — ingest-only, EventSub-owned.** Charity campaigns and creator Goals have **no manageable Helix write endpoints** (Twitch exposes them only as read/EventSub topics — `channel.charity_campaign.*`, `channel.goal.*`). They are therefore **not** a stream-admin write surface. **Orchestrator pointer for the eventsub owner:** add them as an **EventSub ingest** in `twitch-eventsub.md` (subscribe the `channel.charity_campaign.start|progress|stop` and `channel.goal.begin|progress|end` topics → read-side domain events), no write-side service here. (Pointer only — do not heavily edit twitch-eventsub for this; the eventsub owner sizes the ingest.)

All surfaces are pinned by the locked schema, the existing code conventions, and the stack/decisions docs; there is no remaining ambiguity.
