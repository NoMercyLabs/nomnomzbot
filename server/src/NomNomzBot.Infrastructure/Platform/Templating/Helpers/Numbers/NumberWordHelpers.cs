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
using System.Text.RegularExpressions;
using Humanizer;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>A whole number as ordinal, words or Roman numeral (Humanizer), in the channel language where the row says so.</summary>
internal sealed partial class NumberWordHelpers()
    : NumberHelperGroup(
        WholeInt("num.ordinal", "3rd", (n, culture) => n.Ordinalize(culture)),
        WholeInt("num.ordinal.words", "third", (n, culture) => n.ToOrdinalWords(culture)),
        WholeInt("num.words", "forty-two", (n, culture) => n.ToWords(culture)),
        WholeInt("num.roman", "MMXXVI", (n, _) => ToRoman(n)),
        NumberHelperRow.Of("num.fromRoman", "14", FromRoman)
    )
{
    private const int MaxRoman = 3999;

    private static NumberHelperRow WholeInt(
        string key,
        string sample,
        Func<int, CultureInfo, string?> render
    ) =>
        NumberHelperRow.WithCulture(
            key,
            sample,
            (args, culture) =>
                args is [string text]
                && NumberArguments.TryWhole(text, out long whole)
                && whole is >= int.MinValue and <= int.MaxValue
                    ? render((int)whole, culture)
                    : null
        );

    private static string? ToRoman(int value) =>
        value is >= 1 and <= MaxRoman ? value.ToRoman() : null;

    /// <summary><c>num.fromRoman:r</c>: a Roman numeral in either letter case; a non-canonical one such as IIII has no answer.</summary>
    private static string? FromRoman(IReadOnlyList<string> args)
    {
        if (args.Count != 1 || !RomanLetters().IsMatch(args[0]))
            return null;

        string numeral = args[0].ToUpperInvariant();
        int value = numeral.FromRoman();
        return value.ToRoman() == numeral ? value.ToString(CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex("^[IVXLCDMivxlcdm]+$")]
    private static partial Regex RomanLetters();
}
