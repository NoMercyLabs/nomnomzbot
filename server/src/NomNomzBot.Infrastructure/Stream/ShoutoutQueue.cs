// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;

namespace NomNomzBot.Infrastructure.Stream;

/// <summary>
/// In-memory shoutout queue, one list per channel. A raid goes before every manual shoutout and after the
/// raids already waiting; a manual shoutout goes to the end. A target never waits twice (old-bot parity).
/// </summary>
public sealed class ShoutoutQueue : IShoutoutQueue
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<QueuedShoutout>> _byChannel = [];

    public bool Enqueue(QueuedShoutout item)
    {
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(item.BroadcasterId, out List<QueuedShoutout>? list))
            {
                list = [];
                _byChannel[item.BroadcasterId] = list;
            }
            if (list.Exists(q => q.Target.Id == item.Target.Id))
                return false;

            int index = item.IsRaid ? list.FindLastIndex(q => q.IsRaid) + 1 : list.Count;
            list.Insert(index, item);
            return true;
        }
    }

    public QueuedShoutout? Peek(Guid broadcasterId)
    {
        lock (_gate)
            return
                _byChannel.TryGetValue(broadcasterId, out List<QueuedShoutout>? list)
                && list.Count > 0
                ? list[0]
                : null;
    }

    public bool Remove(Guid broadcasterId, string targetTwitchUserId)
    {
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(broadcasterId, out List<QueuedShoutout>? list))
                return false;
            int removed = list.RemoveAll(q => q.Target.Id == targetTwitchUserId);
            if (list.Count == 0)
                _byChannel.Remove(broadcasterId);
            return removed > 0;
        }
    }

    public void Clear(Guid broadcasterId)
    {
        lock (_gate)
            _byChannel.Remove(broadcasterId);
    }

    public IReadOnlyList<Guid> ChannelsWithPending()
    {
        lock (_gate)
            return _byChannel.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
    }
}
