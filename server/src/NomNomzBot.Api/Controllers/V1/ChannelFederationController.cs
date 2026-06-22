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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.DTOs.Federation;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// A channel's federation opt-ins (federation-oidc.md §5). Default-deny + SuperMod-gated: enabling an opt-in is
/// the explicit allow to share/accept a flow with peers. The acting user is bound from the caller, never the body.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/federation")]
[Authorize]
[Tags("Federation")]
public class ChannelFederationController(
    IFederationOptInService optIns,
    ICurrentUserService currentUser
) : BaseController
{
    [HttpGet("opt-ins")]
    [RequireAction("federation:optin:read")]
    public async Task<IActionResult> List(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await optIns.ListAsync(broadcasterId, ct));
    }

    [HttpPut("opt-ins")]
    [RequireAction("federation:optin:write")]
    public async Task<IActionResult> Upsert(
        string channelId,
        [FromBody] UpsertChannelFederationOptInRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await optIns.UpsertAsync(broadcasterId, request, caller, ct));
    }

    [HttpDelete("opt-ins/{optInId:guid}")]
    [RequireAction("federation:optin:delete")]
    public async Task<IActionResult> Disable(string channelId, Guid optInId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await optIns.DisableAsync(broadcasterId, optInId, caller, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
