# Interface Specification — `twitch-eventsub` subsystem

**Status:** Implementable. Code from this directly.
**Owner area:** EventSub lifecycle (`IHostedService`), transport adapter (WebSocket self-host / conduit+webhook SaaS), subscription registry, notification handlers, reconnect/backfill, idempotent journaling.

**Grounding:** schema `2026-06-16-database-schema.md` (LOCKED) §F.4/F.7/F.8/F.9/O.1/O.1a/O.4/Q.3; rebuild `2026-06-16-twitch-rebuild.md` (EventSub transport split, `IEventSource` seam); stack `2026-06-16-stack-and-dependencies.md` (Twitch decision: hand-rolled, lite=`ClientWebSocket`, SaaS=conduits+webhooks+in-box `HMACSHA256`); decisions `2026-06-16-decisions-resolved.md`.

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (no `.Result`/`.Wait`); `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, no MediatR, no Roslyn; responses `StatusResponseDto<T>`/`PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; **Newtonsoft.Json** for app JSON (`[VC:JSON]` converters); inbound EventSub frame parsing stays on `System.Text.Json` (`.Strict`, hot path) per the validation decision; surrogate PKs `Guid` via `Guid.CreateVersion7()`; Twitch ids = indexed `string` attribute columns; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **Migration note (load-bearing).** The live `EventSubscription` entity (string PK, `BroadcasterId string(50)`, `[VC:JSON]`-as-`jsonb`) is **superseded** by `EventSubSubscription` (guid PK, `BroadcasterId Guid`, real converters). The existing `TwitchEventSubService` is **rewritten** behind the transport seam below. The legacy `Dictionary<string,string>`/`string[]` `jsonb` mappings in `EventSubscriptionConfiguration.cs` are removed (banned by §1.4 config-review gate). This spec names the new types; do not keep both.

---

## 1. Entities (owned; defined in the LOCKED schema — referenced, not redefined)

All entities are EF Core 10 classes in `NomNomzBot.Domain/Entities/`. Each implements `BaseEntity` (+`SoftDeletableEntity` where `[soft-delete]`) and, when tenant-owned, `ITenantScoped` (widened `BroadcasterId` → `Guid`). Twitch ids are `string` attribute columns; all `[VC:JSON]` columns use hand-rolled `ValueConverter<T,string>` + `ValueComparer` over Newtonsoft.Json (no `jsonb`, no `HasDefaultValueSql`).

| Entity | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| `EventSubSubscription` | §F.7 `[soft-delete]` | tenant | `Id Guid` PK; `BroadcasterId Guid` (FK→Channels); `Provider string(20)` (`twitch`|`kick`|`youtube`|`x`); `EventType string(100)`; `Version string(20)`; `Condition string` `[VC:JSON]` `Dictionary<string,string>`; `Transport string(20)` (`websocket`\|`conduit`\|`webhook`); `TwitchSubscriptionId string(255)?`; `SessionId string(255)?`; `ConduitId string(255)?`; `ShardId string(255)?`; `Status string(20)` (`pending`\|`enabled`\|`failed`\|`revoked`); `Enabled bool`; `Cost int?`; `LastError string(1000)?`; `ExpiresAt timestamp?`. **Unique** `(BroadcasterId, Provider, EventType, Version)`. |
| `EventSubConduit` | §F.8 `[GLOBAL]` | global | `Id Guid` PK; `Provider string(20)`; `ConduitId string(255)` Unique; `ShardCount int`; `Status string(20)` (`active`\|`degraded`\|`reprovisioning`\|`revoked`); `LastReconciledAt timestamp?`. |
| `EventSubConduitShard` | §F.9 `[GLOBAL]` | global | `Id Guid` PK; `ConduitId Guid` (FK→EventSubConduit); `ShardId string(255)`; `Transport string(20)` (`webhook`\|`websocket`); `CallbackUrl string(2048)?`; `SessionId string(255)?`; `Status string(20)` (`enabled`\|`webhook_callback_verification_pending`\|`disabled`); `AssignedAt timestamp?`. **Unique** `(ConduitId, ShardId)`. |
| `EventJournal` | §O.1 `[APPEND-ONLY]` | tenant-nullable | `Id bigint` PK; `EventId Guid` Unique; `BroadcasterId Guid?` (FK→Channels); `StreamPosition bigint` (app-assigned via TenantSequences); `EventType string(150)`; `EventVersion int`; `Source string(30)` (`eventsub`\|`domain`\|`irc`\|`import`); `Payload string` `[VC:JSON]`; `PayloadIsEncrypted bool`; `SubjectKeyId Guid?` (FK→CryptoKey); `CorrelationId Guid?`; `CausationId Guid?`; `ActorUserId Guid?`; `ActorTwitchUserId string(50)?`; `Metadata string` `[VC:JSON]`; `OccurredAt timestamp`; `RecordedAt timestamp`. **Unique** `EventId`, **Unique** `(BroadcasterId, StreamPosition)`. |
| `EventSubjectKey` | §O.1a | tenant-nullable | `Id Guid` PK; `EventId Guid` (FK→EventJournal.EventId); `BroadcasterId Guid?`; `SubjectIdHash string(64)`; `SubjectKeyId Guid` (FK→CryptoKey); `Role string(20)?` (`gifter`\|`recipient`\|`raider`\|`raided`). **Unique** `(EventId, SubjectKeyId)`. |
| `IdempotencyKey` | §O.4 | tenant-nullable | `Id bigint` PK; `Scope string(100)`; `Key string(255)`; `BroadcasterId Guid?`; `ResultHash string(64)?`; `ExpiresAt timestamp`. **Unique** `(Scope, Key, BroadcasterId)`. |
| `TenantSequence` | §Q.3 | tenant | `Id Guid` PK; `BroadcasterId Guid` (FK→Channels); `SequenceName string(50)` (`event_stream_position`); `NextValue bigint`. **Unique** `(BroadcasterId, SequenceName)`. |
| `TwitchChannelEventLog` | §F.4 `[APPEND-ONLY]` | tenant | read-model written by the projection; `Id bigint` PK; `BroadcasterId Guid`; `EventType string(100)`; `ActorUserId Guid?`; `ActorTwitchUserId string(50)?`; `ActorDisplayNameSnapshot string(255)?`; `StreamId Guid?`; `Payload string?` `[VC:JSON]`; `OccurredAt timestamp`. |

