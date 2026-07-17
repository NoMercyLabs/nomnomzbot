# Moderation — Interface Specification (implementable)

**Subsystem area:** bans, timeouts, automod settings, moderation action log, network-nuke batch + reversal, shared bans, viewer reports.

**Status:** directly-implementable. Owner codes from this. All signatures fully typed. Namespace is `NomNomzBot.*` in every `.cs` file; folders/products are `NomNomzBot.*`.

**Grounding:**
- Locked schema — Domain J (Moderation), tables J.1–J.11; cross-cut O.8 `ModerationAuditLog`. `docs/design/2026-06-16-database-schema.md`.
- Design — `docs/design/2026-06-16-moderation.md` (unified queue, network-nuke split-by-safety, shared-chat ban propagation, evidence-packet-not-mass-report).
- Stack — `docs/design/2026-06-16-stack-and-dependencies.md` (EF Core 10, hand-rolled Helix, `Microsoft.Extensions.Http.Resilience`, profile adapters).
- Decisions — `docs/design/2026-06-16-decisions-pending-confirmation.md` (binding here: federation is feature-gated; the profile-adapter posture is the design).

### Binding conventions applied here
- .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (no `.Result`/`.Wait`).
- `Result<T>` (`NomNomzBot.Application.Common.Models`) over exceptions/null. `Result` for void-success.
- Repository + `IUnitOfWork` (`NomNomzBot.Application.Contracts.Persistence`) — no raw `DbContext` in services/controllers.
- DI via typed interfaces; **no MediatR, no Roslyn**.
- Responses: `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/...")]`.
- App JSON: Newtonsoft.Json for `[VC:JSON]` EF converters (per schema §1.4). Inbound request DTOs ride the host's System.Text.Json (existing controller convention) — converters are an EF persistence concern only.
- Surrogate PKs = `Guid` via `Guid.CreateVersion7()`; append-only journals use `bigint` identity (J.1, J.2, J.3, J.4, J.5, J.8, O.8 are `bigint PK`; J.2a, J.7, J.8a, J.9, J.9a, J.10, J.11, J.12 are `guid PK`).
- Twitch ids are indexed attribute columns, never keys. Tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`).
- Soft-delete (`IsDeleted`/`DeletedAt`) global filter on `[soft-delete]` tables; append-only tables carry `CreatedAt` only.

> **Pre-existing-code reconciliation (binding for the owner).** The live `IModerationService`, `ModerationDtos`, `ModerationController` use `string broadcasterId`/`string channelId`, `int ruleId`, and a free-form `Configuration` key/value bag (`shield.mode`, `blocked-terms`). This spec is the **locked-schema rebuild target**: `BroadcasterId` widens `string`→`Guid` (schema §1.1, owner decision #1), rule ids become the first-class `ChatFilters.Id (bigint)` / `AutoModConfigs.Id (guid)`, and the `Configuration`-bag automod/shield/blocked-terms storage is replaced by `AutoModConfigs` (J.7) + `ChatFilters` (J.6). **Extend the existing `IModerationService`** (same file, same name) to the surface below — do not create a parallel `IModerationServiceV2`. The existing four-built-in-rule `AutomodConfigDto` shape is superseded by `AutoModConfigDto` (J.7-shaped) below; migrate callers. New capabilities (queue, nuke, shared-bans, reports, notes, filters, trust) land as **sibling interfaces** in the same `NomNomzBot.Application.Services.Moderation` namespace, each one responsibility.

---

## 1. Entities (locked-schema, this subsystem owns)

All defined in `docs/design/2026-06-16-database-schema.md` Domain J (+ O.8). Listed here by name + the key fields a coder needs; **do not redefine columns** — the schema is authoritative. EF entity classes live in `NomNomzBot.Domain/Entities/Moderation/`; configs in `NomNomzBot.Infrastructure/Persistence/Configurations/Moderation/`.

| # | Entity | PK | Kind | Key fields (from schema) |
|---|--------|----|------|--------------------------|
| J.1 | `ModerationQueueItem` | `Id bigint` | `[soft-delete]` | `BroadcasterId guid`; `Source {automod\|viewer_report\|bot_flag}`; `Status {pending\|approved\|denied\|actioned\|expired}`; `TargetUserId guid`; `TargetTwitchUserId string(50)` **[PII-hash]**; `TargetUsernameSnapshot` **[PII-scrub]**; `ChatMessageId bigint?`; `MessageContentSnapshot text?` **[PII-scrub]**; `ReportedByUserId guid?`; `Reason string(500)?`; `AutoModCategory string(50)?`; `ResolvedByUserId guid?`; `ResolvedAt?`; `ResolutionAction string(20)?`; `ExpiresAt?`. |
| J.2 | `ModerationAction` | `Id bigint` | `[APPEND-ONLY]` | `BroadcasterId guid`; `ActionType {ban\|unban\|timeout\|untimeout\|delete_message\|warn\|nuke}`; `TargetUserId guid`; `TargetTwitchUserId` **[PII-hash]**; `TargetUsernameSnapshot` **[PII-scrub]**; `ActorUserId guid`; `ActorKind {human\|bot\|automod}`; `Reason string(500)?` **[PII-scrub]**; `DurationSeconds int?`; `ChatMessageId bigint?`; `QueueItemId bigint?`; `IsReverted bool`; `RevertedByActionId bigint?`; `Origin {local\|shared_chat\|network_nuke\|federation}`; `OriginChannelId guid?`; `NetworkNukeBatchId guid?`; `TwitchActionId string(100)?`. |

> **Origin provenance:** inbound federated shared bans from another NomNomzBot instance persist `Origin=federation`; Twitch native shared-chat bans persist `Origin=shared_chat`.
| J.2a | `NetworkNukeBatch` | `Id guid` | `[soft-delete]` | `OriginBroadcasterId guid`; `InitiatedByUserId guid?`; `MatchTerm string(500)?` **[PII-scrub]**; `TargetUserId guid?`; `TargetTwitchUserId string(50)?` **[PII-hash]**; `ChannelCount int`; `Status {active\|reverted\|partial}`; `RevertedByUserId guid?`; `RevertedAt?`. |
| J.3 | `UserNote` | `Id bigint` | `[soft-delete]` | `BroadcasterId guid`; `SubjectUserId guid`; `SubjectTwitchUserId` **[PII-hash]**; `AuthorUserId guid?`; `Content string(2000)` **[PII-scrub]**; `Pinned bool`. |
| J.4 | `UserModerationHistory` | `Id bigint` | projection (rebuildable) | `BroadcasterId guid`; `SubjectUserId guid`; `SubjectTwitchUserId` **[PII-hash]**; `TimeoutCount int`; `BanCount int`; `WarningCount int`; `MessagesDeletedCount int`; `LastActionAt?`; `LastActionType string(20)?`; `FirstSeenAt?`. |
| J.5 | `UserTrustScore` | `Id bigint` | projection (rebuildable) | `BroadcasterId guid`; `SubjectUserId guid`; `SubjectTwitchUserId` **[PII-hash]**; `TrustScore decimal(8,4)`; `HeatScore decimal(8,4)`; `LastHeatEventAt?`; `ComputedAt`. **Unique** `(BroadcasterId, SubjectUserId)`. |
| J.6 | `ChatFilter` | `Id bigint` | `[soft-delete]` | `BroadcasterId guid`; `FilterType {regex\|blocklist\|link_policy}`; `Name string(100)`; `Pattern string(2000)?`; `Terms text?` **[VC:JSON]** `List<string>`; `LinkPolicyJson text?` **[VC:JSON]**; `Action {delete\|timeout\|hold\|flag}`; `TimeoutSeconds int?`; `ExemptMinRoleLevel int`; `IsEnabled bool`; `IsCaseSensitive bool`; `MatchCount bigint`. |
| J.7 | `AutoModConfig` | `Id guid` | mutable | `BroadcasterId guid` **Unique**; `IsEnabled bool`; `OverallLevel int`; `CategoryLevelsJson text` **[VC:JSON]** `Dictionary<string,int>`; `HeldMessageTimeoutSeconds int`; `BlockHyperlinks bool`; `RequireVerifiedAccount bool`; `RequireVerifiedEmail bool`; `AutoTimeoutOnHeat bool`; `HeatTimeoutThreshold decimal(8,4)?`; `BlockedTermsSyncedAt?`. |
| J.8 | `ViewerReport` | `Id bigint` | `[soft-delete]` | `BroadcasterId guid`; `QueueItemId bigint?`; `ReportedUserId guid`; `ReportedTwitchUserId` **[PII-hash]**; `ReporterUserId guid?`; `Reason string(500)` **[PII-scrub]**; `Status {open\|triaged\|dismissed\|escalated}`. |
| J.8a | `ViewerReportEvidence` | `Id guid` | join | `BroadcasterId guid`; `ViewerReportId bigint`; `ChatMessageId bigint`. **Unique** `(ViewerReportId, ChatMessageId)`. |
| J.9 | `SharedBanSettings` | `Id guid` | mutable | `BroadcasterId guid` **Unique**; `AcceptSharedChatBans bool`; `ShareOutgoingBans bool`. |
| J.9a | `SharedBanTrustedChannel` | `Id guid` | join | `BroadcasterId guid` (trusting); `TrustedChannelId guid`; `AddedByUserId guid?`. **Unique** `(BroadcasterId, TrustedChannelId)`. |
| J.10 | `ModerationEscalationPolicy` | `Id guid` | `[soft-delete]` | `BroadcasterId guid` **Unique**; `IsEnabled bool`; `LadderJson text` **[VC:JSON]** `List<EscalationLadderStep>`; `OffenseWindowHours int` (default 168); `CountAutoModViolations bool` (default false); `ConfigSchemaVersion int`. |
| J.11 | `ModerationEscalationState` | `Id guid` | mutable | `BroadcasterId guid`; `SubjectUserId guid`; `SubjectTwitchUserId` **[PII-hash]**; `OffenseCount int`; `WindowStartedAt`; `LastOffenseAt`. **Unique** `(BroadcasterId, SubjectUserId)`. |
| J.12 | `ChannelModerationStanding` | `Id guid` | mutable | `BroadcasterId guid`; `Provider string(20)` (platform key: `twitch`\|`youtube`\|`kick`); `UserId string(64)` (that platform's user id); `Standing {muted\|shadowbanned\|blacklisted}` — an **absent row means normal** (there is no stored "none"); `Reason string(500)?`; `CreatedByUserId guid?` (acting operator). **Unique** `(BroadcasterId, Provider, UserId)`; **Index** `(BroadcasterId, Standing)`. |
| O.8 | `ModerationAuditLog` | `Id bigint` | `[APPEND-ONLY]` | `BroadcasterId guid?`; `ModerationActionId bigint?`; `ActorUserId guid?`; `ActorIamPrincipalId guid?` (staff cross-tenant); `EventType {action_taken\|action_reverted\|queue_resolved\|cross_tenant_access}`; `Justification string(500)?`; `MetadataJson text?` **[VC:JSON]**. |

> **J.12 axis note.** `ChannelModerationStanding` is the **negative, bot-side** standing axis — deliberately distinct from `ChannelCommunityStanding` (positive, badge-sourced, overwritten by chat tags) and from Twitch-native ban/timeout (which stay Helix-enforced and are **not** mirrored here). It keys on the **platform identity** (`Provider` + that platform's `UserId`), not the surrogate `Users.Id`, so each of a human's platform identities carries its own standing. Rows carry `CreatedAt`/`UpdatedAt`; clearing a standing **deletes the row** (no soft-delete — absence is the "normal" state). In the schema doc it sits beside its per-user Domain-J siblings (J.3–J.5).

**Cross-subsystem references (owned elsewhere — referenced, not redefined):** `Channels` (A.2, tenant root), `Users` (A.1), `ChatMessages` (Content domain), `ChannelMemberships` (B.1, management ladder — gate source), `ActionDefinitions` (B.3, floor/permit catalog), `ChannelFederationOptIns` (D.3, cross-instance shared-ban leg), `FederationPeers` (D.1).

---

## 2. Domain events

Namespace `NomNomzBot.Domain.Events`. **New events are `sealed record` deriving the canonical `DomainEventBase`** (`platform-conventions.md` §2.0 — provides `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt`, and implements `IDomainEvent`). Events **inherit** `EventId` / `BroadcasterId` / `OccurredAt` from the base and **must NOT redeclare them** — they add only their own payload fields, and the publishing service sets the inherited `BroadcasterId` to the owning channel (never `Guid.Empty` — every moderation event is tenant-scoped). The base is required because both `IEventHandler<in TEvent>` and `IEventBus.PublishAsync<TEvent>`/`PublishFireAndForget<TEvent>` are constrained `where TEvent : class, IDomainEvent`, so the §3 services and §7 handlers can only carry events that implement it. (The record `DomainEvent` — used by `IHasDomainEvents` aggregate collections — does **not** implement `IDomainEvent` and is a different type; do not derive it here.) The legacy `UserBannedEvent`/`UserTimedOutEvent`/`UserUnbannedEvent` are **kept and still emitted** for SignalR fan-out (`BanBroadcastHandlers`); the new records below are the canonical surrogate-`Guid` persistence/audit events. Publish both during the rebuild window; the dashboard handler reads the legacy ones.

> Tenant key on every new event is the **inherited** `Guid BroadcasterId` (matches widened `ITenantScoped`) — set by the publisher, never redeclared in the record header. An event that references a *different* channel (shared-ban / network-nuke origin) carries that explicitly as `OriginBroadcasterId`, distinct from the inherited tenant key. Twitch ids ride alongside as `string` where a handler needs them for Helix.

```csharp
namespace NomNomzBot.Domain.Events;

