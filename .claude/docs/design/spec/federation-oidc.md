# Federation + OIDC — Interface Specification

**Status:** Implementable. Code from this directly.
**As-built (2026-09-30):** built — the peer directory service + `FederationController` peer routes, the per-message signer (`rsa-sha256`) + its config-backed signing key provider, the per-channel opt-in service + `ChannelFederationController`, the inbound translator + handler registry, and the transport-independent **inbound gateway** (`IFederationInboundGateway`, §3.5). **Not built:** `IFederationHandshakeService` (§3.2), `IRemoteEventBus` outbound/subscribe legs and both transport adapters (§3.5), the `/federation/handshake`, `/federation/inbound` and `/.well-known/federation-descriptor` routes (§5), the OpenIddict issuer, the OIDC client scheme registrar and the outbound dispatcher (§7). Unbuilt parts stay as the target design.
**Subsystem:** NomNomzBot as OIDC issuer + client (OpenIddict, feature-gated), cross-instance trust directory, mTLS + JWKS-signed tokens + per-message signatures, remote event-bus adapter (inbound/outbound queue). **AuthN federates; AuthZ stays local.**

## Grounding & locked decisions (binding)

- **AuthN federates, AuthZ stays local.** A peer instance can *authenticate* a subject (signed token / SSO) and *propagate signed events* (bans, trust, savings), but **every authorization decision is made locally** by this instance's permission resolver (Plane A/B ladders, `!permit` grants, floors). A federated event never carries an authorization verdict — it carries a *claim* (this subject did X on peer P), and the local channel's opt-in + standing decides whether to act. No remote principal is ever a rung on the local management/community ladder.
- **OIDC issuer is FEATURE-GATED.** Stand up the OpenIddict authorize/token/JWKS surface **only** when `federation` or `multi_user_sso` feature flags are enabled. Basic single-user self-host stays JWT-only resource-server: no issuer, no `/connect/*` endpoints. The DI issuer branch is not registered unless the gate is on.
- **Asymmetric signing is a hard prerequisite (dependency).** Token issuance on RS256/ES256 with a published JWKS (as built, the repo's token code uses `JwtSecurityTokenHandler` from `System.IdentityModel.Tokens.Jwt`; see §8) is a dependency of federation: federation MUST NOT start before it is in place. HS256 is unfederatable. The JWKS this subsystem publishes/consumes is the issuer's RS256/ES256 key set.
- **Peer event signatures use `rsa-sha256`** (stack doc, Auth decision) — in-box `System.Security.Cryptography.RSA`, **zero third-party crypto**. `FederationPeerKeys.Algorithm` allows `ed25519` for forward-compat, but this instance signs and verifies with `rsa-sha256` only (no NSec/BouncyCastle). mTLS transport uses native Kestrel/`HttpClient` client certificates.
- **Federation tables are GLOBAL** (schema §1.2): `FederationPeers`, `FederationPeerKeys` carry **no** `BroadcasterId` and do **not** implement `ITenantScoped`. `ChannelFederationOptIns` **is** tenant-scoped (per-channel opt-in). Cross-instance propagation rows are guarded by the `ChannelFederationOptIns` + `FederationPeers.TrustState` predicate, never single-tenant RLS.
- **Default-deny / opt-in** (memory: opt-in/default-deny): a peer is `pending` until explicitly trusted; a channel shares/accepts nothing until a `ChannelFederationOptIns` row enables it. Opt-in changes are gated at `LeadModerator` (the seeded `federation:optin:*` floor); trust changes are platform-IAM (`iam:manage`).
- **`BroadcasterId` is `Guid`** (schema §1.1, ITenantScoped widened `string`→`Guid`). All new federation types use `Guid` for tenant + FK keys. Surrogate PKs are `Guid.CreateVersion7()`. Twitch/peer external ids are indexed `string` attribute columns.
- **Crypto-shred reuse:** federation never introduces a new secret store. Peer PII in propagated events follows the existing `EventJournal` + `EventSubjectKeys` + `CryptoKey` crypto-shred path (schema O.1/O.1a/Q.1). Inbound peer events are recorded in the **same** `EventJournal` with `Source=federation` — the locked `EventJournal.Source` enum (O.1) is extended to include `federation`, so this is a first-class enum member, not an out-of-band value.

---

## 1. Entities (locked-schema tables this subsystem owns)

Defined authoritatively in `2026-06-16-database-schema.md`. This subsystem **owns** the Domain-D federation tables and the federation-relevant columns of `ModerationActions`; it **reads** the global IAM/EventJournal/CryptoKey tables. Do not redefine — reference.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| `FederationPeers` | D.1 | GLOBAL, soft-delete | `Id guid PK`; `InstanceId string(36) Unique`; `DisplayName string(100)?`; `BaseUrl string(2048)?`; `DeploymentMode string(20)` [VC:enum `saas`\|`self_host_lite`\|`self_host_full`]; `TrustState string(20) Index` [VC:enum `pending`\|`trusted`\|`revoked`\|`blocked`]; `FirstSeenAt timestamp`; `LastHandshakeAt timestamp?`; `CreatedAt/UpdatedAt/DeletedAt` |
| `FederationPeerKeys` | D.2 | GLOBAL | `Id guid PK`; `PeerId guid FK→FederationPeers Index`; `PublicKey text` (PEM/base64); `Algorithm string(30)` (`ed25519`\|`rsa-sha256`); `KeyId string(64) Index`; `ValidFrom timestamp`; `ValidTo timestamp?`; `IsActive bool Index`; `CreatedAt/UpdatedAt`. **Unique** `(PeerId, KeyId)` |
| `ChannelFederationOptIns` | D.3 | tenant (`ITenantScoped`), soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `PeerId guid FK→FederationPeers Null Index` (null = any trusted peer); `OptInType string(30) Index` [VC:enum `shared_chat_bans`\|`shared_ban_list`\|`shared_trust_list`\|`shared_savings`]; `Direction string(10)` [VC:enum `accept`\|`share`\|`both`]; `IsEnabled bool Index`; `EnabledByUserId guid FK→Users Null`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, PeerId, OptInType)` |

**Read-only dependencies (owned by other subsystems, consumed here):**

| Table | Schema ref | Why this subsystem reads/writes it |
|---|---|---|
| `EventJournal` | O.1 | Inbound peer events are appended here with `Source=federation` (a member of the O.1 `Source` enum, which is extended to add `federation`); outbound events are read from here. `StreamPosition` via `TenantSequences`. |
| `EventSubjectKeys` | O.1a | Per-subject DEK link for multi-subject propagated events (raid, gift). |
| `CryptoKey` | Q.1 | DEKs encrypting peer-PII payload slices; crypto-shred linchpin. Not created by federation; reused. |
| `ModerationActions` | J.2 | **Written** when an inbound cross-instance federated ban is applied locally: `Origin=federation` (explicitly distinct from `Origin=shared_chat`, which denotes a Twitch-native shared-chat session ban), `OriginChannelId` set. Owned by the moderation subsystem; this subsystem only constructs the federation-origin action via that subsystem's service (`ISharedBanService.ApplyInboundSharedBanAsync`). |
| `EventJournal` (dedupe) | O.1 | **The inbound dedupe key is `EventJournal.EventId`** — the journal is unique on it, so a re-delivered envelope's `EventId` already being journaled IS the `replay` rejection (gate 4, §3.5). No `IdempotencyKey` row is written for inbound federation. |
| `FeatureFlag` / `FeatureFlagOverride` | P.13 | `federation`, `multi_user_sso` gates that decide whether the issuer + bus adapters are stood up. |
| `DeploymentProfile` | P.12 | `Mode`, `TokenVault`, `InstanceId` — drives the issuer/bus DI branch and this instance's own `InstanceId`. |

> **No new tables.** Every persistence need is met by the locked schema. Inbound/outbound queue durability rides on `EventJournal` + `ProjectionCheckpoint` (O.3, projection name `federation.outbound`), not a bespoke queue table.

---

## 2. Domain events

All inherit the canonical `DomainEventBase` (`NomNomzBot.Domain.Events`, platform-conventions §2.0 — provides `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt`), live in `NomNomzBot.Domain/Events/Federation/`, and are `sealed record`. Events do **not** redeclare `EventId`/`BroadcasterId`/`OccurredAt`; the publisher sets the inherited `BroadcasterId` to the affected local channel (`Guid.Empty` for directory-level events with no single tenant). Federation peer identity and any *referenced* channel/journal ids ride as **explicit `Guid` fields** on each record (e.g. `PeerId`, `JournalEventId`), distinct from the inherited base members.

```csharp
namespace NomNomzBot.Domain.Events.Federation;

