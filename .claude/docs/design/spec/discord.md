# Discord Subsystem — Interface Specification

**Status:** Implementable. Code from this directly.
**Scope:** Per-channel Discord guild link (both-opt-in handshake), encrypted bot token storage, notification rules (event → Discord channel → template), member opt-in roles, and the dispatch + dedupe log.
**Grounds:** locked schema `.claude/docs/design/2026-06-16-database-schema.md` §P.10; design `.claude/docs/design/2026-06-16-discord-notifications.md`; stack `.claude/docs/design/2026-06-16-stack-and-dependencies.md`; decisions `.claude/docs/design/2026-06-16-decisions-resolved.md`.

## Conventions inherited (binding)

- Namespace `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way, `Result<T>` over exceptions/null.
- Surrogate PK `Id guid` via `Guid.CreateVersion7()` (UUIDv7), app-assigned at insert — never `Guid.NewGuid()`, never DB-default. **Append-only** dispatch table excepted (see §1).
- Tenant key `BroadcasterId guid` (FK→`Channels.Id`) on every table, denormalized onto every child; EF global query filter + Postgres RLS apply.
- External Discord ids (`GuildId`, `DiscordRoleId`, `DiscordMemberId`, channel ids, message ids) are `string(50)` **indexed attribute columns, never keys**.
- Soft-delete = `IsDeleted` predicate via `DeletedAt` (entities inherit `SoftDeletableEntity`); append-only carries `CreatedAt` only.
- Repository + `IUnitOfWork`; controllers never touch `DbContext`. Responses `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]`.
- App JSON via **Newtonsoft.Json** (the `EmbedConfig` `[VC:JSON]` converter and all DTO bodies). Bot OAuth token persistence goes through `IIntegrationTokenVault` (`identity-auth.md` §3.4) — the canonical crypto-shred-ready vault over `ISubjectKeyService` + `IFieldCipher` (AES-256-GCM AEAD). **This subsystem never hand-rolls crypto and never touches `IFieldCipher`/`ISubjectKeyService` directly**; it calls the vault, which stores/returns/shreds the ciphertext. (The legacy `IEncryptionService` AES-CBC adapter is retired per `gdpr-crypto.md` §9 — do not target it.)

> **Replaces** the live `DiscordServerAuthorization` entity (int PK, `string(50) BroadcasterId`, ad-hoc `Status`/`ApprovedBy`) and the inline OAuth/persist logic in the former `IntegrationOAuthController.HandleDiscordCallback`. The OAuth callback now lives in `DiscordOAuthController` (same route) and delegates persistence to `IDiscordGuildService` (no behavior inline). All five P.10 tables are net-new on the guid/UUIDv7 rebuild.

---

## 1. Entities (locked schema — §P.10; do not redefine, reference only)

All entities live in `NomNomzBot.Domain.Entities`. Types/keys are **as locked in the schema**; restated here only as the field surface the owner binds against.

### `DiscordGuildConnection : SoftDeletableEntity` — both-opt-in handshake (supersedes `DiscordServerAuthorization`)
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildId string(50) Index`; `GuildName string(255) Null` **[PII-scrub]**; `BotInstalled bool`; `ServerConsentStatus string(20)` (`pending`|`approved`|`revoked`) [VC:enum]; `ApprovedByDiscordUserId string(50) Null` **[PII-hash]**; `ApprovedAt timestamp Null`; `StreamerEnabled bool`. **Unique** `(BroadcasterId, GuildId)`. Bot OAuth token is **not** a column here — it lives in `IntegrationTokens` (E.2) under an `IntegrationConnections` (E.1) row with `Provider='discord'`, `ProviderAccountId=GuildId`, `BroadcasterId` matching, crypto-shred-ready. The owning `IntegrationConnection.Id` (returned by `IIntegrationTokenVault.UpsertConnectionAsync`) is the persistence handle the service uses for `StoreTokensAsync` (write) and `RevokeConnectionAsync` (disconnect); it is resolved by the `(BroadcasterId, Provider='discord', ProviderAccountId=GuildId)` unique key, so no extra column is required on this entity.

### `DiscordNotificationConfig : SoftDeletableEntity` — one rule per (guild, trigger)
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `TriggerType string(30) Index` (`go_live`|`new_clip`|`schedule`|`milestone`) [VC:enum]; `Enabled bool`; `TargetChannelId string(50)`; `PingRoleId guid FK→DiscordNotificationRole Null Index`; `MessageTemplate text Null`; `EmbedConfig text Null` **[VC:JSON]**; `MilestoneType string(20) Null`; `MilestoneThreshold int Null`; `ConfigSchemaVersion int` (default 1; upcast anchor for `EmbedConfig` — consumed on read by `IDiscordNotificationConfigService`, §3.2). **Unique** `(GuildConnectionId, TriggerType)`.

