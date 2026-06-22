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
/// Cost-driver counters + quota enforcement (monetization-billing.md §3.3). Per-tenant, accumulating into the
/// current-period <c>UsageRecord</c>. Self-host is a no-op (unlimited).
/// </summary>
public interface IUsageMeteringService
{
    /// <summary>
    /// Accumulates <paramref name="quantity"/> (&gt; 0) into the current-period counter for the metric and
    /// publishes <c>UsageQuotaExceededEvent</c> the first time it crosses the limit. Self-host is a no-op success.
    /// </summary>
    Task<Result> RecordAsync(
        Guid broadcasterId,
        string metricKey,
        long quantity,
        CancellationToken ct = default
    );

    /// <summary>Pre-flight quota check (no increment); returns the verdict as data (<c>Allowed</c>), not an error.</summary>
    Task<Result<QuotaCheckDto>> CheckAsync(
        Guid broadcasterId,
        string metricKey,
        long requestedQuantity,
        CancellationToken ct = default
    );

    /// <summary>Current-period usage across every metered key vs the tenant's limits — drives the usage widget.</summary>
    Task<Result<IReadOnlyList<UsageMetricDto>>> GetCurrentUsageAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Flushes unreported usage to Stripe metered billing and stamps it reported. Returns the count reported.</summary>
    Task<Result<int>> ReportUnbilledUsageToStripeAsync(CancellationToken ct = default);
}
