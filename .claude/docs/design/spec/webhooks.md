# Webhooks — Interface Specification (implementable)

**Subsystem area:** user / third-party **webhooks**, both directions —
- **Inbound:** an external service (Ko-fi, GitHub, Zapier/IFTTT/Make, Stream Deck, any Standard-Webhooks sender) `POST`s to a per-channel opaque URL; the request is verified per-adapter, deduped, journaled as a first-class event, and fanned out to the channel's pipelines/event-responses.
- **Outbound:** the channel emits signed `POST`s to author-configured endpoints (Standard Webhooks signing) when a pipeline action fires, with at-least-once delivery, retry, dead-letter, and auto-disable.

This is **distinct from Twitch EventSub** (`twitch-eventsub.md`) — that is the platform's *own* first-party ingest of Twitch's events. This subsystem is the *user-facing* integration surface for arbitrary third parties. The two share only the journal (`event-store.md`) and the in-box HMAC verifier *pattern*; they are separate subsystems with separate addressing, separate registries, and separate adapters.

**Status:** directly-implementable. Owner codes from this. All signatures fully typed. Namespace is `NomNomzBot.*` in every `.cs` file; folders/products are `NomNomzBot.*`.

**Grounding:**
- Locked schema — new Domain H sub-block H.8–H.10 (Webhooks); reuses O.1 `EventJournal` (+`Source="webhook"`), O.4 `IdempotencyKey`, H.7 `HttpEgressAllowlist`. `docs/design/2026-06-16-database-schema.md`.
- Reused contracts (do **not** reinvent): `event-store.md` (§3.1 `IEventJournal`, §3.7 `ITenantSequenceAllocator`, §3.10 `IIdempotencyGuard`), `commands-pipelines.md` (§3.8 `IEventResponseService.TriggerAsync`, §6.1 `http_request`/`ICommandAction`, §6.3 template engine, H.1 `Pipeline.TriggerKind`, I.2 `EventResponse`), `code-execution-sandbox.md` (§7 SSRF egress: `egress-allowlisted` named client + `ConnectCallback` pinned-IP connect, H.7 allowlist), `gdpr-crypto.md` (§3.1 `IKeyVault`, §3.2 `IFieldCipher`, §3.4 `ISubjectKeyService`), `platform-conventions.md` (Channels.OverlayToken addressing model, §3.7 `IRateLimiterPartitionStore`, §3.8 `IRunOnceGuard`, `ExposureModel`), `twitch-eventsub.md` (§3.6 `IWebhookSignatureVerifier` in-box HMAC pattern — mirrored, not shared).
- Stack — `docs/design/2026-06-16-stack-and-dependencies.md` (in-box crypto only, hand-rolled HTTP egress, EF Core 10, profile adapters, Newtonsoft for `[VC:JSON]`).

### Binding conventions applied here
- .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way (no `.Result`/`.Wait`).
- `Result<T>` (`NomNomzBot.Application.Common.Models`) over exceptions/null. `Result` for void-success. Never return null.
- Repository + `IUnitOfWork` (`NomNomzBot.Application.Contracts.Persistence`) — no raw `DbContext` in services/controllers.
- DI via typed interfaces; **no MediatR, no Roslyn**.
- Responses: `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/...")]`.
- App JSON: **Newtonsoft.Json** for `[VC:JSON]` EF converters (schema §1.4). **Inbound webhook bodies are untrusted** — they are read as a **raw buffered byte body** for signature verification, then parsed with `System.Text.Json` in `.Strict` mode (hot, untrusted path), exactly as `twitch-eventsub.md` parses wire frames. App-facing DTOs ride the host's System.Text.Json.
- Surrogate PKs = `Guid` via `Guid.CreateVersion7()`; append-only delivery log uses `bigint` identity PK. Twitch ids are indexed attribute columns, never keys. Tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`).
- Soft-delete (`IsDeleted`/`DeletedAt`) global filter on `[soft-delete]` tables; append-only tables carry `CreatedAt` only and do **not** inherit `UpdatedAt`/soft-delete.
- **In-box crypto only.** `System.Security.Cryptography.HMACSHA256` + `CryptographicOperations.FixedTimeEquals`; **no 3rd-party** signing/verification library. Signing/verification secrets are stored as AEAD ciphertext via `gdpr-crypto.md`'s `IFieldCipher`/`ISubjectKeyService` (AES-256-GCM, AAD binds tenant+endpoint), exactly as integration tokens are.

> **No pre-existing code to reconcile.** There is no webhook table, entity, service, or controller in the live codebase today (confirmed). This is a clean-slate new subsystem; everything below is net-new except the explicitly *reused* contracts named above.

---

## 1. Entities (locked-schema, this subsystem owns)

New Domain H sub-block **H.8–H.10**, placed beside `HttpEgressAllowlist` (H.7) — webhooks are the user-facing egress/ingress surface that reuses H.7's SSRF boundary, so they live in the same domain. Defined in `docs/design/2026-06-16-database-schema.md`; **do not redefine columns** — the schema is authoritative. EF entity classes live in `NomNomzBot.Domain/Entities/Webhooks/`; configs in `NomNomzBot.Infrastructure/Persistence/Configurations/Webhooks/`.

| # | Entity | PK | Kind | Key fields (from schema) |
|---|--------|----|------|--------------------------|
| H.8 | `OutboundWebhookEndpoint` | `Id guid` | `[soft-delete]` | `BroadcasterId guid`; `Name string(200)`; `Fqdn string(253)` (mirror of the H.7 `HttpEgressAllowlist.Fqdn` this endpoint must match); `HttpEgressAllowlistId guid?` (FK→H.7, the approved egress row); `Path string(255)?`; `SubscribedEventTypesJson text` **[VC:JSON]** `List<string>` (event types this endpoint receives — `*` = all); `BodyTemplate text?` (author template rendered by `ITemplateEngine`); `CustomHeadersJson text?` **[VC:JSON]** `Dictionary<string,string>` (author headers, also templated); `SigningSecretCipher string(512)` **[PII-shred]** (AEAD-wrapped `whsec_` secret); `SigningSecretNonce string(255)`; `SecondarySigningSecretCipher string(512)?` **[PII-shred]** (overlap-valid secret during rotation → multi-sig); `SecondarySigningSecretNonce string(255)?`; `EncryptionKeyId guid` (FK→`CryptoKey`); `IsEnabled bool`; `ConsecutiveFailureCount int`; `DisabledAt timestamp?` (set when auto-disabled); `DisabledReason string(255)?`; `LastDeliveryAt timestamp?`; `LastSuccessAt timestamp?`. **Unique** `(BroadcasterId, Name)`. |
| H.9 | `OutboundWebhookDelivery` | `Id bigint` | `[APPEND-ONLY]` | `BroadcasterId guid`; `EndpointId guid` (FK→H.8); `WebhookMessageId guid` (the `webhook-id` we sent — dedupe key the receiver sees); `JournalEventId guid?` (FK→`EventJournal.EventId` — the event that triggered this send); `EventType string(150)`; `Attempt int` (1-based); `Status string(20)` **[VC:enum]** (`pending`\|`delivered`\|`failed`\|`dead_letter`); `ResponseCode int?`; `DurationMs int?`; `NextRetryAt timestamp?`; `Error string(1000)?` (scrubbed transport/HTTP error). **Index** `(EndpointId, CreatedAt)`, `(Status, NextRetryAt)` (the retry-drain scan). |
| H.10 | `InboundWebhookEndpoint` | `Id guid` | `[soft-delete]` | `BroadcasterId guid`; `Name string(200)`; `Token string(64)` **Unique** (opaque unguessable per-endpoint token, OverlayToken model — the URL path segment; not PII); `AdapterKind string(20)` **[VC:enum]** (`supporter`\|`github`\|`generic`); `VerificationSecretCipher string(512)` **[PII-shred]** (AEAD-wrapped per-provider secret/token); `VerificationSecretNonce string(255)`; `EncryptionKeyId guid` (FK→`CryptoKey`); `GenericConfigJson text?` **[VC:JSON]** (`GenericInboundConfig`: signature header name, signing-string template, or shared-secret-in-body field — only for `AdapterKind=generic`); `TargetPipelineId guid?` (FK→`Pipelines` H.1 — the pipeline to run on a verified hit; null = fan out via `IEventResponseService`); `TargetEventType string(100)?` (override the derived `webhook.<provider>.<kind>` event type); `IsEnabled bool`; `LastReceivedAt timestamp?`; `ReceiveCount bigint`. **Unique** `Token`, `(BroadcasterId, Name)`. |

**Cross-subsystem references (owned elsewhere — referenced, not redefined):** `Channels` (A.2, tenant root), `Users` (A.1, `ApprovedByUserId`), `HttpEgressAllowlist` (H.7, the SSRF allowlist row each outbound endpoint pins to), `EventJournal` (O.1, inbound events appended with `Source="webhook"`), `IdempotencyKey` (O.4, inbound dedupe + outbound idempotency — **no new table**), `CryptoKey` (Q.1, the DEK behind every `*Cipher`), `Pipelines` (H.1, inbound target), `EventResponses` (I.2, inbound fan-out target).

> **Reuse, do not duplicate (binding).** Outbound SSRF safety is **entirely** H.7 + the sandbox `egress-allowlisted` client — H.8 stores the per-endpoint `whsec_`/template/subscription set and **points at** an H.7 row for the actual egress boundary. Inbound replay/dedupe is **entirely** O.4 `IdempotencyKey` via `IIdempotencyGuard` — H.10 introduces **no** dedupe column. Per-tenant ordering on inbound journal events is **entirely** `ITenantSequenceAllocator` (`event-store.md` §3.7) — free `StreamPosition`.

---

## 2. Domain events

Namespace `NomNomzBot.Domain.Events.Webhooks`. All are `sealed record` deriving the canonical `DomainEventBase` (`platform-conventions.md` §2.0 — `EventId`/`BroadcasterId : Guid`/`OccurredAt` provided; **do not** redeclare them). These are bus events (`IEventBus.PublishAsync`), consumed by the dashboard activity feed, the outbound delivery worker, and the projection that maintains endpoint health counters.

```csharp
namespace NomNomzBot.Domain.Events.Webhooks;

using NomNomzBot.Domain.Events;
using NomNomzBot.Domain.Enums;

/// <summary>An inbound webhook was verified, deduped, and journaled (Source="webhook"). Fans out to pipelines/event-responses.</summary>
public sealed record InboundWebhookReceivedEvent(
    Guid InboundEndpointId,
    WebhookAdapterKind Adapter,
    string EventType,                 // "webhook.<provider>.<kind>", e.g. "webhook.kofi.tip"
    Guid JournalEventId,              // EventJournal.EventId of the appended event
    long StreamPosition,
    string ProviderEventId,           // the dedupe key (kofi_transaction_id / X-GitHub-Delivery / generic id)
    bool WasDuplicate                 // true = idempotency short-circuit, no fan-out happened
) : DomainEventBase;

