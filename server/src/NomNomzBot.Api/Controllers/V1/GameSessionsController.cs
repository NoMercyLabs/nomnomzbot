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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Games;
using NomNomzBot.Application.Games.Dtos;
using NomNomzBot.Application.Games.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Live overlay game sessions (live-games.md §5) — the round lifecycle: start, watch, cancel, history, and
/// the discovered-game catalog. Moderator live-ops throughout; per-game CONFIG (odds/bets/enable) is not
/// here — it stays economy's <c>/economy/games</c> surface.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/games/sessions")]
[Authorize]
[Tags("Games — Live Sessions")]
public class GameSessionsController(
    ILiveGameEngine engine,
    ILiveGameCatalog catalog,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>The channel's current non-terminal session (404-style result when none).</summary>
    [HttpGet("active")]
    [RequireAction("games:session:read")]
    [ProducesResponseType<StatusResponseDto<GameSessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await engine.GetActiveAsync(broadcasterId, ct));
    }

    /// <summary>Page through settled/cancelled session history, filtered by game type and status.</summary>
    [HttpGet]
    [RequireAction("games:session:read")]
    [ProducesResponseType<PaginatedResponse<GameSessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        string channelId,
        [FromQuery] GameSessionFilter filter,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<GameSessionDto>> result = await engine.ListAsync(
            broadcasterId,
            filter,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Start a round of <c>GameType</c> — fails while another session is active (D7).</summary>
    [HttpPost]
    [RequireAction("games:session:start")]
    public async Task<IActionResult> Start(
        string channelId,
        [FromBody] StartLiveGameRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Guid? startedBy = TryGetCaller(out Guid caller) ? caller : null;
        return ResultResponse(
            await engine.StartAsync(
                broadcasterId,
                new StartLiveGameCommand(request.GameType, startedBy),
                ct
            )
        );
    }

    /// <summary>Cancel a non-terminal session — every entry fee is refunded.</summary>
    [HttpDelete("{sessionId:guid}")]
    [RequireAction("games:session:cancel")]
    public async Task<IActionResult> Cancel(string channelId, Guid sessionId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await engine.CancelAsync(broadcasterId, sessionId, ct));
    }

    /// <summary>Every discovered live game's manifest — what the dashboard can start.</summary>
    [HttpGet("catalog")]
    [RequireAction("games:session:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<LiveGameCatalogEntryDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetCatalog(string channelId)
    {
        if (!Guid.TryParse(channelId, out _))
            return BadRequestResponse("Invalid channel id.");
        IReadOnlyList<LiveGameCatalogEntryDto> entries =
        [
            .. catalog.All.Select(pair => ToCatalogEntry(pair.Key, pair.Value)),
        ];
        return ResultResponse(Result.Success(entries));
    }

    private static LiveGameCatalogEntryDto ToCatalogEntry(string gameKey, LiveGameManifest m) =>
        new(
            gameKey,
            m.DisplayName,
            m.InputKeywords,
            m.OverlayWidgetKey,
            m.MinPlayers,
            m.MaxPlayers,
            (int)m.LobbyWindow.TotalSeconds,
            m.TickInterval is TimeSpan tick ? (int)tick.TotalSeconds : null,
            m.RequiresEntryFee
        );

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
