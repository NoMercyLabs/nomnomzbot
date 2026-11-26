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

namespace NomNomzBot.Application.Games.Services;

/// <summary>
/// Tells the channel's chat about a live round in the channel's personality: a round opened (and which
/// word joins it), paid out, or was cancelled. A failed announcement never fails the round.
/// </summary>
public interface ILiveGameAnnouncer
{
    /// <summary>Chat line for a freshly opened lobby: game name, join word, lobby seconds, minimum bet.</summary>
    Task AnnounceOpenedAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        long minBet,
        CancellationToken ct = default
    );

    /// <summary>Chat line for a paid-out round: winner count and total paid.</summary>
    Task AnnounceSettledAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        int winnerCount,
        long totalPaidOut,
        CancellationToken ct = default
    );

    /// <summary>Chat line for a cancelled round; <c>min_players_unmet</c> gets its own wording.</summary>
    Task AnnounceCancelledAsync(
        Guid broadcasterId,
        LiveGameManifest manifest,
        string reason,
        CancellationToken ct = default
    );
}
