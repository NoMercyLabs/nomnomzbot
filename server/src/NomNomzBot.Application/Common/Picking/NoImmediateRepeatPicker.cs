// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;

namespace NomNomzBot.Application.Common.Picking;

/// <summary>
/// Picks a uniformly-random item from a pool, excluding only the item drawn last time for the same
/// <paramref name="key"/> — a pure uniform draw repeats back-to-back 1-in-N of the time, and to a viewer that
/// reads as the bot being broken rather than as chance, however small the pool. Excluding only the immediately
/// previous pick keeps every other item equally likely, so the pool still feels random; it neither cycles nor
/// exhausts. Shared by every "speak a random line/pick a random item" surface (random-response commands,
/// pick-lists, quotes, template random helpers, TTS voice selection) so they all get the same guarantee instead
/// of each hand-rolling it.
/// </summary>
public static class NoImmediateRepeatPicker
{
    /// <summary>Last index drawn per <c>key</c> — bounded by the number of distinct pools in use, and purely
    /// cosmetic: losing it on a restart costs nothing more than one possible repeat.</summary>
    private static readonly ConcurrentDictionary<string, int> LastIndexByKey = new();

    /// <summary>
    /// Draws an index into <paramref name="count"/>, never the one drawn last time for <paramref name="key"/>.
    /// <paramref name="key"/> scopes the memory — e.g. a per-broadcaster/per-command/per-list identity, so
    /// unrelated pools never influence each other. Throws if <paramref name="count"/> is not positive.
    /// </summary>
    public static int NextIndex(int count, string key)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "The pool must have at least one item."
            );
        if (count == 1)
            return 0;

        LastIndexByKey.TryGetValue(key, out int previous);
        int index = Random.Shared.Next(count - 1);
        // Map the drawn index around the previous one, so the previous item is the only one excluded and
        // the remaining N-1 stay uniformly likely.
        if (index >= previous)
            index++;
        LastIndexByKey[key] = index;
        return index;
    }

    /// <summary>Convenience wrapper over <see cref="NextIndex"/> for an in-hand list.</summary>
    public static T Pick<T>(IReadOnlyList<T> items, string key)
    {
        if (items.Count == 0)
            throw new ArgumentException("The pool must have at least one item.", nameof(items));
        return items[NextIndex(items.Count, key)];
    }
}
