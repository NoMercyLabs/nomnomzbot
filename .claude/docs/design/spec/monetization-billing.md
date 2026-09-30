# Interface Specification — Monetization & Billing

**Subsystem:** SaaS subscriptions, tiers + `TierLimit` quotas, usage counters tied to cost drivers, invite codes, founders badge. **The hosted/SaaS service is paid-only — there is no free hosted tier; the only free path is self-host (unlimited).**
**Status:** Directly-implementable. Code from this first-try.
**Sources of truth:** locked schema `2026-06-16-database-schema.md` Domain N (N.1–N.7) + globals `P.12 DeploymentProfile` / `P.13 FeatureFlag`; design `2026-06-16-monetization.md`; stack `2026-06-16-stack-and-dependencies.md`; defaults `2026-06-16-decisions-resolved.md`.

## Conventions binding on this subsystem

- Namespace `NomNomzBot.*`. .NET 10 / C# 14 / EF Core 10. File-scoped namespaces, `Nullable` enabled, async all the way (never `.Result`/`.Wait`), `Result<T>` over exceptions/null.
- Surrogate PKs are `guid` via `Guid.CreateVersion7()` (UUIDv7); append-only/high-volume rows (`UsageRecord`) use `bigint` identity. Twitch/Stripe ids are indexed attribute columns. Tenant key `BroadcasterId` is `Guid` (FK→`Channels.Id`).
- Tenant-owned entities implement `ITenantScoped` (EF global filter + Postgres RLS). Soft-delete = `IsDeleted`+`DeletedAt` global filter.
- `BillingTier`, `TierLimit`, `InviteCode`, `PricedUnit` are **GLOBAL** (no `BroadcasterId`, no RLS). `Subscription`, `Invoice`, `EntitlementGrant` and `TenantLimitOverride` are tenant-owned and soft-deletable. `FoundersBadge` is tenant-scoped but NOT soft-deletable (perk persists). `UsageRecord` is append-only.
- App JSON = **Newtonsoft.Json** (project rule). Money is integer cents (`int`/`long`), never `decimal` floats. Enum-ish string columns persist as text via `[VC:enum]` converters.
- Repository + `IUnitOfWork`; controllers never touch `DbContext` raw — they call typed service interfaces. No MediatR, no Roslyn.
- Responses: `StatusResponseDto<T>` / `PaginatedResponse<T>`. Controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`.
- **Existing surface to EXTEND, not duplicate:**
  - `IFeatureGateService` (`NomNomzBot.Domain.Interfaces`) — already gates per-channel features. This subsystem adds the **tier/quota** check that feature-gating consults; do not fork it. `IBillingTierService.GetEntitlementAsync` is the tier-aware source `FeatureGateService` will call for `MinTierId` flags.
  - `Result.ErrorCode` strings already mapped in `BaseController.ResultResponse`: **`BILLING_LIMIT`** and **`FEATURE_DISABLED`** → HTTP 403; `NOT_FOUND`, `VALIDATION_FAILED`, `ALREADY_EXISTS`, `RATE_LIMITED`. Reuse these exact codes. **`QUOTA_EXCEEDED`** (already in the 403 arm) is what a limit hit returns (`Errors.QuotaExceeded`); see §8.
  - Legacy `ChannelSubscription` (int PK, `string` `BroadcasterId`) is **superseded** by `Subscription` (N.3). Do not extend the old entity; it is retired by S-RETIRE-LEGACY (until then it still exists, unused by billing).
  - Legacy `SubscriptionTier` enum (`Free/Starter/Pro/Platform`) is **viewer-Twitch-sub flavored and unrelated** — do NOT reuse for billing tiers. Billing tier keys are `free|base|pro|premium` (string, from `BillingTier.Key`). The hosted cloud plans are **`base` ($3.99), `pro` ($7.99), `premium` ($14.99)** — `base` is the hosted entry tier. The `free` key is retained **only** as the internal marker for self-host / unbilled installs; it is **never** a public cloud plan (`IsPublic=false`), is never offered at SaaS signup, and a SaaS signup can never land on it. Self-host is the only free path: every `TierLimit` resolves to `-1` (unlimited) and the founders badge is available.

---

## 1. Entities (locked schema — owned by this subsystem)

Defined authoritatively in `2026-06-16-database-schema.md` Domain N. Listed here with key fields only; the schema is the contract.

| Entity | Schema | Kind | Key fields |
|---|---|---|---|
| `BillingTier` | N.1 | GLOBAL | `Id guid PK`; `Key string(20) Unique` (`free\|base\|pro\|premium`); `DisplayName string(50)`; `PriceCents int`; `Currency string(3)`; `StripePriceId string(255)?`; `StripeProductId string(255)?`; `AllowsCustomBotName bool`; `PrioritySupport bool`; `IsPublic bool`; `SortOrder int`. |
| `TierLimit` | N.2 | GLOBAL | `Id guid PK`; `TierId guid FK→BillingTier`; `LimitKey string(50)` (a `LimitedResourceRegistry` key: `custom_commands\|timers\|response_variations_per_trigger\|tts_max_characters\|sandbox_exec_ms\|sound_clip_storage_bytes\|channel_asset_storage_bytes`; rows are seeded only for the four COST_DRIVING keys, see §8); `LimitValue bigint` (`-1`=unlimited). **Unique** `(TierId, LimitKey)`. |
| `Subscription` | N.3 | tenant, soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels Unique`; `TierId guid FK→BillingTier`; `Status string(20)` (`active\|trialing\|past_due\|canceled\|incomplete`); `StripeCustomerIdCipher string(512)?` **[PII-shred]**; `StripeSubscriptionId string(255)? Index`; `BillingEmailCipher string(512)?` **[PII-shred]**; `SubjectKeyId guid?`; `CurrentPeriodStart/End timestamp?`; `TrialEndsAt timestamp?`; `GracePeriodEndsAt timestamp?`; `CancelAtPeriodEnd bool`; `CanceledAt timestamp?`; `IsInviteOnlyGrant bool`. **Unique** `(BroadcasterId)`. |
| `Invoice` | N.4 | tenant | `Id guid PK`; `BroadcasterId guid FK→Channels`; `SubscriptionId guid FK→Subscriptions`; `StripeInvoiceId string(255)? Unique`; `Number string(50)?`; `Status string(20)` (`draft\|open\|paid\|void\|uncollectible\|refunded`); `AmountDueCents int`; `AmountPaidCents int`; `AmountRefundedCents int`; `RefundedAt timestamp?`; `DueAt timestamp?` (drives the dunning state); `Currency string(3)`; `PeriodStart/End timestamp?`; `HostedInvoiceUrl string(2048)?`; `IssuedAt timestamp Index`; `PaidAt timestamp?`. **Unique** `StripeInvoiceId`. |
| `UsageRecord` | N.5 | tenant, APPEND-ONLY | `Id bigint PK`; `BroadcasterId guid FK→Channels`; `MetricKey string(50)` (matches `TierLimit.LimitKey`; covers `sandbox_exec_ms`); `Quantity bigint`; `PeriodStart timestamp Index`; `PeriodEnd timestamp`; `ReportedToStripe bool`; `CreatedAt`. **Unique** `(BroadcasterId, MetricKey, PeriodStart)`. |
| `FoundersBadge` | N.6 | tenant, NOT soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels Unique`; `GrantedAt timestamp`; `InviteCode string(50)? Index`; `IsActive bool`. **Unique** `(BroadcasterId)`. |
| `InviteCode` | N.7 | GLOBAL | `Id guid PK`; `Code string(50) Unique`; `MaxRedemptions int`; `RedemptionCount int`; `GrantsFoundersBadge bool`; `GrantsTierId guid? FK→BillingTier`; `ExpiresAt timestamp?`. **Unique** `Code`. |
| `EntitlementGrant` | S-ADMIN-4b | tenant, soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels`; `GrantedTierId guid FK→BillingTier`; `Reason string`; `IssuedAt timestamp`; `ExpiresAt timestamp` (a comp is always time-boxed); `IssuedByAdminId guid?`. Read by tier resolution, so a live grant raises the tenant's effective tier. |
| `TenantLimitOverride` | S-ADMIN-3 | tenant, soft-delete | `Id guid PK`; `BroadcasterId guid FK→Channels`; `LimitKey string` (a `LimitedResourceRegistry` key); `LimitValue bigint`; `Reason string`; `GrantedByPrincipalId guid`; `ExpiresAt timestamp?`. One live override per `(BroadcasterId, LimitKey)`; it wins over the safety baseline and the tier limit (§8.3). |
| `PricedUnit` | S-ADMIN-4d | GLOBAL, soft-delete | `Id guid PK`; `UnitKey string` (e.g. `tts_characters`); `Currency string(3)`; `PriceMinorUnitsPerBatch long`; `BatchSize long`. Ships empty: a unit with no row is unpriced, never reported at a fabricated zero cost. |

