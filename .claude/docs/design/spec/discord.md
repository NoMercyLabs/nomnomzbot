# Discord Subsystem — Interface Specification

**Status:** Implementable. Code from this directly.
**Scope:** Per-channel Discord guild link (both-opt-in handshake), encrypted bot token storage, notification rules (event → Discord channel → template), member opt-in roles, and the dispatch + dedupe log.
**Grounds:** locked schema `docs/design/2026-06-16-database-schema.md` §P.10; design `docs/design/2026-06-16-discord-notifications.md`; stack `docs/design/2026-06-16-stack-and-dependencies.md`; decisions `docs/design/2026-06-16-decisions-pending-confirmation.md`.

## Conventions inherited (binding)

- Namespace `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way, `Result<T>` over exceptions/null.
- Surrogate PK `Id guid` via `Guid.CreateVersion7()` (UUIDv7), app-assigned at insert — never `Guid.NewGuid()`, never DB-default. **Append-only** dispatch table excepted (see §1).
- Tenant key `BroadcasterId guid` (FK→`Channels.Id`) on every table, denormalized onto every child; EF global query filter + Postgres RLS apply.
- External Discord ids (`GuildId`, `DiscordRoleId`, `DiscordMemberId`, channel ids, message ids) are `string(50)` **indexed attribute columns, never keys**.
- Soft-delete = `IsDeleted` predicate via `DeletedAt` (entities inherit `SoftDeletableEntity`); append-only carries `CreatedAt` only.
- Repository + `IUnitOfWork`; controllers never touch `DbContext`. Responses `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]`.
- App JSON via **Newtonsoft.Json** (the `EmbedConfig` `[VC:JSON]` converter and all DTO bodies). Bot OAuth token persistence goes through `IIntegrationTokenVault` (`identity-auth.md` §3.4) — the canonical crypto-shred-ready vault over `ISubjectKeyService` + `IFieldCipher` (AES-256-GCM AEAD). **This subsystem never hand-rolls crypto and never touches `IFieldCipher`/`ISubjectKeyService` directly**; it calls the vault, which stores/returns/shreds the ciphertext. (The legacy `IEncryptionService` AES-CBC adapter is retired per `gdpr-crypto.md` §9 — do not target it.)

> **Replaces** the live `DiscordServerAuthorization` entity (int PK, `string(50) BroadcasterId`, ad-hoc `Status`/`ApprovedBy`) and the inline OAuth/persist logic in `IntegrationOAuthController.HandleDiscordCallback`. The OAuth callback keeps its route but delegates persistence to `IDiscordGuildService` (no behavior inline). All five P.10 tables are net-new on the guid/UUIDv7 rebuild.

---

## 1. Entities (locked schema — §P.10; do not redefine, reference only)

All entities live in `NomNomzBot.Domain.Entities`. Types/keys are **as locked in the schema**; restated here only as the field surface the owner binds against.

### `DiscordGuildConnection : SoftDeletableEntity` — both-opt-in handshake (supersedes `DiscordServerAuthorization`)
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildId string(50) Index`; `GuildName string(255) Null` **[PII-scrub]**; `BotInstalled bool`; `ServerConsentStatus string(20)` (`pending`|`approved`|`revoked`) [VC:enum]; `ApprovedByDiscordUserId string(50) Null` **[PII-hash]**; `ApprovedAt timestamp Null`; `StreamerEnabled bool`. **Unique** `(BroadcasterId, GuildId)`. Bot OAuth token is **not** a column here — it lives in `IntegrationTokens` (E.2) under an `IntegrationConnections` (E.1) row with `Provider='discord'`, `ProviderAccountId=GuildId`, `BroadcasterId` matching, crypto-shred-ready. The owning `IntegrationConnection.Id` (returned by `IIntegrationTokenVault.UpsertConnectionAsync`) is the persistence handle the service uses for `StoreTokensAsync` (write) and `RevokeConnectionAsync` (disconnect); it is resolved by the `(BroadcasterId, Provider='discord', ProviderAccountId=GuildId)` unique key, so no extra column is required on this entity.

### `DiscordNotificationConfig : SoftDeletableEntity` — one rule per (guild, trigger)
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `TriggerType string(30) Index` (`go_live`|`new_clip`|`schedule`|`milestone`) [VC:enum]; `Enabled bool`; `TargetChannelId string(50)`; `PingRoleId guid FK→DiscordNotificationRole Null Index`; `MessageTemplate text Null`; `EmbedConfig text Null` **[VC:JSON]**; `MilestoneType string(20) Null`; `MilestoneThreshold int Null`; `ConfigSchemaVersion int` (default 1; upcast anchor for `EmbedConfig` — consumed on read by `IDiscordNotificationConfigService`, §3.2). **Unique** `(GuildConnectionId, TriggerType)`.

### `DiscordNotificationRole : SoftDeletableEntity` — per-streamer self-assign notify role
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `DiscordRoleId string(50) Index`; `RoleName string(255) Null`; `SelfAssignEnabled bool`; `ButtonMessageId string(50) Null`; `ButtonChannelId string(50) Null`. **Unique** `(GuildConnectionId, DiscordRoleId)`.

### `DiscordMemberOptIn : SoftDeletableEntity` — who gets pinged
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `NotificationRoleId guid FK→DiscordNotificationRole Index`; `DiscordMemberId string(50) Index` **[PII-hash]**; `OptInSource string(20)` (`manual_role`|`command`|`button`) [VC:enum]; `OptedInAt timestamp`; `OptedOutAt timestamp Null`. **Unique** `(NotificationRoleId, DiscordMemberId)`.

