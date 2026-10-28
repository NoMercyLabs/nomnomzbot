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
    Application.Abstractions.Auth.ICurrentUserService currentUser
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
    [NotDestructive(
        "Revokes one AutomationApiToken row; no entity carries an AutomationApiTokenId FK. A new token can be issued immediately."
    )]
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
