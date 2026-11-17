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
        if (currentTenant.BroadcasterId is not { } broadcasterId)
            return BadRequestResponse("No channel resolved for this request.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await pairing.MintCodeAsync(broadcasterId, caller, request, ct));
    }

    /// <summary>
    /// Device-initiated pairing, step 2 (stream-deck.md D9): the link a device shows. It opens the dashboard's
    /// API devices page with the code filled in, where the dashboard's own login and theme apply (Stoney,
    /// 2026-11-17: the old server-rendered page could not see the dashboard login and did not match the theme).
    /// </summary>
    [HttpGet("pair/device/approve")]
    [AllowAnonymous]
    public IActionResult ApproveDevicePage([FromQuery] string? code) =>
        Redirect(
            string.IsNullOrEmpty(code)
                ? ApiDevicesPage
                : $"{ApiDevicesPage}?code={Uri.EscapeDataString(code)}"
        );

    /// <summary>The dashboard page where a device code is approved, in the bot's own theme and login.</summary>
    private const string ApiDevicesPage = "/app/settings/api-devices";

    /// <summary>Device-initiated pairing, step 2b: the approval page's form POSTs here.</summary>
    [HttpPost("pair/device/approve")]
    [RequireAction("automation:tokens:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveDevice(
        [FromBody] ApproveDeviceRequest request,
        CancellationToken ct
    )
    {
        if (currentTenant.BroadcasterId is not { } broadcasterId)
            return BadRequestResponse("No channel resolved for this request.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        Result result = await pairing.ApproveDeviceAsync(
            broadcasterId,
            caller,
            request.UserCode,
            ct
        );
        return ResultResponse(result);
    }
}
