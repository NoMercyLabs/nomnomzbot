// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;

namespace NomNomzBot.Application.Economy.Services;

/// <summary>
/// Mini-games + fun-money gambling (economy.md §3.5). The bet currency cannot be bought or cashed out, so this
/// is NOT regulated gambling — the 18+ gate is an OPTIONAL, off-by-default per-game streamer toggle, engaged only
/// when <c>Requires18Plus=true</c>.
/// </summary>
public interface IGameService
{
    Task<Result<IReadOnlyList<GameConfigDto>>> ListGamesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Upserts a game config by <c>(BroadcasterId, GameType)</c>; validates odds + bet bounds; gambling games default disabled.</summary>
    Task<Result<GameConfigDto>> UpsertGameAsync(
        Guid broadcasterId,
        UpsertGameConfigRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// Settles one play: loads the config (<c>GAMBLING_DISABLED</c> if disabled); when (and only when)
    /// <c>Requires18Plus</c>, verifies the 18+ gate (<c>AGE_CONSENT_REQUIRED</c>); checks the permission floor,
    /// bet range (<c>BET_OUT_OF_RANGE</c>), and cooldown (<c>ON_COOLDOWN</c>); debits the bet
    /// (<c>INSUFFICIENT_FUNDS</c> bubbles), rolls the outcome with a CSPRNG, credits any payout, records the
    /// play, and publishes the event.
    /// </summary>
    Task<Result<GamePlayResultDto>> PlayAsync(
        Guid broadcasterId,
        PlayGameRequest request,
        CancellationToken ct = default
    );

    Task<Result<PagedList<GamePlayDto>>> GetGameHistoryAsync(
        Guid broadcasterId,
        GameHistoryFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
