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
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Supporter (monetization) connections + recorded events (supporter-events.md §5). A connection is the
/// enforced enable-toggle for a provider (Ko-fi, …); ingest is default-deny. Connecting a payout/identity-bearing
/// money source is Broadcaster-gated; reads are Moderator-gated. Secrets are never returned.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/supporters")]
[Authorize]
[Tags("Supporters")]
public class SupportersController : BaseController
{
    private readonly ISupporterConnectionService _connections;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;

    public SupportersController(
        ISupporterConnectionService connections,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser
    )
    {
        _connections = connections;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    /// <summary>The channel's supporter connections (which providers, ingress mode, live state).</summary>
    [RequireAction("supporters:read")]
    [HttpGet("connections")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<SupporterConnectionDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListConnections(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _connections.ListAsync(broadcasterId, ct));
    }

    /// <summary>Create or update a supporter connection (enable/disable a provider).</summary>
    [RequireAction("supporters:config:write")]
    [HttpPut("connections")]
    [ProducesResponseType<StatusResponseDto<SupporterConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertConnection(
        [FromBody] UpsertSupporterConnectionRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse("No acting user.");
        return ResultResponse(
            await _connections.UpsertAsync(broadcasterId, actorUserId, request, ct)
        );
    }

    /// <summary>Remove a supporter connection.</summary>
    [RequireAction("supporters:config:write")]
    [HttpDelete("connections/{sourceKey}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteConnection(string sourceKey, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse("No acting user.");
        return ResultResponse(
            await _connections.DeleteAsync(broadcasterId, actorUserId, sourceKey, ct)
        );
    }

    /// <summary>Browse recorded supporter events (newest first), optionally filtered by kind / source.</summary>
    [RequireAction("supporters:read")]
    [HttpGet("events")]
    [ProducesResponseType<PaginatedResponse<SupporterEventDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEvents(
        [FromQuery] PageRequestDto request,
        [FromQuery] string? kind,
        [FromQuery] string? sourceKey,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        SupporterEventQuery query = new(request.Page, request.Take, kind, sourceKey);
        Result<PagedList<SupporterEventDto>> result = await _connections.ListEventsAsync(
            broadcasterId,
            query,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