### `DiscordNotificationRole : SoftDeletableEntity` — per-streamer self-assign notify role
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `DiscordRoleId string(50) Index`; `RoleName string(255) Null`; `SelfAssignEnabled bool`; `DmEnabled bool` (default false; also DELIVER by DM, §11.1); `ButtonMessageId string(50) Null`; `ButtonChannelId string(50) Null`. **Unique** `(GuildConnectionId, DiscordRoleId)`.

### `DiscordMemberOptIn : SoftDeletableEntity` — who gets pinged
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `NotificationRoleId guid FK→DiscordNotificationRole Index`; `DiscordMemberId string(50) Index` **[PII-hash]**; `OptInSource string(20)` (`manual_role`|`command`|`button`) [VC:enum]; `OptedInAt timestamp`; `OptedOutAt timestamp Null`; `DmChannelId string(50) Null` (the member's DM channel, cached on first DM, §11.1). **Unique** `(NotificationRoleId, DiscordMemberId)`.

### `DiscordNotificationDispatch` — **[APPEND-ONLY]** dispatch + dedupe log
`Id guid PK` (UUIDv7, app-assigned; append-only carries `CreatedAt` only — no `UpdatedAt`/`DeletedAt`); `BroadcasterId guid FK→Channels Index`; `NotificationConfigId guid FK→DiscordNotificationConfig Index`; `TriggerType string(30)`; `DedupeKey string(255) Index`; `StreamId guid FK→Streams Null Index`; `PostedMessageId string(50) Null`; `Status string(20)` (`sent`|`failed`|`skipped_dupe`|`skipped`) [VC:enum]; `Error text Null`; `DispatchedAt timestamp Index`. **Unique** `(NotificationConfigId, DedupeKey)` — the DB-level dedupe guarantee: one post per go-live.

> **Dedupe key contract (as-built).** The unique `(NotificationConfigId, DedupeKey)` index is the guard: the first insert of a live key wins; a second insert of the same key is caught as a unique violation and recorded as `skipped_dupe` (no second post).
> - `go_live`: `go_live:{StartedAt UTC, ISO-8601 "O"}` — the stream-start instant (one row per stream session; the handler has no `StreamId` to hand, so `StreamId` is null).
> - `send_discord_notification` pipeline action: the `dedupe_key` param when set, otherwise `pipeline:{MessageId}` (or the `ExecutionId` when there is no message).
> - Personal DMs (§11.1): `{baseDedupeKey}:dm:{discordMemberId}` — one row per member.
> - Live role (§10): `live_role:{StartedAt UTC, "O"}`, kept on `DiscordLiveRoleConfig.AppliedDedupeKey`, not in this table.
> - **`skipped` status.** When the both-opt-in gate fails (`IsLinkActiveAsync` false) the dispatcher appends a `skipped` row and posts nothing. `skipped` and `skipped_dupe` rows carry a suffixed key `{key}#{status}:{ticks}` so the unique index admits them without colliding with the live key.
> - Only `go_live` has an automatic producer. `new_clip`, `schedule` and `milestone` rules can be stored and validated (milestone fields required iff `TriggerType=milestone`), but they dispatch only through the `send_discord_notification` action with the keys above; the earlier `clip:` / `schedule:` / `milestone:` key shapes have no producer.

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
    public required string Status { get; init; } // "sent" | "failed" | "skipped_dupe" | "skipped"
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
    bool SelfAssignEnabled, bool DmEnabled, string? ButtonMessageId, string? ButtonChannelId,
    int OptInCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateDiscordNotificationRoleRequest(
    string DiscordRoleId, string? RoleName, bool SelfAssignEnabled, bool DmEnabled = false);

public sealed record UpdateDiscordNotificationRoleRequest(
    string? RoleName, bool SelfAssignEnabled, bool DmEnabled = false);

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

**Role gate** — all routes are **management plane (Plane B)**. `[Authorize]` + tenant resolution yields only **Gate-1** (pure entry — any authenticated caller, channel must exist); it **cannot** distinguish the write floor from the read floor. The per-route floor is enforced in **Gate-2** by calling `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)` (roles-permissions.md §3.3) on the action key in the table's **Action key** column **before** the service call — returning `FORBIDDEN` (403) when the caller's resolved `ChannelMemberships.LevelValue` is below the action's effective level. Writes floor **`LeadModerator`** (level 20), reads (list/log/preview) floor **`Moderator`** (level 10), matching the design's "both sides consent; streamer/admin configures." The one exception is `discord:connection:write` (approve/revoke server consent, enable, disconnect): it touches the guild credential, so it floors **`Broadcaster`** (level 40, `609ae0fc2`), like every other identity/credential action. Member opt-in/out endpoints carry the **write** key (`LeadModerator`) — they push role changes into the guild; member self-service happens through the Discord button/command path, not this HTTP surface. Every floor is the action's seeded **default**; a broadcaster may raise it via `ChannelActionOverride` but not lower it past the seeded `FloorLevel`. The keys are seeded global `ActionDefinitions` (schema B.3) — see §7.

| # | Verb | Route (under base) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|------|--------------------|-------------|--------------|--------------------|
| 1 | GET | `/connections` | — | `StatusResponseDto<IReadOnlyList<DiscordGuildConnectionDto>>` | management / Moderator · `discord:connection:read` |
| 2 | GET | `/connections/{connectionId:guid}` | — | `StatusResponseDto<DiscordGuildConnectionDto>` | management / Moderator · `discord:connection:read` |
| 3 | POST | `/connections/{connectionId:guid}/server-consent` | `ServerConsentRequest(string ApprovedByDiscordUserId)` | `StatusResponseDto<object>` | management / Broadcaster · `discord:connection:write` |
| 4 | DELETE | `/connections/{connectionId:guid}/server-consent` | — | `StatusResponseDto<object>` | management / Broadcaster · `discord:connection:write` |
| 5 | PUT | `/connections/{connectionId:guid}/streamer-enabled` | `StreamerEnabledRequest(bool Enabled)` | `StatusResponseDto<object>` | management / Broadcaster · `discord:connection:write` |
| 6 | DELETE | `/connections/{connectionId:guid}` | — | `StatusResponseDto<object>` (disconnect) | management / Broadcaster · `discord:connection:write` |
| 7 | GET | `/connections/{connectionId:guid}/configs` | — | `StatusResponseDto<IReadOnlyList<DiscordNotificationConfigDto>>` | management / Moderator · `discord:config:read` |
| 8 | POST | `/connections/{connectionId:guid}/configs` | `CreateDiscordNotificationConfigRequest` | `StatusResponseDto<DiscordNotificationConfigDto>` | management / LeadModerator · `discord:config:write` |
| 9 | PUT | `/configs/{configId:guid}` | `UpdateDiscordNotificationConfigRequest` | `StatusResponseDto<DiscordNotificationConfigDto>` | management / LeadModerator · `discord:config:write` |
| 10 | DELETE | `/configs/{configId:guid}` | — | `StatusResponseDto<object>` | management / LeadModerator · `discord:config:write` |
| 11 | GET | `/configs/{configId:guid}/preview` | — | `StatusResponseDto<DiscordNotificationPreviewDto>` | management / Moderator · `discord:config:read` |
| 12 | GET | `/connections/{connectionId:guid}/roles` | — | `StatusResponseDto<IReadOnlyList<DiscordNotificationRoleDto>>` | management / Moderator · `discord:role:read` |
| 13 | POST | `/connections/{connectionId:guid}/roles` | `CreateDiscordNotificationRoleRequest` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / LeadModerator · `discord:role:write` |
| 14 | PUT | `/roles/{roleId:guid}` | `UpdateDiscordNotificationRoleRequest` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / LeadModerator · `discord:role:write` |
| 15 | DELETE | `/roles/{roleId:guid}` | — | `StatusResponseDto<object>` | management / LeadModerator · `discord:role:write` |
| 16 | POST | `/roles/{roleId:guid}/button` | `PostOptInButtonRequest(string ButtonChannelId)` | `StatusResponseDto<DiscordNotificationRoleDto>` | management / LeadModerator · `discord:role:write` |
| 17 | POST | `/roles/{roleId:guid}/opt-in` | `DiscordMemberOptInRequest` | `StatusResponseDto<object>` | management / LeadModerator · `discord:optin:write` |
| 18 | POST | `/roles/{roleId:guid}/opt-out` | `DiscordMemberOptInRequest` | `StatusResponseDto<object>` | management / LeadModerator · `discord:optin:write` |
| 19 | GET | `/connections/{connectionId:guid}/dispatch-log?page=1&pageSize=25` | — | `PaginatedResponse<DiscordDispatchLogDto>` | management / Moderator · `discord:dispatch:read` |
| 20 | GET | `/connections/{connectionId:guid}/blast-radius` | — | `StatusResponseDto<BlastRadiusDto>` | management / Moderator · `discord:connection:read` (§11.3) |
| 21 | GET | `/connections/{connectionId:guid}/guild` | — | `StatusResponseDto<DiscordGuildInfoDto>` | management / Moderator · `discord:connection:read` (§11.2) |
| 22 | GET | `/connections/{connectionId:guid}/guild/roles` | — | `StatusResponseDto<IReadOnlyList<DiscordGuildRoleDto>>` | management / Moderator · `discord:role:read` (§11.2) |
| 23 | GET | `/connections/{connectionId:guid}/guild/channels` | — | `StatusResponseDto<IReadOnlyList<DiscordGuildChannelDto>>` | management / Moderator · `discord:connection:read` (§11.2) |
| 24 | GET | `/connections/{connectionId:guid}/guild/roles/assignable` | — | `StatusResponseDto<IReadOnlyList<DiscordAssignableRoleDto>>` | management / Moderator · `discord:role:read` (§11.2) |
| 25 | GET | `/connections/{connectionId:guid}/guild/channels/postable` | — | `StatusResponseDto<IReadOnlyList<DiscordPostableChannelDto>>` | management / Moderator · `discord:connection:read` (§11.2) |

Controller maps every `Result`/`Result<T>` through the existing `BaseController.ResultResponse(...)` overloads (which translate `ErrorCode` → HTTP). Request-DTO records 3, 5, 16 are declared on the controller (or in `Contracts.Discord` alongside the others).

> **Gate-2 action keys (`ActionDefinitions`, schema B.3 — `[GLOBAL, seed]`).** Eight keys gate this surface; all `Plane=management`, none grantable via `!permit` (`IsGrantableViaPermit=false` — Discord wiring is not a per-viewer capability). `FloorTier=low` throughout (config/operational, not ToS/Critical). Reads → `DefaultLevel`/`FloorLevel` = `Moderator(10)`; writes = `LeadModerator(20)`, except `discord:connection:write` = `Broadcaster(40)`:
> `discord:connection:read`(Moderator), `discord:connection:write`(Broadcaster), `discord:config:read`(Moderator), `discord:config:write`(LeadModerator), `discord:role:read`(Moderator), `discord:role:write`(LeadModerator), `discord:optin:write`(LeadModerator), `discord:dispatch:read`(Moderator). Register these rows in `DataSeeder` (§7) alongside the other subsystems' action-definition seeds; the controller enforces each via `IActionAuthorizationService.AuthorizeActionAsync(...)` on the matching key.

**OAuth (`DiscordOAuthController`, route prefix `api/v{version:apiVersion}`).** Discord is not an ordinary user-resource provider (it carries a guild authorization), so it is excluded from the generic descriptor-driven vaulted flow (`IntegrationOAuthController`) by design. Two routes, both `[AllowAnonymous]`:
- `GET /channels/{channelId}/integrations/discord/callback/start` — redirects to Discord's authorize page with scopes `bot guilds` and `integration_type=0` (guild install). The channel id and an optional loopback `redirect_uri` (desktop client; checked by `ClientRedirectPolicy`) are held server-side under a single-use CSRF state nonce (`IDiscordOAuthStateService`), never in the query string. A missing `discord.client_id` returns `PROVIDER_NOT_CONFIGURED`.
- `GET /integrations/discord/callback` — consumes the state nonce (missing, expired or forged state is rejected), exchanges the code, parses the token response into a `DiscordGuildOAuthResult` and calls `IDiscordGuildService.UpsertFromOAuthAsync`, which vaults the bot token through `IIntegrationTokenVault`. No persistence is done inline in the controller.

---

## 6. Pipeline actions

One action: `SendDiscordNotificationAction : ICommandAction` in `NomNomzBot.Infrastructure/Discord/PipelineActions/`, implementing the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13. Lets a command/event pipeline push a Discord post through the same dispatch path (with dedupe).

- **`ActionType`** = `"send_discord_notification"`; **`Category`** = the localized key `pipeline.category.discord`; **`Description`** = `pipeline.send_discord_notification.description` (translations live in the resource files, never in code).
- **Params (as-built)** — the action's `Fields`:
  - `trigger_type` (required; field kind `ResourceId`) — which notification rule to fire (`go_live`, `new_clip`, `schedule`, `milestone`).
  - `dedupe_key` (optional text) — overrides the default `pipeline:{MessageId}` (or `ExecutionId` when there is no message).

  There is no per-step connection, channel, template or embed param. The **matching enabled notification rule** for `trigger_type` supplies the target channel, message template, embed and ping role; the pipeline's resolved variables (plus `user.name` and `raw.message`) become the template data the dispatcher renders through `ITemplateResolver`.
- **Behavior:** builds a `DiscordDispatchRequest(BroadcasterId, trigger_type, dedupeKey, StreamId: null, templateData)` from the execution context and calls `IDiscordNotificationDispatcher.DispatchAsync`, which dispatches to every enabled rule of that trigger across the channel's linked guilds and returns the last outcome. `ActionResult.Success(postedMessageId)` on `sent` / `skipped` / `skipped_dupe`; `ActionResult.Failure(reason)` when no enabled rule exists (`NOT_FOUND`) or the outcome is `failed`. Does not stop the pipeline.

Registered as `services.AddTransient<ICommandAction, SendDiscordNotificationAction>();` alongside the other pipeline actions in §7. The pipeline builder renders it under the Discord category; `trigger_type` is picked from the channel's stored rules.

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

**Seeding (`[GLOBAL, seed]`):** the eight Discord `ActionDefinitions` rows from §5 (`discord:connection:read|write`, `discord:config:read|write`, `discord:role:read|write`, `discord:optin:write`, `discord:dispatch:read`) are added to the existing `DataSeeder` alongside the other subsystems' action-definition seeds (TTS/stream-admin/eventsub pattern) — `Plane=management`, `FloorTier=low`, `IsGrantableViaPermit=false`, `DefaultLevel`/`FloorLevel` = 10 (reads) / 20 (writes), except `discord:connection:write` = 40. These are reference data, not per-channel rows; a fresh channel resolves them through `IActionAuthorizationService` with no Discord-specific seeding.

---

## 8. Dependencies (stack-doc libs used)

- **Microsoft.EntityFrameworkCore** (+ profile provider `Npgsql.EntityFrameworkCore.PostgreSQL` / `Microsoft.EntityFrameworkCore.Sqlite`) — entities, repositories, the `(NotificationConfigId, DedupeKey)` unique-index dedupe, EF named query filters (soft-delete + tenant).
- **Newtonsoft.Json** — app JSON: the `EmbedConfig` `[VC:JSON]` `ValueConverter` and all request/response DTO bodies (project rule: Newtonsoft for app JSON).
- **Microsoft.Extensions.Http.Resilience** (Polly engine) — retry/circuit-breaker/timeout on the `discord` typed `HttpClient`, via a `DiscordRestBotGateway` `DelegatingHandler` honoring Discord's `Retry-After` (same pattern as the Twitch resilience handler). No third-party Discord SDK — the gateway is a hand-rolled REST client over `IHttpClientFactory` + `System.Text.Json` for Discord's own wire format (Twitch precedent: hand-rolled beats a stale SDK).
- **`IIntegrationTokenVault`** (identity-auth.md §3.4; owned + registered there) — the only path this subsystem uses to store/read/revoke the bot OAuth token. The underlying AES-256-GCM AEAD + DEK lifecycle (`IFieldCipher` / `ISubjectKeyService`, in-box `System.Security.Cryptography`) is owned by `gdpr-crypto.md`; this subsystem consumes the vault, never the crypto primitives, and never pulls a crypto package itself.
- **In-box** `IEventBus` (existing), `IHttpClientFactory`, `ILogger` + `[LoggerMessage]` source-gen + OpenTelemetry (PII discipline: never log tokens, member ids, or message bodies).

No new third-party dependency is introduced by this subsystem.

---

## 9. Decisions (resolved)

- **Ping-role cardinality (schema C4).** `DiscordNotificationConfig.PingRoleId` is a single nullable FK — one ping role per rule — and this subsystem is built against exactly that. Tiered notifications (a different ping role per milestone tier) are a separate schema and a separate spec: they require a `DiscordNotificationConfigRoles` join table, which the locked schema does not define and this spec does not introduce. The implementation targets the single `PingRoleId` FK.

---

## 10. Live-role extension — "currently live" Discord role (as built)

**Scope:** while a streamer's channel is live, the bot holds a configured Discord role on the streamer's own guild member; the role is removed the moment the channel goes offline. Net-new entity, service, two event handlers, and a startup reconciler on top of §1–§7 above; consumes the same `DiscordGuildConnection` both-opt-in link and the same `IDiscordBotGateway`.

### 10.1 `DiscordLiveRoleConfig : SoftDeletableEntity, ITenantScoped`

`Id guid PK`; `BroadcasterId guid FK→Channels` — the streamer whose live state drives the role; `GuildConnectionId guid FK→DiscordGuildConnection` — the both-opt-in link (§1) this rule targets; only an **active** link (both consent flags true, checked live via `IDiscordGuildService.IsLinkActiveAsync`) is honored, so a channel can never drive a role in a guild it has no accepted link to; `RoleId string(50)` — the "currently live" Discord role snowflake, an indexed attribute, never a key; `DiscordMemberId string(50)` — the streamer's own Discord member snowflake inside the guild, entered by the streamer (there is no automatic Twitch-account-to-Discord-account link); `Enabled bool`; `IsCurrentlyApplied bool` — true while the role is believed applied, the idempotency + reconciliation flag; `AppliedDedupeKey string(64)? Null` — the dedupe discriminator of the online event that last applied the role (mirrors the go-live handler's per-session key: a duplicate online event for the same stream session is a no-op, not a second Discord call). **Unique** `(BroadcasterId, GuildConnectionId)`.

