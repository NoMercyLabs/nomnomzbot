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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <inheritdoc cref="IChatOverlayLineSender"/>
/// <remarks>
/// The line travels as a <c>ChatMessage</c> widget event whose <c>MessageType</c> is <c>overlay_line</c>, to the
/// chat widgets that are enabled, subscribed to <c>ChatMessage</c> and attached to the hub right now. It never
/// touches <see cref="IDashboardNotifier"/> (the dashboard chat feed shows only what the platform chat carried) and
/// it skips the generic overlay feed, which would also reach widgets that are not chat widgets.
/// </remarks>
public sealed class ChatOverlayLineSender(
    IApplicationDbContext db,
    IWidgetNotifier widgets,
    IOverlayPresenceRegistry presence,
    TimeProvider timeProvider
) : IChatOverlayLineSender
{
    private const string ChatEventType = "ChatMessage";
    private const string OverlayLineMessageType = "overlay_line";

    public async Task<ChatOverlayDelivery> SendAsync(
        ChatOverlayLine line,
        CancellationToken cancellationToken = default
    )
    {
        List<Widget> channelWidgets = await db
            .Widgets.Where(w => w.BroadcasterId == line.BroadcasterId)
            .ToListAsync(cancellationToken);

        List<Widget> open =
        [
            .. WidgetAlertRouting
                .Subscribers(channelWidgets, ChatEventType)
                .Where(w => presence.IsWidgetAttached(line.BroadcasterId, w.Id)),
        ];
        if (open.Count == 0)
            return ChatOverlayDelivery.NoChatOverlayOpen;

        int reached = await WidgetAlertDispatch.PushAsync(
            widgets,
            line.BroadcasterId,
            open,
            ChatEventType,
            ToChatMessage(line),
            cancellationToken
        );
        return new ChatOverlayDelivery(reached);
    }

    private DashboardChatMessageDto ToChatMessage(ChatOverlayLine line) =>
        new(
            Id: Guid.CreateVersion7().ToString(),
            ChannelId: line.BroadcasterId.ToString(),
            UserId: line.BotUserId,
            DisplayName: line.BotDisplayName,
            Username: line.BotDisplayName,
            Message: line.Text,
            Fragments:
            [
                new ChatFragmentDto(
                    line.IsHtml ? "html" : "text",
                    line.Text,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            UserType: "bot",
            IsSubscriber: false,
            IsVip: false,
            IsModerator: false,
            IsBroadcaster: false,
            IsCheer: false,
            IsCommand: false,
            Badges: [],
            BitsAmount: 0,
            Color: null,
            MessageType: OverlayLineMessageType,
            ReplyToMessageId: null,
            ReplyParentMessageBody: null,
            ReplyParentUserName: null,
            Timestamp: timeProvider.GetUtcNow().ToString("O"),
            OverlayLine: ToOverlayLine(line)
        );

    private static OverlayLineDto ToOverlayLine(ChatOverlayLine line)
    {
        ReplyLook look = line.Look ?? ReplyLook.Default;
        return new OverlayLineDto(
            line.RequestedBy,
            look.HighlightToken,
            look.SizeToken,
            look.DurationSeconds
        );
    }
}
