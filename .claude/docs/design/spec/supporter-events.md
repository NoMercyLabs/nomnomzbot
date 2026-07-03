# Interface Specification — Supporter Events (monetization ingest)

**Status:** Implementable. Code the owner writes from this should compile first-try. **Supersedes `donations.md`** — generalizes the tips-only donation subsystem into one adapter-based monetization model (tips + memberships + merch + charity). The SE/Streamlabs/Ko-fi work from `donations.md` is preserved as `tip`-kind adapters.
**Sources of truth (provider ingress verified live 2026-06-23):** **webhook+HMAC** — Patreon (OAuth2 + `members`/`members:pledge` webhooks, sig `X-Patreon-Signature` HMAC-**MD5**), Fourthwall (Basic-auth mgmt + `order_placed`/`donation`/`subscription_*` webhooks, `X-Fourthwall-Hmac-SHA256`), Shopify (OAuth2 app + `orders/*` webhooks, `X-Shopify-Hmac-SHA256`, dedup `X-Shopify-Webhook-Id`, 5 s ack); **Socket.IO** — TipeeeStream (API key as `access_token`, socket host from `GET /v2.0/site/socket`, `donation`/`subscription`), TreatStream (OAuth2 socket token, `realTimeTreat`, **no payload id → composite dedup**); **raw WS** — Pally.gg (`wss://events.pally.gg?auth=KEY`, `campaigntip.notify`, 60 s keepalive, beta); **polling** — DonorDrive/Extra-Life (public `/donations` API, ETag, ~15 s). **StreamLoots excluded** — no public API/webhooks (scrape-only), not productionizable. Corpus: `donations.md` (the SE/Streamlabs/Ko-fi adapters + the socket hosted-service pattern, generalized here); `webhooks.md` (inbound webhook adapter + HMAC verify + dedup); `integrations-oauth.md` (the vaulted OAuth connect for Patreon/Shopify/TreatStream); `economy.md` (optional reward-on-supporter-event); `widgets-overlays.md` (Alerts widget); `platform-conventions.md` (`IEventBus`, `IFieldCipher`, `IRunOnceGuard`, `IDeploymentProfileService`); `scaling-qos.md` (`IRateLimiter`); locked schema `2026-06-16-database-schema.md` (Domain P).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>` / `PaginatedResponse<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json; secrets AEAD via `IFieldCipher`.

