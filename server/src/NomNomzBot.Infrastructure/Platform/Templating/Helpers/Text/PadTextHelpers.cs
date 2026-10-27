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

/// <summary>Padding to a width, centering, and repeating.</summary>
internal sealed class PadTextHelpers()
    : TextHelperGroup(
        new TextHelperRow("text.pad.left", "007", args => Pad(args, left: 1, right: 0)),
        new TextHelperRow("text.pad.right", "ab..", args => Pad(args, left: 0, right: 1)),
        new TextHelperRow("text.center", "**hi**", args => Pad(args, left: 1, right: 1)),
        new TextHelperRow("text.repeat", "ha ha ha", Repeat)
    )
{
    /// <summary>
    /// <c>{text.pad.left:t|n[|c]}</c> and its mirrors: fills to n text elements with c (default a space).
    /// The weights split the fill between the sides; a leftover element goes to the right side.
    /// </summary>
    private static string? Pad(IReadOnlyList<string> args, int left, int right)
    {
        if (args.Count is < 2 or > 3 || !TextArguments.TryCount(args[1], out int width))
            return null;

        int missing = Math.Max(0, width - TextArguments.Elements(args[0]).Count);
        int leftCount = missing * left / (left + right);
        string pad = TextArguments.TextAt(args, 2, " ");
        return TextArguments.Fill(pad, leftCount)
            + args[0]
            + TextArguments.Fill(pad, missing - leftCount);
    }

    /// <summary><c>{text.repeat:n|t[|sep]}</c>: t n times, with sep between the copies.</summary>
    private static string? Repeat(IReadOnlyList<string> args)
    {
        if (args.Count is < 2 or > 3 || !TextArguments.TryCount(args[0], out int count))
            return null;

        return string.Join(TextArguments.TextAt(args, 2, ""), Enumerable.Repeat(args[1], count));
    }
}
