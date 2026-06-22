// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.DTOs.Billing;

// ── Responses (monetization-billing.md §4). Money is integer cents. ──

/// <summary>A channel's subscription view.</summary>
public sealed record SubscriptionDto(
    Guid Id,
    Guid BroadcasterId,
    string TierKey,
    string TierDisplayName,
    string Status,
    bool CancelAtPeriodEnd,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? GracePeriodEndsAt,
    bool IsInviteOnlyGrant,
    bool AllowsCustomBotName,
    bool PrioritySupport
);

/// <summary>One quota lever for a tier (<c>-1</c> = unlimited).</summary>
public sealed record TierLimitDto(string LimitKey, long LimitValue);

/// <summary>A public billing plan with its limits.</summary>
public sealed record TierDto(
    Guid Id,
    string Key,
    string DisplayName,
    int PriceCents,
    string Currency,
    bool AllowsCustomBotName,
    bool PrioritySupport,
    int SortOrder,
    IReadOnlyList<TierLimitDto> Limits
);

/// <summary>A tenant's effective entitlement — the single source feature-gating + quota checks read.</summary>
public sealed record EntitlementDto(
    string TierKey,
    bool AllowsCustomBotName,
    bool PrioritySupport,
    IReadOnlyDictionary<string, long> Limits
);

/// <summary>A Stripe Checkout session redirect.</summary>
public sealed record CheckoutSessionDto(string CheckoutUrl, string StripeSessionId);

/// <summary>A Stripe billing-portal redirect.</summary>
public sealed record BillingPortalDto(string PortalUrl);

/// <summary>A pre-flight quota check result (callers branch on <c>Allowed</c>, not an error).</summary>
public sealed record QuotaCheckDto(
    bool Allowed,
    string MetricKey,
    long Used,
    long Limit,
    long Remaining
);

/// <summary>Current-period usage for one metered key vs the tier limit.</summary>
public sealed record UsageMetricDto(
    string MetricKey,
    long Used,
    long Limit,
    long Remaining,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd
);

/// <summary>A billing invoice view.</summary>
public sealed record InvoiceDto(
    Guid Id,
    string? Number,
    string Status,
    int AmountDueCents,
    int AmountPaidCents,
    string Currency,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string? HostedInvoiceUrl
);

/// <summary>A founders badge view.</summary>
public sealed record FoundersBadgeDto(
    Guid Id,
    DateTimeOffset GrantedAt,
    bool IsActive,
    string? InviteCode
);

/// <summary>An invite-code view (admin).</summary>
public sealed record InviteCodeDto(
    Guid Id,
    string Code,
    int MaxRedemptions,
    int RedemptionCount,
    bool GrantsFoundersBadge,
    Guid? GrantsTierId,
    string? GrantsTierKey,
    DateTimeOffset? ExpiresAt
);

/// <summary>The result of validating an invite code (pre-redemption).</summary>
public sealed record InviteCodeValidationDto(
    bool IsValid,
    string Code,
    bool GrantsFoundersBadge,
    string? GrantsTierKey,
    int RemainingRedemptions,
    DateTimeOffset? ExpiresAt
);

/// <summary>The result of redeeming an invite code.</summary>
public sealed record RedeemInviteCodeResultDto(
    bool GrantedFoundersBadge,
    string? GrantedTierKey,
    FoundersBadgeDto? FoundersBadge
);

// ── Requests ──

/// <summary>Start a Stripe Checkout for a tier.</summary>
public sealed record StartCheckoutRequest(string TierKey, string? SuccessUrl, string? CancelUrl);

/// <summary>Change the active tier (upgrade/downgrade).</summary>
public sealed record ChangeTierRequest(string TierKey, bool AtPeriodEnd);

/// <summary>Cancel the subscription (immediate or at period end).</summary>
public sealed record CancelSubscriptionRequest(bool AtPeriodEnd, string? Reason);

/// <summary>Create an invite code (admin).</summary>
public sealed record CreateInviteCodeRequest(
    int MaxRedemptions,
    bool GrantsFoundersBadge,
    Guid? GrantsTierId,
    DateTimeOffset? ExpiresAt
);

// ── Inbound integration (webhook → service; not an exposed request body) ──

/// <summary>A Stripe subscription event projected for the webhook handler.</summary>
public sealed record StripeSubscriptionEventDto(
    string StripeEventId,
    string EventType,
    string StripeCustomerId,
    string StripeSubscriptionId,
    string? StripePriceId,
    string Status,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    DateTimeOffset? TrialEnd,
    bool CancelAtPeriodEnd
);

/// <summary>A Stripe invoice event projected for the webhook handler.</summary>
public sealed record StripeInvoiceEventDto(
    string StripeEventId,
    string EventType,
    string StripeInvoiceId,
    string StripeCustomerId,
    string? StripeSubscriptionId,
    string? Number,
    string Status,
    int AmountDueCents,
    int AmountPaidCents,
    string Currency,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    DateTimeOffset IssuedAt,
    DateTimeOffset? PaidAt,
    string? HostedInvoiceUrl
);