> **Why.** "More donation and commerce options" spans three different shapes — one-time **tips**, recurring **memberships**, and product **merch orders** — plus **charity**. Rather than three subsystems, this is one **generic adapter system**: every provider is an `ISupporterSource` that normalizes its payload into a single `SupporterEvent` carrying a `Kind`; one normalized domain event fires `supporter.<kind>` pipeline triggers and the Alerts widget, and optionally rewards the supporter through the economy. Adding a provider = drop an adapter (and, for webhook providers, a verifier) — no engine change. This **replaces** the tips-only `donations.md`.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **One model, one event, `Kind`-typed.** `SupporterEvent` carries `Kind ∈ {tip, membership, merch, charity}` + kind-relevant fields (amount/currency, tier, quantity/months, merch line-items, message, `IsRecurring`). One `SupporterEventReceived` domain event → `supporter.tip` / `supporter.membership` / `supporter.merch` / `supporter.charity` triggers, plus a catch-all `supporter.any`. The old `donation` trigger becomes `supporter.tip`. |
| D2 | **Generic adapter: `ISupporterSource`.** Each provider declares a `SourceKey` + `Capabilities` (which kinds it emits, its `ConnectionMode`) and **normalizes** its raw payload → `SupporterEvent`. Shipped adapters: `streamelements`, `streamlabs`, `kofi` (migrated from `donations.md`, `tip`); `patreon` (membership), `fourthwall` (tip+membership+merch), `tipeee` (tip), `treatstream` (tip), `donordrive` (charity), `pally` (tip), `shopify` (merch). Adapters are **auto-discovered**. |
| D3 | **Four ingress modes, reusing existing primitives** (`ConnectionMode`): **`webhook`** — Ko-fi, Patreon, Fourthwall, Shopify ingest through `webhooks.md`'s inbound endpoint + a `Supporter` adapter kind, with **per-provider HMAC verification** (Patreon = HMAC-MD5, Fourthwall/Shopify = HMAC-SHA256; Shopify dedup via `X-Shopify-Webhook-Id`); **`socket`** — StreamElements, Streamlabs, TipeeeStream, TreatStream via the generalized `SupporterSocketHostedService` (Socket.IO / raw WS, one connection per source, `IRunOnceGuard`); **`ws`** — Pally.gg raw WebSocket (same hosted service); **`poll`** — DonorDrive via `SupporterPollHostedService` (ETag, ~15 s, `IRunOnceGuard`). OAuth providers (Patreon, Shopify, TreatStream) connect through the vaulted `integrations-oauth.md` flow. |
| D4 | **Idempotent ingest.** Each event dedups on `(BroadcasterId, SourceKey, ProviderTransactionId)`; the adapter computes `ProviderTransactionId` from the provider's id (Shopify `X-Shopify-Webhook-Id`, others' payload id) or a **composite hash** where none exists (TreatStream = `sender+receiver+createdAt+message`). |
| D5 | **Optional economy reward.** A per-(source,kind) config can grant currency on a supporter event (reuses `economy.md`); off by default. Alerts ride the existing Alerts widget, branching on `Kind`. |
| D6 | **Secrets AEAD, rate-limited, opt-in.** Provider API keys / webhook secrets / OAuth tokens are AEAD (`IFieldCipher` or the vault); ingest passes `IRateLimiter` (per-source inbound bucket); sources disabled until configured (default-deny). |
| D7 | **Schema:** rename **P.15 `DonationConnection`→`SupporterConnection`**, **P.16 `DonationRecord`→`SupporterEvent`** (+ `Kind` and the kind-fields). `donations.md` is deleted (folded here). **StreamLoots is not modeled** (no sanctioned API); `loot` is not a `Kind` until a provider exists. |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`SupporterConnection`** | **P.15 (RENAME of `DonationConnection`)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `SourceKey string(30)` **[VC:enum]** (`streamelements`\|`streamlabs`\|`kofi`\|`patreon`\|`fourthwall`\|`tipeee`\|`treatstream`\|`donordrive`\|`pally`\|`shopify`); `ConnectionMode string(20)` **[VC:enum]** (`webhook`\|`socket`\|`ws`\|`poll`); `AuthSecretCipher text?` **[PII]** (AEAD — API key / webhook secret; null when OAuth-vaulted); `IntegrationConnectionId Guid?` FK→`IntegrationConnection.Id` (OAuth providers — Patreon/Shopify/TreatStream); `InboundWebhookEndpointId Guid?` FK→`InboundWebhookEndpoint.Id` (webhook providers); `IsEnabled bool` (default false); `Status string(20)`; `LastEventAt DateTime?`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, SourceKey)`. |
| **`SupporterEvent`** | **P.16 (RENAME of `DonationRecord`)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index; `SourceKey string(30)`; `Kind string(20)` **[VC:enum]** (`tip`\|`membership`\|`merch`\|`charity`); `SupporterDisplayName string(100)`; `SupporterUserId Guid?` FK→`Users.Id` (resolved viewer when matchable); `AmountMinor long?` (minor units); `Currency string(3)?`; `Tier string(50)?` (membership tier); `Quantity int?` (months / item count); `ItemsJson text?` **[VC:JSON]** (merch line-items); `MessageText text?`; `IsRecurring bool`; `ProviderTransactionId string(120)` (dedup key, §0 D4); `PayloadJson text` **[VC:JSON]** (normalized raw); `ReceivedAt DateTime`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, SourceKey, ProviderTransactionId)`; **Index** `(BroadcasterId, ReceivedAt)`, `(BroadcasterId, Kind)`. |

---

## 2. Domain event (→ pipeline trigger)

Inherit `DomainEventBase`. Published via `IEventBus`; consumed by the trigger source, the Alerts widget, and (opt-in) economy reward.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record SupporterEventReceived : DomainEventBase
{
    public required string SourceKey { get; init; }
    public required string Kind { get; init; }                 // tip | membership | merch | charity
    public required string SupporterDisplayName { get; init; }
    public Guid? SupporterUserId { get; init; }
    public long? AmountMinor { get; init; }
    public string? Currency { get; init; }
    public string? Tier { get; init; }
    public int? Quantity { get; init; }
    public string? MessageText { get; init; }
    public bool IsRecurring { get; init; }
    public required Guid SupporterEventId { get; init; }        // FK→SupporterEvent (full row for the merch line-items etc.)
}
```

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Supporters`. `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Supporters/`.

```csharp
// Generic provider adapter — auto-discovered; one per provider.
public interface ISupporterSource
{
    string SourceKey { get; }                       // "patreon", "fourthwall", …
    SupporterSourceCapabilities Capabilities { get; } // emitted kinds + ConnectionMode
    // Normalizes a raw provider payload into a SupporterEvent draft (no persistence). Computes ProviderTransactionId.
    Task<Result<SupporterEventDraft>> NormalizeAsync(string rawPayload, CancellationToken ct = default);
}

// Single ingest path — dedup, persist, publish. Called by the Supporter webhook adapter / socket / poll services.
public interface ISupporterIngestService
{
    Task<Result> IngestAsync(Guid broadcasterId, string sourceKey, string rawPayload, CancellationToken ct = default);
}

public interface ISupporterConnectionService
{
    Task<Result<IReadOnlyList<SupporterConnectionDto>>> ListAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<SupporterConnectionDto>> UpsertAsync(Guid broadcasterId, Guid actorUserId, UpsertSupporterConnectionRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid broadcasterId, Guid actorUserId, string sourceKey, CancellationToken ct = default);
    Task<Result<PagedList<SupporterEventDto>>> ListEventsAsync(Guid broadcasterId, SupporterEventQuery query, CancellationToken ct = default);
}

public sealed record SupporterSourceCapabilities(IReadOnlyList<string> Kinds, string ConnectionMode, bool RequiresOAuth);
public sealed record SupporterEventDraft(string Kind, string SupporterDisplayName, long? AmountMinor, string? Currency, string? Tier, int? Quantity, string? ItemsJson, string? MessageText, bool IsRecurring, string ProviderTransactionId, string PayloadJson);
public sealed record UpsertSupporterConnectionRequest(string SourceKey, string ConnectionMode, string? AuthSecret, Guid? IntegrationConnectionId, bool IsEnabled);
public sealed record SupporterConnectionDto(string SourceKey, string ConnectionMode, bool HasSecret, bool IsEnabled, string Status, DateTime? LastEventAt);
```

