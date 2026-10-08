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
/// The per-channel queue of shoutouts waiting for Twitch cooldowns.
/// A raid goes before a manual one, first in first out inside a tier, and one target waits only once.
/// Pending items are saved across a restart; stream online and offline clear them.
/// </summary>
public interface IShoutoutQueue
{
    Task<bool> EnqueueAsync(QueuedShoutout item, CancellationToken cancellationToken);
    Task RecoverAsync(CancellationToken cancellationToken);
    Task<bool> RemoveAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken cancellationToken
    );
    Task ClearAsync(Guid broadcasterId, CancellationToken cancellationToken);

    /// <summary>Adds the item. False when the same target already waits in this channel's queue.</summary>
    bool Enqueue(QueuedShoutout item);

    /// <summary>The item that runs next, or null when the channel's queue is empty.</summary>
    QueuedShoutout? Peek(Guid broadcasterId);

    /// <summary>Takes the target's item out. False when it is no longer there (cleared meanwhile).</summary>
    bool Remove(Guid broadcasterId, string targetTwitchUserId);

    void Clear(Guid broadcasterId);

    IReadOnlyList<Guid> ChannelsWithPending();
}
