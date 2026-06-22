// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Billing.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Billing.Entities;

/// <summary>
/// A billing invoice synced from Stripe (monetization-billing.md N.4) — tenant-scoped paid/failed history for the
/// billing page. Amounts are integer cents.
/// </summary>
public class Invoice : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string? StripeInvoiceId { get; set; }
    public string? Number { get; set; }
    public InvoiceStatus Status { get; set; }
    public int AmountDueCents { get; set; }
    public int AmountPaidCents { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? HostedInvoiceUrl { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}