Reads-only from other subsystems (NOT owned here): `Channels.BillingTierKey` (denormalized tier copy — this subsystem **writes** it on tier change; there is no `Channels.IsFounder` column — the `FoundersBadge` row is the only source of the badge), `FeatureFlag.MinTierId`/`MinTierKey` (consumed for tier-gated flags). `DeploymentProfile.Mode` distinguishes `saas` vs `self_host_*` (self-host = unbilled and unlimited; no hosted free tier exists). `BillingTier.AllowsCustomBotName` is **true for `pro`+ only** — a custom per-channel bot identity is a Pro/Premium hosted feature; `base` uses the shared platform bot, and self-host always allows a custom bot identity.

`DbSet` additions to `IApplicationDbContext` (and `AppDbContext`): `BillingTiers`, `TierLimits`, `PricedUnits`, `TenantLimitOverrides`, `EntitlementGrants`, `Subscriptions`, `Invoices`, `UsageRecords`, `FoundersBadges`, `InviteCodes`. (The legacy `ChannelSubscriptions` set is removed when `ChannelSubscription` is retired by S-RETIRE-LEGACY.)

---

## 2. Domain events

All inherit the canonical `DomainEventBase` (`NomNomzBot.Domain.Platform`; supplies `Guid EventId` (UUIDv7), `Guid BroadcasterId`, `DateTimeOffset OccurredAt` — authoritative definition in `platform-conventions.md` §2.0). Events **do not redeclare** the inherited members (`EventId` / `BroadcasterId` / `OccurredAt`) — each event below adds only its own payload fields. Published via `IEventBus.PublishAsync`. Each is a `sealed class` with `required` init properties (the shipped style) (the moderation.md §2 events are the reference pattern). They live in `NomNomzBot.Domain/Billing/Events/BillingEvents.cs`.

