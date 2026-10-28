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