/// <summary>An inbound webhook was rejected before any side effect, on a RESOLVED endpoint (bad signature / replay / disabled / over-limit / malformed).
/// NOT emitted for the UnknownEndpoint (404) path — see §5.2 step 8: an unauthenticated unknown-token flood must not amplify into the bus,
/// so InboundEndpointId is always non-null here and `unknown_endpoint` is never a published Reason (it is metrics-only on the pre-resolution limiter).</summary>
public sealed record InboundWebhookRejectedEvent(
    Guid InboundEndpointId,           // always resolved — unknown-token 404 does not emit this event
    WebhookAdapterKind Adapter,
    WebhookRejectReason Reason,       // invalid_signature | replay_window | disabled | payload_too_large | unsupported_media_type | rate_limited | malformed
    int HttpStatus                    // the status returned to the caller (4xx)
) : DomainEventBase;

/// <summary>An outbound delivery was enqueued (a pipeline send_webhook action or a matching event fired).</summary>
public sealed record OutboundWebhookEnqueuedEvent(
    Guid OutboundEndpointId,
    Guid WebhookMessageId,            // the webhook-id we will sign and send
    string EventType,
    Guid? JournalEventId
) : DomainEventBase;

/// <summary>An outbound delivery attempt finished (one row per attempt). Success or a retriable/terminal failure.</summary>
public sealed record OutboundWebhookAttemptedEvent(
    Guid OutboundEndpointId,
    Guid WebhookMessageId,
    int Attempt,
    WebhookDeliveryStatus Status,     // delivered | failed | dead_letter
    int? ResponseCode,
    DateTime? NextRetryAt
) : DomainEventBase;

/// <summary>An outbound endpoint was auto-disabled after N consecutive failures (default 20). Drives the "needs attention" UI.</summary>
public sealed record OutboundWebhookAutoDisabledEvent(
    Guid OutboundEndpointId,
    int ConsecutiveFailureCount,
    string Reason
) : DomainEventBase;
```

**New enums** (namespace `NomNomzBot.Domain.Enums`, each `[VC:enum]` ↔ schema text):

```csharp
namespace NomNomzBot.Domain.Enums;

// Supporter: the generic supporter webhook adapter (Ko-fi/Patreon/Fourthwall/Shopify) — verifies the per-provider HMAC
//            (Patreon MD5, Fourthwall/Shopify SHA256, Ko-fi token-equality) then calls ISupporterIngestService, which
//            dispatches by SupporterConnection.SourceKey. See supporter-events.md.
// CustomData: routes a verified inbound delivery to ICustomDataIngestService (resolves the source by InboundWebhookEndpointId). See custom-events.md.
public enum WebhookAdapterKind { Supporter, Github, Generic, CustomData }
public enum WebhookDeliveryStatus { Pending, Delivered, Failed, DeadLetter }
public enum WebhookRejectReason
{
    InvalidSignature, ReplayWindow, Disabled, PayloadTooLarge,
    UnsupportedMediaType, RateLimited, Malformed, UnknownEndpoint
}
```

---

## 3. Service interfaces

All in `NomNomzBot.Application.Services.Webhooks` (use-case services) and `NomNomzBot.Application.Contracts.Webhooks` (verifiers/adapters — pure, called from the untrusted controller). Implementations in `NomNomzBot.Infrastructure/Services/Webhooks/`. Every fallible op returns `Result`/`Result<T>`; `CancellationToken ct = default` last; `Guid broadcasterId` first where tenant-scoped.

### 3.1 `IInboundWebhookEndpointService` — inbound endpoint CRUD (H.10)

Owns `InboundWebhookEndpoint` rows. Mints the opaque token (`RandomNumberGenerator`, 64 url-safe chars, OverlayToken model). Stores the verification secret as AEAD ciphertext via `ISubjectKeyService.ProtectAsync`, with the **exact** argument mapping (the contract is `ProtectAsync(Guid cryptoKeyId, string plaintext, CipherAad aad, string resourceTable, string resourceColumn, ct)` and `CipherAad` is the **4-field record** `(TenantId, Provider, TokenType, KeyVersion)`, `gdpr-crypto.md` §3.4/§4.1 — **not** a `‖`-joined string):

- `cryptoKeyId` = `await ISubjectKeyService.GetOrCreateTenantKeyAsync(broadcasterId)` (the per-tenant DEK; its id is stored on `InboundWebhookEndpoint.EncryptionKeyId`).
- `aad` = `CipherAad(TenantId: broadcasterId.ToString(), Provider: "webhook:in", TokenType: endpointId.ToString(), KeyVersion: keyVersion)` — **identical on encrypt and decrypt** (decrypt fails closed on any AAD mismatch, so the field assignment is frozen here: `Provider` is the literal `"webhook:in"`, `TokenType` is the `endpointId`).
- `resourceTable` = `"InboundWebhookEndpoints"`, `resourceColumn` = `"VerificationSecretCipher"` — these seed the `KeyUsageBinding` so `RotateKeyAsync` re-encrypts the column (without them rotation silently skips the webhook secret).

```csharp
namespace NomNomzBot.Application.Services.Webhooks;

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;

public interface IInboundWebhookEndpointService
{
    /// Lists inbound endpoints for a channel, paged. Read-only. NEVER returns the verification secret (only a set/unset flag).
    Task<Result<PagedList<InboundWebhookEndpointDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    /// One endpoint by id, with its public ingest URL (computed from App:BaseUrl + token). Read-only. NOT_FOUND if absent/soft-deleted.
    Task<Result<InboundWebhookEndpointDto>> GetAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Creates an InboundWebhookEndpoint (H.10): mints the opaque Token, AEAD-encrypts the verification secret under the tenant DEK,
    /// persists the row. Returns the dto incl. the ingest URL. Validates GenericConfigJson when AdapterKind=generic.
    Task<Result<InboundWebhookEndpointDto>> CreateAsync(Guid broadcasterId, Guid actorUserId, CreateInboundWebhookRequest request, CancellationToken ct = default);

    /// Patches an endpoint (name, target pipeline/event-type, enabled, generic config). If RotateSecret=true, re-encrypts a new secret. Returns updated dto.
    Task<Result<InboundWebhookEndpointDto>> UpdateAsync(Guid broadcasterId, Guid endpointId, UpdateInboundWebhookRequest request, CancellationToken ct = default);

    /// Rotates the opaque Token (the URL changes); the old URL stops resolving immediately. Returns the new ingest URL.
    Task<Result<InboundWebhookEndpointDto>> RotateTokenAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Soft-deletes an endpoint. Idempotent-safe; NOT_FOUND if absent.
    Task<Result> DeleteAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);
}
```

### 3.2 `IInboundWebhookDispatcher` — verified ingest → journal → fan-out (the core ingest path)

`NomNomzBot.Application.Contracts.Webhooks`. The single place a raw inbound request becomes a verified, deduped, journaled, fanned-out event. Called by the `[AllowAnonymous]` ingest controller (§5). Mirrors `twitch-eventsub.md`'s `INotificationDispatcher` shape so dedupe/journal logic is not duplicated across adapters.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IInboundWebhookDispatcher
{
    /// Idempotently verify + journal + fan out one inbound request. THE DISPATCHER OWNS TOKEN RESOLUTION (single owner —
    /// the [AllowAnonymous] controller does NOT pre-resolve; it only does the IP/raw-token pre-limiter + size/method/content
    /// guards, then hands the raw request here). Pipeline:
    ///  1. Resolve the InboundWebhookEndpoint by Token (constant-time, soft-delete-aware lookup — a fixed-cost index probe
    ///     over the Unique(Token) column; the comparison MUST be constant-time so a 404 leaks no timing oracle, since this is
    ///     the only token-enumeration barrier). Unknown/soft-deleted -> Reject(UnknownEndpoint, 404). Disabled -> Reject(Disabled, 503).
    ///     On resolve, populate ResolvedEndpointId/ResolvedBroadcasterId/ResolvedAdapter on the result.
    ///  2. Select the IInboundWebhookAdapter by the resolved AdapterKind; adapter VERIFIES (per-provider scheme) over the RAW
    ///     body using the decrypted secret. Invalid signature/token -> Reject(InvalidSignature); stale timestamp (generic
    ///     adapter only — see below) -> Reject(ReplayWindow). Verify failures are ALWAYS 4xx, never processed.
    ///  3. Adapter PARSES the raw body into a ParsedInboundEvent (kind + flattened variable bag + ProviderEventId).
    ///  4. Compute the dedup CLAIM KEY (per-adapter — never let one fully-attacker-controlled body field be both the auth and
    ///     the dedup identity for no-HMAC adapters):
    ///        - github / generic-with-HMAC+timestamp: Key = ProviderEventId (the body/secret is HMAC-bound, so a chosen-id
    ///          pre-claim requires the secret AND a valid signature; ProviderEventId alone is a safe identity).
    ///        - supporter-no-HMAC (Ko-fi) / generic-shared-secret-in-body (NO HMAC): Key = lowerhex(SHA-256(rawBody)) + ":" + ProviderEventId.
    ///          Folding the server-observed body hash in means a forged request that pre-claims a genuine future
    ///          transaction's ProviderEventId cannot SHADOW that distinct legit event (its body differs -> different key),
    ///          closing the chosen-id suppression attack. ReceivedAtUtc is available for diagnostics but is NOT in the key
    ///          (it must stay stable across a provider's legit redelivery of the SAME bytes so retries dedup correctly).
    ///  4b. Dedupe via IIdempotencyGuard.TryClaimAsync(IdempotencyClaimRequest(
    ///        Scope="webhook:in:{endpointId}", Key=<step-4 key>, BroadcasterId=resolvedBroadcasterId,
    ///        ExpiresAt=now + WebhookReplayRetention)). WebhookReplayRetention is a SECURITY floor, not a perf knob
    ///        (§11 #1): 30 days for kofi/github (no timestamp -> the dedup row IS the sole replay barrier); 24h for the
    ///        generic adapter (its REQUIRED timestamp + 10-min tolerance is the primary replay guard, dedup is the backstop).
    ///        The retention is fixed in config and the pruner MUST NOT shorten it below this floor. Duplicate
    ///        (IsFirst=false) -> WasDuplicate=true, no side effect, 200.
    ///  5. Append to the journal via IEventJournal.AppendAsync(Source="webhook", EventType="webhook.<provider>.<kind>",
    ///     EventId = WebhookEventId(resolvedBroadcasterId, endpointId, ProviderEventId) — a deterministic UUIDv5 (§3.2.1)
    ///     SALTED with endpoint+tenant, NOT ProviderEventId alone, so the global Unique(EventJournal.EventId) can never
    ///     collide across endpoints/tenants that happen to receive the same small/guessable provider id)
    ///     -> StreamPosition allocated by ITenantSequenceAllocator in the same txn (event-store.md).
    ///  6. Fan out: if TargetPipelineId set -> IPipelineEngine.ExecuteAsync with the system-actor webhook PipelineRequest
    ///     (§3.2.2 — non-user trigger); else IEventResponseService.TriggerAsync(broadcasterId, EventType, variableBag).
    ///  7. Emit InboundWebhookReceivedEvent; complete the idempotency claim.
    /// Returns the verified+journaled result (or a typed rejection the controller maps to an HTTP status).
    Task<Result<InboundDispatchResult>> DispatchAsync(InboundWebhookRequest request, CancellationToken ct = default);
}
```

