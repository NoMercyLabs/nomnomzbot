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
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>The action buttons a streamer pins to the Now playing panel.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/music/player-buttons")]
[Authorize]
[Tags("Music")]
public sealed class PlayerButtonsController(IPlayerButtonService buttons) : BaseController
{
    /// <summary>The channel's pinned player buttons, in display order.</summary>
    [RequireAction("music:queue:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<PlayerButtonDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Get(string channelId, CancellationToken ct) =>
        TryChannelId(channelId, out Guid broadcasterId)
            ? ResultResponse(await buttons.GetAsync(broadcasterId, ct))
            : BadRequestResponse("Invalid channel id.");

    /// <summary>Replace the channel's pinned player buttons.</summary>
    [RequireAction("music:config:write")]
    [HttpPut]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<PlayerButtonDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> Replace(
        string channelId,
        [FromBody] List<PlayerButtonDto> request,
        CancellationToken ct
    ) =>
        TryChannelId(channelId, out Guid broadcasterId)
            ? ResultResponse(await buttons.ReplaceAsync(broadcasterId, request, ct))
            : BadRequestResponse("Invalid channel id.");

    // The wire id is a ULID; a plain guid is accepted too.
    private static bool TryChannelId(string channelId, out Guid broadcasterId) =>
        GuidUlidCodec.TryDecode(channelId, out broadcasterId)
        || Guid.TryParse(channelId, out broadcasterId);
}
