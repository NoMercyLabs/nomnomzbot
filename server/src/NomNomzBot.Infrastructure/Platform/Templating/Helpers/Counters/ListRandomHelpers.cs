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
