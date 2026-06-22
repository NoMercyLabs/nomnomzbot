// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using Stripe;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// The outbound Stripe boundary (monetization-billing.md §3.1) over Stripe.net. Configured from
/// <c>Stripe:SecretKey</c>; with no key (self-host / unconfigured SaaS) every call fails closed with
/// <c>SERVICE_UNAVAILABLE</c> so nothing 500s. Redirects/PII are Stripe-hosted — we never see card data — and the
/// already-wired inbound webhook converges the local subscription after checkout completes.
/// </summary>
public sealed class StripeGateway : IStripeGateway
{
    private readonly IStripeClient? _client;

    public StripeGateway(IConfiguration configuration)
    {
        string? secretKey = configuration["Stripe:SecretKey"];
        _client = string.IsNullOrWhiteSpace(secretKey) ? null : new StripeClient(secretKey);
    }

    public async Task<Result<CheckoutSessionDto>> CreateCheckoutSessionAsync(
        string priceId,
        string clientReferenceId,
        string successUrl,
        string cancelUrl,
        CancellationToken cancellationToken = default
    )
    {
        if (_client is null)
            return Result.Failure<CheckoutSessionDto>(
                "Stripe billing is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        try
        {
            Stripe.Checkout.SessionCreateOptions options = new()
            {
                Mode = "subscription",
                LineItems = [new() { Price = priceId, Quantity = 1 }],
                ClientReferenceId = clientReferenceId,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
            };
            Stripe.Checkout.Session session = await new Stripe.Checkout.SessionService(
                _client
            ).CreateAsync(options, cancellationToken: cancellationToken);
            return Result.Success(new CheckoutSessionDto(session.Url, session.Id));
        }
        catch (StripeException ex)
        {
            return Result.Failure<CheckoutSessionDto>(
                $"Stripe checkout failed: {ex.Message}",
                "SERVICE_UNAVAILABLE"
            );
        }
    }

    public async Task<Result<BillingPortalDto>> CreateBillingPortalSessionAsync(
        string stripeSubscriptionId,
        string returnUrl,
        CancellationToken cancellationToken = default
    )
    {
        if (_client is null)
            return Result.Failure<BillingPortalDto>(
                "Stripe billing is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        try
        {
            // The local record stores the subscription id, not the customer — resolve the customer from it.
            Subscription subscription = await new Stripe.SubscriptionService(_client).GetAsync(
                stripeSubscriptionId,
                cancellationToken: cancellationToken
            );
            Stripe.BillingPortal.SessionCreateOptions options = new()
            {
                Customer = subscription.CustomerId,
                ReturnUrl = returnUrl,
            };
            Stripe.BillingPortal.Session session = await new Stripe.BillingPortal.SessionService(
                _client
            ).CreateAsync(options, cancellationToken: cancellationToken);
            return Result.Success(new BillingPortalDto(session.Url));
        }
        catch (StripeException ex)
        {
            return Result.Failure<BillingPortalDto>(
                $"Stripe billing portal failed: {ex.Message}",
                "SERVICE_UNAVAILABLE"
            );
        }
    }
}