Every event in this subsystem is tenant-scoped, so its publisher sets the inherited `Guid BroadcasterId` to the owning channel — none of these is platform-level and none is left `Guid.Empty`. In particular `UsageQuotaExceededEvent` and `SubscriptionTierChangedEvent` carry no broadcaster field of their own: the broadcaster identity rides entirely on the inherited `BroadcasterId`, which the publisher always populates.

```csharp
namespace NomNomzBot.Domain.Billing.Events;

// Tier changed (upgrade, downgrade, or invite/admin grant). Drives Channels.BillingTierKey sync,
// feature re-gate, quota-window reset evaluation, dashboard refresh.
public sealed class SubscriptionTierChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required string FromTierKey { get; init; }   // "" when newly created
    public required string ToTierKey { get; init; }
    public required string Status { get; init; }        // active|trialing|past_due|canceled|incomplete
    public required bool IsInviteOnlyGrant { get; init; }
}

// Subscription Status transition (Stripe webhook or grace/trial timer). Carries old→new status.
public sealed class SubscriptionStatusChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public DateTimeOffset? GracePeriodEndsAt { get; init; }
    public DateTimeOffset? TrialEndsAt { get; init; }
}

// Subscription canceled (immediate or at-period-end). Distinct from status change for billing analytics/dunning.
public sealed class SubscriptionCanceledEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required bool AtPeriodEnd { get; init; }
    public DateTimeOffset? EffectiveAt { get; init; }   // CurrentPeriodEnd when AtPeriodEnd, else now
}

// A metered usage counter crossed its tier limit for the current period.
// Listeners surface a soft-warning (approaching) or enforce (exceeded).
public sealed class UsageQuotaExceededEvent : DomainEventBase
{
    public required string MetricKey { get; init; }     // matches TierLimit.LimitKey
    public required long Used { get; init; }
    public required long Limit { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
}

// Invoice synced from Stripe and persisted/updated (paid/failed history for the billing page).
public sealed class InvoicePaymentRecordedEvent : DomainEventBase
{
    public required Guid InvoiceId { get; init; }
    public required string Status { get; init; }        // draft|open|paid|void|uncollectible
    public required int AmountPaidCents { get; init; }
    public required string Currency { get; init; }
}

// Invoice refunded in full by a platform admin (billing:refund). Amount is what Stripe actually refunded.
public sealed class InvoiceRefundedEvent : DomainEventBase
{
    public required Guid InvoiceId { get; init; }
    public required int AmountRefundedCents { get; init; }
    public required string Currency { get; init; }
}

// Invite code redeemed (badge and/or tier granted). BroadcasterId = redeemer's channel.
public sealed class InviteCodeRedeemedEvent : DomainEventBase
{
    public required Guid InviteCodeId { get; init; }
    public required string Code { get; init; }
    public required bool GrantedFoundersBadge { get; init; }
    public Guid? GrantedTierId { get; init; }
}

// Founders badge granted (invite redemption or admin grant). Drives the cosmetic badge display.
public sealed class FoundersBadgeGrantedEvent : DomainEventBase
{
    public required Guid FoundersBadgeId { get; init; }
    public string? InviteCode { get; init; }
}
```

---

## 3. Service interfaces

