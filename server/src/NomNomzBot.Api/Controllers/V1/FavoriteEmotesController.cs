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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The signed-in user's favorite emotes for the chat emote picker. Self-scoped: [Authorize] only, no
/// [RequireAction] — every route reads and writes only the caller's own rows, across every channel.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me/favorite-emotes")]
[Authorize]
[Tags("Users")]
public sealed class FavoriteEmotesController : BaseController
{
    private readonly IFavoriteEmoteService _favorites;
    private readonly ICurrentUserService _currentUser;

    public FavoriteEmotesController(
        IFavoriteEmoteService favorites,
        ICurrentUserService currentUser
    )
    {
        _favorites = favorites;
        _currentUser = currentUser;
    }

    /// <summary>The caller's favorite emotes, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<FavoriteEmoteDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid userId))
            return UnauthenticatedResponse();

        Result<IReadOnlyList<FavoriteEmoteDto>> result = await _favorites.ListAsync(userId, ct);
        return ResultResponse(result);
    }

    /// <summary>Add a favorite. Idempotent: the same emote twice keeps one row. At most 200 per user.</summary>
    [HttpPut]
    [ProducesResponseType<StatusResponseDto<FavoriteEmoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Add(
        [FromBody] SetFavoriteEmoteRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid userId))
            return UnauthenticatedResponse();

        Result<FavoriteEmoteDto> result = await _favorites.AddAsync(userId, request, ct);
        return ResultResponse(result);
    }

    /// <summary>Remove one favorite by provider and emote id. Removing one that is not there succeeds.</summary>
    [NotDestructive(
        "Deletes the caller's own favorite-emote row; nothing references it and the user can star the emote again."
    )]
    [HttpDelete("{provider}/{emoteId}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Remove(string provider, string emoteId, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid userId))
            return UnauthenticatedResponse();

        Result result = await _favorites.RemoveAsync(userId, provider, emoteId, ct);
        return ResultResponse(result);
    }
}