**Ownership boundary.** This subsystem **writes** `EventSubSubscription`, `EventSubConduit`, `EventSubConduitShard`, `IdempotencyKey`. It **appends to** `EventJournal`/`EventSubjectKey` and **allocates** `TenantSequence` **through event-store** — those write paths are owned by event-store (`IEventJournal.AppendAsync` + `ITenantSequenceAllocator`; see event-store.md, canonical journal owner), not directly here. It **does not** own the read models projected *from* `EventJournal` (`Streams` F.1, `TwitchSubscribers` F.2, `TwitchFollowers` F.3, `RewardRedemptions` F.6, `TwitchChannelEventLog` F.4) — those belong to the projection subsystem. The seam is: this subsystem appends to `EventJournal` (via event-store) and publishes domain events on `IEventBus`; projections consume.

New `IApplicationDbContext` `DbSet`s to add (replacing `DbSet<EventSubscription> EventSubscriptions`):
`DbSet<EventSubSubscription> EventSubSubscriptions`, `DbSet<EventSubConduit> EventSubConduits`, `DbSet<EventSubConduitShard> EventSubConduitShards`, `DbSet<EventJournal> EventJournal`, `DbSet<EventSubjectKey> EventSubjectKeys`, `DbSet<IdempotencyKey> IdempotencyKeys`, `DbSet<TenantSequence> TenantSequences`.

---

## 2. Domain events

All in `NomNomzBot.Domain/Events/`, inheriting the canonical `DomainEventBase` (`NomNomzBot.Domain.Events`; supplies `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt` — authoritative definition in `platform-conventions.md` §2.0). Events **do not redeclare** the inherited members. The existing per-topic domain events (`FollowEvent`, `NewSubscriptionEvent`, `GiftSubscriptionEvent`, `CheerEvent`, `RaidEvent`, `UserBannedEvent`, `UserTimedOutEvent`, `ChannelUpdatedEvent`, `RewardRedeemedEvent`, `ChannelOnlineEvent`, `ChannelOfflineEvent`, `PollBeganEvent`, `PollEndedEvent`, `PredictionBeganEvent`, `PredictionLockedEvent`, `PredictionEndedEvent`, `HypeTrainBeganEvent`, `HypeTrainEndedEvent`, `ChatMessageReceivedEvent`, `ChatMessageDeletedEvent`, `ShoutoutSentEvent`, `RewardCreatedEvent`/`RewardUpdatedEvent`/`RewardRemovedEvent`) are **reused unchanged** — they are the publish targets of the §3.7 translators. They live in their **domain modules** (`Domain/Community/Events/`, `Domain/Rewards/Events/`, `Domain/Stream/Events/`, `Domain/Moderation/Events/`, `Domain/Chat/Events/`, …), not in a single folder — organised by domain, not by provenance. Where a Twitch subscription type has **no** prior domain event (e.g. shared-chat, ad-break, charity, goals, shield-mode, suspicious-user, warnings, unban-requests, automod, VIP/moderator add-remove, custom power-up, automatic-reward redemption), this subsystem **adds** the missing event in its owning module as part of the fan-out. This subsystem also adds the **lifecycle/transport** events below (which the existing service lacks):

> Note: every lifecycle/transport event below is tenant-scoped, so its publisher sets the inherited `Guid BroadcasterId` to the owning channel — none is platform-level and none is left `Guid.Empty`. Each event adds only its own payload fields; the inherited `EventId`/`BroadcasterId`/`OccurredAt` are not redeclared.

```csharp
// EventSub session/transport reached a steady state (welcome received, subs (re)registered).
public sealed record EventSubConnectedEvent : DomainEventBase
{
    public required EventSubTransportKind Transport { get; init; }   // WebSocket | Conduit | Webhook
    public string? SessionId { get; init; }                          // WS session, null for conduit
    public string? ConduitId { get; init; }                          // conduit, null for WS
    public required int ActiveSubscriptionCount { get; init; }
}

// Transport dropped / will reconnect (backoff in progress). Diagnostic + dashboard "degraded" signal.
public sealed record EventSubDisconnectedEvent : DomainEventBase
{
    public required EventSubTransportKind Transport { get; init; }
    public string? SessionId { get; init; }
    public required string Reason { get; init; }                     // close-code / exception summary (scrubbed)
    public required TimeSpan NextRetryIn { get; init; }
}

// Twitch revoked a subscription (auth lost, scope removed, user gone). Drives needs-reauth UI.
public sealed record EventSubRevokedEvent : DomainEventBase
{
    public required string TwitchSubscriptionId { get; init; }
    public required string EventType { get; init; }
    public required string Status { get; init; }                     // authorization_revoked | user_removed | version_removed
}

// A subscription's lifecycle status changed in the registry (pending→enabled→failed).
public sealed record EventSubSubscriptionStatusChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }               // EventSubSubscription.Id (surrogate)
    public required string EventType { get; init; }
    public required string OldStatus { get; init; }
    public required string NewStatus { get; init; }
    public string? Error { get; init; }
}

// One notification was journaled (idempotent append committed). Emitted before fan-out to per-topic events.
public sealed record EventSubNotificationJournaledEvent : DomainEventBase
{
    public required Guid JournalEventId { get; init; }               // EventJournal.EventId
    public required long StreamPosition { get; init; }
    public required string EventType { get; init; }
    public required bool WasDuplicate { get; init; }                 // true = idempotency short-circuit
}
```

`EventSubTransportKind` lives in `NomNomzBot.Domain/Enums/` and is the **canonical wire transport** handle (owned here):
```csharp
public enum EventSubTransportKind { WebSocket, Conduit, Webhook }
```

`EventSubTokenOwnerKind` also lives in `NomNomzBot.Domain/Enums/` (owned here); it names which Twitch token authorizes a given subscription create (`EventSubSubscriptionRequest.UserAccessTokenOwner`, §4.2). Persisted/serialized as the short string token (`[VC:enum]` — `broadcaster`/`bot`/`moderator`), never the int:
```csharp
public enum EventSubTokenOwnerKind { Broadcaster, Bot, Moderator }
```

