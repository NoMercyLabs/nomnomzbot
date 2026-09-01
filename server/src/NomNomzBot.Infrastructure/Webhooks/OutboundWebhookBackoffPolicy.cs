// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Outbound webhook retry backoff (webhooks.md §3.7, S099a fix). Exponential from a 30s base, capped at a 1-hour
/// ceiling so a long-failing endpoint never drifts into a multi-day next-retry, and jittered to 50-100% of the
/// capped value so many endpoints failing at once don't all wake up and re-POST in lockstep (thundering herd).
/// </summary>
public static class OutboundWebhookBackoffPolicy
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Ceiling = TimeSpan.FromHours(1);

    /// <summary>
    /// Computes the delay before the next retry for the given attempt number (1-based, matching
    /// <c>OutboundWebhookDelivery.Attempt</c>). Accepts an injectable <paramref name="random"/> for
    /// deterministic tests; production callers omit it and get <see cref="Random.Shared"/>.
    /// </summary>
    public static TimeSpan ComputeDelay(int attempt, Random? random = null)
    {
        random ??= Random.Shared;
        int exponent = Math.Max(0, attempt - 1);
        double exponentialSeconds = BaseDelay.TotalSeconds * Math.Pow(2, exponent);
        double cappedSeconds = Math.Min(exponentialSeconds, Ceiling.TotalSeconds);
        double jitteredSeconds = cappedSeconds * (0.5 + (random.NextDouble() * 0.5));
        return TimeSpan.FromSeconds(jitteredSeconds);
    }
}
