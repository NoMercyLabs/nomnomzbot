// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// <see cref="IPlaybackQueue"/> over the existing <see cref="FairQueue{T}"/>. Fair ordering stays in
/// FairQueue; this adapter only adds head re-queue and remove-by-id. It never calls RemoveThrough,
/// which would drop every row above a match.
/// </summary>
public sealed class FairPlaybackQueue(IFairQueue<PlaybackItem> queue) : IPlaybackQueue
{
    public FairPlaybackQueue()
        : this(new FairQueue<PlaybackItem>()) { }

    public PlaybackItem? Head => queue.Peek();

    public IReadOnlyList<PlaybackItem> Snapshot => [.. queue.GetSnapshot().Select(e => e.Item)];

    /// <summary>Adds a request by fair order.</summary>
    public void Enqueue(PlaybackItem item) => queue.Enqueue(item.OwnerKey, item);

    public bool Remove(string itemId) => queue.RemoveFirst(i => i.Id == itemId);

    public void RequeueAtHead(PlaybackItem item)
    {
        queue.Enqueue(item.OwnerKey, item);
        queue.MoveToFront(queue.Count - 1);
    }
}