using NomNomzBot.Domain.Enums;

/// <summary>A mod action was applied (local, shared-chat, nuke fan-out, or federation). One per ModerationAction row.</summary>
public sealed record ModerationActionAppliedEvent(
    long ActionId,
    ModerationActionType ActionType,
    Guid TargetUserId,
    string? TargetTwitchUserId,
    Guid ActorUserId,
    ModerationActorKind ActorKind,
    ModerationActionOrigin Origin,
    Guid? NetworkNukeBatchId,
    int? DurationSeconds,
    string? Reason
) : DomainEventBase;

/// <summary>A prior mod action was reverted (unban/untimeout, or un-nuke unit). Carries the reversing action id.</summary>
public sealed record ModerationActionRevertedEvent(
    long RevertedActionId,
    long RevertingActionId,
    ModerationActionType ActionType,
    Guid TargetUserId,
    Guid ActorUserId,
    Guid? NetworkNukeBatchId
) : DomainEventBase;

/// <summary>An item entered the unified queue (automod hold / viewer report / bot flag).</summary>
public sealed record ModerationQueueItemEnqueuedEvent(
    long QueueItemId,
    ModerationQueueSource Source,
    Guid TargetUserId,
    string? AutoModCategory,
    long? ChatMessageId
) : DomainEventBase;

/// <summary>A queue item was resolved (approve/deny/timeout/ban inline).</summary>
public sealed record ModerationQueueItemResolvedEvent(
    long QueueItemId,
    ModerationQueueStatus Status,
    string? ResolutionAction,
    Guid ResolvedByUserId
) : DomainEventBase;

/// <summary>A network-nuke batch finished fanning out. ChannelCount = channels actually actioned.</summary>
public sealed record NetworkNukeExecutedEvent(
    Guid BatchId,
    Guid OriginBroadcasterId,
    Guid InitiatedByUserId,
    Guid? TargetUserId,
    string? TargetTwitchUserId,
    int ChannelCount
) : DomainEventBase;

/// <summary>A network-nuke batch was reversed. Status = reverted (all) or partial (some legs failed).</summary>
public sealed record NetworkNukeRevertedEvent(
    Guid BatchId,
    Guid OriginBroadcasterId,
    Guid RevertedByUserId,
    NetworkNukeStatus Status,
    int RevertedChannelCount
) : DomainEventBase;

/// <summary>
/// Opt-in shareable event: a SuperMod ban on the origin channel during an active Shared Chat session.
/// Delivered cross-instance via the federation bus; partner accepts iff it opted in + trusts the origin.
/// </summary>
public sealed record SharedChatBanIssuedEvent(
    long OriginActionId,
    Guid OriginBroadcasterId,
    string OriginTwitchChannelId,
    Guid TargetUserId,
    string TargetTwitchUserId,
    Guid ActorUserId,
    string? Reason,
    string SharedChatSessionId
) : DomainEventBase;

/// <summary>A viewer report was filed; feeds the queue and (optionally) an evidence packet.</summary>
public sealed record ViewerReportFiledEvent(
    long ReportId,
    Guid ReportedUserId,
    Guid? ReporterUserId,
    int EvidenceCount
) : DomainEventBase;

/// <summary>A user's trust/heat score was recomputed past a threshold that may trigger auto-action.</summary>
public sealed record UserHeatThresholdCrossedEvent(
    Guid SubjectUserId,
    decimal HeatScore,
    decimal Threshold
) : DomainEventBase;

/// <summary>Chat conversation controls changed (slow / follower-only / subs-only / emote-only / unique-chat / non-mod delay).</summary>
public sealed record ChatSettingsUpdatedEvent(
    bool? SlowModeEnabled,
    int? SlowModeWaitSeconds,
    bool? FollowerModeEnabled,
    int? FollowerModeDurationMinutes,
    bool? SubscriberModeEnabled,
    bool? EmoteModeEnabled,
    bool? UniqueChatModeEnabled,
    int? NonModeratorChatDelaySeconds,
    Guid ActorUserId
) : DomainEventBase;

/// <summary>Shield Mode was toggled for the channel (emergency lockdown on/off).</summary>
public sealed record ShieldModeUpdatedEvent(
    bool IsActive,
    Guid ActorUserId,
    string ModeratorTwitchUserId
) : DomainEventBase;

/// <summary>A chat announcement was sent (color = blue/green/orange/purple/primary).</summary>
public sealed record ChatAnnouncementSentEvent(
    string Color,
    Guid ActorUserId
) : DomainEventBase;

/// <summary>A VIP grant/removal was applied via Helix. Granted=true on add, false on remove.</summary>
public sealed record ChannelVipChangedEvent(
    Guid TargetUserId,
    string TargetTwitchUserId,
    bool Granted,
    Guid ActorUserId
) : DomainEventBase;

/// <summary>A moderator was removed from the channel via Helix (mod-grant is owned by the roles subsystem; this is the Twitch-native demotion leg).</summary>
public sealed record ChannelModeratorRemovedEvent(
    Guid TargetUserId,
    string TargetTwitchUserId,
    Guid ActorUserId
) : DomainEventBase;

/// <summary>A Twitch unban request was resolved (approved/denied) by a moderator.</summary>
public sealed record UnbanRequestResolvedEvent(
    string UnbanRequestId,
    Guid TargetUserId,
    string TargetTwitchUserId,
    UnbanRequestStatus Status,
    Guid ResolvedByUserId,
    string? ResolutionText
) : DomainEventBase;

/// <summary>The broadcaster's personal block list changed (block/unblock — distinct from a channel ban or shared-ban).</summary>
public sealed record UserBlockListChangedEvent(
    string TargetTwitchUserId,
    bool Blocked,
    Guid ActorUserId
) : DomainEventBase;

/// <summary>A suspicious user's monitoring treatment was changed (none/active-monitoring/restricted).</summary>
public sealed record SuspiciousUserTreatmentUpdatedEvent(
    Guid TargetUserId,
    string TargetTwitchUserId,
    SuspiciousUserTreatment Treatment,
    Guid ActorUserId
) : DomainEventBase;
```

**New enums** (namespace `NomNomzBot.Domain.Enums`, each `[VC:enum]` ↔ schema text). The existing `ModerationActionType { Timeout, Ban, Delete, Warn }` is **extended** (do not create a second enum) to the full schema set:

```csharp
namespace NomNomzBot.Domain.Enums;

public enum ModerationActionType { Ban, Unban, Timeout, Untimeout, DeleteMessage, Warn, Nuke }
public enum ModerationActorKind { Human, Bot, AutoMod }
public enum ModerationActionOrigin { Local, SharedChat, NetworkNuke, Federation }
public enum ModerationQueueSource { AutoMod, ViewerReport, BotFlag }
public enum ModerationQueueStatus { Pending, Approved, Denied, Actioned, Expired }
public enum ChatFilterType { Regex, Blocklist, LinkPolicy }
public enum ChatFilterAction { Delete, Timeout, Hold, Flag, Escalate }
public enum ViewerReportStatus { Open, Triaged, Dismissed, Escalated }
public enum NetworkNukeStatus { Active, Reverted, Partial }
public enum UnbanRequestStatus { Pending, Approved, Denied, Acknowledged, Canceled }
public enum SuspiciousUserTreatment { NoTreatment, ActiveMonitoring, Restricted }
```

> **Helix token mapping.** `UnbanRequestStatus` maps to Twitch's `pending|approved|denied|acknowledged|canceled` and `SuspiciousUserTreatment` to `none|active_monitoring|restricted`; the `[VC:enum]` converters carry the explicit name maps (same pattern as `ModerationActionType`). These two enums back **Helix-relayed** state — moderation persists no row for them (Twitch is the system of record; see §3.9/§3.10).

> **Migration note for `ModerationActionType`:** the old members `{Timeout, Ban, Delete, Warn}` map to `{Timeout, Ban, DeleteMessage, Warn}`. The `[VC:enum]` converter serializes to the schema's snake/text tokens (`delete_message`, etc.), not the C# member name — supply an explicit name map in the converter.

> **`ChatFilterAction.Escalate`:** a `ChatFilter` (J.6) with `Action=Escalate` does **not** apply a fixed action — it defers to the per-channel escalation ladder, which decides warn/timeout/ban by the subject's running offense count (`IModerationEscalationService`, §3.11 below).

---

## 3. Service interfaces

All in `NomNomzBot.Application.Services.Moderation` (new folder) except the **extended** `IModerationService` which stays at its current path `NomNomzBot.Application.Services/IModerationService.cs`. Each interface = one responsibility. Implementations in `NomNomzBot.Infrastructure/Services/Moderation/`. All take `Guid broadcasterId` (widened) and `CancellationToken` last.

### 3.1 `IModerationService` — direct mod actions (EXTEND existing)

Replaces the `string`-keyed signatures with `Guid`; keeps method names. Each writes a `ModerationAction` (J.2) row, calls Helix via `ITwitchModerationApi`, fires `ModerationActionAppliedEvent`/`ModerationActionRevertedEvent`, and appends `ModerationAuditLog` (O.8). `actorUserId` is the authenticated principal (no longer implicit). The two bot-side standing methods are the deliberate exception: they never call Helix and write no `ModerationAction` row — their audit is a SYSTEM `UserNote` (J.3), and each write is one `IUnitOfWork` op (standing row + note, all-or-nothing).

```csharp
namespace NomNomzBot.Application.Services;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Moderation;

