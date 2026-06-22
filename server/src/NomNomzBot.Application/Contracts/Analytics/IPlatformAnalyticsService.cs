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