#### 3.2.1 Deterministic journal `EventId` — endpoint-salted (no cross-tenant collision)

The inbound journal `EventId` is **not** derived from `ProviderEventId` alone. Provider ids are small/guessable/attacker-shaped (Ko-fi sequential transaction ids, a generic `$.id` of `1`, a GitHub delivery id two channels can both receive), and `EventJournal.Unique(EventId)` is **global** (not per-tenant). A `ProviderEventId`-only derivation would let endpoint A's id collide with endpoint B's: `AppendAsync` is idempotent-on-`EventId` and would either return A's existing row to B (cross-tenant lineage read) or silently swallow B's distinct event (cross-tenant denial). The eventsub precedent is safe only because Twitch message-ids are globally-unique UUIDs — webhook provider ids are not.

```csharp
// NomNomzBot.Infrastructure.Services.Webhooks — pure, deterministic, endpoint+tenant salted.
// Namespace GUID is a fixed compile-time constant owned by this subsystem (UUIDv5, RFC 9562 §5.5).
static readonly Guid WebhookNamespace = new("d6b4f0a2-9c7e-5b1d-8e3a-2f6c4b9a7e10");
public static Guid WebhookEventId(Guid broadcasterId, Guid endpointId, string providerEventId)
    => Uuid5.Create(WebhookNamespace, $"{broadcasterId:N}|{endpointId:N}|{providerEventId}");
```

It is per-endpoint unique by construction, so it **matches the `IdempotencyKey` per-endpoint scope** (`webhook:in:{endpointId}`) exactly — the two guards agree instead of disagreeing. `event-store.md` §4 `AppendEventRequest.Source="webhook"` note carries the same derivation.

#### 3.2.2 Webhook-trigger `PipelineRequest` — the non-user (system-actor) contract

