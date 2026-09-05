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

    /// <summary>When Stripe considers this invoice due; falls back to <see cref="PeriodEnd"/> when Stripe
    /// sends no explicit <c>due_date</c> (the usual case for subscription invoices). Drives
    /// <see cref="ResolveDunningStatus"/> — the ONE place past-due is computed, so a displayed dunning
    /// state is always the state the rest of the system would compute too.</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>Set by <c>RefundInvoiceAsync</c> alongside <see cref="Status"/> flipping to
    /// <see cref="InvoiceStatus.Refunded"/> — the persisted record of what was actually refunded, not just
    /// the event emitted at the time.</summary>
    public int AmountRefundedCents { get; set; }
    public DateTime? RefundedAt { get; set; }

    /// <summary>
    /// The dunning state as the rest of the system would read it: an <see cref="InvoiceStatus.Open"/>
    /// invoice past its <see cref="DueAt"/> is <see cref="InvoiceDunningStatus.PastDue"/>; any other status
    /// (paid, refunded, void, uncollectible, draft) is never past-due, however old it is. This is the only
    /// place that computes the answer — nothing else may re-derive it, so a badge showing "past due" is
    /// always backed by this exact rule.
    /// </summary>
    public InvoiceDunningStatus ResolveDunningStatus(DateTimeOffset utcNow)
    {
        if (Status != InvoiceStatus.Open)
            return InvoiceDunningStatus.NotDunning;
        return DueAt is { } due && due <= utcNow.UtcDateTime
            ? InvoiceDunningStatus.PastDue
            : InvoiceDunningStatus.Current;
    }
}
