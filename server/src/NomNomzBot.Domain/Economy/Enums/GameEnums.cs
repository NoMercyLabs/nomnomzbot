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
