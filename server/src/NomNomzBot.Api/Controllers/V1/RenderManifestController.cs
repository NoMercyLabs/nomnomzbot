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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Serves the dashboard render manifest — one authenticated call that aggregates everything the
/// shell needs to render for the active channel (resolved access, feature toggles, integration
/// connection states, and outstanding Twitch scope gaps), so the client boots with a single request
/// instead of four.
/// <para>
/// Gated by entry (Gate 1) only — like <c>roles/effective/me</c>, this is self-introspection a pure
/// participant must be able to make (to learn they have no management role and be routed to the
/// participation surface). The manifest then reveals each aggregated section ONLY where the caller's
/// own resolved access clears that surface's Gate-2 read floor, so it never discloses what an
/// individual endpoint would have withheld.
/// </para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}")]
[Authorize]
[Tags("Render Manifest")]
public class RenderManifestController(
    IRenderManifestService manifestService,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>The full render manifest for the channel as seen by the authenticated caller.</summary>
    [HttpGet("render-manifest")]
    [ProducesResponseType<StatusResponseDto<RenderManifestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetManifest(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();

        Result<RenderManifestDto> result = await manifestService.GetManifestAsync(
            caller,
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }
}
