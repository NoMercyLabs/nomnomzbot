# Interface Specification — Monetization & Billing

**Subsystem:** SaaS subscriptions, tiers + `TierLimit` quotas, usage counters tied to cost drivers, invite codes, founders badge. **The hosted/SaaS service is paid-only — there is no free hosted tier; the only free path is self-host (unlimited).**
**Status:** Directly-implementable. Code from this first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` Domain N (N.1–N.7) + globals `P.12 DeploymentProfile` / `P.13 FeatureFlag`; design `2026-06-16-monetization.md`; stack `2026-06-16-stack-and-dependencies.md`; defaults `2026-06-16-decisions-pending-confirmation.md`.

## Conventions binding on this subsystem

- Namespace `NomNomzBot.*`. .NET 10 / C# 14 / EF Core 10. File-scoped namespaces, `Nullable` enabled, async all the way (never `.Result`/`.Wait`), `Result<T>` over exceptions/null.
- Surrogate PKs are `guid` via `Guid.CreateVersion7()` (UUIDv7); append-only/high-volume rows (`UsageRecord`) use `bigint` identity. Twitch/Stripe ids are indexed attribute columns. Tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`).
- Tenant-owned entities implement `ITenantScoped` (EF global filter + Postgres RLS). Soft-delete = `IsDeleted`+`DeletedAt` global filter.
- `BillingTier`, `TierLimit`, `InviteCode` are **GLOBAL** (no `BroadcasterId`, no RLS). `Subscription` is tenant-owned and soft-deletable. `FoundersBadge` is tenant-scoped but NOT soft-deletable (perk persists). `UsageRecord` is append-only.
- App JSON = **Newtonsoft.Json** (project rule). Money is integer cents (`int`/`long`), never `decimal` floats. Enum-ish string columns persist as text via `[VC:enum]` converters.
- Repository + `IUnitOfWork`; controllers never touch `DbContext` raw — they call typed service interfaces. No MediatR, no Roslyn.
- Responses: `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`.
- **Existing surface to EXTEND, not duplicate:**
  - `IFeatureGateService` (`NomNomzBot.Domain.Interfaces`) — already gates per-channel features. This subsystem adds the **tier/quota** check that feature-gating consults; do not fork it. `BillingService.GetEntitlementAsync` is the tier-aware source `FeatureGateService` will call for `MinTierId` flags.
  - `Result.ErrorCode` strings already mapped in `BaseController.ResultResponse`: **`BILLING_LIMIT`** and **`FEATURE_DISABLED`** → HTTP 403; `NOT_FOUND`, `VALIDATION_FAILED`, `ALREADY_EXISTS`, `RATE_LIMITED`. Reuse these exact codes; introduce **`QUOTA_EXCEEDED`** (add to the `FORBIDDEN`/`BILLING_LIMIT` → 403 arm) only where a metered quota (not a static gate) is hit.
  - Legacy `ChannelSubscription` (int PK, `string` `BroadcasterId`) is **superseded** by `Subscription` (N.3). Do not extend the old entity; the migration spec retires it.
  - Legacy `SubscriptionTier` enum (`Free/Starter/Pro/Platform`) is **viewer-Twitch-sub flavored and unrelated** — do NOT reuse for billing tiers. Billing tier keys are `free|base|pro|premium` (string, from `BillingTier.Key`). The hosted cloud plans are **`base` ($3.99), `pro` ($7.99), `premium` ($14.99)** — `base` is the hosted entry tier. The `free` key is retained **only** as the internal marker for self-host / unbilled installs; it is **never** a public cloud plan (`IsPublic=false`), is never offered at SaaS signup, and a SaaS signup can never land on it. Self-host is the only free path: every `TierLimit` resolves to `-1` (unlimited) and the founders badge is available.

---

## 1. Entities (locked schema — owned by this subsystem)

Defined authoritatively in `2026-06-16-database-schema.md` Domain N. Listed here with key fields only; the schema is the contract.

| Entity | Schema | Kind | Key fields |
|---|---|---|---|
| `BillingTier` | N.1 | GLOBAL | `Id guid PK`; `Key string(20) Unique` (`free\|base\|pro\|premium`); `DisplayName string(50)`; `PriceCents int`; `Currency string(3)`; `StripePriceId string(255)?`; `StripeProductId string(255)?`; `AllowsCustomBotName bool`; `PrioritySupport bool`; `IsPublic bool`; `SortOrder int`. |
| `TierLimit` | N.2 | GLOBAL | `Id guid PK`; `TierId guid FK→BillingTier`; `LimitKey string(50)` (`sandbox_exec_ms\|widget_count\|asset_storage_mb\|queue_size\|request_quota_per_day\|tts_max_characters\|response_variations_per_trigger\|custom_commands\|timers\|event_responses`); `LimitValue bigint` (`-1`=unlimited). **Unique** `(TierId, LimitKey)`. |
| `Subscription` | N.3 | tenant, soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels Unique`; `TierId guid FK→BillingTier`; `Status string(20)` (`active\|trialing\|past_due\|canceled\|incomplete`); `StripeCustomerIdCipher string(512)?` **[PII-shred]**; `StripeSubscriptionId string(255)? Index`; `BillingEmailCipher string(512)?` **[PII-shred]**; `SubjectKeyId guid?`; `CurrentPeriodStart/End timestamp?`; `TrialEndsAt timestamp?`; `GracePeriodEndsAt timestamp?`; `CancelAtPeriodEnd bool`; `CanceledAt timestamp?`; `IsInviteOnlyGrant bool`. **Unique** `(BroadcasterId)`. |
| `Invoice` | N.4 | tenant | `Id guid PK`; `BroadcasterId guid FK→Channels`; `SubscriptionId guid FK→Subscriptions`; `StripeInvoiceId string(255)? Unique`; `Number string(50)?`; `Status string(20)` (`draft\|open\|paid\|void\|uncollectible`); `AmountDueCents int`; `AmountPaidCents int`; `Currency string(3)`; `PeriodStart/End timestamp?`; `HostedInvoiceUrl string(2048)?`; `IssuedAt timestamp Index`; `PaidAt timestamp?`. **Unique** `StripeInvoiceId`. |
| `UsageRecord` | N.5 | tenant, APPEND-ONLY | `Id bigint PK`; `BroadcasterId guid FK→Channels`; `MetricKey string(50)` (matches `TierLimit.LimitKey`; covers `sandbox_exec_ms`); `Quantity bigint`; `PeriodStart timestamp Index`; `PeriodEnd timestamp`; `ReportedToStripe bool`; `CreatedAt`. **Unique** `(BroadcasterId, MetricKey, PeriodStart)`. |
| `FoundersBadge` | N.6 | tenant, NOT soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels Unique`; `GrantedAt timestamp`; `InviteCode string(50)? Index`; `IsActive bool`. **Unique** `(BroadcasterId)`. |
| `InviteCode` | N.7 | GLOBAL | `Id guid PK`; `Code string(50) Unique`; `MaxRedemptions int`; `RedemptionCount int`; `GrantsFoundersBadge bool`; `GrantsTierId guid? FK→BillingTier`; `ExpiresAt timestamp?`. **Unique** `Code`. |

