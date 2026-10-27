// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>Lists written inline as <c>a;b;c</c>: pure text rows that need no channel. Catalog section 7.8.</summary>
internal sealed class ListInlineHelpers()
    : ListHelperGroup(
        ListHelperRow.OfArguments(
            "list.inline.length",
            "3",
            args => args.Count == 1 ? ListText.Number(ListText.Inline(args[0]).Count) : null
        ),
        ListHelperRow.OfArguments("list.inline.item", "b", Item),
        ListHelperRow.OfArguments(
            "list.inline.join",
            "a + b",
            args => args.Count == 2 ? ListText.Join(ListText.Inline(args[1]), args[0]) : null
        )
    )
{
    private static string? Item(IReadOnlyList<string> args) =>
        args.Count == 2 && ListText.TryPosition(args[0], out int position)
            ? ListText.ItemAt(ListText.Inline(args[1]), position)
            : null;
}
