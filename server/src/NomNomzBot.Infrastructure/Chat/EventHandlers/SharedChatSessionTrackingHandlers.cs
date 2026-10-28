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

using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Feeds <see cref="ISharedChatSessionTracker"/> from the shared-chat EventSub facts, so the shared-ban
/// trust web (moderation.md §3.5) has an ACTIVE session to verify inbound bans against. Begin/update set
/// the channel's session; end clears it (only if the ended session is still the current one).
/// </summary>
public sealed class SharedChatSessionBeganHandler(ISharedChatSessionTracker tracker)
    : IEventHandler<SharedChatBeganEvent>
{
    public Task HandleAsync(SharedChatBeganEvent @event, CancellationToken ct = default)
    {
        tracker.SetSession(
            @event.BroadcasterId,
            new(@event.SessionId, @event.HostBroadcasterId, @event.Participants)
        );
        return Task.CompletedTask;
    }
}

/// <summary>The update half — participant changes replace the tracked session in place.</summary>
public sealed class SharedChatSessionUpdatedHandler(ISharedChatSessionTracker tracker)
    : IEventHandler<SharedChatUpdatedEvent>
{
    public Task HandleAsync(SharedChatUpdatedEvent @event, CancellationToken ct = default)
    {
        tracker.SetSession(
            @event.BroadcasterId,
            new(@event.SessionId, @event.HostBroadcasterId, @event.Participants)
        );
        return Task.CompletedTask;
    }
}

/// <summary>The end half — clears the channel's tracked session.</summary>
public sealed class SharedChatSessionEndedHandler(ISharedChatSessionTracker tracker)
    : IEventHandler<SharedChatEndedEvent>
{
    public Task HandleAsync(SharedChatEndedEvent @event, CancellationToken ct = default)
    {
        tracker.ClearSession(@event.BroadcasterId, @event.SessionId);
        return Task.CompletedTask;
    }
}