Reads-only from other subsystems (NOT owned here): `Channels.BillingTierKey` (denormalized tier copy — this subsystem **writes** it on tier change), `Channels.IsFounder` (denormalized — written on badge grant), `FeatureFlag.MinTierId`/`MinTierKey` (consumed for tier-gated flags). `DeploymentProfile.Mode` distinguishes `saas` vs `self_host_*` (self-host = unbilled and unlimited; no hosted free tier exists). `BillingTier.AllowsCustomBotName` is **true for `pro`+ only** — a custom per-channel bot identity is a Pro/Premium hosted feature; `base` uses the shared platform bot, and self-host always allows a custom bot identity.

`DbSet` additions to `IApplicationDbContext` (and `AppDbContext`): `BillingTiers`, `TierLimits`, `Subscriptions`, `Invoices`, `UsageRecords`, `FoundersBadges`, `InviteCodes`. (The legacy `ChannelSubscriptions` set is removed when `ChannelSubscription` is retired.)

---

## 2. Domain events

All inherit the canonical `DomainEventBase` (`NomNomzBot.Domain.Events`; supplies `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt` — authoritative definition in `platform-conventions.md` §2.0). Events **do not redeclare** the inherited members (`EventId` / `BroadcasterId` / `OccurredAt`) — each event below adds only its own payload fields. Published via `IEventBus.PublishAsync`. Naming follows existing sealed-class-with-`required`-init style (the moderation.md §2 events are the reference pattern). Place in `NomNomzBot.Domain/Events/Billing/`.

Every event in this subsystem is tenant-scoped, so its publisher sets the inherited `Guid BroadcasterId` to the owning channel — none of these is platform-level and none is left `Guid.Empty`. In particular `UsageQuotaExceededEvent` and `SubscriptionTierChangedEvent` carry no broadcaster field of their own: the broadcaster identity rides entirely on the inherited `BroadcasterId`, which the publisher always populates.

```csharp
namespace NomNomzBot.Domain.Events.Billing;

// Tier changed (upgrade, downgrade, or invite/admin grant). Drives Channels.BillingTierKey sync,
// feature re-gate, quota-window reset evaluation, dashboard refresh.
public sealed record SubscriptionTierChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required string FromTierKey { get; init; }   // "" when newly created
    public required string ToTierKey { get; init; }
    public required string Status { get; init; }        // active|trialing|past_due|canceled|incomplete
    public required bool IsInviteOnlyGrant { get; init; }
}

// Subscription Status transition (Stripe webhook or grace/trial timer). Carries old→new status.
public sealed record SubscriptionStatusChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public DateTimeOffset? GracePeriodEndsAt { get; init; }
    public DateTimeOffset? TrialEndsAt { get; init; }
}

// Subscription canceled (immediate or at-period-end). Distinct from status change for billing analytics/dunning.
public sealed record SubscriptionCanceledEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required bool AtPeriodEnd { get; init; }
    public DateTimeOffset? EffectiveAt { get; init; }   // CurrentPeriodEnd when AtPeriodEnd, else now
}

// A metered usage counter crossed its tier limit for the current period.
// Listeners surface a soft-warning (approaching) or enforce (exceeded).
public sealed record UsageQuotaExceededEvent : DomainEventBase
{
    public required string MetricKey { get; init; }     // matches TierLimit.LimitKey
    public required long Used { get; init; }
    public required long Limit { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
}

// Invoice synced from Stripe and persisted/updated (paid/failed history for the billing page).
public sealed record InvoicePaymentRecordedEvent : DomainEventBase
{
    public required Guid InvoiceId { get; init; }
    public required string Status { get; init; }        // draft|open|paid|void|uncollectible
    public required int AmountPaidCents { get; init; }
    public required string Currency { get; init; }
}

// Invite code redeemed (badge and/or tier granted). BroadcasterId = redeemer's channel.
public sealed record InviteCodeRedeemedEvent : DomainEventBase
{
    public required Guid InviteCodeId { get; init; }
    public required string Code { get; init; }
    public required bool GrantedFoundersBadge { get; init; }
    public Guid? GrantedTierId { get; init; }
}

// Founders badge granted (invite redemption or admin grant). Drives Channels.IsFounder sync + cosmetic display.
public sealed record FoundersBadgeGrantedEvent : DomainEventBase
{
    public required Guid FoundersBadgeId { get; init; }
    public string? InviteCode { get; init; }
}
```

