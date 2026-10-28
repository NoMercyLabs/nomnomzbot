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
