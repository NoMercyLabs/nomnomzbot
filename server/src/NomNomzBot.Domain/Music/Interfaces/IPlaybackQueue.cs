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

namespace NomNomzBot.Domain.Music.Interfaces;

/// <summary>
/// The fair queue as the playback director sees it. The director never picks an item itself: it reads
/// the head and removes or re-queues rows by id.
/// </summary>
public interface IPlaybackQueue
{
    /// <summary>The next item by fair order, or null when the queue is empty.</summary>
    PlaybackItem? Head { get; }

    /// <summary>All rows in fair order.</summary>
    IReadOnlyList<PlaybackItem> Snapshot { get; }

    /// <summary>Removes only the row with this id. Returns false when no row has it.</summary>
    bool Remove(string itemId);

    /// <summary>Puts the item at the head and leaves the order below it unchanged.</summary>
    void RequeueAtHead(PlaybackItem item);
}