public interface IModerationService
{
    /// Times out target via Helix; inserts a ModerationAction(timeout); fires ModerationActionAppliedEvent + audit. Returns the persisted action id + snapshot.
    Task<Result<ModerationActionResult>> TimeoutAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, int durationSeconds, string? reason, CancellationToken ct = default);

    /// Bans target via Helix; inserts ModerationAction(ban, origin=local); fires event + audit; bumps UserModerationHistory.BanCount. If ShareOutgoingBans + active shared chat, also fires SharedChatBanIssuedEvent.
    Task<Result<ModerationActionResult>> BanAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, string? reason, CancellationToken ct = default);

    /// Unbans target via Helix; inserts ModerationAction(unban) linking RevertedByActionId on the original ban; sets original IsReverted; fires ModerationActionRevertedEvent + audit.
    Task<Result<ModerationActionResult>> UnbanAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Deletes one chat message via Helix; inserts ModerationAction(delete_message, ChatMessageId set); fires event + audit; bumps MessagesDeletedCount.
    Task<Result<ModerationActionResult>> DeleteMessageAsync(Guid broadcasterId, Guid actorUserId, string messageId, Guid targetUserId, CancellationToken ct = default);

    /// Issues a Twitch-native warning via Helix (POST /moderation/warnings, scope moderator:manage:warnings) — the warned user must acknowledge before chatting again; inserts ModerationAction(warn); fires event + audit; bumps WarningCount.
    Task<Result<ModerationActionResult>> WarnAsync(Guid broadcasterId, Guid actorUserId, Guid targetUserId, string reason, CancellationToken ct = default);

    /// Upserts the bot-side ChannelModerationStanding (J.12) for one platform identity (standing = muted|shadowbanned|blacklisted; userId is that platform's user id). Rejects the broadcaster themselves (CONFLICT — mirror of the "broadcaster can't be banned" guard). Appends a SYSTEM UserNote (J.3, "standing set to muted — <reason>") so the audit rides the existing notes surface (no new domain event); refreshes the per-channel in-process standing map (ChannelContext, via IChannelRegistry invalidation). Never calls Helix.
    Task<Result<ModerationStandingDto>> SetModerationStandingAsync(Guid broadcasterId, Guid actorUserId, string userId, string provider, string standing, string? reason, CancellationToken ct = default);

    /// Deletes the ChannelModerationStanding row (J.12) — back to normal (an absent row means normal). Same SYSTEM-UserNote + standing-map-refresh side effects as the setter. NOT_FOUND if absent.
    Task<Result> ClearModerationStandingAsync(Guid broadcasterId, Guid actorUserId, string userId, string provider, CancellationToken ct = default);

    /// Append-only action history for a channel (J.2), newest first, filtered/paged. Read-only; no side effects.
    Task<Result<PagedList<ModerationActionLog>>> GetActionsAsync(Guid broadcasterId, ModerationActionQuery query, CancellationToken ct = default);

    /// Live banned-user list for a channel from Helix (real data, no seed). Read-only.
    Task<Result<IReadOnlyList<BannedUserDto>>> GetBannedUsersAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.2 `IAutoModConfigService` — automod settings (J.7) + filters (J.6)

```csharp
namespace NomNomzBot.Application.Services.Moderation;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Moderation;

public interface IAutoModConfigService
{
    /// Reads the single AutoModConfig (J.7) for the channel; returns a default-shaped config (not persisted) if none exists. Read-only.
    Task<Result<AutoModConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken ct = default);

    /// Upserts the AutoModConfig (J.7) and pushes Twitch-native automod level via Helix; diffs the blocklist-type ChatFilter terms against Twitch and pushes the delta via Helix POST/DELETE /moderation/blocked_terms (scope moderator:manage:blocked_terms), reading current terms via GET /moderation/blocked_terms; sets BlockedTermsSyncedAt. Returns persisted config.
    Task<Result<AutoModConfigDto>> SaveConfigAsync(Guid broadcasterId, SaveAutoModConfigRequest request, CancellationToken ct = default);

    /// Lists custom ChatFilters (J.6) for the channel, paged. Read-only.
    Task<Result<PagedList<ChatFilterDto>>> ListFiltersAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    /// Inserts a ChatFilter (J.6) (regex/blocklist/link_policy). Validates the regex compiles. Returns created filter.
    Task<Result<ChatFilterDto>> CreateFilterAsync(Guid broadcasterId, CreateChatFilterRequest request, CancellationToken ct = default);

    /// Patches an existing ChatFilter (J.6). Returns updated filter. Fails NOT_FOUND if absent/soft-deleted.
    Task<Result<ChatFilterDto>> UpdateFilterAsync(Guid broadcasterId, long filterId, UpdateChatFilterRequest request, CancellationToken ct = default);

    /// Soft-deletes a ChatFilter (J.6). Idempotent-safe; returns NOT_FOUND if absent.
    Task<Result> DeleteFilterAsync(Guid broadcasterId, long filterId, CancellationToken ct = default);
}
```

### 3.3 `IModerationQueueService` — unified action queue (J.1)

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface IModerationQueueService
{
    /// Inserts a ModerationQueueItem (J.1, status=pending) from automod hold / report / bot flag; fires ModerationQueueItemEnqueuedEvent. Returns item id.
    Task<Result<long>> EnqueueAsync(Guid broadcasterId, EnqueueModerationItemRequest request, CancellationToken ct = default);

    /// Pending/filtered queue, paged, newest first. Read-only.
    Task<Result<PagedList<ModerationQueueItemDto>>> ListAsync(Guid broadcasterId, ModerationQueueQuery query, CancellationToken ct = default);

    /// Resolves one item: approve (release held msg) / deny (drop) / timeout / ban inline. For automod-source items, approve/deny relays through Helix POST /moderation/automod/message (ALLOW/DENY, scope moderator:manage:automod) to release/drop the held Twitch message. Sets Status+ResolvedBy/At+ResolutionAction; on timeout/ban delegates to IModerationService (writes a linked ModerationAction); fires ModerationQueueItemResolvedEvent + audit(queue_resolved).
    Task<Result<ModerationQueueItemDto>> ResolveAsync(Guid broadcasterId, Guid actorUserId, long queueItemId, ResolveQueueItemRequest request, CancellationToken ct = default);

    /// Marks expired pending items past ExpiresAt as status=expired (called by the held-message TTL sweep). Returns count expired. Idempotent.
    Task<Result<int>> ExpireStaleAsync(Guid broadcasterId, DateTime asOfUtc, CancellationToken ct = default);
}
```

### 3.4 `INetworkNukeService` — cross-channel mass ban + reversal (J.2a)

SuperMod+ only — Gate-2 `moderation:nuke` / `moderation:sharedban:write` at the controller, AND re-checked in-service via `IRoleResolver.ResolveEffectiveLevelAsync ≥ SuperMod(20)`. Legit ban API only — **no mass-reporting** (design §"Network nuke").

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface INetworkNukeService
{
    /// Bans target across every channel the actor holds ban rights on (SuperMod+). Creates ONE NetworkNukeBatch (J.2a), fans out one ModerationAction(nuke, origin=network_nuke, NetworkNukeBatchId set) per channel, sets ChannelCount, fires NetworkNukeExecutedEvent + audit per leg. Partial leg failures → batch Status=partial. Single-confirmation enforced by RequireConfirmation flag in request.
    Task<Result<NetworkNukeBatchDto>> NukeAsync(Guid originBroadcasterId, Guid actorUserId, NetworkNukeRequest request, CancellationToken ct = default);

    /// Reverses an entire batch as one unit: unbans on every actioned channel, inserts ModerationAction(unban) per leg linking RevertedByActionId, sets each nuke action IsReverted, sets batch Status=reverted/partial + RevertedBy/At; fires NetworkNukeRevertedEvent + audit. Fails NOT_FOUND/FORBIDDEN if actor lacks rights on the origin.
    Task<Result<NetworkNukeBatchDto>> RevertAsync(Guid actorUserId, Guid batchId, CancellationToken ct = default);

    /// Lists nuke batches initiated from a channel, paged. Read-only.
    Task<Result<PagedList<NetworkNukeBatchDto>>> ListBatchesAsync(Guid originBroadcasterId, PaginationParams pagination, CancellationToken ct = default);

    /// Builds a legitimate-individual evidence packet (offending messages, timestamps, context) for one-click filing through Twitch's own flow. NEVER files reports programmatically. Read-only aggregation.
    Task<Result<EvidencePacketDto>> BuildEvidencePacketAsync(Guid broadcasterId, Guid targetUserId, EvidencePacketRequest request, CancellationToken ct = default);
}
```

