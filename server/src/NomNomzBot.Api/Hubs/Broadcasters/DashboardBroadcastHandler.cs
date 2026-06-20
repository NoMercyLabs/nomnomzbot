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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Listens to ChatMessageReceivedEvent and broadcasts the rich decorated
/// message to all dashboard/overlay clients subscribed to that channel group.
/// </summary>
public sealed class ChatMessageBroadcastHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly TimeProvider _timeProvider;

    public ChatMessageBroadcastHandler(IDashboardNotifier notifier, TimeProvider timeProvider)
    {
        _notifier = notifier;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(ChatMessageReceivedEvent evt, CancellationToken ct = default)
    {
        string userType =
            evt.IsBroadcaster ? "broadcaster"
            : evt.IsModerator ? "moderator"
            : evt.IsVip ? "vip"
            : evt.IsSubscriber ? "subscriber"
            : "viewer";

        DashboardChatMessageDto dto = new(
            Id: evt.MessageId,
            ChannelId: evt.BroadcasterId.ToString(),
            UserId: evt.UserId,
            DisplayName: evt.UserDisplayName,
            Username: evt.UserLogin,
            Message: evt.Message,
            Fragments: evt.Fragments.Select(MapFragment).ToList(),
            UserType: userType,
            IsSubscriber: evt.IsSubscriber,
            IsVip: evt.IsVip,
            IsModerator: evt.IsModerator,
            IsBroadcaster: evt.IsBroadcaster,
            IsCheer: evt.Bits > 0,
            IsCommand: false,
            Badges: evt.Badges.Select(b => new ChatBadgeDto(b.SetId, b.Id, b.Info)).ToList(),
            BitsAmount: evt.Bits,
            Color: evt.ColorHex,
            MessageType: evt.MessageType,
            ReplyToMessageId: evt.ReplyParentMessageId,
            ReplyParentMessageBody: evt.ReplyParentMessageBody,
            ReplyParentUserName: evt.ReplyParentUserName,
            Timestamp: _timeProvider.GetUtcNow().ToString("O")
        );

        await _notifier.SendChatMessageAsync(evt.BroadcasterId.ToString(), dto, ct);
    }

    private static ChatFragmentDto MapFragment(ChatMessageFragment f) =>
        new(
            Type: f.Type,
            Text: f.Text,
            Emote: f.EmoteId is not null
                ? new ChatEmoteDto(
                    Id: f.EmoteId,
                    SetId: f.EmoteSetId,
                    Format: f.EmoteFormats.Contains("animated") ? "animated" : "static"
                )
                : null,
            Cheermote: f.CheermotePrefix is not null
                ? new ChatCheermoteDto(
                    Prefix: f.CheermotePrefix,
                    Bits: f.CheermoteBits ?? 0,
                    Tier: f.CheermoteTier ?? 1
                )
                : null,
            Mention: f.MentionUserId is not null
                ? new ChatMentionDto(
                    UserId: f.MentionUserId,
                    Username: f.MentionUserLogin ?? string.Empty,
                    DisplayName: f.MentionUserName ?? string.Empty
                )
                : null
        );
}
