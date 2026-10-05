# Interface Specification — Roles & Permissions Subsystem

**Status:** Implementable. Code from this directly.
**Sources (authoritative):** `2026-06-16-roles-and-permissions.md` (model), `2026-06-16-database-schema.md` (LOCKED schema — Domain B `ChannelMemberships`/`ChannelCommunityStandings`/`ActionDefinitions`/`ChannelActionOverrides`/`PermitGrants`; Domain C `Iam*`; O.9 `IamAuditLog`; O.8 `ModerationAuditLog`), `2026-06-16-stack-and-dependencies.md` (libs), `2026-06-16-decisions-resolved.md` (defaults).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/...")]`; surrogate PK `guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete global filter; Newtonsoft.Json for app JSON.

---

## 0. Scope, planes, and the load-bearing migration

This subsystem owns **three authorization planes** and the **two gates** that read them:

| Plane | Axis | Gates | Ladder |
|---|---|---|---|
| **A — Community standing** | chat badges (earned/granted) | chat-command + cosmetics (Gate-2 on `community` actions) | `Everyone(0) < Subscriber(2) < Vip(4) < Artist(6) < Moderator(10)` |
| **B — Channel management** | administer the channel (dashboard/HTTP API) | **Gate-1** (entry) + **Gate-2** (per-action) on `management` actions | `Moderator(10) < LeadModerator(20) < Editor(30) < Broadcaster(40)` |
| **C — Platform IAM** | NoMercy Labs operators + service accounts (SaaS only) | cross-tenant/privileged platform ops, audited | named permission bundles (least-privilege, default-deny) |

**One unified ordered ladder** spans planes A+B for the per-action gate: `Everyone(0) < Subscriber(2) < Vip(4) < Artist(6) < Moderator(10) < LeadModerator(20) < Editor(30) < Broadcaster(40)`. The numeric `LevelValue` is the only thing compared — and it is **internal**: it lives in the entities (`LevelValue` columns) and the resolver, never in a DTO, API response, chat reply, or UI. Every outward surface names the rung (`ManagementRole` / `CommunityStanding` names, PRODUCT-ALIGNMENT glossary); a user never sees a level number.

- **Gate-1** = pure entry, **entry ≠ permission** (decided 2026-07-04): any authenticated caller may resolve tenant context for a channel that **exists** — community participant and channel manager alike. Fails closed only on a malformed id or a nonexistent channel. It carries **no** role floor: requiring management ≥ Moderator here would 403 every community-plane participant (viewer/sub/VIP) before their Everyone-floored actions ever reached Gate-2. All authorization — community AND management floors — is Gate-2's job, which makes **universal Gate-2 coverage a hard invariant**: every tenant-scoped controller action carries `[RequireAction]` (or a documented exemption), enforced by a reflection test.
- **Gate-2** = per-action: "is the caller's resolved level ≥ this action's effective required level for this channel?" New with this epic; replaces the "almost everything is just `[Authorize]`" gap.
- **`!permit`** — individual grants. **Effective level = MAX(Twitch-badge role, bot-role grants, individual capability grants)**, with two guardrails: **no escalation above the grantor's own level** (applies to every grantor), and **default-deny** (only capabilities whose `ActionDefinition.IsGrantableViaPermit == true` may be granted). The **Broadcaster (channel owner) is fully trusted and MAY grant Critical-tier capabilities** — paternalistic caps on what the owner can delegate do not exist; the owner is the sole authority over their channel. Critical grants are **always to a named individual user, never raised on a whole role tier** (§0.2), and the floor-tier guards that block a dangerous capability from being applied to a low *role* tier still hold.

### 0.2 Dangerous capabilities are delegated to a named individual, never raised on a role tier
A Critical- or ToS-tier capability is delegated by **granting it to a specific user** (a `PermitGrant` of `GrantType=Capability` naming one `UserId`), **never** by lowering the action's required level on a whole `ManagementRole` tier via a `ChannelActionOverride`. The override mechanism may **raise** a floor for everyone but must **not** be used to drop a dangerous capability onto an entire tier. The canonical case: *"one of my mods runs my stream deck and I want them to start and stop the broadcast."* The decision is to **grant that one person** the capability (`!permit @thatuser obs:control:broadcast`, owner-issued — a Critical-tier, permit-grantable key), **not** to make every Editor able to start and stop the stream. A Critical action that is **not** permit-grantable (for example `code:script:author`) cannot be delegated per user at all; its floor and a raise via `ChannelActionOverride` are the only levers. This keeps the blast radius of a dangerous capability to the exact individual the Broadcaster trusts, while the role tiers stay at their safe defaults. `SetActionOverrideAsync` rejects an override that would set a Critical-tier action's level below its `FloorLevel` (`VALIDATION_FAILED`); per-user reach into Critical capabilities is **only** via the Broadcaster-issued `PermitGrant` path.

### 0.1 Migration note (do NOT silently "fix")
The original code used `string BroadcasterId` / `string userId` (`ITenantScoped.BroadcasterId : string`, `Permission.Id : int`, `ChannelAccessService` string args). The LOCKED schema (§1.1/§1.2) **widens `ITenantScoped.BroadcasterId` `string → Guid`** and makes all PKs `guid`. This spec targets the **locked `Guid` shape** for entities and for the six authorization services below. **Exception:** `IChannelAccessService` keeps `string` ids (the JWT `sub` and the tenant key in string form, parsed inside the impl) — its authoritative contract is `platform-conventions.md` §3.2. The generic `Permission` entity and `IPermissionService` string-permission methods are **replaced** by `ActionDefinitions` + `ChannelActionOverrides` + `PermitGrants` and the typed services below (schema §B note: *"Replaces the current generic `Permission`"*). `IPermissionService` (`Application/Identity/Services/IPermissionService.cs`, `Infrastructure/Identity/PermissionService.cs`, and the legacy `PermissionsController`) is **dead legacy**: no authorization path calls it, it is superseded by `IActionAuthorizationService` + `IPermitService`, and it is **deleted by S-RETIRE-LEGACY**. Do not grow it and do not call it.

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

