// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts chat cleared events to dashboard clients.</summary>
public sealed class ChatClearedBroadcastHandler : IEventHandler<ChatClearedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChatClearedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChatClearedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "chat_cleared",
            new ChatClearedDto(@event.ClearedByUserId),
            ct
        );
    }
}

/// <summary>Broadcasts message deleted events to dashboard/overlay clients.</summary>
public sealed class ChatMessageDeletedBroadcastHandler : IEventHandler<ChatMessageDeletedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public ChatMessageDeletedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(ChatMessageDeletedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return Task.CompletedTask;

        return _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "message_deleted",
            new MessageDeletedDto(@event.MessageId, @event.DeletedByUserId, @event.TargetUserId),
            ct
        );
    }
}
