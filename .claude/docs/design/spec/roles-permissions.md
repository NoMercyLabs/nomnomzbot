# Interface Specification — Roles & Permissions Subsystem

**Status:** Implementable. Code from this directly.
**Sources (authoritative):** `2026-06-16-roles-and-permissions.md` (model), `2026-06-16-database-schema.md` (LOCKED schema — Domain B `ChannelMemberships`/`ChannelCommunityStandings`/`ActionDefinitions`/`ChannelActionOverrides`/`PermitGrants`; Domain C `Iam*`; O.9 `IamAuditLog`; O.8 `ModerationAuditLog`), `2026-06-16-stack-and-dependencies.md` (libs), `2026-06-16-decisions-pending-confirmation.md` (defaults).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/...")]`; surrogate PK `guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter; Newtonsoft.Json for app JSON.

---

## 0. Scope, planes, and the load-bearing migration

This subsystem owns **three authorization planes** and the **two gates** that read them:

| Plane | Axis | Gates | Ladder |
|---|---|---|---|
| **A — Community standing** | chat badges (earned/granted) | chat-command + cosmetics (Gate 2 on `community` actions) | `Everyone(0) < Subscriber(2) < Vip(4) < Artist(6) < Moderator(10)` |
| **B — Channel management** | administer the channel (dashboard/HTTP API) | **Gate 1** (entry) + **Gate 2** (per-action) on `management` actions | `Moderator(10) < SuperMod(20) < Editor(30) < Broadcaster(40)` |
| **C — Platform IAM** | NoMercy Labs operators + service accounts (SaaS only) | cross-tenant/privileged platform ops, audited | named permission bundles (least-privilege, default-deny) |

**One unified ordered ladder** spans planes A+B for the per-action gate: `Everyone(0) < Subscriber(2) < Vip(4) < Artist(6) < Moderator(10) < SuperMod(20) < Editor(30) < Broadcaster(40)`. The numeric `LevelValue` is the only thing compared.

- **Gate 1** = pure entry, **entry ≠ permission** (decided 2026-07-04): any authenticated caller may resolve tenant context for a channel that **exists** — community participant and channel manager alike. Fails closed only on a malformed id or a nonexistent channel. It carries **no** role floor: requiring management ≥ Moderator here would 403 every community-plane participant (viewer/sub/VIP) before their Everyone-floored actions ever reached Gate 2. All authorization — community AND management floors — is Gate 2's job, which makes **universal Gate-2 coverage a hard invariant**: every tenant-scoped controller action carries `[RequireAction]` (or a documented exemption), enforced by a reflection test.
- **Gate 2** = per-action: "is the caller's resolved level ≥ this action's effective required level for this channel?" New with this epic; replaces the "almost everything is just `[Authorize]`" gap.
- **`!permit`** — individual grants. **Effective level = MAX(Twitch-badge role, bot-role grants, individual capability grants)**, with two guardrails: **no escalation above the grantor's own level** (applies to every grantor), and **default-deny** (only capabilities whose `ActionDefinition.IsGrantableViaPermit == true` may be granted). The **Broadcaster (channel owner) is fully trusted and MAY grant Critical-tier capabilities** — paternalistic caps on what the owner can delegate do not exist; the owner is the sole authority over their channel. Critical grants are **always to a named individual user, never raised on a whole role tier** (§0.2), and the floor-tier guards that block a dangerous capability from being applied to a low *role* tier still hold.

### 0.2 Dangerous capabilities are delegated to a named individual, never raised on a role tier
A Critical- or ToS-tier capability is delegated by **granting it to a specific user** (a `PermitGrant` of `GrantType=Capability` naming one `UserId`), **never** by lowering the action's required level on a whole `ManagementRole` tier via a `ChannelActionOverride`. The override mechanism may **raise** a floor for everyone but must **not** be used to drop a dangerous capability onto an entire tier. The canonical case: *"one of my mods is a developer and I want them to author sandboxed code."* The decision is to **grant that one person** the capability (`!permit @thatuser code:script:author`, owner-issued), **not** to make every Editor a code author. This keeps the blast radius of a dangerous capability to the exact individual the Broadcaster trusts, while the role tiers stay at their safe defaults. `SetActionOverrideAsync` rejects an override that would set a Critical-tier action's level below its `FloorLevel` (`VALIDATION_FAILED`); per-user reach into Critical capabilities is **only** via the Broadcaster-issued `PermitGrant` path.

### 0.1 Migration note (do NOT silently "fix")
The live code uses `string BroadcasterId` / `string userId` (`ITenantScoped.BroadcasterId : string`, `Permission.Id : int`, `ChannelAccessService` string args). The LOCKED schema (§1.1/§1.2) **widens `ITenantScoped.BroadcasterId` `string → Guid`** and makes all PKs `guid`. This spec targets the **locked `Guid` shape**. The existing generic `Permission` entity and `IPermissionService` string-permission methods are **replaced** by `ActionDefinitions` + `ChannelActionOverrides` + `PermitGrants` and the typed services below (schema §B note: *"Replaces the current generic `Permission`"*). `IChannelAccessService` is **extended in place** (same interface name, widened to `Guid`, Gate-1 logic expanded). `IPermissionService` (existing) is **superseded** by `IActionAuthorizationService` + `IPermitService` — keep the existing file until call sites are migrated, then delete; do not grow it.

---

## 1. Entities (LOCKED schema — owned by this subsystem)

Do **not** redefine columns; the rows below name the table, its key fields, and the schema anchor. All implement `BaseEntity`/`SoftDeletableEntity` per the schema's per-table flag. `[VC:enum]` columns are stored as text via an EF `ValueConverter` (Newtonsoft.Json convention); enums live in `NomNomzBot.Domain.Enums`.

