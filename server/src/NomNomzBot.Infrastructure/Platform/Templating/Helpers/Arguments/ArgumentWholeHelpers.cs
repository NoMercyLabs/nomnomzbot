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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>The command's arguments as a whole: how many, the first, the last, the rest, and quoted or URL-encoded.</summary>
internal sealed class ArgumentWholeHelpers()
    : ArgumentHelperGroup(
        ArgumentRow.Exact(
            "args.count",
            "4",
            c => c.Words.Count.ToString(CultureInfo.InvariantCulture)
        ),
        ArgumentRow.Exact("args.isEmpty", "false", c => Flag(c.Words.Count == 0)),
        ArgumentRow.Exact("args.first", "@ViewerTwo", c => c.Words.At(1) ?? string.Empty),
        ArgumentRow.Exact("args.last", "world", c => c.Words.At(c.Words.Count) ?? string.Empty),
        ArgumentRow.Exact("args.rest", "hello big world", c => c.Words.Slice(2, c.Words.Count)),
        ArgumentRow.Exact("args.all.quoted", "\"@ViewerTwo hello big world\"", Quoted),
        ArgumentRow.Exact(
            "args.all.urlEncoded",
            "%40ViewerTwo%20hello%20big%20world",
            c => Uri.EscapeDataString(c.Words.Text)
        )
    )
{
    /// <summary>The text of a yes/no row, as <c>if</c> reads it.</summary>
    internal static string Flag(bool value) => value ? "true" : "false";

    private static string Quoted(ArgumentCall call) => "\"" + call.Words.Text + "\"";
}
