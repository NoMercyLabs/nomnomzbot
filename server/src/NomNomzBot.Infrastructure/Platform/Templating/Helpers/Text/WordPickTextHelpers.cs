// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Picking words by position (1-based) or by length. A position past the end renders the empty text; a
/// position that is not a whole number of 1 or more is a bad argument.
/// </summary>
internal sealed class WordPickTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText(
            "text.word.first",
            "hello",
            text => TextArguments.Words(text).FirstOrDefault() ?? ""
        ),
        TextHelperRow.OfText(
            "text.word.last",
            "world",
            text => TextArguments.Words(text).LastOrDefault() ?? ""
        ),
        TextHelperRow.OfText("text.word.longest", "hello", Longest),
        new TextHelperRow(
            "text.word",
            "big",
            args => Pick(args, (words, n) => words.Skip(n - 1).Take(1))
        ),
        new TextHelperRow(
            "text.words.first",
            "hello big",
            args => Pick(args, (words, n) => words.Take(n))
        ),
        new TextHelperRow(
            "text.words.last",
            "big world",
            args => Pick(args, (words, n) => words.Skip(Math.Max(0, words.Count - n)))
        ),
        new TextHelperRow(
            "text.words.from",
            "big world",
            args => Pick(args, (words, n) => words.Skip(n - 1))
        ),
        new TextHelperRow("text.words.range", "hello big", Range)
    )
{
    private static string Longest(string text) =>
        TextArguments
            .Words(text)
            .OrderByDescending(w => TextArguments.Elements(w).Count)
            .FirstOrDefault()
        ?? "";

    /// <summary>The <c>&lt;n&gt;|&lt;t&gt;</c> forms: <paramref name="select"/> gets the words and n.</summary>
    private static string? Pick(
        IReadOnlyList<string> args,
        Func<List<string>, int, IEnumerable<string>> select
    )
    {
        if (args.Count != 2 || !TryPosition(args[0], out int n))
            return null;

        return TextArguments.Join(select(TextArguments.Words(args[1]), n));
    }

    /// <summary><c>{text.words.range:a|b|t}</c>: words a to b, both included.</summary>
    private static string? Range(IReadOnlyList<string> args)
    {
        if (
            args.Count != 3
            || !TryPosition(args[0], out int from)
            || !TryPosition(args[1], out int to)
        )
            return null;

        return TextArguments.Join(
            TextArguments.Words(args[2]).Skip(from - 1).Take(Math.Max(0, to - from + 1))
        );
    }

    private static bool TryPosition(string value, out int position) =>
        TextArguments.TryCount(value, out position) && position >= 1;
}
