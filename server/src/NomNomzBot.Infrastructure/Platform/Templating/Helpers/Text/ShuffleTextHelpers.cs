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

/// <summary>
/// The random text rows: shuffle the words, shuffle the text elements, pick one word. The provider builds the
/// group with the shared random source; a test hands it a seeded <see cref="Random"/>.
/// </summary>
internal sealed class ShuffleTextHelpers(Random random)
    : TextHelperGroup(
        TextHelperRow.OfText(
            "text.words.shuffle",
            "c a b",
            text => TextArguments.Join(Shuffled(TextArguments.Words(text), random))
        ),
        TextHelperRow.OfText(
            "text.chars.shuffle",
            "bca",
            text => string.Concat(Shuffled(TextArguments.Elements(text), random))
        ),
        TextHelperRow.OfText(
            "text.word.random",
            "b",
            text => PickOne(TextArguments.Words(text), random)
        )
    )
{
    public ShuffleTextHelpers()
        : this(Random.Shared) { }

    /// <summary>Fisher-Yates over a copy.</summary>
    private static List<string> Shuffled(List<string> items, Random random)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        return items;
    }

    private static string PickOne(List<string> words, Random random) =>
        words.Count == 0 ? "" : words[random.Next(words.Count)];
}