| Entity (table) | Schema | Key fields (type) | Tenant / Global |
|---|---|---|---|
| **`ChannelMembership`** (`ChannelMemberships`) `[soft-delete]` | B.1 | `Id guid` PK; `BroadcasterId guid` FK→Channels; `UserId guid` FK→Users; `ManagementRole string(20)` [VC:enum `ManagementRole`]; `LevelValue int`; `Source string(20)` [VC:enum `MembershipSource`]; `GrantedAt timestamp`; `GrantedByUserId guid?`; `LastSyncedAt timestamp?`. **Unique** `(BroadcasterId, UserId)`. | tenant |
| **`ChannelCommunityStanding`** (`ChannelCommunityStandings`) | B.2 | `Id guid` PK; `BroadcasterId guid` FK→Channels; `UserId guid` FK→Users; `Standing string(20)` [VC:enum `CommunityStanding`]; `LevelValue int`; `Source string(20)` [VC:enum `StandingSource`]; `SubTier string(8)?`; `LastSeenAt timestamp?`. **Unique** `(BroadcasterId, UserId)`. | tenant |
| **`ActionDefinition`** (`ActionDefinitions`) `[GLOBAL, seed]` | B.3 | `Id guid` PK; `ActionKey string(100)` Unique; `Plane string(20)` [VC:enum `AuthPlane`]; `DefaultLevel int`; `FloorLevel int`; `FloorTier string(20)` [VC:enum `DangerTier`]; `IsGrantableViaPermit bool`; `Description string(500)?`. | global |
| **`ChannelActionOverride`** (`ChannelActionOverrides`) `[soft-delete]` | B.4 | `Id guid` PK; `BroadcasterId guid` FK→Channels; `ActionDefinitionId guid` FK→ActionDefinitions; `OverrideLevel int`; `SetByUserId guid?`. **Unique** `(BroadcasterId, ActionDefinitionId)`. | tenant |
| **`PermitGrant`** (`PermitGrants`) `[soft-delete]` | B.5 | `Id guid` PK; `BroadcasterId guid` FK→Channels; `UserId guid` FK→Users; `GrantType string(20)` [VC:enum `PermitGrantType`]; `GrantedRole string(20)?` [VC:enum `ManagementRole`]; `ActionDefinitionId guid?` FK→ActionDefinitions; `GrantedByUserId guid` FK→Users; `ExpiresAt timestamp?`; `RevokedAt timestamp?`; `Reason string(500)?`. **Index** `(BroadcasterId, UserId)`. | tenant |
| **`IamPermission`** (`IamPermissions`) `[GLOBAL, seed]` | C.1 | `Id guid` PK; `Key string(60)` Unique; `Category string(20)` [VC:enum `IamCategory`]; `IsSensitive bool`; `Description string(500)?`. | global |
| **`IamRole`** (`IamRoles`) `[GLOBAL, soft-delete]` | C.2 | `Id guid` PK; `Name string(40)` Unique; `IsSystem bool`; `Description string(500)?`. | global |
| **`IamRolePermission`** (`IamRolePermissions`) | C.3 | `Id guid` PK; `RoleId guid` FK→IamRoles; `PermissionId guid` FK→IamPermissions. **Unique** `(RoleId, PermissionId)`. | global |
| **`IamPrincipal`** (`IamPrincipals`) `[GLOBAL, soft-delete]` | C.4 | `Id guid` PK; `PrincipalType string(20)` [VC:enum `IamPrincipalType`]; `UserId guid?` FK→Users; `Name string(100)`; `EmailCipher string(512)?`; `SubjectKeyId guid?` FK→CryptoKey; `ServiceAccountKeyHash string(128)?`; `IsActive bool`; `ExpiresAt timestamp?`. | global |
| **`IamRoleAssignment`** (`IamRoleAssignments`) | C.5 | `Id guid` PK; `PrincipalId guid` FK→IamPrincipals; `RoleId guid` FK→IamRoles; `ScopeChannelId guid?` FK→Channels (null = platform-wide); `AssignedByPrincipalId guid?`; `ExpiresAt timestamp?`; `RevokedAt timestamp?`; `Reason string(500)?`. **Unique** `(PrincipalId, RoleId, ScopeChannelId)`. | global |
| **`IamAuditLog`** (`IamAuditLog`) `[APPEND-ONLY]` | O.9 | `Id bigint` PK; `PrincipalId guid` FK→IamPrincipals; `PrincipalType string(20)` [VC:enum]; `Permission string(60)`; `TargetBroadcasterId guid?` FK→Channels; `TargetResource string(150)?`; `Justification text?`; `BreakGlass bool`; `Outcome string(20)` [VC:enum `IamOutcome`]; `SourceIpCipher string(255)?` **[PII-shred]**; `OccurredAt timestamp`. | global, append-only |

**Read-only dependency (not owned):** `Users` (A.1 — `Id`, `IsPlatformPrincipal`), `Channels` (A.2 — tenant root), `CryptoKey` (Q.1 — `IamPrincipal.SubjectKeyId`), `ModerationAuditLog` (O.8 — written by moderation subsystem; this subsystem reads `cross_tenant_access` rows only when correlating Plane-C access).

### 1.1 Enums (Domain/Enums; `[VC:enum]` text-stored)
`ManagementRole { Moderator, SuperMod, Editor, Broadcaster }` (values 10/20/30/40 via `[Display]`/extension `ToLevel()`).
`CommunityStanding { Everyone, Subscriber, Vip, Artist, Moderator }` (0/2/4/6/10).
`AuthPlane { Community, Management }`.
`DangerTier { Critical, Tos, Low }`.
`MembershipSource { TwitchBadge, HelixEditors, BotGrant, Owner }`.
`StandingSource { ChatTags, EventSubBadge }`.
`PermitGrantType { Role, Capability }`.
`IamCategory { Tenant, Billing, Audit, Iam, FeatureFlag }`.
`IamPrincipalType { Employee, ServiceAccount }`.
`IamOutcome { Allowed, Denied }`.
**Extend (do NOT redefine):** the existing `Domain/Enums/PermissionLevel.cs` (`Everyone, Subscriber, Vip, Moderator, Broadcaster`) is the legacy 5-rung enum used by `ChatMessageHandler.HasPermission`. `PermissionLevel` is extended in place to the full unified ladder (one ladder, no second style; no parallel `AuthLevel`): `Artist`, `SuperMod`, `Editor` rungs are added, giving the final order `Everyone, Subscriber, Vip, Artist, Moderator, SuperMod, Editor, Broadcaster` with an `int ToLevelValue(this PermissionLevel)` extension returning 0/2/4/6/10/20/30/40.

---

## 2. Domain events

Events inherit the canonical `DomainEventBase` (platform-conventions §2.0 — `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; do **NOT** redeclare these). Published via `IEventBus`, placed in `NomNomzBot.Domain.Events`. They are **sealed `record`s** — a record may only derive from a record, and the canonical base is an `abstract record`, so events must be records, not classes — with init-only required props; the publisher sets the inherited `Guid BroadcasterId` (tenant-scoped events) or leaves it `Guid.Empty` (platform-level).

```csharp
public sealed record ManagementRoleChangedEvent : DomainEventBase  // B.1 upsert/soft-delete
{
    public required Guid TargetUserId { get; init; }
    public required ManagementRole? OldRole { get; init; }          // null = newly added
    public required ManagementRole? NewRole { get; init; }          // null = removed
    public required MembershipSource Source { get; init; }
    public required Guid? ChangedByUserId { get; init; }            // null = Helix/badge sync
}

public sealed record CommunityStandingChangedEvent : DomainEventBase // B.2 upsert
{
    public required Guid TargetUserId { get; init; }
    public required CommunityStanding OldStanding { get; init; }
    public required CommunityStanding NewStanding { get; init; }
    public required StandingSource Source { get; init; }
}

public sealed record ActionLevelOverriddenEvent : DomainEventBase   // B.4 set/reset
{
    public required Guid ActionDefinitionId { get; init; }
    public required string ActionKey { get; init; }
    public required int? OldLevel { get; init; }                    // null = was default
    public required int NewEffectiveLevel { get; init; }            // floor-clamped result
    public required Guid SetByUserId { get; init; }
}

public sealed record PermitGrantedEvent : DomainEventBase          // B.5 create
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required PermitGrantType GrantType { get; init; }
    public required ManagementRole? GrantedRole { get; init; }
    public required string? CapabilityActionKey { get; init; }
    public required Guid GrantedByUserId { get; init; }
    public required DateTime? ExpiresAt { get; init; }
}