Interfaces are single-responsibility, all in `NomNomzBot.Application.Contracts.Billing`. Implementations are in `NomNomzBot.Infrastructure.Billing` (scoped). Every method async, returns `Result`/`Result<T>`. `broadcasterId` is `Guid` per the locked tenant-key widening.

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

    // ── Platform-admin (Plane-C) ──
    // Every invoice for one tenant, platform-wide, newest first (billing:read). NOT_FOUND on an unknown channel.
    Task<Result<IReadOnlyList<InvoiceDto>>> ListInvoicesForAdminAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // Refunds a paid invoice in full via Stripe, flips it to Refunded, records AmountRefundedCents, writes an
    // IamAuditLog row, publishes InvoiceRefundedEvent (billing:refund). VALIDATION_FAILED when the invoice has
    // no Stripe id or is not Paid (which also rejects a second refund).
    Task<Result<InvoiceDto>> RefundInvoiceAsync(
        Guid invoiceId, Guid? actorAdminId = null, CancellationToken ct = default);
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

    // Admin-grants a founders badge directly (no invite). Publishes FoundersBadgeGrantedEvent.
    Task<Result<FoundersBadgeDto>> GrantFoundersBadgeAsync(
        Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.5 `IResourceQuotaService` — the one write-path seam for every limited resource

Backs §8. NEAR_FREE keys check the registry's uniform safety baseline; COST_DRIVING keys delegate to `IBillingTierService`; a live `TenantLimitOverride` wins over both.

```csharp
public interface IResourceQuotaService
{
    // Does resultingCount (the count AFTER the write under evaluation) still fit the limit? No mutation.
    // NOT_FOUND for a key that is not in LimitedResourceRegistry.
    Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId, string limitKey, long resultingCount, CancellationToken ct = default);

    // The real current count/gauge for a key with a broadcaster-wide source (custom_commands, timers, the two
    // storage-bytes keys). The SAME number a write path uses before CheckAsync(count + 1).
    // NOT_SUPPORTED for response_variations_per_trigger (evaluated per trigger, not channel-wide).
    Task<Result<long>> GetCurrentCountAsync(
        Guid broadcasterId, string limitKey, CancellationToken ct = default);

    // Truthful usage report across every registry entry with a count source. Drives GET .../billing/limits.
    Task<Result<IReadOnlyList<ResourceUsageDto>>> GetUsageReportAsync(
        Guid broadcasterId, CancellationToken ct = default);
}
```

### 3.6 `IEntitlementGrantService` — comps (tenant, platform-admin)

```csharp
public interface IEntitlementGrantService
{
    // Every LIVE (unexpired, not soft-deleted) grant for the tenant, newest first.
    Task<Result<IReadOnlyList<EntitlementGrantDto>>> ListGrantsAsync(
        Guid broadcasterId, CancellationToken ct = default);

    // The counted diff between the tenant's current resolved entitlement and the target tier (limit keys that
    // would really change). Call before IssueGrantAsync.
    Task<Result<EntitlementGrantPreviewDto>> PreviewGrantAsync(
        Guid broadcasterId, Guid tierId, CancellationToken ct = default);

    // Issues a time-boxed comp with a mandatory reason. Recomputes the diff; PREVIEW_STALE (409) when it no longer
    // matches request.ConfirmedChangedLimitCount. Grant + IamAuditLog row persist in one transaction.
    Task<Result<EntitlementGrantDto>> IssueGrantAsync(
        Guid broadcasterId, IssueEntitlementGrantRequest request, Guid? actorUserId, CancellationToken ct = default);
}
```

### 3.7 `IBillingTierAdminService` — tier catalogue authoring (platform-admin)

```csharp
public interface IBillingTierAdminService
{
    Task<Result<IReadOnlyList<TierDto>>> ListAllTiersAsync(CancellationToken ct = default);   // public + internal
    Task<Result<TierChangePreviewDto>> PreviewTierChangeAsync(Guid tierId, CancellationToken ct = default); // tenants on the tier
    Task<Result<TierDto>> CreateTierAsync(CreateTierRequest request, Guid? actorUserId, CancellationToken ct = default);
    // PREVIEW_STALE when request.ConfirmedAffectedTenantCount no longer matches a fresh count. Audited.
    Task<Result<TierDto>> UpdateTierAsync(Guid tierId, UpdateTierRequest request, Guid? actorUserId, CancellationToken ct = default);
}
```

### 3.8 `IPricedUnitAdminService` — usage-unit pricing (platform-admin)

```csharp
public interface IPricedUnitAdminService
{
    Task<Result<IReadOnlyList<PricedUnitDto>>> ListPricedUnitsAsync(CancellationToken ct = default);
    // Upsert by UnitKey. Always writes an IamAuditLog row naming the acting operator.
    Task<Result<PricedUnitDto>> AuthorPricedUnitAsync(AuthorPricedUnitRequest request, Guid? actorUserId, CancellationToken ct = default);
}
```

Per-tenant quota exceptions (`TenantLimitOverride`) are written by `IPlatformAdminService` (`ListTenantLimitOverridesAsync` / `SetTenantLimitOverrideAsync` / `ClearTenantLimitOverrideAsync`, identity-auth spec), not by a billing service; `LimitedResourceRegistry` resolution (§8.3) reads them.

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
// One LimitedResourceRegistry entry with its real current count (GET .../billing/limits). Class is carried so a UI
// never mistakes a NEAR_FREE abuse floor for a paid ceiling.
public record ResourceUsageDto(
    string LimitKey, ResourceClass Class, string DisplayName, long CurrentCount, long Limit, long SafetyBaseline);
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

// Entitlement grants and priced units (admin). Tier authoring uses CreateTierRequest / UpdateTierRequest / TierChangePreviewDto.
public record EntitlementGrantDto(
    Guid Id, Guid BroadcasterId, Guid GrantedTierId, string GrantedTierKey, string Reason,
    DateTime ExpiresAt, DateTime IssuedAt, Guid? IssuedByAdminId);
public record EntitlementGrantPreviewDto(
    string CurrentTierKey, string GrantedTierKey, int ChangedLimitCount, IReadOnlyList<string> ChangedLimitKeys);
public record IssueEntitlementGrantRequest(
    Guid TierId, string Reason, DateTime ExpiresAt, int ConfirmedChangedLimitCount);
public record PricedUnitDto(
    Guid Id, string UnitKey, string Currency, long PriceMinorUnitsPerBatch, long BatchSize);
public record AuthorPricedUnitRequest(
    string UnitKey, string Currency, long PriceMinorUnitsPerBatch, long BatchSize);

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

All under `api/v1/`. Tenant endpoints (`BillingController`, §5.1) sit on the **management plane**; platform-admin endpoints (`AdminBillingController`, §5.3) sit on the **platform IAM plane (Plane-C)**.

**Role gate** — **Gate-1** = `[Authorize]` + tenant resolution (`ICurrentTenantService`/`IChannelAccessService`) is pure entry only (any authenticated caller, channel must exist); it cannot distinguish the per-route floor. **Gate-2** = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey, ct)` enforces the per-route floor named in the gate column **before** the service call, returning `FORBIDDEN` (403) when the caller's resolved level is below the floor (owner-level billing control — mods cannot change billing). Controllers apply it as `[RequireAction("<key>")]`. The two billing action keys are `billing:read` (every GET plus invite validation) and `billing:manage` (every state change), both seeded at the Broadcaster floor. **Plane-C rows** (§5.3) are enforced by `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, targetBroadcasterId, …)`; the ASP.NET `[Authorize(Policy = "<key>")]` policy name **is** the permission key verbatim (`billing:read`/`billing:write`/`billing:refund`, replacing the legacy `[Authorize(Roles = "admin")]` gate). Floors are seeded global `ActionDefinitions` (schema B.3 / Domain C); a broadcaster may raise a floor via `ChannelActionOverride` but never below the seeded `FloorLevel`.

### 5.1 `BillingController` — tenant self-serve

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/channels/{channelId}/billing")] [Authorize] [Tags("Billing")]`