---

## 3. Service interfaces

Four interfaces, single-responsibility. All in `NomNomzBot.Application.Contracts.Billing`. Implementations in `NomNomzBot.Infrastructure.Services.Billing` (scoped). Every method async, returns `Result`/`Result<T>`. `broadcasterId` is `Guid` per the locked tenant-key widening.

### 3.1 `ISubscriptionService` — subscription lifecycle (tenant)

```csharp
namespace NomNomzBot.Application.Contracts.Billing;

public interface ISubscriptionService
{
    // Reads the tenant's single Subscription (or a synthesized free-tier view when none exists / self-host).
    // No mutation. NOT_FOUND only if the channel itself is missing.
    Task<Result<SubscriptionDto>> GetSubscriptionAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Paginated paid/failed invoice history for the tenant (Invoice rows, IssuedAt-descending) — drives the
    // billing page invoice list. No mutation. Self-host returns an empty page.
    Task<Result<PagedList<InvoiceDto>>> ListInvoicesAsync(
        Guid broadcasterId, PaginationParams pagination, CancellationToken ct = default);

    // Begins a checkout for a target tier. SaaS only. Creates/links a Stripe Customer, returns a hosted
    // Checkout Session URL; does NOT activate the tier (webhook does). VALIDATION_FAILED on unknown/free target.
    Task<Result<CheckoutSessionDto>> StartCheckoutAsync(
        Guid broadcasterId, StartCheckoutRequest request, CancellationToken ct = default);

    // Switches tier immediately (proration via Stripe) or schedules at period end. Persists Subscription,
    // updates Channels.BillingTierKey, publishes SubscriptionTierChangedEvent. SaaS only.
    Task<Result<SubscriptionDto>> ChangeTierAsync(
        Guid broadcasterId, ChangeTierRequest request, CancellationToken ct = default);

    // Cancels (immediate or at-period-end). Sets CancelAtPeriodEnd/CanceledAt, publishes SubscriptionCanceledEvent.
    Task<Result<SubscriptionDto>> CancelAsync(
        Guid broadcasterId, CancelSubscriptionRequest request, CancellationToken ct = default);

    // Reverses a pending at-period-end cancellation. ALREADY_EXISTS-style no-op safe; VALIDATION_FAILED if not pending-cancel.
    Task<Result<SubscriptionDto>> ResumeAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Returns a short-lived Stripe Billing Portal URL for self-serve payment-method/invoice management. SaaS only.
    Task<Result<BillingPortalDto>> CreateBillingPortalSessionAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Applies an inbound, signature-verified Stripe event (subscription.updated/deleted, customer.subscription.*).
    // Upserts Subscription state, drives trial/grace transitions, publishes SubscriptionStatus/TierChanged/Canceled.
    // Idempotent by Stripe event id. Called by the webhook controller, never by UI.
    Task<Result> ApplyStripeSubscriptionEventAsync(
        StripeSubscriptionEventDto stripeEvent, CancellationToken ct = default);

    // Applies an inbound, signature-verified Stripe invoice event (invoice.paid/invoice.payment_failed/invoice.*).
    // Upserts the Invoice row (matched by StripeInvoiceId, resolved to the tenant via StripeSubscriptionId/StripeCustomerId),
    // publishes InvoicePaymentRecordedEvent. Idempotent by Stripe event id. Called by the webhook controller, never by UI.
    Task<Result> ApplyStripeInvoiceEventAsync(
        StripeInvoiceEventDto stripeEvent, CancellationToken ct = default);

    // Admin/invite grant path: assigns a tier WITHOUT Stripe (IsInviteOnlyGrant=true). Used by invite redemption
    // and platform admins. Publishes SubscriptionTierChangedEvent.
    Task<Result<SubscriptionDto>> GrantTierAsync(
        Guid broadcasterId, Guid tierId, bool isInviteOnlyGrant, CancellationToken ct = default);
}
```

### 3.2 `IBillingTierService` — tier + limit catalog + entitlement resolution (global + tenant read)

```csharp
namespace NomNomzBot.Application.Contracts.Billing;

public interface IBillingTierService
{
    // Public tier catalog (IsPublic, ordered by SortOrder) with limits — drives the pricing/upgrade UI.
    Task<Result<IReadOnlyList<TierDto>>> GetPublicTiersAsync(CancellationToken ct = default);

    // Resolves a tenant's effective entitlement: active tier key, commercial flags, and the full LimitKey→value map.
    // Self-host returns the unlimited/founder profile. This is the single source feature-gating + quota checks read.
    Task<Result<EntitlementDto>> GetEntitlementAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Resolves one limit value for a tenant (-1 = unlimited). Convenience over GetEntitlementAsync for hot paths.
    Task<Result<long>> GetLimitAsync(
        Guid broadcasterId, string limitKey, CancellationToken ct = default);

    // True iff the tenant's active tier Key is >= the required tier Key in SortOrder ranking. Backs FeatureFlag.MinTierId.
    Task<Result<bool>> IsTierAtLeastAsync(
        Guid broadcasterId, string requiredTierKey, CancellationToken ct = default);
}
```

### 3.3 `IUsageMeteringService` — cost-driver counters + quota enforcement (tenant, append-only)