A friend's channel gets its **own** `DiscordLiveRoleConfig` row against its **own** `DiscordGuildConnection` into the same guild — the consent model is identical to §1's guild link: guild-admin approval (`ServerConsentStatus="approved"`) **and** the friend's own `StreamerEnabled=true`, both required before any role call is made.

EF configuration: `DiscordLiveRoleConfigConfiguration` in `NomNomzBot.Infrastructure/Discord/Persistence/`. Carried in both migration assemblies (`NomNomzBot.Infrastructure/Platform/Persistence/Migrations` and `NomNomzBot.Migrations.Sqlite/Migrations`, `AddDiscordLiveRoleConfig`).

### 10.2 `IDiscordLiveRoleService` (`NomNomzBot.Application.Contracts.Discord`; impl `DiscordLiveRoleService` in Infrastructure)

```csharp
public interface IDiscordLiveRoleService
{
    // Applies every enabled, actively-linked live-role rule for the channel that just went online.
    // dedupeKey mirrors the go-live handler's per-session key — a repeat for the same session is
    // skipped, not re-applied.
    Task ApplyForOnlineAsync(Guid broadcasterId, string dedupeKey, CancellationToken ct = default);

    // Removes the role for every currently-applied live-role rule of the channel that just went
    // offline, regardless of its current Enabled flag (a disabled-mid-stream rule still gets cleaned up).
    Task RemoveForOfflineAsync(Guid broadcasterId, CancellationToken ct = default);

    // Startup self-heal: clears (removes the Discord role + resets state for) every rule whose
    // IsCurrentlyApplied is stale against its channel's actual IsLive.
    Task ReconcileStaleAsync(CancellationToken ct = default);
}
```

