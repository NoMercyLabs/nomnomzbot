# Webhooks — Interface Specification (implementable)

**Subsystem area:** user / third-party **webhooks**, both directions —
- **Inbound:** an external service (Ko-fi, GitHub, Zapier/IFTTT/Make, Stream Deck, any Standard-Webhooks sender) `POST`s to a per-channel opaque URL; the request is verified per-adapter, deduped, journaled as a first-class event, and fanned out to the channel's pipelines/event-responses.
- **Outbound:** the channel emits signed `POST`s to author-configured endpoints (Standard Webhooks signing) when a pipeline action fires, with at-least-once delivery, retry, dead-letter, and auto-disable.

**Out of scope here — provider-specific webhook controllers.** Kick's chat/event webhook (`KickWebhookController`, `POST /api/v1/webhooks/kick`, verified by `IKickWebhookVerifier` against Kick's RSA public key) and Stripe's billing webhook (`BillingWebhookController`, `POST /api/v1/billing/webhooks/stripe`, `IStripeWebhookHandler` + `StripeWebhookSignature`) each have their own controller, verifier and handler. They do **not** go through the token-addressed ingest plane, the `WebhookAdapterKind` seam or `InboundWebhookEndpoint` rows described below.

This is **distinct from Twitch EventSub** (`twitch-eventsub.md`) — that is the platform's *own* first-party ingest of Twitch's events. This subsystem is the *user-facing* integration surface for arbitrary third parties. The two share only the journal (`event-store.md`) and the in-box HMAC verifier *pattern*; they are separate subsystems with separate addressing, separate registries, and separate adapters.

**Status:** implemented (shipped) — see the *Implementation status* note below for where the code departs from this spec. Namespace is `NomNomzBot.*` in every `.cs` file; folders/products are `NomNomzBot.*`.