```csharp
namespace NomNomzBot.Application.Contracts.Billing;

public interface IUsageMeteringService
{
    // Increments the current-period UsageRecord for (broadcaster, metricKey) by quantity (append/accumulate under
    // the (BroadcasterId, MetricKey, PeriodStart) unique row). Publishes UsageQuotaExceededEvent on first crossing.
    // Self-host = no-op success. quantity must be > 0 (VALIDATION_FAILED otherwise).
    Task<Result> RecordAsync(
        Guid broadcasterId, string metricKey, long quantity, CancellationToken ct = default);

    // Pre-flight quota check WITHOUT incrementing. Returns Allowed=false + Remaining when used+requested > limit.
    // Returns QUOTA_EXCEEDED via the DTO (not a failed Result) so callers branch on data, not error strings.
    Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId, string metricKey, long requestedQuantity, CancellationToken ct = default);

    // Current-period usage snapshot across every metered key vs the tenant's limits — drives the usage widget.
    Task<Result<IReadOnlyList<UsageMetricDto>>> GetCurrentUsageAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Flushes unreported UsageRecords to Stripe metered billing and stamps ReportedToStripe=true.
    // Called by the metering background job (SaaS); idempotent. Returns count reported.
    Task<Result<int>> ReportUnbilledUsageToStripeAsync(CancellationToken ct = default);
}
```

### 3.4 `IInviteCodeService` — invite codes + founders badge (global codes, tenant grants)

```csharp
namespace NomNomzBot.Application.Contracts.Billing;

public interface IInviteCodeService
{
    // Validates a code without consuming it (exists, not expired, RedemptionCount < MaxRedemptions).
    // Returns the would-be grants for UI preview. NOT_FOUND on unknown code.
    Task<Result<InviteCodeValidationDto>> ValidateAsync(
        string code, CancellationToken ct = default);

    // Redeems a code for the calling tenant: atomically increments RedemptionCount (guarded against over-redeem),
    // grants the founders badge (if GrantsFoundersBadge) and/or tier (if GrantsTierId, via ISubscriptionService.GrantTierAsync),
    // publishes InviteCodeRedeemedEvent (+ FoundersBadgeGrantedEvent). ALREADY_EXISTS if this tenant already redeemed it;
    // RATE_LIMITED/VALIDATION_FAILED if exhausted/expired.
    Task<Result<RedeemInviteCodeResultDto>> RedeemAsync(
        Guid broadcasterId, string code, CancellationToken ct = default);

    // Reads the tenant's founders badge (or null DTO when none). No mutation.
    Task<Result<FoundersBadgeDto?>> GetFoundersBadgeAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // ── Platform-admin (Plane-C) operations ──
    // Creates an invite code. Returns the generated/persisted code. VALIDATION_FAILED on bad maxRedemptions/tier.
    Task<Result<InviteCodeDto>> CreateInviteCodeAsync(
        CreateInviteCodeRequest request, CancellationToken ct = default);

    // Paginated invite-code list with live redemption counts for the admin console.
    Task<Result<PagedList<InviteCodeDto>>> ListInviteCodesAsync(
        PaginationParams pagination, CancellationToken ct = default);

    // Expires a code now (sets ExpiresAt) so it can no longer be redeemed; existing grants are untouched.
    Task<Result> RevokeInviteCodeAsync(
        Guid inviteCodeId, CancellationToken ct = default);

    // Admin-grants a founders badge directly (no invite). Publishes FoundersBadgeGrantedEvent, syncs Channels.IsFounder.
    Task<Result<FoundersBadgeDto>> GrantFoundersBadgeAsync(
        Guid broadcasterId, CancellationToken ct = default);
}
```

---

## 4. DTOs / contracts

All in `NomNomzBot.Application.DTOs.Billing` (records, Newtonsoft.Json-serialized). Money is integer cents.

```csharp
namespace NomNomzBot.Application.DTOs.Billing;

// ── Responses ──
public record SubscriptionDto(
    Guid Id, Guid BroadcasterId, string TierKey, string TierDisplayName,
    string Status, bool CancelAtPeriodEnd, DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt, DateTimeOffset? GracePeriodEndsAt,
    bool IsInviteOnlyGrant, bool AllowsCustomBotName, bool PrioritySupport);

public record TierLimitDto(string LimitKey, long LimitValue);          // -1 = unlimited

public record TierDto(
    Guid Id, string Key, string DisplayName, int PriceCents, string Currency,
    bool AllowsCustomBotName, bool PrioritySupport, int SortOrder,
    IReadOnlyList<TierLimitDto> Limits);

public record EntitlementDto(
    string TierKey, bool AllowsCustomBotName, bool PrioritySupport,
    IReadOnlyDictionary<string, long> Limits);                         // LimitKey → value (-1 unlimited)

public record CheckoutSessionDto(string CheckoutUrl, string StripeSessionId);
public record BillingPortalDto(string PortalUrl);

public record QuotaCheckDto(bool Allowed, string MetricKey, long Used, long Limit, long Remaining);
public record UsageMetricDto(
    string MetricKey, long Used, long Limit, long Remaining,
    DateTimeOffset PeriodStart, DateTimeOffset PeriodEnd);

public record InvoiceDto(
    Guid Id, string? Number, string Status, int AmountDueCents, int AmountPaidCents,
    string Currency, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd,
    DateTimeOffset IssuedAt, DateTimeOffset? PaidAt, string? HostedInvoiceUrl);

public record FoundersBadgeDto(Guid Id, DateTimeOffset GrantedAt, bool IsActive, string? InviteCode);

public record InviteCodeDto(
    Guid Id, string Code, int MaxRedemptions, int RedemptionCount,
    bool GrantsFoundersBadge, Guid? GrantsTierId, string? GrantsTierKey, DateTimeOffset? ExpiresAt);

public record InviteCodeValidationDto(
    bool IsValid, string Code, bool GrantsFoundersBadge, string? GrantsTierKey,
    int RemainingRedemptions, DateTimeOffset? ExpiresAt);

public record RedeemInviteCodeResultDto(
    bool GrantedFoundersBadge, string? GrantedTierKey, FoundersBadgeDto? FoundersBadge);

// ── Requests ──
public record StartCheckoutRequest(string TierKey, string? SuccessUrl, string? CancelUrl);
public record ChangeTierRequest(string TierKey, bool AtPeriodEnd);
public record CancelSubscriptionRequest(bool AtPeriodEnd, string? Reason);
public record CreateInviteCodeRequest(
    int MaxRedemptions, bool GrantsFoundersBadge, Guid? GrantsTierId, DateTimeOffset? ExpiresAt);

// ── Inbound integration (webhook → service, not exposed as a request body schema) ──
public record StripeSubscriptionEventDto(
    string StripeEventId, string EventType, string StripeCustomerId, string StripeSubscriptionId,
    string? StripePriceId, string Status, DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd, DateTimeOffset? TrialEnd, bool CancelAtPeriodEnd);

public record StripeInvoiceEventDto(
    string StripeEventId, string EventType, string StripeInvoiceId, string StripeCustomerId,
    string? StripeSubscriptionId, string? Number, string Status, int AmountDueCents, int AmountPaidCents,
    string Currency, DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd,
    DateTimeOffset IssuedAt, DateTimeOffset? PaidAt, string? HostedInvoiceUrl);
```