**`ApplyForOnlineAsync`** — for every enabled config on the broadcaster: skip if already applied under the same `dedupeKey` (idempotent); resolve the link via `IDiscordGuildService.IsLinkActiveAsync` and skip (logged, not thrown) if it is not active; call `IDiscordBotGateway.ValidateRoleAssignableAsync` and skip on failure (see §10.4); call `IDiscordBotGateway.AddMemberRoleAsync` and skip on failure; on success set `IsCurrentlyApplied=true`, `AppliedDedupeKey=dedupeKey`. Every skip is a `LogWarning`, never a thrown exception — a Discord-side failure must never disturb the live flow.

**`RemoveForOfflineAsync`** — removes the role for every config on the broadcaster with `IsCurrentlyApplied=true` (via the shared `RemoveOneAsync` helper), independent of `Enabled` — a rule disabled mid-stream still gets its role cleaned up.

**`RemoveOneAsync`** (private, shared by remove-on-offline and reconciliation) — if the `DiscordGuildConnection` row itself is gone (unlinked/disconnected), there is nothing left to call Discord against: local state is cleared (`IsCurrentlyApplied=false`, `AppliedDedupeKey=null`) and the call returns. Otherwise it calls `IDiscordBotGateway.RemoveMemberRoleAsync`; a `DISCORD_NOT_FOUND` failure (the member no longer has the role, or left the guild) is treated as success and clears local state; any other failure is logged and **`IsCurrentlyApplied` is left `true`** so the next offline event or reconciliation pass retries the removal.