public sealed record PermitRevokedEvent : DomainEventBase          // B.5 revoke/expire
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required Guid? RevokedByUserId { get; init; }            // null = auto-expiry
    public required string Reason { get; init; }                   // "unpermit" | "expired"
}

public sealed record AuthorizationDeniedEvent : DomainEventBase    // Gate 1 or Gate 2 denial
{
    public required Guid CallerUserId { get; init; }
    public required string ActionKey { get; init; }                // "" for Gate-1 entry denial
    public required int RequiredLevel { get; init; }
    public required int CallerLevel { get; init; }
    public required string Gate { get; init; }                     // "gate1" | "gate2"
}

public sealed record IamAccessEvaluatedEvent : DomainEventBase     // Plane C → also persists IamAuditLog
{
    public required Guid PrincipalId { get; init; }
    public required string Permission { get; init; }
    public required Guid? TargetBroadcasterId { get; init; }
    public required bool BreakGlass { get; init; }
    public required IamOutcome Outcome { get; init; }
}
```

---

## 3. Service interfaces

All in `NomNomzBot.Application.Common.Interfaces` (matching `IChannelAccessService`'s location); impls in `NomNomzBot.Infrastructure.Services.Identity` (matching `ChannelAccessService`). All read/write through `IApplicationDbContext` + `IUnitOfWork`; mutating methods publish the events in §2 and `await _uow.SaveChangesAsync(ct)` before returning.

### 3.1 `IChannelAccessService` — Gate 1 (EXTEND existing)
Existing interface kept; signature widened `string → Guid` per §0.1; `IsPlatformPrincipal`/Plane-C path replaces the `User.IsAdmin` check.

> **Single owner of the full surface:** `IChannelAccessService` is defined authoritatively in `platform-conventions.md` §3.2, which carries **both** `CanResolveTenantAsync` *and* `ResolveOwnChannelAsync` (the owner-channel resolver the `TenantResolutionMiddleware`/`OBSRelayHub` IDOR fix needs). This subsystem only consumes/extends the `CanResolveTenantAsync` member shown below — do not redeclare a narrower interface; the type has both methods.

```csharp
public interface IChannelAccessService
{
    // Gate 1 = pure entry (entry ≠ permission, §0): any authenticated caller resolves tenant context for a
    // channel that exists. Fails closed only on malformed id / nonexistent channel; all floors are Gate 2's.
    Task<bool> CanResolveTenantAsync(Guid userId, Guid channelId, CancellationToken cancellationToken = default);

    // Owner-channel resolver — defined in platform-conventions.md §3.2; listed here so the surface is not truncated.
    Task<Result<Guid>> ResolveOwnChannelAsync(Guid userId, CancellationToken cancellationToken = default);
}
```

### 3.2 `IRoleResolver` — resolves the effective level (the MAX rule)
```csharp
public interface IRoleResolver
{
    // Computes the caller's effective unified-ladder level for a channel:
    // MAX(community standing, management role, active non-expired !permit role grant). Pure read; no writes.
    Task<Result<int>> ResolveEffectiveLevelAsync(Guid userId, Guid broadcasterId, CancellationToken cancellationToken = default);

    // Full breakdown for the permissions UI / debugging: each plane's contributing level + the winning source.
    Task<Result<ResolvedAccessDto>> ResolveAccessAsync(Guid userId, Guid broadcasterId, CancellationToken cancellationToken = default);

    // True if the caller holds (via role OR direct capability grant OR resolved level) the given action key.
    // The canonical "can this user do this action" rule — backs BOTH chat-command gating and HTTP Gate 2.
    Task<Result<bool>> HasCapabilityAsync(Guid userId, Guid broadcasterId, string actionKey, CancellationToken cancellationToken = default);
}
```

### 3.3 `IActionAuthorizationService` — Gate 2 + per-action config (REPLACES generic `IPermissionService` authz)
```csharp
public interface IActionAuthorizationService
{
    // Gate 2: effectiveRequired = clamp(override ?? default, floor, Broadcaster); allow iff resolved caller level ≥ it
    // OR the caller holds a direct per-user capability grant for this exact action (the HTTP mirror of
    // HasCapabilityAsync — how a broadcaster delegates an above-floor, permit-grantable action such as
    // channel:title:write to a specific mod; the bot then acts on the broadcaster's own token). Bounded by
    // construction: a grant can only exist for an IsGrantableViaPermit action, so non-delegable Critical actions
    // stay locked. Emits AuthorizationDeniedEvent on deny. Fails closed if action key unknown.
    Task<Result<bool>> AuthorizeActionAsync(Guid userId, Guid broadcasterId, string actionKey, CancellationToken cancellationToken = default);

    // Resolved required level for one action in a channel (override clamped to floor). Read-only; drives UI + AuthorizeActionAsync.
    Task<Result<int>> GetEffectiveLevelAsync(Guid broadcasterId, string actionKey, CancellationToken cancellationToken = default);

    // Full per-channel action matrix (definition + default + floor + tier + current override + grantable flag) for the permissions screen.
    Task<Result<IReadOnlyList<ActionPermissionDto>>> GetActionMatrixAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    // Upsert ChannelActionOverride. Validates clamp to [floor, Broadcaster]; rejects below floor (VALIDATION_FAILED).
    // Emits ActionLevelOverriddenEvent. Returns the stored effective level.
    Task<Result<int>> SetActionOverrideAsync(Guid broadcasterId, string actionKey, int level, Guid setByUserId, CancellationToken cancellationToken = default);

    // Soft-delete the override → action reverts to its global default. Emits ActionLevelOverriddenEvent (NewEffectiveLevel = default).
    Task<Result> ResetActionOverrideAsync(Guid broadcasterId, string actionKey, Guid setByUserId, CancellationToken cancellationToken = default);
}
```

### 3.4 `IMembershipService` — Plane B ladder writes + Helix/badge sync
```csharp
public interface IMembershipService
{
    // Upsert a management-ladder membership (mod/super-mod/editor/broadcaster). Recomputes LevelValue.
    // Guardrail: grantedByUserId may not grant a role above their own resolved level (FORBIDDEN). Emits ManagementRoleChangedEvent.
    Task<Result<ChannelMembershipDto>> SetManagementRoleAsync(Guid broadcasterId, Guid userId, ManagementRole role, MembershipSource source, Guid? grantedByUserId, CancellationToken cancellationToken = default);

    // Soft-delete a membership (demote/remove). Owner (Source=Owner) is non-removable (VALIDATION_FAILED). Emits ManagementRoleChangedEvent(NewRole=null).
    Task<Result> RemoveManagementRoleAsync(Guid broadcasterId, Guid userId, Guid? removedByUserId, CancellationToken cancellationToken = default);

