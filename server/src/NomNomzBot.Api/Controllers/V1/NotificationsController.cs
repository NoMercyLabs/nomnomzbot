// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The dashboard's "action required" notification centre (S071a) — a single aggregation endpoint surfacing
/// real, already-detected conditions needing the streamer's attention (dead integration tokens, AutoMod-held
/// messages pending review), so they no longer need to be discovered by noticing something silently broke.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Tags("Notifications")]
public class NotificationsController : BaseController
{
    private readonly IActionRequiredInboxService _inbox;

    public NotificationsController(IActionRequiredInboxService inbox)
    {
        _inbox = inbox;
    }

    /// <summary>Lists the channel's current action-required items, newest first.</summary>
    [HttpGet("{channelId}/notifications/action-required")]
    [Authorize]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<List<ActionRequiredItemDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActionRequiredItems(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<List<ActionRequiredItemDto>> result = await _inbox.GetItemsAsync(tenantId, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Dismisses action-required items by their stable ids (S-OWN22 T2). A grouped
    /// <c>held-user:{sourceUserId}</c> id is expanded into one persisted dismissal per contained
    /// <c>held:{queueItemGuid}</c> key, so a NEW hold from that user surfaces again. Returns the number of
    /// dismissal rows written (already-dismissed keys are skipped).
    /// </summary>
    [HttpPost("{channelId}/notifications/action-required/dismiss")]
    [Authorize]
    [RequireAction("notifications:dismiss")]
    [ProducesResponseType<StatusResponseDto<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DismissActionRequiredItems(
        string channelId,
        [FromBody] DismissActionRequiredItemsRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid actorId))
            return UnauthenticatedResponse();

        Result<int> result = await _inbox.DismissAsync(tenantId, actorId, request.Ids, ct);
        return ResultResponse(result);
    }
}
