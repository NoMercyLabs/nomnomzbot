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

/// <summary>Surrounding text with marks: any marks, quotes, parentheses.</summary>
internal sealed class WrapTextHelpers()
    : TextHelperGroup(
        new TextHelperRow("text.wrap", "[hi]", Wrap),
        TextHelperRow.OfText("text.quote", "\"hi\"", text => $"\"{text}\""),
        TextHelperRow.OfText("text.paren", "(hi)", text => $"({text})")
    )
{
    /// <summary><c>{text.wrap:t|l[|r]}</c>: l before and r after; r defaults to l.</summary>
    private static string? Wrap(IReadOnlyList<string> args)
    {
        if (args.Count is < 2 or > 3)
            return null;

        string left = TextArguments.TextAt(args, 1, "");
        return left + args[0] + TextArguments.TextAt(args, 2, left);
    }
}