### `DiscordNotificationDispatch` — **[APPEND-ONLY]** dispatch + dedupe log
`Id guid PK` (UUIDv7, app-assigned; append-only carries `CreatedAt` only — no `UpdatedAt`/`DeletedAt`); `BroadcasterId guid FK→Channels Index`; `NotificationConfigId guid FK→DiscordNotificationConfig Index`; `TriggerType string(30)`; `DedupeKey string(255) Index`; `StreamId guid FK→Streams Null Index`; `PostedMessageId string(50) Null`; `Status string(20)` (`sent`|`failed`|`skipped_dupe`) [VC:enum]; `Error text Null`; `DispatchedAt timestamp Index`. **Unique** `(NotificationConfigId, DedupeKey)` — the DB-level dedupe guarantee: one post per go-live.

> **Dedupe key contract:** `DedupeKey = $"{TriggerType}:{StreamId:N}"` for `go_live` (one row per stream session); for `milestone`, `$"milestone:{MilestoneType}:{MilestoneThreshold}"`; for `new_clip`, `$"clip:{ClipId}"`; for `schedule`, `$"schedule:{ScheduledSegmentId}"`. The unique index makes a duplicate insert the dedupe mechanism (catch the unique-violation → `skipped_dupe`).

> **Ping-role cardinality (schema C4):** the locked schema keeps `DiscordNotificationConfig.PingRoleId` as a **single nullable FK** — one ping role per rule. This spec implements exactly that. Multi-role tiered ping is outside this subsystem's surface: it is a distinct schema (`DiscordNotificationConfigRoles` join table) that this spec does not define, and is not built against this locked schema (see §9).

EF configurations go in `NomNomzBot.Infrastructure/Persistence/Configurations/Discord*Configuration.cs` (one per entity, replacing `DiscordServerAuthorizationConfiguration.cs`). `EmbedConfig` uses the hand-rolled `ValueConverter<DiscordEmbedDto, string>` + `ValueComparer` (Newtonsoft.Json) per stack §Persistence — **no `HasColumnType("jsonb")`**.

---

## 2. Domain events

All in `NomNomzBot.Domain.Events`, `sealed`, inherit the canonical `DomainEventBase` (platform-conventions.md §2.0 — `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt`). Published via `IEventBus`. Members are `required ... init`. Events **do not redeclare** `EventId` / `BroadcasterId` / `OccurredAt` — they add only their own payload fields; the publisher sets the inherited `Guid BroadcasterId` to the tenant the event belongs to.

> Tenant key on every event below is the inherited `Guid BroadcasterId` (matches widened `ITenantScoped`). Every Discord event here is tenant-scoped: the publishing service sets `BroadcasterId` to the owning channel — it is **never** left at the default `Guid.Empty`. Discord ids ride alongside as `string` where a handler needs them for the Discord API.

```csharp
namespace NomNomzBot.Domain.Events;

/// <summary>Published when a Discord guild reaches both-opt-in (server approved AND streamer enabled). Triggers notification-role / button provisioning.</summary>
// Publisher (IDiscordGuildService) sets inherited Guid BroadcasterId = the linked channel; tenant-scoped, never Guid.Empty.
public sealed record DiscordGuildLinkedEvent : DomainEventBase
{
    public required Guid GuildConnectionId { get; init; }
    public required string GuildId { get; init; }
    public required string GuildName { get; init; }
}

/// <summary>Published when a guild link is revoked by server admin OR disabled by streamer (no longer both-opt-in). Consumers stop dispatching to it.</summary>
// Publisher (IDiscordGuildService) sets inherited Guid BroadcasterId = the unlinked channel; tenant-scoped, never Guid.Empty.
public sealed record DiscordGuildUnlinkedEvent : DomainEventBase
{
    public required Guid GuildConnectionId { get; init; }
    public required string GuildId { get; init; }
    public required string Reason { get; init; } // "server_revoked" | "streamer_disabled" | "disconnected"
}

/// <summary>Published after a notification is posted to Discord (or deduped/failed). Mirrors the appended DiscordNotificationDispatch row for SignalR dashboard feed + audit.</summary>
// Publisher (IDiscordNotificationDispatcher) sets inherited Guid BroadcasterId = the dispatching channel; tenant-scoped, never Guid.Empty.
public sealed record DiscordNotificationDispatchedEvent : DomainEventBase
{
    public required Guid DispatchId { get; init; }
    public required Guid NotificationConfigId { get; init; }
    public required string TriggerType { get; init; }
    public required string DedupeKey { get; init; }
    public required string Status { get; init; } // "sent" | "failed" | "skipped_dupe"
    public string? PostedMessageId { get; init; }
    public string? Error { get; init; }
}

/// <summary>Published when a member self-assigns/removes a notify role (command/button/role sync). Drives opt-in count refresh.</summary>
// Publisher (IDiscordNotificationRoleService) sets inherited Guid BroadcasterId = the role's channel; tenant-scoped, never Guid.Empty.
public sealed record DiscordMemberOptInChangedEvent : DomainEventBase
{
    public required Guid NotificationRoleId { get; init; }
    public required string DiscordMemberId { get; init; }
    public required bool OptedIn { get; init; }
    public required string Source { get; init; } // "manual_role" | "command" | "button"
}
```

