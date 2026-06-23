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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Listens to ChatMessageReceivedEvent and broadcasts the rich decorated
/// message to all dashboard/overlay clients subscribed to that channel group.
/// Runs the chat-decoration pipeline first so emotes (Twitch + BTTV/FFZ/7TV) carry render-ready urls.
/// </summary>
public sealed class ChatMessageBroadcastHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IChatMessageDecorator _decorator;
    private readonly TimeProvider _timeProvider;

    public ChatMessageBroadcastHandler(
        IDashboardNotifier notifier,
        IChatMessageDecorator decorator,
        TimeProvider timeProvider
    )
    {
        _notifier = notifier;
        _decorator = decorator;
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

        DecoratedChatMessage decorated = await _decorator.DecorateAsync(evt, ct);

        DashboardChatMessageDto dto = new(
            Id: evt.MessageId,
            ChannelId: evt.BroadcasterId.ToString(),
            UserId: evt.UserId,
            DisplayName: evt.UserDisplayName,
            Username: evt.UserLogin,
            Message: evt.Message,
            Fragments: decorated.Fragments.Select(MapFragment).ToList(),
            UserType: userType,
            IsSubscriber: evt.IsSubscriber,
            IsVip: evt.IsVip,
            IsModerator: evt.IsModerator,
            IsBroadcaster: evt.IsBroadcaster,
            IsCheer: evt.Bits > 0,
            IsCommand: false,
            Badges: decorated
                .Badges.Select(badge => new ChatBadgeDto(
                    badge.SetId,
                    badge.Id,
                    badge.Info,
                    badge.Urls
                ))
                .ToList(),
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
            Emote: f.Emote is not null
                ? new ChatEmoteDto(
                    Id: f.Emote.Id,
                    SetId: f.Emote.SetId,
                    Format: f.Emote.Animated ? "animated" : "static",
                    Provider: f.Emote.Provider.ToString(),
                    Urls: f.Emote.Urls,
                    Animated: f.Emote.Animated,
                    ZeroWidth: f.Emote.ZeroWidth
                )
                : null,
            Cheermote: f.CheermotePrefix is not null
                ? new ChatCheermoteDto(
                    Prefix: f.CheermotePrefix,
                    Bits: f.CheermoteBits ?? 0,
                    Tier: f.CheermoteTier ?? 1,
                    Urls: f.CheermoteImage?.Urls,
                    Animated: f.CheermoteImage?.Animated ?? false,
                    ColorHex: f.CheermoteImage?.ColorHex
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
