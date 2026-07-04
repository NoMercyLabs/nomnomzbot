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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Analytics dashboard reads (analytics.md §5). Channel routes are management plane (<c>analytics:read</c>,
/// Moderator floor). Viewer routes are <c>analytics:viewer:read</c> — but a viewer may read their OWN profile/
/// engagement/streak/opt-out without a management role (self-or-Gate-2), so those bind the caller and authorize
/// in-action rather than via the attribute.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/analytics")]
[Authorize]
[Tags("Analytics")]
public class AnalyticsController(
    IChannelAnalyticsService channelAnalytics,
    IViewerAnalyticsService viewerAnalytics,
    IActionAuthorizationService authorization,
    ICurrentUserService currentUser
) : BaseController
{
    // ── Channel (management plane) ───────────────────────────────────────────

    /// <summary>Read the channel's daily analytics series for a date range.</summary>
    [HttpGet("channel/daily")]
    [RequireAction("analytics:read")]
    public async Task<IActionResult> GetChannelDaily(
        Guid channelId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    ) => ResultResponse(await channelAnalytics.GetDailySeriesAsync(channelId, from, to, ct));

    /// <summary>Read the channel's aggregated analytics summary for a date range.</summary>
    [HttpGet("channel/summary")]
    [RequireAction("analytics:read")]
    public async Task<IActionResult> GetChannelSummary(
        Guid channelId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    ) => ResultResponse(await channelAnalytics.GetSummaryAsync(channelId, from, to, ct));

    /// <summary>Rank the channel's top viewers by the chosen metric over a date range.</summary>
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

    // ── Viewers ──────────────────────────────────────────────────────────────

    /// <summary>List viewer analytics profiles matching the query, paginated.</summary>
    [HttpGet("viewers")]
    [RequireAction("analytics:viewer:read")]
    public async Task<IActionResult> ListViewers(
        Guid channelId,
        [FromQuery] ViewerProfileQuery query,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams paging = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ViewerProfileListItemDto>> result =
            await viewerAnalytics.ListProfilesAsync(channelId, query, paging, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read a single viewer's analytics profile (self-or-Gate-2, authorized in-action).</summary>
    [HttpGet("viewers/{viewerUserId:guid}")]
    public async Task<IActionResult> GetViewer(
        Guid channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!await CanReadViewerAsync(channelId, viewerUserId, ct))
            return UnauthorizedResponse();
        return ResultResponse(await viewerAnalytics.GetProfileAsync(channelId, viewerUserId, ct));
    }

    /// <summary>Read a viewer's engagement series over a date range (self-or-Gate-2).</summary>
    [HttpGet("viewers/{viewerUserId:guid}/engagement")]
    public async Task<IActionResult> GetViewerEngagement(
        Guid channelId,
        Guid viewerUserId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct
    )
    {
        if (!await CanReadViewerAsync(channelId, viewerUserId, ct))
            return UnauthorizedResponse();
        return ResultResponse(
            await viewerAnalytics.GetEngagementSeriesAsync(channelId, viewerUserId, from, to, ct)
        );
    }

    /// <summary>Read a viewer's current attendance streak (self-or-Gate-2).</summary>
    [HttpGet("viewers/{viewerUserId:guid}/streak")]
    public async Task<IActionResult> GetViewerStreak(
        Guid channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!await CanReadViewerAsync(channelId, viewerUserId, ct))
            return UnauthorizedResponse();
        return ResultResponse(await viewerAnalytics.GetStreakAsync(channelId, viewerUserId, ct));
    }

    /// <summary>Set or clear a viewer's analytics opt-out flag (self-or-Gate-2).</summary>
    [HttpPost("viewers/{viewerUserId:guid}/opt-out")]
    public async Task<IActionResult> SetViewerOptOut(
        Guid channelId,
        Guid viewerUserId,
        [FromBody] SetAnalyticsOptOutRequest request,
        CancellationToken ct
    )
    {
        if (!await CanReadViewerAsync(channelId, viewerUserId, ct))
            return UnauthorizedResponse();
        return ResultResponse(
            await viewerAnalytics.SetAnalyticsOptOutAsync(
                channelId,
                viewerUserId,
                request.OptedOut,
                ct
            )
        );
    }

    /// <summary>Self-or-Gate-2: the viewer themselves, or a caller holding <c>analytics:viewer:read</c>.</summary>
    private async Task<bool> CanReadViewerAsync(
        Guid channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return false;
        if (caller == viewerUserId)
            return true;
        Result<bool> authorized = await authorization.AuthorizeActionAsync(
            caller,
            channelId,
            "analytics:viewer:read",
            ct
        );
        return authorized.IsSuccess && authorized.Value;
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
