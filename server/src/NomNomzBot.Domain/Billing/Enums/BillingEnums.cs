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

namespace NomNomzBot.Domain.Billing.Enums;

/// <summary>The lifecycle status of a <c>Subscription</c> (monetization-billing.md N.3), persisted as text.</summary>
public enum SubscriptionStatus
{
    Active,
    Trialing,
    PastDue,
    Canceled,
    Incomplete,
}

/// <summary>The status of an <c>Invoice</c> (monetization-billing.md N.4), persisted as text.</summary>
public enum InvoiceStatus
{
    Draft,
    Open,
    Paid,
    Void,
    Uncollectible,
    Refunded,
}

/// <summary>
/// The dunning state of an <c>Invoice</c>, computed on demand by <see cref="Entities.Invoice.ResolveDunningStatus"/>
/// — never persisted, so it can never drift from the rule that produces it (S-ADMIN-4c).
/// </summary>
public enum InvoiceDunningStatus
{
    /// <summary>Paid, refunded, void, uncollectible, or draft — dunning never applies.</summary>
    NotDunning,

    /// <summary>Open and not yet past its due date.</summary>
    Current,

    /// <summary>Open and past its due date.</summary>
    PastDue,
}