`SupporterSocketHostedService` / `SupporterPollHostedService` (`IHostedService`, `IRunOnceGuard`) own socket/ws/poll ingress; the `Supporter` webhook adapter (`webhooks.md`) handles webhook providers (per-provider HMAC verify before calling `IngestAsync`). `SupporterTriggerSource` (consumes `SupporterEventReceived`) matches `supporter.<kind>`/`supporter.any` bound pipelines.

---

## 4. Pipeline triggers, alerts, economy

- **Triggers:** `supporter.tip`, `supporter.membership`, `supporter.merch`, `supporter.charity`, `supporter.any` (registered `event` kinds). Vars: `{{supporter.name}}`, `{{supporter.kind}}`, `{{supporter.amount}}`, `{{supporter.currency}}`, `{{supporter.tier}}`, `{{supporter.quantity}}`, `{{supporter.message}}`.
- **Alerts:** the Alerts widget (`widgets-overlays.md`) renders supporter events, branching on `Kind`.
- **Economy (opt-in):** a per-(source,kind) reward config grants currency via `economy.md`; off by default.

No new pipeline **actions** — responses use existing actions.

---

## 5. REST surface

Controller `SupportersController`, `[Route("api/v{version:apiVersion}/supporters")]`. `[Authorize]`; Gate-2 keys.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/connections` | — | `StatusResponseDto<IReadOnlyList<SupporterConnectionDto>>` | management / Moderator · `supporters:read` |
| PUT | `/connections` | `UpsertSupporterConnectionRequest` | `StatusResponseDto<SupporterConnectionDto>` | management / Broadcaster · `supporters:config:write` |
| DELETE | `/connections/{sourceKey}` | — | `StatusResponseDto<bool>` | management / Broadcaster · `supporters:config:write` |
| GET | `/events` | query | `PaginatedResponse<SupporterEventDto>` | management / Moderator · `supporters:read` |

Seed in `roles-permissions.md`: **`supporters:read`** (`management`, Moderator 10, `Low`), **`supporters:config:write`** (`management`, Broadcaster 40, **`Critical`** — connecting a payout/identity-bearing money source; not permit-grantable). Replaces the old `donations:read`/`donations:config:write`.

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Supporters/DependencyInjection.cs` (`AddSupporters()`): `ISupporterIngestService`→`SupporterIngestService` (Scoped); `ISupporterConnectionService`→`SupporterConnectionService` (Scoped); `SupporterConnectionRepository` + `SupporterEventRepository` (Scoped); all `ISupporterSource` adapters auto-discovered; `SupporterSocketHostedService` + `SupporterPollHostedService` (`IHostedService`, `IRunOnceGuard`); the `Supporter` webhook adapter registers with `webhooks.md`; `SupporterTriggerSource` (Singleton). OAuth providers resolve tokens from the `integrations-oauth.md` vault.

**Tests (prove behavior):** each adapter `NormalizeAsync` maps a real provider sample to the right `Kind` + fields (Patreon→`membership` with `Tier`/`IsRecurring`; Fourthwall order→`merch` with `ItemsJson`; Tipeee→`tip` with amount/currency; DonorDrive→`charity`); ingest **dedups** on `(BroadcasterId, SourceKey, ProviderTransactionId)` (a redelivered Shopify webhook with the same `X-Shopify-Webhook-Id`, or a recomputed TreatStream composite, inserts **once**); webhook HMAC verification rejects a bad signature (Patreon MD5, Shopify/Fourthwall SHA256) with no persistence; a verified event publishes exactly one `SupporterEventReceived` with the right `Kind`, firing `supporter.<kind>` + `supporter.any`; the opt-in economy reward grants the configured currency only when enabled; a disabled connection ingests nothing and starts no socket/poll runner; rate-limit denial drops the event without persisting; the SE/Streamlabs/Ko-fi adapters still produce `tip` events (parity with the former donations subsystem).

---

## 7. Decisions (resolved)

One `Kind`-typed `SupporterEvent` model + `supporter.<kind>` triggers (D1); generic auto-discovered `ISupporterSource` adapters, 10 shipped (D2); four ingress modes reusing webhook/socket/ws/poll primitives + OAuth vault (D3); idempotent dedup incl. composite where no id exists (D4); opt-in economy reward + Alerts branching on kind (D5); AEAD secrets, rate-limited, opt-in (D6); schema rename P.15→`SupporterConnection`, P.16→`SupporterEvent`, `donations.md` folded/deleted, StreamLoots/`loot` excluded pending a real API (D7).