**`ReconcileStaleAsync`** — joins every `IsCurrentlyApplied=true` config against its `Channel.IsLive`; any config whose channel is **not** live is stale (a missed `stream.offline` event — bot restart/crash between online and offline) and is cleared through `RemoveOneAsync`.

### 10.3 Event handlers (`NomNomzBot.Infrastructure.Discord.EventHandlers`)

- **`DiscordLiveRoleOnlineHandler : IEventHandler<ChannelOnlineEvent>`** — builds `dedupeKey = $"live_role:{StartedAt.UtcDateTime:O}"` (the stream-start instant, same session-scoping shape as `DiscordGoLiveNotificationHandler`'s dedupe) and calls `ApplyForOnlineAsync`. Mirrors the go-live notification handler's shape exactly: best-effort, wraps the call in `try/catch (Exception ex) when (ex is not OperationCanceledException)` and logs a warning rather than letting a Discord failure propagate into the online-event pipeline.
- **`DiscordLiveRoleOfflineHandler : IEventHandler<ChannelOfflineEvent>`** — calls `RemoveForOfflineAsync`, same best-effort try/catch shape. If this event is ever missed, `DiscordLiveRoleReconciliationHostedService` clears the stranded role on next startup.

Both handlers no-op on `BroadcasterId == Guid.Empty` and are auto-registered by the existing `RegisterEventHandlers(...)` assembly scan (§7) — no manual DI line.

### 10.4 `IDiscordBotGateway.ValidateRoleAssignableAsync` — actionable failure modes

`Task<Result> ValidateRoleAssignableAsync(Guid broadcasterId, string guildId, string roleId, CancellationToken ct = default)` (declared alongside `GetAssignableGuildRolesAsync` in §3.5's `IDiscordBotGateway`; both share the private `EvaluateRoleAssignability` check against the bot's resolved role context — never reimplemented a second time). Reads the target role from the guild's role list (`DISCORD_NOT_FOUND` if it no longer exists), then evaluates two real Discord permission-bit failure modes against the bot's own resolved guild-member state:

1. **Bot missing Manage Roles** — the bot's combined role permissions do not include Discord's Manage Roles bit (`1 << 28`). `Result.Failure` code `DISCORD_MISSING_MANAGE_ROLES`, message: *"The bot needs the Manage Roles permission in this Discord server to manage the live role. Grant Manage Roles to the bot's role in Server Settings > Roles."*
2. **Bot's highest role at or below the target role** — the bot's highest role `Position` is `<=` the target role's `Position` (Discord's role hierarchy: a bot can only assign roles strictly below its own highest role). `Result.Failure` code `DISCORD_ROLE_HIERARCHY`, message: *"The bot's role must be above '{RoleName}' in Server Settings > Roles for the bot to apply or remove the live role."*