### 1.1 Enums (`NomNomzBot.Domain/Identity/Enums/`; `[VC:enum]` text-stored)
`ManagementRole { Moderator, LeadModerator, Editor, Broadcaster }` (values 10/20/30/40 via `[Display]`/extension `ToLevel()`).
`CommunityStanding { Everyone, Subscriber, Vip, Artist, Moderator }` (0/2/4/6/10).
`AuthPlane { Community, Management }`.
`DangerTier { Critical, Tos, Low }`.
`MembershipSource { TwitchBadge, HelixEditors, BotGrant, Owner }`.
`StandingSource { ChatTags, EventSubBadge, HelixSeed }` (`HelixSeed` = a Subscriber/Vip standing seeded from a Twitch snapshot by `ReconcileTwitchStandingsAsync`, §3.5).
`PermitGrantType { Role, Capability }`.
`IamCategory { Tenant, Billing, Audit, Iam, FeatureFlag, Content }` (`Content` = platform content authoring/propagation, platform-admin.md §4).
`IamPrincipalType { Employee, ServiceAccount }`.
`IamOutcome { Allowed, Denied }`.
**As-built:** `Domain/Identity/Enums/PermissionLevel.cs` is the one unified ladder (one ladder, no parallel `AuthLevel`): `Everyone, Subscriber, Vip, Artist, Moderator, LeadModerator, Editor, Broadcaster`, with `int ToLevelValue(this PermissionLevel)` returning 0/2/4/6/10/20/30/40 (`AuthorizationLadder`). `ChatMessageHandler.HasPermission` and command/trigger floors read this same enum. The level-20 rung is named `LeadModerator` everywhere (enum, seed catalogue, DTOs).

---

## 2. Domain events

Events inherit the canonical `DomainEventBase` (platform-conventions §2.0 — an **abstract class** providing `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; do **NOT** redeclare these). Published via `IEventBus`, placed in `NomNomzBot.Domain/Identity/Events/AuthorizationEvents.cs` (namespace `NomNomzBot.Domain.Identity.Events`). They are **sealed classes** (never records) with init-only required props; the publisher sets the inherited `Guid BroadcasterId` (tenant-scoped events) or leaves it `Guid.Empty` (platform-level).

```csharp
public sealed class ManagementRoleChangedEvent : DomainEventBase  // B.1 upsert/soft-delete
{
    public required Guid TargetUserId { get; init; }
    public required ManagementRole? OldRole { get; init; }          // null = newly added
    public required ManagementRole? NewRole { get; init; }          // null = removed
    public required MembershipSource Source { get; init; }
    public required Guid? ChangedByUserId { get; init; }            // null = Helix/badge sync
}

public sealed class CommunityStandingChangedEvent : DomainEventBase // B.2 upsert
{
    public required Guid TargetUserId { get; init; }
    public required CommunityStanding OldStanding { get; init; }
    public required CommunityStanding NewStanding { get; init; }
    public required StandingSource Source { get; init; }
}

public sealed class ActionLevelOverriddenEvent : DomainEventBase   // B.4 set/reset
{
    public required Guid ActionDefinitionId { get; init; }
    public required string ActionKey { get; init; }
    public required int? OldLevel { get; init; }                    // null = was default
    public required int NewEffectiveLevel { get; init; }            // floor-clamped result
    public required Guid SetByUserId { get; init; }
}

public sealed class PermitGrantedEvent : DomainEventBase          // B.5 create
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required PermitGrantType GrantType { get; init; }
    public required ManagementRole? GrantedRole { get; init; }
    public required string? CapabilityActionKey { get; init; }
    public required Guid GrantedByUserId { get; init; }
    public required DateTime? ExpiresAt { get; init; }
}

public sealed class PermitRevokedEvent : DomainEventBase          // B.5 revoke/expire
{
    public required Guid GrantId { get; init; }
    public required Guid TargetUserId { get; init; }
    public required Guid? RevokedByUserId { get; init; }            // null = auto-expiry
    public required string Reason { get; init; }                   // "unpermit" | "expired"
}

public sealed class AuthorizationDeniedEvent : DomainEventBase    // Gate-1 or Gate-2 denial
{
    public required Guid CallerUserId { get; init; }
    public required string ActionKey { get; init; }                // "" for Gate-1 entry denial
    public required int RequiredLevel { get; init; }
    public required int CallerLevel { get; init; }
    public required string Gate { get; init; }                     // "gate1" | "gate2"
}

public sealed class IamAccessEvaluatedEvent : DomainEventBase     // Plane-C → also persists IamAuditLog
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

The six authorization contracts (`IRoleResolver`, `IActionAuthorizationService`, `IMembershipService`, `ICommunityStandingService`, `IPermitService`, `IPlatformIamService`) live in `NomNomzBot.Application/Contracts/Authorization/` (namespace `NomNomzBot.Application.Contracts.Authorization`); `IChannelAccessService` lives in `NomNomzBot.Application/Identity/Services/`. Implementations are in `NomNomzBot.Infrastructure/Identity/`. All read/write through `IApplicationDbContext` + `IUnitOfWork`; mutating methods publish the events in §2 and `await _uow.SaveChangesAsync(ct)` before returning.

### 3.1 `IChannelAccessService` — Gate-1 (EXTEND existing)
Existing interface kept; the Plane-C path (`IsPlatformPrincipal` / `IPlatformIamService`) replaces the `User.IsAdmin` check.

> **Single owner of the full surface:** `IChannelAccessService` is defined authoritatively in `platform-conventions.md` §3.2 (`Application/Identity/Services/IChannelAccessService.cs`), which carries **both** `CanResolveTenantAsync` *and* `ResolveOwnChannelAsync` (the owner-channel resolver the `TenantResolutionMiddleware`/`OBSRelayHub` IDOR fix needs). As-built, the ids stay `string` and `ResolveOwnChannelAsync` returns a bare `Guid` (`Guid.Empty` = no channel). Do not redeclare a narrower interface; the type has both methods.

```csharp
public interface IChannelAccessService
{
    // Gate-1 = pure entry (entry ≠ permission, §0): true iff userId parses as a Guid and channelId names an
    // existing, Active, non-soft-deleted channel. All floors are Gate-2's.
    Task<bool> CanResolveTenantAsync(string userId, string channelId, CancellationToken cancellationToken = default);

    // Owner-channel resolver — defined in platform-conventions.md §3.2; listed here so the surface is not truncated.
    Task<Guid> ResolveOwnChannelAsync(string userId, CancellationToken cancellationToken = default);
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
    // The canonical "can this user do this action" rule — backs BOTH chat-command gating and HTTP Gate-2.
    Task<Result<bool>> HasCapabilityAsync(Guid userId, Guid broadcasterId, string actionKey, CancellationToken cancellationToken = default);

    // True when a platform-wide network block (active or partial, never lifted) stands against the user. Not
    // tenant-scoped: a blocked actor is denied every Gate-2 action in every channel, whatever their level there.
    Task<Result<bool>> IsNetworkBlockedAsync(Guid userId, CancellationToken cancellationToken = default);
}
```

