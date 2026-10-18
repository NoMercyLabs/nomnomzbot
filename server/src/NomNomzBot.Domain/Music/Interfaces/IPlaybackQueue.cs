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
