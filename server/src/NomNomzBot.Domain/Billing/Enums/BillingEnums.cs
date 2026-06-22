// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
