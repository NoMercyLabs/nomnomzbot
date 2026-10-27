// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
