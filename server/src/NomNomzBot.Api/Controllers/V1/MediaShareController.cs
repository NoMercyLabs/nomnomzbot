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
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The media-share queue (media-share.md §5) — the mod queue (approve/reject/skip/reorder), the overlay
/// pull (next/played), and the per-channel config. Submissions come in via the <c>!media</c> command and
/// the <c>submit_media</c> pipeline action, not this surface.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/media-share")]
[Authorize]
[Tags("Media Share")]
public class MediaShareController : BaseController
{
    private readonly IMediaShareService _media;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;

    public MediaShareController(
        IMediaShareService media,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser
    )
    {
        _media = media;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    private bool TryGetActor(out Guid actorUserId) =>
        Guid.TryParse(_currentUser.UserId, out actorUserId);

    /// <summary>The queue, optionally filtered by status.</summary>
    [RequireAction("media:read")]
    [HttpGet("queue")]
    [ProducesResponseType<PaginatedResponse<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] string? status,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<PagedList<MediaShareRequestDto>> result = await _media.GetQueueAsync(
            broadcasterId,
            new(status),
            new(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>The next approved item — the overlay pulls this and it flips to playing.</summary>
    [RequireAction("media:read")]
    [HttpGet("next")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNext(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _media.GetNextAsync(broadcasterId, ct));
    }

    /// <summary>Approve a pending item → appended to the play order.</summary>
    [RequireAction("media:moderate")]
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        if (!TryGetActor(out Guid moderatorUserId))
            return UnauthenticatedResponse("No acting user resolved.");
        return ResultResponse(await _media.ApproveAsync(broadcasterId, id, moderatorUserId, ct));
    }

    /// <summary>Reject an item (refunds the entry cost if charged).</summary>
    [RequireAction("media:moderate")]
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        if (!TryGetActor(out Guid moderatorUserId))
            return UnauthenticatedResponse("No acting user resolved.");

        return ResultResponse(await _media.RejectAsync(broadcasterId, id, moderatorUserId, ct));
    }

    /// <summary>Skip an approved/playing item (refunds the entry cost if charged).</summary>
    [RequireAction("media:moderate")]
    [HttpPost("{id:guid}/skip")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Skip(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        return ResultResponse(await _media.SkipAsync(broadcasterId, id, ct));
    }

    /// <summary>Move an approved item to a new 1-based play position.</summary>
    [RequireAction("media:moderate")]
    [HttpPost("{id:guid}/reorder")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder(
        Guid id,
        [FromBody] ReorderMediaRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        return ResultResponse(await _media.ReorderAsync(broadcasterId, id, request.Position, ct));
    }

    /// <summary>The overlay reports the current item finished → played, the queue advances.</summary>
    [RequireAction("media:moderate")]
    [HttpPost("{id:guid}/played")]
    [ProducesResponseType<StatusResponseDto<MediaShareRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkPlayed(Guid id, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        return ResultResponse(await _media.MarkPlayedAsync(broadcasterId, id, ct));
    }

    /// <summary>The channel's media-share config (defaults when never set).</summary>
    [RequireAction("media:read")]
    [HttpGet("config")]
    [ProducesResponseType<StatusResponseDto<MediaShareConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _media.GetConfigAsync(broadcasterId, ct));
    }

    /// <summary>Update the media-share config (enable, approval, sources, cap, cost, queue, cooldown).</summary>
    [RequireAction("media:write")]
    [HttpPut("config")]
    [ProducesResponseType<StatusResponseDto<MediaShareConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        [FromBody] UpdateMediaShareConfigRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _media.UpdateConfigAsync(broadcasterId, request, ct));
    }
}
