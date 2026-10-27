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

/// <summary>Comparing two numbers: part of a whole, percent of a number, a ratio and the difference.</summary>
internal sealed class NumberPercentHelpers()
    : NumberHelperGroup(
        NumberHelperRow.Of("num.percent", "82%", Percent),
        NumberHelperRow.OfNumbers(
            "num.percentOf",
            "25",
            2,
            2,
            v => NumberText.Plain(v[1] * v[0] / 100)
        ),
        NumberHelperRow.Of("num.ratio", "1:4", Ratio),
        NumberHelperRow.OfNumbers(
            "num.diff",
            "300",
            2,
            2,
            v => NumberText.Plain(Math.Abs(v[0] - v[1]))
        )
    )
{
    /// <summary><c>num.percent:part|whole[|places]</c>: places default to 0; a whole of 0 has no answer.</summary>
    private static string? Percent(IReadOnlyList<string> args)
    {
        if (
            args.Count is < 2 or > 3
            || !NumberArguments.TryNumbers([.. args.Take(2)], out List<decimal> v)
            || v[1] == 0
            || !NumberArguments.TryPlacesAt(args, 2, 0, out int places)
        )
            return null;

        return NumberText.Plain(NumberText.Round(v[0] / v[1] * 100, places)) + "%";
    }

    /// <summary><c>num.ratio:a|b</c>: two whole numbers reduced by their greatest common divisor.</summary>
    private static string? Ratio(IReadOnlyList<string> args)
    {
        if (
            args.Count != 2
            || !NumberArguments.TryWhole(args[0], out long a)
            || !NumberArguments.TryWhole(args[1], out long b)
            || a < 0
            || b < 0
            || a + b == 0
        )
            return null;

        long divisor = GreatestCommonDivisor(a, b);
        return $"{a / divisor}:{b / divisor}";
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return a;
    }
}