Both messages name the exact Discord Server Settings screen the streamer needs to fix, not a bare error code. `ApplyForOnlineAsync` calls this validation before every `AddMemberRoleAsync` and skips (logged) on either failure — a misconfigured guild never throws, it just fails to apply and says why.

### 10.5 Startup reconciliation

`DiscordLiveRoleReconciliationHostedService : IHostedService` runs once on startup, gated by `IRunOnceGuard.TryAcquireAsync("discord-live-role-reconcile", TimeSpan.FromMinutes(5), ct)` — the same lease seam `SongRequestQueueRestoreHostedService` uses — so a zero-downtime blue/green overlap (two API instances starting against one database) does not double the removal calls. When the lease is acquired it calls `IDiscordLiveRoleService.ReconcileStaleAsync`; when another instance already holds it, this instance no-ops for the startup.

---

## 11. Shipped extensions (as built)

Features built on top of §1–§10; each consumes the same both-opt-in `DiscordGuildConnection` link and the same `IDiscordBotGateway`.

### 11.1 Personal DM opt-in delivery

A notify role can also deliver by direct message (decided 2026-07-17). `DiscordNotificationRole.DmEnabled` (default `false`) is set through `CreateDiscordNotificationRoleRequest` / `UpdateDiscordNotificationRoleRequest` and returned on `DiscordNotificationRoleDto`.

