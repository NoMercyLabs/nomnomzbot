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
using NomNomzBot.Application.Contracts.Egress;
using NomNomzBot.Application.DTOs.Egress;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's outbound egress allowlist: the hosts the bot may call for outbound webhooks and custom data
/// sources. This is the SSRF boundary, so reads are Moderator-floored and writes Editor-floored. The acting user
/// is bound from the caller.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/egress-allowlist")]
[Authorize]
[Tags("EgressAllowlist")]
public class EgressAllowlistController(
    IHttpEgressAllowlistService allowlist,
    Application.Abstractions.Auth.ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List the channel's approved outbound hosts, paginated.</summary>
    [HttpGet]
    [RequireAction("egress:allowlist:read")]
    [ProducesResponseType<PaginatedResponse<HttpEgressAllowlistDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<HttpEgressAllowlistDto>> result = await allowlist.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Approve an outbound host for the channel, recording the caller as the approver.</summary>
    [HttpPost]
    [RequireAction("egress:allowlist:write")]
    [ProducesResponseType<StatusResponseDto<HttpEgressAllowlistDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        Guid channelId,
        [FromBody] CreateHttpEgressAllowlistRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await allowlist.CreateAsync(channelId, caller, request, ct));
    }

    /// <summary>Turn an approved host on or off. A disabled host is refused by every outbound consumer.</summary>
    [HttpPut("{allowlistId:guid}/enabled")]
    [RequireAction("egress:allowlist:write")]
    [ProducesResponseType<StatusResponseDto<HttpEgressAllowlistDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEnabled(
        Guid channelId,
        Guid allowlistId,
        [FromBody] SetHttpEgressAllowlistEnabledRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await allowlist.SetEnabledAsync(channelId, allowlistId, request.IsEnabled, ct)
        );
}