---

## 5. Controller endpoints

All under `api/v1/`. Tenant endpoints (`BillingController`, §5.1) sit on the **management plane**; platform-admin endpoints (`AdminBillingController`, §5.3) sit on the **platform IAM plane (Plane C)**.

**Role gate** — **Gate 1** = `[Authorize]` + tenant resolution (`ICurrentTenantService`/`IChannelAccessService`) is entry only (any management level ≥ Moderator); it cannot distinguish the per-route floor. **Gate 2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)` enforces the per-route floor named in the gate column **before** the service call, returning `FORBIDDEN` (403) when the caller's resolved level is below the floor (owner-level billing control — mods cannot change billing). **Plane-C rows** (§5.3) are enforced by `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, targetBroadcasterId, …)`; the ASP.NET `[Authorize(Policy = "<key>")]` policy name **is** the permission key verbatim (`billing:read`/`billing:refund`/`iam:manage`, replacing the legacy `[Authorize(Roles = "admin")]` gate). Floors are seeded global `ActionDefinitions` (schema B.3 / Domain C); a broadcaster may raise a floor via `ChannelActionOverride` but never below the seeded `FloorLevel`.

### 5.1 `BillingController` — tenant self-serve

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/channels/{channelId}/billing")] [Authorize] [Tags("Billing")]`

**All billing endpoints — reads included — are Broadcaster-floor.** Billing is owner-level control: only the channel owner sees subscription/usage/invoice state or mutates it; mods/editors never read or write billing.

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/subscription` | — | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster |
| GET | `/tiers` | — | `StatusResponseDto<List<TierDto>>` | management / Broadcaster (public catalog) |
| GET | `/entitlement` | — | `StatusResponseDto<EntitlementDto>` | management / Broadcaster |
| GET | `/usage` | — | `StatusResponseDto<List<UsageMetricDto>>` | management / Broadcaster |
| GET | `/invoices` | `[FromQuery] PageRequestDto` | `PaginatedResponse<InvoiceDto>` | management / Broadcaster |
| POST | `/checkout` | `StartCheckoutRequest` | `StatusResponseDto<CheckoutSessionDto>` | management / Broadcaster |
| POST | `/change-tier` | `ChangeTierRequest` | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster |
| POST | `/cancel` | `CancelSubscriptionRequest` | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster |
| POST | `/resume` | — | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster |
| POST | `/portal` | — | `StatusResponseDto<BillingPortalDto>` | management / Broadcaster |
| GET | `/founders-badge` | — | `StatusResponseDto<FoundersBadgeDto?>` | management / Broadcaster |
| POST | `/invite/validate` | `{ "code": string }` | `StatusResponseDto<InviteCodeValidationDto>` | management / Broadcaster |
| POST | `/invite/redeem` | `{ "code": string }` | `StatusResponseDto<RedeemInviteCodeResultDto>` | management / Broadcaster |

### 5.2 `BillingWebhookController` — Stripe inbound

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/billing/webhooks/stripe")] [AllowAnonymous] [Tags("Billing")]`

| Verb | Route | Request | Response | Auth |
|---|---|---|---|---|
| POST | `/` | raw body + `Stripe-Signature` header | `StatusResponseDto<object>` (200 ack) | **Anonymous**; authenticated by HMAC signature verification against the Stripe webhook secret (in-box `HMACSHA256`, constant-time compare). Invalid signature → 400. Routes by event type: `customer.subscription.*` → `StripeSubscriptionEventDto` → `ISubscriptionService.ApplyStripeSubscriptionEventAsync`; `invoice.*` (`invoice.paid`/`invoice.payment_failed`) → `StripeInvoiceEventDto` → `ISubscriptionService.ApplyStripeInvoiceEventAsync` (Invoice upsert). Idempotent by `StripeEventId`. |

### 5.3 `AdminBillingController` — platform admin (Plane C)

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/admin/billing")] [Authorize] [Tags("Admin")]`

