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

namespace NomNomzBot.Application.Contracts.Analytics;

/// <summary>
/// SaaS-only cross-tenant platform stats for the operator dashboard (analytics.md §3.4) — Plane C. On self-host
/// there is no platform plane, so the implementation returns <c>FEATURE_DISABLED</c>.
/// </summary>
public interface IPlatformAnalyticsService
{
    /// <summary>Cross-tenant basic stats over a range; no per-viewer PII crosses the tenant boundary.</summary>
    Task<Result<PlatformAnalyticsDto>> GetPlatformStatsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default
    );
}
