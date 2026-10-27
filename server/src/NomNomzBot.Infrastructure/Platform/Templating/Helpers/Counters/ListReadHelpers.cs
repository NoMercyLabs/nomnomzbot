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

/// <summary>Reads from a named pick list: its length, one entry, a search. Catalog section 7.8.</summary>
internal sealed class ListReadHelpers()
    : ListHelperGroup(
        Pick(),
        ListHelperRow.OfItems("list.length", "3", 0, 0, (items, _) => ListText.Number(items.Count)),
        ListHelperRow.OfItems("list.item", "cookies", 1, 1, Item),
        ListHelperRow.OfItems("list.first", "chips", 0, 0, (items, _) => ListText.ItemAt(items, 1)),
        ListHelperRow.OfItems("list.last", "fruit", 0, 0, (items, _) => Last(items)),
        ListHelperRow.OfItems("list.contains", "true", 1, 1, Contains),
        ListHelperRow.OfItems("list.indexOf", "3", 1, 1, IndexOf)
    )
{
    /// <summary>
    /// <c>{list.pick:name}</c>: the service's own uniform pick, the same read the as-built
    /// <c>{list.pick.name}</c> uses. A missing or empty list renders nothing.
    /// </summary>
    private static ListHelperRow Pick() =>
        ListHelperRow.OfName(
            "list.pick",
            "chips",
            (name, call) => call.Source.PickAsync(name, call.Ct)
        );

    private static string? Item(IReadOnlyList<string> items, IReadOnlyList<string> extra) =>
        ListText.TryPosition(extra[0], out int position) ? ListText.ItemAt(items, position) : null;

    private static string Last(IReadOnlyList<string> items) =>
        items.Count == 0 ? string.Empty : items[^1];

    private static string Contains(IReadOnlyList<string> items, IReadOnlyList<string> extra) =>
        items.Contains(extra[0], StringComparer.OrdinalIgnoreCase) ? "true" : "false";

    /// <summary>The 1-based position of the first match (case-insensitive); 0 when absent.</summary>
    private static string IndexOf(IReadOnlyList<string> items, IReadOnlyList<string> extra)
    {
        int index = items
            .Select((item, i) => (item, i))
            .Where(pair => string.Equals(pair.item, extra[0], StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.i + 1)
            .FirstOrDefault();
        return ListText.Number(index);
    }
}