    // Reconcile twitch_badge + helix_editors-sourced memberships from a freshly-fetched snapshot (idempotent upsert + prune of stale synced rows;
    // bot_grant/owner rows untouched). Sets LastSyncedAt. Emits ManagementRoleChangedEvent per delta.
    Task<Result> SyncManagementFromTwitchAsync(Guid broadcasterId, IReadOnlyList<TwitchManagementMember> snapshot, CancellationToken cancellationToken = default);

    // Paginated membership list for the dashboard roles screen.
    Task<Result<PaginatedResponse<ChannelMembershipDto>>> ListMembershipsAsync(Guid broadcasterId, int page, int pageSize, CancellationToken cancellationToken = default);
}
```

### 3.5 `ICommunityStandingService` — Plane A writes (chat-tag / EventSub badge sourced)
```csharp
public interface ICommunityStandingService
{
    // Upsert a viewer's community standing from chat tags or an EventSub badge. Recomputes LevelValue.
    // Emits CommunityStandingChangedEvent only on change. Hot-path friendly (single upsert).
    Task<Result> UpsertStandingAsync(Guid broadcasterId, Guid userId, CommunityStanding standing, StandingSource source, string? subTier, CancellationToken cancellationToken = default);

    // Read a viewer's current standing (Everyone if none recorded).
    Task<Result<CommunityStanding>> GetStandingAsync(Guid broadcasterId, Guid userId, CancellationToken cancellationToken = default);
}
```

### 3.6 `IPermitService` — `!permit` / `!unpermit` (REPLACES generic grant/revoke)
```csharp
public interface IPermitService
{
    // !permit @user <role>. Guardrails: (1) role ≤ Broadcaster (Owner is non-grantable — owner is sole, set at install, never via permit; else FORBIDDEN);
    // (2) no-escalation — granted role ≤ grantor's resolved level (else FORBIDDEN). Optional expiry. Emits PermitGrantedEvent.
    Task<Result<PermitGrantDto>> GrantRoleAsync(Guid broadcasterId, Guid targetUserId, ManagementRole role, Guid grantedByUserId, DateTime? expiresAt, string? reason, CancellationToken cancellationToken = default);

    // !permit @user <actionKey>. Always a per-user capability grant (§0.2 — never raised on a role tier). Guardrails:
    // (1) default-deny — ActionDefinition.IsGrantableViaPermit must be true (else FORBIDDEN);
    // (2) no-escalation — the grantor must themselves be authorized for the action (Critical-tier actions are thus
    //     grantable only by the Broadcaster, who is fully trusted and sits at/above every floor — §0, §0.2);
    // (3) tier guard — a Critical/ToS capability is delegated to this one named UserId, never dropped onto a role tier.
    // Optional expiry. Emits PermitGrantedEvent.
    Task<Result<PermitGrantDto>> GrantCapabilityAsync(Guid broadcasterId, Guid targetUserId, string actionKey, Guid grantedByUserId, DateTime? expiresAt, string? reason, CancellationToken cancellationToken = default);

    // !unpermit @user [actionKey|role]. Soft-deletes matching active grant(s); sets RevokedAt. Emits PermitRevokedEvent.
    Task<Result> RevokeAsync(Guid broadcasterId, Guid targetUserId, string? actionKeyOrRole, Guid revokedByUserId, CancellationToken cancellationToken = default);

    // Active (non-expired, non-revoked) grants for a channel — permissions UI + audit.
    Task<Result<IReadOnlyList<PermitGrantDto>>> ListActiveGrantsAsync(Guid broadcasterId, CancellationToken cancellationToken = default);

    // Sweep: soft-delete grants past ExpiresAt (called by a BackgroundService). Emits PermitRevokedEvent(Reason="expired") per row. Returns count.
    Task<Result<int>> ExpireDueGrantsAsync(CancellationToken cancellationToken = default);
}
```

### 3.7 `IPlatformIamService` — Plane C (SaaS only; no-op/owner=full on self-host)
```csharp
public interface IPlatformIamService
{
    // Plane-C authorization: does the principal hold this permission (optionally scoped to a tenant)?
    // ALWAYS writes IamAuditLog (allowed|denied) and emits IamAccessEvaluatedEvent. Self-host (no IamPrincipals) → owner=full (true), audit no-op.
    Task<Result<bool>> AuthorizePlatformAsync(Guid principalId, string permissionKey, Guid? targetBroadcasterId, bool breakGlass, string? justification, CancellationToken cancellationToken = default);

    // Resolve the IamPrincipal for an authenticated platform user (Users.IsPlatformPrincipal). Null if not a platform principal.
    Task<Result<IamPrincipalDto?>> ResolvePrincipalAsync(Guid userId, CancellationToken cancellationToken = default);

    // True when at least one IamPrincipal row exists — the service's own "is SaaS" discriminator, exposed for the
    // ASP.NET Plane-C policy handler: a caller holding the platform-principal claim but NO principal row is allowed
    // ONLY in the zero-principals (self-host implicit-full) state. The handler cannot probe via AuthorizePlatformAsync
    // with a sentinel id — on SaaS that would write an IamAuditLog row violating its FK to IamPrincipals.
    Task<bool> HasAnyPrincipalsAsync(CancellationToken cancellationToken = default);

    // Provision a new IamPrincipal: an employee (PrincipalType=Employee over an existing User flagged IsPlatformPrincipal)
    // or a service account (PrincipalType=ServiceAccount — generates a key, stores its ServiceAccountKeyHash, returns it once via the principal).
    // Assigns the requested RoleIds. Requires the acting principal to hold iam:principal:create. Emits via audit.
    Task<Result<IamPrincipalDto>> CreatePrincipalAsync(Guid actingPrincipalId, CreatePrincipalRequest request, CancellationToken cancellationToken = default);

    // Assign a role to a principal, optionally tenant-scoped + time-boxed. Requires caller iam:manage. Emits via audit.
    Task<Result<IamRoleAssignmentDto>> AssignRoleAsync(Guid actingPrincipalId, Guid principalId, Guid roleId, Guid? scopeChannelId, DateTime? expiresAt, string? reason, CancellationToken cancellationToken = default);

    // Revoke an assignment (sets RevokedAt). Requires caller iam:manage.
    Task<Result> RevokeAssignmentAsync(Guid actingPrincipalId, Guid assignmentId, string? reason, CancellationToken cancellationToken = default);

    // Effective platform permission keys for a principal (union over active, non-expired role assignments).
    Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(Guid principalId, Guid? scopeChannelId, CancellationToken cancellationToken = default);
}
```

---

## 4. DTOs / contracts

Records in `NomNomzBot.Application.Contracts.Authorization` (new domain folder). Inbound request records in the same namespace; controller-local DTOs follow the existing `PermissionsController` pattern only for tiny request bodies.

```csharp
// ── Resolution / read models ────────────────────────────────────────────────
public sealed record ResolvedAccessDto(
    Guid UserId, Guid BroadcasterId, int EffectiveLevel,
    CommunityStanding CommunityStanding, int CommunityLevel,
    ManagementRole? ManagementRole, int ManagementLevel,
    ManagementRole? PermitRole, IReadOnlyList<string> PermitCapabilities,
    string WinningSource);                                    // "community" | "management" | "permit"