### 3.5 `ISharedBanService` — shared-chat ban propagation + trust list (J.9/J.9a)

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface ISharedBanService
{
    /// Reads SharedBanSettings (J.9) + trusted-channel list (J.9a) for the channel; returns defaults (accept=false, share=false) if none. Read-only.
    Task<Result<SharedBanSettingsDto>> GetSettingsAsync(Guid broadcasterId, CancellationToken ct = default);

    /// Upserts SharedBanSettings (J.9). Default-deny; SuperMod/Broadcaster gated. Returns persisted settings.
    Task<Result<SharedBanSettingsDto>> SaveSettingsAsync(Guid broadcasterId, Guid actorUserId, SaveSharedBanSettingsRequest request, CancellationToken ct = default);

    /// Adds a SharedBanTrustedChannel (J.9a). Unique (BroadcasterId, TrustedChannelId); idempotent on conflict. Returns the row dto.
    Task<Result<SharedBanTrustedChannelDto>> AddTrustedChannelAsync(Guid broadcasterId, Guid actorUserId, Guid trustedChannelId, CancellationToken ct = default);

    /// Removes a SharedBanTrustedChannel (J.9a). Returns NOT_FOUND if absent.
    Task<Result> RemoveTrustedChannelAsync(Guid broadcasterId, Guid trustedChannelId, CancellationToken ct = default);

    /// Applies an inbound SharedChatBanIssuedEvent to THIS partner channel iff AcceptSharedChatBans + origin is trusted (J.9a) AND an active shared-chat session is verified (SharedChatSessionId on the inbound event). Writes ModerationAction(ban, origin=federation, OriginChannelId set) — origin=federation marks the cross-instance federated apply path, distinct from origin=shared_chat (a Twitch-native same-instance shared-chat session ban); bans via Helix. Returns Applied/Skipped(reason). Called by the federation inbound handler — the predicate is enforced here, not by the caller.
    Task<Result<SharedBanApplicationResult>> ApplyInboundSharedBanAsync(Guid partnerBroadcasterId, SharedChatBanIssuedEvent inbound, CancellationToken ct = default);
}
```

> **Inbound model (decided 2026-07-17, as built):** the shared-ban web is a fully LOCAL chain — there is no
> remote-peer inbound today (the federation handshake transport does not exist, and the J.9a trust list is
> keyed by local `Channels` Guids). The chain: the `channel.shared_chat.begin/update/end` EventSub events
> feed a singleton `ISharedChatSessionTracker` (in-memory active-session state per channel, keyed by tenant
> Guid, carrying `SessionId` + participants); a subscriber on `UserBannedEvent` publishes
> `SharedChatBanIssuedEvent { SharedChatSessionId, OriginChannelId, TargetTwitchUserId, TargetDisplayName?,
> Reason? }` IFF the origin channel is tracked in a session AND opted in via `ShareOutgoingBans`; a consumer
> fans it out to every OTHER local channel tracked in the same session, each applying through
> `ApplyInboundSharedBanAsync` (accept + trust + same-session predicate enforced in-service). The ban
> executes on the partner's OWN tenant token (`ITwitchModerationApi.BanUserAsync` — broadcaster =
> moderator = the channel, system-initiated, no operator), and provenance is recorded as a
> `moderation_action` Record whose JSON carries `Origin="shared_chat"` + `OriginChannelId` (the `federation` origin stays reserved for a true cross-instance transport) (additive to the
> existing `ModerationActionData` shape; the record's `UserId` = the origin channel id). A future
> cross-instance federation transport plugs in by publishing the same `SharedChatBanIssuedEvent` from its
> inbound dispatcher — the consumer chain is transport-agnostic.

### 3.6 `IViewerReportService` — viewer reports + evidence (J.8/J.8a)

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface IViewerReportService
{
    /// Inserts a ViewerReport (J.8, status=open) plus ViewerReportEvidence (J.8a) rows for each cited message id; enqueues a linked ModerationQueueItem(source=viewer_report); fires ViewerReportFiledEvent. Returns report id.
    Task<Result<long>> FileReportAsync(Guid broadcasterId, FileViewerReportRequest request, CancellationToken ct = default);

    /// Reports for a channel, filtered/paged, with evidence counts. Read-only.
    Task<Result<PagedList<ViewerReportDto>>> ListAsync(Guid broadcasterId, ViewerReportQuery query, CancellationToken ct = default);

    /// One report with its full evidence message list (J.8a join). Read-only. NOT_FOUND if absent.
    Task<Result<ViewerReportDetailDto>> GetAsync(Guid broadcasterId, long reportId, CancellationToken ct = default);

    /// Transitions report Status (triaged/dismissed/escalated); on escalated may enqueue/raise the linked queue item priority. Returns updated dto + audit.
    Task<Result<ViewerReportDto>> SetStatusAsync(Guid broadcasterId, Guid actorUserId, long reportId, ViewerReportStatus status, CancellationToken ct = default);
}
```

### 3.7 `IUserContextService` — per-user mod panel (notes J.3, history J.4, trust J.5)

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface IUserContextService
{
    /// Aggregated per-user context: pinned+recent notes (J.3), rollup counts (J.4), trust/heat (J.5), recent actions (J.2), current bot-side standings (J.12 — one per platform identity; empty = normal). Read-only.
    Task<Result<UserContextDto>> GetContextAsync(Guid broadcasterId, Guid subjectUserId, CancellationToken ct = default);

    /// Adds a shared UserNote (J.3). Returns created note. Content is [PII-scrub].
    Task<Result<UserNoteDto>> AddNoteAsync(Guid broadcasterId, Guid authorUserId, Guid subjectUserId, AddUserNoteRequest request, CancellationToken ct = default);

    /// Toggles UserNote.Pinned (J.3). NOT_FOUND if absent.
    Task<Result<UserNoteDto>> SetNotePinnedAsync(Guid broadcasterId, long noteId, bool pinned, CancellationToken ct = default);

    /// Soft-deletes a UserNote (J.3). NOT_FOUND if absent.
    Task<Result> DeleteNoteAsync(Guid broadcasterId, long noteId, CancellationToken ct = default);
}
```

### 3.8 `IModerationProjectionService` — rebuildable projections (J.4, J.5)

Maintains the two append-only-derived projections. Called by event handlers (`ModerationActionAppliedEvent` etc.), not controllers.

```csharp
namespace NomNomzBot.Application.Services.Moderation;

public interface IModerationProjectionService
{
    /// Incrementally updates UserModerationHistory (J.4) counts/last-action from one applied/reverted action. Idempotent per ModerationAction id.
    Task<Result> ApplyActionToHistoryAsync(Guid broadcasterId, Guid subjectUserId, ModerationActionType actionType, bool reverted, DateTime occurredAtUtc, CancellationToken ct = default);

    /// Recomputes UserTrustScore (J.5) for a user using TrustScoreCalculator (reused). Upserts (BroadcasterId, SubjectUserId); fires UserHeatThresholdCrossedEvent when HeatScore crosses AutoModConfig.HeatTimeoutThreshold.
    Task<Result<UserTrustScoreDto>> RecomputeTrustAsync(Guid broadcasterId, Guid subjectUserId, CancellationToken ct = default);

    /// Full rebuild of both projections for a channel from ModerationActions (J.2). Admin/maintenance. Returns rows rebuilt.
    Task<Result<int>> RebuildAsync(Guid broadcasterId, CancellationToken ct = default);
}
```

> **Trust reuse (design "reuse TrustScoreCalculator").** `RecomputeTrustAsync` builds a `NomNomzBot.Infrastructure.Services.Trust.TrustContext` (existing) from `UserModerationHistory` (TimeoutCount/BanCount) + Helix/community data and calls the existing static `TrustScoreCalculator.Calculate`. **Do not fork** the algorithm. `HeatScore` is the inverse/complementary signal (recent-violation pressure); store both `decimal(8,4)` per J.5.
>
> **HeatScore accrual (decided).** Heat is a 0–100 signal with exponential decay, half-life **24 h**. On every recompute: `HeatScore = clamp(HeatScore × 0.5^(Δt / 24h) + delta, 0, 100)` with `Δt` = time since `LastHeatEventAt`. Per-violation deltas: filter/blocked-term hit **+5**; AutoMod-held message denied (confirmed violation) **+5**; validated viewer report **+10**; timeout **+15**; ban **+40**; the `apply_heat` pipeline action supplies its explicit `delta`. There is no negative accrual (unban/report-dismissed add nothing) — decay is the only cool-down. `UserHeatThresholdCrossedEvent` fires only on an **upward** crossing of `AutoModConfig.HeatTimeoutThreshold`.

### 3.9 `IChatControlService` — chat & channel controls (Group B)

Twitch-native chat/channel knobs that are **Helix-relayed, not row-owned** here: chat settings, Shield Mode, announcements, and the bot's own chat color all mutate Twitch state via `ITwitchHelixClient` (Helix sub-clients). The single persisted bit is `AutoModConfigs.ShieldModeActive` (J.7, new column — Twitch is the system of record for the live toggle; the flag is a denormalized cache for dashboard reads without a Helix round-trip). Each write fires the matching §2 event + appends `ModerationAuditLog` (O.8, `EventType=action_taken`); none writes a `ModerationAction` row (these aren't per-target actions).

```csharp
namespace NomNomzBot.Application.Services.Moderation;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Moderation;

public interface IChatControlService
{
    /// Reads current chat settings from Helix (GET /chat/settings). Read-only.
    Task<Result<ChatSettingsDto>> GetChatSettingsAsync(Guid broadcasterId, CancellationToken ct = default);

    /// Patches chat settings via Helix (PATCH /chat/settings — slow/follower-only/subs-only/emote-only/unique-chat/non-mod delay). Only set fields change. Fires ChatSettingsUpdatedEvent + audit. Returns the post-update settings.
    Task<Result<ChatSettingsDto>> UpdateChatSettingsAsync(Guid broadcasterId, Guid actorUserId, UpdateChatSettingsRequest request, CancellationToken ct = default);

    /// Reads Shield Mode status from Helix (GET /moderation/shield_mode), reconciling the AutoModConfigs.ShieldModeActive cache. Read-only.
    Task<Result<ShieldModeStatusDto>> GetShieldModeAsync(Guid broadcasterId, CancellationToken ct = default);

    /// Toggles Shield Mode via Helix (PUT /moderation/shield_mode); upserts AutoModConfigs.ShieldModeActive (J.7); fires ShieldModeUpdatedEvent + audit. Returns the post-update status.
    Task<Result<ShieldModeStatusDto>> SetShieldModeAsync(Guid broadcasterId, Guid actorUserId, bool isActive, CancellationToken ct = default);

    /// Sends a chat announcement via Helix (POST /chat/announcements, color blue/green/orange/purple/primary). Fires ChatAnnouncementSentEvent + audit. No row written.
    Task<Result> SendAnnouncementAsync(Guid broadcasterId, Guid actorUserId, SendAnnouncementRequest request, CancellationToken ct = default);

    /// Updates the BOT's own chat name color via Helix (PUT /chat/color, user:manage:chat_color on the bot identity). Cosmetic; fires no moderation event, appends audit only. Returns the applied color.
    Task<Result<string>> UpdateBotChatColorAsync(Guid broadcasterId, Guid actorUserId, string color, CancellationToken ct = default);
}
```

> **Identity note.** `UpdateBotChatColorAsync` is the only Group-B call made on the **bot** identity (`user:manage:chat_color`); the rest use the broadcaster/moderator identity exactly like the §3.1 direct actions. `SendAnnouncementAsync` uses `moderator:manage:announcements`; Shield Mode `moderator:manage:shield_mode`; chat settings `moderator:manage:chat_settings`. All are **progressive** scopes (requested when the feature is enabled), per the `TwitchScopeRequirements` map (`twitch-helix.md` §9).

### 3.10 `IModerationDirectoryService` — VIP / moderator / unban-request / block-list / suspicious-user writes (Group C)

The Twitch-native directory/treatment mutations that complete the moderation write surface. Each relays through the §3.3 `ITwitchModerationApi` Helix legs (VIP/moderator via `AddVipAsync`/`RemoveVipAsync`/`RemoveModeratorAsync`; unban-requests/blocks/suspicious-users via their Helix endpoints). VIP/moderator/block/suspicious changes fire the matching §2 event + audit (O.8); none writes a `ModerationAction` row (those are target-message/ban actions, not directory edits). Unban-request *resolve* additionally writes a `ModerationAction(unban, origin=local)` when the resolution approves and lifts the standing ban.

```csharp
namespace NomNomzBot.Application.Services.Moderation;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Moderation;

