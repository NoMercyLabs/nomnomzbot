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

/// <summary>A named pick list written out as one text: joined, joined with "and", sorted. Catalog section 7.8.</summary>
internal sealed class ListJoinHelpers()
    : ListHelperGroup(
        ListHelperRow.OfItems("list.join", "chips, cookies, fruit", 0, 1, Join),
        ListHelperRow.OfItems("list.join.and", "chips, cookies and fruit", 0, 0, JoinAnd),
        ListHelperRow.OfItems(
            "list.sorted",
            "chips, cookies, fruit",
            0,
            0,
            (items, _) =>
                ListText.Join(
                    items.Order(StringComparer.OrdinalIgnoreCase),
                    ListText.DefaultSeparator
                )
        )
    )
{
    private static string Join(IReadOnlyList<string> items, IReadOnlyList<string> extra) =>
        ListText.Join(items, extra.Count == 0 ? ListText.DefaultSeparator : extra[0]);

    /// <summary><c>a, b and c</c>; two entries read <c>a and b</c>, one reads as itself.</summary>
    internal static string JoinAnd(IReadOnlyList<string> items, IReadOnlyList<string> extra) =>
        items.Count < 2
            ? ListText.Join(items, ListText.DefaultSeparator)
            : ListText.Join(items.Take(items.Count - 1), ListText.DefaultSeparator)
                + " and "
                + items[^1];
}