> **Disambiguation.** `EventSubTransportKind` (3 members, owned here) is the *wire* transport handle/DTO — how a given subscription/session actually talks to Twitch. It is **distinct** from `platform-conventions`' deployment-profile selector `EventSubTransportMode { WebSocket, ConduitWebhook }`, which picks the deployment *profile* (self-host vs SaaS). Do not conflate them: a SaaS profile (`EventSubTransportMode.ConduitWebhook`) drives both `Conduit` and `Webhook` wire kinds.

---

## 3. Service interfaces

All in `NomNomzBot.Application/Contracts/Twitch/` unless noted. `IEventSource` (the cross-platform seam from the rebuild doc) lives in `NomNomzBot.Application/Contracts/Platform/`.

### 3.1 `IEventSource` — cross-platform inbound-event seam (rebuild doc)

```csharp
namespace NomNomzBot.Application.Contracts.Platform;

public interface IEventSource
{
    string Provider { get; }   // "twitch" | "kick" | "youtube" | "x" — one IEventSource per platform connection (PRODUCT-ALIGNMENT D1)

    Task<Result> EnsureSubscribedAsync(Guid broadcasterId, IReadOnlyCollection<string> eventTypes, CancellationToken ct = default);
    Task<Result> UnsubscribeAllAsync(Guid broadcasterId, CancellationToken ct = default);
    EventSourceHealth Health { get; }
}
```
- `Provider` — discriminator; one implementation per platform (`TwitchEventSource` = EventSub, `KickEventSource` = Kick webhooks, `YouTubeEventSource` = Live Chat / Data API polling, `XEventSource` = X Live). Every source translates its raw events into the **same domain events** with `Provider` stamped (Kick subs/gifts/Kicks and YouTube memberships/Super Chats → `NewSubscriptionEvent`/`ResubscriptionEvent`/`GiftSubscriptionEvent`/`CheerEvent`, table in `supporter-events.md` §4.1) — one event response config, one Alert surface, one `domain.action` name per fact (`widget-sdk.md` §2.1).
- `EnsureSubscribedAsync` — declaratively reconciles the channel's subscription set to exactly `eventTypes` (creates missing, leaves existing, no-ops duplicates); persists registry rows; returns failure with `ErrorCode` `SCOPE_MISSING`/`SERVICE_UNAVAILABLE` on Twitch rejection.
- `UnsubscribeAllAsync` — revokes every active subscription for the tenant at Twitch and soft-deletes its registry rows (channel offboarding / erasure).
- `Health` — synchronous transport-health snapshot for the dashboard/health endpoint.

`EventSourceHealth` (record in same namespace): `record EventSourceHealth(bool IsConnected, EventSubTransportKind Transport, int ActiveSubscriptions, DateTimeOffset? LastEventAt, DateTimeOffset? LastReconnectAt);`

### 3.2 `ITwitchEventSubService` — registry + lifecycle facade (**replaces** the existing interface)

Implemented by `TwitchEventSubHostedService` (the `IHostedService`); also exposes runtime control. Method bodies delegate to the selected `IEventSubTransport`.

```csharp
namespace NomNomzBot.Application.Contracts.Twitch;

public interface ITwitchEventSubService : IEventSource
{
    // Subscribe one event type for a tenant; idempotent on (BroadcasterId, EventType, Version).
    // State change: upserts an EventSubSubscription (Status=pending→enabled), calls Twitch via the transport,
    // emits EventSubSubscriptionStatusChangedEvent. Returns the registry row.
    Task<Result<EventSubSubscriptionDto>> SubscribeAsync(Guid broadcasterId, string eventType, CancellationToken ct = default);

    // Revoke one subscription by its surrogate id. State change: DELETE at Twitch + soft-delete registry row;
    // emits EventSubSubscriptionStatusChangedEvent(NewStatus=revoked). NOT_FOUND if unknown.
    Task<Result> UnsubscribeAsync(Guid subscriptionId, CancellationToken ct = default);

    // Read the persisted registry for a tenant (no Twitch call). Paginated.
    Task<Result<PagedList<EventSubSubscriptionDto>>> GetSubscriptionsAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    // Reconcile this tenant's registry against Twitch's actual subscription list (drops orphans, re-creates
    // missing, repairs status). Side effect: registry writes + status events. Used after reconnect & by admin.
    Task<Result<EventSubReconcileReportDto>> ReconcileAsync(Guid broadcasterId, CancellationToken ct = default);

    // Force a transport reconnect (admin/diagnostic). Side effect: drops current session, re-runs welcome+resubscribe.
    Task<Result> ReconnectAsync(CancellationToken ct = default);
}
```

### 3.3 `IEventSubTransport` — deployment-profile adapter seam

`NomNomzBot.Application/Contracts/Twitch/`. Two impls, one chosen by DI (§7). The hosted service owns lifecycle; the transport owns the wire.

```csharp
public interface IEventSubTransport
{
    EventSubTransportKind Kind { get; }

    // Bring the transport up: connect WS / ensure conduit+shards exist. Returns the session/conduit handle
    // the service uses when creating subscriptions. Idempotent; safe to call after reconnect.
    Task<Result<EventSubTransportHandle>> StartAsync(CancellationToken ct = default);

    // Create one subscription at Twitch under this transport (session_id for WS, conduit_id for conduit).
    // Returns Twitch's subscription id + cost + status. Failure carries Twitch error body.
    Task<Result<TwitchSubscriptionResult>> CreateSubscriptionAsync(EventSubSubscriptionRequest request, EventSubTransportHandle handle, CancellationToken ct = default);

    // DELETE /eventsub/subscriptions?id=. Idempotent (404 → Success).
    Task<Result> DeleteSubscriptionAsync(string twitchSubscriptionId, CancellationToken ct = default);

    // List the app/user's current subscriptions at Twitch (paged, follows cursor). For ReconcileAsync.
    Task<Result<IReadOnlyList<TwitchSubscriptionResult>>> ListSubscriptionsAsync(Guid broadcasterId, CancellationToken ct = default);

    // Gracefully tear down (close WS / leave conduit shards). Called on shutdown.
    Task StopAsync(CancellationToken ct = default);
}
```

