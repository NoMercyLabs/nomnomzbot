// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Billing.Entities;

/// <summary>
/// A channel's billing subscription (monetization-billing.md N.3) — one per channel, tenant-scoped + soft-delete.
/// Supersedes the legacy <c>ChannelSubscription</c>. Stripe customer/email are encrypted (PII-shred);
/// <c>IsInviteOnlyGrant</c> marks an unbilled invite/admin grant.
/// </summary>
public class Subscription : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid TierId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public string? StripeCustomerIdCipher { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? BillingEmailCipher { get; set; }
    public Guid? SubjectKeyId { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? GracePeriodEndsAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool IsInviteOnlyGrant { get; set; }
}