**All billing endpoints — reads included — are Broadcaster-floor.** Billing is owner-level control: only the channel owner sees subscription/usage/invoice state or mutates it; mods/editors never read or write billing. Reads gate on action key `billing:read`, writes on `billing:manage`.

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/subscription` | — | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster · `billing:read` |
| GET | `/tiers` | — | `StatusResponseDto<List<TierDto>>` | management / Broadcaster (public catalog) · `billing:read` |
| GET | `/entitlement` | — | `StatusResponseDto<EntitlementDto>` | management / Broadcaster · `billing:read` |
| GET | `/usage` | — | `StatusResponseDto<List<UsageMetricDto>>` | management / Broadcaster · `billing:read` |
| GET | `/limits` | — | `StatusResponseDto<List<ResourceUsageDto>>` | management / Broadcaster · `billing:read` |
| GET | `/invoices` | `[FromQuery] PageRequestDto` | `PaginatedResponse<InvoiceDto>` | management / Broadcaster · `billing:read` |
| POST | `/checkout` | `StartCheckoutRequest` | `StatusResponseDto<CheckoutSessionDto>` | management / Broadcaster · `billing:manage` |
| POST | `/change-tier` | `ChangeTierRequest` | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster · `billing:manage` |
| POST | `/cancel` | `CancelSubscriptionRequest` | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster · `billing:manage` |
| POST | `/resume` | — | `StatusResponseDto<SubscriptionDto>` | management / Broadcaster · `billing:manage` |
| POST | `/portal` | — | `StatusResponseDto<BillingPortalDto>` | management / Broadcaster · `billing:manage` |
| GET | `/founders-badge` | — | `StatusResponseDto<FoundersBadgeDto?>` | management / Broadcaster · `billing:read` |
| POST | `/invite/validate` | `{ "code": string }` | `StatusResponseDto<InviteCodeValidationDto>` | management / Broadcaster · `billing:read` |
| POST | `/invite/redeem` | `{ "code": string }` | `StatusResponseDto<RedeemInviteCodeResultDto>` | management / Broadcaster · `billing:manage` |

### 5.2 `BillingWebhookController` — Stripe inbound

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/billing/webhooks/stripe")] [AllowAnonymous] [Tags("Billing")]`

| Verb | Route | Request | Response | Auth |
|---|---|---|---|---|
| POST | `/` | raw body + `Stripe-Signature` header | `StatusResponseDto<object>` (200 ack) | **Anonymous**; authenticated by HMAC signature verification against the Stripe webhook secret (in-box `HMACSHA256`, constant-time compare). Invalid signature → 400. Routes by event type: `customer.subscription.*` → `StripeSubscriptionEventDto` → `ISubscriptionService.ApplyStripeSubscriptionEventAsync`; `invoice.*` (`invoice.paid`/`invoice.payment_failed`) → `StripeInvoiceEventDto` → `ISubscriptionService.ApplyStripeInvoiceEventAsync` (Invoice upsert). Idempotent by `StripeEventId`. |

### 5.3 `AdminBillingController` — platform admin (Plane-C)

`[ApiVersion("1.0")] [Route("api/v{version:apiVersion}/admin/billing")] [Authorize] [Tags("Admin")]`

Plane-C IAM gate per the §5 **Role gate** preamble (`AuthorizePlatformAsync` + `[Authorize(Policy = "<key>")]` where the policy name is the permission key verbatim). The `targetBroadcasterId` passed to `AuthorizePlatformAsync` is the route `{broadcasterId}` on the channel-scoped grant actions and `null` for the platform-global invite-code actions. Reads gate on `billing:read`; every billing write (invite create/revoke, tier create/edit, tier/founder grants, comps, priced units) gates on `billing:write`; refunds gate on `billing:refund`. Billing never gates on `iam:manage` — the `platform-billing` role holds the billing keys and nothing else.

Per-tenant quota overrides are not on this controller: `GET/PUT /api/v1/admin/tenants/{broadcasterId}/limits` and `DELETE .../limits/{limitKey}` live on `PlatformAdminController` (`tenant:read` / `tenant:quota:manage`), and feed §8.3.

