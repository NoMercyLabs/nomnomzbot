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

namespace NomNomzBot.Application.Contracts.Billing;

/// <summary>
/// Authenticates and dispatches an inbound Stripe webhook (monetization-billing.md §5.2). The raw body is
/// verified against the webhook secret (HMAC) before any state change. SERVICE_UNAVAILABLE when Stripe is not
/// configured; VALIDATION_FAILED on a bad signature.
/// </summary>
public interface IStripeWebhookHandler
{
    Task<Result> HandleAsync(
        string payload,
        string? signatureHeader,
        CancellationToken ct = default
    );
}
