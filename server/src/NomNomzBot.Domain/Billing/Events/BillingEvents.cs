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
    public required Guid SubscriptionId { get; init; }
    public required string FromTierKey { get; init; } // "" when newly created
    public required string ToTierKey { get; init; }
    public required string Status { get; init; }
    public required bool IsInviteOnlyGrant { get; init; }
}

/// <summary>Subscription Status transition (Stripe webhook or grace/trial timer).</summary>
public sealed class SubscriptionStatusChangedEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public DateTimeOffset? GracePeriodEndsAt { get; init; }
    public DateTimeOffset? TrialEndsAt { get; init; }
}

/// <summary>Subscription canceled — immediate or at-period-end (monetization-billing.md §2).</summary>
public sealed class SubscriptionCanceledEvent : DomainEventBase
{
    public required Guid SubscriptionId { get; init; }
    public required bool AtPeriodEnd { get; init; }
    public DateTimeOffset? EffectiveAt { get; init; }
}

/// <summary>A metered usage counter crossed its tier limit for the current period.</summary>
public sealed class UsageQuotaExceededEvent : DomainEventBase
{
    public required string MetricKey { get; init; }
    public required long Used { get; init; }
    public required long Limit { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required DateTimeOffset PeriodEnd { get; init; }
}

/// <summary>Invoice synced from Stripe and persisted/updated (paid/failed history).</summary>
public sealed class InvoicePaymentRecordedEvent : DomainEventBase
{
    public required Guid InvoiceId { get; init; }
    public required string Status { get; init; }
    public required int AmountPaidCents { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Invite code redeemed (badge and/or tier granted). <c>BroadcasterId</c> = redeemer's channel.</summary>
public sealed class InviteCodeRedeemedEvent : DomainEventBase
{
    public required Guid InviteCodeId { get; init; }
    public required string Code { get; init; }
    public required bool GrantedFoundersBadge { get; init; }
    public Guid? GrantedTierId { get; init; }
}

/// <summary>Founders badge granted (invite redemption or admin grant).</summary>
public sealed class FoundersBadgeGrantedEvent : DomainEventBase
{
    public required Guid FoundersBadgeId { get; init; }
    public string? InviteCode { get; init; }
}