Plane-C IAM gate per the §5 **Role gate** preamble (`AuthorizePlatformAsync` + `[Authorize(Policy = "<key>")]` where the policy name is the permission key verbatim). The `targetBroadcasterId` passed to `AuthorizePlatformAsync` is the route `{broadcasterId}` on the channel-scoped grant actions and `null` for the platform-global invite-code actions. Reads gate on `billing:read`; refund/credit-style grants gate on `billing:refund`; plan/tier and platform-IAM administration gate on `iam:manage`.

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/invites` | `[FromQuery] PageRequestDto` | `PaginatedResponse<InviteCodeDto>` | platform · `billing:read` |
| POST | `/invites` | `CreateInviteCodeRequest` | `StatusResponseDto<InviteCodeDto>` | platform · `iam:manage` |
| POST | `/invites/{inviteCodeId}/revoke` | — | `StatusResponseDto<object>` | platform · `iam:manage` |
| POST | `/channels/{broadcasterId}/grant-tier` | `{ "tierId": guid, "isInviteOnlyGrant": bool }` | `StatusResponseDto<SubscriptionDto>` | platform · `iam:manage` |
| POST | `/channels/{broadcasterId}/grant-founder` | — | `StatusResponseDto<FoundersBadgeDto>` | platform · `iam:manage` |

---

## 6. Pipeline actions

One action — lets pipelines/commands branch on entitlement (e.g. premium-only command). Implements the **single canonical `ICommandAction`** owned by `commands-pipelines.md` §3.13 (`string Type` + `Category`/`Description`; `Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken ct)`; config DTO from `Newtonsoft.Json`); registered in `InfrastructureServiceExtensions`. Reads only — never mutates billing.

| Field | Value |
|---|---|
| Class | `RequireTierAction : ICommandAction` in `NomNomzBot.Infrastructure/Pipeline/Actions/RequireTierAction.cs` |
| `Type` | `"require_tier"` |
| Config DTO | `RequireTierActionConfig` (in `NomNomzBot.Application.Contracts.Pipeline`): `record RequireTierActionConfig(string MinTierKey, string? DeniedMessage);` |
| Behavior | Resolves the channel's entitlement via `IBillingTierService.IsTierAtLeastAsync(broadcasterId, MinTierKey)`. If satisfied, continue. If not, **fail-closed**: stop the pipeline (like `StopAction`), optionally send `DeniedMessage`. Self-host always satisfies (unlimited profile). No quota increment, no events. |

No metering action is exposed to user pipelines — `sandbox_exec_ms` etc. are metered by the engine/host via `IUsageMeteringService.RecordAsync`, not by author-controlled blocks.

---

## 7. DI registration

`NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`), scoped (they consume `IApplicationDbContext`/`IUnitOfWork`):

```csharp
services.AddScoped<ISubscriptionService, SubscriptionService>();
services.AddScoped<IBillingTierService, BillingTierService>();
services.AddScoped<IUsageMeteringService, UsageMeteringService>();
services.AddScoped<IInviteCodeService, InviteCodeService>();

