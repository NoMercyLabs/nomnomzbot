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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>Rounding: to a number of places (half away from zero) and cutting the fraction off.</summary>
internal sealed class NumberRoundingHelpers()
    : NumberHelperGroup(
        NumberHelperRow.Of("num.round", "2.46", RoundTo),
        NumberHelperRow.OfNumber("num.truncate", "2", n => NumberText.Plain(decimal.Truncate(n)))
    )
{
    /// <summary><c>num.round:n[|places]</c>: places default to 0.</summary>
    private static string? RoundTo(IReadOnlyList<string> args)
    {
        if (
            args.Count > 2
            || args.Count == 0
            || !NumberArguments.TryNumber(args[0], out decimal value)
            || !NumberArguments.TryPlacesAt(args, 1, 0, out int places)
        )
            return null;

        return NumberText.Plain(NumberText.Round(value, places));
    }
}
