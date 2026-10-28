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
