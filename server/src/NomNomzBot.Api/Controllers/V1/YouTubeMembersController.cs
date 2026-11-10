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
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's YouTube members, the YouTube side of the Twitch subscriber list. Management plane
/// (<c>community:read</c>).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/community/youtube/members")]
[Authorize]
[Tags("Community")]
public class YouTubeMembersController(IYouTubeMembersService members) : BaseController
{
    private const int DefaultTake = 50;

    /// <summary>
    /// Read one page of the channel's current YouTube members with their level and member-since date. The
    /// first page also names the membership levels. <c>access</c> says why the list is empty when the channel
    /// is not connected, did not allow the member list, or has no memberships.
    /// </summary>
    [HttpGet]
    [RequireAction("community:read")]
    [ProducesResponseType<StatusResponseDto<YouTubeMembersDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(
        Guid channelId,
        [FromQuery] string? pageToken,
        [FromQuery] int? take,
        CancellationToken ct
    ) =>
        ResultResponse(
            await members.GetMembersAsync(channelId, pageToken, take ?? DefaultTake, ct)
        );
}