- **WebSocket impl** (`WebSocketEventSubTransport`, lite/self-host): `StartAsync` connects `wss://eventsub.wss.twitch.tv/ws`, awaits `session_welcome`, returns a handle carrying `SessionId`; its own receive loop reads `notification`/`session_reconnect`/`revocation`/`session_keepalive` frames and forwards each notification envelope to `INotificationDispatcher`. Reconnect/backoff lives here.
- **Conduit impl** (`ConduitEventSubTransport`, SaaS): `StartAsync` ensures the app-global `EventSubConduit` row + shards exist (creating via Helix if absent), returns a handle carrying `ConduitId`; notifications arrive out-of-band via the webhook controller (§5), not a receive loop. `CreateSubscriptionAsync` uses `transport: { method: "conduit", conduit_id }`.

### 3.4 `INotificationDispatcher` — envelope → domain events + journal

`NomNomzBot.Application/Contracts/Twitch/`. The single place a raw EventSub notification is turned into a journaled, deduped, fanned-out event. Called by **both** transports (WS receive loop and webhook controller) so dedupe/journal logic is not duplicated.

```csharp
public interface INotificationDispatcher
{
    // Idempotently journal + fan out one notification.
    // 1. Dedupe on Twitch message-id via IdempotencyKey (Scope="eventsub", Key=messageId). Duplicate → WasDuplicate=true, no side effects.
    // 2. Journal via event-store's IEventJournal.AppendAsync(AppendEventRequest) -> EventRecord (canonical journal owner;
    //    StreamPosition allocated by event-store's ITenantSequenceAllocator under per-tenant lock, same txn). See event-store.md.
    // 3. Map to the matching per-topic DomainEventBase and IEventBus.PublishAsync.
    // 4. Emit EventSubNotificationJournaledEvent.
    // Returns the journaled event id + position (or duplicate signal).
    Task<Result<NotificationDispatchResult>> DispatchAsync(EventSubNotification notification, CancellationToken ct = default);
}
```

`EventSubNotification` (input contract, §4) is the transport-agnostic shape both transports build from their wire frame.

The dispatcher does **not** hand-roll a per-type `switch` for the typed mapping (step 3). It resolves the matching `IEventSubEventTranslator` from `IEventSubTranslatorRegistry` (§3.7) and lets it publish the concrete domain event(s). The typed publish rides the same `IEventBus`, so the journaling decorator (`JournalingEventBusDecorator`, event-store §7) records the derived `domain`-source row(s) in addition to the dispatcher's explicit raw `eventsub`-source row — **by design**: the raw row is the replay source, the derived rows feed live handlers and projections. The fan-out runs on the **genuinely-new path only** (a redelivery already fanned out on its first delivery); an unknown subscription type with no translator yet is journaled raw and skipped (no event is ever lost). A faulting translator is isolated and logged — one malformed payload never fails the journal append.

### 3.5 Journal append — owned by event-store

The append-only journal write path is **not** owned here. The dispatcher journals via event-store's `IEventJournal.AppendAsync(AppendEventRequest) -> EventRecord` and allocates `StreamPosition` via event-store's `ITenantSequenceAllocator` (per-tenant monotonic insert in the same transaction). See `event-store.md` (canonical journal owner) for the contract, the `AppendEventRequest`/`EventRecord` shapes, and the §1.4/Q.3 sequencing semantics. This subsystem does not define a local journal-writer interface.

### 3.6 `IWebhookSignatureVerifier` — SaaS HMAC verification (in-box crypto)

`NomNomzBot.Application/Contracts/Twitch/`. Used only by the conduit/webhook controller. In-box `HMACSHA256` per the stack decision (no 3rd-party).

```csharp
public interface IWebhookSignatureVerifier
{
    // Verify Twitch-Eventsub-Message-Signature = "sha256=" + HMAC(secret, id + timestamp + rawBody),
    // using CryptographicOperations.FixedTimeEquals. Rejects timestamps older than 10 minutes (replay).
    // Pure function (no I/O); secret supplied by the controller from config/DI.
    bool Verify(string messageId, string messageTimestamp, ReadOnlySpan<byte> rawBody, string signatureHeader, ReadOnlySpan<byte> secret);
}
```

### 3.7 `IEventSubEventTranslator` — typed fan-out (one per subscription type)

`NomNomzBot.Application/Contracts/Twitch/`. The typed half of the dispatcher. **One implementation per Twitch subscription type**, each owning the parse of that type's raw `event` payload into the strongly-typed domain event(s) it represents, and publishing them.

```csharp
public interface IEventSubEventTranslator
{
    string SubscriptionType { get; }   // "channel.follow" — the registry key
    Task TranslateAsync(EventSubNotification notification, CancellationToken ct = default);
}

public interface IEventSubTranslatorRegistry
{
    bool TryGet(string subscriptionType, [NotNullWhen(true)] out IEventSubEventTranslator? translator);
}
```

**Why one-per-type and not a `switch`.** `IEventBus.PublishAsync<TEvent>` binds handlers by the **compile-time** event type (`GetServices<IEventHandler<TEvent>>()`), so a typed event must be published from a call site that knows its concrete type — a generic `DomainEventBase` publish would resolve no handlers. Each translator knows its concrete type at its publish call site, which is exactly the seam that turns a raw envelope into typed delivery **without reflection** on the hot path. New type → new file (auto-discovered, §7); the dispatcher and registry never change. Translators are **pure parse + publish** (deps: `IEventBus` + `TimeProvider` only — no DbContext): they read the already-resolved tenant off the notification and stamp the injected clock. Implementations live in `NomNomzBot.Infrastructure/Platform/Eventing/Translators/` (engine plumbing), grouped one file per category; the domain events they publish live in their domain modules (§2). The abstract `EventSubEventTranslator` base supplies the clock + a typed `PublishAsync<TEvent>` helper; the `EventSubPayload` extension readers parse the raw `JsonElement` null-tolerantly (a missing/omitted field degrades to a default, never throws).

A translator may publish **zero** events (a payload variant that carries no actionable domain event), **one** (the common case), or **several** (e.g. `channel.subscription.message` → a resubscription event; `channel.moderate` → the specific moderation-action event its `action` discriminates). The translator owns that branching internally.

---

## 4. DTOs / contracts