/// Raised after a peer transitions to TrustState=trusted (handshake accepted). Drives bus subscription + JWKS prefetch.
public sealed record FederationPeerTrustedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required string InstanceId { get; init; }
    public required string DeploymentMode { get; init; }
    public string? BaseUrl { get; init; }
}

/// Raised after a peer is revoked or blocked. Drives bus unsubscription + key deactivation.
public sealed record FederationPeerRevokedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required string InstanceId { get; init; }
    public required string Reason { get; init; }      // "manual" | "key_compromise" | "blocklist"
    public required bool Blocked { get; init; }        // true => TrustState=blocked, false => revoked
}

/// Raised when a channel enables/disables a federation opt-in. Drives outbound-share eligibility + inbound-accept filter.
public sealed record ChannelFederationOptInChangedEvent : DomainEventBase
{
    public required Guid OptInBroadcasterId { get; init; }
    public Guid? PeerId { get; init; }                 // null = any trusted peer
    public required string OptInType { get; init; }    // shared_chat_bans | shared_ban_list | shared_trust_list | shared_savings
    public required string Direction { get; init; }    // accept | share | both
    public required bool IsEnabled { get; init; }
}

/// Raised after an inbound peer event passes signature + trust + opt-in + idempotency and is appended to EventJournal.
/// AuthZ note: this is a *claim*, not a verdict — local handlers decide whether to act.
public sealed record FederatedEventReceivedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required Guid JournalEventId { get; init; }   // EventJournal.EventId of the appended row (NOT this event's own inherited EventId)
    public required string FederatedEventType { get; init; } // e.g. "moderation.ban.shared"
    public Guid? TargetBroadcasterId { get; init; }      // local channel the claim targets (null = directory-level)
    public required long StreamPosition { get; init; }
}

/// Raised after an outbound event is signed and accepted by the transport for delivery to a peer.
public sealed record FederatedEventDispatchedEvent : DomainEventBase
{
    public required Guid PeerId { get; init; }
    public required Guid JournalEventId { get; init; }   // EventJournal.EventId of the dispatched row (NOT this event's own inherited EventId)
    public required string FederatedEventType { get; init; }
    public required string KeyId { get; init; }          // signing key version used
}

/// Raised when an inbound peer event is rejected (bad signature, untrusted peer, no opt-in, replay). Audit + alerting.
public sealed record FederatedEventRejectedEvent : DomainEventBase
{
    public Guid? PeerId { get; init; }                   // null if peer unknown
    public required string Reason { get; init; }         // "signature_invalid" | "algorithm_unsupported" | "peer_untrusted" | "no_opt_in" | "replay" | "schema_invalid" | "key_unknown"
    public required string FederatedEventType { get; init; }
    public string? PeerInstanceId { get; init; }
}
```

---

## 3. Service interfaces

All interfaces live in `NomNomzBot.Application.Contracts.Federation` (as built; the `NomNomzBot.Application.Services.Federation` namespace shown in the blocks below is the original design), async-all-the-way, return `Result`/`Result<T>` (`NomNomzBot.Application.Common.Models`). `PagedList<T>`/`PaginationParams` are the existing `Common.Models` types. Implementations in `NomNomzBot.Infrastructure/Federation/`. Repositories/`IUnitOfWork` only — no raw `DbContext`.

### 3.1 `IFederationPeerService` — trust directory CRUD + lifecycle

```csharp
namespace NomNomzBot.Application.Services.Federation;

public interface IFederationPeerService
{
    // Lists peers in the global directory (paged, optional TrustState filter). No tenant scope (global table).
    Task<Result<PagedList<FederationPeerDto>>> ListPeersAsync(
        PaginationParams pagination,
        string? trustStateFilter,
        CancellationToken cancellationToken = default);

    // Returns one peer with its active keys. Failure if not found.
    Task<Result<FederationPeerDto>> GetPeerAsync(
        Guid peerId,
        CancellationToken cancellationToken = default);

