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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel owner's own look in Twitch chat: name color and worn badges. Only the owner may read or
/// change it, because the call acts on the owner's Twitch account, not on the channel.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat-identity")]
[Authorize]
[Tags("Chat")]
public class ChatIdentityController : BaseController
{
    private readonly IChatIdentityService _service;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IViewAsScope _viewAs;

    public ChatIdentityController(
        IChatIdentityService service,
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IViewAsScope viewAs
    )
    {
        _service = service;
        _db = db;
        _currentUser = currentUser;
        _viewAs = viewAs;
    }

    /// <summary>The owner's chat color, the named colors, and the badges on their latest chat line.</summary>
    [HttpGet]
    [RequireAction("chat:read")]
    [ProducesResponseType<StatusResponseDto<ChatIdentityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid id))
            return BadRequestResponse("Invalid channel id.");

        if (!await IsOwnerAsync(id, ct))
            return UnauthorizedResponse("Only the channel owner can see their chat identity.");

        return ResultResponse(await _service.GetAsync(id, ct));
    }

    /// <summary>Set the owner's chat color: a named color, or a hex value for Turbo and Prime users.</summary>
    [HttpPut("color")]
    [RequireAction("chat:read")]
    [ProducesResponseType<StatusResponseDto<ChatIdentityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetColor(
        string channelId,
        [FromBody] UpdateChatColorDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid id))
            return BadRequestResponse("Invalid channel id.");

        if (!await IsOwnerAsync(id, ct))
            return UnauthorizedResponse("Only the channel owner can change their chat color.");

        return ResultResponse(await _service.SetColorAsync(id, request.Color, ct));
    }

    private async Task<bool> IsOwnerAsync(Guid channelId, CancellationToken ct)
    {
        // Viewing as a lower role, the owner is not the owner for this write.
        if (
            _viewAs.CeilingLevel is not null
            || !Guid.TryParse(_currentUser.UserId, out Guid userId)
        )
            return false;

        return await _db.Channels.AnyAsync(c => c.Id == channelId && c.OwnerUserId == userId, ct);
    }
}