**Grounding:**
- Locked schema — new Domain H sub-block H.8–H.10 (Webhooks); reuses O.1 `EventJournal` (+`Source="webhook"`), O.4 `IdempotencyKey`, H.7 `HttpEgressAllowlist`. `.claude/docs/design/2026-06-16-database-schema.md`.
- Reused contracts (do **not** reinvent): `event-store.md` (§3.1 `IEventJournal`, §3.7 `ITenantSequenceAllocator`, §3.10 `IIdempotencyGuard`), `commands-pipelines.md` (§3.8 `IEventResponseService.TriggerAsync`, §6.1 `ICommandAction`, §6.3 template engine, H.1 `Pipeline.TriggerKind`, I.2 `EventResponse`), `code-execution-sandbox.md` (§7 SSRF egress: `egress-allowlisted` named client + `ConnectCallback` pinned-IP connect, H.7 allowlist — built as `NomNomzBot.Infrastructure/Sandbox/EgressHttpClient.cs` + `EgressAddressGuard.cs`; note there is **no `http_request` pipeline action** in the code — the egress client's callers are listed in `code-execution-sandbox.md` §7.1), `gdpr-crypto.md` (§3.1 `IKeyVault`, §3.2 `IFieldCipher`, §3.4 `ISubjectKeyService`), `platform-conventions.md` (Channels.OverlayToken addressing model, §3.7 `IRateLimiterPartitionStore`, §3.8 `IRunOnceGuard`, `ExposureModel`), `twitch-eventsub.md` (§3.6 `IWebhookSignatureVerifier` in-box HMAC pattern — mirrored, not shared).
- Stack — `.claude/docs/design/2026-06-16-stack-and-dependencies.md` (in-box crypto only, hand-rolled HTTP egress, EF Core 10, profile adapters, Newtonsoft for `[VC:JSON]`).

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

> **Implementation status: shipped.** The subsystem is live in `server/src` (inbound ingest, outbound delivery, retry drain, `send_webhook` action, management controller, dashboard endpoints). This spec was written clean-slate, so where the code departs from it the **code is the as-built truth** and each departure is called out in the section it touches. The consolidated list of deviations:
>
> 1. **Module-first layout, not layer-first.** Domain: `NomNomzBot.Domain/Webhooks/{Entities,Enums,Events}/` (namespaces `NomNomzBot.Domain.Webhooks.*`). Contracts: `NomNomzBot.Application/Contracts/Webhooks/` (all six interfaces, including both endpoint services — there is no `NomNomzBot.Application.Services.Webhooks`). DTOs: one file, `NomNomzBot.Application/DTOs/Webhooks/WebhookDtos.cs`. Infrastructure: `NomNomzBot.Infrastructure/Webhooks/` (dispatchers, endpoint services, signer, verifier, renderer, catalogue, fan-out hook, `Adapters/`, `EventHandlers/`, `PipelineActions/`) plus `NomNomzBot.Infrastructure/BackgroundServices/WebhookDeliveryWorker.cs`. EF configurations sit flat in `NomNomzBot.Infrastructure/Platform/Persistence/Configurations/`.
> 2. **Seven adapter kinds, one class per provider** (§2, §9): `Kofi`, `Github`, `Generic`, `Fourthwall`, `Shopify`, `Patreon`, `Buymeacoffee`. There is no `Supporter` umbrella adapter and no `CustomData` kind.
> 3. **Secrets use `ITokenProtector`, not `ISubjectKeyService.ProtectAsync`** (§1, §3.1, §8). One sealed-envelope column per secret; there are no separate cipher/nonce columns.
> 4. **`IWebhookDeliveryWorker` does not exist.** Retry is `WebhookRetryProcessor` (scoped) driven by `WebhookDeliveryWorker` (a `BackgroundService`) — §3.7.
> 5. **Inbound dedup rides the journal, not `IIdempotencyGuard`** (§3.2). Outbound enqueue takes **no** idempotency claim. Idempotency is an open owner question.
> 6. **Inbound rate limiting is the ASP.NET `Anonymous` rate-limit policy on the controller**, not the two-tier `IRateLimiterPartitionStore` design (§5.2). Rate limiting is an open owner question.
> 7. **`payload.*` taint (§7.1) is not built.** No `TaintedVariables` sub-bag, no `tainted_payload_in_sensitive_param` validator rule. Taint is an open owner question.
> 8. **The delivery retry cap is not built** (§11.2). Backoff is 30 s doubling to a 1 h ceiling with jitter and each delivery keeps retrying until the endpoint's consecutive-failure counter reaches 20 and auto-disables it. There is no per-delivery `MaxAttempts` dead-letter. The retry cap is an open owner question.
> 9. **No `IIdempotencyGuard`, no `WebhookSystemActor`, no `CausationId` cycle guard** — see §3.2, §3.2.2 and §9.
> 10. **Prerequisite gap: nothing creates `HttpEgressAllowlist` rows — S-EGRESS-ALLOWLIST-CRUD.** `OutboundWebhookEndpointService.CreateAsync` requires an enabled H.7 row for the FQDN and fails `EGRESS_NOT_ALLOWED` otherwise; today no service, controller or seeder inserts one, so outbound endpoints can be created only where a row already exists.
> 11. **The two-policy header/body split and the byte clamps (§3.6, §8, §8.1) are design intent, not code** — see §8.1.
> 12. **Outbound endpoints add `BodyIsJson`; deliveries add `RenderedBody`** (§1); routes add blast-radius, event-catalogue and delivery-retry (§5.1).
> 13. **Services read and write through `IApplicationDbContext` directly** (no `IUnitOfWork` repository layer), and the inbound/outbound endpoint services do not enforce `(BroadcasterId, Name)` uniqueness (§1).

---

## 1. Entities (locked-schema, this subsystem owns)

New Domain H sub-block **H.8–H.10**, placed beside `HttpEgressAllowlist` (H.7) — webhooks are the user-facing egress/ingress surface that reuses H.7's SSRF boundary, so they live in the same domain. Defined in `.claude/docs/design/2026-06-16-database-schema.md`. **As built, the code differs from the schema on the secret columns and adds two columns** (called out below) — the entity classes are the truth. EF entity classes live in `NomNomzBot.Domain/Webhooks/Entities/` (namespace `NomNomzBot.Domain.Webhooks.Entities`); the three configurations live flat in `NomNomzBot.Infrastructure/Platform/Persistence/Configurations/`.

**Sealed secret envelope (as built).** Every secret is one column holding an `ITokenProtector` sealed envelope — key id, nonce, ciphertext and tag are serialized inside the single string. The schema's separate `*Cipher` + `*Nonce` column pairs do **not** exist. `EncryptionKeyId` is kept on each endpoint row as the id of the tenant's webhook DEK. Reading a secret is `ITokenProtector.TryUnprotectAsync(envelope, context)`, which returns `null` (never throws) when the envelope is malformed, the DEK was crypto-shredded, or authentication fails; the dispatchers treat `null` as "secret cannot be decrypted" (inbound: reject; outbound: the attempt fails with `Signing secret could not be decrypted.`).

| # | Entity | PK | Kind | Key fields (from schema) |
|---|--------|----|------|--------------------------|
| H.8 | `OutboundWebhookEndpoint` | `Id guid` | `[soft-delete]` | `BroadcasterId guid`; `Name string(200)`; `Fqdn string(253)` (mirror of the H.7 `HttpEgressAllowlist.Fqdn` this endpoint must match); `HttpEgressAllowlistId guid?` (FK→H.7, the approved egress row); `Path string(255)?`; `SubscribedEventTypesJson text` **[VC:JSON]** `List<string>` (event types this endpoint receives — `*` = all); `BodyTemplate text?` (author template rendered by `ITemplateEngine`); `CustomHeadersJson text?` **[VC:JSON]** `Dictionary<string,string>` (author headers, also templated); `BodyIsJson bool` (default `true`; whether `BodyTemplate` is authored as JSON — validated at save time and rendered through the JSON-safe leaf-substitution path, else plain text; added by S-WEBHOOK-JSON-FALLBACK, not in the schema); `SigningSecretEnvelope string(1024)` **[PII-shred]** (`ITokenProtector`-sealed `whsec_` secret); `SecondarySigningSecretEnvelope string(1024)?` **[PII-shred]** (overlap-valid sealed secret during rotation → multi-sig); `EncryptionKeyId guid` (the tenant's webhook DEK id); `IsEnabled bool`; `ConsecutiveFailureCount int`; `DisabledAt timestamp?` (set when auto-disabled); `DisabledReason string(255)?`; `LastDeliveryAt timestamp?`; `LastSuccessAt timestamp?`. **Index (NOT unique)** `(BroadcasterId, Name)` — the configuration carries a plain index (the entity is soft-deletable) and **no service enforces name uniqueness either**, despite the configuration comment; duplicate names are currently allowed. |
| H.9 | `OutboundWebhookDelivery` | `Id bigint` | `[APPEND-ONLY]` | `BroadcasterId guid`; `EndpointId guid` (FK→H.8); `WebhookMessageId guid` (the `webhook-id` we sent — dedupe key the receiver sees); `JournalEventId guid?` (FK→`EventJournal.EventId` — the event that triggered this send); `EventType string(150)`; `RenderedBody text` (the exact body that was sent, stored so a retry or manual replay re-sends the same bytes and re-signs them — added, not in the schema); `Attempt int` (1-based); `Status string(20)` **[VC:enum]** (`pending`\|`delivered`\|`failed`\|`dead_letter`); `ResponseCode int?`; `DurationMs int?`; `NextRetryAt timestamp?`; `Error string(1000)?` (scrubbed transport/HTTP error). **Index** `(EndpointId, CreatedAt)`, `(Status, NextRetryAt)` (the retry-drain scan). |
| H.10 | `InboundWebhookEndpoint` | `Id guid` | `[soft-delete]` | `BroadcasterId guid`; `Name string(200)`; `Token string(64)` **Unique** (opaque unguessable per-endpoint token, OverlayToken model — the URL path segment; not PII; minted as 32 random bytes → 64 lowercase hex chars); `AdapterKind string(20)` **[VC:enum → string]** (`Kofi`\|`Github`\|`Generic`\|`Fourthwall`\|`Shopify`\|`Patreon`\|`Buymeacoffee` — see §2); `VerificationSecretEnvelope string(1024)` **[PII-shred]** (`ITokenProtector`-sealed per-provider secret/token); `EncryptionKeyId guid` (the tenant's webhook DEK id); `GenericConfigJson text?` **[VC:JSON]** (`GenericInboundConfig`: signature header name, signing-string template, or shared-secret-in-body field — only for `AdapterKind=generic`); `TargetPipelineId guid?` (FK→`Pipelines` H.1 — the pipeline to run on a verified hit; null = fan out via `IEventResponseService`); `TargetEventType string(100)?` (override the derived `webhook.<provider>.<kind>` event type); `IsEnabled bool`; `LastReceivedAt timestamp?`; `ReceiveCount bigint`. **Unique** `Token`; **Index (NOT unique)** `(BroadcasterId, Name)` (no service enforces name uniqueness). |

**Cross-subsystem references (owned elsewhere — referenced, not redefined):** `Channels` (A.2, tenant root), `Users` (A.1, `ApprovedByUserId`), `HttpEgressAllowlist` (H.7, the SSRF allowlist row each outbound endpoint pins to), `EventJournal` (O.1, inbound events appended with `Source="webhook"`), `IdempotencyKey` (O.4, inbound dedupe + outbound idempotency — **no new table**), `CryptoKey` (Q.1, the DEK behind every `*Cipher`), `Pipelines` (H.1, inbound target), `EventResponses` (I.2, inbound fan-out target).

> **Reuse, do not duplicate (binding).** Outbound SSRF safety is **entirely** H.7 + the sandbox `egress-allowlisted` client — H.8 stores the per-endpoint `whsec_`/template/subscription set and **points at** an H.7 row for the actual egress boundary. Inbound replay/dedupe introduces **no** dedupe column on H.10. *Design intent:* dedupe is O.4 `IdempotencyKey` via `IIdempotencyGuard`. *As built:* dedupe rides the journal's idempotent-on-`EventId` behaviour (§3.2 step 4b) and `IIdempotencyGuard` is **not** called by the webhook code. Per-tenant ordering on inbound journal events is `IEventJournal.AppendAsync` allocating `StreamPosition` (`event-store.md`).

---

## 2. Domain events

Namespace `NomNomzBot.Domain.Webhooks.Events` (`NomNomzBot.Domain/Webhooks/Events/WebhookEvents.cs`). As built, all five are `sealed class`es deriving `DomainEventBase` (`NomNomzBot.Domain.Platform`) with `required` / `init` properties — not the `sealed record` positional form shown below (`EventId`/`BroadcasterId : Guid`/`OccurredAt` come from the base; do not redeclare them). The signatures below give the field set; construct them with object initializers. These are bus events (`IEventBus.PublishAsync`), consumed by the dashboard activity feed, the outbound delivery worker, and the projection that maintains endpoint health counters.

```csharp
namespace NomNomzBot.Domain.Webhooks.Events;

using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Webhooks.Enums;

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

**New enums** (namespace `NomNomzBot.Domain.Webhooks.Enums`, `NomNomzBot.Domain/Webhooks/Enums/WebhookEnums.cs`; each stored as its string name):

```csharp
namespace NomNomzBot.Domain.Webhooks.Enums;

// One adapter kind per provider — each kind has its own IInboundWebhookAdapter class with its own verification scheme
// (there is NO umbrella `Supporter` adapter and NO `CustomData` kind — those were design placeholders):
//   Kofi         — no HMAC; the JSON `verification_token` field must equal the secret (FixedTimeEquals). Low-assurance.
//   Github       — X-Hub-Signature-256 = "sha256=" + HMAC-SHA256(secret, rawBody).
//   Generic      — configurable HMAC + required timestamp header, or shared-secret-in-body (GenericInboundConfig).
//   Fourthwall   — X-Fourthwall-Hmac-SHA256 = base64(HMAC-SHA256(secret, rawBody)).
//   Shopify      — X-Shopify-Hmac-SHA256 = base64(HMAC-SHA256(secret, rawBody)); kind = X-Shopify-Topic; dedupe id = X-Shopify-Webhook-Id.
//   Patreon      — X-Patreon-Signature = hex(HMAC-MD5(secret, rawBody)); kind = X-Patreon-Event.
//   Buymeacoffee — X-Signature-Sha256 = hex(HMAC-SHA256(secret, rawBody)), no prefix; kind = body `type`; dedupe id = body `event_id`.
// The five monetization providers (Kofi, Fourthwall, Shopify, Patreon, Buymeacoffee) are routed on into supporter ingest by
// SupporterWebhookBridge (§3.2 step 6); custom-data push sources reference an InboundWebhookEndpoint by id instead.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebhookAdapterKind { Kofi, Github, Generic, Fourthwall, Shopify, Patreon, Buymeacoffee }
public enum WebhookDeliveryStatus { Pending, Delivered, Failed, DeadLetter }
public enum WebhookRejectReason
{
    InvalidSignature, ReplayWindow, Disabled, PayloadTooLarge,
    UnsupportedMediaType, RateLimited, Malformed, UnknownEndpoint
}
```

---

## 3. Service interfaces

**As built, every interface below lives in `NomNomzBot.Application.Contracts.Webhooks`** (`NomNomzBot.Application/Contracts/Webhooks/`) — the two endpoint services included; `NomNomzBot.Application.Services.Webhooks` does not exist. Implementations are in `NomNomzBot.Infrastructure/Webhooks/`; the inbound adapters in `NomNomzBot.Infrastructure/Webhooks/Adapters/`. The DTOs are in one file, `NomNomzBot.Application/DTOs/Webhooks/WebhookDtos.cs` (§4). The listings below show the design contract; §3.x notes give the as-built deltas. Every fallible op returns `Result`/`Result<T>`; `CancellationToken ct = default` last; `Guid broadcasterId` first where tenant-scoped.

### 3.1 `IInboundWebhookEndpointService` — inbound endpoint CRUD (H.10)

Owns `InboundWebhookEndpoint` rows. Mints the opaque token (`RandomNumberGenerator`, 32 random bytes → 64 lowercase hex chars, OverlayToken model). **As built, the verification secret is sealed with `ITokenProtector.ProtectAsync(plaintext, context)`** (`NomNomzBot.Application.Common.Interfaces.Crypto`), not `ISubjectKeyService.ProtectAsync`. The mapping is frozen here:

- `context` = `new TokenProtectionContext(SubjectId: broadcasterId.ToString(), Provider: "webhook:in", Field: endpointId.ToString())` — **identical on seal and unseal** (`TryUnprotectAsync` returns `null` on any context mismatch). `ITokenProtector` derives the per-subject DEK itself (minted on first use).
- The returned sealed-envelope string is stored in `InboundWebhookEndpoint.VerificationSecretEnvelope`. The dispatcher **unseals** it per request with `TryUnprotectAsync(endpoint.VerificationSecretEnvelope, context)`; `null` → reject `InvalidSignature` (403).
- `EncryptionKeyId` is set from `ISubjectKeyService.GetOrCreateSubjectKeyAsync` over the same subject hash `ITokenProtector` uses (`SHA-256("webhook:in:{broadcasterId}")`); it is informational bookkeeping, not an input to decryption.
- **No `KeyUsageBinding` is written**, so `RotateKeyAsync` re-encryption does **not** cover webhook secret columns (design intent in the original spec; not built). Crypto-shred of the subject DEK still makes every sealed envelope unreadable.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;   // as built (design text said Application.Services.Webhooks)

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

    /// As built (S-CONSEQ): the real, counted blast radius of deleting this endpoint — an exhaustive FK count of the
    /// CustomDataSource and SupporterConnection rows that carry InboundWebhookEndpointId and would stop receiving.
    /// Failure when the endpoint does not exist in this tenant.
    Task<Result<BlastRadiusDto>> GetDeleteBlastRadiusAsync(Guid broadcasterId, Guid endpointId, CancellationToken ct = default);
}
```

### 3.2 `IInboundWebhookDispatcher` — verified ingest → journal → event (the core ingest path)

`NomNomzBot.Application.Contracts.Webhooks`; implementation `NomNomzBot.Infrastructure/Webhooks/InboundWebhookDispatcher.cs` (Scoped). The single place a raw inbound request becomes a verified, deduped, journaled event. Called by the `[AllowAnonymous]` ingest controller (§5.2). **The dispatcher stops at publishing `InboundWebhookReceivedEvent`** — routing to a pipeline / event-response / supporter ingest happens in event handlers (step 6), keeping execution out of the ingest hot path.

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;

public interface IInboundWebhookDispatcher
{
    Task<Result<InboundDispatchResult>> DispatchAsync(InboundWebhookRequest request, CancellationToken ct = default);
}
```

**As-built pipeline** (the dispatcher owns token resolution — the controller does not pre-resolve):

1. **Resolve** the `InboundWebhookEndpoint` by `Token` (`Token == request.Token && DeletedAt == null`). Unknown/soft-deleted → `Reject(UnknownEndpoint, 404)` with **no bus event**. Disabled → `InboundWebhookRejectedEvent` + `Reject(Disabled, 503)`. The lookup is a plain unique-index probe, not an explicit constant-time compare. On resolve, `ResolvedEndpointId` / `ResolvedBroadcasterId` / `ResolvedAdapter` are filled on the result.
2. **Select the adapter** whose `Kind == endpoint.AdapterKind`. None registered → `Malformed` (400). **Unseal the secret** with `ITokenProtector.TryUnprotectAsync` (context per §3.1); `null` → `InvalidSignature` (403).
3. **Verify** via `adapter.Verify(request, secretBytes, genericConfig)`. A failed verification is rejected with its reason (`ReplayWindow` → 403, `Malformed` → 400, anything else → 401), emits `InboundWebhookRejectedEvent`, and is never processed.
   - The **10-minute `ReplayTolerance`** lives in `GenericInboundWebhookAdapter` (a private `TimeSpan.FromMinutes(10)`), applied through `IInboundSignatureVerifier.VerifyWithTimestamp`. A stale timestamp is reported as **`InvalidSignature` (401)**, not `ReplayWindow`: the adapter is pure and cannot tell a stale-but-authentic message from a forged one. `ReplayWindow` therefore exists in the enum and status map but no adapter emits it today.
   - The generic-config invariant "HMAC mode requires `TimestampHeaderName`" is enforced **at verify time** (`Malformed`), not at endpoint save.
4. **Parse** via `adapter.Parse(request, genericConfig)`; failure → `Malformed` (400). `eventType = "webhook.<provider>.<kind>"` where `<provider>` is the lowercase adapter label (`kofi`, `github`, `fourthwall`, `shopify`, `patreon`, `buymeacoffee`, `generic`).
   - **4b. Dedup by journal, not `IIdempotencyGuard`.** The journal `EventId` is the endpoint-salted UUIDv5 of the as-built `dedupKey` (§3.2.1). If `IEventJournal.GetByEventIdAsync(eventId)` already succeeds, the request is a **duplicate**: return `200` with `WasDuplicate = true`, `StreamPosition = 0`, **no journal write, no bus event**. There is no `WebhookReplayRetention` and no `ExpiresAt`: the dedup memory is the journal row itself, so it lasts as long as the journal keeps that event.
5. **Journal**: `IEventJournal.AppendAsync(eventId, broadcasterId, eventType, schemaVersion 1, Source = "webhook", payload = JSON of the flattened variable bag, metadata "{}", now)` → `StreamPosition` allocated by the journal. Then bump `LastReceivedAt` / `ReceiveCount` and save.
6. **Emit `InboundWebhookReceivedEvent`** (`WasDuplicate = false`) on the bus. Two `IEventHandler<InboundWebhookReceivedEvent>` handlers consume it:
   - **`InboundWebhookAutomationBridge`** (`Infrastructure/Webhooks/EventHandlers/`) — skips duplicates and tenant-less events; loads the endpoint; if neither `TargetPipelineId` nor `TargetEventType` is set the endpoint is a pure journal sink and nothing runs. Otherwise it rebuilds the variable bag from the journaled payload — `payload.<field>` for every flattened field plus `webhook.event_type`, `webhook.provider`, `webhook.provider_event_id` — and runs **`IPipelineEngine.ExecuteAsync`** (when `TargetPipelineId` is set; `TriggeredByUserId = "webhook"`, `TriggeredByDisplayName = endpoint.Name`, `InitialVariables = bag`) **or** `IEventResponseExecutor.ExecuteAsync(broadcasterId, endpoint.TargetEventType, userId: null, endpoint.Name, bag)`. A pipeline fault is logged and never propagates back into the bus.
   - **`SupporterWebhookBridge`** (`Infrastructure/Supporters/EventHandlers/`) — for the five monetization adapters (`Kofi`, `Fourthwall`, `Shopify`, `Patreon`, `Buymeacoffee`, mapped by `SupporterWebhookAdapters`) it loads the journaled payload and calls `ISupporterIngestService.IngestAsync(broadcasterId, sourceKey, payloadJson)`. Other adapters (`Github`, `Generic`) are ignored by this bridge.
7. Return the verified+journaled `InboundDispatchResult` (`HttpStatus 200`).

**Not built (design intent kept for the owner):** the pre-resolution + post-resolution `IRateLimiterPartitionStore` tiers (the `RateLimited` reject reason and a dispatcher-issued `429` never occur — the controller's ASP.NET `Anonymous` rate-limit policy is the only throttle, §5.2); `IIdempotencyGuard` claims and their retention floors; the `WebhookSystemActor` `Guid.Empty` pipeline trigger (§3.2.2). Idempotency and rate limiting are open owner questions.

#### 3.2.1 Deterministic journal `EventId` — endpoint-salted (no cross-tenant collision)

The inbound journal `EventId` is **not** derived from `ProviderEventId` alone. Provider ids are small/guessable/attacker-shaped (Ko-fi sequential transaction ids, a generic `$.id` of `1`, a GitHub delivery id two channels can both receive), and `EventJournal.Unique(EventId)` is **global** (not per-tenant). A `ProviderEventId`-only derivation would let endpoint A's id collide with endpoint B's: `AppendAsync` is idempotent-on-`EventId` and would either return A's existing row to B (cross-tenant lineage read) or silently swallow B's distinct event (cross-tenant denial). The eventsub precedent is safe only because Twitch message-ids are globally-unique UUIDs — webhook provider ids are not.

**As built** (`InboundWebhookDispatcher`): the salted id is derived from a `dedupKey`, not from the bare `ProviderEventId`:

```csharp
// NomNomzBot.Infrastructure.Webhooks.InboundWebhookDispatcher — private static, pure.
static readonly Guid WebhookNamespace = new("d6b4f0a2-9c7e-5b1d-8e3a-2f6c4b9a7e10");   // fixed UUIDv5 namespace, RFC 9562 §5.5
static Guid WebhookEventId(Guid broadcasterId, Guid endpointId, string dedupKey)
    => Uuid5(WebhookNamespace, $"{broadcasterId:N}|{endpointId:N}|{dedupKey}");

static string DedupKey(WebhookAdapterKind kind, GenericInboundConfig? config, string providerEventId, ReadOnlySpan<byte> rawBody)
{
    bool hmacBound = kind == WebhookAdapterKind.Github
                  || (kind == WebhookAdapterKind.Generic && config?.SignatureHeaderName is not null);
    return hmacBound
        ? providerEventId                                                            // secret+signature bind the id
        : $"{Convert.ToHexStringLower(SHA256.HashData(rawBody))}:{providerEventId}"; // body hash folded in
}
```

- **`Github` and `Generic`-in-HMAC-mode** use the bare `ProviderEventId` (the body is HMAC-bound, so pre-claiming an id needs the secret).
- **Every other adapter** — `Kofi`, `Fourthwall`, `Shopify`, `Patreon`, `Buymeacoffee`, and `Generic` in shared-secret-in-body mode — folds `lowerhex(SHA-256(rawBody)) + ":"` in front. `Fourthwall`, `Shopify`, `Patreon` and `Buymeacoffee` do carry an HMAC, but the as-built `hmacBound` test does not list them, so they take the body-hash form. Consequence: a provider redelivery that changes any byte of the body (for example Buy Me a Coffee's `attempt` counter) is **not** treated as a duplicate for these adapters. This is stricter than intended for HMAC providers and is recorded here as a known deviation.
- The `Generic` adapter falls back to `ReceivedAtUtc.Ticks` as the provider id when the configured `ProviderEventIdJsonPath` yields nothing, so a body without an id is never deduped.

The id is per-endpoint unique by construction. `event-store.md` §4 `AppendEventRequest.Source="webhook"` carries the same derivation.

#### 3.2.2 Webhook-trigger `PipelineRequest` — the non-user actor contract (as built)

An inbound third-party webhook has **no Twitch user**. The `WebhookSystemActor` sentinel (`Guid.Empty`) this section originally specified was **not built**: `InboundWebhookAutomationBridge` builds the `PipelineRequest` with `TriggeredByUserId = "webhook"` (a string literal — the request carries a string user id) and `TriggeredByDisplayName = endpoint.Name`, and seeds `InitialVariables` with the bag from step 6. `TriggerKind`, `RawMessage`, `Args` and the message/reward ids keep their `PipelineRequest` defaults. The `IEventResponseExecutor` branch takes `userId: null` and `userDisplayName: endpoint.Name`.

### 3.3 `IInboundWebhookAdapter` — per-provider verify + parse (multi-impl)

`NomNomzBot.Application.Contracts.Webhooks`. **One adapter class per `WebhookAdapterKind` — seven classes** in `NomNomzBot.Infrastructure/Webhooks/Adapters/` (`KofiInboundWebhookAdapter`, `GithubInboundWebhookAdapter`, `GenericInboundWebhookAdapter`, `FourthwallInboundWebhookAdapter`, `ShopifyInboundWebhookAdapter`, `PatreonInboundWebhookAdapter`, `BuymeacoffeeInboundWebhookAdapter`) plus the shared `WebhookAdapterHelpers`; the dispatcher selects by `AdapterKind`. **Each provider dictates its own verification scheme** — this is the seam that keeps provider quirks out of the dispatcher. Pure (no DB/I-O); the secret is supplied decrypted by the dispatcher. The per-provider schemes are listed at the `WebhookAdapterKind` enum (§2); the `///` block below is the design text and its `supporter` bullet describes the pre-split umbrella adapter.

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

Owns `OutboundWebhookEndpoint` rows. Mints the `whsec_<base64>` signing secret (`"whsec_" + base64(24 random bytes)`) and seals it with `ITokenProtector.ProtectAsync` using the **same mapping** as §3.1 with `Provider = "webhook:out"`: `new TokenProtectionContext(broadcasterId.ToString(), "webhook:out", endpointId.ToString())`, identical on seal and unseal. The envelope goes to `SigningSecretEnvelope`; rotation moves the current envelope to `SecondarySigningSecretEnvelope` and seals a fresh primary. The dispatcher **unseals both** with `TryUnprotectAsync` on every attempt (§3.6) and signs with each secret that opens. As with inbound, **no `KeyUsageBinding` is written** (design intent, not built). **Requires** the target FQDN to already have an enabled `HttpEgressAllowlist` (H.7) row for the tenant — creation fails `EGRESS_NOT_ALLOWED` otherwise (broker pattern: the URL/secret live on the endpoint, the SSRF boundary lives on H.7). **Prerequisite gap: nothing creates `HttpEgressAllowlist` rows — S-EGRESS-ALLOWLIST-CRUD.** No service, controller or seeder inserts an H.7 row today, so this check can only pass against a row that already exists. `CreateAsync` also rejects a `Path` that could move the request off the allowlisted host (`INVALID_PATH`, `OutboundWebhookTargetUrl.TryBuild`), an unknown or lifecycle event type in `SubscribedEventTypes` (`VALIDATION_FAILED` — the lifecycle deny-list, §9), and a `BodyTemplate` that does not validate (unknown `{{helper}}` key, or invalid JSON while `BodyIsJson` is true).

```csharp
namespace NomNomzBot.Application.Contracts.Webhooks;   // as built (design text said Application.Services.Webhooks)

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

    /// As built: the closed catalogue of subscribable business event types (`OutboundWebhookEventCatalogue.Entries`, grouped by category)
    /// an endpoint's SubscribedEventTypes is validated against — the dashboard renders a checklist from it. Lifecycle events are absent. Never fails.
    Result<IReadOnlyList<OutboundWebhookEventCatalogueEntry>> GetEventCatalogue();

    /// As built: the endpoint's delivery log, newest attempt first, paged. NOT_FOUND when the endpoint is not in this tenant.
    Task<Result<PagedList<OutboundWebhookDeliveryDto>>> ListDeliveriesAsync(Guid broadcasterId, Guid endpointId, PaginationParams pagination, CancellationToken ct = default);

    /// As built: manually replays ONE stored delivery with its already-stored RenderedBody (never re-rendered from the current template).
    /// NOT_FOUND when the delivery is not under this endpoint/tenant; ENDPOINT_DISABLED when the endpoint is disabled (re-enabling is a separate action).
    /// Delegates to IOutboundWebhookDispatcher.ReplayDeliveryAsync, which creates a NEW delivery row (fresh WebhookMessageId, attempt 1).
    Task<Result<OutboundWebhookDeliveryDto>> RetryDeliveryAsync(Guid broadcasterId, Guid endpointId, long deliveryId, CancellationToken ct = default);
}
```

### 3.6 `IOutboundWebhookDispatcher` — enqueue + sign + deliver (the core egress path)

`NomNomzBot.Application.Contracts.Webhooks`; implementation `NomNomzBot.Infrastructure/Webhooks/OutboundWebhookDispatcher.cs` (Scoped). Enqueues a delivery, signs per Standard Webhooks, and performs one attempt through the SSRF-hardened `egress-allowlisted` client (`EgressHttpClient.Name`). **As built there is no idempotency claim on enqueue** — every `EnqueueFor*Async` call mints a fresh `webhook-id` (`Guid.CreateVersion7()`) and inserts a delivery row (idempotency is an open owner question). The retry loop is driven by `WebhookRetryProcessor` (§3.7), which calls `AttemptDeliveryAsync` again.

**As-built signatures** — these differ from the design listing below in two places: `AttemptDeliveryAsync` takes the loaded `OutboundWebhookDelivery` and returns `Result<WebhookDeliveryStatus>`; and `ReplayDeliveryAsync` is added.

```csharp
Task<Result<WebhookDeliveryStatus>> AttemptDeliveryAsync(OutboundWebhookDelivery delivery, CancellationToken ct = default);

// Creates and sends a genuinely NEW delivery row (fresh id, fresh WebhookMessageId, attempt 1) carrying the original's stored RenderedBody.
// The original is never mutated. NOT_FOUND if the endpoint is gone (looked up with IgnoreQueryFilters, so a soft-deleted endpoint is rejected
// for the right reason); ENDPOINT_DISABLED if it is disabled — a replay must never appear to succeed while sending nowhere.
Task<Result<OutboundWebhookDelivery>> ReplayDeliveryAsync(OutboundWebhookDelivery original, CancellationToken ct = default);
```

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
    /// response capped). DESIGN INTENT (not built, §8.1): a TRUSTED-WEBHOOK header/body policy distinct from the sandbox guest policy, with the
    /// endpoint's H.7 row carrying AllowRequestBody=true + MaxRequestBytes=256 KiB. AS BUILT the dispatcher attaches webhook-id/webhook-timestamp/
    /// webhook-signature, the author CustomHeaders and the stored RenderedBody directly on the HttpRequestMessage; no header allowlist or body
    /// clamp is applied on this path. 2xx -> Status=delivered, resets
    /// ConsecutiveFailureCount, sets LastSuccessAt. Non-2xx / conn / TLS / timeout -> Status=failed, schedules NextRetryAt (exp backoff+jitter;
    /// 30 s doubling to a 1 h ceiling, x0.5-1.0 jitter); AS BUILT there is NO per-delivery MaxAttempts dead-letter (the retry cap is an open owner
    /// question) — Status=dead_letter is set only when the endpoint is disabled (including the attempt that crosses the threshold); bumps
    /// ConsecutiveFailureCount and auto-disables at 20. Emits
    /// OutboundWebhookAttemptedEvent (+ OutboundWebhookAutoDisabledEvent when tripped). Returns the attempt outcome.
    Task<Result<OutboundAttemptResult>> AttemptDeliveryAsync(long deliveryId, CancellationToken ct = default);
}
```

### 3.7 `WebhookRetryProcessor` + `WebhookDeliveryWorker` — retry drain (background)

**As built there is no `IWebhookDeliveryWorker` interface.** The drain is two classes:

- **`WebhookRetryProcessor`** (`NomNomzBot.Infrastructure/Webhooks/WebhookRetryProcessor.cs`, Scoped) — `Task<int> ProcessDueAsync(int batchSize, CancellationToken ct = default)`. Selects `OutboundWebhookDelivery` rows with `Status == Failed && NextRetryAt != null && NextRetryAt <= now`, ordered by `NextRetryAt`, takes `batchSize`, and for each one increments `Attempt` and calls `IOutboundWebhookDispatcher.AttemptDeliveryAsync(delivery)`. Returns the count attempted. It is directly unit-testable without the host.
- **`WebhookDeliveryWorker`** (`NomNomzBot.Infrastructure/BackgroundServices/WebhookDeliveryWorker.cs`, a `BackgroundService`) — a `PeriodicTimer` every **30 s**; each tick opens a scope, takes the `IRunOnceGuard` lease `"webhook-delivery-drain"` (30 s TTL; no lease → another instance is draining, skip the tick), then runs `WebhookRetryProcessor.ProcessDueAsync(batchSize: 50)`. A failed iteration is logged and retried next tick.

Backoff is `OutboundWebhookBackoffPolicy.ComputeDelay(attempt)`: `30 s × 2^(attempt-1)`, capped at 1 h, then multiplied by a random factor in `[0.5, 1.0)`. **There is no per-delivery attempt cap** (the 5-attempt schedule in §11.2 was not built): a failing delivery keeps retrying until the endpoint's consecutive-failure counter reaches 20, which disables the endpoint and dead-letters the delivery in flight. The retry cap is an open owner question.

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

**As built, every DTO and transport record below is in one file, `NomNomzBot.Application/DTOs/Webhooks/WebhookDtos.cs`, namespace `NomNomzBot.Application.DTOs.Webhooks`** — including the records this section files under `Contracts.Webhooks`. The listing shows the design shapes; **read `WebhookDtos.cs` for the truth** and apply these deltas:

- `InboundWebhookEndpointDto` gains a trailing `GenericInboundConfig? GenericConfig`.
- `OutboundWebhookEndpointDto` gains `string? BodyTemplate` and `bool BodyIsJson` (after `SubscribedEventTypes`). `CreateOutboundWebhookRequest` gains `bool BodyIsJson = true`; `UpdateOutboundWebhookRequest` gains `bool? BodyIsJson` (null = leave unchanged).
- `WebhookDeliveryDto` is built as **`OutboundWebhookDeliveryDto`**: `(long Id, Guid EndpointId, string EventType, int Attempt, string Status, int? ResponseCode, int? DurationMs, DateTime? NextRetryAt, string? Error, DateTime CreatedAt)` — `Status` is a `string`, and there is no `WebhookMessageId`. **`WebhookDeliveryQuery` is not built**: the delivery list takes `PaginationParams`.
- New: `OutboundWebhookEventCatalogueEntry(string EventType, string Label, string Category)`.
- `InboundWebhookRequest`, `WebhookVerification`, `ParsedInboundEvent`, `InboundDispatchResult` and `OutboundEnqueueResult` are in `DTOs.Webhooks` (not `Contracts.Webhooks`). `OutboundAttemptResult` and `InboundWebhookRequestContext` are **not built**. `WebhookSignatureHeaders` is declared in `IOutboundWebhookSigner.cs` with the members `(WebhookId, Timestamp, Signature)`.
- `BlastRadiusDto` (`NomNomzBot.Application.Common.Consequences`) is returned by the inbound blast-radius route.
- Enums come from `NomNomzBot.Domain.Webhooks.Enums` (`using NomNomzBot.Domain.Webhooks.Enums;`).

Secrets are **never** echoed back except the documented create-time/rotate-time reveal.

```csharp
namespace NomNomzBot.Application.DTOs.Webhooks;

using NomNomzBot.Domain.Webhooks.Enums;

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
namespace NomNomzBot.Application.DTOs.Webhooks;   // as built: same file as the DTOs above (WebhookDtos.cs)

using NomNomzBot.Domain.Webhooks.Enums;

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

Two controllers in `NomNomzBot.Api/Controllers/V1/` (`WebhooksController.cs`, `InboundWebhookController.cs`). The management controller is JWT-gated and tenant-scoped; the ingest controller is `[AllowAnonymous]` (auth = token + per-adapter signature, **never** the user JWT — exactly the OverlayHub model). The management controller inherits `BaseController` (`ResultResponse` / `GetPaginatedResponse`); the ingest controller is a plain `ControllerBase` that answers with a bare status code. (Kick's `POST /api/v1/webhooks/kick` and Stripe's `POST /api/v1/billing/webhooks/stripe` are separate controllers, out of scope here.)

### 5.1 `WebhooksController` — management plane

`[Route("api/v{version:apiVersion}/channels/{channelId:guid}/webhooks")]`, `[ApiVersion("1.0")]`, `[Authorize]`.

**Role gate.** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 is the **`[RequireAction("<actionKey>")]`** attribute on each action (it calls `IActionAuthorizationService.AuthorizeActionAsync` before the service call; 403 FORBIDDEN when below) — the controller does not call the authorization service inline. The keys are seeded global `ActionDefinition`s (schema B.3); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Webhook config is sensitive egress + secrets → the write floor is **Editor**; read is **Moderator** (Plane-B, channel management).

| Method | Route (suffix under `…/webhooks`) | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|--------|-----------------------------------|-------------|--------------|-----------------------------------|
| GET | `/inbound` | `PaginationParams` (query) | `PaginatedResponse<InboundWebhookEndpointDto>` | management / Moderator · `webhooks:inbound:read` |
| GET | `/inbound/{endpointId:guid}` | — | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Moderator · `webhooks:inbound:read` |
| GET | `/inbound/{endpointId:guid}/blast-radius` *(added)* | — | `StatusResponseDto<BlastRadiusDto>` — the counted `CustomDataSource` + `SupporterConnection` rows that stop receiving if the endpoint is deleted (S-CONSEQ; `[DestructiveAction(HasCountedBlastRadius = true)]`; the dashboard **must** call it and render the result before the delete confirm) | management / Moderator · `webhooks:inbound:read` |
| POST | `/inbound` | `CreateInboundWebhookRequest` | `StatusResponseDto<InboundWebhookEndpointDto>` (201) | management / Editor · `webhooks:inbound:write` |
| PUT | `/inbound/{endpointId:guid}` | `UpdateInboundWebhookRequest` | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Editor · `webhooks:inbound:write` |
| POST | `/inbound/{endpointId:guid}/rotate-token` | — | `StatusResponseDto<InboundWebhookEndpointDto>` | management / Editor · `webhooks:inbound:write` |
| DELETE | `/inbound/{endpointId:guid}` | — | 204 (`[DestructiveAction(HasCountedBlastRadius = true)]` — pairs with the blast-radius route above) | management / Editor · `webhooks:inbound:write` |
| GET | `/outbound/event-catalogue` *(added)* | — | `StatusResponseDto<IReadOnlyList<OutboundWebhookEventCatalogueEntry>>` — the closed, categorized set of subscribable business event types (§9); lifecycle events are absent | management / Moderator · `webhooks:outbound:read` |
| GET | `/outbound` | `PaginationParams` (query) | `PaginatedResponse<OutboundWebhookEndpointDto>` | management / Moderator · `webhooks:outbound:read` |
| GET | `/outbound/{endpointId:guid}` | — | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Moderator · `webhooks:outbound:read` |
| POST | `/outbound` | `CreateOutboundWebhookRequest` | `StatusResponseDto<OutboundWebhookEndpointCreatedDto>` (201, secret revealed once) | management / Editor · `webhooks:outbound:write` |
| PUT | `/outbound/{endpointId:guid}` | `UpdateOutboundWebhookRequest` | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/rotate-secret` | — | `StatusResponseDto<OutboundWebhookEndpointCreatedDto>` (secret revealed once) | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/reenable` | — | `StatusResponseDto<OutboundWebhookEndpointDto>` | management / Editor · `webhooks:outbound:write` |
| POST | `/outbound/{endpointId:guid}/test` | — | `StatusResponseDto<WebhookTestResultDto>` | management / Editor · `webhooks:outbound:write` |
| DELETE | `/outbound/{endpointId:guid}` | — | 204 (`[NotDestructive]` — no entity carries an `OutboundWebhookEndpointId` FK, so there is no blast-radius route for outbound) | management / Editor · `webhooks:outbound:write` |
| GET | `/outbound/{endpointId:guid}/deliveries` | `PaginationParams` (query) — no `WebhookDeliveryQuery` | `PaginatedResponse<OutboundWebhookDeliveryDto>` (newest first) | management / Moderator · `webhooks:outbound:read` |
| POST | `/outbound/{endpointId:guid}/deliveries/{deliveryId:long}/retry` *(added)* | — | `StatusResponseDto<OutboundWebhookDeliveryDto>` — replays that delivery's stored `RenderedBody` as a NEW delivery row; `NOT_FOUND` if the delivery is not under this endpoint/tenant; `ENDPOINT_DISABLED` while the endpoint is disabled | management / Editor · `webhooks:outbound:write` |

### 5.2 `InboundWebhookController` — public, token + signature-gated ingest

`[Route("api/v{version:apiVersion}/webhooks/in")]`, `[AllowAnonymous]`, `[Tags("Webhooks")]`. Auth is the opaque token + the per-adapter signature, **not** JWT. Reads the **raw buffered body** before any deserialization (signature is over the exact bytes). The token is **scrubbed from request logs** (same log-scrub rule as OverlayToken / `access_token`). **As built the controller carries `[EnableRateLimiting(RateLimitPolicyNames.Anonymous)]` (the host's ASP.NET anonymous rate-limit policy) and does not use `IRateLimiterPartitionStore`; the two-tier scheme below is design intent (an open owner question — rate limiting).** The controller checks content-type first (`415`), then the `256 KiB` cap (`413` on `Content-Length`, and again after buffering), reads the raw body, computes `RemoteIpHash` (carried on the request but not used to throttle), and dispatches. Design intent: rate-limited via `IRateLimiterPartitionStore` in **two tiers**: a **pre-resolution** tier keyed on data known *before* the DB token lookup (`wh:in:ip:{clientIpHash}` — and, optionally, `wh:in:rawtok:{sha256(token-segment)}`), and a **post-resolution** tier keyed on the resolved endpoint/tenant (`wh:in:{endpointId}` + `wh:in:tenant:{broadcasterId}`). The pre-resolution tier is what throttles unknown/garbage-token floods and token-guessing — neither post-resolution partition can fire on the 404 path because `endpointId`/`broadcasterId` don't exist yet.

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
6. Map the typed result (as built, the controller returns `dispatcher HttpStatus` as a bare status code, or `500` when the `Result` itself failed): verified+journaled or duplicate → **`200`** (a minimal `2xx` ack — never problem-details JSON, matching the EventSub-webhook ack convention so senders don't retry on a body they can't parse); `InvalidSignature` → `401`/`403`; `ReplayWindow` → `403`; `Malformed` → `400`. **The `200` means "verified and journaled", not "acted on":** the dispatcher has already published `InboundWebhookReceivedEvent`, and `InboundWebhookAutomationBridge` (target pipeline / event-response) and `SupporterWebhookBridge` (supporter ingest for the five monetization adapters) run as event handlers off the request path (§3.2 step 6).
7. **Generic errors only** — the public response never leaks internal detail (no stack, no entity ids, no SQL). Invalid HMAC is rejected `4xx` and **never processed**.
8. **No unthrottled bus emission on the unknown-token 404 path.** `InboundWebhookRejectedEvent` is emitted **only** for a *resolved* endpoint (bad signature / replay / disabled-target / over-limit on a known endpoint). The `UnknownEndpoint` (404) case **does not** emit a per-request bus event — that would hand an unauthenticated flooder a free amplification into the event bus; unknown-token volume is observed via the pre-resolution limiter's counter/metrics, coalesced, not as one bus event per probe.

> **Self-host reachability (`ExposureModel`).** SaaS = `managed_edge` → the ingest URL is publicly reachable out of the box. Self-host = `opt_in_tunnel` → the instance is behind NAT; the operator **opts in** by exposing this route via a reverse-proxy / Cloudflare tunnel (the same dev-tunnel story as EventSub OAuth). Inbound webhooks are therefore a self-host-with-public-exposure feature, parallel to EventSub being WebSocket-only (no inbound) on self-host. Default-deny: nothing is reachable until the operator exposes it.

---

## 6. Pipeline actions

One net-new `ICommandAction`. **The live contract** (`NomNomzBot.Application/Abstractions/Pipeline/ICommandAction.cs`): `string ActionType`; `LocalizedText Category` and `LocalizedText Description` (localization **keys**, e.g. `pipeline.category.webhooks`); `IReadOnlyList<PipelineActionFieldDescriptor> Fields` (the typed step form: `ResourceId`, `Text`, …, with a `Templated` flag); `bool ResolvesOwnTemplates`; and `Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)`, reading params through `action.GetString("<key>")`. Located in `NomNomzBot.Infrastructure/Webhooks/PipelineActions/SendWebhookAction.cs`; registered by the `ICommandAction` assembly scan in `DependencyInjection.cs` (no manual registration line).

| Type string | Config DTO (`context.Parameters`) | Behavior |
|-------------|-----------------------------------|----------|
| `send_webhook` *(new)* | **As built, two config keys:** **`endpoint`** (`PipelineActionFieldKind.ResourceId`, required — the outbound endpoint's public owned id, decoded with `OwnedIdCodec.TryDecode`) and **`event_type`** (`Text`, **not** templated, optional; defaults to `"pipeline.send_webhook"`). No url/secret/headers in step config (broker pattern). | Decodes `endpoint`, then calls `IOutboundWebhookDispatcher.EnqueueForEndpointAsync(ctx.BroadcasterId, endpointId, eventType, ctx.Variables, journalEventId: null)` where **`eventType` = the `event_type` key when non-blank, else `"pipeline.send_webhook"`**. `journalEventId` is always `null` as built (the `ActionContext.EventType`/`JournalEventId` seeding this spec assumed does not exist), which is valid because `OutboundWebhookDelivery.JournalEventId` is nullable. `event_type` is deliberately **not** templated: an endpoint's subscription matches it verbatim, so a literal double-brace string must reach the dispatcher unchanged. The url, secret, body/header templates, and SSRF boundary all live on the H.8 endpoint + its H.7 row — the step only names the endpoint. A missing/undecodable `endpoint`, an unknown endpoint (`NOT_FOUND`), or an enqueue failure returns `ActionResult.Failure`; otherwise `ActionResult.Success("send_webhook:<endpointId>")` (enqueue is fast-ack; the first attempt runs inline, retries are async). |

> **Config-validator invariant (reused).** `ICommandConfigValidator` (`commands-pipelines.md` §3.11) already rejects any step config key naming a url/secret/credential/peer-channel id. `send_webhook`'s keys are an opaque endpoint id (`endpoint`, resolved tenant-side) and a plain `event_type` label — neither carries a url or secret, satisfying the invariant by construction.

**Inbound as a trigger.** An inbound verified webhook is **not** an `ICommandAction` — it is a pipeline/event-response *trigger*. `commands-pipelines.md` H.1 gains `TriggerKind=webhook`; **as built, the dispatcher (§3.2) does not route directly**: it publishes `InboundWebhookReceivedEvent`, and `InboundWebhookAutomationBridge` runs `IPipelineEngine.ExecuteAsync` (when `TargetPipelineId` is set) or `IEventResponseExecutor.ExecuteAsync(broadcasterId, endpoint.TargetEventType, …)` (when `TargetEventType` is set); an endpoint with neither is a pure journal sink. `SupporterWebhookBridge` handles the five monetization adapters in parallel. An `EventResponse` (I.2) may also set `ResponseType=pipeline`/`chat_message` keyed on the `webhook.*` event type. The parsed body is seeded by the dispatcher into the template bag under the `webhook.*` / `payload.*` namespace (see §7).

---

## 7. Template namespace (inbound body → `webhook.*` / `payload.*`)

**As built**, the seeding is done by `InboundWebhookAutomationBridge` (not the dispatcher): it reads the flattened payload back from the journal row and passes the bag as the pipeline's `InitialVariables` / the event-response variables. The dispatcher seeds the parsed body into the variables bag **before** any render, under a new namespace — the `VariableResolver` (`commands-pipelines.md` §6.3) stays **pure / I-O-free**; no engine change, only a new pre-seeded namespace (exactly the documented pattern for Helix/economy/music tokens).

| Token | Value | Seeded by | Trust |
|-------|-------|-----------|-------|
| `{{webhook.provider}}` | the adapter kind, lowercased (`kofi`\|`github`\|`generic`\|`fourthwall`\|`shopify`\|`patreon`\|`buymeacoffee`) | `InboundWebhookAutomationBridge` | trusted (server-derived) |
| `{{webhook.event_type}}` *(design name: `webhook.event`)* | the full journaled type, `webhook.<provider>.<kind>` | `InboundWebhookAutomationBridge` | trusted (server-derived) |
| `{{webhook.provider_event_id}}` *(design name: `webhook.id`)* | the provider event id | `InboundWebhookAutomationBridge` | trusted (server-derived) |
| `{{payload.<field>}}` | any flattened body field (e.g. `{{payload.from_name}}`, `{{payload.amount}}`, `{{payload.message}}`) | `InboundWebhookAutomationBridge` (rebuilds the bag from the journaled flattened payload; adapter `Parse` flattens nested JSON to dot-path keys, arrays as `.0`, `.1`, …) | **attacker-authored** (taint enforcement not built — §7.1) |

Unknown token ⇒ empty string (uniform §6.3 rule).

### 7.1 `payload.*` is untrusted — a taint boundary, not just a namespace (design intent — NOT BUILT)

> **As built: none of the three controls below exists.** `payload.*` is merged flat into the same variables bag as trusted tokens; there is no `TaintedVariables` sub-bag, no fail-closed check on sensitive parameters, no `tainted_payload_in_sensitive_param` validator rule and no author warning. The only flatten-time bound is a **recursion-depth cap of 32** (`WebhookAdapterHelpers.MaxFlattenDepth`; deeper containers are recorded as their serialized JSON) — there is no 2 KiB/field or 64 KiB/bag cap and no control-character stripping. Whether to build the taint boundary is an **open owner question**. The text below is the intended design, kept as the target.

`payload.*` is the **external caller's raw JSON body**. Seeding it flat into the *same* `Variables` dictionary that also holds platform-resolved trusted tokens (`user.*`, `channel.*`) would let one verified inbound POST steer a security-sensitive action — `{{payload.username}}` in a `ban`/`timeout` `UserRef`, `{{payload.target}}` in `shoutout`, `{{payload.url_path}}` in a path/URL parameter, or an endpoint selector — handing the external sender direct control of the action's *target*. (Verification proves the body came from someone who knows the secret; it proves **nothing** about the *values* in it. `ICommandConfigValidator` guards config **keys** at save time, never runtime **values**.) The single-pass `[GeneratedRegex]` resolver already blocks second-order injection — it does not re-scan substituted values — but value-as-target escalation is not closed by that. So:

1. **Quarantined sub-bag.** The dispatcher seeds `payload.*` into a **distinct tainted sub-bag** (`ActionContext.TaintedVariables`, see `commands-pipelines.md` §4.4) — *not* merged into `Variables`. The resolver renders `{{payload.<field>}}` from the tainted bag for **display-only** sinks (`send_message`/`send_reply`/`set_variable` output, outbound `BodyTemplate`) but the engine **fails-closed** if a tainted token resolves inside a **security-sensitive action parameter**: `ban`/`timeout` `UserRef`, `shoutout` `TargetChannel`, and `send_webhook` endpoint selection (the design also listed `http_request` `Fqdn`/`Path`/`Method`; **no `http_request` action exists**, so that item is void). A pipeline that pipes `payload.*` into any of those is rejected at **compile/validate** (`ICommandConfigValidator` flags a `webhook`-triggered pipeline whose sensitive param references a `payload.*` token → `Result.Failure("tainted_payload_in_sensitive_param")`); if it ever reaches runtime it is a `denied` execution, never executed.
2. **Hard caps + sanitization at flatten time.** Each flattened `payload.<field>` value is **length-capped** (default **2 KiB/field**, total **64 KiB/bag**, over → field dropped) and **control-char-stripped** (no CR/LF/NUL — blocks chat-command/log injection) by the adapter `Parse` before it ever enters the bag.
3. **Author warning.** The save-time validator surfaces a **warning** (not just the hard fail in #1) whenever a `webhook`-triggered pipeline references `payload.*` anywhere, so the author is told the namespace is attacker-authored.

**Outbound** body/header templates are the *author's* `BodyTemplate`/`CustomHeaders`, rendered over the standard seeded bag (the triggering event's variables) — no new outbound namespace. **As built, the body is rendered by `IWebhookBodyTemplateRenderer.Render(template, variables, bodyIsJson)`** (`WebhookBodyTemplateRenderer`; JSON templates use JSON-safe leaf substitution, non-JSON templates the plain-text path, and the rendered string is stored on the delivery as `RenderedBody`). Body templates are validated at save time against the `TemplateHelperContext.Webhook` key set (an unknown `{{key}}` is rejected) and, when `BodyIsJson`, must parse as JSON. **`CustomHeaders` values are stored and sent verbatim — they are not templated as built.** (When the triggering event is itself an inbound webhook, `payload.*` in an outbound body is rendered display-only from the tainted bag, after the §7.1 caps — egress of attacker text is acceptable; egress *targeting* is governed by the H.7 allowlist on the endpoint, never by `payload.*`.)

---

## 8. Security model

> **As built vs design.** Rows marked *(design intent)* below describe controls the code does not implement today: the `IRateLimiterPartitionStore` two-tier limiter, `IIdempotencyGuard` retention floors, `KeyUsageBinding` rotation, the header/body **two-policy split** and the `AllowRequestBody` / `MaxRequestBytes` byte clamps, and reserved-header rejection on `CustomHeaders` (as built, `CustomHeaders` are stored and sent verbatim, appended after the `webhook-*` headers). What is built: token + per-adapter signature verification, `FixedTimeEquals` comparisons, the 10-minute generic-adapter replay tolerance, the endpoint-salted journal dedup, the `256 KiB` inbound cap and content-type allowlist, `ITokenProtector` sealing of both secret kinds, Standard Webhooks outbound signing with rotation overlap, the shared `egress-allowlisted` client (resolve-then-pin, https-only, no redirects, blocked-range guard), the lifecycle deny-list, and endpoint auto-disable at 20 failures.

| Concern | Control (reused contract in **bold**) |
|---------|---------------------------------------|
| **Inbound auth** | Opaque per-endpoint `Token` (64 url-safe chars, `RandomNumberGenerator`, **OverlayToken model**), `[AllowAnonymous]`, validated at request, **scrubbed from logs**. Plus the per-adapter signature — token alone is never sufficient for HMAC adapters. |
| **Inbound verification** | Per-adapter (`IInboundWebhookAdapter.Verify`): the `supporter` adapter verifies per `SourceKey` (Patreon `X-Patreon-Signature` HMAC-MD5, Fourthwall/Shopify HMAC-SHA256, Ko-fi `verification_token` body compare via FixedTimeEquals), GitHub `X-Hub-Signature-256` HMAC, generic configurable. In-box **`HMACSHA256` + `CryptographicOperations.FixedTimeEquals`** (mirrors `twitch-eventsub.md` §3.6). Invalid → `4xx`, **never processed**. |
| **Inbound replay** | *(as built: the 10-minute tolerance is `GenericInboundWebhookAdapter.ReplayTolerance`, a stale timestamp surfaces as `InvalidSignature`; dedup is the journal row — no `ExpiresAt` and no `IIdempotencyGuard`, §3.2 step 4b. The floors below are design intent.)* **Per-adapter, with a dedup-retention SECURITY FLOOR (§3.2 step 4, §11 #1).** Generic adapter: required `webhook-timestamp` + **10-min tolerance** (matching in-house EventSub) is the primary guard; dedup is the backstop (24h `ExpiresAt`). Ko-fi / GitHub have **no timestamp** → the **dedup row is the SOLE replay barrier**, so its `ExpiresAt` is pinned to a **30-day fixed floor** (not a perf knob — the pruner must never shorten it). Dedup via **`IIdempotencyGuard`** O.4 (`Scope="webhook:in:{endpointId}"`, `ExpiresAt` per the floor above). No-HMAC adapters (kofi / generic-shared-secret-in-body) key the claim on `SHA-256(rawBody)+":"+ProviderEventId` (server-observed body hash) so a chosen-`ProviderEventId` pre-claim cannot shadow a distinct legit event. *(As built the same body-hash key shape folds into the journal `EventId`, and applies to every adapter except `Github` and HMAC-mode `Generic`, §3.2.1.)* |
| **Inbound DoS hardening** | *(as built: `[EnableRateLimiting(Anonymous)]` on the controller; the two-tier `IRateLimiterPartitionStore` below is design intent, §5.2)* Size cap → `413`; method allowlist → `405`; content-type → `415`; rate limit → `429` via **`IRateLimiterPartitionStore`** **two tiers** — **pre-resolution** (`wh:in:ip:{clientIpHash}`, default 60/min/IP, + optional `wh:in:rawtok:{sha256(token)}`) runs **before** the DB token lookup to throttle unknown-token floods/guessing, then **post-resolution** (`wh:in:{endpointId}` + `wh:in:tenant:{broadcasterId}`). The `UnknownEndpoint` 404 path emits **no** bus event (no unauthenticated amplification). Generic errors (no internal leak); deny-by-default. |
| **Outbound SSRF** | *(as built: the shared core in `Sandbox/EgressHttpClient.cs` + `EgressAddressGuard.cs` — https-only via `EgressSchemeHandler`, no redirects, resolve-then-pin over `resolved[0]`, blocked ranges `0/8`, `127/8`, `10/8`, `172.16/12`, `192.168/16`, `169.254/16`, `100.64/10`, broadcast, `::1`, `::`, `fc00::/7`, `fe80::/10`, IPv4-mapped IPv6 normalized first; **multicast is not blocked — S-EGRESS-GUARD-MULTICAST**. The FQDN allowlist match is done by `OutboundWebhookEndpointService.CreateAsync` and re-checked by `OutboundWebhookTargetUrl.TryBuild` at send time, not inside the client. The `MaxResponseBytes` response cap and the header/body policy split described below are design intent.)* **Same SSRF *core*, different (server-controlled) header+body policy — NOT a verbatim reuse.** The IP-pin/allowlist/redirect/metadata-block controls are reused **unchanged** from `code-execution-sandbox.md` §7 (the binding requirement): every outbound delivery goes through the **same `egress-allowlisted` `SocketsHttpHandler.ConnectCallback`** (resolve-then-pin to the validated IP, TLS `TargetHost`=FQDN), `https`-only, FQDN must match an enabled **`HttpEgressAllowlist`** (H.7) row, reject `127/8`,`10/8`,`172.16/12`,`192.168/16`,**full `169.254/16`** (+`169.254.169.254`),`100.64/10`,`0.0.0.0/8`,`::1`,`fc00::/7` (ULA),`fe80::/10` (link-local),`::ffff:0:0/96`,multicast; **redirects disabled**; response capped (`MaxResponseBytes`). **But the sandbox front-end's *guest* header/body clamp must NOT be reused as-is** — it strips down to `Accept`/`Content-Type` and gates bodies behind `AllowRequestBody` (default off), which would silently drop the Standard-Webhooks `webhook-id`/`webhook-timestamp`/`webhook-signature` headers (receiver rejects every delivery as unsigned → auto-disable) and the JSON body. Instead the **trusted webhook dispatcher injects** the signature/`webhook-*` headers, the author `CustomHeaders`, and the rendered body **server-side** (this is a first-party signed sender, not an untrusted guest), and the H.8 endpoint's H.7 row is created with **`AllowRequestBody=true`** and a webhook-sized **`MaxRequestBytes` = the 256 KiB inbound cap** (not the few-KiB sandbox default). See §3.6 + §8.1. H.8 still points at the H.7 row for the egress boundary; only the per-caller header/body **policy object** differs. |
| **Outbound signing** | Standard Webhooks: `webhook-id` / `webhook-timestamp` (unix s) / `webhook-signature` = `v1,<base64 HMAC-SHA256(secret, "<id>.<timestamp>.<payload>")>`, space-delimited multi-sig during rotation. In-box **`HMACSHA256`**; **no 3rd-party**. |
| **Secret storage** | *(as built: one sealed-envelope column per secret via `ITokenProtector`, context `(broadcasterId, "webhook:in"\|"webhook:out", endpointId)`, unsealed per request with `TryUnprotectAsync`; no `KeyUsageBinding`; §3.1/§3.5)* `whsec_` (outbound) + verification secret (inbound) stored as AEAD ciphertext via **`ISubjectKeyService.ProtectAsync`** / **`IFieldCipher`** (AES-256-GCM) — never plaintext. Design binding (§3.1/§3.5): `cryptoKeyId` from `GetOrCreateTenantKeyAsync(broadcasterId)`; **`CipherAad(TenantId=broadcasterId, Provider="webhook:in"|"webhook:out", TokenType=endpointId, KeyVersion)`** identical on encrypt/decrypt (4-field record, not a `‖` string); `resourceTable`/`resourceColumn` = the table + each cipher column (incl. `SecondarySigningSecretCipher`) so `RotateKeyAsync` re-encrypts every secret column via `KeyUsageBinding`. Rotation = new `whsec_`, overlap-valid (multi-sig). Crypto-shred via **`DestroyKeyAsync`** (mirrors `IntegrationTokens` envelope; `AppSetting.SecureValueCipher` for any global-secret case). |
| **Outbound delivery** | At-least-once (receivers dedupe; we send the `webhook-id`), retry exp backoff+jitter, **auto-disable after 20 consecutive failures** (dead-letters the in-flight delivery). Worker under **`IRunOnceGuard`** (`"webhook-delivery-drain"`, no double-deliver multi-instance). *(Design intent, not built: `IIdempotencyGuard` (`Scope="webhook:out"`) so the same event never double-enqueues, and dead-letter after max attempts — §3.6, §3.7, open owner questions.)* |
| **Tenant isolation** | Every row carries `BroadcasterId`; management endpoints gate per-action via `IActionAuthorizationService.AuthorizeActionAsync(userId, channelId, actionKey)` (Gate-2) after `[Authorize]` + tenant resolution (Gate-1). Ingest resolves tenant **from the endpoint row**, never from the request body. |
| **Exposure** | SaaS `managed_edge` (public). Self-host `opt_in_tunnel` (operator opts in via reverse-proxy/tunnel; default-deny). |

### 8.1 Outbound egress: shared SSRF core, per-caller header/body policy (no second client) — design intent for the policy split

> **As built:** the shared core exists (`EgressHttpClient`, one named client used by the sandbox, outbound webhooks, link previews and custom-data fetches). The **per-caller policy object does not**: the client has no header allowlist and no request-body clamp, `HttpEgressAllowlist.AllowRequestBody` / `MaxRequestBytes` are columns that nothing reads, and the trusted-webhook path simply builds its own `HttpRequestMessage`. The two-policy split and the byte clamps are **design intent**, not code; the architecture test the next bullet describes is not built either.

The fix for "the reused sandbox client strips exactly what webhooks must send" is to **factor the egress policy**, not to stand up a parallel client (a parallel un-validated client is precisely the SSRF regression the lens hunts for):

- **Shared, unchanged:** the `egress-allowlisted` `SocketsHttpHandler` + `ConnectCallback` IP-pin, FQDN allowlist match, resolved-IP re-validation (metadata/internal/link-local blocks), no-redirects, response cap. Owned by `code-execution-sandbox.md` §7; the webhook path goes through the **same handler instance** — SSRF cannot be re-opened. An **architecture test asserts the webhook dispatcher's `HttpClient` resolves the `egress-allowlisted` named client and that its outbound requests traverse that same `ConnectCallback`** (so no one can quietly add a second egress path).
- **Per-caller policy object (the only difference):** the header-allowlist + request-body clamp are a **policy parameter**, not hard-coded into the handler. Two policies exist: the **sandbox/guest** policy (header set = `Accept`/`Content-Type`, body gated by `AllowRequestBody`, default off — for the untrusted `http.fetch` guest — there is no `http_request` pipeline action) and the **trusted-webhook** policy (the dispatcher attaches the `webhook-id`/`webhook-timestamp`/`webhook-signature` + author `CustomHeaders` + rendered body server-side; the H.7 row is provisioned `AllowRequestBody=true`, `MaxRequestBytes=256 KiB`). The guest can still never reach this policy — it is selected by the **caller** (trusted dispatcher vs guest front-end), server-side, never by anything crossing the sandbox boundary.

This keeps one SSRF owner and one connect path while letting the trusted first-party sender transmit signed headers + a webhook-sized body.

---

## 9. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). **As built, most registrations are by assembly scan or naming convention; only the items marked explicit are written out.** Lifetimes: row-touching use-case services **Scoped**; pure verifier/signer **Singleton**; adapters **Transient** (stateless, multi-registered); the drain worker **Hosted**.

```csharp
// Endpoint services — auto-registered by the "*Service" naming convention (Scoped):
//   IInboundWebhookEndpointService -> InboundWebhookEndpointService
//   IOutboundWebhookEndpointService -> OutboundWebhookEndpointService

// Pure crypto primitives (explicit, Singleton, in-box HMACSHA256)
services.AddSingleton<IInboundSignatureVerifier, InboundSignatureVerifier>();
services.AddSingleton<IOutboundWebhookSigner, OutboundWebhookSigner>();

// Per-provider inbound adapters — assembly scan, Transient, one class per WebhookAdapterKind (seven):
//   Kofi, Github, Generic, Fourthwall, Shopify, Patreon, Buymeacoffee
services.AddImplementationsOf<IInboundWebhookAdapter>(infrastructure, ServiceLifetime.Transient);

// Core ingest + egress paths (explicit, Scoped)
services.AddScoped<IInboundWebhookDispatcher, InboundWebhookDispatcher>();
services.AddScoped<IOutboundWebhookDispatcher, OutboundWebhookDispatcher>();

// Body renderer (explicit, Singleton)
services.AddSingleton<IWebhookBodyTemplateRenderer, WebhookBodyTemplateRenderer>();

// Pipeline action — picked up by the ICommandAction scan (no manual line for SendWebhookAction).
// Event handlers — picked up by the IEventHandler<> scan: InboundWebhookAutomationBridge, SupporterWebhookBridge.
// Fan-out — picked up by the IJournalPostCommitHook scan: OutboundWebhookFanoutHandler.

// Retry/dead-letter drain (explicit)
services.AddScoped<WebhookRetryProcessor>();
services.AddHostedService<WebhookDeliveryWorker>();          // PeriodicTimer 30 s; IRunOnceGuard lease "webhook-delivery-drain"

// egress-allowlisted named HttpClient — one registration shared with the sandbox (Sandbox/EgressHttpClient + EgressSchemeHandler).
```

The `egress-allowlisted` client, `IEventJournal`, `ITokenProtector`, `ISubjectKeyService`, `IRunOnceGuard`, `IPipelineEngine` and `IEventResponseExecutor` are owned by their own subsystems and reused, not re-registered here.

> **As built (`OutboundWebhookFanoutHandler`, `IJournalPostCommitHook`):** the hook fast-skips when the tenant has no enabled endpoint, then flattens the committed event's **top-level** payload properties into `event.<snake_case>` variables (plus `event.id`, `event.type`, `event.occurred_at`, `event.broadcaster_id`) — no depth/size bound — and runs `EnqueueForEventAsync` **detached** on a fresh DI scope (`Task.Run`, `CancellationToken.None`, fire-and-forget; the most recent task is kept on an internal `LastDispatch` test seam). Per-endpoint event-type filtering (`Subscribes`, with `*` = all catalogue events) happens in the dispatcher. **The self-amplification guard is the lifecycle deny-list only:** `OutboundWebhookEventCatalogue.LifecycleDenyList` (the five webhook lifecycle event class names) is excluded from the catalogue, `*` never matches it, and an explicit subscription to one is rejected at save with **`VALIDATION_FAILED`** (not `event_type_not_subscribable`); an unknown type outside the catalogue is rejected with the same code. **The `CausationId` cycle guard is NOT built** — nothing stamps `CausationId` on outbound-triggered emissions or refuses to fan out a chain already containing a send for the same endpoint; the deny-list is the only barrier (defense in depth is missing).
>
> **`OutboundWebhookFanoutHandler`.** Outbound is triggered two ways: (a) explicitly by the `send_webhook` pipeline action (§6), and (b) declaratively by subscribing an endpoint to event types. For (b), a thin observer is invoked **after** each event is journaled and calls `EnqueueForEventAsync` for every event whose `EventType` an enabled endpoint lists in `SubscribedEventTypes`. The hook signature is `OnCommittedAsync(EventRecord)`; `EventRecord` carries `EventType` + `BroadcasterId` + `PayloadJson` (the `[VC:JSON]`-serialized payload) but **not** a flattened bag — so the handler **deserializes `PayloadJson` with Newtonsoft (schema §1.4) and flattens it into the `IReadOnlyDictionary<string,string>` variable bag** for `EnqueueForEventAsync(broadcasterId, eventType, variables, journalEventId)`. This payload is a **trusted, server-generated** journaled domain event (not external input), so the §7.1 inbound `payload.*` taint caps do **not** apply to it; only a flatten depth/size bound (mirroring §7.1's 2 KiB/field · 64 KiB/bag) guards against a pathological event.
>
> **Wiring (resolves the "hook that doesn't exist" gap).** The previous "piggyback the `JournalingEventBusDecorator` post-journal hook keyed on `EventType`" route assumed a seam the decorator never exposed (it only does `capture → delegate`; and a generic `IEventHandler<IDomainEvent>` is impossible because handlers resolve per concrete `TEvent` — `event-store.md` §3.2). So this subsystem **requires `event-store.md` to expose a real post-commit observer seam, `IJournalPostCommitHook`** (added there in §3.2/§7): the decorator, after `CaptureAsync` commits the journal row, invokes every registered `IJournalPostCommitHook.OnCommittedAsync(EventRecord)` — one wiring point, EventType-agnostic at the seam, the handler filters by `SubscribedEventTypes`. `OutboundWebhookFanoutHandler` implements it. (No per-`TEvent` fallback — that route is also unimplementable as a single registration for the same per-concrete-handler reason; the seam is the binding answer.) Without this seam, declarative `SubscribedEventTypes` endpoints would never fire — only the `send_webhook` action would.
>
> **Self-amplification guard (binding — must not loop).** The fan-out match set is **business/domain events only**. The webhook subsystem's own lifecycle events (`OutboundWebhookEnqueuedEvent`, `OutboundWebhookAttemptedEvent`, `OutboundWebhookAutoDisabledEvent`, `InboundWebhookReceivedEvent`, `InboundWebhookRejectedEvent`) are on a **hard deny-list**: `'*'` means "all **subscribable business** events" and **never** matches a webhook-lifecycle type, and an explicit subscription to one of those types is rejected at endpoint save (`Result.Failure("event_type_not_subscribable")`). Reason: an endpoint subscribed to `'*'` (or to a lifecycle type) would have each delivery emit `OutboundWebhookEnqueuedEvent`/`OutboundWebhookAttemptedEvent`, which the post-commit hook would re-match and re-enqueue — an unbounded cascade the outbound idempotency guard cannot break (every hop mints a fresh `webhook-id`, so `TryClaimAsync` is always `IsFirst=true`). In addition the fan-out stamps **`CausationId`** on every outbound-triggered emission and **refuses to fan out** an event whose causation chain already contains an outbound-webhook send for the **same endpoint** (defense in depth against any other cycle). Test (§10): an endpoint subscribed to `'*'` that fires once produces **exactly one** delivery, not a growing cascade.

EF configurations (`NomNomzBot.Infrastructure/Platform/Persistence/Configurations/`, flat): `OutboundWebhookEndpointConfiguration`, `OutboundWebhookDeliveryConfiguration`, `InboundWebhookEndpointConfiguration`. As built, `SubscribedEventTypesJson`, `CustomHeadersJson` and `GenericConfigJson` are plain `text` columns whose (de)serialization the services own with Newtonsoft — there are no `ValueConverter`s. (`OutboundWebhookEndpoint.BodyIsJson` uses `HasDefaultValue(true)`.) New `IApplicationDbContext` `DbSet`s: `OutboundWebhookEndpoints`, `OutboundWebhookDeliveries`, `InboundWebhookEndpoints`.

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
- **Validation** — service-layer checks returning `Result<T>` (`VALIDATION_FAILED`, `EGRESS_NOT_ALLOWED`, `INVALID_PATH`, `NOT_FOUND`, `ENDPOINT_DISABLED`); the `AddValidation()` source-generator remark is design text.
- **Testing** — xunit.v3, NSubstitute, AwesomeAssertions; SQLite in-memory for service tests; Testcontainers Postgres for the RLS-isolation + retry-drain concurrency subset. Tests prove **behavior**:
  - signature verify accepts a known-good vector and rejects a one-byte-flipped body;
  - a duplicate `ProviderEventId` on **one** endpoint produces exactly one journal row + one fan-out;
  - **two tenants ingesting the identical `ProviderEventId` produce two distinct journal rows + two independent fan-outs** (the endpoint-salted `WebhookEventId` §3.2.1 — guards the cross-tenant collision);
  - a forged no-HMAC (Ko-fi) request that pre-claims a genuine future `ProviderEventId` does **not** suppress the later genuine event (different `rawBody` → different dedup key, §3.2 step 4);
  - an over-cap body returns `413` with no journal row;
  - **unknown-token flood is throttled by the pre-resolution IP limiter (`429`) and emits no `InboundWebhookRejectedEvent`** (§5.2 step 0/8);
  - **a `webhook`-triggered pipeline that references `{{payload.*}}` in a `ban`/`timeout`/`shoutout`/`send_webhook` sensitive param is rejected at validate (`tainted_payload_in_sensitive_param`)** and never executes (§7.1);
  - **an outbound endpoint subscribed to `'*'` that fires once produces exactly one delivery, not a cascade** (lifecycle deny-list + causation guard, §9);
  - **the webhook delivery path traverses the same `egress-allowlisted` `ConnectCallback`** (architecture test, §8.1) while carrying the signature headers + body (a delivery with stripped `webhook-signature` would be the regression);
  - an outbound delivery row records the triggering `EventType` + `JournalEventId` (from `ActionContext`, §6/§4.4);
  - an outbound non-2xx schedules a `NextRetryAt` and the 20th consecutive failure flips `DisabledAt` + emits `OutboundWebhookAutoDisabledEvent`;
  - a rotated secret signs with two `v1,` signatures during overlap.

---

## 11. Decisions (resolved)

The numeric/policy forks below were **decided at design time**. **Status against the code:** #1 (10-min generic tolerance) is built; the 30 d / 24 h dedup floors are not (dedup is the journal row, §3.2 step 4b); #2 (5 attempts / 7-day outbound idempotency) is **not built** — see §3.7 (retry cap: open owner question); #3 (auto-disable at 20) is built; #4 (256 KiB inbound) is built, the outbound `MaxRequestBytes` clamp is not (§8.1); #5's adapter set has grown to seven kinds and the Streamlabs adapter was **never built** (no Streamlabs adapter exists in the code; the "Streamlabs ships on this seam" claim is dropped).

1. **Inbound replay — 10-min timestamp tolerance + a dedup-retention SECURITY FLOOR.** Inbound replay is guarded two ways, and **both** numbers are fixed so the call site builds and the floor is not tuned away:
   - **Timestamp tolerance is 10 min** (generic adapter, which **requires** a `webhook-timestamp`), matching the in-house EventSub verifier (`twitch-eventsub.md` §3.6) — not Stripe's non-normative 5 min.
   - **Dedup retention `ExpiresAt` (`WebhookReplayRetention`) is the replay boundary — a security floor, not a perf knob:** **30 days** for **Ko-fi and GitHub** (no timestamp → the O.4 dedup row is the *sole* replay barrier, so a long fixed retention is what stops a captured still-valid request from re-firing after expiry; the pruner MUST NOT shorten it below this floor), and **24h** for the **generic** adapter (its required timestamp + 10-min tolerance is the primary guard, so the dedup row only needs to survive a provider redelivery storm). For no-HMAC adapters the claim key folds in `SHA-256(rawBody)` (§3.2 step 4) so a chosen-`ProviderEventId` pre-claim cannot shadow a distinct legit event. Every `IIdempotencyGuard.TryClaimAsync` call passes this as `IdempotencyClaimRequest.ExpiresAt` (the contract requires it).

2. **Outbound retry — 5 attempts over ~1 h; outbound idempotency `ExpiresAt` is 7 days.** The outbound retry schedule is **5 attempts** with exponential backoff + jitter over roughly one hour (~1 min, ~5 min, ~15 min, ~30 min, ~60 min, ± jitter), then **dead-letter**. Stripe's exact schedule/jitter is unpublished, so these are the project's numbers, fixed in config. The **`webhook:out` idempotency `ExpiresAt` (`WebhookOutboundIdempotencyRetention`) is 7 days** — comfortably longer than the full retry-to-dead-letter window so the same event never double-enqueues across the retry lifecycle, and bounded so O.4 does not grow unbounded. Passed as `IdempotencyClaimRequest.ExpiresAt` in every `EnqueueFor*Async` claim.

3. **Outbound auto-disable — 20 consecutive failures.** An outbound endpoint **auto-disables after 20 consecutive failures** (`ConsecutiveFailureCount`): set `DisabledAt`, emit `OutboundWebhookAutoDisabledEvent`; a successful delivery resets the counter. The operator re-enables via `ReenableAsync`.

4. **Body size caps — 256 KiB inbound; H.7 `MaxRequestBytes` outbound.** The inbound body size cap is **256 KiB** (`413` over cap, before full buffering). The outbound request-body cap is the per-row H.7 `MaxRequestBytes` (default 8192, **reject not truncate**); a streamer raises it per endpoint as needed.

5. **Inbound adapter set — seven kinds (was: Ko-fi, GitHub, Generic/Standard-Webhooks).** The original set was **Ko-fi, GitHub, and Generic/Standard-Webhooks** (Generic covers Zapier/IFTTT/Make/Stream Deck/custom). The adapter set has since grown to seven kinds (Ko-fi, GitHub, Generic, Fourthwall, Shopify, Patreon, Buy Me a Coffee) — each one class on the same `IInboundWebhookAdapter` seam. StreamElements is an **integration** (WebSocket + OAuth), **not** a webhook, and belongs to the integrations subsystem — a dependency boundary, not this subsystem's surface.

> Settled foundations (restated for clarity): inbound addressing is the per-channel opaque-token URL `/api/v1/webhooks/in/{token}` (OverlayToken model); outbound signing is Standard Webhooks (`whsec_`, `v1,`, multi-sig rotation); inbound becomes a `Source="webhook"` journal event with a deterministic `EventId` = **`WebhookEventId(broadcasterId, endpointId, ProviderEventId)`** (endpoint+tenant-salted UUIDv5, §3.2.1 — never the bare provider id); the dispatcher owns token resolution (controller does the pre-resolution limiter + size/method/content guards only); `payload.*` is a quarantined **tainted** namespace barred from security-sensitive action params (§7.1); webhook-lifecycle events are deny-listed from outbound fan-out (no self-amplification, §9); `TriggerKind=webhook` is added to `commands-pipelines.md` and runs under the `WebhookSystemActor` non-user contract; secrets are AEAD-enveloped with the explicit `CipherAad`/`KeyUsageBinding` mapping (§3.1/§3.5); SSRF is the reused H.7 + `egress-allowlisted` `ConnectCallback` core under a trusted-webhook header/body policy (§8.1).