### 3.3 `IActionAuthorizationService` — Gate-2 + per-action config (REPLACES generic `IPermissionService` authz)
```csharp
public interface IActionAuthorizationService
{
    // Gate-2: effectiveRequired = clamp(override ?? default, floor, Broadcaster); allow iff resolved caller level ≥ it
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

### 3.4 `IMembershipService` — Plane-B ladder writes + Helix/badge sync
```csharp
public interface IMembershipService
{
    // Upsert a management-ladder membership (moderator/lead-moderator/editor/broadcaster). Recomputes LevelValue.
    // Guardrail: grantedByUserId may not grant a role above their own resolved level (FORBIDDEN). Emits ManagementRoleChangedEvent.
    Task<Result<ChannelMembershipDto>> SetManagementRoleAsync(Guid broadcasterId, Guid userId, ManagementRole role, MembershipSource source, Guid? grantedByUserId, CancellationToken cancellationToken = default);

    // Soft-delete a membership (demote/remove). Owner (Source=Owner) is non-removable (VALIDATION_FAILED). Emits ManagementRoleChangedEvent(NewRole=null).
    Task<Result> RemoveManagementRoleAsync(Guid broadcasterId, Guid userId, Guid? removedByUserId, CancellationToken cancellationToken = default);

    // Reconcile twitch_badge + helix_editors-sourced memberships from a freshly-fetched snapshot (idempotent upsert + prune of stale synced rows;
    // bot_grant/owner rows untouched). Sets LastSyncedAt. Emits ManagementRoleChangedEvent per delta.
    // `authoritativeSources` names which MembershipSource values this snapshot is authoritative for (only those synced rows are pruned).
    Task<Result> SyncManagementFromTwitchAsync(Guid broadcasterId, IReadOnlyList<TwitchManagementMember> snapshot, IReadOnlySet<MembershipSource> authoritativeSources, CancellationToken cancellationToken = default);

    // Paginated membership list for the dashboard roles screen.
    Task<Result<PagedList<ChannelMembershipDto>>> ListMembershipsAsync(Guid broadcasterId, int page, int pageSize, CancellationToken cancellationToken = default);
}
```

### 3.5 `ICommunityStandingService` — Plane-A writes (chat-tag / EventSub badge sourced)
```csharp
public interface ICommunityStandingService
{
    // Upsert a viewer's community standing from chat tags or an EventSub badge. Recomputes LevelValue.
    // Emits CommunityStandingChangedEvent only on change. Hot-path friendly (single upsert).
    Task<Result> UpsertStandingAsync(Guid broadcasterId, Guid userId, CommunityStanding standing, StandingSource source, string? subTier, CancellationToken cancellationToken = default);

    // Read a viewer's current standing (Everyone if none recorded).
    Task<Result<CommunityStanding>> GetStandingAsync(Guid broadcasterId, Guid userId, CancellationToken cancellationToken = default);

