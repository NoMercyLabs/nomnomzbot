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
using NomNomzBot.Domain.Identity;
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
    private readonly IHubUserEnricher _enricher;
    private readonly TimeProvider _timeProvider;

    public ChatMessageBroadcastHandler(
        IDashboardNotifier notifier,
        IChatMessageDecorator decorator,
        IHubUserEnricher enricher,
        TimeProvider timeProvider
    )
    {
        _notifier = notifier;
        _decorator = decorator;
        _enricher = enricher;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(ChatMessageReceivedEvent evt, CancellationToken ct = default)
    {
        // One resolution for the chatter's role (Lead Moderator included) — the same mapping the command gate uses.
        string userType = ChatRole.ToToken(
            ChatRole.Resolve(
                evt.IsBroadcaster,
                evt.IsModerator,
                evt.IsVip,
                evt.IsSubscriber,
                evt.Badges
            )
        );

        DecoratedChatMessage decorated = await _decorator.DecorateAsync(evt, ct);
        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            evt.BroadcasterId,
            evt.UserId,
            ct
        );

        DashboardChatMessageDto dto = new(
            Id: evt.MessageId,
            ChannelId: evt.BroadcasterId.ToString(),
            UserId: evt.UserId,
            DisplayName: evt.UserDisplayName,
            Username: evt.UserLogin,
            Message: evt.Message,
            Fragments: decorated.Fragments.Select(ChatFragmentMapper.MapFragment).ToList(),
            UserType: userType,
            IsSubscriber: evt.IsSubscriber,
            IsVip: evt.IsVip,
            IsModerator: evt.IsModerator,
            IsBroadcaster: evt.IsBroadcaster,
            IsCheer: evt.Bits > 0,
            IsCommand: false,
            Badges: decorated.Badges.Select(ChatFragmentMapper.MapBadge).ToList(),
            BitsAmount: evt.Bits,
            Color: evt.ColorHex,
            MessageType: evt.MessageType,
            ReplyToMessageId: evt.ReplyParentMessageId,
            ReplyParentMessageBody: evt.ReplyParentMessageBody,
            ReplyParentUserName: evt.ReplyParentUserName,
            Timestamp: _timeProvider.GetUtcNow().ToString("O"),
            AvatarUrl: enrichment?.AvatarUrl,
            Pronouns: enrichment?.Pronouns
        );

        await _notifier.SendChatMessageAsync(evt.BroadcasterId.ToString(), dto, ct);
    }
}