public interface IModerationDirectoryService
{
    /// Grants VIP via Helix (POST /channels/vips). Fires ChannelVipChangedEvent(Granted=true) + audit. Surfaces Twitch's VIP-slot-limit as a Result.Failure.
    Task<Result> AddVipAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Removes VIP via Helix (DELETE /channels/vips). Fires ChannelVipChangedEvent(Granted=false) + audit.
    Task<Result> RemoveVipAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Removes a Twitch moderator via Helix (DELETE /moderation/moderators). Fires ChannelModeratorRemovedEvent + audit. The mirrored ManagementRole demotion is owned by the roles subsystem reacting to the resulting EventSub channel.moderator.remove, not written here.
    Task<Result> RemoveModeratorAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Lists pending Twitch unban requests via Helix (GET /moderation/unban_requests). Read-only. Twitch scope `moderator:read:unban_requests` (progressive).
    Task<Result<PagedList<UnbanRequestDto>>> ListUnbanRequestsAsync(Guid broadcasterId, UnbanRequestQuery query, CancellationToken ct = default);

    /// Resolves a Twitch unban request via Helix (PATCH /moderation/unban_requests, status approved/denied). Twitch scope `moderator:manage:unban_requests` (progressive). On approve, also writes ModerationAction(unban, origin=local) + bumps UserModerationHistory. Fires UnbanRequestResolvedEvent + audit. Returns the resolved request.
    Task<Result<UnbanRequestDto>> ResolveUnbanRequestAsync(Guid broadcasterId, Guid actorUserId, ResolveUnbanRequestRequest request, CancellationToken ct = default);

    /// Adds to the broadcaster's personal block list via Helix (PUT /users/blocks). Distinct from a channel ban / shared-ban. Fires UserBlockListChangedEvent(Blocked=true) + audit.
    Task<Result> BlockUserAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Removes from the broadcaster's personal block list via Helix (DELETE /users/blocks). Fires UserBlockListChangedEvent(Blocked=false) + audit.
    Task<Result> UnblockUserAsync(Guid broadcasterId, Guid actorUserId, string targetTwitchUserId, CancellationToken ct = default);

    /// Updates a suspicious user's treatment via Helix (PUT /moderation/suspicious_users — none/active_monitoring/restricted). Twitch scope `moderator:read:suspicious_users` (progressive). Fires SuspiciousUserTreatmentUpdatedEvent + audit.
    Task<Result> UpdateSuspiciousUserAsync(Guid broadcasterId, Guid actorUserId, UpdateSuspiciousUserRequest request, CancellationToken ct = default);
}
```

> **No new rows.** Unban-requests, blocks, and suspicious-user treatment are **Twitch-owned state** — moderation relays the call and emits its event/audit but persists no Domain-J table for them (Twitch is the system of record; the dashboard reads them live via the `List*`/`Get*` Helix reads). The lone exception is the `unban → ModerationAction(unban)` row when an unban request is *approved*, which reuses the existing J.2 ban-reversal path. Suspicious-user *detection* arrives via EventSub `channel.suspicious_user.*` (owned by the EventSub subsystem); this service only writes the treatment back.

### 3.11 `IModerationEscalationService` — auto-mod escalation ladder (J.10/J.11)

The **explicit discrete** escalation path: a per-channel ladder maps a subject's running offense count to a `warn`/`timeout`/`ban` action over a decaying window (J.10 `ModerationEscalationPolicy` config, J.11 `ModerationEscalationState` per-subject tally). Invoked from the chat-filter path when a `ChatFilter` fires with `Action=Escalate` (§3.2 enforcement / the automod engine): the service resolves+records the offense and returns the action, which the caller applies via §3.1 `IModerationService` — the resulting `ModerationActionAppliedEvent` carries `OffenseCount` in its metadata.

```csharp
namespace NomNomzBot.Application.Services.Moderation;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Moderation;
using NomNomzBot.Domain.Enums;

public interface IModerationEscalationService
{
    // Records one offense for the subject (resets the decaying window if OffenseWindowHours elapsed, else increments
    // OffenseCount), looks up the ladder step for the new count (clamped to the highest step), and returns the action
    // to apply. The caller — the chat-filter path when a filter's Action=Escalate — applies it via IModerationService;
    // the resulting ModerationActionAppliedEvent carries OffenseCount in its metadata.
    Task<Result<EscalationDecision>> ResolveAndRecordAsync(Guid broadcasterId, Guid subjectUserId, string subjectTwitchUserId, CancellationToken ct = default);
    Task<Result<ModerationEscalationPolicyDto>> GetPolicyAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<ModerationEscalationPolicyDto>> UpsertPolicyAsync(Guid broadcasterId, UpsertEscalationPolicyRequest request, CancellationToken ct = default);
    Task<Result> ResetUserAsync(Guid broadcasterId, Guid subjectUserId, CancellationToken ct = default);   // forgiveness — clears the tally
}

public sealed record EscalationLadderStep(int AtOffense, string Action, int? TimeoutSeconds);   // Action ∈ warn|timeout|ban
public sealed record EscalationDecision(ModerationActionType Action, int? TimeoutSeconds, int OffenseCount);
public sealed record ModerationEscalationPolicyDto(bool IsEnabled, IReadOnlyList<EscalationLadderStep> Ladder, int OffenseWindowHours, bool CountAutoModViolations);
public sealed record UpsertEscalationPolicyRequest(bool IsEnabled, IReadOnlyList<EscalationLadderStep> Ladder, int OffenseWindowHours, bool CountAutoModViolations);
```

> **Default ladder (safety baseline).** Seeded when a channel enables the ladder without supplying one: offense 1 → `warn`, 2 → `timeout 60s`, 3 → `600s`, 4 → `3600s`, 5 → `86400s`, 6+ → `ban` (the highest step clamps — every offense at or past the top rung applies `ban`).

> **Two complementary paths — discrete vs continuous.** The ladder is the **explicit discrete** escalation path. The existing `UserTrustScore.HeatScore` (J.5) + `AutoModConfig.AutoTimeoutOnHeat`/`HeatTimeoutThreshold` (J.7) is the **continuous** AutoMod-driven path (heat accrues per violation; an auto-timeout fires when it crosses the threshold). They are **complementary** — a channel may run either or both. When a filter fires with `Action=Escalate`, the **ladder** decides the action (not the heat threshold); `CountAutoModViolations` (J.10, default false) governs whether native Twitch AutoMod violations also tick the ladder's offense counter, keeping the two paths independent by default.

---

## 4. DTOs / contracts

Namespace `NomNomzBot.Application.DTOs.Moderation` (extend the existing `ModerationDtos.cs`; split large groups into sibling files — `ModerationQueueDtos.cs`, `NetworkNukeDtos.cs`, `SharedBanDtos.cs`, `ViewerReportDtos.cs`, `UserContextDtos.cs` — one concern per file). All `sealed record`. Inbound request records validated by the in-box `.NET 10 AddValidation()` source generator.

**Kept/retyped from existing `ModerationDtos.cs`:** `ModerationActionResult(bool Success, string? Message)` (kept). `ModerationActionLog` retyped — `ModeratorId`/`TargetUserId` become `Guid`, adds `Origin`, `IsReverted`. `BannedUserDto` kept. The old `AutomodConfigDto`/`AutomodLinkFilterDto`/`AutomodCapsFilterDto`/`AutomodBannedPhrasesDto`/`AutomodEmoteSpamDto` and `ModerationRuleListItem`/`ModerationRuleDetail`/`CreateModerationRuleRequest`/`UpdateModerationRuleRequest`/`ModLogEntryDto` are **superseded** by the J.6/J.7-shaped records below; remove after migrating callers.

```csharp
namespace NomNomzBot.Application.DTOs.Moderation;

using NomNomzBot.Domain.Enums;

// ── Direct actions ─────────────────────────────────────────────────────────
public sealed record ModerationActionResult(bool Success, string? Message, long? ActionId = null);

public sealed record ModerationActionLog(
    long Id, ModerationActionType ActionType, Guid ActorUserId, string ActorUsername,
    Guid TargetUserId, string? TargetUsername, string? Reason, int? DurationSeconds,
    ModerationActionOrigin Origin, bool IsReverted, DateTime CreatedAt);

public sealed record ModerationActionQuery(
    int Page = 1, int PageSize = 25, ModerationActionType? ActionType = null,
    Guid? TargetUserId = null, DateTime? Since = null, DateTime? Until = null);

public sealed record BannedUserDto(string TwitchUserId, string Username, string? Reason, string BannedBy, DateTime BannedAt);

// Bot-side moderation standing (J.12) — UserId is the platform user id, Standing ∈ muted|shadowbanned|blacklisted.
public sealed record ModerationStandingDto(string UserId, string Provider, string Standing, string? Reason, DateTime UpdatedAt);

// ── AutoMod config (J.7) ───────────────────────────────────────────────────
public sealed record AutoModConfigDto(
    Guid Id, bool IsEnabled, int OverallLevel, IReadOnlyDictionary<string,int> CategoryLevels,
    int HeldMessageTimeoutSeconds, bool BlockHyperlinks, bool RequireVerifiedAccount,
    bool RequireVerifiedEmail, bool AutoTimeoutOnHeat, decimal? HeatTimeoutThreshold, DateTime? BlockedTermsSyncedAt);

public sealed record SaveAutoModConfigRequest
{
    public required bool IsEnabled { get; init; }
    public required int OverallLevel { get; init; }          // 0–4 (Twitch automod levels)
    public Dictionary<string,int>? CategoryLevels { get; init; }
    public int HeldMessageTimeoutSeconds { get; init; } = 300;
    public bool BlockHyperlinks { get; init; }
    public bool RequireVerifiedAccount { get; init; }
    public bool RequireVerifiedEmail { get; init; }
    public bool AutoTimeoutOnHeat { get; init; }
    public decimal? HeatTimeoutThreshold { get; init; }
}

