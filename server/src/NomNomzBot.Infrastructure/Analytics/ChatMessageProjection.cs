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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Folds every journaled chat message, live or imported from the old bot, into the <c>ChatMessages</c> table that
/// the trust ladder, the viewer pages and the chat counts read. The row key is the platform message id, so the
/// imported copy and the live copy of one message are one row. <c>CreatedAt</c> is the time the message was sent.
/// </summary>
public sealed class ChatMessageProjection(IApplicationDbContext db) : IProjection
{
    private static readonly HashSet<string> Subscribed = new(StringComparer.Ordinal)
    {
        "ChatMessageReceivedEvent",
    };

    public string Name => "chat-message";
    public bool IsGlobal => false;
    public IReadOnlySet<string> SubscribedEventTypes => Subscribed;

    public async Task<Result> ApplyAsync(
        EventRecord @event,
        CancellationToken cancellationToken = default
    )
    {
        ChatMessageReceivedEvent? chat = Parse(@event.PayloadJson);
        if (
            chat is null
            || @event.BroadcasterId is not { } broadcasterId
            || string.IsNullOrEmpty(chat.MessageId)
        )
            return Result.Success();

        // IgnoreQueryFilters: the projection driver runs without a tenant, and the tenant filter would hide the
        // row a previous batch committed, so the same message id would insert twice.
        IQueryable<ChatMessage> sameId = db
            .ChatMessages.IgnoreQueryFilters()
            .Where(m => m.Id == chat.MessageId);
        if (await sameId.AnyAsync(cancellationToken))
        {
            // The live handler may have stamped the replay time. The journal event holds the real send time, so
            // only CreatedAt is set; DeletedAt and every other field stay. ExecuteUpdateAsync because the audit
            // interceptor never lets a tracked update change CreatedAt.
            DateTime sentAt = @event.OccurredAt;
            await sameId.ExecuteUpdateAsync(
                s => s.SetProperty(m => m.CreatedAt, sentAt),
                cancellationToken
            );
            return Result.Success();
        }

        db.ChatMessages.Add(
            new()
            {
                Id = chat.MessageId,
                BroadcasterId = broadcasterId,
                Provider = chat.Provider,
                UserId = chat.UserId,
                Username = chat.UserLogin,
                DisplayName = chat.UserDisplayName,
                UserType = ResolveUserType(chat),
                ColorHex = chat.ColorHex,
                Message = chat.Message,
                Fragments = [.. chat.Fragments],
                Badges = [.. chat.Badges],
                MessageType = chat.MessageType,
                IsCommand = !string.IsNullOrEmpty(chat.Message) && chat.Message.StartsWith('!'),
                IsCheer = chat.Bits > 0,
                BitsAmount = chat.Bits > 0 ? chat.Bits : null,
                IsHighlighted = string.Equals(
                    chat.MessageType,
                    "channel_points_highlighted",
                    StringComparison.OrdinalIgnoreCase
                ),
                ReplyToMessageId = chat.ReplyParentMessageId,
                CreatedAt = @event.OccurredAt,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // Deletes nothing: the table also has a live writer (ChatMessagePersistenceHandler), and the refold is an idempotent upsert.
    public Task<Result> ResetAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success());

    private static ChatMessageReceivedEvent? Parse(string payloadJson)
    {
        try
        {
            return JsonConvert.DeserializeObject<ChatMessageReceivedEvent>(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ResolveUserType(ChatMessageReceivedEvent chat)
    {
        if (chat.IsBroadcaster)
            return "broadcaster";
        if (chat.IsModerator)
            return "moderator";
        if (chat.IsVip)
            return "vip";
        if (chat.IsSubscriber)
            return "subscriber";
        return "viewer";
    }
}
