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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Listens to ChatMessageReceivedEvent and broadcasts the rich decorated
/// message to all dashboard/overlay clients subscribed to that channel group.
/// Runs the chat-decoration pipeline first so emotes (Twitch + BTTV/FFZ/7TV) carry render-ready urls.
/// </summary>
public sealed class ChatMessageBroadcastHandler : IEventHandler<ChatMessageReceivedEvent>
{
    // The decorated chat payload the OVERLAY feed carries is serialized here (a JSON string inside
    // OverlayEventDto), camelCase so it byte-matches the frontend ChatMessagePayload shape the dashboard
    // receives over SignalR — a chat widget then parses exactly the same render-ready fields.
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDashboardNotifier _notifier;
    private readonly IChatMessageDecorator _decorator;
    private readonly IHubUserEnricher _enricher;
    private readonly IWidgetNotifier _widgets;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ChatMessageBroadcastHandler(
        IDashboardNotifier notifier,
        IChatMessageDecorator decorator,
        IHubUserEnricher enricher,
        IWidgetNotifier widgets,
        IApplicationDbContext db,
        TimeProvider timeProvider
    )
    {
        _notifier = notifier;
        _decorator = decorator;
        _enricher = enricher;
        _widgets = widgets;
        _db = db;
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

        // Decoration (Twitch/BTTV/FFZ/7TV emote + badge lookups) and enrichment (avatar/pronouns) are
        // keyed by Twitch ids — for other platforms the raw fragments already ARE the render shape, and
        // running the adapters would fire lookups against foreign ids for nothing.
        bool isTwitch = evt.Provider == AuthEnums.Platform.Twitch;

        IReadOnlyList<ChatFragmentDto> fragments;
        IReadOnlyList<ChatBadgeDto> badges;
        HubUserEnrichment? enrichment = null;
        if (isTwitch)
        {
            DecoratedChatMessage decorated = await _decorator.DecorateAsync(evt, ct);
            enrichment = await _enricher.EnrichAsync(evt.BroadcasterId, evt.UserId, ct);
            fragments = decorated.Fragments.Select(ChatFragmentMapper.MapFragment).ToList();
            badges = decorated.Badges.Select(ChatFragmentMapper.MapBadge).ToList();
        }
        else
        {
            // Badge images resolve from the cached Helix badge sets — no non-Twitch source exists, and the
            // role flags below already carry the chatter's standing (owner/mod/member) to the UI.
            fragments = evt.Fragments.Select(ChatFragmentMapper.MapFragment).ToList();
            badges = [];
        }

        DashboardChatMessageDto dto = new(
            Id: evt.MessageId,
            ChannelId: evt.BroadcasterId.ToString(),
            UserId: evt.UserId,
            DisplayName: evt.UserDisplayName,
            Username: evt.UserLogin,
            Message: evt.Message,
            Fragments: fragments,
            UserType: userType,
            IsSubscriber: evt.IsSubscriber,
            IsVip: evt.IsVip,
            IsModerator: evt.IsModerator,
            IsBroadcaster: evt.IsBroadcaster,
            IsCheer: evt.Bits > 0,
            IsCommand: false,
            Badges: badges,
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

        // Overlays (OBS browser sources) subscribe to the generic overlay feed, which otherwise carries the
        // RAW journaled ChatMessageReceivedEvent — a receive-side event with none of the render data a chat
        // widget needs (resolved emote/badge image urls, structured fragments, colour, avatar, pronouns). Push
        // the SAME decorated dto the dashboard gets, as a clean "ChatMessage" overlay event, so a widget can
        // build a fully-styled bubble. OverlayEventFilter drops the raw duplicate so the widget sees only this.
        await _widgets.BroadcastOverlayEventAsync(
            evt.BroadcasterId.ToString(),
            new OverlayEventDto("ChatMessage", JsonSerializer.Serialize(dto, OverlayJson)),
            ct
        );

        // Authored widgets (chat_box / emote_wall) live in a sandboxed iframe, and the overlay host page
        // forwards ONLY WidgetEvent frames into it — the generic feed push above never reaches them. Route
        // the SAME decorated dto through the shared subscription-matched dispatch, so chat volume only hits
        // widgets whose EventSubscriptions declare "ChatMessage"; everything else stays quiet.
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _widgets,
            evt.BroadcasterId,
            "ChatMessage",
            dto,
            ct
        );
    }
}