**Consumed (not owned)** by this subsystem's dispatch handler: `ChannelOnlineEvent` (existing, §`NomNomzBot.Domain.Events.ChannelOnlineEvent`) is the `go_live` trigger source. The handler `DiscordGoLiveNotificationHandler : IEventHandler<ChannelOnlineEvent>` lives in Infrastructure and calls `IDiscordNotificationDispatcher`.

---

## 3. Service interfaces

All interfaces in `NomNomzBot.Application.Contracts.Discord`. Implementations in `NomNomzBot.Infrastructure.Services.Discord`. All methods async, return `Result` / `Result<T>`, take `CancellationToken ct = default` last. `BroadcasterId` is `Guid` throughout.

### 3.1 `IDiscordGuildService` — both-opt-in handshake + connection lifecycle

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

public interface IDiscordGuildService
{
    // Lists every guild link for the tenant (any consent state). Read-only; no side effects.
    Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Single connection by id, tenant-scoped. NOT_FOUND if absent or other-tenant.
    Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);

    // Upserts the connection after the Discord bot-install OAuth callback: creates/updates the
    // (BroadcasterId,GuildId) row, sets BotInstalled=true and ServerConsentStatus per server approval,
    // and persists the bot OAuth token via IIntegrationTokenVault (identity-auth.md §3.4) in two steps —
    //   1. UpsertConnectionAsync(new UpsertConnectionDto(BroadcasterId, Provider:"discord",
    //      ProviderAccountId: oauth.GuildId, ProviderAccountName: oauth.GuildName, oauth.Scopes, ...)) → connectionId;
    //   2. StoreTokensAsync(connectionId, new StoreTokensDto(oauth.AccessToken, oauth.RefreshToken, AppToken:null, oauth.ExpiresAt), ct).
    // Records connectionId on the row (so Disconnect can find it). Side effect: if this completes both-opt-in,
    // publishes DiscordGuildLinkedEvent. ALREADY_EXISTS-safe (idempotent upsert; the vault upsert is idempotent too).
    Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
        Guid broadcasterId, DiscordGuildOAuthResult oauth, CancellationToken ct = default);

    // Server-admin side of consent. Sets ServerConsentStatus=approved + ApprovedByDiscordUserId + ApprovedAt.
    // If StreamerEnabled already true → both-opt-in reached → publishes DiscordGuildLinkedEvent. Persists via IUnitOfWork.
    Task<Result> ApproveServerConsentAsync(
        Guid broadcasterId, Guid connectionId, string approvedByDiscordUserId, CancellationToken ct = default);

    // Server-admin revoke. Sets ServerConsentStatus=revoked. Breaks both-opt-in → publishes DiscordGuildUnlinkedEvent("server_revoked").
    Task<Result> RevokeServerConsentAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);

    // Streamer side of consent (the dashboard toggle). Sets StreamerEnabled.
    // true + server approved → both-opt-in → DiscordGuildLinkedEvent; false → DiscordGuildUnlinkedEvent("streamer_disabled").
    Task<Result> SetStreamerEnabledAsync(
        Guid broadcasterId, Guid connectionId, bool enabled, CancellationToken ct = default);

    // Full disconnect: soft-deletes the connection + its configs/roles (cascade soft-delete), revokes the bot
    // token via IIntegrationTokenVault.RevokeConnectionAsync(connectionId, "discord_disconnected", ct)
    // (soft-deletes IntegrationTokens, Status=revoked, best-effort provider-side revoke), publishes
    // DiscordGuildUnlinkedEvent("disconnected"). Idempotent.
    Task<Result> DisconnectAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);

    // True iff ServerConsentStatus=approved AND StreamerEnabled AND not soft-deleted. The single gate the
    // dispatcher checks before posting. No side effects.
    Task<Result<bool>> IsLinkActiveAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);
}
```

### 3.2 `IDiscordNotificationConfigService` — notification rules (event → channel → template)

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

public interface IDiscordNotificationConfigService
{
    // All rules for one guild connection, tenant-scoped. Read-only.
    Task<Result<IReadOnlyList<DiscordNotificationConfigDto>>> GetConfigsAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);

    // Creates a rule for (GuildConnectionId, TriggerType). ALREADY_EXISTS if that pair exists (unique).
    // Validates: TargetChannelId non-empty; PingRoleId (if set) belongs to same connection; milestone fields
    // present iff TriggerType=milestone. Persists via IUnitOfWork; assigns Id via Guid.CreateVersion7().
    Task<Result<DiscordNotificationConfigDto>> CreateConfigAsync(
        Guid broadcasterId, Guid connectionId, CreateDiscordNotificationConfigRequest request, CancellationToken ct = default);

    // Updates an existing rule (Enabled, TargetChannelId, PingRoleId, MessageTemplate, EmbedConfig, milestone).
    // NOT_FOUND if absent/other-tenant. Re-validates as Create. Bumps UpdatedAt.
    Task<Result<DiscordNotificationConfigDto>> UpdateConfigAsync(
        Guid broadcasterId, Guid configId, UpdateDiscordNotificationConfigRequest request, CancellationToken ct = default);

    // Soft-deletes the rule (sets DeletedAt). NOT_FOUND if absent.
    Task<Result> DeleteConfigAsync(
        Guid broadcasterId, Guid configId, CancellationToken ct = default);

    // Renders MessageTemplate + EmbedConfig against sample data and returns the resolved preview WITHOUT
    // posting to Discord. Pure; used by the dashboard "preview" button. No state change.
    Task<Result<DiscordNotificationPreviewDto>> PreviewAsync(
        Guid broadcasterId, Guid configId, CancellationToken ct = default);
}
```

