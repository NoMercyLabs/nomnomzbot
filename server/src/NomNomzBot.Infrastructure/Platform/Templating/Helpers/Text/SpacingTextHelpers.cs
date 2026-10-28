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

/// <summary>Spacing and trimming: spread letters apart, trim the ends, squeeze inner whitespace, reverse.</summary>
internal sealed class SpacingTextHelpers()
    : TextHelperGroup(
        new TextHelperRow("text.spaced", "h y p e", Spaced),
        TextHelperRow.OfText("text.trim", "hi", text => text.Trim()),
        TextHelperRow.OfText(
            "text.squish",
            "a b c",
            text => TextArguments.Join(TextArguments.Words(text))
        ),
        TextHelperRow.OfText("text.reverse", "cba", Reverse),
        TextHelperRow.OfText(
            "text.reverse.words",
            "world big hello",
            text => TextArguments.Join(TextArguments.Words(text).AsEnumerable().Reverse())
        )
    )
{
    /// <summary><c>{text.spaced:t}</c> or <c>{text.spaced:t|sep}</c>: the separator goes between text elements.</summary>
    private static string? Spaced(IReadOnlyList<string> args) =>
        args.Count is 1 or 2
            ? string.Join(TextArguments.TextAt(args, 1, " "), TextArguments.Elements(args[0]))
            : null;

    private static string Reverse(string text)
    {
        List<string> elements = TextArguments.Elements(text);
        elements.Reverse();
        return string.Concat(elements);
    }
}
