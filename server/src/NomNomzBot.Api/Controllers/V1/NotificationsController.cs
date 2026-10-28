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

using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
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