    // Reconcile a channel's Plane-A standings against a freshly-read Twitch subscriber + VIP snapshot (StandingSource.HelixSeed):
    // raises every reported sub/VIP; on a FULLY authoritative read downgrades a Helix-seeded sub/VIP who no longer appears.
    // Prune-safe (a partial read only raises) and it manages only Helix-seeded Subscriber/Vip rows — an Artist or Moderator standing is never clobbered.
    Task<Result> ReconcileTwitchStandingsAsync(Guid broadcasterId, CommunityStandingSnapshot snapshot, CancellationToken cancellationToken = default);
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

### 3.7 `IPlatformIamService` — Plane-C (ONE service on every profile)

There is **one** `PlatformIamService` (`Infrastructure/Identity/PlatformIamService.cs`), registered for every deployment. It branches on the deployment mode read once from `DeploymentContext` (exposed as `IsSaasDeploymentAsync`), never on a swapped adapter and never on a count of `IamPrincipal` rows: **self-host** (mode ≠ Saas) → `AuthorizePlatformAsync` returns allow with no audit (owner = full; the bootstrapped owner still holds a real principal); **SaaS** → default-deny, checks the principal's effective permissions, **always** appends the `IamAuditLog` row itself and emits `IamAccessEvaluatedEvent`. There is no separate self-host adapter class and no separate audit-writer sink.

```csharp
public interface IPlatformIamService
{
    // Plane-C authorization: does the principal hold this permission (optionally scoped to a tenant)?
    // SaaS: ALWAYS writes IamAuditLog (allowed|denied) and emits IamAccessEvaluatedEvent. Self-host: true, no audit.
    // targetResource is an optional free-form id of the acted-upon resource (e.g. the impersonated user), recorded on the audit row.
    Task<Result<bool>> AuthorizePlatformAsync(Guid principalId, string permissionKey, Guid? targetBroadcasterId, bool breakGlass, string? justification, CancellationToken cancellationToken = default, string? targetResource = null);

    // Resolve the IamPrincipal for an authenticated platform user (Users.IsPlatformPrincipal). Null if not a platform principal.
    Task<Result<IamPrincipalDto?>> ResolvePrincipalAsync(Guid userId, CancellationToken cancellationToken = default);

    // The deployment-shape fact the whole plane keys on (mode == Saas), exposed for the ASP.NET Plane-C policy handler:
    // a caller holding the platform-principal claim but NO principal row is allowed only on self-host (implicit-full);
    // on SaaS that is a fail-closed misconfiguration. The handler cannot probe via AuthorizePlatformAsync with a
    // sentinel id — on SaaS that would write an IamAuditLog row violating its FK to IamPrincipals.
    Task<bool> IsSaasDeploymentAsync(CancellationToken cancellationToken = default);

    // Provision a new IamPrincipal: an employee (over an existing User, which is flagged IsPlatformPrincipal) or a service
    // account (generates a key, stores its ServiceAccountKeyHash, returns it once). Assigns the requested RoleIds.
    // Requires the acting principal to hold iam:principal:create. Audited on SaaS.
    Task<Result<IamPrincipalDto>> CreatePrincipalAsync(Guid actingPrincipalId, CreatePrincipalRequest request, CancellationToken cancellationToken = default);

    // Assign a role to a principal, optionally tenant-scoped + time-boxed. Requires caller iam:manage. Audited on SaaS.
    Task<Result<IamRoleAssignmentDto>> AssignRoleAsync(Guid actingPrincipalId, Guid principalId, Guid roleId, Guid? scopeChannelId, DateTime? expiresAt, string? reason, CancellationToken cancellationToken = default);

    // Revoke an assignment (sets RevokedAt). Requires caller iam:manage.
    Task<Result> RevokeAssignmentAsync(Guid actingPrincipalId, Guid assignmentId, string? reason, CancellationToken cancellationToken = default);

    // Effective platform permission keys for a principal (union over active, non-expired, in-scope role assignments).
    Task<Result<IReadOnlyList<string>>> GetEffectivePermissionsAsync(Guid principalId, Guid? scopeChannelId, CancellationToken cancellationToken = default);

    // The role catalog with each role's permission bundle — the admin screen's role picker.
    Task<Result<IReadOnlyList<IamRoleDto>>> ListRolesAsync(CancellationToken cancellationToken = default);

    // Every principal with its ACTIVE assignments — the admin IAM screen's principal list.
    Task<Result<IReadOnlyList<IamPrincipalSummaryDto>>> ListPrincipalsAsync(CancellationToken cancellationToken = default);

    // Deactivate a principal AND clear the backing employee user's IsPlatformPrincipal marker (the demote).
    // A principal cannot deactivate itself (lockout guard). Requires caller iam:manage.
    Task<Result> DeactivatePrincipalAsync(Guid actingPrincipalId, Guid principalId, string? reason, CancellationToken cancellationToken = default);

    // Reactivate a principal and restore the employee user's IsPlatformPrincipal marker. Requires caller iam:manage.
    Task<Result> ReactivatePrincipalAsync(Guid actingPrincipalId, Guid principalId, CancellationToken cancellationToken = default);
}
```

---

## 4. DTOs / contracts

Records in `NomNomzBot.Application.Contracts.Authorization` (new domain folder). Inbound request records in the same namespace; controller-local DTOs follow the existing `PermissionsController` pattern only for tiny request bodies.

```csharp
// ── Resolution / read models ────────────────────────────────────────────────
// Role NAMES only on the wire (PRODUCT-ALIGNMENT glossary): the numeric LevelValue stays inside the resolver/entities.
// `EffectiveRung` is the unified-ladder rung name (the PermissionLevel enum: Everyone…Broadcaster) the comparison resolved to.
public sealed record ResolvedAccessDto(
    Guid UserId, Guid BroadcasterId, PermissionLevel EffectiveRung,
    CommunityStanding CommunityStanding,
    ManagementRole? ManagementRole,
    ManagementRole? PermitRole, IReadOnlyList<string> PermitCapabilities,
    string WinningSource);                                    // "community" | "management" | "permit"

public sealed record ChannelMembershipDto(
    Guid Id, Guid UserId, string? Username, ManagementRole Role,
    MembershipSource Source, Guid? GrantedByUserId, DateTime GrantedAt, DateTime? LastSyncedAt);

public sealed record ActionPermissionDto(
    Guid ActionDefinitionId, string ActionKey, AuthPlane Plane, string? Description,
    PermissionLevel DefaultRung, PermissionLevel FloorRung, DangerTier FloorTier, bool IsGrantableViaPermit,
    PermissionLevel? OverrideRung, PermissionLevel EffectiveRung); // EffectiveRung = clamp(override ?? default, floor, Broadcaster) — compared by LevelValue internally, surfaced as the rung name

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

Four controllers (`RolesController`, `ActionPermissionsController`, `PermitsController`, `PlatformIamController`). All `[ApiVersion("1.0")]`, inherit `BaseController`, `[Authorize]`, return `StatusResponseDto<T>`/`PaginatedResponse<T>` via `ResultResponse(...)`. Channel-scoped route templates are `{channelId}` **strings**; the wire form of an owned id is a ULID (see `platform-conventions.md` §5 "Wire id format").

**Role gate** — **Gate-1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). **Gate-2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the gate column before the service call (403 `FORBIDDEN` when the caller's resolved level is below the action's effective level). **Plane-C** rows (§5.4) = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)`; the ASP.NET `[Authorize(Policy="<key>")]` policy name **is** the permission key verbatim. The keys are seeded global `ActionDefinitions` (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.

### 5.1 `RolesController` — `[Route("api/v{version:apiVersion}/channels/{channelId}/roles")]`
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<PaginatedResponse<ChannelMembershipDto>>` | management / Moderator · `roles:read` |
| PUT | `/` | `SetManagementRoleRequest` (`{ userId, role }`) | `StatusResponseDto<ChannelMembershipDto>` | management / Broadcaster · `roles:manage` (Critical: managing mods/roles) |
| DELETE | `/{userId:guid}` | — | `StatusResponseDto<object>` | management / Broadcaster · `roles:manage` |
| GET | `/effective/me` | — | `StatusResponseDto<ResolvedAccessDto>` | (Gate-1 only — self-introspection; a pure viewer must be able to learn they hold no management role) |
| GET | `/effective/{userId:guid}` | — | `StatusResponseDto<ResolvedAccessDto>` | management / Moderator · `roles:read` |

### 5.2 `ActionPermissionsController` — `[Route("api/v{version:apiVersion}/channels/{channelId}/action-permissions")]`
(Replaces the generic `PermissionsController` grant/revoke surface; the old controller is removed once callers migrate.)
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<List<ActionPermissionDto>>` (the matrix) | management / Moderator · `roles:read` |
| PUT | `/{actionKey}` | `{ "level": int }` | `StatusResponseDto<int>` (effective level) | management / Broadcaster · `roles:manage` |
| DELETE | `/{actionKey}` | — | `StatusResponseDto<object>` | management / Broadcaster · `roles:manage` |

### 5.3 `PermitsController` — `[Route("api/v{version:apiVersion}/channels/{channelId}/permits")]`
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/` | — | `StatusResponseDto<List<PermitGrantDto>>` | management / Moderator · `roles:read` |
| POST | `/role` | `{ userId, role, expiresAt?, reason? }` | `StatusResponseDto<PermitGrantDto>` | management / Broadcaster · `permit:issue` (configurable default, lowerable to Editor) |
| POST | `/capability` | `{ userId, actionKey, expiresAt?, reason? }` | `StatusResponseDto<PermitGrantDto>` | management / Broadcaster · `permit:issue` |
| DELETE | `/{userId:guid}` | `?actionKeyOrRole=` (query) | `StatusResponseDto<object>` | management · `permit:issue` |

> Chat `!permit`/`!unpermit` are **not** HTTP — they enter via `ChatMessageHandler` → `IPermitService`, gated by `IActionAuthorizationService.AuthorizeActionAsync(..., "permit:issue")`. The HTTP `PermitsController` is the dashboard equivalent.

### 5.4 `PlatformIamController` — `[Route("api/v{version:apiVersion}/platform/iam")]` (Plane-C; every deployment — self-host owner = full)
`[PlatformPlane]` `[Authorize]` + Plane-C policy per action (policy name = `IamPermission.Key` verbatim, enforced by `PlatformIamAuthorizationHandler`, audited on SaaS). Not a channel route — no tenant resolution. Decided 2026-07-17: this is the FULL management surface the admin panel's IAM screen renders from.
| Verb | Path | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/roles` | — | `StatusResponseDto<List<IamRoleDto>>` | platform · `iam:manage` |
| GET | `/principals` | — | `StatusResponseDto<List<IamPrincipalSummaryDto>>` | platform · `iam:manage` |
| GET | `/principals/{principalId:guid}/permissions` | `?scopeChannelId=` | `StatusResponseDto<List<string>>` | platform · `iam:manage` |
| POST | `/principals` | `CreatePrincipalRequest` | `StatusResponseDto<IamPrincipalDto>` (service-account key returned ONCE) | platform · `iam:principal:create` |
| POST | `/principals/{principalId:guid}/deactivate` | `?reason=` | `StatusResponseDto<object>` | platform · `iam:manage` |
| POST | `/principals/{principalId:guid}/reactivate` | — | `StatusResponseDto<object>` | platform · `iam:manage` |
| POST | `/assignments` | `AssignIamRoleRequest` | `StatusResponseDto<IamRoleAssignmentDto>` | platform · `iam:manage` |
| DELETE | `/assignments/{assignmentId:guid}` | `?reason=` | `StatusResponseDto<object>` | platform · `iam:manage` |

Promote/demote wiring (decided 2026-07-17): creating an EMPLOYEE principal sets the backing `User.IsPlatformPrincipal = true` (the `admin` role claim mints on the next token refresh, ≤ the JWT expiry — no re-login); `deactivate` sets `IamPrincipal.IsActive = false` AND clears the employee's `User.IsPlatformPrincipal`; `reactivate` restores both. A principal cannot deactivate itself (`VALIDATION_FAILED`) — the lockout guard. Permission reads are manager-only (`iam:manage`; the earlier "(or self)" option is dropped — the screen is manager-facing). New DTOs: `IamRoleDto(Id, Name, Description, IsSystem, PermissionKeys)`, `IamPrincipalSummaryDto(Id, PrincipalType, UserId, Name, IsActive, ExpiresAt, ActiveAssignments: List<IamRoleAssignmentDto>)`, `AssignIamRoleRequest(PrincipalId, RoleId, ScopeChannelId?, ExpiresAt?, Reason?)`.

### 5.5 The privileged tenant routes moved to `PlatformAdminController`
The old plan to rewire `AdminController`'s `User.IsAdmin` gate is superseded: the privileged tenant/support/impersonation/limits/erasure surface is `PlatformAdminController` (`/api/v1/admin`, owned by `stream-admin.md` §3.2/§5), each route gated by `[Authorize(Policy = "<IamPermission.Key>")]`. Feature-flag administration is `FeatureFlagAdminController` (`platform-conventions.md` §5). `AdminController` keeps only the platform-bot and stats/health routes.

---

## 6. Pipeline actions

This subsystem ships **two** pipeline actions (the `!permit`/`!unpermit` flow as pipeline steps), in `NomNomzBot.Infrastructure/Identity/PipelineActions/`, implementing the **single live `ICommandAction`** (`NomNomzBot.Application.Abstractions.Pipeline`, `commands-pipelines.md` §3.13):

```csharp
public interface ICommandAction
{
    string ActionType { get; }                                   // the pipeline `type` string
    LocalizedText Category { get; }                              // palette group — a resource KEY (identity actions: "pipeline.category.identity")
    LocalizedText Description { get; }                           // builder description — a resource KEY, unique per action
    IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];   // typed config schema the step form renders
    bool ResolvesOwnTemplates => false;                          // true → the engine skips its template pass for this action
    Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action);
}
```

Config is read from the step's `ActionDefinition` (`action.GetString(...)`, `GetInt(...)`); there are no per-action config records. Actions are auto-discovered by the `AddImplementationsOf<ICommandAction>` scan — no manual DI line. Results are `ActionResult.Success(...)` / `ActionResult.Failure(...)`.

| Action class | `ActionType` | Config keys (`Fields`) | Behavior |
|---|---|---|---|
| `PermitAction` | `permit` | `target_variable`, `role_or_capability`, `duration_minutes` | Requires the **invoking user** (`ctx.TriggeredByUserId`) to hold `permit:issue` via `IRoleResolver.HasCapabilityAsync`, resolves the target user, then calls `IPermitService.GrantRoleAsync` or `GrantCapabilityAsync` (a token that names a `ManagementRole` is a role grant, anything else an action-key capability grant) with the invoker as grantor, so the no-escalation + `IsGrantableViaPermit` default-deny guardrails (§3.6) apply to the real caller. `duration_minutes` > 0 → `ExpiresAt`. Fail-closed: an unresolved invoker/target, a missing token, or a service failure returns `ActionResult.Failure`. |
| `UnpermitAction` | `unpermit` | `target_variable`, `role_or_capability` (optional) | Same invoker check, resolves the target, calls `IPermitService.RevokeAsync`. A blank `role_or_capability` revokes all active grants for the user. |

> Gate-2 for these actions is re-asserted **inside the action** (`PermitCommandSupport.AuthorizeInvokerAsync` → `HasCapabilityAsync("permit:issue")` on the invoking user), so they are safe when invoked from any pipeline, not only from the chat command whose own required level is also `permit:issue`.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`. Lifetimes: **scoped** (per-request, DbContext-bound). Most of these are bound by the `I<X>Service` single-implementation convention scan (`AddServicesByConvention`); pipeline actions and seeders are discovered by their own scans.

```csharp
// Authorization — three planes + two gates (all scoped)
//   by convention scan (AddServicesByConvention): IChannelAccessService, IActionAuthorizationService,
//   IMembershipService, ICommunityStandingService, IPermitService, IPlatformIamService
services.AddScoped<IRoleResolver, RoleResolver>();                          // explicit (not an I<X>Service name)

// Pipeline actions — no manual line: AddImplementationsOf<ICommandAction> discovers PermitAction / UnpermitAction

// Seeders — no manual line: AddImplementationsOf<ISeeder> (scoped) discovers ActionDefinitionSeeder (Order 5)
// and IamCatalogSeeder (Order 6); SeedRunner orders them and runs them in one transaction.
```

**One `IPlatformIamService` on every profile.** `PlatformIamService` is the single implementation (§3.7). It reads the deployment mode from `DeploymentContext`: self-host = owner-is-full allow with no audit, SaaS = default-deny + `IamAuditLog` + `IamAccessEvaluatedEvent`. There is **no** separate self-host adapter and no DI branch on the deployment mode; controllers and `AdminController` carry no profile branching. **Seeding:** `ActionDefinitions`, `IamPermissions`, `IamRoles`, `IamRolePermissions` are `[GLOBAL, seed]`, written by the two seeders above (idempotent upsert by natural key; the catalogue is authoritative and re-syncs existing rows).

**Bootstrap (first platform super-admin):** decided **at login**, not at startup. `AuthService` calls the pure `AdminBootstrap.ShouldPromote(...)` after the Twitch identity resolves: a user is promoted when their Twitch id equals the configured **`App:InitialAdminTwitchId`** (any deployment), or — on **self-host only** — when they are the **first account to onboard** and no platform principal exists yet (SaaS never auto-promotes; its admins are pre-provisioned staff). Promotion sets `User.IsPlatformPrincipal = true` and `PlatformOwnerPrincipalMinter` creates the `Employee` `IamPrincipal` in the seeded `platform-super-admin` role, plus a `system-bootstrap` service-account principal that is attributed as the acting principal for bootstrap-time IAM writes (so nothing is attributed to `Guid.Empty`). It is idempotent (an existing principal is never re-promoted) and is the **only** principal not created via `IPlatformIamService.CreatePrincipalAsync` (which requires an existing acting principal with `iam:principal:create`) — it breaks the chicken-and-egg so every subsequent principal is provisioned through the audited service path.

---

## 7.1 Seed catalogue (canonical reference data)

These are the `[GLOBAL, seed]` rows the seeders (`ActionDefinitionSeeder`, `IamCatalogSeeder`) write (idempotent upsert by natural key) for `ActionDefinitions` (B.3), `IamPermissions` (C.1), `IamRoles` (C.2), and `IamRolePermissions` (C.3). Without them Gate-2 fails closed (403) on every gated route. Columns follow the schema: an `ActionDefinition` is `ActionKey` · `Plane` (`AuthPlane{Community,Management}`) · `DefaultLevel` · `FloorLevel` · `FloorTier` (`DangerTier{Critical,Tos,Low}`) · `IsGrantableViaPermit`. The tables below are **generated from the seeder source** (`ActionDefinitionSeeder.cs`, `IamCatalogSeeder.cs`), not hand-kept — regenerate them, do not edit by hand. Level values: `Everyone(0)`, `Subscriber(2)`, `Vip(4)`, `Artist(6)`, `Moderator(10)`, `LeadModerator(20)`, `Editor(30)`, `Broadcaster(40)`.

**Rule:** unless a row shows an explicit `Default X, Floor Y`, `DefaultLevel = FloorLevel`. `DefaultLevel` is the **out-of-the-box** required level — the action's Twitch base role — so a lower-standing viewer (e.g. a VIP or Sub) gets **nothing extra by default**. `FloorLevel` is the **lowest a broadcaster may set** via `ChannelActionOverride`: the override is clamped to `[FloorLevel, Broadcaster(40)]`, so a broadcaster may **raise** an action as high as Broadcaster **or lower** it as far as its floor — never below `FloorLevel`, and Critical-tier rows are not lowerable at all. A floor sits **below** the default only where abusing the action **cannot cause irreversible or serious harm** — non-destructive reads and reversible, non-destructive writes — so the broadcaster can *choose* to open them to a trusted VIP/Sub. Destructive, irreversible, Twitch-mutating, currency, or role/IAM actions keep `Floor = Default` at Moderator+ (or Broadcaster/Critical) and can never be lowered to VIP. **Moderator default for bot-internal tooling is intentional (commit 609ae0fc2).** Reversible, auditable, non-Twitch-mutating automation tooling (commands, pipelines, event responses, timers, widgets, code scripts, feature toggles, and similar) defaults to Moderator so a moderator can build and adjust the bot on the channel they moderate; a broadcaster may still raise any of them via `ChannelActionOverride`. The per-spec §5 controller tables are the **source** of these rows and must stay in sync — a §5 cell whose action key is absent here is a seed bug.

### ActionDefinitions — Management plane
`Plane = Management`; `DefaultLevel = FloorLevel`, `Tier = Low`, and `Grant = true` unless noted. A `Default X, Floor Y` cell marks a **broadcaster-lowerable** action: it defaults to `X` (its base role) but the broadcaster may lower the requirement as far as `Y`. (220 keys.)

| ActionKey | Floor | Tier | Grant |
|---|---|---|---|
| commands:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| commands:write | Moderator(10) | Low | true |
| commands:builtin:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| commands:builtin:write | Moderator(10) | Low | true |
| pipelines:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| pipelines:write | Moderator(10) | Low | true |
| pipelines:validate | **Default Moderator(10), Floor Vip(4)** | Low | true |
| eventresponses:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| eventresponses:write | Moderator(10) | Low | true |
| chattriggers:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| chattriggers:write | Moderator(10) | Low | true |
| voicetriggers:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| voicetriggers:write | Moderator(10) | Low | true |
| chatpolls:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| chatpolls:write | **Default Moderator(10), Floor Vip(4)** | Low | true |
| timers:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| timers:write | Moderator(10) | Low | true |
| bundles:read | Moderator(10) | Low | true |
| bundles:export | Editor(30) | Low | true |
| bundles:import | Editor(30) | Low | true |
| bundles:publish | Broadcaster(40) | Low | true |
| sdk:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| roles:read | Moderator(10) | Low | true |
| roles:manage | Broadcaster(40) | Critical | false |
| permit:issue | **Default Broadcaster(40), Floor Editor(30)** | Low | true |
| code:script:author | Moderator(10) | Critical | false |
| discord:connection:read | Moderator(10) | Low | false |
| discord:connection:write | Broadcaster(40) | Low | false |
| discord:config:read | Moderator(10) | Low | false |
| discord:config:write | LeadModerator(20) | Low | false |
| discord:role:read | Moderator(10) | Low | false |
| discord:role:write | LeadModerator(20) | Low | false |
| discord:optin:write | LeadModerator(20) | Low | false |
| discord:dispatch:read | Moderator(10) | Low | false |
| channelbot:connect | Broadcaster(40) | Low | false |
| channelbot:read | Broadcaster(40) | Low | true |
| channelbot:disconnect | Broadcaster(40) | Low | false |
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
| moderation:automod:write | LeadModerator(20) | Low | true |
| moderation:filter:read | Moderator(10) | Low | true |
| moderation:filter:write | LeadModerator(20) | Low | true |
| moderation:nuke | LeadModerator(20) | Critical | false |
| moderation:nuke:read | LeadModerator(20) | Low | true |
| moderation:sharedban:read | LeadModerator(20) | Low | true |
| moderation:sharedban:write | LeadModerator(20) | Critical | false |
| moderation:escalation:read | Moderator(10) | Low | true |
| moderation:escalation:write | LeadModerator(20) | Low | true |
| moderation:report:read | Moderator(10) | Low | true |
| moderation:report:triage | LeadModerator(20) | Low | true |
| moderation:evidence:build | Moderator(10) | Low | true |
| moderation:usercontext:read | Moderator(10) | Low | true |
| moderation:chat:settings:read | Moderator(10) | Low | true |
| moderation:chat:settings:write | Moderator(10) | Low | true |
| moderation:shieldmode:read | Moderator(10) | Low | true |
| moderation:shieldmode:write | LeadModerator(20) | Low | true |
| chat:announce | Moderator(10) | Low | true |
| chat:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| chat:send | Moderator(10) | Low | true |
| moderation:shoutout | Moderator(10) | Low | true |
| moderation:chatcolor:write | Editor(30) | Low | true |
| moderation:vip | Broadcaster(40) | Low | true |
| moderation:moderator:write | Broadcaster(40) | Critical | false |
| moderation:unbanrequest:read | Moderator(10) | Low | true |
| moderation:unbanrequest:resolve | LeadModerator(20) | Low | true |
| moderation:blocklist:write | LeadModerator(20) | Low | true |
| moderation:suspicioususer:write | LeadModerator(20) | Low | true |
| tts:config:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| tts:config:write | Moderator(10) | Low | true |
| tts:voice:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| tts:voice:test | Moderator(10) | Low | true |
| tts:uservoice:write | Moderator(10) | Low | true |
| tts:queue:review | Moderator(10) | Low | true |
| tts:playback:control | Moderator(10) | Low | true |
| eventsub:read | Moderator(10) | Low | true |
| eventsub:subscribe | Editor(30) | Low | true |
| eventsub:unsubscribe | Editor(30) | Low | true |
| twitch:diagnostics:read | Moderator(10) | Low | true |
| eventstore:journal:read | Broadcaster(40) | Low | true |
| eventstore:projection:read | Moderator(10) | Low | true |
| eventstore:projection:rebuild | Broadcaster(40) | Low | true |
| eventstore:replay:write | Broadcaster(40) | Low | true |
| eventstore:replay:republish | Broadcaster(40) | Low | true |
| eventstore:export | Broadcaster(40) | Low | false |
| eventstore:import | Broadcaster(40) | Critical | false |
| eventstore:import:legacy | Broadcaster(40) | Critical | false |
| music:config:write | Editor(30) | Low | true |
| music:config:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| music:queue:moderate | Moderator(10) | Low | true |
| music:token:read | Broadcaster(40) | Low | true |
| music:token:rotate | Broadcaster(40) | Critical | false |
| music:remote:control | Moderator(10) | Low | true |
| music:library:write | Moderator(10) | Low | true |
| music:control:write | Moderator(10) | Critical | false |
| stream:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| stream:preset:write | Editor(30) | Low | true |
| stream:schedule:write | Editor(30) | Low | true |
| channel:title:write | Editor(30) | Low | true |
| channel:game:write | Editor(30) | Low | true |
| channel:tags:write | Editor(30) | Low | true |
| channel:ccl:write | Editor(30) | Low | true |
| channel:language:write | Editor(30) | Low | true |
| channel:brandedcontent:write | Editor(30) | Low | true |
| channel:extensions:write | Editor(30) | Low | true |
| chat:whisper:send | Editor(30) | Low | true |
| live-ops:polls:read | Moderator(10) | Low | true |
| live-ops:polls:write | Moderator(10) | Low | true |
| live-ops:predictions:read | Moderator(10) | Low | true |
| live-ops:predictions:write | Editor(30) | Low | true |
| live-ops:raids:read | Moderator(10) | Low | true |
| live-ops:raids:write | Editor(30) | Low | true |
| live-ops:ads:read | Moderator(10) | Low | true |
| live-ops:ads:write | Editor(30) | Low | true |
| live-ops:schedule:read | Moderator(10) | Low | true |
| live-ops:schedule:write | Editor(30) | Low | true |
| live-ops:marker:create | Moderator(10) | Low | true |
| live-ops:clips:write | Moderator(10) | Low | true |
| automation:tokens:read | Editor(30) | Low | true |
| automation:tokens:write | Broadcaster(40) | Critical | false |
| obs:config:read | Broadcaster(40) | Low | true |
| obs:config:write | Broadcaster(40) | Critical | false |
| obs:control | Moderator(10) | Low | true |
| obs:control:broadcast | Broadcaster(40) | Critical | true |
| vts:config:read | Moderator(10) | Low | true |
| vts:config:write | Broadcaster(40) | Low | true |
| vts:control | Moderator(10) | Low | true |
| webhooks:inbound:read | Moderator(10) | Low | true |
| webhooks:inbound:write | Editor(30) | Low | true |
| webhooks:outbound:read | Moderator(10) | Low | true |
| webhooks:outbound:write | Editor(30) | Low | true |
| widget:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| widget:write | Moderator(10) | Low | true |
| widget:compile | Moderator(10) | Low | true |
| widget:version:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| widget:rollback | Moderator(10) | Low | true |
| widget:install | Moderator(10) | Low | true |
| integration:read | Moderator(10) | Low | true |
| integration:write | Editor(30) | Low | true |
| community:read | Moderator(10) | Low | true |
| community:trust:write | Moderator(10) | Low | true |
| dashboard:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| notifications:dismiss | Moderator(10) | Low | true |
| trust:policy:read | Moderator(10) | Low | true |
| trust:policy:manage | Broadcaster(40) | Low | true |
| moderation:automod:twitch:read | Moderator(10) | Low | true |
| moderation:automod:twitch:manage | Broadcaster(40) | Low | true |
| spam:policy:read | Moderator(10) | Low | true |
| spam:policy:manage | Broadcaster(40) | Low | true |
| spam:detections:read | Moderator(10) | Low | true |
| spam:detections:manage | Moderator(10) | Low | true |
| dashboard:replay | Moderator(10) | Low | true |
| setup:write | Broadcaster(40) | Low | false |
| feature:read | Moderator(10) | Low | true |
| feature:write | Moderator(10) | Low | true |
| analytics:read | Moderator(10) | Low | true |
| analytics:viewer:read | Moderator(10) | Low | true |
| economy:config:read | Moderator(10) | Low | true |
| economy:config:write | Editor(30) | Low | true |
| economy:earning-rules:read | Moderator(10) | Low | true |
| economy:earning-rules:write | Editor(30) | Low | true |
| economy:earning-rules:delete | Editor(30) | Low | true |
| economy:accounts:read | Moderator(10) | Low | true |
| economy:games:write | Broadcaster(40) | Low | true |
| games:session:read | Moderator(10) | Low | true |
| games:session:start | Moderator(10) | Low | true |
| games:session:cancel | Moderator(10) | Low | true |
| economy:catalog:create | Editor(30) | Low | true |
| economy:catalog:update | Editor(30) | Low | true |
| economy:catalog:delete | Editor(30) | Low | true |
| economy:catalog:refund | LeadModerator(20) | Low | true |
| economy:catalog:purchases:read | Moderator(10) | Low | true |
| economy:account:freeze | Moderator(10) | Low | true |
| economy:account:adjust | Moderator(10) | Low | true |
| economy:ledger:read | Moderator(10) | Low | true |
| economy:leaderboards:config:read | Moderator(10) | Low | true |
| economy:leaderboards:config:write | Editor(30) | Low | true |
| economy:leaderboards:config:delete | Editor(30) | Low | true |
| economy:jars:update | Editor(30) | Low | true |
| economy:jars:delete | Editor(30) | Low | true |
| billing:read | Broadcaster(40) | Low | true |
| billing:manage | Broadcaster(40) | Low | true |
| federation:optin:read | LeadModerator(20) | Low | true |
| federation:optin:write | LeadModerator(20) | Low | true |
| federation:optin:delete | LeadModerator(20) | Low | true |
| reward:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| reward:manage | Broadcaster(40) | Low | true |
| reward:sync | Broadcaster(40) | Low | true |
| reward:redemption:read | Moderator(10) | Low | true |
| reward:redemption:fulfill | Moderator(10) | Low | true |
| reward:redemption:refund | Moderator(10) | Low | true |
| quotes:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| quotes:write | **Default Moderator(10), Floor Vip(4)** | Low | true |
| quotes:delete | Moderator(10) | Low | true |
| picklists:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| picklists:write | **Default Moderator(10), Floor Vip(4)** | Low | true |
| picklists:delete | Moderator(10) | Low | true |
| giveaways:read | Moderator(10) | Low | true |
| giveaways:write | Moderator(10) | Low | true |
| giveaways:codes:write | Broadcaster(40) | Low | true |
| customdata:read | Moderator(10) | Low | true |
| customdata:write | Moderator(10) | Low | true |
| viewerdata:read | Moderator(10) | Low | true |
| viewerdata:write | Moderator(10) | Low | true |
| engagement:read | Moderator(10) | Low | true |
| engagement:write | Moderator(10) | Low | true |
| media:read | Moderator(10) | Low | true |
| media:moderate | Moderator(10) | Low | true |
| media:write | Moderator(10) | Low | true |
| supporters:read | Moderator(10) | Low | true |
| supporters:config:write | Broadcaster(40) | Critical | false |
| sounds:read | **Default Moderator(10), Floor Vip(4)** | Low | true |
| sounds:write | Moderator(10) | Low | true |

### ActionDefinitions — Community plane
`Plane = Community`; `Default = Floor = Everyone(0)`, `Tier = Low`, `Grant = true` unless noted (`economy:account:read` floors at Moderator: a participant reads their own wallet via the self-bound `/accounts/me`, so the keyed route is a Moderator action). (25 keys.)

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
| economy:account:read | Moderator(10) | Low | true |
| economy:consent:read | Everyone(0) | Low | true |
| economy:consent:write | Everyone(0) | Low | true |
| economy:consent:revoke | Everyone(0) | Low | true |
| economy:transfer:write | Everyone(0) | Low | true |
| economy:earning | Everyone(0) | Low | true |
| pronouns:self:write | Everyone(0) | Low | true |

### IamPermissions (C.1)
Plane-C platform keys; `Category` is `IamCategory{Tenant,Billing,Audit,Iam,FeatureFlag,Content}`. (27 keys.)

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
| billing:write | Billing | true |
| billing:refund | Billing | true |
| platform:analytics:read | Tenant | false |
| compliance:erasure | Tenant | true |
| gallery:review | Iam | true |
| system:ipc:manage | FeatureFlag | true |
| user:impersonate | Iam | true |
| content:read | Content | false |
| content:author | Content | true |
| content:publish | Content | true |
| content:publish:force | Content | true |
| tenant:quota:manage | Tenant | true |
| tenant:remigrate | Tenant | true |
| tenant:erase | Tenant | true |
| user:support:view | Iam | true |
| trust-safety:review | Tenant | true |
| network:block:manage | Tenant | true |
| platform:bot:manage | Iam | true |
| platform:defaults:manage | Content | true |

The legacy alias `iam:audit:read` collapses to `audit:read` — a single key, not two.

### IamRoles (C.2, all `IsSystem = true`) + IamRolePermissions (C.3)
Least-privilege bundles; each row seeds the `IamRole` and its `IamRolePermission` join rows.

| Role | Bundled permission keys |
|---|---|
| platform-super-admin | tenant:read, tenant:access, tenant:suspend, iam:manage, iam:principal:create, audit:read, featureflag:write, billing:read, billing:write, billing:refund, platform:analytics:read, compliance:erasure, gallery:review, system:ipc:manage, user:impersonate, content:read, content:author, content:publish, content:publish:force, tenant:quota:manage, tenant:remigrate, tenant:erase, user:support:view, trust-safety:review, platform:defaults:manage |
| platform-content-author | content:read, content:author, content:publish |
| platform-support | tenant:read, tenant:access, audit:read, platform:analytics:read, user:support:view |
| platform-trust-safety | tenant:read, tenant:suspend, tenant:access, audit:read, gallery:review, compliance:erasure, trust-safety:review |
| platform-network-block | network:block:manage |
| platform-bot-admin | platform:bot:manage |
| platform-billing | billing:read, billing:write, billing:refund |
| platform-iam-admin | iam:manage, iam:principal:create, audit:read |
| platform-analyst | tenant:read, platform:analytics:read |

`platform-super-admin` is the role the §7 bootstrap principal is assigned. It is **not** "all keys": `network:block:manage` and `platform:bot:manage` are deliberately excluded and each has its own narrow role (`platform-network-block`, `platform-bot-admin`), so a dangerous capability reaches a NAMED operator, never an entire team. `user:impersonate` is in `platform-super-admin` only — deliberately not bundled into `platform-support`. Every other principal is provisioned via `IPlatformIamService.CreatePrincipalAsync` and assigned one of these system roles (or a custom role).

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

1. **`PermissionLevel` is the one 8-rung unified ladder** (no parallel `AuthLevel`), the single style `ChatMessageHandler` uses. The enum and its `ToLevelValue` extension are described in §1.1; the resolver maps against this one ladder.
2. **Self-host Plane-C collapses to owner = full inside the one `PlatformIamService`** (allow, no audit), per the model doc's "Self-hosted: N/A — Plane-C collapses to owner = full." There is a single implementation on every profile (§3.7, §7) that reads the deployment mode from `DeploymentContext`, so controllers and `AdminController` carry no profile branching and no adapter swap exists.