// Pipeline action (transient — stateless), beside the other ICommandAction registrations
services.AddTransient<ICommandAction, RequireTierAction>();
```

`IApplicationDbContext` + `AppDbContext` gain the seven `DbSet`s (§1). EF configurations in `NomNomzBot.Infrastructure/Persistence/Configurations/Billing/` (one `IEntityTypeConfiguration<T>` per entity): `BillingTier`/`TierLimit`/`InviteCode` configured WITHOUT the tenant filter (global); `Subscription`/`Invoice`/`UsageRecord`/`FoundersBadge` get `BroadcasterId` + tenant filter; `Subscription` also the soft-delete filter; `[VC:enum]` converters on all status/key text columns; `[PII-shred]` cipher columns mapped as `text`. The cipher columns (`StripeCustomerIdCipher`, `BillingEmailCipher`) encrypt/decrypt at the service layer (never in EF) through `IFieldCipher` (AES-256-GCM AEAD) composed over `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` — the field vault owned by `gdpr-crypto.md` §3.2/§3.4 that **replaces** the retired AES-CBC `IEncryptionService` (which could not crypto-shred). They are keyed by the per-subject DEK from `ISubjectKeyService`: `Subscription.SubjectKeyId guid? FK→CryptoKey` (schema N.3) names the DEK, and destroying it crypto-shreds the customer/email ciphertext. This matches how identity-auth and discord encrypt OAuth tokens via the same field vault. This subsystem only **consumes** `IFieldCipher`/`ISubjectKeyService`; both are registered by `gdpr-crypto.md` §7, not here.

**Deployment-profile adapters (chosen by DI):**

- **Billing provider.** `IStripeGateway` (Infrastructure) wraps all Stripe HTTP/SDK calls behind one interface so the data-plane services stay testable and self-host carries zero Stripe dependency.
  - SaaS (`DeploymentProfile.Mode == saas`): `StripeGateway` (real Stripe API via `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`, hand-rolled thin client — no heavy SDK unless it earns its place).
  - self-host (`self_host_lite`/`self_host_full`): `NullBillingGateway` — checkout/portal/webhook are no-ops; every channel resolves to the **free/founder unlimited entitlement**; `IUsageMeteringService.RecordAsync` is a no-op; founders badge available (self-host = free, forever).
- **Metering report job.** `UsageBillingReportService : BackgroundService` (`PeriodicTimer`) registered **only in the SaaS profile**, guarded by `IRunOnceGuard` (no-op on lite) to prevent multi-instance double-report. Calls `IUsageMeteringService.ReportUnbilledUsageToStripeAsync`.
- The cipher columns ride the existing field-vault adapter (`local_aes` vs `kms_envelope`, profile-selected by `gdpr-crypto.md`) via `ISubjectKeyService` + `IFieldCipher` — no separate billing adapter.

Seed: `BillingTier` rows seeded by `DataSeeder` (global reference data), mirroring the existing TTS-voice/permission-preset seeding path. Three **public** hosted plans are seeded — `base` ($3.99, `IsPublic=true`), `pro` ($7.99, `IsPublic=true`), `premium` ($14.99, `IsPublic=true`) — plus the **non-public** `free` marker row (`IsPublic=false`, `PriceCents=0`) used solely as the self-host / unbilled tag; `free` is never surfaced as a cloud plan. Per-tier `TierLimit` rows are seeded for `base`/`pro`/`premium` only; **self-host receives no `TierLimit` rows — its unlimited entitlement resolves every limit to `-1`**. The authoring-count `TierLimit` rows of §8 (`response_variations_per_trigger`, `custom_commands`, `timers`, `event_responses`) are seeded in the same `DataSeeder` pass alongside the cost-driver rows, for the same `base`/`pro`/`premium` set.

---

## 8. Authoring-count quotas (the `TierLimit` count levers)

The `TierLimit` mechanism (N.2) already meters cost-driver volumes (`sandbox_exec_ms`, `widget_count`, …). The same mechanism — same `(TierId, LimitKey)` row, same `LimitValue bigint` where `-1`=unlimited, same `IBillingService.GetEntitlementAsync` LimitKey→value map — also caps the **count** of author-created content. No new infrastructure: these are additional `LimitKey` values, read through the existing entitlement resolver.

### 8.1 The principle — meter quantity, never expressiveness

**These quotas meter quantity (how many variations / triggers a streamer may configure), never expressiveness.** The full template language — `{{if.*}}` conditionals, nesting, pronouns, every namespaced variable, and the `random_response` action — is available to **all** tiers, including the `base` entry tier and self-host. Only the *volume* of authored content is tiered; the bot's personality is never gated. This protects the product's core value: a `base`-tier streamer's commands are exactly as expressive as a premium streamer's — they may simply have fewer of them.

### 8.2 The count `LimitKey`s (added to the N.2 documented set)

| `LimitKey` | Meters | Counted against |
|---|---|---|
| `response_variations_per_trigger` | per-trigger cap on the number of response variations a single trigger holds | `Command.TemplateResponses` length; the `random_response` action's `Messages` length on an event-response pipeline; the equivalent variation list on a channel-point reward-redemption response (which routes through an `EventResponse` / `random_response`) |
| `custom_commands` | total authored (non-built-in) `Command` rows per tenant | live `Commands` count (`IsPlatform=false`, not soft-deleted) |
| `timers` | total `Timer` rows per tenant | live `Timers` count (not soft-deleted) |
| `event_responses` | total configured `EventResponse` triggers per tenant | live `EventResponses` count (not soft-deleted) |

`response_variations_per_trigger` is a **per-trigger** cap (each command / event-response / reward holds up to N variations); the other three are **per-tenant breadth** caps (how many triggers of each kind exist). All four are ordinary N.2 rows; `LimitValue = -1` = unlimited.

### 8.3 Self-host is always unlimited

For `DeploymentProfile.Mode = self_host_lite` / `self_host_full`, `IBillingTierService.GetEntitlementAsync` resolves **every** limit — these four included — to the self-host/founder unlimited profile, i.e. `-1` (unlimited). The same enforcement code (§8.4) runs on self-host; it simply always passes because `LimitValue == -1`. There is no self-host billing and no self-host metering — the check is a no-op-equivalent, not a separate code path.

### 8.4 Add-time enforcement (where it is wired)

Enforcement is **add-time only** — checked when a new variation or a new trigger is about to be persisted, and **never silently truncates**. Each authoring service reads the tenant entitlement through `IBillingTierService.GetEntitlementAsync` (the single tier-aware source — `IFeatureGateService` is **not** forked) and compares the relevant `LimitValue` to the current count:

```csharp
// Shared shape, applied in each create/update path before SaveChangesAsync.
// (Hot paths may use IBillingTierService.GetLimitAsync(broadcasterId, key) instead of the full entitlement.)
var limit = entitlement.Limits[limitKey];            // -1 = unlimited (always so on self-host)
if (limit != -1 && currentCount >= limit)
    return Result.Failure(
        "tier_limit_reached",
        // upsell payload: which key, the cap, and the tenant's current tier
        new { LimitKey = limitKey, Limit = limit, CurrentTier = entitlement.TierKey, Current = currentCount });