    // Registers a peer from a handshake/manual entry as TrustState=pending. Persists FederationPeers row + initial
    // FederationPeerKeys; emits no trust event (pending is not trusted). Idempotent on InstanceId (returns existing).
    Task<Result<FederationPeerDto>> RegisterPeerAsync(
        RegisterFederationPeerRequest request,
        CancellationToken cancellationToken = default);

    // Promotes a pending peer to TrustState=trusted; sets LastHandshakeAt; emits FederationPeerTrustedEvent.
    // Side effect: downstream bus adapter subscribes; JWKS is prefetched. No-op-success if already trusted.
    Task<Result<FederationPeerDto>> TrustPeerAsync(
        Guid peerId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Sets TrustState=revoked (reversible) or blocked (terminal); deactivates the peer's keys; emits
    // FederationPeerRevokedEvent. Side effect: bus adapter unsubscribes; in-flight inbound from this peer is dropped.
    Task<Result> RevokePeerAsync(
        Guid peerId,
        RevokeFederationPeerRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Adds a rotated public key (new KeyId) for a trusted peer; older key kept active until ValidTo. Unique (PeerId, KeyId).
    Task<Result<FederationPeerKeyDto>> AddPeerKeyAsync(
        Guid peerId,
        AddFederationPeerKeyRequest request,
        CancellationToken cancellationToken = default);

    // Marks a peer key IsActive=false (rotation retire / compromise). Verification of new inbound events stops using it.
    Task<Result> DeactivatePeerKeyAsync(
        Guid peerId,
        string keyId,
        CancellationToken cancellationToken = default);
}
```

### 3.2 `IFederationHandshakeService` — mTLS handshake + this-instance identity

> **Not built.** No handshake service, descriptor DTO or handshake route exists yet. Target design.

```csharp
namespace NomNomzBot.Application.Services.Federation;

public interface IFederationHandshakeService
{
    // Returns this instance's public federation descriptor (InstanceId, DeploymentMode, signing JWKS URL,
    // current rsa-sha256 public key + KeyId, BaseUrl). Served at the handshake endpoint; no secret material.
    Task<Result<FederationInstanceDescriptorDto>> GetLocalDescriptorAsync(
        CancellationToken cancellationToken = default);

    // Processes an inbound handshake from a peer presenting a valid client certificate (mTLS, validated by Kestrel
    // before this is called). Upserts the peer as pending with its descriptor + key; records the cert thumbprint.
    // Returns the local descriptor for the peer to store. Does NOT trust the peer (trust is a separate manual step).
    Task<Result<FederationInstanceDescriptorDto>> AcceptHandshakeAsync(
        FederationHandshakeRequest request,
        string clientCertThumbprint,
        CancellationToken cancellationToken = default);

    // Initiates an outbound handshake to a peer BaseUrl over mTLS (our client cert). Exchanges descriptors,
    // persists the peer as pending, prefetches the peer JWKS. Returns the peer descriptor.
    Task<Result<FederationInstanceDescriptorDto>> InitiateHandshakeAsync(
        string peerBaseUrl,
        CancellationToken cancellationToken = default);
}
```

### 3.3 `IFederationEventSigner` — per-message signature (rsa-sha256, in-box crypto)

```csharp
namespace NomNomzBot.Application.Services.Federation;

public interface IFederationEventSigner
{
    // Produces a detached rsa-sha256 signature over the canonical (sorted-key, UTF-8) JSON of the envelope payload,
    // using this instance's active private signing key. Returns the signature (base64) + the KeyId used.
    Task<Result<FederationSignature>> SignAsync(
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default);

    // Verifies a peer envelope's signature against the peer's active FederationPeerKeys (matched by KeyId).
    // Fails closed: unknown KeyId, inactive/expired key, or algorithm mismatch => failure. No DB write.
    Task<Result> VerifyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default);
}

// Value record (NomNomzBot.Application.Contracts.Federation), not a DB row.
public sealed record FederationSignature(string KeyId, string Algorithm, string SignatureBase64);

// As built: supplies this instance's active rsa-sha256 signing key (the issuer's RS256 private key material).
// Config-backed (a stateless IConfiguration reader, singleton) until the OIDC issuer key vault lands.
public interface IFederationSigningKeyProvider
{
    Result<FederationSigningKey> GetActiveSigningKey();   // failure when no key is configured
}

public sealed record FederationSigningKey(string KeyId, string PrivateKeyPem);
```

**Algorithm rule (`ed25519` forward-compat, fully decided).** This instance signs **and** verifies `rsa-sha256` only. A `FederationPeerKeys.Algorithm=ed25519` key MAY be *stored* (the column allows it for forward-compat) but is **not verifiable by this version**: `VerifyAsync` fails closed whenever the presented `FederationSignature.Algorithm` — or the matched key's `Algorithm` — is anything but `rsa-sha256` (surfaces as `FederatedEventRejectedEvent` reason `algorithm_unsupported`, distinct from a real `signature_invalid`). Because a peer whose only keys are `ed25519` produces signatures this version cannot check, **`TrustPeerAsync` requires the peer to hold at least one active `rsa-sha256` key** and fails with a clear error otherwise — a peer cannot be promoted to `trusted` until a usable `rsa-sha256` key is registered (`AddPeerKeyAsync`). Storing an `ed25519` key never widens what is accepted at verify time; it only pre-positions material for a future Ed25519-capable build.

### 3.4 `IFederationOptInService` — per-channel opt-in (tenant-scoped)

```csharp
namespace NomNomzBot.Application.Contracts.Federation;

public interface IFederationOptInService
{
    // Lists this channel's opt-ins (tenant-scoped). broadcasterId resolved from the authenticated principal upstream.
    Task<Result<IReadOnlyList<ChannelFederationOptInDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default);

