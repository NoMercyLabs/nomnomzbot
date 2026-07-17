// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
            new SharedChatSessionInfo(
                @event.SessionId,
                @event.HostBroadcasterId,
                @event.Participants
            )
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
            new SharedChatSessionInfo(
                @event.SessionId,
                @event.HostBroadcasterId,
                @event.Participants
            )
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
