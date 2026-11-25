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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.ValueObjects;
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
    TimeProvider timeProvider,
    IChatColorMemory colors,
    ITwitchHelixClient helix,
    IChatBadgeResolver badgeResolver
) : IChatOverlayLineSender
{
    private const string ChatEventType = "ChatMessage";
    private const string OverlayLineMessageType = "overlay_line";

    // The bot speaks in a channel as one of its moderators, so its line wears the moderator badge a chat client
    // shows next to every mod.
    private static readonly ChatBadge[] BotBadges = [new("moderator", "1")];

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

        string? color = await ResolveColorAsync(line, cancellationToken);
        IReadOnlyList<ChatBadgeDto> badges = await ResolveBadgesAsync(line, cancellationToken);
        int reached = await WidgetAlertDispatch.PushAsync(
            widgets,
            line.BroadcasterId,
            open,
            ChatEventType,
            ToChatMessage(line, color, badges),
            cancellationToken
        );
        return new ChatOverlayDelivery(reached);
    }

    // The color the bot's own chat lines carry: what chat last showed for it, else what Twitch says it set.
    private async Task<string?> ResolveColorAsync(ChatOverlayLine line, CancellationToken ct)
    {
        RememberedChatColor? remembered = await colors.RecallAsync(
            line.BroadcasterId,
            line.BotUserId,
            ct
        );
        if (remembered is not null)
            return remembered.Hex;

        Result<IReadOnlyList<TwitchUserChatColor>> fetched =
            await helix.Chat.GetUserChatColorsAsync([line.BotUserId], ct);
        if (fetched.IsFailure)
            return null;

        string? hex = fetched.Value.FirstOrDefault(c => c.UserId == line.BotUserId)?.Color;
        await colors.RememberFetchedAsync(line.BroadcasterId, line.BotUserId, hex, ct);
        return string.IsNullOrEmpty(hex) ? null : hex;
    }

    private async Task<IReadOnlyList<ChatBadgeDto>> ResolveBadgesAsync(
        ChatOverlayLine line,
        CancellationToken ct
    )
    {
        IReadOnlyList<ResolvedChatBadge> resolved = await badgeResolver.ResolveAsync(
            line.BroadcasterId,
            BotBadges,
            ct
        );
        return [.. resolved.Select(ChatFragmentMapper.MapBadge)];
    }

    private DashboardChatMessageDto ToChatMessage(
        ChatOverlayLine line,
        string? color,
        IReadOnlyList<ChatBadgeDto> badges
    ) =>
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
            Badges: badges,
            BitsAmount: 0,
            Color: color,
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
