// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Persists every incoming chat message to the <c>ChatMessages</c> table (upsert by <c>MessageId</c>) so that
/// community stats, user message counts, and the chat feed history are all backed by real data from the live stream.
/// Idempotent: a duplicate EventSub delivery for the same message id overwrites (no duplicate rows). This runs on the
/// hot path alongside <see cref="ChatColorRecorder"/> and <see cref="ChatMessageHandler"/> — it is intentionally
/// lightweight: one read (duplicate check) + one insert, scoped to the caller's request lifetime.
/// </summary>
public sealed class ChatMessagePersistenceHandler(IApplicationDbContext db)
    : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.MessageId))
            return;

        bool exists = await db.ChatMessages.AnyAsync(
            m => m.Id == @event.MessageId,
            cancellationToken
        );

        if (exists)
            return;

        ChatMessage msg = new()
        {
            Id = @event.MessageId,
            BroadcasterId = @event.BroadcasterId,
            UserId = @event.UserId,
            Username = @event.UserLogin,
            DisplayName = @event.UserDisplayName,
            UserType = ResolveUserType(@event),
            ColorHex = @event.ColorHex,
            Message = @event.Message,
            Fragments = @event.Fragments.ToList(),
            Badges = @event.Badges.ToList(),
            MessageType = @event.MessageType,
            IsCommand = !string.IsNullOrEmpty(@event.Message) && @event.Message.StartsWith('!'),
            IsCheer = @event.Bits > 0,
            BitsAmount = @event.Bits > 0 ? @event.Bits : null,
            IsHighlighted = string.Equals(
                @event.MessageType,
                "channel_points_highlighted",
                StringComparison.OrdinalIgnoreCase
            ),
            ReplyToMessageId = @event.ReplyParentMessageId,
        };

        db.ChatMessages.Add(msg);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string ResolveUserType(ChatMessageReceivedEvent @event)
    {
        if (@event.IsBroadcaster)
            return "broadcaster";
        if (@event.IsModerator)
            return "moderator";
        if (@event.IsVip)
            return "vip";
        if (@event.IsSubscriber)
            return "subscriber";
        return "viewer";
    }
}
