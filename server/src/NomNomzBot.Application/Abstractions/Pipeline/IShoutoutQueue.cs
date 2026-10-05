// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// The per-channel in-memory queue of shoutouts waiting for the global shoutout cooldown (old-bot parity).
/// A raid goes before a manual one, first in first out inside a tier, and one target waits only once.
/// It is not saved across a restart; stream online and offline clear it.
/// </summary>
public interface IShoutoutQueue
{
    /// <summary>Adds the item. False when the same target already waits in this channel's queue.</summary>
    bool Enqueue(QueuedShoutout item);

    /// <summary>The item that runs next, or null when the channel's queue is empty.</summary>
    QueuedShoutout? Peek(Guid broadcasterId);

    /// <summary>Takes the target's item out. False when it is no longer there (cleared meanwhile).</summary>
    bool Remove(Guid broadcasterId, string targetTwitchUserId);

    void Clear(Guid broadcasterId);

    IReadOnlyList<Guid> ChannelsWithPending();
}
