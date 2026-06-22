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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Contracts.Analytics;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Per-channel analytics dashboard reads (analytics.md §5). Channel-scoped, management plane — every route gated
/// on <c>analytics:read</c> (Moderator floor). The viewer-scoped routes land with the viewer-analytics service.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/analytics")]
[Authorize]
[Tags("Analytics")]
public class AnalyticsController(IChannelAnalyticsService channelAnalytics) : BaseController
{
    [HttpGet("channel/daily")]
    [RequireAction("analytics:read")]
    public async Task<IActionResult> GetChannelDaily(
        Guid channelId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    ) => ResultResponse(await channelAnalytics.GetDailySeriesAsync(channelId, from, to, ct));

    [HttpGet("channel/summary")]
    [RequireAction("analytics:read")]
    public async Task<IActionResult> GetChannelSummary(
        Guid channelId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    ) => ResultResponse(await channelAnalytics.GetSummaryAsync(channelId, from, to, ct));

    [HttpGet("channel/top-viewers")]
    [RequireAction("analytics:read")]
    public async Task<IActionResult> GetTopViewers(
        Guid channelId,
        [FromQuery] TopViewerMetric metric,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] int top,
        CancellationToken ct
    ) =>
        ResultResponse(
            await channelAnalytics.GetTopViewersAsync(channelId, metric, from, to, top, ct)
        );
}