`NomNomzBot.Application/DTOs/Twitch/EventSub/`. Records, init-only. App-facing JSON via Newtonsoft.Json; wire-frame parsing (`EventSubNotification` source) via System.Text.Json in the transport.

### 4.1 Transport-agnostic notification (transport → dispatcher)

```csharp
// Normalized envelope both WS and webhook transports produce from their wire frame.
public sealed record EventSubNotification
{
    public required string MessageId { get; init; }                  // Twitch message-id (dedupe key)
    public required DateTimeOffset MessageTimestamp { get; init; }
    public required string SubscriptionType { get; init; }           // "channel.follow", …
    public required string SubscriptionVersion { get; init; }
    public required Guid BroadcasterId { get; init; }                // resolved tenant (FK→Channels.Id)
    public required string TwitchBroadcasterUserId { get; init; }    // raw id from condition/payload
    public required JsonElement Event { get; init; }                 // raw event object (System.Text.Json)
}
```

### 4.2 Subscription request / result (service → transport → Twitch)

```csharp
public sealed record EventSubSubscriptionRequest
{
    public required Guid BroadcasterId { get; init; }
    public required string TwitchBroadcasterUserId { get; init; }
    public required string EventType { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyDictionary<string, string> Condition { get; init; }
    public EventSubTokenOwnerKind? UserAccessTokenOwner { get; init; } // which token authorizes (Broadcaster | Bot | Moderator)
}

public sealed record TwitchSubscriptionResult
{
    public required string TwitchSubscriptionId { get; init; }
    public required string Type { get; init; }
    public required string Version { get; init; }
    public required string Status { get; init; }                     // enabled | webhook_callback_verification_pending | …
    public required int Cost { get; init; }
    public string? SessionId { get; init; }
    public string? ConduitId { get; init; }
}

public sealed record EventSubTransportHandle
{
    public required EventSubTransportKind Kind { get; init; }
    public string? SessionId { get; init; }                          // WS
    public string? ConduitId { get; init; }                          // conduit
}
```

### 4.3 Journal append (dispatcher → event-store)

The journal-append request/result contracts (`AppendEventRequest` → `EventRecord`) are **owned by event-store** — see `event-store.md` (canonical journal owner). The dispatcher builds an `AppendEventRequest` (`Source="eventsub"`, `EventId` = MessageId-derived deterministic guid for idempotency, `Source`/`EventType`/`EventVersion`/`PayloadJson` ids-and-refs only, optional `SubjectKeyId`/`ActorUserId`/`ActorTwitchUserId`/`MetadataJson`, `OccurredAt`) and passes it to `IEventJournal.AppendAsync`. This subsystem does not define a local append-request DTO.

### 4.4 API response DTOs (controller surface)

```csharp
public sealed record EventSubSubscriptionDto(
    Guid Id, string EventType, string Version, string Transport,
    string Status, bool Enabled, int? Cost, string? TwitchSubscriptionId,
    string? LastError, DateTimeOffset? ExpiresAt, DateTimeOffset CreatedAt);

public sealed record EventSubReconcileReportDto(
    int Created, int Revoked, int Repaired, int Unchanged, IReadOnlyList<string> Errors);

public sealed record CreateEventSubSubscriptionRequest(string EventType);   // body for POST

public sealed record EventSubConduitDto(
    Guid Id, string ConduitId, int ShardCount, string Status,
    DateTimeOffset? LastReconciledAt, IReadOnlyList<EventSubConduitShardDto> Shards);

public sealed record EventSubConduitShardDto(
    string ShardId, string Transport, string Status, string? CallbackUrl, DateTimeOffset? AssignedAt);

public sealed record NotificationDispatchResult(Guid EventId, long StreamPosition, bool WasDuplicate);
```

---

## 5. Controller endpoints

Two controllers, both `NomNomzBot.Api/Controllers/V1/`, `[ApiVersion("1.0")]`, returning `StatusResponseDto<T>`/`PaginatedResponse<T>` via `ResultResponse`.

### 5.1 `EventSubController` — tenant subscription management (management plane)

`[Route("api/v{version:apiVersion}/eventsub")]`, `[Authorize]`, `[Tags("EventSub")]`. Tenant resolved from JWT `sub` → `BroadcasterId`. **Role gate.** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below). The keys are seeded global `ActionDefinitions` (§5.1.1); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Self-host collapses to "owner = full".

#### 5.1.1 Seeded `ActionDefinitions` rows (this subsystem owns these seed entries)

The three action keys the §5.1 gate resolves are **not** defined elsewhere — this subsystem adds them to the `[GLOBAL, seed]` `ActionDefinitions` seed set in the existing `DataSeeder` (the same seed pass that owns Domain-B B.3 rows, per `roles-permissions.md`). All three are channel-management config actions (Plane `management`, danger tier `low`, permit-grantable); `DefaultLevel` ships at `FloorLevel` and mirrors the §5.1 ladder (read ≥ Moderator 10, writes ≥ Editor 30). Without these rows the resolver fails closed and every EventSub management call 403s.

| `ActionKey` | `Plane` | `DefaultLevel` | `FloorLevel` | `FloorTier` | `IsGrantableViaPermit` | `Description` |
|---|---|---|---|---|---|---|
| `eventsub:read` | `management` | 10 (Moderator) | 10 (Moderator) | `low` | `true` | View this channel's EventSub subscription registry. |
| `eventsub:subscribe` | `management` | 30 (Editor) | 30 (Editor) | `low` | `true` | Create EventSub subscriptions and reconcile the registry for this channel. |
| `eventsub:unsubscribe` | `management` | 30 (Editor) | 30 (Editor) | `low` | `true` | Revoke EventSub subscriptions for this channel. |

