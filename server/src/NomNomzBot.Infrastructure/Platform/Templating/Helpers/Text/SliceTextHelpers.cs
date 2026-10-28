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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Taking a piece of the text by text elements (first, last, a range), the first letter of each word and the
/// line forms. Positions are 1-based; a piece past the end is shorter, never an error.
/// </summary>
internal sealed class SliceTextHelpers()
    : TextHelperGroup(
        new TextHelperRow(
            "text.first",
            "hel",
            args => Piece(args, (elements, n) => elements.Take(n))
        ),
        new TextHelperRow(
            "text.last",
            "llo",
            args => Piece(args, (elements, n) => elements.Skip(Math.Max(0, elements.Count - n)))
        ),
        new TextHelperRow("text.substring", "ell", Substring),
        new TextHelperRow("text.length.fit", "hel", Fit),
        TextHelperRow.OfText("text.initials", "BW", Initials),
        TextHelperRow.OfText("text.lines.first", "a", FirstLine),
        new TextHelperRow("text.lines.join", "a / b", JoinLines)
    )
{
    /// <summary>The <c>&lt;n&gt;|&lt;t&gt;</c> forms: <paramref name="select"/> gets the elements and n.</summary>
    private static string? Piece(
        IReadOnlyList<string> args,
        Func<List<string>, int, IEnumerable<string>> select
    )
    {
        if (args.Count != 2 || !TextArguments.TryCount(args[0], out int n))
            return null;

        return string.Concat(select(TextArguments.Elements(args[1]), n));
    }

    /// <summary><c>{text.substring:from|len|t}</c>: len elements from the 1-based position from.</summary>
    private static string? Substring(IReadOnlyList<string> args)
    {
        if (
            args.Count != 3
            || !TextArguments.TryCount(args[0], out int from)
            || from < 1
            || !TextArguments.TryCount(args[1], out int length)
        )
            return null;

        return string.Concat(TextArguments.Elements(args[2]).Skip(from - 1).Take(length));
    }

    /// <summary><c>{text.length.fit:t|n}</c>: at most n elements, with no ending mark.</summary>
    private static string? Fit(IReadOnlyList<string> args) =>
        args.Count == 2 && TextArguments.TryCount(args[1], out int n)
            ? string.Concat(TextArguments.Elements(args[0]).Take(n))
            : null;

    private static string Initials(string text) =>
        string.Concat(
            TextArguments.Words(text).Select(w => TextArguments.Elements(w)[0].ToUpperInvariant())
        );

    private static string FirstLine(string text) =>
        TextArguments.Literal(text).Split('\n')[0].TrimEnd('\r');

    /// <summary><c>{text.lines.join:t|sep}</c>: the lines of t joined with sep (default one space).</summary>
    private static string? JoinLines(IReadOnlyList<string> args)
    {
        if (args.Count is < 1 or > 2)
            return null;

        string[] lines = TextArguments
            .Literal(args[0])
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();
        return string.Join(TextArguments.TextAt(args, 1, " "), lines);
    }
}