public sealed record ChannelMembershipDto(
    Guid Id, Guid UserId, string? Username, ManagementRole Role, int LevelValue,
    MembershipSource Source, Guid? GrantedByUserId, DateTime GrantedAt, DateTime? LastSyncedAt);

public sealed record ActionPermissionDto(
    Guid ActionDefinitionId, string ActionKey, AuthPlane Plane, string? Description,
    int DefaultLevel, int FloorLevel, DangerTier FloorTier, bool IsGrantableViaPermit,
    int? OverrideLevel, int EffectiveLevel);                  // EffectiveLevel = clamp(override ?? default, floor, Broadcaster)

public sealed record PermitGrantDto(
    Guid Id, Guid UserId, string? Username, PermitGrantType GrantType,
    ManagementRole? GrantedRole, string? CapabilityActionKey,
    Guid GrantedByUserId, DateTime? ExpiresAt, DateTime? RevokedAt, string? Reason, DateTime CreatedAt);

public sealed record IamPrincipalDto(
    Guid Id, IamPrincipalType PrincipalType, Guid? UserId, string Name, bool IsActive, DateTime? ExpiresAt);

public sealed record IamRoleAssignmentDto(
    Guid Id, Guid PrincipalId, Guid RoleId, string RoleName, Guid? ScopeChannelId,
    DateTime? ExpiresAt, DateTime? RevokedAt, string? Reason, DateTime CreatedAt);

// External snapshot fed into membership sync (built by the Twitch integration subsystem).
public sealed record TwitchManagementMember(
    Guid UserId, string TwitchUserId, ManagementRole Role, MembershipSource Source);

// ── Inbound request records ─────────────────────────────────────────────────
public sealed record SetActionLevelRequest(string ActionKey, int Level);
public sealed record SetManagementRoleRequest(Guid UserId, ManagementRole Role);
public sealed record GrantPermitRequest(Guid UserId, PermitGrantType GrantType,
    ManagementRole? Role, string? ActionKey, DateTime? ExpiresAt, string? Reason);
public sealed record AssignIamRoleRequest(Guid PrincipalId, Guid RoleId,
    Guid? ScopeChannelId, DateTime? ExpiresAt, string? Reason);
public sealed record CreatePrincipalRequest(IamPrincipalType PrincipalType, Guid? UserId,
    string DisplayName, IReadOnlyList<Guid> RoleIds, string? ServiceAccountName);
