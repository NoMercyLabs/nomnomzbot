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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Automation API token management (automation-api.md §5) — the dashboard surface where an operator
/// issues, rotates, and revokes the external tokens third-party tools present on the data plane. The
/// secret appears exactly once in a create/rotate response and is never retrievable afterwards.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/automation")]
[Authorize]
[Tags("Automation")]
public class AutomationTokensController(
    IAutomationApiTokenService tokens,
    NomNomzBot.Application.Abstractions.Auth.ICurrentUserService currentUser
) : BaseController
{
    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);

    /// <summary>List the channel's automation tokens (hash-only rows — no secrets), paginated.</summary>
    [HttpGet("tokens")]
    [RequireAction("automation:tokens:read")]
    [ProducesResponseType<PaginatedResponse<AutomationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AutomationTokenDto>> result = await tokens.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Create an automation token; the response carries the one-time secret.</summary>
    [HttpPost("tokens")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<IssuedAutomationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        Guid channelId,
        [FromBody] CreateAutomationTokenRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await tokens.CreateAsync(channelId, caller, request, ct));
    }

    /// <summary>Rotate a token's secret; the old secret stops working immediately.</summary>
    [HttpPost("tokens/{tokenId:guid}/rotate")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<IssuedAutomationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rotate(Guid channelId, Guid tokenId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await tokens.RotateAsync(channelId, tokenId, caller, ct));
    }

    /// <summary>The subscribable public event catalog — the wire names an events-scoped token may stream.</summary>
    [HttpGet("events/catalog")]
    [RequireAction("automation:tokens:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<AutomationEventCatalogItem>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetEventCatalog(Guid channelId, CancellationToken ct) =>
        ResultResponse(await tokens.GetEventCatalogAsync(ct));

    /// <summary>Revoke a token (tombstone — the row stays for the audit trail).</summary>
    [HttpDelete("tokens/{tokenId:guid}")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(Guid channelId, Guid tokenId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await tokens.RevokeAsync(channelId, tokenId, caller, ct));
    }
}
