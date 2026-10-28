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

using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The shared EventSub inbox (twitch-eventsub §10.1). Registered only where two instances can share one
/// database (Postgres): there, every shard receiver stores its notifications here and only the chat-ingest
/// lease holder drains them, so one instance processes at a time. On SQLite only one instance can exist, it
/// is not registered, and notifications are processed in-process as they arrive.
/// </summary>
public interface IEventSubInbox
{
    /// <summary>Stores a received notification. False when its message id is already waiting (a resend).</summary>
    Task<bool> EnqueueAsync(EventSubInboxMessage message, CancellationToken ct = default);

    /// <summary>The oldest waiting notifications, in arrival order.</summary>
    Task<IReadOnlyList<EventSubInboxMessage>> PeekAsync(int max, CancellationToken ct = default);

    /// <summary>Removes a notification once it has been dispatched.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