| Verb | Route | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| GET | `/invites` | `[FromQuery] PageRequestDto` | `PaginatedResponse<InviteCodeDto>` | platform · `billing:read` |
| POST | `/invites` | `CreateInviteCodeRequest` | `StatusResponseDto<InviteCodeDto>` | platform · `billing:write` |
| POST | `/invites/{inviteCodeId}/revoke` | — | `StatusResponseDto<object>` | platform · `billing:write` |
| POST | `/channels/{broadcasterId}/grant-tier` | `{ "tierId": guid, "isInviteOnlyGrant": bool }` | `StatusResponseDto<SubscriptionDto>` | platform · `billing:write` |
| POST | `/channels/{broadcasterId}/grant-founder` | — | `StatusResponseDto<FoundersBadgeDto>` | platform · `billing:write` |
| POST | `/invoices/{invoiceId}/refund` | — | `StatusResponseDto<InvoiceDto>` | platform · `billing:refund` |
| GET | `/channels/{broadcasterId}/invoices` | — | `StatusResponseDto<List<InvoiceDto>>` | platform · `billing:read` |
| GET | `/tiers` | — | `StatusResponseDto<List<TierDto>>` | platform · `billing:read` |
| GET | `/tiers/{tierId}/preview` | — | `StatusResponseDto<TierChangePreviewDto>` | platform · `billing:read` |
| POST | `/tiers` | `CreateTierRequest` | `StatusResponseDto<TierDto>` | platform · `billing:write` |
| PUT | `/tiers/{tierId}` | `UpdateTierRequest` | `StatusResponseDto<TierDto>` | platform · `billing:write` |
| GET | `/channels/{broadcasterId}/grants` | — | `StatusResponseDto<List<EntitlementGrantDto>>` | platform · `billing:read` |
| GET | `/channels/{broadcasterId}/grants/preview?tierId=` | — | `StatusResponseDto<EntitlementGrantPreviewDto>` | platform · `billing:read` |
| POST | `/channels/{broadcasterId}/grants` | `IssueEntitlementGrantRequest` | `StatusResponseDto<EntitlementGrantDto>` | platform · `billing:write` |
| GET | `/priced-units` | — | `StatusResponseDto<List<PricedUnitDto>>` | platform · `billing:read` |
| POST | `/priced-units` | `AuthorPricedUnitRequest` | `StatusResponseDto<PricedUnitDto>` | platform · `billing:write` |

---

## 6. Pipeline actions

One action — lets pipelines/commands branch on entitlement (e.g. premium-only command). Implements the **single canonical `ICommandAction`** (`NomNomzBot.Application.Abstractions.Pipeline`, owned by `commands-pipelines.md` §3.13): `string ActionType`, `LocalizedText Category`/`Description` (localization keys), `IReadOnlyList<PipelineActionFieldDescriptor> Fields`, and `Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)`. Parameters are read from `ActionDefinition` (`action.GetString(...)`); there is no config DTO. Discovered by `AddImplementationsOf<ICommandAction>` — no explicit registration. Reads only — never mutates billing.

| Field | Value |
|---|---|
| Class | `RequireTierAction : ICommandAction` in `NomNomzBot.Infrastructure/Billing/PipelineActions/RequireTierAction.cs` (namespace `NomNomzBot.Infrastructure.Billing.PipelineActions`) |
| `ActionType` | `"require_tier"` |
| Parameters | `min_tier` (required; enum `free\|base\|pro\|premium`), `denied_message` (optional text) |
| Behavior | Resolves the channel's entitlement via `IBillingTierService.IsTierAtLeastAsync(broadcasterId, min_tier)`. If satisfied, returns `ActionResult.Success()`. If not, **fail-closed**: returns `ActionResult.Failure(denied_message ?? default text)`, which stops the pipeline. Self-host always satisfies (unlimited profile). No quota increment, no events. |

No metering action is exposed to user pipelines — `sandbox_exec_ms` etc. are metered by the engine/host via `IUsageMeteringService.RecordAsync`, not by author-controlled blocks.

---

## 7. DI registration

`NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`). The `I<X>Service` implementations in `NomNomzBot.Infrastructure.Billing` (`SubscriptionService`, `BillingTierService`, `UsageMeteringService`, `InviteCodeService`, `ResourceQuotaService`, `EntitlementGrantService`, `BillingTierAdminService`, `PricedUnitAdminService`) are bound scoped by `AddServicesByConvention` (they consume `IApplicationDbContext`/`IUnitOfWork`). Types that do not match the `I<X>Service` convention are registered explicitly:

```csharp
services.AddScoped<Application.Contracts.Billing.IStripeGateway, Billing.StripeGateway>();
services.AddScoped<Application.Contracts.Billing.IStripeWebhookHandler, Billing.StripeWebhookHandler>();
// RequireTierAction is picked up by AddImplementationsOf<ICommandAction>(..., ServiceLifetime.Transient).
```

`IApplicationDbContext` + `AppDbContext` gain the ten `DbSet`s (§1). EF configurations are in `NomNomzBot.Infrastructure/Platform/Persistence/Configurations/` (one `IEntityTypeConfiguration<T>` per entity): `BillingTier`/`TierLimit`/`InviteCode`/`PricedUnit` configured WITHOUT the tenant filter (global); `Subscription`/`Invoice`/`UsageRecord`/`FoundersBadge`/`EntitlementGrant`/`TenantLimitOverride` get `BroadcasterId` + tenant filter; `Subscription`, `Invoice`, `EntitlementGrant`, `TenantLimitOverride` and `PricedUnit` also the soft-delete filter; `[VC:enum]` converters on all status/key text columns; `[PII-shred]` cipher columns mapped as `text`. The cipher columns (`StripeCustomerIdCipher`, `BillingEmailCipher`) encrypt/decrypt at the service layer (never in EF) through `IFieldCipher` (AES-256-GCM AEAD) composed over `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` — the field vault owned by `gdpr-crypto.md` §3.2/§3.4 that **replaces** the retired AES-CBC `IEncryptionService` (which could not crypto-shred). They are keyed by the per-subject DEK from `ISubjectKeyService`: `Subscription.SubjectKeyId guid? FK→CryptoKey` (schema N.3) names the DEK, and destroying it crypto-shreds the customer/email ciphertext. This matches how identity-auth and discord encrypt OAuth tokens via the same field vault. This subsystem only **consumes** `IFieldCipher`/`ISubjectKeyService`; both are registered by `gdpr-crypto.md` §7, not here.

