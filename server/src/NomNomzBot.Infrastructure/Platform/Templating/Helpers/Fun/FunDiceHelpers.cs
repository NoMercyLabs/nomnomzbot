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

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>The dice rows: <c>fun.dice</c> (the total), <c>fun.dice.detail</c> (each roll) and <c>fun.dice.emoji</c> (die faces).</summary>
internal sealed class FunDiceHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.dice", "4", Total),
        FunRow.Pure("fun.dice.detail", "3 + 5 = 8", Detail),
        FunRow.Pure("fun.dice.emoji", "⚄", Faces)
    )
{
    private const int MaxFaces = 20;

    /// <summary><c>dice[:sides]</c> or <c>dice:NdS</c>: the sum of the rolls.</summary>
    private static string? Total(FunCall call) =>
        FunDiceSpec.TryParse(call.Args, out FunDiceSpec spec)
            ? spec.Roll(call.Random).Sum().ToString(CultureInfo.InvariantCulture)
            : null;

    /// <summary><c>dice.detail:NdS</c>: every roll, then the total. One die is just its number.</summary>
    private static string? Detail(FunCall call)
    {
        if (!FunDiceSpec.TryParse(call.Args, out FunDiceSpec spec))
            return null;

        int[] rolls = spec.Roll(call.Random);
        string joined = string.Join(" + ", rolls);
        return rolls.Length == 1 ? joined : $"{joined} = {rolls.Sum()}";
    }

    /// <summary><c>dice.emoji[:n]</c>: one die face, or n of them (1 to 20) separated by spaces.</summary>
    private static string? Faces(FunCall call)
    {
        int count = 1;
        bool fits =
            call.Args.Count == 0
            || (
                call.Args.Count == 1
                && FunDiceSpec.TryWhole(call.Args[0], out count)
                && count is >= 1 and <= MaxFaces
            );
        return fits
            ? string.Join(
                ' ',
                Enumerable
                    .Range(0, count)
                    .Select(_ => FunCatalog.DieFaces[call.Random.Next(FunCatalog.DieFaces.Length)])
            )
            : null;
    }
}