`Id` is `Guid.CreateVersion7()` at seed time; the seed is idempotent on the `ActionKey` unique index (upsert, no duplicate on re-run). `Plane` uses the `AuthPlane` `[VC:enum]`, `FloorTier` the `DangerTier` `[VC:enum]` (matching B.3). The `POST /eventsub/reconcile` action reuses `eventsub:subscribe` (no separate key — reconcile only ever creates/repairs, never exceeds subscribe's authority).

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/eventsub/subscriptions` | `[FromQuery] PageRequestDto` | `PaginatedResponse<EventSubSubscriptionDto>` | management / Moderator · `eventsub:read` |
| POST | `/eventsub/subscriptions` | `CreateEventSubSubscriptionRequest` | `StatusResponseDto<EventSubSubscriptionDto>` | management / Editor · `eventsub:subscribe` |
| DELETE | `/eventsub/subscriptions/{id:guid}` | — (route `id`) | `StatusResponseDto<object>` | management / Editor · `eventsub:unsubscribe` |
| POST | `/eventsub/reconcile` | — | `StatusResponseDto<EventSubReconcileReportDto>` | management / Editor · `eventsub:subscribe` (reuses subscribe) |

### 5.2 `EventSubWebhookController` — SaaS conduit/webhook ingest (public, signature-gated)

`[Route("api/v{version:apiVersion}/eventsub/webhook")]`, `[AllowAnonymous]`, `[Tags("EventSub")]`. **Registered only in the SaaS DI profile** (conduit transport). Authentication is the HMAC signature, not JWT. Reads the **raw** request body (buffered) for signature verification before deserializing.

| Verb | Route | Request | Response | Auth |
|---|---|---|---|---|
| POST | `/eventsub/webhook` | raw body + `Twitch-Eventsub-*` headers | `200` (notification, text/plain echo of nothing) / `200` text `challenge` (verification) / `403` (bad signature) / `409`→`200` (duplicate, idempotent) | `[AllowAnonymous]`, `IWebhookSignatureVerifier` HMAC + replay window |

Behavior: verify signature (fail-closed `403`); branch on `Twitch-Eventsub-Message-Type`: `webhook_callback_verification` → return `200 text/plain` with the `challenge` value; `notification` → build `EventSubNotification`, call `INotificationDispatcher.DispatchAsync`, return `200`; `revocation` → mark registry row `revoked`, emit `EventSubRevokedEvent`, `200`. Never returns problem-details JSON to Twitch on the notification path (Twitch expects `2xx` text).

---

## 6. Pipeline actions

**None.** This subsystem ingests events; it does not author pipeline actions. EventSub topics fan out to existing per-topic domain events, which the **pipeline/event-response** subsystem consumes as triggers. No `ICommandAction` is added here.

---

## 7. DI registration

`NomNomzBot.Infrastructure/DependencyInjection.cs` (extends the existing block at lines 272–277, removing the old `TwitchEventSubService` singleton wiring). Transport is profile-selected by `platform-conventions`' `EventSubTransportMode` (lite/`self_host_*` = `EventSubTransportMode.WebSocket` → WebSocket wire kind; SaaS = `EventSubTransportMode.ConduitWebhook` → conduit+webhook wire kinds).

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `INotificationDispatcher` | `NotificationDispatcher` | Scoped | Dedupe + fan-out; journals via event-store's `IEventJournal`/`ITenantSequenceAllocator` (see event-store.md, canonical journal owner); resolves scoped `IEventBus` publish + the singleton translator registry. |
| `IEventSubEventTranslator` (many) | `*Translator` per subscription type | Singleton | Auto-discovered via `AddImplementationsOf<IEventSubEventTranslator>` (§3.7); pure parse + publish, stateless. Drop a file → live next boot. |
| `IEventSubTranslatorRegistry` | `EventSubTranslatorRegistry` | Singleton | Indexes the translator set by `SubscriptionType`; throws at construction on a duplicate type (fail fast). |
| `IWebhookSignatureVerifier` | `HmacWebhookSignatureVerifier` | Singleton | Pure; in-box `HMACSHA256`. (SaaS profile only.) |
| `IEventSubTransport` | `WebSocketEventSubTransport` | Singleton | **lite / `self_host_*`** profile branch. Owns the WS receive loop. |
| `IEventSubTransport` | `ConduitEventSubTransport` | Singleton | **SaaS** profile branch. Ensures conduit+shards. |
| `ITwitchEventSubService` / `IEventSource` | `TwitchEventSubHostedService` | Singleton | Same instance for both interfaces (`AddSingleton<TwitchEventSubHostedService>()` + two forwarding registrations). |
| `IHostedService` | → `TwitchEventSubHostedService` | hosted | `AddHostedService(sp => sp.GetRequiredService<TwitchEventSubHostedService>())`. Starts transport, drives reconnect + post-reconnect `ReconcileAsync`/resubscribe, calls scoped services via `IServiceScopeFactory` (singleton→scoped boundary, matching the existing `GetBotTokenAsync` pattern). |

Profile branch (pseudocode):
```csharp
if (deploymentMode is "saas")
{
    services.AddSingleton<IEventSubTransport, ConduitEventSubTransport>();
    services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookSignatureVerifier>();
    // EventSubWebhookController is discovered by MVC; gate its route via a feature/endpoint filter in SaaS only.
}
else // self_host_lite | self_host_full
{
    services.AddSingleton<IEventSubTransport, WebSocketEventSubTransport>();
}
// IEventJournal / ITenantSequenceAllocator registered by event-store (see event-store.md, canonical journal owner).
services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
services.AddSingleton<TwitchEventSubHostedService>();
services.AddSingleton<ITwitchEventSubService>(sp => sp.GetRequiredService<TwitchEventSubHostedService>());
services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<TwitchEventSubHostedService>());
services.AddHostedService(sp => sp.GetRequiredService<TwitchEventSubHostedService>());
```

EF configurations (`NomNomzBot.Infrastructure/Persistence/Configurations/`): `EventSubSubscriptionConfiguration` (replaces `EventSubscriptionConfiguration`), `EventSubConduitConfiguration`, `EventSubConduitShardConfiguration`, `EventJournalConfiguration`, `EventSubjectKeyConfiguration`, `IdempotencyKeyConfiguration`, `TenantSequenceConfiguration`. All `[VC:JSON]` columns use the hand-rolled Newtonsoft converter convention; **no `HasColumnType("jsonb")`/`HasDefaultValueSql`**.

---

## 8. Dependencies (from the stack doc)

| Dependency | Party | Use here |
|---|---|---|
| `System.Net.WebSockets` (`ClientWebSocket`) | 1st (in-box) | WebSocket transport (lite/self-host). |
| `System.Net.Http` / `IHttpClientFactory` | 1st (in-box) | Helix `POST/DELETE/GET /eventsub/subscriptions`, conduit provisioning. |
| `Microsoft.Extensions.Http.Resilience` 10.7.0 | 2nd | Retry/circuit-breaker on the Helix client (shared with the Helix subsystem). |
| `System.Security.Cryptography` (`HMACSHA256`, `CryptographicOperations.FixedTimeEquals`) | 1st (in-box) | Webhook signature verification (SaaS). |
| `System.Text.Json` (`.Strict` on inbound) | 1st (in-box) | EventSub **wire-frame** parse (hot path, untrusted). |
| Newtonsoft.Json | app JSON | `[VC:JSON]` entity columns + app-facing DTO serialization (project convention). |
| `Microsoft.EntityFrameworkCore` 10.0.9 (+ Npgsql 10.0.2 / EF.Sqlite 10.0.9) | 2nd/3rd | Registry + journal persistence; provider chosen by profile adapter. |
| `Microsoft.Extensions.Hosting` (`IHostedService`, `IServiceScopeFactory`) | 1st | Lifecycle host + singleton→scoped boundary. |
| `Microsoft.Extensions.Logging` (`ILogger`, `[LoggerMessage]`) | 1st | Structured logs (scrub tokens/usernames; `tenant_id` scope). |

No new 3rd-party dependency. (Conduit transport stays in-box; no `TwitchLib`.)

---

## 9. Decisions (resolved)

1. **`EventId` derivation for idempotency.** `EventId` is a `Guid` derived deterministically from the Twitch `message-id` (UUIDv5, namespace-scoped to `eventsub`, name = `message-id`). The journal `Unique(EventId)` and the `IdempotencyKey(Scope="eventsub", Key=message-id)` therefore agree, and replays are exact (the same wire message always maps to the same journal row).
2. **`channel.chat.message` journaling volume.** Chat messages **are** journaled (`Source="eventsub"`) for replay/projection parity, consistent with `EventJournal.EventType` listing `channel.chat.message`. Chat does not bypass the journal; the journal is the canonical, append-only source the projection and replay paths read.

### 9.1 Chat read/send boundary (relationship to `IChatProvider`)

Chat **read** (ingest) is EventSub `channel.chat.message` on **both** deployment profiles — this subsystem journals and fans it out via `INotificationDispatcher` like any other notification; the Kick/YouTube/X sources ingest their chat the same way through `IEventSource`. There is **no per-channel chat socket on any profile** for ingest. Chat **send** is **not** this subsystem — it is the platform-keyed `IChatProvider` seam behind `IChatProviderRouter` (`HelixChatProvider` = Helix `POST /helix/chat/messages` for Twitch; Kick/YouTube/X providers beside it; scaling-qos.md §6). This subsystem owns inbound events only; outbound chat is the chat-provider seam's concern.

---

## 10. Zero-downtime handover — conduit WebSocket shards (decided 2026-09-29)

**Problem.** A WebSocket subscription dies with its session. The blue/green deploy stops the old colour, the standby then opens its sessions and re-subscribes: 2–5 s with no subscription, and every event in that window is lost.

**Verified Twitch facts** (live docs, 2026-09-29):

| # | Fact | Source |
|---|------|--------|
| a | "When EventSub sends a notification to a disabled shard, it attempts to resend the notification to another shard. If that second shard is also disabled, EventSub will drop the notification." | [handling-conduit-events](https://dev.twitch.tv/docs/eventsub/handling-conduit-events/) |
| b | "you have 10 seconds from the time you receive the Welcome message to associate it with a shard." An unassigned session is closed. | same |
| c | Notifications are routed "using a hashing mechanism to determine which shard notifications for a particular channel ID will be delivered on". Max 5 enabled conduits per client, 20,000 shards per conduit (Create Conduits body: max 200). | same; [API ref — Create Conduits](https://dev.twitch.tv/docs/api/reference/#create-conduits) |
| d | Update Conduit Shards returns per-shard `errors`; the docs do not promise that reassignment is atomic. The `session_id` "must be from a WebSocket connection established with the same Client ID". | [API ref — Update Conduit Shards](https://dev.twitch.tv/docs/api/reference/#update-conduit-shards) |
| e | "Creating a conduit and associating subscriptions with it requires an app access token." Chat topics on an app token need `user:read:chat` + `user:bot` from the chatting user and `channel:bot` from the broadcaster **or** moderator status. | conduit page; [subscription types](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/) |
| f | No cost for subscriptions the user authorized; app `max_total_cost` 10,000. A conduit with no reactivated shard for 72 h is deleted. | [manage-subscriptions](https://dev.twitch.tv/docs/eventsub/manage-subscriptions/); conduit page |

**Rejected.** *Standby parks an unassigned session and the old instance PATCHes its shard over at stop* — fact (b) closes the parked session after 10 s, so the standby would have to re-dial every 10 s and the stop would race that loop. *One conduit shard per instance with more than two shards* — two disabled shards at once lets fact (a) resend to a disabled shard and drop the event.

**Design — one conduit, two WebSocket shards, one shard per running instance.**

1. **Conduit mode is on when the app access token can be minted** (client id + secret configured). A secret-less self-host stays on the per-owner WebSocket sessions (§3.3) — nothing changes for it.
2. **Provisioning.** The active or standby instance ensures ONE conduit with `shard_count = 2`, persisted in `EventSubConduits`. On boot it lists the app's conduits (Get Conduits): the persisted id is reused when Twitch still has it (shard count corrected to 2 when it drifted); otherwise a new conduit is created and the row is repointed (BYOC: a different client id never sees the old conduit, so it gets its own). An unpersisted conduit is **never adopted** — another deployment on the same client id (local dev next to the dev server) owns it.
3. **Every running instance holds exactly one shard** through one WebSocket session (owner key `conduit-shard`). On the session welcome it immediately PATCHes the first shard that is not `enabled` onto its session id (inside the 10 s window), then re-reads the shards to confirm the shard now carries its session (a concurrent claimer loses cleanly and its session is re-dialled). This runs for the **standby too** — the standby receives events from the moment it is up.
4. **Subscriptions live on the conduit**, created with the app token (`transport: { method: "conduit", conduit_id }`), only by the lease holder. They outlive every session, so a handover never re-subscribes anything: the successor's startup reconcile adopts rows whose `ConduitId` equals the current conduit without a Twitch call.
5. **Invariant: at most one shard is disabled at any time** (with two shards, fact (a) then resends every notification of the disabled shard to the enabled one). Steady state: one instance, one enabled shard, one disabled shard. Overlap: two enabled shards, events split by channel hash — **receiving is not processing**: see §10.1.
6. **Stop.** The outgoing instance asks `HasWaitingSuccessorAsync`. If a successor waits, it polls Get Conduit Shards (≤ 10 s) until a shard other than its own is `enabled`, and only then closes its sockets and releases the lease. Its shard turns disabled and fact (a) resends that shard's notifications to the successor. No successor → it closes at once (single-instance stop, the chat announcement still fires).
7. **Duplicates.** Each notification goes to one shard; a resend is the same notification. The journal's `Unique(EventId)` (§9 decision 1) makes any repeat a no-op. There is never a second subscription for the same topic, so no second copy of an event exists.
8. **Topics refused on the conduit** (a 403 because the channel lacks `channel:bot`/moderator status, or the single-account self-host lacks `user:bot`) fall back to the per-owner WebSocket session for that topic only — the channel keeps working. That topic keeps the old handover gap until the grant arrives; the missing grant is surfaced through the usual reauth event, never a forced logout.
9. **Crash.** A crashed instance's shard turns disabled. During an overlap the other instance holds the second shard, so nothing is lost. In steady state both shards are disabled until the restarted instance claims one: events in that window are dropped by Twitch — the minimum possible, as no process is listening; the gap backfill (§7) repairs what Helix can report.
10. **Migration from per-owner sessions (first deploy of this version).** The old version still runs its per-owner WebSocket subscriptions. The new instance claims a shard but creates **no** conduit subscriptions while standby (a second subscription for the same topic has a different message id, so dedupe could not catch the double copy). When it takes the lease it deletes each row's old WebSocket subscription with that owner's token and recreates it on the conduit. This first deploy therefore still has the old gap **once**; every later deploy is gap-free.
11. **Readiness.** Only a **standby** — an instance that lost the chat-ingest lease to a live holder, which keeps serving behind the proxy — fails `/health/ready` (check `eventsub-handover`) until its shard is bound, so the deploy never stops the old instance while nobody else receives. A lone instance (fresh boot, crash-restart, single-instance self-host, Twitch down at boot) is the only upstream and reports ready at once whatever its shard is doing — holding it back would 503 the whole port (background integrations never gate readiness). It is a latch (once ready, always ready — a later shard reconnect never drops a live instance from the proxy pool) with a **60 s** bound: the deploy polls for up to 300 s, and past 60 s the outgoing instance's own bounded successor wait (point 6) still protects the handover.
12. **One gate for every subscription mutation.** Opening an owner session, creating and deleting subscriptions (re-auth recovery, onboarding seed, reconcile, scope handlers, dashboard resubscribe, `BotLifecycleService` sync, the stale-session cleanup on a welcome) all run through `TwitchEventSubHostedService`, which refuses them unless this instance is the decided lease holder. The gate is **closed until StartAsync has decided** — on 2026-09-29 a token-refresh recovery ran on the incoming colour before that decision and deleted 28 of the live colour's subscriptions. During an overlap a dashboard change that lands on the standby answers `SERVICE_UNAVAILABLE`; the live instance's reconcile is the source of truth.
13. **Reconcile** lists the app's conduit subscriptions per channel (`GET /eventsub/subscriptions?user_id=` on the app token — a user token never sees them). A conduit row Twitch no longer lists is re-created; one on a replaced conduit is deleted and re-created. If that listing fails, reconcile degrades to the user-token set and conduit rows are adopted as before.

### 10.1 One processor at a time — the shared inbox

**Problem.** During the overlap Twitch delivers half the channels to the standby's shard. Much of the bot's state is per instance and single-writer by design (the song-request `FairQueue` and its persisted queue, command cooldowns, timer line counters, lurk state, `ChannelRegistry` caches, the music hand-over). If the standby processed its half, two writers would clobber that state (e.g. a `!sr` lands in the standby's queue, then each instance overwrites the other's persisted queue).

**Decision.** The receiver is not the processor. Every shard receiver writes the raw notification to a durable inbox table, `EventSubInboxMessages` (`MessageId` unique, drained in `ReceivedAt, Id` order); **only the chat-ingest lease holder drains it** and dispatches through `INotificationDispatcher`, deleting each row after its dispatch. The journal's `Unique(EventId)` stays the second guard (a crash between dispatch and delete re-dispatches into a journal no-op).

- **Postgres only.** The inbox is registered on the same axis as the advisory-lock guard: the database two instances can share. It lives in the database that already holds the lease and the journal, so the queue, its single consumer and the dedupe answer to one source of truth. On SQLite only one instance can exist, so notifications are dispatched in process as they arrive.
- **Why not Redis Streams.** Redis is selected separately from the database (the cache provider and its connection string are their own setting), so the two-instance axis is Postgres, not Redis; and it would split the truth: the lease in Postgres, the queue position in Redis, with no atomic link between "who holds the lease" and "who acked what".
- **Why not "standby buffers locally".** The standby's half of the channels would wait the whole overlap (readiness poll + drain, tens of seconds — `!sr` and commands visibly stall), ordering against the active instance's events is lost, and a standby that dies during the overlap loses its buffer. The shared inbox has none of these: the active instance processes the standby's events within one poll (250 ms).
- **Latency.** The active instance's own notifications wake its drain at once; the standby's arrive through the table and are picked up within 250 ms.

**Takeover order (never both, never neither):**

1. Old: `_handedOver = true` → stops draining; the notification in hand finishes, everything still waiting stays in the inbox.
2. Old: waits for the successor's shard to be enabled (point 6), closes its sockets; notifications that reach it until the close still go into the inbox.
3. Old: releases the lease.
4. New: takes the lease (standby poll, ≤ 2 s), starts draining from the oldest waiting row — including everything the old instance left.

Between steps 1 and 4 notifications are stored, not processed: nothing is lost, and the delay is bounded by the successor wait plus one standby poll.