**`ConfigSchemaVersion` upcasting (binding — mirrors event-store §3.6 on the config side).** `ConfigSchemaVersion` is the per-row upcast anchor for the `[VC:JSON]` `EmbedConfig` blob (schema audit B4). `IDiscordNotificationConfigService` is its **only** consumer:

- A `const int CurrentEmbedConfigVersion = 1;` lives on the service (the single source of truth for the shape `DiscordEmbedDto` currently maps).
- On **read** (`GetConfigsAsync`, `PreviewAsync`, and any dispatcher fetch via the same loader path), when a row's `ConfigSchemaVersion < CurrentEmbedConfigVersion` the service forward-migrates (upcasts) the stored `EmbedConfig` JSON to the current shape **before** it deserializes to `DiscordEmbedDto`, chaining per-version upcast steps (`v1→v2→…`) until it reaches `CurrentEmbedConfigVersion`. Callers and the dispatcher only ever see the current shape.
- The upgraded row is **persisted on next write of that row** (`UpdateConfigAsync` writes `ConfigSchemaVersion = CurrentEmbedConfigVersion` alongside the new `EmbedConfig`, via `IUnitOfWork`); read paths are pure and never write. Old rows keep their stored version until then — never a bulk JSON data migration.
- **Additive changes do not bump the version.** Newtonsoft tolerates missing/extra members, so adding an optional `DiscordEmbedDto` field deserializes old rows with no upcaster and no version change. Only a **breaking** `EmbedConfig` reshape raises `CurrentEmbedConfigVersion` and adds the matching upcast step.

### 3.3 `IDiscordNotificationRoleService` — self-assign notify roles + opt-in management

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

public interface IDiscordNotificationRoleService
{
    // All notify roles for a connection (with live opt-in counts). Read-only.
    Task<Result<IReadOnlyList<DiscordNotificationRoleDto>>> GetRolesAsync(
        Guid broadcasterId, Guid connectionId, CancellationToken ct = default);

    // Registers a Discord role as the per-streamer notify role. ALREADY_EXISTS if (GuildConnectionId,DiscordRoleId)
    // exists. Persists via IUnitOfWork. Does NOT post the button (see PostOptInButtonAsync).
    Task<Result<DiscordNotificationRoleDto>> CreateRoleAsync(
        Guid broadcasterId, Guid connectionId, CreateDiscordNotificationRoleRequest request, CancellationToken ct = default);

    // Updates RoleName / SelfAssignEnabled. NOT_FOUND if absent.
    Task<Result<DiscordNotificationRoleDto>> UpdateRoleAsync(
        Guid broadcasterId, Guid roleId, UpdateDiscordNotificationRoleRequest request, CancellationToken ct = default);

    // Soft-deletes the notify role. NOT_FOUND if absent. Side effect: configs referencing it via PingRoleId have
    // PingRoleId nulled (FK is Null-able) in the same transaction.
    Task<Result> DeleteRoleAsync(
        Guid broadcasterId, Guid roleId, CancellationToken ct = default);

    // Posts (or re-posts) the bot button message to ButtonChannelId via IDiscordBotGateway; records returned
    // ButtonMessageId on the role row. Members click the button to toggle the role.
    Task<Result<DiscordNotificationRoleDto>> PostOptInButtonAsync(
        Guid broadcasterId, Guid roleId, string buttonChannelId, CancellationToken ct = default);

    // Records a member's opt-in (idempotent upsert on (NotificationRoleId,DiscordMemberId)); sets OptedInAt,
    // clears OptedOutAt; assigns the Discord role via IDiscordBotGateway; publishes DiscordMemberOptInChangedEvent(true).
    Task<Result> OptInMemberAsync(
        Guid broadcasterId, Guid roleId, string discordMemberId, string source, CancellationToken ct = default);

    // Records opt-out: sets OptedOutAt; removes the Discord role via IDiscordBotGateway;
    // publishes DiscordMemberOptInChangedEvent(false). NOT_FOUND if no opt-in row.
    Task<Result> OptOutMemberAsync(
        Guid broadcasterId, Guid roleId, string discordMemberId, string source, CancellationToken ct = default);
}
```

### 3.4 `IDiscordNotificationDispatcher` — dispatch + dedupe (Infrastructure-internal, no controller)

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

public interface IDiscordNotificationDispatcher
{
    // The core go-live/trigger path. For the matching enabled config(s) of the tenant+trigger:
    //  1. Gate: IsLinkActive (both-opt-in) — else append Status=skipped (no post).
    //  2. Atomic dedupe: insert DiscordNotificationDispatch with the computed DedupeKey; a unique-constraint
    //     violation on (NotificationConfigId,DedupeKey) → append Status=skipped_dupe, return Ok (no double post).
    //  3. Render template+embed, ping PingRole, post via IDiscordBotGateway.
    //  4. Persist outcome (PostedMessageId | Error, Status sent|failed) on the SAME appended row.
    //  5. Publish DiscordNotificationDispatchedEvent.
    // Returns the dispatch outcome. Never throws for a Discord-side failure (captured as Status=failed).
    Task<Result<DiscordDispatchOutcomeDto>> DispatchAsync(
        DiscordDispatchRequest request, CancellationToken ct = default);

    // Append-only dispatch history for a connection (paged), newest first. Read-only.
    Task<Result<PagedList<DiscordDispatchLogDto>>> GetDispatchLogAsync(
        Guid broadcasterId, Guid connectionId, int page, int pageSize, CancellationToken ct = default);
}
```