```

---

## 5. Controller endpoints

Two new controllers + one extended. All `[ApiVersion("1.0")]`, inherit `BaseController`, `[Authorize]`, return `StatusResponseDto<T>`/`PaginatedResponse<T>` via `ResultResponse(...)`.

**Role gate** — **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the gate column before the service call (403 `FORBIDDEN` when the caller's resolved level is below the action's effective level). **Plane-C** rows (§5.4) = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)`; the ASP.NET `[Authorize(Policy="<key>")]` policy name **is** the permission key verbatim. The keys are seeded global `ActionDefinitions` (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.

### 5.1 `RolesController` — `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/roles")]`
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<PaginatedResponse<ChannelMembershipDto>>` | management / Moderator · `roles:read` |
| PUT | `/management` | `SetManagementRoleRequest` | `StatusResponseDto<ChannelMembershipDto>` | management / Broadcaster · `roles:manage` (Critical: managing mods/roles) |
| DELETE | `/management/{userId:guid}` | — | `StatusResponseDto<object>` | management / Broadcaster · `roles:manage` |
| GET | `/effective/{userId:guid}` | — | `StatusResponseDto<ResolvedAccessDto>` | management / Moderator · `roles:read` |

### 5.2 `ActionPermissionsController` — `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/action-permissions")]`
(Replaces the generic `PermissionsController` grant/revoke surface; the old controller is removed once callers migrate.)
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<List<ActionPermissionDto>>` (the matrix) | management / Moderator · `roles:read` |
| PUT | `/` | `SetActionLevelRequest` | `StatusResponseDto<int>` (effective level) | management / Broadcaster · `roles:manage` |
| DELETE | `/{actionKey}` | — | `StatusResponseDto<object>` | management / Broadcaster · `roles:manage` |

### 5.3 `PermitsController` — `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/permits")]`
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<List<PermitGrantDto>>` | management / Moderator · `roles:read` |
| POST | `/` | `GrantPermitRequest` | `StatusResponseDto<PermitGrantDto>` | management / Broadcaster · `permit:issue` (configurable default, lowerable to Editor) |
| DELETE | `/{userId:guid}` | `?actionKeyOrRole=` (query) | `StatusResponseDto<object>` | management · `permit:issue` |

> Chat `!permit`/`!unpermit` are **not** HTTP — they enter via `ChatMessageHandler` → `IPermitService`, gated by `IActionAuthorizationService.AuthorizeActionAsync(..., "permit:issue")`. The HTTP `PermitsController` is the dashboard equivalent.

### 5.4 `PlatformIamController` — `[Route("api/v{version:apiVersion}/platform/iam")]` (SaaS only)
`[Authorize]` **plus** Plane-C check in each action via `IPlatformIamService.AuthorizePlatformAsync`. Not a channel route — no tenant resolution.
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/principals/{principalId:guid}/permissions` | `?scopeChannelId=` | `StatusResponseDto<List<string>>` | platform · `iam:manage` (or self) |
| POST | `/assignments` | `AssignIamRoleRequest` | `StatusResponseDto<IamRoleAssignmentDto>` | platform · `iam:manage` |
| DELETE | `/assignments/{assignmentId:guid}` | `?reason=` | `StatusResponseDto<object>` | platform · `iam:manage` |

### 5.5 Extend existing `AdminController`
Migrate its `User.IsAdmin` gate to `IPlatformIamService.AuthorizePlatformAsync(..., "tenant:access" | "tenant:suspend")`. No new routes here beyond rewiring the gate; this is the "replaces `User.IsAdmin`" step from the model doc.

---

## 6. Pipeline actions

This subsystem ships **two** pipeline actions (the `!permit`/`!unpermit` flow as pipeline steps), in `NomNomzBot.Infrastructure/Pipeline/Actions/`, implementing the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13 (`string Type` + `Category`/`Description`; `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`), registered as `AddTransient<ICommandAction, …>`.

| Action class | `Type` | Config DTO | Behavior |
|---|---|---|---|
| `PermitAction` | `permit` | `PermitActionConfig(string TargetVariable, string RoleOrCapability, int? DurationMinutes)` | Resolves `{{target}}` user, calls `IPermitService.GrantRoleAsync`/`GrantCapabilityAsync` (role vs capability decided by whether the token matches a `ManagementRole` or an `ActionKey`); honors the no-escalation + `IsGrantableViaPermit` default-deny guardrails (§3.6); `DurationMinutes` → `ExpiresAt`. Fail-closed: unknown role/capability stops the pipeline (`Result.Failure`). |
| `UnpermitAction` | `unpermit` | `UnpermitActionConfig(string TargetVariable, string? RoleOrCapability)` | Resolves `{{target}}`, calls `IPermitService.RevokeAsync`. Null `RoleOrCapability` revokes all active grants for the user. |

Config DTOs live in `NomNomzBot.Application.Contracts.Pipeline` (matching the existing action-config convention), serialized with Newtonsoft.Json.

> Gate-2 enforcement for these actions is the **chat command's own** required level on `permit:issue` (checked in `ChatMessageHandler` before pipeline execution); the actions themselves re-assert the `IPermitService` guardrails so they are safe if invoked from any pipeline.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, beside the existing identity registrations (lines ~163–165). Lifetimes: **scoped** (per-request, DbContext-bound), matching `ChannelAccessService`/`CurrentTenantService`. Pipeline actions transient (matching existing actions).

```csharp
// Authorization — three planes + two gates
services.AddScoped<IChannelAccessService, ChannelAccessService>();          // EXISTING — keep (Gate 1, widened)
services.AddScoped<IRoleResolver, RoleResolver>();
services.AddScoped<IActionAuthorizationService, ActionAuthorizationService>(); // Gate 2 + per-action config
services.AddScoped<IMembershipService, MembershipService>();                // Plane B writes + Twitch sync
services.AddScoped<ICommunityStandingService, CommunityStandingService>();   // Plane A writes
services.AddScoped<IPermitService, PermitService>();                        // !permit / !unpermit

// Pipeline actions
services.AddTransient<ICommandAction, PermitAction>();
services.AddTransient<ICommandAction, UnpermitAction>();

// Plane C — Platform IAM: profile-adapter (SaaS = DB-backed; self-host = owner-is-full no-op)
if (deploymentMode == DeploymentMode.Saas)
    services.AddScoped<IPlatformIamService, PlatformIamService>();          // reads Iam* tables + writes IamAuditLog
else
    services.AddScoped<IPlatformIamService, OwnerIsFullIamService>();        // self-host: always-allow, audit no-op (Plane C collapses)
```

**Deployment-profile adapter variant:** `IPlatformIamService` is the one swappable adapter — `PlatformIamService` (SaaS, `DeploymentMode.Saas`) vs `OwnerIsFullIamService` (self-host lite/full), selected by the `DeploymentProfile.Mode` boot switch (per the deployment-profile axis). The other six services have a single impl across profiles. **Seeding:** `ActionDefinitions`, `IamPermissions`, `IamRoles`, `IamRolePermissions` are `[GLOBAL, seed]` — add to the existing `DataSeeder` (line ~170).

**Bootstrap (first super-admin):** the very first platform super-admin `IamPrincipal` is **seeded once at startup from configuration** (`Platform:BootstrapAdminUserId` / env), idempotent — on boot, if no super-admin principal exists and the configured User id is present, create the `Employee` principal and assign the system super-admin `IamRole`; otherwise no-op. This is the **only** principal not created via `IPlatformIamService.CreatePrincipalAsync` (which requires an existing acting principal with `iam:principal:create`) — it breaks the chicken-and-egg so every subsequent principal is provisioned through the audited service path.

---

## 7.1 Seed catalogue (canonical reference data)

These are the `[GLOBAL, seed]` rows the `DataSeeder` writes (idempotent upsert by natural key) for `ActionDefinitions` (B.3), `IamPermissions` (C.1), `IamRoles` (C.2), and `IamRolePermissions` (C.3). Without them Gate 2 fails closed (403) on every gated route. Columns follow the schema: an `ActionDefinition` is `ActionKey` · `Plane` (`AuthPlane{Community,Management}`) · `DefaultLevel` · `FloorLevel` · `FloorTier` (`DangerTier{Critical,Tos,Low}`) · `IsGrantableViaPermit`. Level values: `Everyone(0)`, `Subscriber(2)`, `Vip(4)`, `Artist(6)`, `Moderator(10)`, `SuperMod(20)`, `Editor(30)`, `Broadcaster(40)`.

**Rule:** unless a row shows an explicit `Default X, Floor Y`, `DefaultLevel = FloorLevel`. `DefaultLevel` is the **out-of-the-box** required level — the action's Twitch base role — so a lower-standing viewer (e.g. a VIP or Sub) gets **nothing extra by default**. `FloorLevel` is the **lowest a broadcaster may set** via `ChannelActionOverride`: the override is clamped to `[FloorLevel, Broadcaster(40)]`, so a broadcaster may **raise** an action as high as Broadcaster **or lower** it as far as its floor — never below `FloorLevel`, and Critical-tier rows are not lowerable at all. A floor sits **below** the default only where abusing the action **cannot cause irreversible or serious harm** — non-destructive reads and reversible, non-destructive writes — so the broadcaster can *choose* to open them to a trusted VIP/Sub. Destructive, irreversible, Twitch-mutating, currency, or role/IAM actions keep `Floor = Default` at Moderator+ (or Broadcaster/Critical) and can never be lowered to VIP. The per-spec §5 controller tables are the **source** of these rows and must stay in sync — a §5 cell whose action key is absent here is a seed bug.

### ActionDefinitions — Management plane
`Plane = Management`; `DefaultLevel = FloorLevel`, `Tier = Low`, and `Grant = true` unless noted. A `Default X, Floor Y` cell marks a **broadcaster-lowerable** action: it defaults to `X` (its base role) but the broadcaster may lower the requirement as far as `Y`.

| ActionKey | Floor | Tier | Grant |
|---|---|---|---|
| commands:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| commands:write | Editor(30) | Low | true |
| commands:builtin:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| commands:builtin:write | Editor(30) | Low | true |
| pipelines:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| pipelines:write | Editor(30) | Low | true |
| pipelines:validate | **Default Moderator(10), Floor Vip(4)** | Low | true |
| eventresponses:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| eventresponses:write | Editor(30) | Low | true |
| timers:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| timers:write | Editor(30) | Low | true |
| quotes:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| quotes:write | **Default Moderator(10), Floor Vip(4)** | Low | true |
| quotes:delete | Moderator(10) | Low | true |
| giveaways:read | Moderator(10) | Low | true |
| giveaways:write | Moderator(10) | Low | true |
| giveaways:codes:write | Broadcaster(40) | Critical | false |
| engagement:read | Moderator(10) | Low | true |
| engagement:write | Editor(30) | Low | true |
| customdata:read | Moderator(10) | Low | true |
| customdata:write | Editor(30) | Low | true |
| viewerdata:read | Moderator(10) | Low | true |
| viewerdata:write | Editor(30) | Low | true |
| roles:read | Moderator(10) | Low | true |
| roles:manage | Broadcaster(40) | Critical | false |
| permit:issue | **Default Broadcaster(40), Floor Editor(30)** | Low | true |
| code:script:author | Broadcaster(40) | Critical | true |
| discord:connection:read | Moderator(10) | Low | false |
| discord:connection:write | SuperMod(20) | Low | false |
| discord:config:read | Moderator(10) | Low | false |
| discord:config:write | SuperMod(20) | Low | false |
| discord:role:read | Moderator(10) | Low | false |
| discord:role:write | SuperMod(20) | Low | false |
| discord:optin:write | SuperMod(20) | Low | false |
| discord:dispatch:read | Moderator(10) | Low | false |
| moderation:read | Moderator(10) | Low | true |
| moderation:queue:read | Moderator(10) | Low | true |
| moderation:queue:resolve | Moderator(10) | Low | true |
| moderation:action:read | Moderator(10) | Low | true |
| moderation:timeout | Moderator(10) | Low | true |
| moderation:ban | Moderator(10) | Low | true |
| moderation:unban | Moderator(10) | Low | true |
| moderation:delete_message | Moderator(10) | Low | true |
| moderation:warn | Moderator(10) | Low | true |
| moderation:note:write | Moderator(10) | Low | true |
| moderation:automod:read | Moderator(10) | Low | true |
| moderation:automod:write | SuperMod(20) | Low | true |
| moderation:escalation:read | Moderator(10) | Low | true |
| moderation:escalation:write | SuperMod(20) | Low | true |
| moderation:filter:read | Moderator(10) | Low | true |
| moderation:filter:write | SuperMod(20) | Low | true |
| moderation:nuke | SuperMod(20) | Critical | false |
| moderation:nuke:read | SuperMod(20) | Low | true |
| moderation:sharedban:read | SuperMod(20) | Low | true |
| moderation:sharedban:write | SuperMod(20) | Critical | false |
| moderation:report:read | Moderator(10) | Low | true |
| moderation:report:triage | SuperMod(20) | Low | true |
| moderation:evidence:build | Moderator(10) | Low | true |
| moderation:usercontext:read | Moderator(10) | Low | true |
| tts:config:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| tts:config:write | Editor(30) | Low | true |
| tts:voice:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| tts:voice:test | Moderator(10) | Low | true |
| tts:uservoice:write | Moderator(10) | Low | true |
| tts:queue:review | Moderator(10) | Low | true |
| eventsub:read | Moderator(10) | Low | true |
| eventsub:subscribe | Editor(30) | Low | true |
| eventsub:unsubscribe | Editor(30) | Low | true |
| twitch:diagnostics:read | Moderator(10) | Low | true |
| music:config:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| music:config:write | Editor(30) | Low | true |
| music:queue:moderate | Moderator(10) | Low | true |
| music:token:read | Editor(30) | Low | true |
| music:token:rotate | Broadcaster(40) | Critical | false |
| media:read | Moderator(10) | Low | true |
| media:moderate | Moderator(10) | Low | true |
| media:write | Editor(30) | Low | true |
| sounds:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| sounds:write | Editor(30) | Low | true |
| stream:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| stream:preset:write | Editor(30) | Low | true |
| stream:schedule:write | Editor(30) | Low | true |
| obs:control | Moderator(10) | Low | true |
| obs:config:read | Broadcaster(40) | Low | true |
| obs:config:write | Broadcaster(40) | Critical | false |
| obs:control:broadcast | Broadcaster(40) | Critical | false |
| vts:config:read | Moderator(10) | Low | true |
| vts:config:write | Broadcaster(40) | Low | false |
| vts:control | Moderator(10) | Low | true |
| webhooks:inbound:read | Moderator(10) | Low | true |
| webhooks:inbound:write | Editor(30) | Low | true |
| webhooks:outbound:read | Moderator(10) | Low | true |
| webhooks:outbound:write | Editor(30) | Low | true |
| bundles:read | Moderator(10) | Low | true |
| bundles:export | Editor(30) | Low | true |
| bundles:import | Editor(30) | Low | true |
| bundles:publish | Broadcaster(40) | Low | true |
| supporters:read | Moderator(10) | Low | true |
| supporters:config:write | Broadcaster(40) | Critical | false |
| automation:tokens:read | Editor(30) | Low | true |
| automation:tokens:write | Broadcaster(40) | Critical | false |
| widget:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| widget:write | Editor(30) | Low | true |
| eventstore:journal:read | Broadcaster(40) | Low | true |
| eventstore:projection:read | Moderator(10) | Low | true |
| eventstore:projection:rebuild | Broadcaster(40) | Low | true |
| eventstore:replay:write | Broadcaster(40) | Low | true |
| eventstore:replay:republish | Broadcaster(40) | Low | true |
| economy:config:read | Moderator(10) | Low | true |
| economy:config:write | Editor(30) | Low | true |
| games:session:read | Moderator(10) | Low | true |
| games:session:start | Moderator(10) | Low | true |
| games:session:cancel | Moderator(10) | Low | true |
| economy:catalog:create | Editor(30) | Low | true |
| economy:catalog:update | Editor(30) | Low | true |
| economy:catalog:delete | Editor(30) | Low | true |
| economy:catalog:refund | SuperMod(20) | Low | true |
| economy:catalog:purchases:read | Moderator(10) | Low | true |
| economy:account:freeze | Moderator(10) | Low | true |
| economy:account:adjust | Moderator(10) | Low | true |
| economy:ledger:read | Moderator(10) | Low | true |
| economy:leaderboards:config:read | Moderator(10) | Low | true |
| economy:leaderboards:config:write | Editor(30) | Low | true |
| economy:leaderboards:config:delete | Editor(30) | Low | true |
| reward:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| reward:manage | Broadcaster(40) | Low | true |
| reward:sync | Broadcaster(40) | Low | true |
| reward:redemption:read | Moderator(10) | Low | true |
| reward:redemption:fulfill | Moderator(10) | Low | true |
| reward:redemption:refund | Moderator(10) | Low | true |
| analytics:read | Moderator(10) | Low | true |
| analytics:viewer:read | Moderator(10) | Low | true |
| setup:write | Broadcaster(40) | Low | false |
| integration:read | Moderator(10) | Low | true |
| integration:write | Editor(30) | Low | true |
| channelbot:connect | Broadcaster(40) | Low | false |
| channelbot:read | Broadcaster(40) | Low | true |
| channelbot:disconnect | Broadcaster(40) | Low | false |
| community:read | Moderator(10) | Low | true |
| community:trust:write | Moderator(10) | Low | true |
| dashboard:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| chat:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| chat:send | Moderator(10) | Low | true |
| feature:read | Moderator(10) | Low | true |
| feature:write | Editor(30) | Low | true |
| liveops:poll:read | Moderator(10) | Low | true |
| liveops:poll:manage | Editor(30) | Low | true |
| liveops:prediction:read | Moderator(10) | Low | true |
| liveops:prediction:manage | Editor(30) | Low | true |
| liveops:raid:start | Editor(30) | Low | true |
| liveops:ads:read | Moderator(10) | Low | true |
| liveops:ads:run | Editor(30) | Low | true |
| liveops:schedule:read | Moderator(10) | Low | true |
| liveops:schedule:write | Editor(30) | Low | true |
| liveops:marker:create | Moderator(10) | Low | true |
| liveops:clip:create | Moderator(10) | Low | true |
| moderation:chat:settings:read | Moderator(10) | Low | true |
| moderation:chat:settings:write | Moderator(10) | Low | true |
| moderation:shieldmode:read | Moderator(10) | Low | true |
| moderation:shieldmode:write | SuperMod(20) | Low | true |
| moderation:announce | Moderator(10) | Low | true |
| moderation:chatcolor:write | Editor(30) | Low | true |
| moderation:vip:write | Broadcaster(40) | Low | true |
| moderation:moderator:write | Broadcaster(40) | Critical | false |
| moderation:unbanrequest:read | Moderator(10) | Low | true |
| moderation:unbanrequest:resolve | SuperMod(20) | Low | true |
| moderation:blocklist:write | SuperMod(20) | Low | true |
| moderation:suspicioususer:write | SuperMod(20) | Low | true |
| channel:title:write | Editor(30) | Low | true |
| channel:game:write | Editor(30) | Low | true |
| channel:tags:write | Editor(30) | Low | true |
| channel:ccl:write | Editor(30) | Low | true |
| channel:language:write | Editor(30) | Low | true |
| channel:brandedcontent:write | Editor(30) | Low | true |
| channel:extensions:write | Editor(30) | Low | true |
| chat:whisper:send | Editor(30) | Low | true |
| music:remote:control | Moderator(10) | Low | true |
| music:library:write | Editor(30) | Low | true |

### ActionDefinitions — Community plane
`Plane = Community`; `Default = Floor = Everyone(0)`, `Tier = Low`, `Grant = true` unless noted.

| ActionKey | Floor | Tier | Grant |
|---|---|---|---|
| music:request:submit | Everyone(0) | Low | true |
| moderation:report:file | Everyone(0) | Low | true |
| economy:catalog:read | Everyone(0) | Low | true |
| economy:catalog:purchase | Everyone(0) | Low | true |
| economy:games:read | Everyone(0) | Low | true |
| economy:games:play | Everyone(0) | Low | true |
| economy:games:history:read | Everyone(0) | Low | true |
| economy:jars:read | Everyone(0) | Low | true |
| economy:jars:create | Everyone(0) | Low | true |
| economy:jars:membership:accept | Everyone(0) | Low | true |
| economy:jars:membership:revoke | Everyone(0) | Low | true |
| economy:jars:invite | Everyone(0) | Low | true |
| economy:jars:contribute | Everyone(0) | Low | true |
| economy:jars:withdraw | Everyone(0) | Low | true |
| economy:jars:history:read | Everyone(0) | Low | true |
| economy:leaderboards:read | Everyone(0) | Low | true |
| economy:leaderboards:opt-in | Everyone(0) | Low | true |
| economy:leaderboards:opt-out | Everyone(0) | Low | true |
| economy:account:read | Everyone(0) | Low | true |
| economy:consent:read | Everyone(0) | Low | true |
| economy:consent:write | Everyone(0) | Low | true |
| economy:consent:revoke | Everyone(0) | Low | true |
| economy:transfer:write | Everyone(0) | Low | true |
| economy:earning | Everyone(0) | Low | true |
| pronouns:read | Everyone(0) | Low | true |
| pronouns:self:write | Everyone(0) | Low | true |

### IamPermissions (C.1)
Plane-C platform keys; `Category` is `IamCategory{Tenant,Billing,Audit,Iam,FeatureFlag}`.

| Key | Category | IsSensitive |
|---|---|---|
| tenant:read | Tenant | false |
| tenant:access | Tenant | true |
| tenant:suspend | Tenant | true |
| iam:manage | Iam | true |
| iam:principal:create | Iam | true |
| audit:read | Audit | false |
| featureflag:write | FeatureFlag | true |
| billing:read | Billing | false |
| billing:refund | Billing | true |
| platform:analytics:read | Tenant | false |

The legacy alias `iam:audit:read` collapses to `audit:read` — a single key, not two.

### IamRoles (C.2, all `IsSystem = true`) + IamRolePermissions (C.3)
Least-privilege bundles; each row seeds the `IamRole` and its `IamRolePermission` join rows.

| Role | Bundled permission keys |
|---|---|
| platform-super-admin | tenant:read, tenant:access, tenant:suspend, iam:manage, iam:principal:create, audit:read, featureflag:write, billing:read, billing:refund, platform:analytics:read (ALL) |
| platform-support | tenant:read, tenant:access, audit:read, platform:analytics:read |
| platform-trust-safety | tenant:read, tenant:suspend, tenant:access, audit:read |
| platform-billing | billing:read, billing:refund |
| platform-iam-admin | iam:manage, iam:principal:create, audit:read |
| platform-analyst | tenant:read, platform:analytics:read |

`platform-super-admin` is the role the §7 bootstrap principal is assigned. Every other principal is provisioned via `IPlatformIamService.CreatePrincipalAsync` and assigned one of these system roles (or a custom role).

---

## 8. Dependencies (from the stack doc)

| Use | Package / API | Party |
|---|---|---|
| ORM, named query filters (soft-delete + tenant), `[VC:enum]`/`[VC:JSON]` converters | `Microsoft.EntityFrameworkCore` 10.0.9 (+ Sqlite/Npgsql via profile adapter) | 2nd / 3rd (Npgsql) |
| App JSON for `[VC:enum]`/`[VC:JSON]` columns + config DTOs | **Newtonsoft.Json** (per CLAUDE.md app-JSON rule) | — |
| Inbound request validation (`SetActionLevelRequest`, etc.) | in-box `.NET 10 AddValidation()` source generator | 1st |
| HTTP host, versioning, problem details, rate-limit | `Microsoft.AspNetCore.*` in-box + `Asp.Versioning.Mvc` 10.0.0 | 2nd |
| Auth (JWT resource-server → caller `userId`/principal) | `Microsoft.AspNetCore.Authentication.JwtBearer` + `Microsoft.IdentityModel.JsonWebTokens` 8.19.1 | 2nd |
| `IamPrincipal.EmailCipher` / `IamAuditLog.SourceIpCipher` (read; encrypt via vault) | in-box `System.Security.Cryptography` (AesGcm) behind the token-vault service; KEK via profile adapter (local-AES / Azure Key Vault) | 1st / 2nd |
| Domain events | in-box `IEventBus` (no MediatR) | 1st |
| `PermitGrant` expiry sweep | in-box `BackgroundService` + `PeriodicTimer`; SaaS multi-node guarded by `IRunOnceGuard` | 1st |
| `IamAuditLog`/`ModerationAuditLog` append-only `bigint` PK monotonic ordering | global identity (NOT `TenantSequences` — these are global tables, not tenant-scoped) | — |

**No new third-party dependency** is introduced by this subsystem.

---

## 9. Decisions (resolved)

1. **`PermissionLevel` is extended in place** to the 8-rung unified ladder (one ladder, no parallel `AuthLevel`), keeping the single style `ChatMessageHandler` already uses. The enum and its `ToLevelValue` extension are defined in §1.1; the resolver maps against this one ladder.
2. **Self-host Plane C is the `OwnerIsFullIamService` no-op adapter** (owner = full, audit no-op), per the model doc's "Self-hosted: N/A — Plane C collapses to owner = full." `IPlatformIamService` is always injected (DI selects the adapter by `DeploymentProfile.Mode`, §7) so controllers and `AdminController` carry no profile branching.
