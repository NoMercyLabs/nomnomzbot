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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Device pairing, management side (stream-deck.md §4): the dashboard mints a short-lived single-use
/// code under the caller's channel. The device's anonymous redeem lives on the data plane
/// (<c>POST /automation/v1/pair</c>, <see cref="AutomationDataController.RedeemPairing"/>). The
/// paired device then appears in the normal token list; revoking it unpairs (D3).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/automation")]
[Authorize]
[Tags("Automation")]
public class AutomationPairingController(
    IAutomationPairingService pairing,
    ICurrentTenantService currentTenant,
    ICurrentUserService currentUser
) : BaseController
{
    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);

    /// <summary>Mint a pairing code for the caller's channel (single-use, ~5 minutes).</summary>
    [HttpPost("pair-codes")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<PairingCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MintCode(
        [FromBody] MintPairingCodeRequest request,
        CancellationToken ct
    )
    {
        if (currentTenant.BroadcasterId is not Guid broadcasterId)
            return BadRequestResponse("No channel resolved for this request.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await pairing.MintCodeAsync(broadcasterId, caller, request, ct));
    }
}