```

| Service (owner spec) | Method(s) | Key checked | When |
|---|---|---|---|
| `ICommandService` (`commands-pipelines.md` §3.1) | `CreateAsync` (new command), `CreateAsync`/`UpdateAsync` (variation list) | `custom_commands` on create; `response_variations_per_trigger` on the `TemplateResponses` length | before persisting a new `Command` row / before persisting a longer variation list |
| `IEventResponseService` (`commands-pipelines.md` §3.8) | `UpsertAsync` | `event_responses` when creating a new `(BroadcasterId, EventType)` trigger; `response_variations_per_trigger` on the response's variation list (`random_response.Messages`) | before persisting a new trigger / longer variation list |
| `ITimerService` (`commands-pipelines.md` §3.7) | `CreateAsync` | `timers` | before persisting a new `Timer` row |
| Reward-redemption response (authored as an `EventResponse` keyed on `channel.channel_points_custom_reward_redemption.add`) | flows through `IEventResponseService.UpsertAsync` | `response_variations_per_trigger` on the reward's response variation list | as event-response above — rewards do **not** own a separate variation entity (the `Rewards` table is a Twitch mirror only) |

The failure code is **`tier_limit_reached`** with the upsell payload above. `BaseController.ResultResponse` maps it onto the existing 403 arm alongside `BILLING_LIMIT`/`QUOTA_EXCEEDED` (a tier cap is a billing limit, not a validation error), so the dashboard can render an in-context upgrade prompt.

### 8.5 Grandfather on downgrade

These caps gate **adding**, never **keeping**. If a tenant's tier drops below current usage (a `premium`/`pro`→`base` downgrade, or a lapse to the `base` entry tier on non-payment), existing over-limit variations and triggers are **not deleted and continue to fire** — a command with 80 variations authored on `pro` still picks randomly across all 80 after a drop to `base`. Only *new* additions are blocked (`tier_limit_reached`) until the tenant is back under the cap for that key. The enforcement check compares against the cap **only on the add path**; it never sweeps or truncates existing rows.

### 8.6 Seeded indicative values

Seeded by `DataSeeder` alongside the other `TierLimit` rows. These are the shipped seed values — pure data with no schema or interface impact, re-tunable by editing the seed at any time (§10 decision 2 governs them alongside the cost-driver limits).

| `LimitKey` | base | pro | premium | self-host |
|---|---|---|---|---|
| `response_variations_per_trigger` | 15 | 40 | 100 | -1 |
| `custom_commands` | 100 | 400 | 1500 | -1 |
| `timers` | 20 | 60 | 200 | -1 |
| `event_responses` | 40 | 120 | 400 | -1 |
| `tts_max_characters` | 500 | 2000 | 8000 | -1 |

(`base` is the hosted entry tier; there is no hosted free column because there is no free hosted plan. Self-host receives no seeded rows for these keys at all — the profile's unlimited entitlement resolves them to `-1` regardless; the column is shown only to make the resolved value explicit.)

`tts_max_characters` is a **cost-driver** limit (per-message TTS character ceiling), not an authoring-count lever — it lives here only because §8.6 is the single per-tier seeded-value table. It is seeded for the same `base`/`pro`/`premium` set as the other cost-driver/count rows, with a safe `base` baseline plus tier-scaled headroom (`500`/`2000`/`8000`) that stays within the absolute 8000-character ceiling; self-host resolves it to `-1` (the per-message limit is unbounded, so the engine applies the 8000 hard ceiling directly).

---

## 9. Dependencies (from the stack doc)

- **In-box / 2nd-party only for the data plane:** `Microsoft.EntityFrameworkCore` 10.0.9 (+ Npgsql/Sqlite providers via the profile adapter); `Microsoft.Extensions.Http.Resilience` 10.7.0 (Stripe `HttpClient` retry/breaker — do not hand-roll); in-box `System.Security.Cryptography` `HMACSHA256` + `CryptographicOperations.FixedTimeEquals` for Stripe webhook signature verification; `IFieldCipher` (AES-256-GCM AEAD) over `ISubjectKeyService` (the crypto-shred-capable field vault from `gdpr-crypto.md` §3.2/§3.4, keyed by `Subscription.SubjectKeyId FK→CryptoKey`) for the `[PII-shred]` cipher columns — consumed, not registered here, and superseding the retired `IEncryptionService`; `Newtonsoft.Json` for app JSON DTOs.
- **`[VC:enum]`/`[VC:JSON]` converters:** hand-rolled `ValueConverter<T,string>` per the persistence decision — no 3rd-party EF-Json package.
- **Background metering:** in-box `BackgroundService` + `PeriodicTimer`; SaaS run-once via `IRunOnceGuard` (`DistributedLock.Postgres` 1.3.1 or hand-rolled `pg_try_advisory_lock`).
- **Stripe:** thin hand-rolled `IStripeGateway` over `HttpClient` (the genuine external integration; the Stripe.net SDK is not adopted — §10 decision 1). The surface stays behind the interface so the implementation is swappable without touching any contract here. No new 3rd-party NuGet beyond what the stack doc already lists.
- **No** new dependency is introduced by this subsystem; it composes the stack doc's existing approved set.

---

## 10. Decisions (resolved)

1. **Stripe client is a thin hand-rolled `IStripeGateway` over `HttpClient`.** This is the design — consistent with the "hand-roll the Twitch/Helix client" precedent and the minimal-deps rule, with webhook-event typing/parsing done against the `StripeSubscriptionEventDto`/`StripeInvoiceEventDto` shapes (§4). The Stripe.net SDK is not adopted. Because the interface and every service signature are independent of the implementation, the `StripeGateway` impl is swappable behind `IStripeGateway` if a later, deliberate decision elevates the SDK — that swap changes no contract here.
2. **Hosted is paid-only; seeded tier prices and limits are the shipped values, tunable as data.** There is **no free hosted tier** — the cloud entry plan is `base`. `BillingTier.PriceCents` seeds the monetization figures (**$3.99/$7.99/$14.99 → `399`/`799`/`1499` cents for `base`/`pro`/`premium`**); the `free` row is `PriceCents=0`, `IsPublic=false`, and exists only as the self-host / unbilled marker (never a cloud plan). `TierLimit.LimitValue` seeds the cost-driver and §8 authoring-count numbers (`response_variations_per_trigger` 15/40/100 across `base`/`pro`/`premium`, etc.) for the three hosted tiers only — self-host gets no rows and resolves every limit to `-1`. These are reference data seeded by `DataSeeder` with no schema or interface impact, so they are re-tunable by editing the seed/data at any time without a contract change. They are flagged for owner review before go-live as a business pricing call, but coding proceeds against these concrete values now.