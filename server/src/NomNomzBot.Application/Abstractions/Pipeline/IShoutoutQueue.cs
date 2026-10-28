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
