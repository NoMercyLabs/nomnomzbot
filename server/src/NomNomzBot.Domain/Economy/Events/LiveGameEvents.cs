// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Events;

/// <summary>A live overlay game round opened — <c>games.live.started</c> (live-games.md §2).</summary>
public sealed class LiveGameStartedEvent : DomainEventBase
{
    /// <summary>The id of the game session.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>The kind of game, as text.</summary>
    public required string GameType { get; init; }

    /// <summary>The id of the user who started the game. Empty when the system started it.</summary>
    public Guid? StartedByUserId { get; init; }
}

/// <summary>
/// A live round settled — <c>games.live.resolved</c>. Per-participant currency outcomes are carried by
/// economy's per-row <c>GamePlayedEvent</c>s, not re-emitted here.
/// </summary>
public sealed class LiveGameResolvedEvent : DomainEventBase
{
    /// <summary>The id of the game session.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>The kind of game, as text.</summary>
    public required string GameType { get; init; }

    /// <summary>How many viewers took part.</summary>
    public required int ParticipantCount { get; init; }

    /// <summary>How many viewers won.</summary>
    public required int WinnerCount { get; init; }

    /// <summary>The total currency paid out to all winners.</summary>
    public required long TotalPaidOut { get; init; }
}

/// <summary>A live round cancelled — <c>games.live.cancelled</c>. Reason: <c>host_cancel</c> | <c>startup_sweep</c> | <c>min_players_unmet</c>.</summary>
public sealed class LiveGameCancelledEvent : DomainEventBase
{
    /// <summary>The id of the game session.</summary>
    public required Guid SessionId { get; init; }

    /// <summary>The kind of game, as text.</summary>
    public required string GameType { get; init; }

    /// <summary>A short text that explains why the game was canceled.</summary>
    public required string Reason { get; init; }
}
