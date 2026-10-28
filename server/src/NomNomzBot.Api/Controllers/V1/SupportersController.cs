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
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
        if (_tenant.BroadcasterId is not { } broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse("No acting user.");
        return ResultResponse(
            await _connections.UpsertAsync(broadcasterId, actorUserId, request, ct)
        );
    }

    /// <summary>Remove a supporter connection.</summary>
    [RequireAction("supporters:config:write")]
    [NotDestructive(
        "Deletes one SupporterConnection row; SupporterConnection references others but no entity carries a SupporterConnectionId FK."
    )]
    [HttpDelete("connections/{sourceKey}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteConnection(string sourceKey, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
        if (_tenant.BroadcasterId is not { } broadcasterId)
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
