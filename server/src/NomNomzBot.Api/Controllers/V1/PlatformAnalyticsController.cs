// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// SaaS-only cross-tenant platform stats (analytics.md §5) — Plane C (admin). On self-host the service returns
/// FEATURE_DISABLED (there is no cross-tenant view).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/analytics")]
[Authorize(Roles = "admin")]
[Tags("Platform Analytics")]
public class PlatformAnalyticsController(IPlatformAnalyticsService platformAnalytics)
    : BaseController
{
    /// <summary>Read cross-tenant platform stats for a date range (FEATURE_DISABLED on self-host).</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    ) => ResultResponse(await platformAnalytics.GetPlatformStatsAsync(from, to, ct));
}
