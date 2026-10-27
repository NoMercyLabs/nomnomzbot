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
