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

namespace NomNomzBot.Application.Commands.Builtin.Personality;

public static partial class BuiltinResponseSlots
{
    /// <summary>
    /// The bot's own announcements of a live round (drop game, raffle). No command owns this
    /// group: the game engine speaks it at round open, settle and cancel.
    /// </summary>
    public static class LiveGame
    {
        public const string Key = "livegame";

        /// <summary>A round opened, no entry fee; <c>{game.name}</c>, <c>{game.join}</c>, <c>{game.lobby}</c> are set.</summary>
        public const string Opened = "opened";

        /// <summary>A round opened with an entry fee; adds <c>{game.minbet}</c>.</summary>
        public const string OpenedBet = "openedbet";

        /// <summary>A round paid out; <c>{game.name}</c>, <c>{game.winners}</c>, <c>{game.payout}</c> are set.</summary>
        public const string Settled = "settled";

        /// <summary>A round closed with too few players, bets refunded; <c>{game.name}</c> is set.</summary>
        public const string CancelledFewPlayers = "cancelledfewplayers";

        /// <summary>A round called off for any other reason, bets refunded; <c>{game.name}</c> is set.</summary>
        public const string Cancelled = "cancelled";
    }
}