**Deployment-profile adapters (chosen by DI):**

- **Billing provider.** `IStripeGateway` (Infrastructure) wraps all Stripe HTTP/SDK calls behind one interface so the data-plane services stay testable and self-host carries zero Stripe dependency.
  - SaaS (`DeploymentProfile.Mode == saas`): `StripeGateway` (real Stripe API via `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience`, hand-rolled thin client — no heavy SDK unless it earns its place).
  - self-host (`self_host_lite`/`self_host_full`): `NullBillingGateway` — checkout/portal/webhook are no-ops; every channel resolves to the **free/founder unlimited entitlement**; `IUsageMeteringService.RecordAsync` is a no-op; founders badge available (self-host = free, forever).
- **Metering report job.** `UsageBillingReportService : BackgroundService` (`PeriodicTimer`) registered **only in the SaaS profile**, guarded by `IRunOnceGuard` (no-op on lite) to prevent multi-instance double-report. Calls `IUsageMeteringService.ReportUnbilledUsageToStripeAsync`.
- The cipher columns ride the existing field-vault adapter (`local_aes` vs `kms_envelope`, profile-selected by `gdpr-crypto.md`) via `ISubjectKeyService` + `IFieldCipher` — no separate billing adapter.

Seed: `BillingTierSeeder` (`NomNomzBot.Infrastructure/Content/Billing/`, an `ISeeder`) seeds the global reference data. Three **public** hosted plans are seeded — `base` ($3.99, `IsPublic=true`), `pro` ($7.99, `IsPublic=true`), `premium` ($14.99, `IsPublic=true`) — plus the **non-public** `free` marker row (`IsPublic=false`, `PriceCents=0`) used solely as the self-host / unbilled tag; `free` is never surfaced as a cloud plan. `TierLimit` rows are seeded for `base`/`pro`/`premium` for the four COST_DRIVING registry keys only (`tts_max_characters`, `sandbox_exec_ms`, `sound_clip_storage_bytes`, `channel_asset_storage_bytes`); the seeder also backfills any COST_DRIVING key added to the catalogue later. NEAR_FREE keys have no `TierLimit` rows — their value is the registry safety baseline (§8). **Self-host receives no `TierLimit` rows — its unlimited entitlement resolves every COST_DRIVING limit to `-1`.**

---

## 8. Limited resources (`LimitedResourceRegistry`)

Every capped resource is declared once in `LimitedResourceRegistry` (`NomNomzBot.Application.Contracts.Billing`). A registry entry is a `LimitedResourceDescriptor(LimitKey, Class, DisplayName, SafetyBaseline)`. The `Class` decides where the limit comes from. Every write path checks it through `IResourceQuotaService` (§3.5) — never by reading `TierLimit` directly.

### 8.1 The principle — meter cost, never expressiveness

Two classes:

- **NEAR_FREE** — one DB row, effectively free to serve. Capped only against abuse, by a **uniform safety baseline** that is the same for every tenant, self-host included. It is never tier-scaled and never sold as headroom.
- **COST_DRIVING** — maps to a real bill (CPU, TTS, disk, egress, backup). The limit is the tenant's tier `TierLimit` row, read through `IBillingTierService`. Self-host resolves to unlimited (`-1`).

The full template language — `{{if.*}}` conditionals, nesting, pronouns, every namespaced variable, and the `random_response` action — is available to **all** tiers and self-host. Limits never gate expressiveness or the bot's personality.

### 8.2 The registry

| `LimitKey` | Class | Meters | Baseline / tier source |
|---|---|---|---|
| `custom_commands` | NEAR_FREE | live authored `Command` rows per tenant | safety baseline (in the registry) |
| `timers` | NEAR_FREE | live `Timer` rows per tenant | safety baseline |
| `response_variations_per_trigger` | NEAR_FREE | variation-list length of ONE command or timer (per trigger, not channel-wide) | safety baseline |
| `tts_max_characters` | COST_DRIVING | TTS characters per month | `TierLimit` |
| `sandbox_exec_ms` | COST_DRIVING | script CPU time per month | `TierLimit` |
| `sound_clip_storage_bytes` | COST_DRIVING | live gauge: SUM of `SizeBytes` of live sound clips | `TierLimit` |
| `channel_asset_storage_bytes` | COST_DRIVING | live gauge: SUM of `SizeBytes` of live channel assets | `TierLimit` |

`event_responses` is deliberately **not** in the registry (S-EVENTRESPONSE-NO-CREATE): `EventResponse` rows are a fixed, seeded catalogue keyed by event type, never user-created, so a per-channel limit could never be reached. The numeric values live in code (`LimitedResourceRegistry` for baselines, `BillingTierSeeder` for tier limits), not in this spec.

A structural guard keeps the registry honest: an entity carrying `[CountedResource(limitKey, class)]` (`NomNomzBot.Domain.Billing`) must have a matching registry entry with the same class, or the guard test (`LimitedResourceGuardTests`) fails. A limit that nothing enforces is a truthful-data violation, not a feature.

### 8.3 Resolution order

`IResourceQuotaService.CheckAsync(broadcasterId, limitKey, resultingCount)` resolves the limit in this order:

