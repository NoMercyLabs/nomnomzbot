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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Tenant EventSub subscription management (twitch-eventsub §5.1). Gate 1 is <c>[Authorize]</c> + tenant
/// resolution from the route. Gate 2 is the per-route <c>[RequireAction]</c> floor (<c>eventsub:read</c> /
/// <c>eventsub:subscribe</c> / <c>eventsub:unsubscribe</c>), enforced by <c>IActionAuthorizationService</c>.
/// Self-host collapses to "owner = full".
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/eventsub")]
[Authorize]
[Tags("EventSub")]
public class EventSubController : BaseController
{
    private readonly ITwitchEventSubService _eventSub;

    public EventSubController(ITwitchEventSubService eventSub)
    {
        _eventSub = eventSub;
    }

    /// <summary>Reads this channel's EventSub subscription registry (no Twitch call).</summary>
    [HttpGet("subscriptions")]
    [RequireAction("eventsub:read")]
    [ProducesResponseType<PaginatedResponse<EventSubSubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSubscriptions(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<EventSubSubscriptionDto>> result = await _eventSub.GetSubscriptionsAsync(
            broadcasterId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Creates one EventSub subscription for this channel.</summary>
    [HttpPost("subscriptions")]
    [RequireAction("eventsub:subscribe")]
    [ProducesResponseType<StatusResponseDto<EventSubSubscriptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSubscription(
        string channelId,
        [FromBody] CreateEventSubSubscriptionRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<EventSubSubscriptionDto> result = await _eventSub.SubscribeAsync(
            broadcasterId,
            request.EventType,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Revokes one EventSub subscription by its surrogate id.</summary>
    [HttpDelete("subscriptions/{id:guid}")]
    [RequireAction("eventsub:unsubscribe")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteSubscription(
        string channelId,
        Guid id,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out _))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _eventSub.UnsubscribeAsync(id, ct);
        return ResultResponse(result);
    }

    /// <summary>Reconciles this channel's registry against Twitch's actual subscription list.</summary>
    [HttpPost("reconcile")]
    [RequireAction("eventsub:subscribe")]
    [ProducesResponseType<StatusResponseDto<EventSubReconcileReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<EventSubReconcileReportDto> result = await _eventSub.ReconcileAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }
}
