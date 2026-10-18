// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// The last items a channel played, newest last. The director uses it for Previous. Holds at most
/// <see cref="Capacity"/> items; the oldest drop first.
/// </summary>
public sealed class PlayedHistory
{
    public const int Capacity = 20;

    private readonly List<PlaybackItem> _items = [];

    public int Count => _items.Count;

    /// <summary>The most recently played item, or null when empty.</summary>
    public PlaybackItem? Last => _items.Count == 0 ? null : _items[^1];

    public void Push(PlaybackItem item)
    {
        _items.Add(item);
        if (_items.Count > Capacity)
            _items.RemoveAt(0);
    }

    /// <summary>Removes and returns the most recently played item, or null when empty.</summary>
    public PlaybackItem? Pop()
    {
        if (_items.Count == 0)
            return null;

        PlaybackItem last = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        return last;
    }
}