1. A live `TenantLimitOverride` for `(broadcasterId, limitKey)` (unexpired, not soft-deleted). It wins over both other sources. It is a single-tenant operator exception, never a way to reconfigure a tier.
2. NEAR_FREE: the registry `SafetyBaseline`.
3. COST_DRIVING: `IBillingTierService.GetLimitAsync` (tier limit; a live `EntitlementGrant` raises the effective tier; self-host → `-1`).

`allowed = limit == -1 || resultingCount <= limit`. The result is a `QuotaCheckDto`; callers branch on `Allowed`, and a denied write returns `Errors.QuotaExceeded(what, limit)` (`QUOTA_EXCEEDED`, HTTP 403).

### 8.4 Write-path enforcement

Enforcement is **add-time only** and never silently truncates. A create/update path computes the real resulting count and calls `CheckAsync`. Row-counted keys read the current count through `IResourceQuotaService.GetCurrentCountAsync` (the same source the usage report reads), then check `count + 1`.

| Service | Key checked | When |
|---|---|---|
| `CommandService` | `custom_commands`; `response_variations_per_trigger` on the `TemplateResponses` length | before persisting a new command / a longer variation list |
| `TimerManagementService` | `timers`; `response_variations_per_trigger` on the timer's variation list | before persisting a new timer / a longer variation list |
| `SoundClipService` | `sound_clip_storage_bytes` | before storing an upload (`usedBytes + new size`) |
| `ChannelAssetService` | `channel_asset_storage_bytes` | before storing an upload (`usedBytes + new size`) |
| TTS and sandbox metering | `tts_max_characters`, `sandbox_exec_ms` | metered per period through `IUsageMeteringService` (§3.3) |

`GET /channels/{channelId}/billing/limits` (§5.1) returns the truthful usage report for every entry with a count source.

### 8.5 Grandfather on downgrade

These caps gate **adding**, never **keeping**. If a tenant's tier drops below current usage, existing over-limit rows are **not deleted and continue to work**. Only *new* additions are blocked (`QUOTA_EXCEEDED`) until the tenant is back under the cap. The check compares against the cap only on the add path; it never sweeps or truncates existing rows.

---

## 9. Dependencies (from the stack doc)

- **In-box / 2nd-party only for the data plane:** `Microsoft.EntityFrameworkCore` (+ Npgsql/Sqlite providers via the profile adapter); `Microsoft.Extensions.Http.Resilience` (Stripe `HttpClient` retry/breaker — do not hand-roll); in-box `System.Security.Cryptography` `HMACSHA256` + `CryptographicOperations.FixedTimeEquals` for Stripe webhook signature verification; `IFieldCipher` (AES-256-GCM AEAD) over `ISubjectKeyService` (the crypto-shred-capable field vault from `gdpr-crypto.md` §3.2/§3.4, keyed by `Subscription.SubjectKeyId FK→CryptoKey`) for the `[PII-shred]` cipher columns — consumed, not registered here, and superseding the retired `IEncryptionService`; `Newtonsoft.Json` for app JSON DTOs.
- **`[VC:enum]`/`[VC:JSON]` converters:** hand-rolled `ValueConverter<T,string>` per the persistence decision — no 3rd-party EF-Json package.
- **Background metering:** in-box `BackgroundService` + `PeriodicTimer`; SaaS run-once via `IRunOnceGuard` (`DistributedLock.Postgres` or hand-rolled `pg_try_advisory_lock`).
- **Stripe:** thin hand-rolled `IStripeGateway` over `HttpClient` (the genuine external integration; the Stripe.net SDK is not adopted — §10 decision 1). The surface stays behind the interface so the implementation is swappable without touching any contract here. No new 3rd-party NuGet beyond what the stack doc already lists.
- **No** new dependency is introduced by this subsystem; it composes the stack doc's existing approved set.

---

## 10. Decisions (resolved)

1. **Stripe client is a thin hand-rolled `IStripeGateway` over `HttpClient`.** This is the design — consistent with the "hand-roll the Twitch/Helix client" precedent and the minimal-deps rule, with webhook-event typing/parsing done against the `StripeSubscriptionEventDto`/`StripeInvoiceEventDto` shapes (§4). The Stripe.net SDK is not adopted. Because the interface and every service signature are independent of the implementation, the `StripeGateway` impl is swappable behind `IStripeGateway` if a later, deliberate decision elevates the SDK — that swap changes no contract here.
2. **Hosted is paid-only; seeded tier prices are the shipped values, tunable as data.** There is **no free hosted tier** — the cloud entry plan is `base`. `BillingTier.PriceCents` seeds the monetization figures (**$3.99/$7.99/$14.99 → `399`/`799`/`1499` cents for `base`/`pro`/`premium`**); the `free` row is `PriceCents=0`, `IsPublic=false`, and exists only as the self-host / unbilled marker (never a cloud plan). Limits follow the `LimitedResourceRegistry` model (§8): NEAR_FREE limits are a uniform safety baseline held in the registry (self-host included, never tier-scaled); only COST_DRIVING limits are `TierLimit` rows, seeded for the three hosted tiers by `BillingTierSeeder` — self-host gets no rows and resolves every COST_DRIVING limit to `-1`. Tier prices and `TierLimit` values are reference data with no schema or interface impact, so they are re-tunable by editing the seed or through the admin tier editor (`AdminBillingController`, §5.3) without a contract change. They are flagged for owner review before go-live as a business pricing call, but coding proceeds against the shipped values now.
