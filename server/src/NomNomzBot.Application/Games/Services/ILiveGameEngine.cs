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
using NomNomzBot.Application.Games.Dtos;

namespace NomNomzBot.Application.Games.Services;

/// <summary>
/// The generic live-game orchestrator (live-games.md §3.1): one engine runs every discovered
/// <see cref="ILiveGame"/> — lobby window, ticks, chat-input routing, resolution, and crash recovery. The
/// engine brokers ALL side effects (DB, currency via <c>IGameService</c>, overlay frames, events) so games
/// stay pure logic.
/// </summary>
public interface ILiveGameEngine
{
    /// <summary>
    /// Opens a session for <c>GameType</c> (must map to a discovered game with an enabled
    /// <c>GameConfig</c>). Fails <c>SESSION_ALREADY_ACTIVE</c> while a non-terminal session exists for the
    /// channel (D7); persists the <c>lobby</c> row, runs <c>OnStartAsync</c>, publishes
    /// <c>LiveGameStartedEvent</c>, and pushes the first overlay frame.
    /// </summary>
    Task<Result<GameSessionDto>> StartAsync(
        Guid broadcasterId,
        StartLiveGameCommand command,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cancels a non-terminal session: refunds entry-fee debits, sets <c>cancelled</c>, publishes
    /// <c>LiveGameCancelledEvent(host_cancel)</c>, and pushes a final overlay frame.
    /// </summary>
    Task<Result> CancelAsync(Guid broadcasterId, Guid sessionId, CancellationToken ct = default);

    /// <summary>The channel's current non-terminal session; <c>NOT_FOUND</c> when none.</summary>
    Task<Result<GameSessionDto>> GetActiveAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Settled/cancelled session history, tenant-filtered, paged.</summary>
    Task<Result<PagedList<GameSessionDto>>> ListAsync(
        Guid broadcasterId,
        GameSessionFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}

/// <summary>What starts a round: the game key and (when known) who asked for it.</summary>
public sealed record StartLiveGameCommand(string GameType, Guid? StartedByUserId);