After a rule's channel post, `DiscordNotificationDispatcher` fans out DMs when the rule's `PingRoleId` role has `DmEnabled`: every member with an active opt-in (`OptedOutAt` null) receives the rendered notification (content + embed, no role ping) as a DM.
- The DMs are an independent output of the same dispatch — a channel failure never blocks them, and a DM failure never fails the channel post.
- Sequential, best-effort per member. Each DM is its own append-only `DiscordNotificationDispatch` row keyed `{baseDedupeKey}:dm:{discordMemberId}`, so a re-dispatch is a per-member no-op (the unique index rejects the repeat).
- The member's DM channel is opened with `IDiscordBotGateway.OpenDmChannelAsync` and cached on `DiscordMemberOptIn.DmChannelId`, so the next go-live skips the open call. A member with DMs closed is recorded as a `failed` row with the Discord reason; the fan-out continues with the next member.

### 11.2 Guild directory (live pickers)

`IDiscordGuildDirectoryService` (`Contracts.Discord`; impl `DiscordGuildDirectoryService`) proxies the linked guild's live data to the dashboard pickers. Nothing is persisted. Backed by the gateway reads `GetGuildAsync`, `GetGuildRolesAsync`, `GetGuildChannelsAsync`, `GetAssignableGuildRolesAsync`, `GetPostableGuildChannelsAsync`.

| Method | Route (§5 rows 21–25) | Returns |
|---|---|---|
| `GetGuildAsync` | `/guild` | `DiscordGuildInfoDto(Id, Name, Icon, Description)` |
| `GetGuildRolesAsync` | `/guild/roles` | `DiscordGuildRoleDto(Id, Name, Color, Position, Managed, Mentionable, Permissions)` |
| `GetGuildChannelsAsync` | `/guild/channels` | `DiscordGuildChannelDto(Id, Name, Type, ParentId, Position)` |
| `GetAssignableGuildRolesAsync` | `/guild/roles/assignable` | the role list plus `CanAssign`, `UnavailableReasonCode`, `UnavailableReason` per role (S055c) |
| `GetPostableGuildChannelsAsync` | `/guild/channels/postable` | the channel list plus `CanPost`, `UnavailableReasonCode`, `UnavailableReason` per channel (honors per-channel permission overwrites, not just guild-level ones) |

