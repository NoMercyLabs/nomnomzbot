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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Generic fair queue implementation that ensures equitable scheduling across
/// multiple owners (e.g., per-user song request fairness).
///
/// Algorithm: Each item is assigned a rank = how many items this owner
/// already has ahead in the queue + 1. The queue is sorted by rank first,
/// then insertion order within the same rank (FIFO).
///
/// Result: If N users each enqueue 1 item, all N play before anyone's 2nd item.
/// </summary>
public sealed class FairQueue<T> : IFairQueue<T>
{
    private readonly List<QueueEntry> _queue = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
                return _queue.Count;
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
                return _queue.Count == 0;
        }
    }

    public void Enqueue(string ownerKey, T item)
    {
        lock (_lock)
        {
            // Rank = number of items this owner already has in the queue + 1
            int ownerCount = _queue.Count(e => e.OwnerKey == ownerKey);
            int rank = ownerCount + 1;

            QueueEntry entry = new(ownerKey, item, rank);

            // Insert after all entries with rank <= this rank (stable FIFO within same rank)
            int insertAt = _queue.Count;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].Rank <= rank)
                {
                    insertAt = i + 1;
                    break;
                }

                insertAt = i;
            }

            _queue.Insert(insertAt, entry);
        }
    }

    public T? Dequeue()
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
                return default;

            QueueEntry entry = _queue[0];
            _queue.RemoveAt(0);

            // Recalculate ranks for remaining items by this owner
            int newRank = 1;
            foreach (QueueEntry e in _queue.Where(e => e.OwnerKey == entry.OwnerKey))
                e.Rank = newRank++;

            return entry.Item;
        }
    }

    public T? Peek()
    {
        lock (_lock)
        {
            return _queue.Count == 0 ? default : _queue[0].Item;
        }
    }

    public void Clear()
    {
        lock (_lock)
            _queue.Clear();
    }

    public int RemoveByOwner(string ownerKey)
    {
        lock (_lock)
        {
            int removed = _queue.RemoveAll(e => e.OwnerKey == ownerKey);

            // Recalculate ranks for all remaining owners after removal
            List<string> owners = _queue.Select(e => e.OwnerKey).Distinct().ToList();
            foreach (string owner in owners)
            {
                int rank = 1;
                foreach (QueueEntry e in _queue.Where(e => e.OwnerKey == owner))
                    e.Rank = rank++;
            }

            return removed;
        }
    }

    public bool RemoveAt(int position)
    {
        lock (_lock)
        {
            if (position < 0 || position >= _queue.Count)
                return false;

            QueueEntry removed = _queue[position];
            _queue.RemoveAt(position);

            // Recalculate ranks for the removed owner's remaining items
            int rank = 1;
            foreach (QueueEntry e in _queue.Where(e => e.OwnerKey == removed.OwnerKey))
                e.Rank = rank++;

            return true;
        }
    }

    /// <summary>
    /// Returns a snapshot of the queue as (item, rank, ownerKey) tuples.
    /// </summary>
    public IReadOnlyList<(T Item, int Rank, string OwnerKey)> GetSnapshot()
    {
        lock (_lock)
        {
            return _queue.Select(e => (e.Item, e.Rank, e.OwnerKey)).ToList();
        }
    }

    private sealed class QueueEntry(string ownerKey, T item, int rank)
    {
        public string OwnerKey { get; } = ownerKey;
        public T Item { get; } = item;
        public int Rank { get; set; } = rank;
    }
}
