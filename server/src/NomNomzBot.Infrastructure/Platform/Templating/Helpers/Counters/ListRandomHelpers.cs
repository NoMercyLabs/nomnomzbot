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

/// <summary>The random-order rows of a named pick list. The random source is injected so a test can seed it.</summary>
internal sealed class ListRandomHelpers(Random random)
    : ListHelperGroup(
        ListHelperRow.OfItems(
            "list.shuffle",
            "fruit, chips, cookies",
            0,
            0,
            (items, _) => ListText.Join(Shuffled(items, random), ListText.DefaultSeparator)
        ),
        ListHelperRow.OfItems(
            "list.random.index",
            "2",
            0,
            0,
            (items, _) => ListText.Number(items.Count == 0 ? 0 : random.Next(items.Count) + 1)
        )
    )
{
    public ListRandomHelpers()
        : this(Random.Shared) { }

    /// <summary>Fisher-Yates over a copy.</summary>
    private static List<string> Shuffled(IReadOnlyList<string> items, Random random)
    {
        List<string> copy = [.. items];
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }
}
