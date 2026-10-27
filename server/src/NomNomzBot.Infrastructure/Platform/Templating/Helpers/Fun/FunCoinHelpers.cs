// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>The coin rows: <c>fun.coin</c> (heads or tails) and <c>fun.coin.emoji</c> (the same behind a coin).</summary>
internal sealed class FunCoinHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.coin", "heads", call => Flip(call, withCoin: false)),
        FunRow.Pure("fun.coin.emoji", "🪙 heads", call => Flip(call, withCoin: true))
    )
{
    private const string Coin = "🪙";

    private static string? Flip(FunCall call, bool withCoin)
    {
        if (call.Args.Count != 0)
            return null;

        string side = call.Random.Next(2) == 0 ? "heads" : "tails";
        return withCoin ? $"{Coin} {side}" : side;
    }
}
