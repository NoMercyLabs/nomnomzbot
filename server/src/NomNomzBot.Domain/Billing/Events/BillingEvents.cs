// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Billing.Events;

/// <summary>Tier changed — upgrade, downgrade, or invite/admin grant (monetization-billing.md §2).</summary>
public sealed class SubscriptionTierChangedEvent : DomainEventBase
{
    /// <summary>The id of the subscription.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>The key of the old tier. Empty when the subscription is new.</summary>
    public required string FromTierKey { get; init; } // "" when newly created

    /// <summary>The key of the new tier.</summary>
    public required string ToTierKey { get; init; }

    /// <summary>The status of the subscription after the change: active, trialing, past_due, canceled or incomplete.</summary>
    public required string Status { get; init; }

    /// <summary>True when the tier was granted by an invite code and not paid for.</summary>
    public required bool IsInviteOnlyGrant { get; init; }
}

/// <summary>Subscription Status transition (Stripe webhook or grace/trial timer).</summary>
public sealed class SubscriptionStatusChangedEvent : DomainEventBase
{
    /// <summary>The id of the subscription.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>The status before the change: active, trialing, past_due, canceled or incomplete.</summary>
    public required string FromStatus { get; init; }

    /// <summary>The status after the change: active, trialing, past_due, canceled or incomplete.</summary>
    public required string ToStatus { get; init; }

    /// <summary>When the grace period ends, in UTC. Empty when there is no grace period.</summary>
    public DateTimeOffset? GracePeriodEndsAt { get; init; }

    /// <summary>When the trial ends, in UTC. Empty when there is no trial.</summary>
    public DateTimeOffset? TrialEndsAt { get; init; }
}

/// <summary>Subscription canceled — immediate or at-period-end (monetization-billing.md §2).</summary>
public sealed class SubscriptionCanceledEvent : DomainEventBase
{
    /// <summary>The id of the subscription.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>True when the subscription stays active until the end of the paid period.</summary>
    public required bool AtPeriodEnd { get; init; }

    /// <summary>When the cancel takes effect, in UTC. Empty when it is not set.</summary>
    public DateTimeOffset? EffectiveAt { get; init; }
}

/// <summary>A metered usage counter crossed its tier limit for the current period.</summary>
public sealed class UsageQuotaExceededEvent : DomainEventBase
{
    /// <summary>The key of the usage metric that went over its limit.</summary>
    public required string MetricKey { get; init; }

    /// <summary>How much of the metric the channel has used in this period.</summary>
    public required long Used { get; init; }

    /// <summary>The most the plan allows for this metric in one period.</summary>
    public required long Limit { get; init; }

    /// <summary>When the usage period starts, in UTC.</summary>
    public required DateTimeOffset PeriodStart { get; init; }

    /// <summary>When the usage period ends, in UTC.</summary>
    public required DateTimeOffset PeriodEnd { get; init; }
}

/// <summary>Invoice synced from Stripe and persisted/updated (paid/failed history).</summary>
public sealed class InvoicePaymentRecordedEvent : DomainEventBase
{
    /// <summary>The id of the invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The status of the invoice after the payment: open, paid, void, uncollectible, refunded or draft.</summary>
    public required string Status { get; init; }

    /// <summary>The amount paid, in cents of the invoice currency.</summary>
    public required int AmountPaidCents { get; init; }

    /// <summary>The currency of the invoice, as stored on the invoice.</summary>
    public required string Currency { get; init; }
}

/// <summary>An invoice was refunded via platform admin (monetization-billing.md §5.3, <c>billing:refund</c>).</summary>
public sealed class InvoiceRefundedEvent : DomainEventBase
{
    /// <summary>The id of the invoice.</summary>
    public required Guid InvoiceId { get; init; }

    /// <summary>The amount refunded, in cents of the invoice currency.</summary>
    public required int AmountRefundedCents { get; init; }

    /// <summary>The currency of the invoice, as stored on the invoice.</summary>
    public required string Currency { get; init; }
}

/// <summary>Invite code redeemed (badge and/or tier granted). <c>BroadcasterId</c> = redeemer's channel.</summary>
public sealed class InviteCodeRedeemedEvent : DomainEventBase
{
    /// <summary>The id of the invite code.</summary>
    public required Guid InviteCodeId { get; init; }

    /// <summary>The invite code text that was redeemed.</summary>
    public required string Code { get; init; }

    /// <summary>True when the redeem also granted a founders badge.</summary>
    public required bool GrantedFoundersBadge { get; init; }

    /// <summary>The id of the tier the code granted. Empty when the code grants no tier.</summary>
    public Guid? GrantedTierId { get; init; }
}

/// <summary>Founders badge granted (invite redemption or admin grant).</summary>
public sealed class FoundersBadgeGrantedEvent : DomainEventBase
{
    /// <summary>The id of the founders badge.</summary>
    public required Guid FoundersBadgeId { get; init; }

    /// <summary>The invite code used to get the badge. Empty when no code was used.</summary>
    public string? InviteCode { get; init; }
}