    // Upserts one (BroadcasterId, PeerId, OptInType) opt-in (default-deny: creating one is the explicit allow).
    // Persists the row; emits ChannelFederationOptInChangedEvent. Caller must hold >= LeadModerator (Gate-2 `federation:optin:write`, enforced in the controller).
    Task<Result<ChannelFederationOptInDto>> UpsertAsync(
        Guid broadcasterId,
        UpsertChannelFederationOptInRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Disables (IsEnabled=false, soft-delete) one opt-in; emits ChannelFederationOptInChangedEvent(IsEnabled=false).
    Task<Result> DisableAsync(
        Guid broadcasterId,
        Guid optInId,
        Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Pure predicate used by the bus adapters: does this channel currently accept/share this OptInType with this peer?
    // Honors PeerId-null ("any trusted") + Direction (accept/share/both) + peer TrustState=trusted. No side effects.
    Task<Result<bool>> IsActionPermittedAsync(
        Guid broadcasterId,
        Guid peerId,
        string optInType,
        string direction,   // a FederationDirection constant: "accept" | "share" | "both" (NomNomzBot.Domain.Federation.Enums)
        CancellationToken cancellationToken = default);

    // Every local channel that accepts `optInType` from this peer — an enabled accept|both opt-in matching
    // (peerId OR any-trusted) — for the inbound broadcast fan-out (§3.7). Returns an EMPTY list (never a failure)
    // when the peer is untrusted or no channel opted in. No writes.
    Task<Result<IReadOnlyList<Guid>>> ListAcceptingBroadcasterIdsAsync(
        Guid peerId,
        string optInType,
        CancellationToken cancellationToken = default);
}
```

`FederationDirection` is a static class of string constants (`Accept`, `Share`, `Both`) in `NomNomzBot.Domain/Federation/Enums/FederationEnums.cs`, not an enum: the direction is a `string` end to end, matching the `ChannelFederationOptIns.Direction` column.

### 3.5 `IFederationInboundGateway` (inbound seam) + `IRemoteEventBus` (outbound/subscribe legs)

The remote bus is the cross-instance leg layered **beside** the existing in-process `IEventBus` — it does not replace it. It is split into two seams:

- **Inbound — `IFederationInboundGateway` (BUILT).** Inbound is always controller-driven (the mTLS `/federation/inbound` endpoint), so it is transport-independent and lives in its own interface. It is THE inbound seam; `IRemoteEventBus` no longer carries a `ReceiveInboundAsync`.
- **Outbound + subscribe — `IRemoteEventBus` (NOT BUILT).** Local domain events a channel has opted to `share` are enveloped, signed, and queued for delivery to trusted peers. This leg is the deployment-variant transport (Redis vs WebSocket, §7) and ships with those adapters.

```csharp
namespace NomNomzBot.Application.Contracts.Federation;

public interface IFederationInboundGateway
{
    // Verifies, gates, journals and applies one inbound peer envelope. `peerId` is resolved upstream from the validated
    // mTLS client-cert thumbprint. Fail-closed at every gate; a failure carries the SAME reason code that is published
    // on the audit FederatedEventRejectedEvent. Idempotent on EventId. On accept it publishes FederatedEventReceivedEvent.
    // Gate order (each rejection => FederatedEventRejectedEvent with the reason):
    //   1. signature      IFederationEventSigner.VerifyAsync                  -> signature_invalid | algorithm_unsupported | ...
    //   2. peer binding   peer exists, TrustState=trusted, and
    //                     peer.InstanceId == envelope.OriginInstanceId         -> peer_untrusted
    //   3. accept-set     a registered IFederationInboundHandler has
    //                     Type == envelope.FederatedEventType                  -> schema_invalid
    //   4. replay guard   EventJournal already holds envelope.EventId          -> replay      (journal EventId is the dedupe key)
    //   5. opt-in         DIRECTED envelope only (TargetBroadcasterId set): IsActionPermittedAsync(accept) -> no_opt_in
    //                     (a BROADCAST envelope fans out in the translator; zero matches is an accepted no-op)
    //   6. journal        IEventJournal.AppendAsync (Source="federation"; metadata = peerId, originInstanceId, originBroadcasterId)
    //   7. translate+apply IFederationInboundTranslator.TranslateAndApplyAsync -> failure surfaces its code (default schema_invalid)
    Task<Result<FederationInboundOutcome>> ReceiveInboundAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default);
}

// EventId = the journaled id; StreamPosition = the allocated per-tenant position;
// Applied = at least one local channel actually applied the claim (false = accepted-but-noop: no channel opted in).
public sealed record FederationInboundOutcome(Guid EventId, long StreamPosition, bool Applied);

// NOT BUILT — outbound + subscribe legs only.
public interface IRemoteEventBus
{
    // Enqueues a signed envelope for outbound delivery to every trusted peer whose (channel, optInType) share predicate
    // passes. Durable: the envelope's source row is EventJournal; the outbound cursor is ProjectionCheckpoint
    // "federation.outbound". Emits FederatedEventDispatchedEvent per accepted peer. At-least-once; peers dedupe by EventId.
    Task<Result> PublishOutboundAsync(
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default);

    // Subscribes the adapter to a newly trusted peer's transport stream (SaaS Redis channel / lite WebSocket).
    // Idempotent. Invoked from the FederationPeerTrustedEvent handler.
    Task<Result> SubscribePeerAsync(Guid peerId, CancellationToken cancellationToken = default);