### 3.5 `IDiscordBotGateway` — Discord REST/gateway adapter (Infrastructure-internal; the only thing that talks to Discord)

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

// All methods read the tenant's decrypted bot token by resolving the discord IntegrationConnection
// ((BroadcasterId, Provider="discord")) and calling IIntegrationTokenVault.GetAccessTokenAsync(connectionId, ct)
// per call (identity-auth.md §3.4) — never a cached plaintext token; a crypto-shredded DEK surfaces as Result.Failure.
public interface IDiscordBotGateway
{
    // Posts a channel message (optionally embed + role ping) using the tenant's decrypted bot token.
    // Returns the Discord message id. Failure → Result.Failure (caller records Status=failed); never throws.
    Task<Result<string>> PostMessageAsync(
        Guid broadcasterId, string targetChannelId, DiscordOutboundMessage message, CancellationToken ct = default);

    // Posts the role self-assign button message; returns its message id.
    Task<Result<string>> PostButtonMessageAsync(
        Guid broadcasterId, string targetChannelId, DiscordOptInButton button, CancellationToken ct = default);

    // Adds/removes a guild role on a member (member opt-in/out enforcement).
    Task<Result> AddMemberRoleAsync(
        Guid broadcasterId, string guildId, string discordMemberId, string discordRoleId, CancellationToken ct = default);
    Task<Result> RemoveMemberRoleAsync(
        Guid broadcasterId, string guildId, string discordMemberId, string discordRoleId, CancellationToken ct = default);
}
```

---

## 4. DTOs / contracts

All in `NomNomzBot.Application.Contracts.Discord`. `record` types; Newtonsoft.Json on the wire. Ids are `Guid`; Discord ids are `string`. Enum-like fields are `string` (matches `[VC:enum]` storage) — validated against the allowed sets in §1.

```csharp
namespace NomNomzBot.Application.Contracts.Discord;

// ── Guild connection ────────────────────────────────────────────────────────
public sealed record DiscordGuildConnectionDto(
    Guid Id, Guid BroadcasterId, string GuildId, string? GuildName,
    bool BotInstalled, string ServerConsentStatus, string? ApprovedByDiscordUserId,
    DateTime? ApprovedAt, bool StreamerEnabled, bool IsLinkActive,
    DateTime CreatedAt, DateTime UpdatedAt);

// Carried out of the OAuth callback into IDiscordGuildService.UpsertFromOAuthAsync.
public sealed record DiscordGuildOAuthResult(
    string GuildId, string? GuildName, string AccessToken, string? RefreshToken,
    DateTime? ExpiresAt, IReadOnlyList<string> Scopes, string? InstalledByDiscordUserId);

