// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>Maps an off-bot chat EventSub event to the <see cref="ExternalChatEvent"/> a viewer receives.</summary>
internal static class ExternalChatEventMapper
{
    public static ExternalChatEvent? Map(
        string subscriptionType,
        string twitchBroadcasterUserId,
        DateTimeOffset timestamp,
        JsonElement @event
    ) =>
        subscriptionType switch
        {
            "channel.chat.message" => Message(twitchBroadcasterUserId, timestamp, @event),
            "channel.chat.message_delete" => Empty(
                ExternalChatEventKind.MessageDeleted,
                twitchBroadcasterUserId,
                timestamp,
                @event.GetString("message_id")
            ),
            "channel.chat.clear" => Empty(
                ExternalChatEventKind.ChatCleared,
                twitchBroadcasterUserId,
                timestamp,
                null
            ),
            _ => null,
        };

    private static ExternalChatEvent Message(
        string broadcaster,
        DateTimeOffset timestamp,
        JsonElement @event
    )
    {
        JsonElement? message = @event.GetObject("message");
        return new(
            ExternalChatEventKind.Message,
            broadcaster,
            @event.GetString("message_id"),
            @event.GetString("chatter_user_id"),
            @event.GetString("chatter_user_login"),
            @event.GetString("chatter_user_name"),
            @event.GetString("color"),
            ChatPayload.ReadMessageText(message),
            ChatPayload.ReadFragments(message),
            [.. ChatPayload.ReadBadges(@event).Select(Unresolved)],
            @event.GetObject("reply")?.GetString("parent_message_id"),
            timestamp
        );
    }

    // The reader fills the image urls from the channel's badge sets before the line is pushed.
    private static ResolvedChatBadge Unresolved(ChatBadge badge) =>
        new(badge.SetId, badge.Id, badge.Info, new Dictionary<string, string>());

    private static ExternalChatEvent Empty(
        ExternalChatEventKind kind,
        string broadcaster,
        DateTimeOffset timestamp,
        string? messageId
    ) =>
        new(
            kind,
            broadcaster,
            messageId,
            null,
            null,
            null,
            null,
            string.Empty,
            [],
            [],
            null,
            timestamp
        );
}