    // Tears down a revoked/blocked peer's subscription and drops buffered inbound. Idempotent.
    Task<Result> UnsubscribePeerAsync(Guid peerId, CancellationToken cancellationToken = default);
}
```

**Dedupe (decided).** The inbound at-most-once guard is the journal itself: `EventJournal.EventId` is unique and `IEventJournal.AppendAsync` is idempotent on it, so gate 4 asks the journal (`GetByEventIdAsync`) and rejects an already-journaled id as `replay`. There is no separate `IdempotencyKey` row for inbound federation (an earlier draft scoped one as `"federation.inbound"`).

### 3.6 `IFederationInboundTranslator` — envelope → typed event mapping (federation owns translation)

The federation ingress service owns wire-envelope → typed dispatch. After the gateway's `ReceiveInboundAsync` (§3.5) passes gates 1-5 and appends to `EventJournal`, the translator (1) **upcasts** `FederationEventEnvelope.PayloadJson` to the current schema via `IEventUpcasterRegistry` keyed by `(FederatedEventType, SchemaVersion)` (event-store; rollout-updates §3), (2) **resolves the local target channel(s)** (§3.7), and (3) **dispatches to the per-type `IFederationInboundHandler`** owned by the applying subsystem (§3.7) — it does **not** hardcode a switch or reference any subsystem payload type. For `"moderation.ban.shared"`, moderation ships `SharedChatBanInboundHandler`, which deserializes `SharedChatBanIssuedEvent` and calls `ISharedBanService.ApplyInboundSharedBanAsync` — federation routes; moderation owns the apply. No matching handler (unknown or not-yet-shipped type) ⇒ `Result` failure, surfaced as `FederatedEventRejectedEvent` reason `"schema_invalid"` (fail-closed, never silently dropped). No raw `DbContext`; the apply happens through the owning subsystem's service.

```csharp
namespace NomNomzBot.Application.Contracts.Federation;

public interface IFederationInboundTranslator
{
    // Deserializes envelope.PayloadJson by envelope.FederatedEventType into the owning subsystem's typed event and
    // invokes that subsystem's local service. "moderation.ban.shared" -> SharedChatBanIssuedEvent ->
    // ISharedBanService.ApplyInboundSharedBanAsync(...). Fails closed on unknown FederatedEventType / payload schema.
    // Returns the number of targets the handler applied to (0 = accepted-but-noop: no channel opted in).
    Task<Result<int>> TranslateAndApplyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
```

### 3.7 Inbound handler registry, identity & target resolution

**Per-type handlers (auto-discovered, owned by the applying subsystem).** Translation is not a hardcoded switch. Each accepted `FederatedEventType` has exactly one handler implementing the marker below, **shipped by the subsystem that owns the apply** (moderation ships the ban handlers; economy the trust/savings handlers). Handlers are registered by the assembly scan (backend-structure §4 auto-discovery — "drop a class, no wiring edit"); `FederationInboundTranslator` (§3.6) injects `IEnumerable<IFederationInboundHandler>`, selects the one whose `Type` equals `envelope.FederatedEventType`, and invokes it once per resolved target channel. This inverts the dependency cleanly: federation defines the abstraction; subsystems depend on it, never the reverse — federation references no subsystem payload type.

```csharp
namespace NomNomzBot.Application.Contracts.Federation;

public interface IFederationInboundHandler
{
    // The single FederatedEventType this handler accepts, e.g. "moderation.ban.shared". One handler per type;
    // the set of registered handlers' Types IS the closed accept-set — anything else is rejected "schema_invalid".
    string Type { get; }

    // The ChannelFederationOptIns.OptInType that gates this type. The translator confirms an enabled accept|both
    // opt-in for (peerId, this) on each resolved target before invoking — the handler never re-checks the gate.
    string GatingOptInType { get; }