// ── Chat filters (J.6) ─────────────────────────────────────────────────────
public sealed record ChatFilterDto(
    long Id, ChatFilterType FilterType, string Name, string? Pattern, IReadOnlyList<string> Terms,
    ChatFilterAction Action, int? TimeoutSeconds, int ExemptMinRoleLevel, bool IsEnabled,
    bool IsCaseSensitive, long MatchCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateChatFilterRequest
{
    public required ChatFilterType FilterType { get; init; }
    public required string Name { get; init; }
    public required ChatFilterAction Action { get; init; }
    public string? Pattern { get; init; }
    public List<string>? Terms { get; init; }
    public string? LinkPolicyJson { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int ExemptMinRoleLevel { get; init; } = 10;       // moderator floor
    public bool IsEnabled { get; init; } = true;
    public bool IsCaseSensitive { get; init; }
}

public sealed record UpdateChatFilterRequest
{
    public string? Name { get; init; }
    public ChatFilterAction? Action { get; init; }
    public string? Pattern { get; init; }
    public List<string>? Terms { get; init; }
    public string? LinkPolicyJson { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? ExemptMinRoleLevel { get; init; }
    public bool? IsEnabled { get; init; }
    public bool? IsCaseSensitive { get; init; }
}

// ── Unified queue (J.1) ────────────────────────────────────────────────────
public sealed record ModerationQueueItemDto(
    long Id, ModerationQueueSource Source, ModerationQueueStatus Status, Guid TargetUserId,
    string? TargetUsername, long? ChatMessageId, string? MessageContent, string? Reason,
    string? AutoModCategory, Guid? ResolvedByUserId, string? ResolutionAction,
    DateTime? ExpiresAt, DateTime CreatedAt);

public sealed record ModerationQueueQuery(
    int Page = 1, int PageSize = 25, ModerationQueueSource? Source = null, ModerationQueueStatus? Status = null);

public sealed record EnqueueModerationItemRequest
{
    public required ModerationQueueSource Source { get; init; }
    public required string TargetTwitchUserId { get; init; }
    public string? TargetUsername { get; init; }
    public long? ChatMessageId { get; init; }
    public string? MessageContent { get; init; }
    public string? Reason { get; init; }
    public string? AutoModCategory { get; init; }
    public Guid? ReportedByUserId { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

public sealed record ResolveQueueItemRequest
{
    public required string Resolution { get; init; }         // approve | deny | timeout | ban
    public int? DurationSeconds { get; init; }               // required when Resolution == timeout
    public string? Reason { get; init; }
}

// ── Network nuke (J.2a) ────────────────────────────────────────────────────
public sealed record NetworkNukeBatchDto(
    Guid Id, Guid OriginBroadcasterId, Guid? InitiatedByUserId, string? MatchTerm,
    Guid? TargetUserId, string? TargetTwitchUserId, int ChannelCount, NetworkNukeStatus Status,
    Guid? RevertedByUserId, DateTime? RevertedAt, DateTime CreatedAt);

public sealed record NetworkNukeRequest
{
    public required string TargetTwitchUserId { get; init; }
    public string? Reason { get; init; }
    public string? MatchTerm { get; init; }
    public required bool RequireConfirmation { get; init; }  // must be true; single-confirmation guardrail
}

public sealed record EvidencePacketRequest
{
    public int MaxMessages { get; init; } = 50;
    public DateTime? Since { get; init; }
}

public sealed record EvidencePacketDto(
    Guid TargetUserId, string TargetTwitchUserId, string TargetUsername,
    IReadOnlyList<EvidenceMessageDto> Messages, IReadOnlyList<string> ClipUrls,
    DateTime GeneratedAt, string TwitchReportFlowUrl);

public sealed record EvidenceMessageDto(long ChatMessageId, string Content, DateTime SentAt);

// ── Shared bans (J.9/J.9a) ─────────────────────────────────────────────────
public sealed record SharedBanSettingsDto(
    bool AcceptSharedChatBans, bool ShareOutgoingBans, IReadOnlyList<SharedBanTrustedChannelDto> TrustedChannels);

public sealed record SaveSharedBanSettingsRequest
{
    public required bool AcceptSharedChatBans { get; init; }
    public required bool ShareOutgoingBans { get; init; }
}

public sealed record SharedBanTrustedChannelDto(Guid TrustedChannelId, string TrustedChannelName, Guid? AddedByUserId, DateTime CreatedAt);

public sealed record SharedBanApplicationResult(bool Applied, string? SkippedReason, long? ActionId);

// ── Viewer reports (J.8/J.8a) ──────────────────────────────────────────────
public sealed record ViewerReportDto(
    long Id, Guid ReportedUserId, string? ReportedUsername, Guid? ReporterUserId,
    string Reason, ViewerReportStatus Status, int EvidenceCount, long? QueueItemId, DateTime CreatedAt);

public sealed record ViewerReportDetailDto(ViewerReportDto Report, IReadOnlyList<EvidenceMessageDto> Evidence);

public sealed record ViewerReportQuery(int Page = 1, int PageSize = 25, ViewerReportStatus? Status = null);

public sealed record FileViewerReportRequest
{
    public required string ReportedTwitchUserId { get; init; }
    public required string Reason { get; init; }
    public Guid? ReporterUserId { get; init; }
    public List<long>? EvidenceMessageIds { get; init; }
}

// ── User context (J.3/J.4/J.5) ─────────────────────────────────────────────
public sealed record UserContextDto(
    Guid SubjectUserId, string? Username, UserModerationHistoryDto History,
    UserTrustScoreDto? Trust, IReadOnlyList<UserNoteDto> Notes, IReadOnlyList<ModerationActionLog> RecentActions,
    IReadOnlyList<ModerationStandingDto> Standings);   // bot-side standings (J.12), one per platform identity; empty = normal

public sealed record UserModerationHistoryDto(
    int TimeoutCount, int BanCount, int WarningCount, int MessagesDeletedCount,
    DateTime? LastActionAt, ModerationActionType? LastActionType, DateTime? FirstSeenAt);

public sealed record UserTrustScoreDto(decimal TrustScore, decimal HeatScore, DateTime? LastHeatEventAt, DateTime ComputedAt);

public sealed record UserNoteDto(long Id, Guid? AuthorUserId, string? AuthorUsername, string Content, bool Pinned, DateTime CreatedAt);

public sealed record AddUserNoteRequest
{
    public required string Content { get; init; }
    public bool Pinned { get; init; }
}

// ── Chat & channel controls (Group B — Helix-relayed) ──────────────────────
public sealed record ChatSettingsDto(
    bool SlowMode, int? SlowModeWaitSeconds, bool FollowerMode, int? FollowerModeDurationMinutes,
    bool SubscriberMode, bool EmoteMode, bool UniqueChatMode, int? NonModeratorChatDelaySeconds);

public sealed record UpdateChatSettingsRequest
{
    public bool? SlowMode { get; init; }
    public int? SlowModeWaitSeconds { get; init; }              // 3–120 when SlowMode=true
    public bool? FollowerMode { get; init; }
    public int? FollowerModeDurationMinutes { get; init; }      // 0–129600
    public bool? SubscriberMode { get; init; }
    public bool? EmoteMode { get; init; }
    public bool? UniqueChatMode { get; init; }
    public int? NonModeratorChatDelaySeconds { get; init; }     // 2 | 4 | 6
}

public sealed record ShieldModeStatusDto(bool IsActive, string? ModeratorTwitchUserId, string? ModeratorName, DateTime? LastActivatedAt);

public sealed record SendAnnouncementRequest
{
    public required string Message { get; init; }              // ≤ 500 chars
    public string Color { get; init; } = "primary";           // blue | green | orange | purple | primary
}

// ── Moderation directory / treatment (Group C — Helix-relayed) ─────────────
public sealed record UnbanRequestDto(
    string UnbanRequestId, Guid TargetUserId, string TargetTwitchUserId, string TargetUsername,
    string Text, UnbanRequestStatus Status, string? ResolutionText, Guid? ResolvedByUserId,
    DateTime CreatedAt, DateTime? ResolvedAt);

public sealed record UnbanRequestQuery(int Page = 1, int PageSize = 25, UnbanRequestStatus Status = UnbanRequestStatus.Pending);

public sealed record ResolveUnbanRequestRequest
{
    public required string UnbanRequestId { get; init; }
    public required UnbanRequestStatus Status { get; init; }    // approved | denied
    public string? ResolutionText { get; init; }
}

public sealed record UpdateSuspiciousUserRequest
{
    public required string TargetTwitchUserId { get; init; }
    public required SuspiciousUserTreatment Treatment { get; init; }   // none | active_monitoring | restricted
}
```

---

## 5. Controller endpoints

Single `ModerationController` (extend existing) at `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/moderation")]`, `[ApiVersion("1.0")]`, `[Authorize]`. `channelId` is now `Guid` (was `string`). Responses wrapped in `StatusResponseDto<T>` / `PaginatedResponse<T>` via the existing `BaseController` helpers (`ResultResponse`, `GetPaginatedResponse`). Large groups MAY be split into `NetworkNukeController` / `SharedBanController` / `ViewerReportController` under the same route prefix — listed inline below for completeness.

**Role gate (`roles-permissions.md` §0 + §3.3).** Two gates guard every row:

- **Gate 1** = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's).
- **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below). It resolves the caller's effective level via `IRoleResolver` (the MAX of community standing, `ManagementRole` membership, and active `!permit` grants) and compares it to the action's effective required level (`ActionDefinitions.DefaultLevel`, clamped to `FloorLevel`, channel-overridable via `ChannelActionOverrides`).

The keys are seeded global `ActionDefinitions` (schema B.3) with `Plane=Management`; a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. The management ladder is the canonical `ManagementRole` enum — **`Moderator(10) < SuperMod(20) < Editor(30) < Broadcaster(40)`** — always PascalCase, never `super_mod`/`moderator` snake-case; `FloorLevel` is held at the seeded level for the Critical-tier actions (nuke / shared-ban). There is **no** bespoke `[RequireManagementRole]` attribute and **no** `[Authorize(Roles="admin")]` target — moderation consumes the roles/permissions subsystem's gate, it does not reimplement one. Staff cross-tenant access (Plane C) is carried separately on `ModerationAuditLog.ActorIamPrincipalId` and authorized via `IPlatformIamService.AuthorizePlatformAsync` (`tenant:access`), never via the management ladder.

| Method | Route (suffix under `…/moderation`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|--------|-------------------------------------|-------------|--------------|-----------------------------------|
| POST | `/actions/timeout` | `TimeoutUserRequest` | `StatusResponseDto<ModerationActionResult>` | management / Moderator · `moderation:timeout` |
| POST | `/actions/ban` | `BanUserRequest` | `StatusResponseDto<ModerationActionResult>` | management / Moderator · `moderation:ban` |
| DELETE | `/bans/{targetTwitchUserId}` | — | `StatusResponseDto<ModerationActionResult>` | management / Moderator · `moderation:unban` |
| POST | `/actions/delete-message` | `DeleteMessageRequest` | `StatusResponseDto<ModerationActionResult>` | management / Moderator · `moderation:delete_message` |
| POST | `/actions/warn` | `WarnUserRequest` | `StatusResponseDto<ModerationActionResult>` | management / Moderator · `moderation:warn` |
| GET | `/actions` | `ModerationActionQuery` (query) | `PaginatedResponse<ModerationActionLog>` | management / Moderator · `moderation:action:read` |
| GET | `/bans` | — | `StatusResponseDto<IReadOnlyList<BannedUserDto>>` | management / Moderator · `moderation:action:read` |
| GET | `/automod` | — | `StatusResponseDto<AutoModConfigDto>` | management / Moderator · `moderation:automod:read` |
| PUT | `/automod` | `SaveAutoModConfigRequest` | `StatusResponseDto<AutoModConfigDto>` | management / Editor · `moderation:automod:write` |
| GET | `/escalation` | — | `StatusResponseDto<ModerationEscalationPolicyDto>` | management / Moderator · `moderation:escalation:read` |
| PUT | `/escalation` | `UpsertEscalationPolicyRequest` | `StatusResponseDto<ModerationEscalationPolicyDto>` | management / SuperMod · `moderation:escalation:write` |
| POST | `/escalation/users/{userId:guid}/reset` | — | 204 | management / SuperMod · `moderation:escalation:write` |
| GET | `/filters` | `PaginationParams` (query) | `PaginatedResponse<ChatFilterDto>` | management / Moderator · `moderation:filter:read` |
| POST | `/filters` | `CreateChatFilterRequest` | `StatusResponseDto<ChatFilterDto>` (201) | management / Editor · `moderation:filter:write` |
| PUT | `/filters/{filterId:long}` | `UpdateChatFilterRequest` | `StatusResponseDto<ChatFilterDto>` | management / Editor · `moderation:filter:write` |
| DELETE | `/filters/{filterId:long}` | — | 204 | management / Editor · `moderation:filter:write` |
| GET | `/queue` | `ModerationQueueQuery` (query) | `PaginatedResponse<ModerationQueueItemDto>` | management / Moderator · `moderation:queue:read` |
| POST | `/queue/{queueItemId:long}/resolve` | `ResolveQueueItemRequest` | `StatusResponseDto<ModerationQueueItemDto>` | management / Moderator · `moderation:queue:resolve` |
| GET | `/reports` | `ViewerReportQuery` (query) | `PaginatedResponse<ViewerReportDto>` | management / Moderator · `moderation:report:read` |
| GET | `/reports/{reportId:long}` | — | `StatusResponseDto<ViewerReportDetailDto>` | management / Moderator · `moderation:report:read` |
| POST | `/reports` | `FileViewerReportRequest` | `StatusResponseDto<long>` (201) | management / Moderator · `moderation:report:file` ¹ |
| PATCH | `/reports/{reportId:long}/status` | `SetReportStatusRequest` | `StatusResponseDto<ViewerReportDto>` | management / Moderator · `moderation:report:triage` |
| GET | `/users/{subjectUserId:guid}` | — | `StatusResponseDto<UserContextDto>` | management / Moderator · `moderation:usercontext:read` |
| POST | `/users/{subjectUserId:guid}/notes` | `AddUserNoteRequest` | `StatusResponseDto<UserNoteDto>` (201) | management / Moderator · `moderation:note:write` |
| PATCH | `/notes/{noteId:long}/pin` | `SetNotePinnedRequest` | `StatusResponseDto<UserNoteDto>` | management / Moderator · `moderation:note:write` |
| DELETE | `/notes/{noteId:long}` | — | 204 | management / Moderator · `moderation:note:write` |
| POST | `/users/{userId}/standing` | `SetModerationStandingRequest` | `StatusResponseDto<ModerationStandingDto>` | management / SuperMod · `moderation:suspicioususer:write` ⁴ |
| DELETE | `/users/{userId}/standing` | `provider` (query) | 204 | management / SuperMod · `moderation:suspicioususer:write` ⁴ |
| GET | `/shared-bans` | — | `StatusResponseDto<SharedBanSettingsDto>` | management / SuperMod · `moderation:sharedban:read` |
| PUT | `/shared-bans` | `SaveSharedBanSettingsRequest` | `StatusResponseDto<SharedBanSettingsDto>` | management / SuperMod · `moderation:sharedban:write` |
| POST | `/shared-bans/trusted` | `AddTrustedChannelRequest` | `StatusResponseDto<SharedBanTrustedChannelDto>` (201) | management / SuperMod · `moderation:sharedban:write` |
| DELETE | `/shared-bans/trusted/{trustedChannelId:guid}` | — | 204 | management / SuperMod · `moderation:sharedban:write` |
| POST | `/nuke` | `NetworkNukeRequest` | `StatusResponseDto<NetworkNukeBatchDto>` | management / SuperMod · `moderation:nuke` ² |
| POST | `/nuke/{batchId:guid}/revert` | — | `StatusResponseDto<NetworkNukeBatchDto>` | management / SuperMod · `moderation:nuke` ² |
| GET | `/nuke` | `PaginationParams` (query) | `PaginatedResponse<NetworkNukeBatchDto>` | management / Moderator · `moderation:nuke:read` |
| POST | `/users/{targetUserId:guid}/evidence-packet` | `EvidencePacketRequest` | `StatusResponseDto<EvidencePacketDto>` | management / Moderator · `moderation:evidence:build` |
| GET | `/chat/settings` | — | `StatusResponseDto<ChatSettingsDto>` | management / Moderator · `moderation:chat:settings:read` |
| PATCH | `/chat/settings` | `UpdateChatSettingsRequest` | `StatusResponseDto<ChatSettingsDto>` | management / Moderator · `moderation:chat:settings:write` |
| GET | `/shield-mode` | — | `StatusResponseDto<ShieldModeStatusDto>` | management / Moderator · `moderation:shieldmode:read` |
| PUT | `/shield-mode` | `SetShieldModeRequest` | `StatusResponseDto<ShieldModeStatusDto>` | management / SuperMod · `moderation:shieldmode:write` |
| POST | `/chat/announcements` | `SendAnnouncementRequest` | 204 | management / Moderator · `moderation:announce` |
| PUT | `/chat/color` | `SetChatColorRequest` | `StatusResponseDto<string>` | management / Editor · `moderation:chatcolor:write` |
| POST | `/vips/{targetTwitchUserId}` | — | 204 | management / Broadcaster · `moderation:vip:write` |
| DELETE | `/vips/{targetTwitchUserId}` | — | 204 | management / Broadcaster · `moderation:vip:write` |
| DELETE | `/moderators/{targetTwitchUserId}` | — | 204 | management / Broadcaster · `moderation:moderator:write` ³ |
| GET | `/unban-requests` | `UnbanRequestQuery` (query) | `PaginatedResponse<UnbanRequestDto>` | management / Moderator · `moderation:unbanrequest:read` |
| PATCH | `/unban-requests` | `ResolveUnbanRequestRequest` | `StatusResponseDto<UnbanRequestDto>` | management / SuperMod · `moderation:unbanrequest:resolve` |
| PUT | `/blocks/{targetTwitchUserId}` | — | 204 | management / SuperMod · `moderation:blocklist:write` |
| DELETE | `/blocks/{targetTwitchUserId}` | — | 204 | management / SuperMod · `moderation:blocklist:write` |
| PUT | `/suspicious-users` | `UpdateSuspiciousUserRequest` | 204 | management / SuperMod · `moderation:suspicioususer:write` |

¹ Viewer-report *filing* is also reachable from a public/viewer surface; the dashboard endpoint here is mod-facing. A separate public submit path (if any) lives in the Community/public subsystem, not here.
² Network-nuke + shared-ban writes are **SuperMod tier** (design: "Risky → super-mod tier only"). Beyond Gate 2, the service re-verifies the floor in-process via `IRoleResolver.ResolveEffectiveLevelAsync ≥ SuperMod(20)` — never trust the gate alone (defense in depth; existing cross-tenant IDOR is a tracked live defect).
³ VIP grant/removal (`moderation:vip:write`) and moderator removal (`moderation:moderator:write`) mutate the channel's Twitch role directory and floor at **Broadcaster(40)** — these are management-ladder changes the owner delegates per-user, never raised on a role tier. `moderation:moderator:write` is **Critical / not permit-grantable** (mirrors `roles:manage`); `moderation:vip:write` is reversible and **Low / permit-grantable**. Shield Mode *write* floors at **SuperMod(20)** (emergency lockdown, beside `moderation:automod:write`); its *read* and chat-settings/announce stay at **Moderator(10)**; bot chat-color is config-tier **Editor(30)**.

⁴ `{userId}` on the standing rows is the **platform** user id (`ChannelModerationStanding.UserId`, varchar 64) paired with its `provider` — not the surrogate `Users.Id` guid the other per-user rows take (a standing targets one platform identity). Both rows reuse the existing `moderation:suspicioususer:write` action key verbatim: same per-user-treatment surface, same **SuperMod** floor, no new `ActionDefinitions` seed.

**Thin request records added for controller binding** (namespace `NomNomzBot.Application.DTOs.Moderation`):

```csharp
// TimeoutUserRequest + BanUserRequest are the canonical records in Contracts.Twitch (twitch-helix.md §4.1); the
// moderation controller binds those directly — NOT redeclared here (single owner, no duplicate).
public sealed record DeleteMessageRequest(string MessageId, Guid TargetUserId);
public sealed record WarnUserRequest(Guid TargetUserId, string Reason);
public sealed record SetReportStatusRequest(ViewerReportStatus Status);
public sealed record SetNotePinnedRequest(bool Pinned);
public sealed record SetModerationStandingRequest(string Provider, string Standing, string? Reason);   // standing ∈ muted|shadowbanned|blacklisted
public sealed record AddTrustedChannelRequest(Guid TrustedChannelId);
public sealed record SetShieldModeRequest(bool IsActive);
public sealed record SetChatColorRequest(string Color);   // bot's own chat color: blue/green/orange/… or hex (Prime/Turbo)
```

> The existing free-form `shield`, `blocked-terms`, `stats` actions on the current controller move to: `blocked-terms` → `ChatFilter(blocklist)` (J.6) plus the Twitch-native blocked-terms push (`IAutoModConfigService.SaveConfigAsync` → Helix `POST/DELETE /moderation/blocked_terms`, §3.2); `stats` → a `GetActionsAsync`-derived aggregate (or `IDashboardService`); `shield` → the first-class `IChatControlService.SetShieldModeAsync`/`GetShieldModeAsync` (§3.9, Helix Shield Mode via `ITwitchModerationApi`), persisted as the `AutoModConfigs.ShieldModeActive` cache column (J.7), NOT a `Configuration` key. These are migrations, not new surface — fold them in when porting the controller off `string` ids.

---

## 6. Pipeline actions

Moderation pipeline actions already exist (`BanAction` Type `"ban"`, `TimeoutAction` Type `"timeout"`). They implement the **single canonical `ICommandAction`** defined in `commands-pipelines.md` §3.13 (`Application/Pipeline`): `string Type` (+ `Category`/`Description`); `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`, reading params from `context.Parameters`. (The pre-consolidation Infrastructure shape — `ActionType`/`ExecuteAsync(PipelineExecutionContext, ActionDefinition)` — is collapsed away per commands-pipelines §0; `BanAction`/`TimeoutAction` re-target the canonical contract.) They currently call `IChatProvider` directly; the spec's persistence/audit happens because those provider calls should route through `IModerationService` so a `ModerationAction` row + events are written. New actions in `NomNomzBot.Infrastructure/Pipeline/Actions/`:

| Type string | Config keys (`context.Parameters`) | Behavior |
|-------------|----------------------------------------|----------|
| `ban` *(exists)* | `user_id`, `reason` | Resolve target → `IModerationService.BanAsync`. Writes J.2, fires events. (Re-point from raw `IChatProvider`.) |
| `timeout` *(exists)* | `user_id`, `duration` (default 60), `reason` | Resolve target → `IModerationService.TimeoutAsync`. |
| `delete_message` *(new)* | `message_id`, `user_id` | `IModerationService.DeleteMessageAsync`. |
| `warn` *(new)* | `user_id`, `reason` | `IModerationService.WarnAsync`. |
| `add_chat_filter_hit` *(new)* | `filter_id` | Increments `ChatFilter.MatchCount` (J.6) + may `EnqueueAsync` a `bot_flag` queue item. Used by the automod engine's pipeline path. |
| `apply_heat` *(new)* | `user_id`, `delta` | Triggers `IModerationProjectionService.RecomputeTrustAsync` after a heat-bearing event; may fire `UserHeatThresholdCrossedEvent`. |

Each new action: `Type` = the snake_case string above; resolve `user_id`/`message_id` from `context.Parameters` then `context.Variables` (`target.id` → `user.id`) exactly like the re-targeted `BanAction`; return `ActionResult.Success/Failure`. Register in `InfrastructureServiceExtensions`/`DependencyInjection` action list (where `BanAction`/`TimeoutAction` are registered).

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs` (where `IModerationService`→`ModerationService` and `AutoModerationEngine` already register, lines ~189/203). All **Scoped** (per-request, DbContext-bound) except the stateless calculator. Profile-adapter variants are selected by `DeploymentProfile`/`App__DeploymentMode` exactly as the cache/bus/executor adapters.

```csharp
// Core moderation services — Scoped (DbContext / IUnitOfWork lifetime)
services.AddScoped<IModerationService, ModerationService>();                 // extend existing registration
services.AddScoped<IAutoModConfigService, AutoModConfigService>();
services.AddScoped<IModerationQueueService, ModerationQueueService>();
services.AddScoped<INetworkNukeService, NetworkNukeService>();
services.AddScoped<ISharedBanService, SharedBanService>();
services.AddScoped<IViewerReportService, ViewerReportService>();
services.AddScoped<IUserContextService, UserContextService>();
services.AddScoped<IModerationProjectionService, ModerationProjectionService>();
services.AddScoped<IChatControlService, ChatControlService>();               // Group B — chat/channel controls (Helix-relayed)
services.AddScoped<IModerationDirectoryService, ModerationDirectoryService>(); // Group C — VIP/mod/unban-request/block/suspicious
services.AddScoped<IModerationEscalationService, ModerationEscalationService>(); // auto-mod escalation ladder (J.10/J.11)

// AutoMod engine (exists) + new pipeline actions — registered alongside BanAction/TimeoutAction
services.AddScoped<AutoModerationEngine>();                                   // existing
services.AddScoped<ICommandAction, DeleteMessageAction>();
services.AddScoped<ICommandAction, WarnAction>();
services.AddScoped<ICommandAction, ChatFilterHitAction>();
services.AddScoped<ICommandAction, ApplyHeatAction>();

// Event handlers maintaining projections (Scoped, resolved per publish)
services.AddScoped<IEventHandler<ModerationActionAppliedEvent>, ModerationProjectionHandler>();
services.AddScoped<IEventHandler<ModerationActionRevertedEvent>, ModerationProjectionHandler>();
services.AddScoped<IEventHandler<ViewerReportFiledEvent>, ViewerReportQueueLinkHandler>();
```

**Deployment-profile adapter variants** (selected in the `App__DeploymentMode` switch, mirroring the existing DB/cache/bus/executor adapters):

| Capability | lite (self-host) | full / SaaS |
|-----------|------------------|-------------|
| Shared-ban cross-instance delivery (`ISharedBanService` outbound + inbound) | in-process `IEventBus` only; **federation outbound disabled** unless a peer is configured (federation is feature-gated) | federation event-bus (signed `SharedChatBanIssuedEvent` over `RedisEventBus` + `FederationPeers`) |
| Network-nuke fan-out target resolution | channels in the local DB the actor mods | local DB; cross-instance partner legs only via federation when enabled |
| Held-message expiry sweep (`IModerationQueueService.ExpireStaleAsync`) | `BackgroundService` + `PeriodicTimer` (single-instance safe) | same, behind `IRunOnceGuard` (no double-fire on multi-node) |

The eleven core service interfaces have **one implementation each** regardless of profile (they call profile-agnostic abstractions: `ITwitchHelixClient`, `IEventBus`, `IUnitOfWork`); only the federation/sweep edges are adapter-swapped, so no second impl set is needed. `IModerationEscalationService` is likewise profile-agnostic (pure DB + `IModerationService` delegation). `IChatControlService` and `IModerationDirectoryService` are pure Helix relays (Twitch is the system of record) — profile-agnostic, no adapter variants.

---

## 8. Dependencies (from the stack doc)

This subsystem uses **only second-party + already-present** packages — **zero new third-party deps**:

- **Microsoft.EntityFrameworkCore 10.0.9** (+ provider via adapter: `Microsoft.EntityFrameworkCore.Sqlite` lite / `Npgsql.EntityFrameworkCore.PostgreSQL` SaaS) — all J.* persistence, soft-delete + tenant **named query filters** (EF10), `[VC:JSON]` converters on `ChatFilters.Terms`/`LinkPolicyJson`, `AutoModConfigs.CategoryLevelsJson`, `ModerationAuditLog.MetadataJson` via hand-rolled `ValueConverter<T,string>` + `ValueComparer` (Newtonsoft.Json per schema §1.4).
- **`ITwitchHelixClient`** (hand-rolled typed Helix client; uses **Microsoft.Extensions.Http.Resilience 10.7.0** + custom rate-limit `DelegatingHandler`) — ban/unban/timeout/delete-message/banned-list + the `ITwitchModerationApi`/`ITwitchChannelsApi` legs this subsystem drives: warn, automod held-message ALLOW/DENY, blocked-terms CRUD, VIP add/remove, moderator remove, unban-requests, blocks, suspicious-users, plus chat-settings/shield-mode/announcements/chat-color + Twitch-native automod level (`twitch-helix.md` §3.2/§3.3). No `TwitchLib`.
- **`IEventBus`** (in-process `EventBus` lite / thin `RedisEventBus` over StackExchange.Redis **2.13.17** SaaS) — domain events incl. cross-instance `SharedChatBanIssuedEvent`.
- **Background processing** — in-box `BackgroundService` + `PeriodicTimer` for the held-message TTL sweep; `IRunOnceGuard` (no-op lite / `pg_try_advisory_lock` SaaS) for multi-node.
- **In-box `System.Security.Cryptography`** — only indirectly, via the token vault behind `ITwitchHelixClient`; this subsystem holds no `[PII-shred]` columns of its own (Twitch ids are `[PII-hash]`, content is `[PII-scrub]` — row-level scrub, not crypto-shred).
- **Validation** — in-box **.NET 10 `AddValidation()`** source generator on request records; async/uniqueness rules in the service layer returning `Result<T>`.
- **Existing `TrustScoreCalculator`** (1st-party, `NomNomzBot.Infrastructure.Services.Trust`) — reused by `IModerationProjectionService.RecomputeTrustAsync`; **not** re-implemented.
- **Testing** — xunit.v3 3.2.2, NSubstitute 5.3.0, AwesomeAssertions 9.4.0; SQLite in-memory for service tests; Testcontainers Postgres only for the RLS-isolation subset (cross-tenant IDOR on `/moderation/*`).

---

## 9. Decisions (resolved)

1. **Authorization is the canonical Gate 2 — no moderation-local mechanism.** Every §5 endpoint gates via `IActionAuthorizationService.AuthorizeActionAsync(userId, channelId, actionKey)` against its `moderation:*` `ActionDefinitions` (B.3) row, resolved through `IRoleResolver` (`roles-permissions.md` §0/§3.3). This is a hard **dependency** on the roles/permissions subsystem, not an interim or parallel attribute — the `moderation:*` action keys listed in §5 are that subsystem's seed catalog for this domain.

2. **"Active Shared Chat session" verification source.** `ISharedBanService.ApplyInboundSharedBanAsync` reads active-session state from an EventSub-owned shared-chat session projection; moderation only consumes it. Twitch exposes shared-chat session state via EventSub `channel.shared_chat.begin`/`channel.shared_chat.update`/`channel.shared_chat.end`; the EventSub/Twitch subsystem persists current session membership as a one-row-per-active-session projection (`SharedChatSessions`) that this subsystem queries at apply time. That projection is **not** part of Domain J — it is owned by the EventSub/Twitch subsystem, and moderation's only coupling to it is the read at `ApplyInboundSharedBanAsync`. This is a **dependency** on the EventSub subsystem owning and populating that projection.

3. **Bot-side standing tiers (muted / shadowbanned / blacklisted).** `ChannelModerationStanding` (J.12) is the graduated bot-side ignore axis; an absent row means normal, and the broadcaster can never be assigned a standing. **Semantics:** `muted` — the bot ignores the user's interactions (commands, chat triggers, session-first-message welcome, poll votes, giveaway keyword entries, chat currency/engagement earning; chat song requests are covered because they are commands) while their chat still displays, persists, and folds into analytics. `shadowbanned` — everything `muted` does, PLUS the user's lines are excluded from bot-driven public overlay surfaces (the overlay event filter never pushes them). `blacklisted` — the user's chat events are DROPPED at the publisher seams (Twitch EventSub translation, the YouTube live-chat poll publisher, the Kick webhook ingest) before the bus fan-out: no persistence, no dashboard display, no feature sees them. **Enforcement placement:** blacklist at the 3 publishers; mute/shadowban as a guard at the top of the 4 feature subscribers (`ChatMessageHandler`, `ChatEarningHandler`, `EngagementChatActivityHandler`, `GiveawayKeywordListener`) reading a per-channel in-memory standing map on `ChannelContext` (loaded by `ChannelRegistry`, invalidated on standing writes) — never a per-message DB read; shadowban's overlay exclusion lives in the overlay event filter. **Separation from Twitch-native:** Twitch-native ban/timeout remain the only Twitch-visible punishments; bot-side standing never calls Helix. Standing is per-platform (a Twitch mute does not mute the same human's Kick identity). Every standing write's audit is a SYSTEM `UserNote` (J.3) riding the existing notes surface — no new domain event.
