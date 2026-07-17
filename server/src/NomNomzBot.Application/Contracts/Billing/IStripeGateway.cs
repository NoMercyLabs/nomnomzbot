// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// The outbound Stripe boundary (monetization-billing.md §3.1) — the thin seam over the Stripe API that
/// <see cref="ISubscriptionService"/> uses for hosted checkout and the self-serve billing portal. Implementations
/// fail closed with <c>SERVICE_UNAVAILABLE</c> when Stripe is not configured (no secret key), so self-host is
/// unaffected. The inbound webhook (already wired) converges the local subscription after Stripe events.
/// </summary>
public interface IStripeGateway
{
    /// <summary>
    /// Opens a hosted Checkout for a subscription to <paramref name="priceId"/>. <paramref name="clientReferenceId"/>
    /// (the broadcaster id) rides on the session so the completion webhook can map it back. Stripe collects the
    /// customer's email + creates the customer; the page redirects to success/cancel.
    /// </summary>
    Task<Result<CheckoutSessionDto>> CreateCheckoutSessionAsync(
        string priceId,
        string clientReferenceId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// A short-lived self-serve Billing Portal URL for the customer behind <paramref name="stripeSubscriptionId"/>
    /// (resolved to its customer host-side), where they manage payment method, plan, and cancellation.
    /// </summary>
    Task<Result<BillingPortalDto>> CreateBillingPortalSessionAsync(
        string stripeSubscriptionId,
        string returnUrl,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches the live Stripe subscription's price (the self-serve tier change, §3.1):
    /// <paramref name="prorate"/> true bills the difference immediately (<c>create_prorations</c>); false
    /// switches without proration — the new price simply applies from the next renewal invoice.
    /// </summary>
    Task<Result> ChangeSubscriptionPriceAsync(
        string stripeSubscriptionId,
        string newPriceId,
        bool prorate,
        CancellationToken cancellationToken = default
    );
}
