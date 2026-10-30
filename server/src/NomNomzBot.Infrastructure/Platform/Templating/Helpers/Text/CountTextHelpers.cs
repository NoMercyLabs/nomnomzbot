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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Counting: words, text elements, letters, lines and occurrences.</summary>
internal sealed class CountTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText(
            "text.words.count",
            "3",
            text => Number(TextArguments.Words(text).Count)
        ),
        TextHelperRow.OfText(
            "text.chars.count",
            "5",
            text => Number(TextArguments.Elements(text).Count)
        ),
        TextHelperRow.OfText("text.letters.count", "2", text => Number(Letters(text))),
        TextHelperRow.OfText("text.lines.count", "2", text => Number(Lines(text))),
        new TextHelperRow("text.count", "3", Occurrences)
    )
{
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int Letters(string text) =>
        TextArguments.Elements(text).Count(e => char.IsLetter(e, 0));

    /// <summary>Lines split on a new line; the empty text has none.</summary>
    private static int Lines(string text) => text.Length == 0 ? 0 : text.Split('\n').Length;

    /// <summary><c>{text.count:t|find}</c>: non-overlapping, case-sensitive matches.</summary>
    private static string? Occurrences(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || args[1].Length == 0)
            return null;

        string find = args[1];
        int count = 0;
        for (int at = args[0].IndexOf(find, StringComparison.Ordinal); at >= 0; count++)
            at = args[0].IndexOf(find, at + find.Length, StringComparison.Ordinal);
        return Number(count);
    }
}
