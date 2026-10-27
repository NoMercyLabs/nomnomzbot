// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>The one place a number becomes text: invariant, rounded, no trailing zeros.</summary>
internal static class NumberText
{
    private static readonly string[] CompactSuffixes = ["", "k", "M", "B", "T"];

    /// <summary>The number with at most <see cref="NumberArguments.MaxPlaces"/> decimals and no trailing zeros.</summary>
    public static string Plain(decimal value)
    {
        decimal rounded = Round(value, NumberArguments.MaxPlaces);
        return rounded == 0 ? "0" : rounded.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    /// <summary>The text of a yes or no that <c>{if:...}</c> reads: <c>true</c> or <c>false</c>.</summary>
    public static string Flag(bool value) => value ? "true" : "false";

    /// <summary>Rounds half away from zero, the rule of every rounding row.</summary>
    public static decimal Round(decimal value, int places) =>
        Math.Round(value, places, MidpointRounding.AwayFromZero);

    /// <summary>1234 becomes <c>1.2k</c>, 3400000 <c>3.4M</c>, 2100000000 <c>2.1B</c>; a number below 1000 stays as it is.</summary>
    public static string Compact(decimal value)
    {
        decimal scaled = Math.Abs(value);
        int tier = 0;
        while (tier < CompactSuffixes.Length - 1 && Round(scaled, 1) >= 1000)
        {
            scaled /= 1000;
            tier++;
        }

        decimal rounded = Round(scaled, 1);
        string text = rounded.ToString("0.#", CultureInfo.InvariantCulture) + CompactSuffixes[tier];
        return value < 0 && rounded != 0 ? "-" + text : text;
    }
}