    // Deserializes envelope.PayloadJson (canonical JSON, already upcast to the current SchemaVersion by the translator)
    // into this subsystem's typed event and applies it through this subsystem's own service, for exactly one target
    // channel. Idempotent on (envelope.EventId, targetBroadcasterId). Fails closed on payload-schema mismatch
    // (=> FederatedEventRejectedEvent reason "schema_invalid").
    Task<Result> ApplyAsync(
        Guid peerId,
        Guid targetBroadcasterId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
```

**Recognized-type catalog.** The runtime accept-set is exactly the registered handlers' `Type` values; the table is the design roadmap and the federation-owned decision of *which* type strings exist and *which* opt-in gates each. Federation owns the left two columns; the owning subsystem ships the handler (right two).

| `FederatedEventType` | Gating `OptInType` | Owning subsystem · handler | Typed payload → apply |
|---|---|---|---|
| `moderation.ban.shared` | `shared_chat_bans` | moderation · `SharedChatBanInboundHandler` | `SharedChatBanIssuedEvent` → `ISharedBanService.ApplyInboundSharedBanAsync` |
| `moderation.banlist.entry` | `shared_ban_list` | moderation · `SharedBanListInboundHandler` *(reserved)* | `SharedBanListEntrySharedEvent` → `ISharedBanListService.ApplyInboundEntryAsync` |
| `trust.list.updated` | `shared_trust_list` | economy · `SharedTrustListInboundHandler` *(reserved)* | `SharedTrustEntrySharedEvent` → `ITrustListService.ApplyInboundTrustAsync` |
| `savings.contribution` | `shared_savings` | economy · `SharedSavingsContributionInboundHandler` *(reserved)* | `SharedSavingsContributionEvent` → `ISavingsService.ApplyInboundContributionAsync` |

> Only `moderation.ban.shared` has its applying service fully specced today (`ISharedBanService.ApplyInboundSharedBanAsync`, moderation §3). The three *(reserved)* rows fix the **type string + gating opt-in** now (so the accept-set is decided and a channel can already opt in); their handler ships — auto-discovered — with the owning subsystem's federation feature. Until that handler is registered the type is simply not in the accept-set, so an inbound envelope of that type is rejected `schema_invalid` (fail-closed), never queued or silently dropped.

**Inbound identity & target resolution.** The translator resolves *who sent it* and *which local channels it lands on* before any handler runs:

- **Peer identity (`peerId`)** — resolved at `/federation/inbound` from the validated mTLS **client-cert thumbprint** → the `FederationPeers` row (thumbprint recorded at handshake, §3.2). `envelope.OriginInstanceId` MUST equal that peer's `InstanceId` (blocks a trusted peer relaying another instance's envelope) and the peer MUST be `TrustState=trusted`; either mismatch ⇒ reject `peer_untrusted`.
- **Origin channel (`envelope.OriginBroadcasterId`)** — the *sender's* local channel `Guid`, opaque on this instance: retained for audit/correlation only, **never** resolved to a local FK. All cross-instance subject identity rides on **stable external ids** in the payload (Twitch user id, Twitch channel id), never a peer's local surrogate `Guid`. (E.g. `SharedChatBanIssuedEvent` carries `OriginTwitchChannelId` + `TargetTwitchUserId` as the load-bearing keys; its `OriginBroadcasterId` is informational.)
- **Target channel(s)** — the local `BroadcasterId`(s) the claim applies to:
  - `envelope.TargetBroadcasterId` **non-null** (directed): it must be a local `Channels.Id` **and** hold an enabled `accept|both` opt-in for `(peerId, gatingOptInType)`; otherwise reject `no_opt_in`. (A directed target requires the recipient's local `Guid` to have been exchanged in a prior directed handshake — uncommon.)
  - `envelope.TargetBroadcasterId` **null** (directory broadcast — the common case): fan out to **every** local channel holding an enabled `accept|both` opt-in matching `(peerId OR any-trusted, gatingOptInType)`. The handler's `ApplyAsync` runs **once per resolved target**, each idempotent on `(envelope.EventId, targetBroadcasterId)`. Zero matches ⇒ accept-and-noop (journaled, outcome `Applied=false`), not an error.

---

## 4. DTOs / contracts

Requests/responses are `record` types in `NomNomzBot.Application/DTOs/Federation/`; the wire envelope/signature contracts are in `NomNomzBot.Application/Contracts/Federation/`. App JSON uses **Newtonsoft.Json** (project convention); the **signed canonical** envelope body uses deterministic sorted-key UTF-8 JSON for signature stability.

```csharp
namespace NomNomzBot.Application.DTOs.Federation;

public sealed record FederationPeerDto(
    Guid Id, string InstanceId, string? DisplayName, string? BaseUrl,
    string DeploymentMode, string TrustState,
    DateTime FirstSeenAt, DateTime? LastHandshakeAt,
    IReadOnlyList<FederationPeerKeyDto> ActiveKeys);

public sealed record FederationPeerKeyDto(
    Guid Id, Guid PeerId, string KeyId, string Algorithm, string PublicKey,
    DateTime ValidFrom, DateTime? ValidTo, bool IsActive);

public sealed record RegisterFederationPeerRequest(
    string InstanceId, string? DisplayName, string? BaseUrl, string DeploymentMode,
    string PublicKey, string KeyId, string Algorithm);

public sealed record RevokeFederationPeerRequest(string Reason, bool Blocked);

public sealed record AddFederationPeerKeyRequest(
    string PublicKey, string KeyId, string Algorithm, DateTime ValidFrom, DateTime? ValidTo);

public sealed record ChannelFederationOptInDto(
    Guid Id, Guid BroadcasterId, Guid? PeerId, string OptInType, string Direction, bool IsEnabled);

public sealed record UpsertChannelFederationOptInRequest(
    Guid? PeerId, string OptInType, string Direction, bool IsEnabled);

public sealed record FederationInstanceDescriptorDto(
    string InstanceId, string DeploymentMode, string BaseUrl,
    string JwksUri, string SigningKeyId, string SigningPublicKey, string SigningAlgorithm);

public sealed record FederationHandshakeRequest(
    string InstanceId, string DeploymentMode, string BaseUrl,
    string SigningKeyId, string SigningPublicKey, string SigningAlgorithm);
```

```csharp
namespace NomNomzBot.Application.Contracts.Federation;

// The signed cross-instance message. Body is serialized canonically (sorted keys) for signature stability.
public sealed record FederationEventEnvelope(
    Guid EventId,                 // == EventJournal.EventId; the dedupe key end-to-end
    string OriginInstanceId,      // sender instance
    Guid? OriginBroadcasterId,    // sender channel (null = directory-level claim)
    Guid? TargetBroadcasterId,    // intended local channel (null = directory broadcast)
    string FederatedEventType,    // "moderation.ban.shared" | "trust.list.updated" | "savings.contribution" | ...
    int SchemaVersion,            // upcaster anchor
    string PayloadJson,           // canonical JSON; ids/refs only, PII slices reference CryptoKey DEKs
    DateTimeOffset OccurredAt);
```

---

## 5. Controller endpoints

Two controllers in `NomNomzBot.Api/Controllers/V1/`, both `[ApiVersion("1.0")]` + `[Authorize]`, responses `StatusResponseDto<T>` / `PaginatedResponse<T>`: `FederationController` (`[Route("api/v{version:apiVersion}/federation")]`, the global peer directory, Plane-C) and `ChannelFederationController` (`[Route("api/v{version:apiVersion}/channels/{channelId}/federation")]`, the per-channel opt-ins, Gate-2). The handshake, inbound and descriptor routes are **not built**; `IFederationInboundGateway.ReceiveInboundAsync` (§3.5) is the service the inbound route will call.

**Role gate.** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 (management) = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the action-key column before the service call (403 FORBIDDEN when below); its keys are seeded global `ActionDefinitions` (schema B.3), and a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. Plane-C (platform) rows = `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)` against the seeded global `IamPermissions` (schema C.1) — a separate vocabulary from Gate-2 `ActionDefinitions`; the ASP.NET `[Authorize(Policy="<key>")]` policy-name IS the `IamPermissions` key verbatim (directory + peer-key endpoints are global, operator-managed). The mTLS handshake/inbound endpoints sit on a separate cert-authenticated pipeline — `[Authorize(AuthenticationSchemes="Certificate")]`, not the user JWT — and `/.well-known/federation-descriptor` is anonymous/public.

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/federation/peers` | GET | `PageRequestDto` (+`?trustState=`) | `PaginatedResponse<FederationPeerDto>` | platform · `audit:read` |
| `/federation/peers/{peerId}` | GET | — | `StatusResponseDto<FederationPeerDto>` | platform · `audit:read` |
| `/federation/peers` | POST | `RegisterFederationPeerRequest` | `StatusResponseDto<FederationPeerDto>` | platform · `iam:manage` |
| `/federation/peers/{peerId}/trust` | POST | — | `StatusResponseDto<FederationPeerDto>` | platform · `iam:manage` |
| `/federation/peers/{peerId}/revoke` | POST | `RevokeFederationPeerRequest` | `StatusResponseDto<object>` | platform · `iam:manage` |
| `/federation/peers/{peerId}/keys` | POST | `AddFederationPeerKeyRequest` | `StatusResponseDto<FederationPeerKeyDto>` | platform · `iam:manage` |
| `/federation/peers/{peerId}/keys/{keyId}` | DELETE | — | `StatusResponseDto<object>` | platform · `iam:manage` |
| `/channels/{channelId}/federation/opt-ins` | GET | — | `StatusResponseDto<IReadOnlyList<ChannelFederationOptInDto>>` | management / LeadModerator · `federation:optin:read` |
| `/channels/{channelId}/federation/opt-ins` | PUT | `UpsertChannelFederationOptInRequest` | `StatusResponseDto<ChannelFederationOptInDto>` | management / LeadModerator · `federation:optin:write` |
| `/channels/{channelId}/federation/opt-ins/{optInId}` | DELETE | — | `StatusResponseDto<object>` | management / LeadModerator · `federation:optin:delete` |
| `/.well-known/federation-descriptor` | GET | — | `StatusResponseDto<FederationInstanceDescriptorDto>` | Anonymous (public, no secrets) — **NOT BUILT** |
| `/federation/handshake` | POST | `FederationHandshakeRequest` | `StatusResponseDto<FederationInstanceDescriptorDto>` | mTLS peer cert (not JWT) — **NOT BUILT** |
| `/federation/inbound` | POST | `FederationEventEnvelope` + `X-Federation-Signature` header | `StatusResponseDto<FederationInboundOutcome>` | **mTLS client cert**; peer resolved from cert thumbprint; envelope signature re-verified in-body (defense-in-depth) — **NOT BUILT** (service seam: `IFederationInboundGateway`) |

> The OIDC issuer endpoints (`/.well-known/openid-configuration`, `/connect/authorize`, `/connect/token`, `/connect/jwks`) are **owned by OpenIddict**, registered only when the `federation`/`multi_user_sso` gate is on (see §7). This subsystem does not hand-author those routes; it configures OpenIddict's server + the OIDC *client* (`OpenIdConnect` handler with `Authority`/`MetadataAddress` pointing at the trusted peer) for SSO relying-party flows.

---

## 6. Pipeline actions

**None.** Federation is an infrastructure/trust-plane concern, not a per-command pipeline step. Inbound `shared_chat_bans` claims are applied through the existing moderation service (constructing a `ModerationActions` row with `Origin=federation` — the cross-instance federated origin, distinct from Twitch-native `Origin=shared_chat`), not through a user-authored pipeline action — so a channel can never script around the opt-in/trust gates. No `ICommandAction` is added.

> **Inbound shared-ban precondition.** The precondition for applying an inbound shared ban is a **verified NomNomzBot federation trust relationship** — a `FederationPeers` trust-directory entry at `TrustState=trusted`, a valid signed federation token, and a verified per-message signature — **not** an active Twitch shared-chat session. NomNomzBot cross-instance federation is a distinct trust plane from Twitch's shared-chat feature; they are not interchangeable. This precondition is exactly the federation trust gate already enforced in the inbound sequence (`IFederationInboundGateway.ReceiveInboundAsync`: signature verify → `TrustState=trusted` + origin binding → accept-set → replay guard (journal `EventId`) → opt-in `accept`, §3.5). A Twitch-native shared-chat session ban is a separate path persisted with `Origin=shared_chat`.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs`, new `AddFederation(this IServiceCollection, IConfiguration)` extension called from `AddInfrastructure`. All registrations are **conditional on the `federation` feature gate** resolved from `DeploymentProfile` + `FeatureFlag`; when off, none of the issuer/bus types are registered and the `/federation/*` + `/connect/*` surfaces return 404.

| Interface | Implementation | Lifetime | Notes |
|---|---|---|---|
| `IFederationPeerService` | `FederationPeerService` | Scoped | Repository + `IUnitOfWork`; emits trust/revoke events via `IEventBus`. |
| `IFederationHandshakeService` | `FederationHandshakeService` | Scoped | Uses `IHttpClientFactory` (mTLS client cert) for outbound handshake. |
| `IFederationOptInService` | `FederationOptInService` | Scoped | Tenant-scoped; reads `FederationPeers.TrustState`. |
| `IFederationInboundGateway` | `FederationInboundGateway` | Scoped | **Built.** The fail-closed inbound gate sequence (§3.5). Explicit registration. |
| `IFederationInboundTranslator` | `FederationInboundTranslator` | Scoped | Deserializes `FederationEventEnvelope.PayloadJson` by `FederatedEventType`; for `moderation.ban.shared` calls moderation's `ISharedBanService.ApplyInboundSharedBanAsync`. |
| `IFederationEventSigner` | `FederationEventSigner` | Scoped | In-box `System.Security.Cryptography.RSA` (`rsa-sha256`); scoped because it verifies against the scoped `IApplicationDbContext`. |
| `IFederationSigningKeyProvider` | `FederationSigningKeyProvider` | Singleton | Config-backed active signing key (§3.3). |
| `IRemoteEventBus` | **profile adapter** (below) | Singleton | **Not built.** Selected by `DeploymentProfile.CacheProvider`/`Mode`. |
| `FederationPeerRepository` | (concrete, extends `GenericRepository<FederationPeers>`) | Scoped | Directory reads/writes; global (no tenant filter). |
| `ChannelFederationOptInRepository` | (concrete, extends `GenericRepository<ChannelFederationOptIns>`) | Scoped | Tenant-filtered. |
| `IEventHandler<FederationPeerTrustedEvent>` | `FederationPeerTrustedHandler` | Scoped | Calls `IRemoteEventBus.SubscribePeerAsync` + JWKS prefetch + `IFederationSchemeRegistrar.EnsurePeerSchemeAsync`. |
| `IEventHandler<FederationPeerRevokedEvent>` | `FederationPeerRevokedHandler` | Scoped | Calls `IRemoteEventBus.UnsubscribePeerAsync` + `IFederationSchemeRegistrar.RemovePeerScheme`. |
| `IFederationInboundHandler` (n impls) | per-type, owned by applying subsystem (e.g. moderation `SharedChatBanInboundHandler`) | Scoped | Auto-discovered by assembly scan (§3.7); the registered `Type` set is the inbound accept-set. |
| `IFederationSchemeRegistrar` | `FederationSchemeRegistrar` | Singleton | Materializes/removes per-peer OIDC client schemes at runtime; owns the `OpenIdConnectOptions` cache keyed `fed:{InstanceId}`. Gated. |
| `IAuthenticationSchemeProvider` | `FederationSchemeProvider` (decorator) | Singleton | Resolves `fed:*` schemes on demand from the `FederationPeers` directory (default provider decorated). Gated. |
| `FederationSchemeWarmup` | `IHostedService` | Singleton (hosted) | On start, ensures schemes for all `TrustState=trusted` peers. Gated. |
| `FederationOutboundDispatcher` | `IHostedService` | Singleton (hosted) | Reads `EventJournal` past `ProjectionCheckpoint "federation.outbound"`, calls `IRemoteEventBus.PublishOutboundAsync`. Guarded by `IRunOnceGuard` on multi-instance deployments. |

**Deployment-profile adapter variants for `IRemoteEventBus` (chosen by DI):**

| Profile / `DeploymentProfile` | `IRemoteEventBus` impl | Transport |
|---|---|---|
| `saas` (`CacheProvider=redis`) | `RedisRemoteEventBus` | Redis pub/sub channel per trusted peer over `ISubscriber` (StackExchange.Redis) + outbound `HttpClient` (mTLS) to peer `/federation/inbound`. |
| `self_host_lite` / `self_host_full` (`CacheProvider=in_memory`) | `WebSocketRemoteEventBus` | `ClientWebSocket` (in-box) to each trusted peer + inbound via the mTLS `/federation/inbound` controller. |

**OIDC issuer (feature-gated, registered only when `federation`/`multi_user_sso` on):**
- `services.AddOpenIddict().AddServer(...)` with the EF Core store over `AppDbContext` (OpenIddict), RS256/ES256 signing keys, `/connect/authorize` + `/connect/token` + `/connect/jwks`.
- OIDC **client** for peer SSO — **dynamically registered per trusted peer at runtime**, not at startup. ASP.NET auth schemes are normally fixed at boot, but trusted peers change via `TrustPeerAsync`/`RevokePeerAsync`, so a static `AddOpenIdConnect`-per-peer at boot cannot enumerate them. The dynamic mechanism (`MS.AspNetCore.Authentication.OpenIdConnect`):
  - `IFederationSchemeRegistrar` (Application) — `EnsurePeerSchemeAsync(FederationPeerDto)` / `RemovePeerScheme(string instanceId)`. Scheme name convention `fed:{InstanceId}`, callback path `/signin-fed/{InstanceId}`.
  - `FederationSchemeProvider` (Infrastructure) **decorates** the default `IAuthenticationSchemeProvider` to resolve `fed:*` schemes on demand from the `FederationPeers` directory; an `IOptionsMonitorCache<OpenIdConnectOptions>` seeded by the registrar builds each peer's `OpenIdConnectOptions` with `Authority = peer.BaseUrl`, `MetadataAddress = {BaseUrl}/.well-known/openid-configuration` (auto-fetches the peer JWKS).
  - Lifecycle: `FederationPeerTrustedHandler` → `EnsurePeerSchemeAsync`; `FederationPeerRevokedHandler` → `RemovePeerScheme`; `FederationSchemeWarmup` (`IHostedService`) ensures schemes for all already-`trusted` peers on cold start. The whole branch sits inside the `federation`/`multi_user_sso` gate.
- mTLS: `services.AddAuthentication().AddCertificate(...)` (`MS.AspNetCore.Authentication.Certificate`) for the `Certificate` scheme guarding handshake + inbound.

---

## 8. Dependencies (from the stack doc)

| Dependency | Party | Use here |
|---|---|---|
| `OpenIddict.AspNetCore` + `OpenIddict.EntityFrameworkCore` | 3rd (Apache-2.0) | OIDC/OAuth2 **issuer** — authorize/token/JWKS — **only** under the feature gate. *Not yet referenced by the code.* |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 2nd | OIDC **client** / SSO relying party; auto-fetches peer JWKS. *Not yet referenced by the code.* |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 2nd | Resource-server validation of issued + peer tokens. |
| `System.IdentityModel.Tokens.Jwt` + `Microsoft.IdentityModel.Tokens` | 2nd | RS256/ES256 token create/validate + JWKS. As built, the repo's token code (`JwtTokenService`, the impersonation token minter) uses `JwtSecurityTokenHandler` from `System.IdentityModel.Tokens.Jwt`; `Microsoft.IdentityModel.JsonWebTokens` / `JsonWebTokenHandler` is **not** referenced. |
| `Microsoft.AspNetCore.Authentication.Certificate` | 2nd | mTLS client-cert scheme for handshake + inbound endpoints. *Not yet referenced by the code.* |
| `System.Security.Cryptography` (`RSA`, `SHA256`) | 1st (in-box) | `rsa-sha256` per-message envelope signing/verification — **no third-party crypto**. |
| `System.Net.WebSockets` (`ClientWebSocket`) | 1st (in-box) | Lite/self-host remote-bus transport. |
| `StackExchange.Redis` | 3rd (MIT, transitive) | SaaS remote-bus pub/sub channels. |
| `System.Net.Http` (`IHttpClientFactory`) + `Microsoft.Extensions.Http.Resilience` | 1st/2nd | Outbound peer delivery + handshake (retry/breaker). |
| `Newtonsoft.Json` | 3rd | App-side DTO JSON (project convention). Canonical signed body uses deterministic sorted-key serialization. |
| `IRunOnceGuard` (in-repo `PostgresRunOnceGuard`, `pg_try_advisory_lock`) | 1st | Single-fire of `FederationOutboundDispatcher` across overlapping instances on any Postgres deployment. No third-party lock library. |

**Explicitly NOT used:** `DistributedLock.Postgres`; NSec / BouncyCastle (no Ed25519 — `rsa-sha256` only); Duende IdentityServer (license-encumbered → OpenIddict); MassTransit (remote bus is the thin adapter over existing `IEventBus` + transport).

---

## 9. Decisions (resolved)

Five cross-cutting decisions govern this subsystem; all are settled and binding:
- **OIDC issuer is OpenIddict, feature-gated.** The issuer surface is OpenIddict, stood up only under the `federation`/`multi_user_sso` gate (§7).
- **Asymmetric (RS256/ES256) signing is a hard prerequisite of federation.** Token issuance on RS256/ES256 with a published JWKS is a dependency federation builds on; federation does not start until it is in place (§Grounding, decision 3).
- **Inbound translation is an auto-discovered handler registry, not a switch.** `IFederationInboundHandler` (one per `FederatedEventType`, owned by the applying subsystem) is assembly-scanned (§3.7); the registered `Type` set is the closed accept-set; unknown/not-yet-shipped types fail closed `schema_invalid`. Federation routes; it never references a subsystem payload type.
- **`ed25519` is forward-compat-only; verification is `rsa-sha256`-only.** An `ed25519` peer key may be stored but is never accepted at verify time (`algorithm_unsupported`); `TrustPeerAsync` requires an active `rsa-sha256` key (§3.3).
- **Per-peer OIDC client schemes are registered dynamically at runtime.** `IFederationSchemeRegistrar` + a decorating `FederationSchemeProvider` materialize/remove `fed:{InstanceId}` schemes on trust/revoke; a warm-up hosted service rehydrates them on start (§7). Cross-instance identity is keyed on stable external ids (Twitch ids, `InstanceId`), never a peer's local surrogate `Guid` (§3.7).