An inbound third-party webhook has **no Twitch user**, but `PipelineRequest` (`commands-pipelines.md` §3.3) requires non-null `TriggeredByUserId:Guid` / `TriggeredByDisplayName:string`. When `TargetPipelineId` is set, the dispatcher builds the request with the **reserved system-actor sentinel** `WebhookSystemActor.UserId` (`Guid.Empty`, documented in `commands-pipelines.md` §3.3 as the non-user trigger id — never an FK to a real `Users` row; the engine's permission/concurrency gates treat `TriggeredByUserId == Guid.Empty` as "system trigger: skip per-user permission/cooldown checks, full concurrency admission still applies"):

```
TriggeredByUserId      = Guid.Empty           // WebhookSystemActor — system trigger, not a Users FK
TriggeredByDisplayName = "<provider>"         // e.g. "kofi" / "github" / "generic" — the wire-source label
TriggerKind            = "webhook"
RawMessage             = ""
Args                   = []                    // empty
RedemptionId/RewardId/MessageId = null
InitialVariables       = the seeded webhook.* / payload.* bag (§7), with payload.* TAINTED (§7)
```

The `IEventResponseService.TriggerAsync(broadcasterId, eventType, variables)` branch needs none of this (no actor parameter) and is used unchanged.

### 3.3 `IInboundWebhookAdapter` — per-provider verify + parse (multi-impl)

`NomNomzBot.Application.Contracts.Webhooks`. One implementation per `WebhookAdapterKind`; the dispatcher selects by `AdapterKind`. **Each provider dictates its own verification scheme** — this is the seam that keeps provider quirks out of the dispatcher. Pure (no DB/I-O); the secret is supplied decrypted by the dispatcher.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IInboundWebhookAdapter
{
    WebhookAdapterKind Kind { get; }

    /// Verify the request against the provider's own scheme using the decrypted secret. Pure, constant-time where it compares MACs.
    ///   - supporter: per SupporterConnection.SourceKey — Patreon = X-Patreon-Signature HMAC-MD5; Fourthwall = X-Fourthwall-Hmac-SHA256;
    ///              Shopify = X-Shopify-Hmac-SHA256 (dedup X-Shopify-Webhook-Id); Ko-fi = no HMAC, assert the JSON `verification_token`
    ///              field equals the secret (FixedTimeEquals on the bytes). Ko-fi has no timestamp -> replay resistance comes ONLY from the
    ///              body-hash dedup (§3.2 step 4, 30-day floor), low-assurance; the HMAC providers carry their own signature. See supporter-events.md §0 D3.
    ///   - github:  X-Hub-Signature-256 = "sha256=" + HMAC(secret, rawBody); FixedTimeEquals.
    ///              No timestamp -> replay resistance comes ONLY from the ProviderEventId dedup (§3.2 step 4, 30-day floor).
    ///   - generic: per GenericInboundConfig. HMAC mode REQUIRES TimestampHeaderName -> VerifyWithTimestamp (10-min tolerance)
    ///              so dedup is the BACKSTOP, not the sole guard (config-validate rejects HMAC mode without it). Shared-secret-
    ///              in-body mode (no HMAC) is low-assurance like Ko-fi: replay resistance is the body-hash dedup only.
    /// Returns Ok / InvalidSignature / ReplayWindow / Malformed.
    Result<WebhookVerification> Verify(InboundWebhookRequest request, ReadOnlySpan<byte> secret, GenericInboundConfig? genericConfig);

    /// Parse the raw body into a normalized event: the <kind> token + a flat string->string variable bag (seeded under webhook.*/payload.*)
    /// + the ProviderEventId used for dedupe (kofi_transaction_id / X-GitHub-Delivery id / configured generic id field).
    Result<ParsedInboundEvent> Parse(InboundWebhookRequest request, GenericInboundConfig? genericConfig);
}
```

### 3.4 `IInboundSignatureVerifier` — in-box HMAC primitive (shared by adapters)

`NomNomzBot.Application.Contracts.Webhooks`. The in-box `HMACSHA256` + `CryptographicOperations.FixedTimeEquals` primitive the GitHub and generic adapters call. **Mirrors** `twitch-eventsub.md` §3.6 `IWebhookSignatureVerifier` (in-box, no 3rd-party) but is a separate type — webhook signing strings differ per provider and the replay window is ours to set. Pure function; no I/O.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IInboundSignatureVerifier
{
    /// True iff expectedSignatureHeader == "<prefix>" + lowerhex(HMAC-SHA256(secret, signingString)), compared with FixedTimeEquals.
    /// `prefix` is the provider token (e.g. "sha256="); empty for raw-hex schemes. No timestamp logic here (provider-agnostic).
    bool Verify(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> signingString, string expectedSignatureHeader, string prefix);

    /// Same, but rejects when `timestampUnixSeconds` is older than `tolerance` (our-own / Standard-Webhooks-style inbound, default 10 min). Replay guard.
    bool VerifyWithTimestamp(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> signingString, string expectedSignatureHeader, string prefix, long timestampUnixSeconds, TimeSpan tolerance);
}
```

### 3.5 `IOutboundWebhookEndpointService` — outbound endpoint CRUD (H.8)

Owns `OutboundWebhookEndpoint` rows. Mints the `whsec_<base64>` signing secret, AEAD-encrypts it via `ISubjectKeyService.ProtectAsync` with the **same explicit mapping** as §3.1 (`cryptoKeyId` from `GetOrCreateTenantKeyAsync(broadcasterId)` → stored on `EncryptionKeyId`; `CipherAad(TenantId: broadcasterId.ToString(), Provider: "webhook:out", TokenType: endpointId.ToString(), KeyVersion: keyVersion)`, identical encrypt/decrypt). **Both** secret columns get a `KeyUsageBinding` so rotation re-encrypts each: `resourceTable="OutboundWebhookEndpoints"` with `resourceColumn="SigningSecretCipher"` (primary) **and** `resourceColumn="SecondarySigningSecretCipher"` (the overlap-valid rotation secret — bound the same way, else `RotateKeyAsync` skips it). **Requires** the target FQDN to already have an enabled `HttpEgressAllowlist` (H.7) row for the tenant — creation fails `EGRESS_NOT_ALLOWED` otherwise (broker pattern: the URL/secret live on the endpoint, the SSRF boundary lives on H.7).

```csharp
namespace NomNomzBot.Application.Services.Webhooks;

public interface IOutboundWebhookEndpointService
{
    /// Lists outbound endpoints for a channel, paged, with health (ConsecutiveFailureCount, DisabledAt, LastSuccessAt). Read-only. Never returns the secret.
    Task<Result<PagedList<OutboundWebhookEndpointDto>>> ListAsync(Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    Task<Result<OutboundWebhookEndpointDto>> GetAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Creates an OutboundWebhookEndpoint (H.8): validates the Fqdn matches an enabled HttpEgressAllowlist (H.7) row (else EGRESS_NOT_ALLOWED),
    /// mints + AEAD-encrypts the whsec_ secret, persists. Returns the dto WITH the plaintext secret ONCE (create-time reveal, never re-readable).
    Task<Result<OutboundWebhookEndpointCreatedDto>> CreateAsync(Guid broadcasterId, Guid actorUserId, CreateOutboundWebhookRequest request, CancellationToken ct = default);

    /// Patches an endpoint (name, subscribed event types, body/header templates, enabled). Returns updated dto.
    Task<Result<OutboundWebhookEndpointDto>> UpdateAsync(Guid broadcasterId, Guid endpointId, UpdateOutboundWebhookRequest request, CancellationToken ct = default);

    /// Rotates the signing secret with OVERLAP: promotes the current secret to Secondary*, mints a new primary. Both sign outgoing requests
    /// (Standard Webhooks space-delimited multi-sig) until the secondary is cleared. Returns the new plaintext secret ONCE.
    Task<Result<OutboundWebhookEndpointCreatedDto>> RotateSecretAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Re-enables an auto-disabled endpoint: clears DisabledAt/ConsecutiveFailureCount, sets IsEnabled=true. Manual operator action.
    Task<Result<OutboundWebhookEndpointDto>> ReenableAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Soft-deletes an endpoint. Pending deliveries are abandoned (no further attempts). Idempotent. NOT_FOUND if absent.
    Task<Result> DeleteAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);

    /// Sends a synthetic "ping" event to the endpoint NOW (synchronous single attempt, no retry/dead-letter) so the author can verify wiring. Returns the attempt result.
    Task<Result<WebhookTestResultDto>> SendTestAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);
}
```

### 3.6 `IOutboundWebhookDispatcher` — enqueue + sign + deliver (the core egress path)

`NomNomzBot.Application.Contracts.Webhooks`. Enqueues a delivery (claims an idempotency key so the same event never double-sends), signs per Standard Webhooks, and performs one attempt through the SSRF-hardened `egress-allowlisted` client. The retry/dead-letter loop is driven by `IWebhookDeliveryWorker` (§3.7), which calls `AttemptDeliveryAsync` again.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IOutboundWebhookDispatcher
{
    /// Fan-out entry point: for the given (broadcasterId, eventType, variableBag), finds every enabled OutboundWebhookEndpoint whose
    /// SubscribedEventTypes match, mints a webhook-id per endpoint, claims IIdempotencyGuard(IdempotencyClaimRequest(
    /// Scope="webhook:out", Key=webhook-id, BroadcasterId, ExpiresAt=now + WebhookOutboundIdempotencyRetention [§11 #2, 7 days])),
    /// renders BodyTemplate + each CustomHeaders value via ITemplateEngine.Render(template, variables) (commands-pipelines.md §6.3),
    /// inserts an OutboundWebhookDelivery(Attempt=1, Status=pending), emits
    /// OutboundWebhookEnqueuedEvent, then performs attempt #1 inline (fast path). Returns one result per endpoint matched.
    Task<Result<IReadOnlyList<OutboundEnqueueResult>>> EnqueueForEventAsync(Guid broadcasterId, string eventType, IReadOnlyDictionary<string, string> variables, Guid? journalEventId, CancellationToken ct = default);

    /// Enqueue + attempt a single explicit delivery to one endpoint (the send_webhook pipeline action path). Same idempotency/sign/attempt flow.
    Task<Result<OutboundEnqueueResult>> EnqueueForEndpointAsync(Guid broadcasterId, Guid endpointId, string eventType, IReadOnlyDictionary<string, string> variables, Guid? journalEventId, CancellationToken ct = default);

    /// Performs ONE delivery attempt for an existing pending/failed OutboundWebhookDelivery row: re-signs (current + secondary secret),
    /// POSTs via the egress-allowlisted client through its SHARED ConnectCallback SSRF core (FQDN-pinned, https-only, no redirects,
    /// response capped) BUT under the TRUSTED-WEBHOOK header/body policy (§8.1): the dispatcher injects the webhook-id/webhook-timestamp/
    /// webhook-signature headers + author CustomHeaders + rendered body server-side (NOT the sandbox guest header allowlist), and the
    /// endpoint's H.7 row carries AllowRequestBody=true + MaxRequestBytes=256 KiB. 2xx -> Status=delivered, resets
    /// ConsecutiveFailureCount, sets LastSuccessAt. Non-2xx / conn / TLS / timeout -> Status=failed, schedules NextRetryAt (exp backoff+jitter)
    /// or Status=dead_letter when Attempt >= MaxAttempts; bumps ConsecutiveFailureCount and auto-disables at the threshold. Emits
    /// OutboundWebhookAttemptedEvent (+ OutboundWebhookAutoDisabledEvent when tripped). Returns the attempt outcome.
    Task<Result<OutboundAttemptResult>> AttemptDeliveryAsync(long deliveryId, CancellationToken ct = default);
}
```

### 3.7 `IWebhookDeliveryWorker` — retry/dead-letter drain (background)

`NomNomzBot.Application.Contracts.Webhooks`. Implemented by a `BackgroundService` (`PeriodicTimer`) that, under `IRunOnceGuard` (no double-deliver on multi-instance SaaS — same pattern as `TimerSchedulerService`), scans `OutboundWebhookDelivery` rows with `Status=failed` + `NextRetryAt <= now` and re-calls `AttemptDeliveryAsync`. Exposed as an interface so it is unit-testable without the host.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IWebhookDeliveryWorker
{
    /// Claims the per-instance run-once lease, selects due failed deliveries (Status=failed, NextRetryAt<=asOfUtc), and re-attempts each
    /// via IOutboundWebhookDispatcher.AttemptDeliveryAsync. Bounded batch. Returns the count attempted. Idempotent and safe to call concurrently
    /// (the run-once lease + per-row optimistic claim prevent double-send). No-op when the lease is held elsewhere.
    Task<Result<int>> DrainDueAsync(DateTime asOfUtc, CancellationToken ct = default);
}
```

### 3.8 `IOutboundWebhookSigner` — Standard Webhooks signing (in-box HMAC)

`NomNomzBot.Application.Contracts.Webhooks`. Builds the Standard Webhooks headers. In-box `HMACSHA256`; **no 3rd-party**. Pure.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IOutboundWebhookSigner
{
    /// Produces the Standard Webhooks signature header value: for each active secret, "v1," + base64(HMAC-SHA256(secret, "<id>.<timestamp>.<payload>")),
    /// space-delimited across secrets (primary + secondary during rotation) so the receiver accepts either during overlap. Also returns the
    /// webhook-id / webhook-timestamp header values. Pure; secrets supplied decrypted by the caller.
    WebhookSignatureHeaders Sign(string webhookId, long timestampUnixSeconds, ReadOnlySpan<byte> payload, IReadOnlyList<byte[]> activeSecrets);
}
```

---

## 4. DTOs / contracts

Namespace `NomNomzBot.Application.DTOs.Webhooks` (split into `InboundWebhookDtos.cs` / `OutboundWebhookDtos.cs`); transport/verify contracts in `NomNomzBot.Application.Contracts.Webhooks`. All `sealed record`. Inbound request records validated by the in-box `.NET 10 AddValidation()` source generator. Secrets are **never** echoed back except the documented create-time/rotate-time reveal.

```csharp
namespace NomNomzBot.Application.DTOs.Webhooks;

using NomNomzBot.Domain.Enums;

// ── Inbound endpoints (H.10) ─────────────────────────────────────────────────
public sealed record InboundWebhookEndpointDto(
    Guid Id, string Name, WebhookAdapterKind Adapter, string IngestUrl,   // App:BaseUrl + /api/v1/webhooks/in/{token}
    bool VerificationSecretSet, Guid? TargetPipelineId, string? TargetEventType,
    bool IsEnabled, DateTime? LastReceivedAt, long ReceiveCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CreateInboundWebhookRequest
{
    public required string Name { get; init; }
    public required WebhookAdapterKind Adapter { get; init; }
    public required string VerificationSecret { get; init; }     // provider token / shared secret; AEAD-encrypted, never stored plaintext
    public Guid? TargetPipelineId { get; init; }
    public string? TargetEventType { get; init; }
    public GenericInboundConfig? GenericConfig { get; init; }    // required when Adapter == Generic
    public bool IsEnabled { get; init; } = true;
}

public sealed record UpdateInboundWebhookRequest
{
    public string? Name { get; init; }
    public string? VerificationSecret { get; init; }            // rotate the secret when present
    public Guid? TargetPipelineId { get; init; }
    public string? TargetEventType { get; init; }
    public GenericInboundConfig? GenericConfig { get; init; }
    public bool? IsEnabled { get; init; }
}

// Generic / Standard-Webhooks adapter config (covers Zapier/IFTTT/Make/Stream Deck/custom).
public sealed record GenericInboundConfig(
    string? SignatureHeaderName,          // e.g. "webhook-signature" / "X-Signature-256" — null => shared-secret-in-body mode (low-assurance)
    string? SignaturePrefix,              // e.g. "v1," / "sha256=" — stripped before compare
    string? SigningStringTemplate,        // tokens {id}{timestamp}{body}; default "{id}.{timestamp}.{body}" (Standard-Webhooks) in HMAC mode
    string? TimestampHeaderName,          // REQUIRED in HMAC mode (SignatureHeaderName set) — drives the 10-min replay guard so dedup is the backstop, not the sole guard
    string? SharedSecretBodyField,        // shared-secret-in-body mode: JSON field that must equal the secret (Ko-fi-style)
    string EventKindJsonPath,             // JSONPath/field that yields <kind> for the event type, e.g. "$.type"
    string ProviderEventIdJsonPath);      // JSONPath/field that yields the dedupe id, e.g. "$.id"
// Validation (IInboundWebhookEndpointService.Create/Update, AdapterKind=generic): HMAC mode (SignatureHeaderName non-null)
// REQUIRES a non-null TimestampHeaderName -> else Result.Failure("generic_hmac_requires_timestamp"). Shared-secret-in-body
// mode (SignatureHeaderName null) requires SharedSecretBodyField; it is accepted but flagged low-assurance (no timestamp,
// replay rests on the body-hash dedup floor only — prefer HMAC+timestamp wherever the sender supports it).

// ── Outbound endpoints (H.8) ─────────────────────────────────────────────────
public sealed record OutboundWebhookEndpointDto(
    Guid Id, string Name, string Fqdn, string? Path, IReadOnlyList<string> SubscribedEventTypes,
    bool IsEnabled, int ConsecutiveFailureCount, DateTime? DisabledAt, string? DisabledReason,
    DateTime? LastDeliveryAt, DateTime? LastSuccessAt, DateTime CreatedAt, DateTime UpdatedAt);

// Returned ONLY at create / rotate — carries the one-time plaintext signing secret.
public sealed record OutboundWebhookEndpointCreatedDto(OutboundWebhookEndpointDto Endpoint, string SigningSecret);   // whsec_<base64>

public sealed record CreateOutboundWebhookRequest
{
    public required string Name { get; init; }
    public required string Fqdn { get; init; }                  // must match an enabled HttpEgressAllowlist (H.7) row
    public string? Path { get; init; }
    public required List<string> SubscribedEventTypes { get; init; }   // event types; "*" = all
    public string? BodyTemplate { get; init; }                 // ITemplateEngine template; default = canonical JSON envelope
    public Dictionary<string, string>? CustomHeaders { get; init; }    // templated; reserved webhook-* / signature headers rejected
    public bool IsEnabled { get; init; } = true;
}

public sealed record UpdateOutboundWebhookRequest
{
    public string? Name { get; init; }
    public List<string>? SubscribedEventTypes { get; init; }
    public string? BodyTemplate { get; init; }
    public Dictionary<string, string>? CustomHeaders { get; init; }
    public bool? IsEnabled { get; init; }
}

public sealed record WebhookDeliveryDto(
    long Id, Guid EndpointId, Guid WebhookMessageId, string EventType, int Attempt,
    WebhookDeliveryStatus Status, int? ResponseCode, int? DurationMs, DateTime? NextRetryAt,
    string? Error, DateTime CreatedAt);

public sealed record WebhookDeliveryQuery(int Page = 1, int PageSize = 25, WebhookDeliveryStatus? Status = null);

public sealed record WebhookTestResultDto(bool Delivered, int? ResponseCode, int DurationMs, string? Error);
```

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

using NomNomzBot.Domain.Enums;

// Raw untrusted inbound request (controller -> dispatcher). RawBody is the buffered bytes used for signature verification.
// The DISPATCHER owns token resolution (§3.2 step 1): the controller never queries the endpoint row, so this record
// carries ONLY token-derived-by-the-caller transport facts. BroadcasterId/EndpointId/Adapter are NOT on it — the
// dispatcher resolves them from Token and returns them on InboundDispatchResult. (Single owner; no duplicate lookup.)
public sealed record InboundWebhookRequest
{
    public required string Token { get; init; }                              // URL path segment -> endpoint (dispatcher resolves)
    public required string Method { get; init; }                             // controller already enforced POST; carried for adapters that sign the method
    public required string ContentType { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public required ReadOnlyMemory<byte> RawBody { get; init; }              // exact bytes (signature is over these)
    public required DateTime ReceivedAtUtc { get; init; }                    // server clock at ingest — seeds the no-HMAC dedup time-bucket (§3.2 step 4b)
    public required string RemoteIpHash { get; init; }                       // SHA-256 of client IP — pre-resolution rate-limit key only; never logged raw
}

public sealed record WebhookVerification(bool IsValid, WebhookRejectReason? Reason);

public sealed record ParsedInboundEvent(
    string Kind,                                                              // -> "webhook.<provider>.<kind>"
    string ProviderEventId,                                                   // dedupe key
    IReadOnlyDictionary<string, string> Variables);                          // flattened body, seeded under webhook.*/payload.*

public sealed record InboundWebhookRequestContext(InboundWebhookRequest Request, byte[] DecryptedSecret, GenericInboundConfig? GenericConfig);

public sealed record InboundDispatchResult(
    bool Verified, bool WasDuplicate, Guid? JournalEventId, long StreamPosition,
    string EventType, WebhookRejectReason? RejectReason, int HttpStatus,
    Guid? ResolvedEndpointId, Guid? ResolvedBroadcasterId, WebhookAdapterKind? ResolvedAdapter);  // dispatcher-resolved (null on UnknownEndpoint)

public sealed record OutboundEnqueueResult(Guid EndpointId, Guid WebhookMessageId, long DeliveryId, WebhookDeliveryStatus Status);
public sealed record OutboundAttemptResult(long DeliveryId, int Attempt, WebhookDeliveryStatus Status, int? ResponseCode, DateTime? NextRetryAt);
public sealed record WebhookSignatureHeaders(string WebhookId, string WebhookTimestamp, string WebhookSignature);
```

---

## 5. Controller endpoints

Two controllers in `NomNomzBot.Api/Controllers/V1/`. The management controller is JWT-gated and tenant-scoped; the ingest controller is `[AllowAnonymous]` (auth = token + per-adapter signature, **never** the user JWT — exactly the OverlayHub model). Both inherit `BaseController` (`ResultResponse` / `GetPaginatedResponse`).

### 5.1 `WebhooksController` — management plane

`[Route("api/v{version:apiVersion}/channels/{channelId:guid}/webhooks")]`, `[ApiVersion("1.0")]`, `[Authorize]`.

**Role gate.** Gate 1 = `[Authorize]` + tenant resolution (entry; any management level ≥ Moderator). Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below). The keys are seeded global `ActionDefinition`s (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Webhook config is sensitive egress + secrets → the write floor is **Editor**; read is **Moderator** (Plane B, channel management).

| Method | Route (suffix under `…/webhooks`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|--------|-----------------------------------|-------------|--------------|-----------------------------------|
| GET | `/inbound` | `PaginationParams` (query) | `PaginatedResponse<InboundWebhookEndpointDto>` | management / Moderator · `webhooks:inbound:read` |
| GET | `/inbound/{endpointId:guid}` | — | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Moderator · `webhooks:inbound:read` |
| POST | `/inbound` | `CreateInboundWebhookRequest` | `StatusResponseDto<InboundWebhookEndpointDto>` (201) | management / Editor · `webhooks:inbound:write` |
| PUT | `/inbound/{endpointId:guid}` | `UpdateInboundWebhookRequest` | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Editor · `webhooks:inbound:write` |
| POST | `/inbound/{endpointId:guid}/rotate-token` | — | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Editor · `webhooks:inbound:write` |
| DELETE | `/inbound/{endpointId:guid}` | — | 204 | management / Editor · `webhooks:inbound:write` |
| GET | `/outbound` | `PaginationParams` (query) | `PaginatedResponse<OutboundWebhookEndpointDto>` | management / Moderator · `webhooks:outbound:read` |
| GET | `/outbound/{endpointId:guid}` | — | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Moderator · `webhooks:outbound:read` |
| POST | `/outbound` | `CreateOutboundWebhookRequest` | `StatusResponseDto<OutboundWebhookEndpointCreatedDto>` (201, secret revealed once) | management / Editor · `webhooks:outbound:write` |
| PUT | `/outbound/{endpointId:guid}` | `UpdateOutboundWebhookRequest` | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/rotate-secret` | — | `StatusResponseDto<OutboundWebhookEndpointCreatedDto>` (secret revealed once) | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/reenable` | — | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/test` | — | `StatusResponseDto<WebhookTestResultDto>` | management / Editor · `webhooks:outbound:write` |
| DELETE | `/outbound/{endpointId:guid}` | — | 204 | management / Editor · `webhooks:outbound:write` |
| GET | `/outbound/{endpointId:guid}/deliveries` | `WebhookDeliveryQuery` (query) | `PaginatedResponse<WebhookDeliveryDto>` | management / Moderator · `webhooks:outbound:read` |

### 5.2 `InboundWebhookController` — public, token + signature-gated ingest

`[Route("api/v{version:apiVersion}/webhooks/in")]`, `[AllowAnonymous]`, `[Tags("Webhooks")]`. Auth is the opaque token + the per-adapter signature, **not** JWT. Reads the **raw buffered body** before any deserialization (signature is over the exact bytes). The token is **scrubbed from request logs** (same log-scrub rule as OverlayToken / `access_token`). Rate-limited via `IRateLimiterPartitionStore` in **two tiers**: a **pre-resolution** tier keyed on data known *before* the DB token lookup (`wh:in:ip:{clientIpHash}` — and, optionally, `wh:in:rawtok:{sha256(token-segment)}`), and a **post-resolution** tier keyed on the resolved endpoint/tenant (`wh:in:{endpointId}` + `wh:in:tenant:{broadcasterId}`). The pre-resolution tier is what throttles unknown/garbage-token floods and token-guessing — neither post-resolution partition can fire on the 404 path because `endpointId`/`broadcasterId` don't exist yet.

| Verb | Route | Request | Response | Auth |
|------|-------|---------|----------|------|
| POST | `/webhooks/in/{token}` | raw body + provider headers | `200` (accepted / duplicate-idempotent) · `400` malformed · `401/403` bad signature · `404` unknown token · `405` non-POST · `413` body over cap · `415` bad content-type · `429` rate-limited · `503` disabled-target | `[AllowAnonymous]`, token + `IInboundWebhookAdapter.Verify` |

**Behavior (untrusted-input hardening — OWASP REST, deny-by-default). Steps are ordered: the cheapest unauthenticated-abuse guards run BEFORE the DB token lookup.**
0. **Pre-resolution rate limit (runs FIRST, before any DB I/O)** — partition on the client IP hash (`wh:in:ip:{clientIpHash}`, tight default **60/min/IP**) and optionally the raw token-segment hash (`wh:in:rawtok:{sha256(token)}`). Over cap → `429` (`Retry-After`). This is the ONLY throttle on the unknown-token / token-guessing path; it must precede the token lookup so a garbage-token flood can't force an unbounded stream of DB probes.
1. **Method allowlist** — only `POST`; anything else `405` (route constraint).
2. **Size cap** — body over the configured cap (default 256 KiB) `413` **before** buffering completes; never load an unbounded body.
3. **Content-type allowlist** — `application/json` / `application/x-www-form-urlencoded` (Ko-fi) only; else `415`.
4. Build `InboundWebhookRequest` (raw bytes + headers + `Method`/`ReceivedAtUtc`/`RemoteIpHash`), call `IInboundWebhookDispatcher.DispatchAsync` — **the dispatcher owns token→endpoint resolution** (§3.2 step 1); the controller does not query the endpoint row.
5. The dispatcher resolves the token → endpoint, then applies the **post-resolution** rate-limit tier (`wh:in:{endpointId}` + `wh:in:tenant:{broadcasterId}`) → `RateLimited` → `429`. Unknown/soft-deleted token → `UnknownEndpoint` → `404`. Disabled → `Disabled` → `503`.
6. Map the typed result: verified+journaled or duplicate → **`200`** (a minimal `2xx` ack — never problem-details JSON, matching the EventSub-webhook ack convention so senders don't retry on a body they can't parse); `InvalidSignature` → `401`/`403`; `ReplayWindow` → `403`; `Malformed` → `400`.
7. **Generic errors only** — the public response never leaks internal detail (no stack, no entity ids, no SQL). Invalid HMAC is rejected `4xx` and **never processed**.
8. **No unthrottled bus emission on the unknown-token 404 path.** `InboundWebhookRejectedEvent` is emitted **only** for a *resolved* endpoint (bad signature / replay / disabled-target / over-limit on a known endpoint). The `UnknownEndpoint` (404) case **does not** emit a per-request bus event — that would hand an unauthenticated flooder a free amplification into the event bus; unknown-token volume is observed via the pre-resolution limiter's counter/metrics, coalesced, not as one bus event per probe.

> **Self-host reachability (`ExposureModel`).** SaaS = `managed_edge` → the ingest URL is publicly reachable out of the box. Self-host = `opt_in_tunnel` → the instance is behind NAT; the operator **opts in** by exposing this route via a reverse-proxy / Cloudflare tunnel (the same dev-tunnel story as EventSub OAuth). Inbound webhooks are therefore a self-host-with-public-exposure feature, parallel to EventSub being WebSocket-only (no inbound) on self-host. Default-deny: nothing is reachable until the operator exposes it.

---

## 6. Pipeline actions

One net-new `ICommandAction` (the canonical contract from `commands-pipelines.md` §3.13: `string Type` (+`Category`/`Description`), `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`, reading params from `context.Parameters`). In `NomNomzBot.Infrastructure/Pipeline/Actions/`.

| Type string | Config DTO (`context.Parameters`) | Behavior |
|-------------|-----------------------------------|----------|
| `send_webhook` *(new)* | `{ Guid OutboundWebhookEndpointId }` **only** — no url/secret/headers in step config (broker pattern, like `song_request`/`http_request`) | Resolves the endpoint for `ctx.BroadcasterId`, calls `IOutboundWebhookDispatcher.EnqueueForEndpointAsync(ctx.BroadcasterId, endpointId, eventType, ctx.Variables, journalEventId)` where **`eventType = ctx.EventType ?? "webhook.manual.send"`** and **`journalEventId = ctx.JournalEventId`** (both read from the `ActionContext` fields added in `commands-pipelines.md` §4.4 — the engine seeds them from the triggering event; for a `send_webhook` fired in a non-event-triggered pipeline `JournalEventId` is null, which is valid since `OutboundWebhookDelivery.JournalEventId` is nullable). The url, secret, body/header templates, and SSRF boundary all live on the H.8 endpoint + its H.7 row — the step only names the endpoint. Returns `ActionResult.Success` (enqueue is fast-ack; delivery is async). |

> **Config-validator invariant (reused).** `ICommandConfigValidator` (`commands-pipelines.md` §3.11) already rejects any step config key naming a url/secret/credential/peer-channel id. `send_webhook`'s sole key is an opaque `Guid` endpoint id resolved tenant-side — it carries **no** url or secret, satisfying the invariant by construction.

**Inbound as a trigger.** An inbound verified webhook is **not** an `ICommandAction` — it is a pipeline/event-response *trigger*. `commands-pipelines.md` H.1 gains `TriggerKind=webhook`; the dispatcher (§3.2) routes a verified hit either to `IPipelineEngine.ExecuteAsync` (when `TargetPipelineId` is set) or to `IEventResponseService.TriggerAsync(broadcasterId, "webhook.<provider>.<kind>", variables)` (the existing event-response fan-out). An `EventResponse` (I.2) may also set `ResponseType=pipeline`/`chat_message` keyed on the `webhook.*` event type. The parsed body is seeded by the dispatcher into the template bag under the `webhook.*` / `payload.*` namespace (see §7).

---

## 7. Template namespace (inbound body → `webhook.*` / `payload.*`)

The inbound dispatcher seeds the parsed body into the `ActionContext.Variables` bag **before** any render, under a new namespace — the `VariableResolver` (`commands-pipelines.md` §6.3) stays **pure / I-O-free**; no engine change, only a new pre-seeded namespace (exactly the documented pattern for Helix/economy/music tokens).

| Token | Value | Seeded by | Trust |
|-------|-------|-----------|-------|
| `{{webhook.provider}}` | `kofi`\|`github`\|`generic` | dispatcher | trusted (server-derived) |
| `{{webhook.event}}` | the `<kind>` (e.g. `tip`) | dispatcher | trusted (server-derived) |
| `{{webhook.id}}` | the provider event id (dedupe key) | dispatcher | trusted (server-derived) |
| `{{payload.<field>}}` | any flattened body field (e.g. `{{payload.from_name}}`, `{{payload.amount}}`, `{{payload.message}}`) | dispatcher (adapter `Parse` flattens nested JSON dot-path) | **TAINTED — attacker-authored** |

Unknown token ⇒ empty string (uniform §6.3 rule).

### 7.1 `payload.*` is untrusted — a taint boundary, not just a namespace (binding)

`payload.*` is the **external caller's raw JSON body**. Seeding it flat into the *same* `Variables` dictionary that also holds platform-resolved trusted tokens (`user.*`, `channel.*`) would let one verified inbound POST steer a security-sensitive action — `{{payload.username}}` in a `ban`/`timeout` `UserRef`, `{{payload.target}}` in `shoutout`, `{{payload.url_path}}` in an `http_request` `Path`, or an endpoint selector — handing the external sender direct control of the action's *target*. (Verification proves the body came from someone who knows the secret; it proves **nothing** about the *values* in it. `ICommandConfigValidator` guards config **keys** at save time, never runtime **values**.) The single-pass `[GeneratedRegex]` resolver already blocks second-order injection — it does not re-scan substituted values — but value-as-target escalation is not closed by that. So:

1. **Quarantined sub-bag.** The dispatcher seeds `payload.*` into a **distinct tainted sub-bag** (`ActionContext.TaintedVariables`, see `commands-pipelines.md` §4.4) — *not* merged into `Variables`. The resolver renders `{{payload.<field>}}` from the tainted bag for **display-only** sinks (`send_message`/`send_reply`/`set_variable` output, outbound `BodyTemplate`) but the engine **fails-closed** if a tainted token resolves inside a **security-sensitive action parameter**: `ban`/`timeout`/`ban` `UserRef`, `shoutout` `TargetChannel`, `http_request` `Fqdn`/`Path`/`Method`, and `send_webhook` endpoint selection. A pipeline that pipes `payload.*` into any of those is rejected at **compile/validate** (`ICommandConfigValidator` flags a `webhook`-triggered pipeline whose sensitive param references a `payload.*` token → `Result.Failure("tainted_payload_in_sensitive_param")`); if it ever reaches runtime it is a `denied` execution, never executed.
2. **Hard caps + sanitization at flatten time.** Each flattened `payload.<field>` value is **length-capped** (default **2 KiB/field**, total **64 KiB/bag**, over → field dropped) and **control-char-stripped** (no CR/LF/NUL — blocks chat-command/log injection) by the adapter `Parse` before it ever enters the bag.
3. **Author warning.** The save-time validator surfaces a **warning** (not just the hard fail in #1) whenever a `webhook`-triggered pipeline references `payload.*` anywhere, so the author is told the namespace is attacker-authored.

**Outbound** body/header templates are the *author's* `BodyTemplate`/`CustomHeaders`, rendered by **`ITemplateEngine.Render(template, variables)`** (`commands-pipelines.md` §6.3 — the body uses the whole-template overload, each header value rendered the same way) over the standard seeded bag (the triggering event's variables) — the same engine, no new outbound namespace. (When the triggering event is itself an inbound webhook, `payload.*` in an outbound body is rendered display-only from the tainted bag, after the §7.1 caps — egress of attacker text is acceptable; egress *targeting* is governed by the H.7 allowlist on the endpoint, never by `payload.*`.)

---

## 8. Security model

| Concern | Control (reused contract in **bold**) |
|---------|---------------------------------------|
| **Inbound auth** | Opaque per-endpoint `Token` (64 url-safe chars, `RandomNumberGenerator`, **OverlayToken model**), `[AllowAnonymous]`, validated at request, **scrubbed from logs**. Plus the per-adapter signature — token alone is never sufficient for HMAC adapters. |
| **Inbound verification** | Per-adapter (`IInboundWebhookAdapter.Verify`): the `supporter` adapter verifies per `SourceKey` (Patreon `X-Patreon-Signature` HMAC-MD5, Fourthwall/Shopify HMAC-SHA256, Ko-fi `verification_token` body compare via FixedTimeEquals), GitHub `X-Hub-Signature-256` HMAC, generic configurable. In-box **`HMACSHA256` + `CryptographicOperations.FixedTimeEquals`** (mirrors `twitch-eventsub.md` §3.6). Invalid → `4xx`, **never processed**. |
| **Inbound replay** | **Per-adapter, with a dedup-retention SECURITY FLOOR (§3.2 step 4, §11 #1).** Generic adapter: required `webhook-timestamp` + **10-min tolerance** (matching in-house EventSub) is the primary guard; dedup is the backstop (24h `ExpiresAt`). Ko-fi / GitHub have **no timestamp** → the **dedup row is the SOLE replay barrier**, so its `ExpiresAt` is pinned to a **30-day fixed floor** (not a perf knob — the pruner must never shorten it). Dedup via **`IIdempotencyGuard`** O.4 (`Scope="webhook:in:{endpointId}"`, `ExpiresAt` per the floor above). No-HMAC adapters (kofi / generic-shared-secret-in-body) key the claim on `SHA-256(rawBody)+":"+ProviderEventId` (server-observed body hash) so a chosen-`ProviderEventId` pre-claim cannot shadow a distinct legit event. |
| **Inbound DoS hardening** | Size cap → `413`; method allowlist → `405`; content-type → `415`; rate limit → `429` via **`IRateLimiterPartitionStore`** **two tiers** — **pre-resolution** (`wh:in:ip:{clientIpHash}`, default 60/min/IP, + optional `wh:in:rawtok:{sha256(token)}`) runs **before** the DB token lookup to throttle unknown-token floods/guessing, then **post-resolution** (`wh:in:{endpointId}` + `wh:in:tenant:{broadcasterId}`). The `UnknownEndpoint` 404 path emits **no** bus event (no unauthenticated amplification). Generic errors (no internal leak); deny-by-default. |
| **Outbound SSRF** | **Same SSRF *core*, different (server-controlled) header+body policy — NOT a verbatim reuse.** The IP-pin/allowlist/redirect/metadata-block controls are reused **unchanged** from `code-execution-sandbox.md` §7 (the binding requirement): every outbound delivery goes through the **same `egress-allowlisted` `SocketsHttpHandler.ConnectCallback`** (resolve-then-pin to the validated IP, TLS `TargetHost`=FQDN), `https`-only, FQDN must match an enabled **`HttpEgressAllowlist`** (H.7) row, reject `127/8`,`10/8`,`172.16/12`,`192.168/16`,**full `169.254/16`** (+`169.254.169.254`),`100.64/10`,`0.0.0.0/8`,`::1`,`fc00::/7` (ULA),`fe80::/10` (link-local),`::ffff:0:0/96`,multicast; **redirects disabled**; response capped (`MaxResponseBytes`). **But the sandbox front-end's *guest* header/body clamp must NOT be reused as-is** — it strips down to `Accept`/`Content-Type` and gates bodies behind `AllowRequestBody` (default off), which would silently drop the Standard-Webhooks `webhook-id`/`webhook-timestamp`/`webhook-signature` headers (receiver rejects every delivery as unsigned → auto-disable) and the JSON body. Instead the **trusted webhook dispatcher injects** the signature/`webhook-*` headers, the author `CustomHeaders`, and the rendered body **server-side** (this is a first-party signed sender, not an untrusted guest), and the H.8 endpoint's H.7 row is created with **`AllowRequestBody=true`** and a webhook-sized **`MaxRequestBytes` = the 256 KiB inbound cap** (not the few-KiB sandbox default). See §3.6 + §8.1. H.8 still points at the H.7 row for the egress boundary; only the per-caller header/body **policy object** differs. |
| **Outbound signing** | Standard Webhooks: `webhook-id` / `webhook-timestamp` (unix s) / `webhook-signature` = `v1,<base64 HMAC-SHA256(secret, "<id>.<timestamp>.<payload>")>`, space-delimited multi-sig during rotation. In-box **`HMACSHA256`**; **no 3rd-party**. |
| **Secret storage** | `whsec_` (outbound) + verification secret (inbound) stored as AEAD ciphertext via **`ISubjectKeyService.ProtectAsync`** / **`IFieldCipher`** (AES-256-GCM) — never plaintext. Exact binding (§3.1/§3.5): `cryptoKeyId` from `GetOrCreateTenantKeyAsync(broadcasterId)`; **`CipherAad(TenantId=broadcasterId, Provider="webhook:in"|"webhook:out", TokenType=endpointId, KeyVersion)`** identical on encrypt/decrypt (4-field record, not a `‖` string); `resourceTable`/`resourceColumn` = the table + each cipher column (incl. `SecondarySigningSecretCipher`) so `RotateKeyAsync` re-encrypts every secret column via `KeyUsageBinding`. Rotation = new `whsec_`, overlap-valid (multi-sig). Crypto-shred via **`DestroyKeyAsync`** (mirrors `IntegrationTokens` envelope; `AppSetting.SecureValueCipher` for any global-secret case). |
| **Outbound delivery** | At-least-once (receivers dedupe; we send the `webhook-id`), **`IIdempotencyGuard`** (`Scope="webhook:out"`) so the same event never double-enqueues, retry exp backoff+jitter, dead-letter after max attempts, **auto-disable after N consecutive failures**. Worker under **`IRunOnceGuard`** (no double-deliver multi-instance). |
| **Tenant isolation** | Every row carries `BroadcasterId`; management endpoints gate per-action via `IActionAuthorizationService.AuthorizeActionAsync(userId, channelId, actionKey)` (Gate 2) after `[Authorize]` + tenant resolution (Gate 1). Ingest resolves tenant **from the endpoint row**, never from the request body. |
| **Exposure** | SaaS `managed_edge` (public). Self-host `opt_in_tunnel` (operator opts in via reverse-proxy/tunnel; default-deny). |

### 8.1 Outbound egress: shared SSRF core, per-caller header/body policy (no second client)

The fix for "the reused sandbox client strips exactly what webhooks must send" is to **factor the egress policy**, not to stand up a parallel client (a parallel un-validated client is precisely the SSRF regression the lens hunts for):

- **Shared, unchanged:** the `egress-allowlisted` `SocketsHttpHandler` + `ConnectCallback` IP-pin, FQDN allowlist match, resolved-IP re-validation (metadata/internal/link-local blocks), no-redirects, response cap. Owned by `code-execution-sandbox.md` §7; the webhook path goes through the **same handler instance** — SSRF cannot be re-opened. An **architecture test asserts the webhook dispatcher's `HttpClient` resolves the `egress-allowlisted` named client and that its outbound requests traverse that same `ConnectCallback`** (so no one can quietly add a second egress path).
- **Per-caller policy object (the only difference):** the header-allowlist + request-body clamp are a **policy parameter**, not hard-coded into the handler. Two policies exist: the **sandbox/guest** policy (header set = `Accept`/`Content-Type`, body gated by `AllowRequestBody`, default off — for the untrusted `http.fetch`/`http_request` guest) and the **trusted-webhook** policy (the dispatcher attaches the `webhook-id`/`webhook-timestamp`/`webhook-signature` + author `CustomHeaders` + rendered body server-side; the H.7 row is provisioned `AllowRequestBody=true`, `MaxRequestBytes=256 KiB`). The guest can still never reach this policy — it is selected by the **caller** (trusted dispatcher vs guest front-end), server-side, never by anything crossing the sandbox boundary.

This keeps one SSRF owner and one connect path while letting the trusted first-party sender transmit signed headers + a webhook-sized body.

---

## 9. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`, new `// Webhooks` block). Lifetimes: row-touching use-case services **Scoped** (DbContext/`IUnitOfWork`); pure verifiers/signer **Singleton**; adapters **Singleton** (stateless, multi-registered like `ICommandAction`); the worker **Hosted**.

```csharp
// Inbound + outbound endpoint services (Scoped — DbContext / IUnitOfWork)
services.AddScoped<IInboundWebhookEndpointService, InboundWebhookEndpointService>();
services.AddScoped<IOutboundWebhookEndpointService, OutboundWebhookEndpointService>();

// Core ingest + egress paths (Scoped — journal/idempotency/template/egress all per-request)
services.AddScoped<IInboundWebhookDispatcher, InboundWebhookDispatcher>();
services.AddScoped<IOutboundWebhookDispatcher, OutboundWebhookDispatcher>();

// Per-provider inbound adapters (Singleton, multi-register; dispatcher selects by Kind)
services.AddSingleton<IInboundWebhookAdapter, SupporterInboundWebhookAdapter>(); // supporter-events.md — Ko-fi/Patreon/Fourthwall/Shopify, dispatches by SourceKey
services.AddSingleton<IInboundWebhookAdapter, GithubInboundWebhookAdapter>();
services.AddSingleton<IInboundWebhookAdapter, GenericInboundWebhookAdapter>();

// Pure crypto primitives (Singleton, in-box HMACSHA256)
services.AddSingleton<IInboundSignatureVerifier, HmacInboundSignatureVerifier>();
services.AddSingleton<IOutboundWebhookSigner, StandardWebhooksSigner>();

// Pipeline action (multi-register, beside the other ICommandAction registrations)
services.AddScoped<ICommandAction, SendWebhookAction>();

// Fan-out: a post-journal observer that calls IOutboundWebhookDispatcher.EnqueueForEventAsync for subscribed event types.
// Bound to the event-store's post-commit seam (NOT a generic IEventHandler<...> — see note below for why that placeholder
// was unimplementable). IJournalPostCommitHook is owned by event-store.md §3.2/§7.
services.AddScoped<IJournalPostCommitHook, OutboundWebhookFanoutHandler>();

// Retry/dead-letter drain worker (Hosted; guarded by IRunOnceGuard)
services.AddScoped<IWebhookDeliveryWorker, WebhookDeliveryWorker>();
services.AddHostedService<WebhookDeliveryBackgroundService>();           // PeriodicTimer -> DrainDueAsync, under IRunOnceGuard("webhooks:drain")

// egress-allowlisted named HttpClient is OWNED by code-execution-sandbox.md §7 (…Infrastructure.CustomCode.Egress) — REUSED, not re-registered here.
// IEventJournal / ITenantSequenceAllocator / IIdempotencyGuard owned by event-store.md. IFieldCipher / ISubjectKeyService owned by gdpr-crypto.md.
// IRateLimiterPartitionStore / IRunOnceGuard owned by platform-conventions.md. ITemplateEngine / IEventResponseService / IPipelineEngine owned by commands-pipelines.md.
```

> **`OutboundWebhookFanoutHandler`.** Outbound is triggered two ways: (a) explicitly by the `send_webhook` pipeline action (§6), and (b) declaratively by subscribing an endpoint to event types. For (b), a thin observer is invoked **after** each event is journaled and calls `EnqueueForEventAsync` for every event whose `EventType` an enabled endpoint lists in `SubscribedEventTypes`. The hook signature is `OnCommittedAsync(EventRecord)`; `EventRecord` carries `EventType` + `BroadcasterId` + `PayloadJson` (the `[VC:JSON]`-serialized payload) but **not** a flattened bag — so the handler **deserializes `PayloadJson` with Newtonsoft (schema §1.4) and flattens it into the `IReadOnlyDictionary<string,string>` variable bag** for `EnqueueForEventAsync(broadcasterId, eventType, variables, journalEventId)`. This payload is a **trusted, server-generated** journaled domain event (not external input), so the §7.1 inbound `payload.*` taint caps do **not** apply to it; only a flatten depth/size bound (mirroring §7.1's 2 KiB/field · 64 KiB/bag) guards against a pathological event.
>
> **Wiring (resolves the "hook that doesn't exist" gap).** The previous "piggyback the `JournalingEventBusDecorator` post-journal hook keyed on `EventType`" route assumed a seam the decorator never exposed (it only does `capture → delegate`; and a generic `IEventHandler<IDomainEvent>` is impossible because handlers resolve per concrete `TEvent` — `event-store.md` §3.2). So this subsystem **requires `event-store.md` to expose a real post-commit observer seam, `IJournalPostCommitHook`** (added there in §3.2/§7): the decorator, after `CaptureAsync` commits the journal row, invokes every registered `IJournalPostCommitHook.OnCommittedAsync(EventRecord)` — one wiring point, EventType-agnostic at the seam, the handler filters by `SubscribedEventTypes`. `OutboundWebhookFanoutHandler` implements it. (No per-`TEvent` fallback — that route is also unimplementable as a single registration for the same per-concrete-handler reason; the seam is the binding answer.) Without this seam, declarative `SubscribedEventTypes` endpoints would never fire — only the `send_webhook` action would.
>
> **Self-amplification guard (binding — must not loop).** The fan-out match set is **business/domain events only**. The webhook subsystem's own lifecycle events (`OutboundWebhookEnqueuedEvent`, `OutboundWebhookAttemptedEvent`, `OutboundWebhookAutoDisabledEvent`, `InboundWebhookReceivedEvent`, `InboundWebhookRejectedEvent`) are on a **hard deny-list**: `'*'` means "all **subscribable business** events" and **never** matches a webhook-lifecycle type, and an explicit subscription to one of those types is rejected at endpoint save (`Result.Failure("event_type_not_subscribable")`). Reason: an endpoint subscribed to `'*'` (or to a lifecycle type) would have each delivery emit `OutboundWebhookEnqueuedEvent`/`OutboundWebhookAttemptedEvent`, which the post-commit hook would re-match and re-enqueue — an unbounded cascade the outbound idempotency guard cannot break (every hop mints a fresh `webhook-id`, so `TryClaimAsync` is always `IsFirst=true`). In addition the fan-out stamps **`CausationId`** on every outbound-triggered emission and **refuses to fan out** an event whose causation chain already contains an outbound-webhook send for the **same endpoint** (defense in depth against any other cycle). Test (§10): an endpoint subscribed to `'*'` that fires once produces **exactly one** delivery, not a growing cascade.

EF configurations (`NomNomzBot.Infrastructure/Persistence/Configurations/Webhooks/`): `OutboundWebhookEndpointConfiguration`, `OutboundWebhookDeliveryConfiguration`, `InboundWebhookEndpointConfiguration`. All `[VC:JSON]` columns (`SubscribedEventTypesJson`, `CustomHeadersJson`, `GenericConfigJson`) use the hand-rolled Newtonsoft `ValueConverter<T,string>` + `ValueComparer`; **no `jsonb`/`HasDefaultValueSql`**. New `IApplicationDbContext` `DbSet`s: `OutboundWebhookEndpoints`, `OutboundWebhookDeliveries`, `InboundWebhookEndpoints`.

**Deployment-profile adapter variants** (no second impl set — the core services call profile-agnostic abstractions):

| Capability | lite (self-host) | full / SaaS |
|-----------|------------------|-------------|
| Inbound reachability (`ExposureModel`) | `opt_in_tunnel` — route exists; operator exposes via reverse-proxy / Cloudflare tunnel (opt-in) | `managed_edge` — publicly reachable out of the box |
| Inbound rate limiter (`IRateLimiterPartitionStore`) | in-memory partitioned counter (single node) — both the pre-resolution IP/raw-token tier and the post-resolution endpoint/tenant tier | Redis `INCR`+`EXPIRE` (cluster-wide) — both tiers |
| Delivery-drain worker run-once (`IRunOnceGuard`) | no-op (single instance safe) | `pg_try_advisory_lock` (no double-deliver) |
| Outbound egress client | `egress-allowlisted` named client (in-box `SocketsHttpHandler`) — identical both profiles | identical |
| Secret KEK custody (`IKeyVault`) | `local_aes` file keystore | `kms_envelope` (Azure Key Vault) |

---

## 10. Dependencies (from the stack doc)

This subsystem uses **only second-party + already-present** packages — **zero new third-party deps**:

- **`System.Security.Cryptography`** (`HMACSHA256`, `CryptographicOperations.FixedTimeEquals`, `RandomNumberGenerator`) — in-box; all signing (Standard Webhooks outbound) + verification (GitHub/generic inbound) + opaque token/secret minting. **No 3rd-party webhook library.**
- **`System.Net.Http` / `IHttpClientFactory`** (in-box) — outbound delivery rides the **`egress-allowlisted` named client** owned by `code-execution-sandbox.md` §7 (single `SocketsHttpHandler` + `ConnectCallback` pinned-IP connect). Not re-registered here; reused.
- **`System.Text.Json`** (`.Strict`, in-box) — untrusted inbound body parse (hot path), exactly as EventSub wire frames.
- **Microsoft.EntityFrameworkCore 10.0.9** (+ provider via adapter: Npgsql 10.0.2 / `Microsoft.EntityFrameworkCore.Sqlite` 10.0.9) — H.8/H.9/H.10 persistence, soft-delete + tenant named query filters (EF10), `[VC:JSON]` converters via hand-rolled `ValueConverter<T,string>` (Newtonsoft.Json per schema §1.4).
- **Reused first-party contracts (no package):** `IEventJournal` + `ITenantSequenceAllocator` + `IIdempotencyGuard` (`event-store.md`), `IFieldCipher` + `ISubjectKeyService` + `IKeyVault` (`gdpr-crypto.md`), `IRateLimiterPartitionStore` + `IRunOnceGuard` (`platform-conventions.md`), `ITemplateEngine` + `IEventResponseService` + `IPipelineEngine` + `ICommandAction` (`commands-pipelines.md`), `HttpEgressAllowlist` repo + `egress-allowlisted` client (`code-execution-sandbox.md`).
- **Background processing** — in-box `BackgroundService` + `PeriodicTimer` for the retry-drain; `IRunOnceGuard` for multi-node.
- **Validation** — in-box **.NET 10 `AddValidation()`** source generator on request records; async/uniqueness rules in the service layer returning `Result<T>`.
- **Testing** — xunit.v3, NSubstitute, AwesomeAssertions; SQLite in-memory for service tests; Testcontainers Postgres for the RLS-isolation + retry-drain concurrency subset. Tests prove **behavior**:
  - signature verify accepts a known-good vector and rejects a one-byte-flipped body;
  - a duplicate `ProviderEventId` on **one** endpoint produces exactly one journal row + one fan-out;
  - **two tenants ingesting the identical `ProviderEventId` produce two distinct journal rows + two independent fan-outs** (the endpoint-salted `WebhookEventId` §3.2.1 — guards the cross-tenant collision);
  - a forged no-HMAC (Ko-fi) request that pre-claims a genuine future `ProviderEventId` does **not** suppress the later genuine event (different `rawBody` → different dedup key, §3.2 step 4);
  - an over-cap body returns `413` with no journal row;
  - **unknown-token flood is throttled by the pre-resolution IP limiter (`429`) and emits no `InboundWebhookRejectedEvent`** (§5.2 step 0/8);
  - **a `webhook`-triggered pipeline that references `{{payload.*}}` in a `ban`/`timeout`/`shoutout`/`http_request`/`send_webhook` sensitive param is rejected at validate (`tainted_payload_in_sensitive_param`)** and never executes (§7.1);
  - **an outbound endpoint subscribed to `'*'` that fires once produces exactly one delivery, not a cascade** (lifecycle deny-list + causation guard, §9);
  - **the webhook delivery path traverses the same `egress-allowlisted` `ConnectCallback`** (architecture test, §8.1) while carrying the signature headers + body (a delivery with stripped `webhook-signature` would be the regression);
  - an outbound delivery row records the triggering `EventType` + `JournalEventId` (from `ActionContext`, §6/§4.4);
  - an outbound non-2xx schedules a `NextRetryAt` and the 20th consecutive failure flips `DisabledAt` + emits `OutboundWebhookAutoDisabledEvent`;
  - a rotated secret signs with two `v1,` signatures during overlap.

---

## 11. Decisions (resolved)

The numeric/policy forks below are **decided** and applied throughout the spec above. Each value is fixed in config at the named floor; the config knob exists for deployment, not for re-litigating the decision.

1. **Inbound replay — 10-min timestamp tolerance + a dedup-retention SECURITY FLOOR.** Inbound replay is guarded two ways, and **both** numbers are fixed so the call site builds and the floor is not tuned away:
   - **Timestamp tolerance is 10 min** (generic adapter, which **requires** a `webhook-timestamp`), matching the in-house EventSub verifier (`twitch-eventsub.md` §3.6) — not Stripe's non-normative 5 min.
   - **Dedup retention `ExpiresAt` (`WebhookReplayRetention`) is the replay boundary — a security floor, not a perf knob:** **30 days** for **Ko-fi and GitHub** (no timestamp → the O.4 dedup row is the *sole* replay barrier, so a long fixed retention is what stops a captured still-valid request from re-firing after expiry; the pruner MUST NOT shorten it below this floor), and **24h** for the **generic** adapter (its required timestamp + 10-min tolerance is the primary guard, so the dedup row only needs to survive a provider redelivery storm). For no-HMAC adapters the claim key folds in `SHA-256(rawBody)` (§3.2 step 4) so a chosen-`ProviderEventId` pre-claim cannot shadow a distinct legit event. Every `IIdempotencyGuard.TryClaimAsync` call passes this as `IdempotencyClaimRequest.ExpiresAt` (the contract requires it).

2. **Outbound retry — 5 attempts over ~1 h; outbound idempotency `ExpiresAt` is 7 days.** The outbound retry schedule is **5 attempts** with exponential backoff + jitter over roughly one hour (~1 min, ~5 min, ~15 min, ~30 min, ~60 min, ± jitter), then **dead-letter**. Stripe's exact schedule/jitter is unpublished, so these are the project's numbers, fixed in config. The **`webhook:out` idempotency `ExpiresAt` (`WebhookOutboundIdempotencyRetention`) is 7 days** — comfortably longer than the full retry-to-dead-letter window so the same event never double-enqueues across the retry lifecycle, and bounded so O.4 does not grow unbounded. Passed as `IdempotencyClaimRequest.ExpiresAt` in every `EnqueueFor*Async` claim.

3. **Outbound auto-disable — 20 consecutive failures.** An outbound endpoint **auto-disables after 20 consecutive failures** (`ConsecutiveFailureCount`): set `DisabledAt`, emit `OutboundWebhookAutoDisabledEvent`; a successful delivery resets the counter. The operator re-enables via `ReenableAsync`.

4. **Body size caps — 256 KiB inbound; H.7 `MaxRequestBytes` outbound.** The inbound body size cap is **256 KiB** (`413` over cap, before full buffering). The outbound request-body cap is the per-row H.7 `MaxRequestBytes` (default 8192, **reject not truncate**); a streamer raises it per endpoint as needed.

5. **Inbound adapter set — Ko-fi, GitHub, Generic/Standard-Webhooks.** The inbound adapter set is **Ko-fi, GitHub, and Generic/Standard-Webhooks** (Generic covers Zapier/IFTTT/Make/Stream Deck/custom). A Streamlabs adapter is part of the plan and ships on the same `IInboundWebhookAdapter` seam — additive, no contract change. StreamElements is an **integration** (WebSocket + OAuth), **not** a webhook, and belongs to the integrations subsystem — a dependency boundary, not this subsystem's surface.

> Settled foundations (restated for clarity): inbound addressing is the per-channel opaque-token URL `/api/v1/webhooks/in/{token}` (OverlayToken model); outbound signing is Standard Webhooks (`whsec_`, `v1,`, multi-sig rotation); inbound becomes a `Source="webhook"` journal event with a deterministic `EventId` = **`WebhookEventId(broadcasterId, endpointId, ProviderEventId)`** (endpoint+tenant-salted UUIDv5, §3.2.1 — never the bare provider id); the dispatcher owns token resolution (controller does the pre-resolution limiter + size/method/content guards only); `payload.*` is a quarantined **tainted** namespace barred from security-sensitive action params (§7.1); webhook-lifecycle events are deny-listed from outbound fan-out (no self-amplification, §9); `TriggerKind=webhook` is added to `commands-pipelines.md` and runs under the `WebhookSystemActor` non-user contract; secrets are AEAD-enveloped with the explicit `CipherAad`/`KeyUsageBinding` mapping (§3.1/§3.5); SSRF is the reused H.7 + `egress-allowlisted` `ConnectCallback` core under a trusted-webhook header/body policy (§8.1).
