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

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// <see cref="IPlaybackQueue"/> over the existing <see cref="FairQueue{T}"/>. Fair ordering stays in
/// FairQueue; this adapter only adds head re-queue and remove-by-id. It removes one row at a time.
/// </summary>
public sealed class FairPlaybackQueue(IFairQueue<PlaybackItem> queue) : IPlaybackQueue
{
    public FairPlaybackQueue()
        : this(new FairQueue<PlaybackItem>()) { }

    public PlaybackItem? Head => queue.Peek();

    public IReadOnlyList<PlaybackItem> Snapshot => [.. queue.GetSnapshot().Select(e => e.Item)];

    /// <summary>Adds a request by fair order.</summary>
    public void Enqueue(PlaybackItem item) => queue.Enqueue(item.OwnerKey, item);

    public bool Remove(string itemId) =>
        queue.Peek()?.Id == itemId
            ? queue.Dequeue() is not null
            : queue.RemoveFirst(i => i.Id == itemId);

    public void RequeueAtHead(PlaybackItem item)
    {
        queue.Enqueue(item.OwnerKey, item);
        queue.MoveToFront(queue.Count - 1);
    }
}