// ── Notification config ─────────────────────────────────────────────────────
public sealed record DiscordNotificationConfigDto(
    Guid Id, Guid GuildConnectionId, string TriggerType, bool Enabled,
    string TargetChannelId, Guid? PingRoleId, string? MessageTemplate,
    DiscordEmbedDto? EmbedConfig, string? MilestoneType, int? MilestoneThreshold,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateDiscordNotificationConfigRequest(
    string TriggerType, bool Enabled, string TargetChannelId, Guid? PingRoleId,
    string? MessageTemplate, DiscordEmbedDto? EmbedConfig,
    string? MilestoneType, int? MilestoneThreshold);

public sealed record UpdateDiscordNotificationConfigRequest(
    bool Enabled, string TargetChannelId, Guid? PingRoleId,
    string? MessageTemplate, DiscordEmbedDto? EmbedConfig,
    string? MilestoneType, int? MilestoneThreshold);

// EmbedConfig [VC:JSON] shape — persisted via Newtonsoft converter on DiscordNotificationConfig.EmbedConfig.
public sealed record DiscordEmbedDto(
    string? Title, string? Description, string? Color, string? ThumbnailUrl,
    string? ImageUrl, string? FooterText, IReadOnlyList<DiscordEmbedFieldDto>? Fields);
public sealed record DiscordEmbedFieldDto(string Name, string Value, bool Inline);

public sealed record DiscordNotificationPreviewDto(
    string RenderedContent, DiscordEmbedDto? RenderedEmbed, string? PingRoleMention);

// ── Notify role + opt-in ────────────────────────────────────────────────────
public sealed record DiscordNotificationRoleDto(
    Guid Id, Guid GuildConnectionId, string DiscordRoleId, string? RoleName,
    bool SelfAssignEnabled, string? ButtonMessageId, string? ButtonChannelId,
    int OptInCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateDiscordNotificationRoleRequest(
    string DiscordRoleId, string? RoleName, bool SelfAssignEnabled);

public sealed record UpdateDiscordNotificationRoleRequest(
    string? RoleName, bool SelfAssignEnabled);

public sealed record DiscordMemberOptInRequest(string DiscordMemberId, string Source);

// ── Dispatch ────────────────────────────────────────────────────────────────
public sealed record DiscordDispatchRequest(
    Guid BroadcasterId, string TriggerType, string DedupeKey, Guid? StreamId,
    IReadOnlyDictionary<string, string> TemplateData);

public sealed record DiscordDispatchOutcomeDto(
    Guid DispatchId, string Status, string? PostedMessageId, string? Error);

public sealed record DiscordDispatchLogDto(
    Guid Id, Guid NotificationConfigId, string TriggerType, string DedupeKey,
    Guid? StreamId, string? PostedMessageId, string Status, string? Error,
    DateTime DispatchedAt);

// ── Gateway value objects (Infrastructure-internal payloads) ─────────────────
public sealed record DiscordOutboundMessage(
    string Content, DiscordEmbedDto? Embed, string? PingRoleId);
public sealed record DiscordOptInButton(
    string MessageContent, Guid NotificationRoleId, string ButtonLabel);
```

`PagedList<T>` is the existing `NomNomzBot.Application.Common.Models.PagedList<T>` (already consumed by `BaseController.GetPaginatedResponse`).

---

## 5. Controller endpoints

Controller `DiscordController : BaseController` in `NomNomzBot.Api.Controllers.V1`.
`[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/discord")]`, `[Authorize]`, `[Tags("Discord")]`. Tenant `channelId` (`Guid`) is resolved/authorized by `TenantResolutionMiddleware` + `IChannelAccessService` (caller may act on that tenant). Responses `StatusResponseDto<T>` (success) or `PaginatedResponse<T>` (logs). All actions take `CancellationToken ct`.

**Role gate** — all routes are **management plane (Plane B)**. `[Authorize]` + tenant resolution yields only **Gate 1** (pure entry — any authenticated caller, channel must exist); it **cannot** distinguish the write floor from the read floor. The per-route floor is enforced in **Gate 2** by calling `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)` (roles-permissions.md §3.3) on the action key in the table's **Action key** column **before** the service call — returning `FORBIDDEN` (403) when the caller's resolved `ChannelMemberships.LevelValue` is below the action's effective level. Writes floor **`SuperMod`** (level 20), reads (list/log/preview) floor **`Moderator`** (level 10), matching the design's "both sides consent; streamer/admin configures." Member opt-in/out endpoints carry the **write** key (`SuperMod`) — they push role changes into the guild; member self-service happens through the Discord button/command path, not this HTTP surface. Every floor is the action's seeded **default**; a broadcaster may raise it via `ChannelActionOverride` but not lower it past the seeded `FloorLevel`. The keys are seeded global `ActionDefinitions` (schema B.3) — see §7.

| # | Verb | Route (under base) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|------|--------------------|-------------|--------------|--------------------|
| 1 | GET | `/connections` | — | `StatusResponseDto<IReadOnlyList<DiscordGuildConnectionDto>>` | management / Moderator · `discord:connection:read` |
| 2 | GET | `/connections/{connectionId:guid}` | — | `StatusResponseDto<DiscordGuildConnectionDto>` | management / Moderator · `discord:connection:read` |
| 3 | POST | `/connections/{connectionId:guid}/server-consent` | `ServerConsentRequest(string ApprovedByDiscordUserId)` | `StatusResponseDto<object>` | management / SuperMod · `discord:connection:write` |
| 4 | DELETE | `/connections/{connectionId:guid}/server-consent` | — | `StatusResponseDto<object>` | management / SuperMod · `discord:connection:write` |
| 5 | PUT | `/connections/{connectionId:guid}/streamer-enabled` | `StreamerEnabledRequest(bool Enabled)` | `StatusResponseDto<object>` | management / SuperMod · `discord:connection:write` |
| 6 | DELETE | `/connections/{connectionId:guid}` | — | `StatusResponseDto<object>` (disconnect) | management / SuperMod · `discord:connection:write` |
| 7 | GET | `/connections/{connectionId:guid}/configs` | — | `StatusResponseDto<IReadOnlyList<DiscordNotificationConfigDto>>` | management / Moderator · `discord:config:read` |
| 8 | POST | `/connections/{connectionId:guid}/configs` | `CreateDiscordNotificationConfigRequest` | `StatusResponseDto<DiscordNotificationConfigDto>` | management / SuperMod · `discord:config:write` |
| 9 | PUT | `/configs/{configId:guid}` | `UpdateDiscordNotificationConfigRequest` | `StatusResponseDto<DiscordNotificationConfigDto>` | management / SuperMod · `discord:config:write` |
| 10 | DELETE | `/configs/{configId:guid}` | — | `StatusResponseDto<object>` | management / SuperMod · `discord:config:write` |
| 11 | GET | `/configs/{configId:guid}/preview` | — | `StatusResponseDto<DiscordNotificationPreviewDto>` | management / Moderator · `discord:config:read` |
| 12 | GET | `/connections/{connectionId:guid}/roles` | — | `StatusResponseDto<IReadOnlyList<DiscordNotificationRoleDto>>` | management / Moderator · `discord:role:read` |
| 13 | POST | `/connections/{connectionId:guid}/roles` | `CreateDiscordNotificationRoleRequest` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / SuperMod · `discord:role:write` |
| 14 | PUT | `/roles/{roleId:guid}` | `UpdateDiscordNotificationRoleRequest` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / SuperMod · `discord:role:write` |
| 15 | DELETE | `/roles/{roleId:guid}` | — | `StatusResponseDto<object>` | management / SuperMod · `discord:role:write` |
| 16 | POST | `/roles/{roleId:guid}/button` | `PostOptInButtonRequest(string ButtonChannelId)` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / SuperMod · `discord:role:write` |
| 17 | POST | `/roles/{roleId:guid}/opt-in` | `DiscordMemberOptInRequest` | `StatusResponseDto<object>` | management / SuperMod · `discord:optin:write` |
| 18 | POST | `/roles/{roleId:guid}/opt-out` | `DiscordMemberOptInRequest` | `StatusResponseDto<object>` | management / SuperMod · `discord:optin:write` |
| 19 | GET | `/connections/{connectionId:guid}/dispatch-log?page=1&pageSize=25` | — | `PaginatedResponse<DiscordDispatchLogDto>` | management / Moderator · `discord:dispatch:read` |

Controller maps every `Result`/`Result<T>` through the existing `BaseController.ResultResponse(...)` overloads (which translate `ErrorCode` → HTTP). Request-DTO records 3, 5, 16 are declared on the controller (or in `Contracts.Discord` alongside the others).

> **Gate-2 action keys (`ActionDefinitions`, schema B.3 — `[GLOBAL, seed]`).** Eight keys gate this surface; all `Plane=management`, none grantable via `!permit` (`IsGrantableViaPermit=false` — Discord wiring is not a per-viewer capability). `FloorTier=low` throughout (config/operational, not ToS/Critical). Reads → `DefaultLevel`/`FloorLevel` = `Moderator(10)`; writes = `SuperMod(20)`:
> `discord:connection:read`(Moderator), `discord:connection:write`(SuperMod), `discord:config:read`(Moderator), `discord:config:write`(SuperMod), `discord:role:read`(Moderator), `discord:role:write`(SuperMod), `discord:optin:write`(SuperMod), `discord:dispatch:read`(Moderator). Register these rows in `DataSeeder` (§7) alongside the other subsystems' action-definition seeds; the controller enforces each via `IActionAuthorizationService.AuthorizeActionAsync(...)` on the matching key.

**OAuth callback (existing, unchanged route):** `IntegrationOAuthController.HandleDiscordCallback` (`GET /api/v1/integrations/discord/callback`, `[AllowAnonymous]`) is refactored to parse the token response into a `DiscordGuildOAuthResult` and call `IDiscordGuildService.UpsertFromOAuthAsync` — replacing the inline `Service` + `DiscordServerAuthorization` writes. The `/connect` start route stays in `IntegrationOAuthController`/`IntegrationsController` as today.

---

## 6. Pipeline actions

One action: `SendDiscordNotificationAction : ICommandAction` in `NomNomzBot.Infrastructure.Pipeline.Actions`, implementing the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13 (`string Type` + `Category`/`Description`; `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`). Lets a command/event pipeline push an ad-hoc Discord post through the same dispatch path (with dedupe).

- **`Type`** = `"send_discord_notification"`
- **`Category`** = `"Integrations"`, **`Description`** = `"Post a notification to a linked Discord channel."`
- **Config DTO** (the action's `Parameters`, mirrored as a typed contract in `Contracts.Pipeline`):
  ```csharp
  public sealed record SendDiscordNotificationActionConfig(
      Guid ConnectionId, string TriggerType, string? TargetChannelIdOverride,
      string MessageTemplate, DiscordEmbedDto? Embed, string? DedupeKeyOverride);
  ```
- **Behavior:** resolves `BroadcasterId` from `ActionContext`, builds a `DiscordDispatchRequest` (DedupeKey = `DedupeKeyOverride ?? $"pipeline:{ActionContext.MessageId ?? EventId}"`), calls `IDiscordNotificationDispatcher.DispatchAsync`. Returns `ActionResult.Ok(output: postedMessageId)` on `sent`/`skipped_dupe`, `ActionResult.Fail(error)` on `failed`. Does not stop the pipeline. Templates resolve through the existing `ITemplateEngine` before dispatch.

Registered as `services.AddTransient<ICommandAction, SendDiscordNotificationAction>();` alongside the other pipeline actions in §7. The action surfaces in the pipeline-builder UI under Integrations; that card is the frontend spec's deliverable (this is a backend spec), wired against the `Type`/`Category`/`Description`/`SendDiscordNotificationActionConfig` contract defined here.

---

## 7. DI registration

In `NomNomzBot.Infrastructure.DependencyInjection.AddInfrastructure(...)`, after the music/integration block:

```csharp
// Discord — guild link, notification rules, dispatch + dedupe
services.AddScoped<IDiscordGuildService, DiscordGuildService>();
services.AddScoped<IDiscordNotificationConfigService, DiscordNotificationConfigService>();
services.AddScoped<IDiscordNotificationRoleService, DiscordNotificationRoleService>();
services.AddScoped<IDiscordNotificationDispatcher, DiscordNotificationDispatcher>();

// Discord REST/gateway adapter (typed HttpClient with resilience, like the Twitch/Spotify clients)
services.AddHttpClient("discord").AddDiscordResilienceHandler();
services.AddScoped<IDiscordBotGateway, DiscordRestBotGateway>();

// Pipeline action
services.AddTransient<ICommandAction, SendDiscordNotificationAction>();
```

- **Lifetimes:** all services **scoped** (consume `IApplicationDbContext`/`IUnitOfWork`, matching `IChannelService`, `IMusicConfigService`, etc.). `IDiscordBotGateway` scoped (resolves per-tenant token per call). Pipeline action **transient** (stateless), matching the other actions.
- **Event handler:** `DiscordGoLiveNotificationHandler : IEventHandler<ChannelOnlineEvent>` in Infrastructure is auto-registered by the existing `RegisterEventHandlers(...)` assembly scan — **no manual line**.
- **Consumed, registered elsewhere (no Discord-side registration):** `IActionAuthorizationService` (roles-permissions.md §7 — Gate-2 per-action authorization, injected into `DiscordController`) and `IIntegrationTokenVault` (identity-auth.md §7 — bot-token vault, injected into `DiscordGuildService` + `DiscordRestBotGateway`). Both are constructor-injected; this subsystem adds no registration line for them.
- **Deployment-profile adapter variants** (chosen by DI per `DeploymentProfile`, no new switch in business code):
  - **Token vault:** the bot token is read/written through `IIntegrationTokenVault` (identity-auth.md §3.4; registered by identity-auth §7 — **no Discord-side registration**). Discord code calls only `UpsertConnectionAsync`/`StoreTokensAsync`/`GetAccessTokenAsync`/`RevokeConnectionAsync`; the underlying KEK custody adapter (`local_aes` lite, in-box AES-GCM vs `kms_envelope` SaaS, Azure Key Vault) lives in `gdpr-crypto.md` and is selected there, invisible to this subsystem.
  - **Dispatch dedupe under multi-instance SaaS:** dedupe correctness comes from the DB unique constraint `(NotificationConfigId, DedupeKey)` (works on both SQLite-lite and Postgres-SaaS — no `IRunOnceGuard` needed; the unique index *is* the guard). The go-live handler stays idempotent because a duplicate insert resolves to `skipped_dupe`.
  - **DbProvider:** `DiscordNotificationDispatch.Id` is UUIDv7 app-assigned (not identity), so the append-only table is provider-portable across SQLite/Postgres with no sequence dependency.
  - No `discord` HttpClient in the lite profile is special-cased — the gateway is identical; only the token-vault branch differs.

EF configurations registered through `AppDbContext.OnModelCreating` (apply-from-assembly), replacing `DiscordServerAuthorizationConfiguration`. New `DbSet`s on `IApplicationDbContext` / `AppDbContext`: `DiscordGuildConnections`, `DiscordNotificationConfigs`, `DiscordNotificationRoles`, `DiscordMemberOptIns`, `DiscordNotificationDispatches` (the old `DiscordServerAuthorizations` set is removed).

**Seeding (`[GLOBAL, seed]`):** the eight Discord `ActionDefinitions` rows from §5 (`discord:connection:read|write`, `discord:config:read|write`, `discord:role:read|write`, `discord:optin:write`, `discord:dispatch:read`) are added to the existing `DataSeeder` alongside the other subsystems' action-definition seeds (TTS/stream-admin/eventsub pattern) — `Plane=management`, `FloorTier=low`, `IsGrantableViaPermit=false`, `DefaultLevel`/`FloorLevel` = 10 (reads) / 20 (writes). These are reference data, not per-channel rows; a fresh channel resolves them through `IActionAuthorizationService` with no Discord-specific seeding.

---

## 8. Dependencies (stack-doc libs used)

- **Microsoft.EntityFrameworkCore 10** (+ profile provider `Npgsql.EntityFrameworkCore.PostgreSQL` / `Microsoft.EntityFrameworkCore.Sqlite`) — entities, repositories, the `(NotificationConfigId, DedupeKey)` unique-index dedupe, EF10 named query filters (soft-delete + tenant).
- **Newtonsoft.Json** — app JSON: the `EmbedConfig` `[VC:JSON]` `ValueConverter` and all request/response DTO bodies (project rule: Newtonsoft for app JSON).
- **Microsoft.Extensions.Http.Resilience 10.7.0** (Polly v8 engine) — retry/circuit-breaker/timeout on the `discord` typed `HttpClient`, via a `DiscordRestBotGateway` `DelegatingHandler` honoring Discord's `Retry-After` (same pattern as the Twitch resilience handler). No third-party Discord SDK — the gateway is a hand-rolled REST client over `IHttpClientFactory` + `System.Text.Json` for Discord's own wire format (Twitch precedent: hand-rolled beats a stale SDK).
- **`IIntegrationTokenVault`** (identity-auth.md §3.4; owned + registered there) — the only path this subsystem uses to store/read/revoke the bot OAuth token. The underlying AES-256-GCM AEAD + DEK lifecycle (`IFieldCipher` / `ISubjectKeyService`, in-box `System.Security.Cryptography`) is owned by `gdpr-crypto.md`; this subsystem consumes the vault, never the crypto primitives, and never pulls a crypto package itself.
- **In-box** `IEventBus` (existing), `IHttpClientFactory`, `ILogger` + `[LoggerMessage]` source-gen + OpenTelemetry (PII discipline: never log tokens, member ids, or message bodies).

No new third-party dependency is introduced by this subsystem.

---

## 9. Decisions (resolved)

- **Ping-role cardinality (schema C4).** `DiscordNotificationConfig.PingRoleId` is a single nullable FK — one ping role per rule — and this subsystem is built against exactly that. Tiered notifications (a different ping role per milestone tier) are a separate schema and a separate spec: they require a `DiscordNotificationConfigRoles` join table, which the locked schema does not define and this spec does not introduce. The implementation targets the single `PingRoleId` FK.
