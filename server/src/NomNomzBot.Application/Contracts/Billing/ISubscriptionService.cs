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
/// Subscription lifecycle (monetization-billing.md §3.1). Reads + the invite/admin grant + the inbound Stripe
/// webhook appliers are local; the outbound Stripe operations (checkout, portal, tier change) flow through the
/// Stripe gateway.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>The channel's subscription, or a synthesized free-tier view when none exists / self-host.</summary>
    Task<Result<SubscriptionDto>> GetSubscriptionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Paginated invoice history (newest first); self-host returns an empty page.</summary>
    Task<Result<PagedList<InvoiceDto>>> ListInvoicesAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Begins a Stripe Checkout for a target tier (SaaS only); the webhook activates it.</summary>
    Task<Result<CheckoutSessionDto>> StartCheckoutAsync(
        Guid broadcasterId,
        StartCheckoutRequest request,
        CancellationToken ct = default
    );

    /// <summary>Switches tier (Stripe proration or scheduled at period end); SaaS only.</summary>
    Task<Result<SubscriptionDto>> ChangeTierAsync(
        Guid broadcasterId,
        ChangeTierRequest request,
        CancellationToken ct = default
    );

    /// <summary>Cancels immediately or at period end; publishes <c>SubscriptionCanceledEvent</c>.</summary>
    Task<Result<SubscriptionDto>> CancelAsync(
        Guid broadcasterId,
        CancelSubscriptionRequest request,
        CancellationToken ct = default
    );

    /// <summary>Reverses a pending at-period-end cancellation. VALIDATION_FAILED if not pending-cancel.</summary>
    Task<Result<SubscriptionDto>> ResumeAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>A short-lived Stripe Billing Portal URL for self-serve management; SaaS only.</summary>
    Task<Result<BillingPortalDto>> CreateBillingPortalSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Applies an inbound, signature-verified Stripe subscription event (upsert + status/tier events).</summary>
    Task<Result> ApplyStripeSubscriptionEventAsync(
        StripeSubscriptionEventDto stripeEvent,
        CancellationToken ct = default
    );

    /// <summary>Applies an inbound, signature-verified Stripe invoice event (upsert + payment event).</summary>
    Task<Result> ApplyStripeInvoiceEventAsync(
        StripeInvoiceEventDto stripeEvent,
        CancellationToken ct = default
    );

    /// <summary>Assigns a tier WITHOUT Stripe (invite/admin grant); publishes <c>SubscriptionTierChangedEvent</c>.</summary>
    Task<Result<SubscriptionDto>> GrantTierAsync(
        Guid broadcasterId,
        Guid tierId,
        bool isInviteOnlyGrant,
        CancellationToken ct = default
    );
}
