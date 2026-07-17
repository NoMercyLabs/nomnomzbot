// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Economy.Enums;

/// <summary>
/// The class of a game (economy.md K.7). <see cref="Gambling"/> games are wager-and-chance and gate behind the
/// optional 18+ age consent; <see cref="Minigame"/> games are deterministic/skill and do not.
/// </summary>
public enum GameCategory
{
    Minigame,
    Gambling,
}

/// <summary>The resolved result of one play (economy.md K.9).</summary>
public enum GameOutcome
{
    Win,
    Lose,
    Push,
    Jackpot,
}

/// <summary>
/// The lifecycle of a live overlay game session (live-games.md K.9a). <see cref="Lobby"/>, <see cref="Running"/>,
/// and <see cref="Resolving"/> are non-terminal (at most one per channel, service-enforced — D7);
/// <see cref="Settled"/> and <see cref="Cancelled"/> are terminal and accumulate as history.
/// </summary>
public enum GameSessionStatus
{
    Lobby,
    Running,
    Resolving,
    Settled,
    Cancelled,
}
