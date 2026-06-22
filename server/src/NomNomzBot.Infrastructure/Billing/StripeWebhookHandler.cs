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

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Stripe webhook authentication (monetization-billing.md §5.2). Verifies the signature against
/// <c>Stripe:WebhookSecret</c> before any side effect. (Deferred — documented: the verified event's
/// body→DTO mapping and dispatch to <c>ISubscriptionService.ApplyStripe*EventAsync</c> land with the Stripe
/// gateway, which fixes the Stripe payload shape; a verified event is acknowledged until then.)
/// </summary>
public sealed class StripeWebhookHandler(IConfiguration configuration, TimeProvider clock)
    : IStripeWebhookHandler
{
    public Task<Result> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken ct = default
    )
    {
        string? secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrEmpty(secret))
            return Task.FromResult(
                Result.Failure("Stripe webhooks are not configured.", "SERVICE_UNAVAILABLE")
            );

        bool authentic = StripeWebhookSignature.Verify(
            payload,
            signatureHeader,
            secret,
            clock.GetUtcNow().ToUnixTimeSeconds()
        );
        if (!authentic)
            return Task.FromResult(
                Result.Failure("Invalid Stripe signature.", "VALIDATION_FAILED")
            );

        return Task.FromResult(Result.Success());
    }
}
