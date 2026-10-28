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