The assignable and postable reads fail with `DISCORD_LINK_INACTIVE` when the both-opt-in handshake is not fully active — distinct from an empty list and from a per-item "the bot cannot use this one". The per-role reason reuses the §10.4 checks (`DISCORD_MISSING_MANAGE_ROLES`, `DISCORD_ROLE_HIERARCHY`).

### 11.3 Disconnect blast radius

`GET /connections/{connectionId:guid}/blast-radius` (`discord:connection:read`, `[DestructiveAction(HasCountedBlastRadius = true)]`) returns `StatusResponseDto<BlastRadiusDto>` from `IDiscordGuildService.GetDisconnectBlastRadiusAsync`: what STOPS WORKING on disconnect, counted, not just the rows removed. Categories (a zero-count category is omitted): `DiscordNotificationRules` (rules on the connection), `DiscordRoleButtons` (notify roles on the connection), `PipelineSteps` (pipeline steps of type `send_discord_notification`, with the pipeline names; the count is flagged as a MINIMUM when some references can only be resolved at run time). `NOT_FOUND` when the connection is absent or another tenant's. The dashboard calls it and renders the result before the disconnect confirm (`DELETE /connections/{connectionId:guid}`, also `[DestructiveAction(HasCountedBlastRadius = true)]`).

### 11.4 Interactions endpoint

`POST /api/v1/discord/interactions` (`DiscordInteractionsController`) is the URL registered as the application's **Interactions Endpoint URL** in the Discord Developer Portal. `[AllowAnonymous]` with the anonymous rate-limit policy — Discord calls it unauthenticated, and security is the mandatory Ed25519 check:
- `IDiscordInteractionVerifier` verifies `X-Signature-Ed25519` over `X-Signature-Timestamp` + the **raw body bytes** against the application public key `Discord:PublicKey` (32-byte hex). The body is read straight off the request stream (no model binding), so the signature covers exactly what Discord sent.
- Missing headers or a bad signature answer **401** before the body is parsed (Discord probes with invalid signatures at registration and afterwards). An unconfigured `Discord:PublicKey` answers **503** (feature not configured, never a crash). A body over 256 KiB answers **413**.
- `IDiscordInteractionService.HandleAsync` routes the verified payload: `PING` (1) -> `PONG` (`{"type":1}`); `MESSAGE_COMPONENT` (3) with `custom_id` `notify_optin:{roleId:N}` (the id the gateway stamps on the posted opt-in button, §3.3) -> toggles the clicking member's opt-in through `IDiscordNotificationRoleService` with source `button` and answers with an ephemeral message (type 4, flags 64). An unknown interaction type or `custom_id` gets an ephemeral "not supported" reply, never an error status; only an unparseable body fails (`VALIDATION_FAILED`). The response JSON is returned verbatim within Discord's 3-second deadline.
- The webhook is app-level (no tenant context): the tenant is resolved FROM the role row the `custom_id` names.

### 11.5 `!discord` invite command

`DiscordInviteBuiltin` (builtin key `discord`, default cooldown 30 s, minimum permission level 0) replies with a live invite link to the channel's linked server. There is no stored invite URL anywhere in the schema, so it builds a real one on demand: the first connection whose `IsLinkActive` is true, the first channel the bot can post in (`GetPostableGuildChannelsAsync`), then `IDiscordBotGateway.CreateChannelInviteAsync` (a permanent invite; Discord's own de-dupe reuses an existing one). Every failure mode degrades to an honest reply through a personality response slot — `Discord.NotConnected` (no active link) or `Discord.Unavailable` (no postable channel, or the Discord call failed); it never fabricates an invite.

### 11.6 Pipeline option providers

`DiscordChannelOptionProvider` (`PipelineActionFieldKind.DiscordChannel`) and `DiscordRoleOptionProvider` (`DiscordRole`) back the rich pickers for pipeline step fields (S-RICH-PICKERS), so a step never asks for a bare snowflake id. Both resolve the tenant's active guild link through the shared `DiscordGuildOptionProviderBase` (approved AND `StreamerEnabled`) and read the live lists through `IDiscordGuildDirectoryService`. Channels are ordered by position, searchable by name, with `SecondaryText` = the channel type plus its parent category; roles carry their colour (hex) and whether they are mentionable. With no active link the provider answers `PipelineOptionListResult.Unavailable("No active Discord server link for this channel — link and enable a server in the Discord integration settings.")`; a Discord read failure answers `Unavailable` with the reason. Only real Discord data is shown, never a fabricated label.
