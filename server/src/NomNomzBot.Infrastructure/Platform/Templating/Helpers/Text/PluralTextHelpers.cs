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
using Humanizer;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Singular and plural forms picked by a number, and English word forms (<c>box</c> and <c>boxes</c>, the
/// possessive). The number is any whole number; exactly 1 (or -1) picks the singular. The root alias
/// <c>plural:</c> of <c>text.plural</c> is not claimed here: a group cannot add an alias, see the lane handoff.
/// </summary>
internal sealed class PluralTextHelpers()
    : TextHelperGroup(
        new TextHelperRow("text.plural", "subs", args => Choose(args, withCount: false)),
        new TextHelperRow("text.plural.count", "3 subs", args => Choose(args, withCount: true)),
        new TextHelperRow("text.plural.auto", "boxes", Auto),
        TextHelperRow.OfText("text.singular", "box", word => word.Singularize()),
        TextHelperRow.OfText("text.possessive", "James'", Possessive)
    )
{
    /// <summary><c>{text.plural:n|one|many}</c>; with <paramref name="withCount"/> the number leads the word.</summary>
    private static string? Choose(IReadOnlyList<string> args, bool withCount)
    {
        if (args.Count != 3 || !TryWhole(args[0], out long n))
            return null;

        string word = Math.Abs(n) == 1 ? args[1] : args[2];
        return withCount ? args[0] + " " + word : word;
    }

    /// <summary><c>{text.plural.auto:n|word}</c>: the word, pluralized by Humanizer unless n is 1.</summary>
    private static string? Auto(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !TryWhole(args[0], out long n) || args[1].Length == 0)
            return null;

        return Math.Abs(n) == 1 ? args[1] : args[1].Pluralize(inputIsKnownToBeSingular: false);
    }

    private static string Possessive(string name) =>
        name.EndsWith('s') || name.EndsWith('S') ? name + "'" : name + "'s";

    private static bool TryWhole(string value, out long number) =>
        long.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out number
        );
}
