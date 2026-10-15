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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The signed-in viewer's OWN activity. Both routes bind the subject to the authenticated caller (the user id
/// comes from the token, never from the route or query), so they can only ever return the caller's data and carry
/// no moderator floor.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
[Tags("Viewer")]
public class ViewerSelfController(
    IViewerSelfService viewer,
    IViewerSelfActivityService activity,
    IViewerSelfQueueService queue,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>Channels where the caller has viewer activity (never their own), newest activity first.</summary>
    [HttpGet("api/v{version:apiVersion}/me/watched-channels")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<WatchedChannelDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetWatchedChannels(CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await viewer.ListWatchedChannelsAsync(caller, ct));
    }

    /// <summary>The caller's own tiles in one channel: counts, points, song requests, follow and sub.</summary>
    [HttpGet("api/v{version:apiVersion}/channels/{channelId:guid}/viewer/me")]
    [ProducesResponseType<StatusResponseDto<ViewerSelfSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySummary(Guid channelId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await viewer.GetSummaryAsync(channelId, caller, ct));
    }

    /// <summary>The caller's own recent chat lines in one channel, newest first, paged.</summary>
    [HttpGet("api/v{version:apiVersion}/channels/{channelId:guid}/viewer/me/messages")]
    [ProducesResponseType<PaginatedResponse<ViewerMessageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyMessages(
        Guid channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();

        Result<PagedList<ViewerMessageDto>> page = await activity.ListMessagesAsync(
            channelId,
            caller,
            request.Page,
            request.Take,
            ct
        );
        return page.IsSuccess ? GetPaginatedResponse(page.Value, request) : ResultResponse(page);
    }

    /// <summary>The channel's commands the caller may run, by name, with the caller's own last use.</summary>
    [HttpGet("api/v{version:apiVersion}/channels/{channelId:guid}/viewer/me/commands")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ViewerCommandDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetMyCommands(Guid channelId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await activity.ListCommandsAsync(channelId, caller, ct));
    }

    /// <summary>The caller's own songs in the channel's song-request queue, with their place in line.</summary>
    [HttpGet("api/v{version:apiVersion}/channels/{channelId:guid}/viewer/me/queue")]
    [ProducesResponseType<StatusResponseDto<ViewerQueueDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyQueue(Guid channelId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await queue.GetMyQueueAsync(channelId, caller, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
