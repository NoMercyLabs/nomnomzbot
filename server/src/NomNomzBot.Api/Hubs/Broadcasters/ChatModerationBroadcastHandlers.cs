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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts chat cleared events to dashboard AND overlay/widget clients (mirrors ChatMessageBroadcastHandler's fan-out — a chat_box overlay must drop every rendered message the SAME instant the dashboard does).</summary>
public sealed class ChatClearedBroadcastHandler : IEventHandler<ChatClearedEvent>
{
    // camelCase so the overlay-feed payload byte-matches the frontend shape a widget parses (mirrors
    // ChatMessageBroadcastHandler.OverlayJson) — one instance per handler class, the established convention.
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDashboardNotifier _notifier;
    private readonly IWidgetNotifier _widgets;
    private readonly IApplicationDbContext _db;

    public ChatClearedBroadcastHandler(
        IDashboardNotifier notifier,
        IWidgetNotifier widgets,
        IApplicationDbContext db
    )
    {
        _notifier = notifier;
        _widgets = widgets;
        _db = db;
    }

    public async Task HandleAsync(ChatClearedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        ChatClearedDto dto = new(@event.ClearedByUserId);

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "chat_cleared",
            dto,
            ct
        );

        await _widgets.BroadcastOverlayEventAsync(
            @event.BroadcasterId.ToString(),
            new("ChatCleared", JsonSerializer.Serialize(dto, OverlayJson)),
            ct
        );
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "ChatCleared",
            dto,
            excludeWidgetId: null,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>Broadcasts message deleted events to dashboard AND overlay/widget clients.</summary>
public sealed class ChatMessageDeletedBroadcastHandler : IEventHandler<ChatMessageDeletedEvent>
{
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDashboardNotifier _notifier;
    private readonly IWidgetNotifier _widgets;
    private readonly IApplicationDbContext _db;

    public ChatMessageDeletedBroadcastHandler(
        IDashboardNotifier notifier,
        IWidgetNotifier widgets,
        IApplicationDbContext db
    )
    {
        _notifier = notifier;
        _widgets = widgets;
        _db = db;
    }

    public async Task HandleAsync(ChatMessageDeletedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        MessageDeletedDto dto = new(@event.MessageId, @event.DeletedByUserId, @event.TargetUserId);

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "message_deleted",
            dto,
            ct
        );

        await _widgets.BroadcastOverlayEventAsync(
            @event.BroadcasterId.ToString(),
            new("MessageDeleted", JsonSerializer.Serialize(dto, OverlayJson)),
            ct
        );
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "MessageDeleted",
            dto,
            excludeWidgetId: null,
            @event.EventId.ToString(),
            ct
        );
    }
}

/// <summary>
/// Broadcasts a targeted per-user message purge (EventSub <c>channel.chat.clear_user_messages</c> — a moderator
/// clearing everything from ONE chatter, distinct from a whole-channel <see cref="ChatClearedEvent"/>) to
/// dashboard AND overlay/widget clients. Previously had NO handler at all — the event was published but never
/// reached anything, so a per-user purge silently did nothing outside Twitch's own chat.
/// </summary>
public sealed class ChatUserMessagesClearedBroadcastHandler
    : IEventHandler<ChatUserMessagesClearedEvent>
{
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IDashboardNotifier _notifier;
    private readonly IWidgetNotifier _widgets;
    private readonly IApplicationDbContext _db;

    public ChatUserMessagesClearedBroadcastHandler(
        IDashboardNotifier notifier,
        IWidgetNotifier widgets,
        IApplicationDbContext db
    )
    {
        _notifier = notifier;
        _widgets = widgets;
        _db = db;
    }

    public async Task HandleAsync(
        ChatUserMessagesClearedEvent @event,
        CancellationToken ct = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        UserMessagesClearedDto dto = new(
            @event.TargetUserId,
            @event.TargetUserDisplayName,
            @event.TargetUserLogin
        );

        await _notifier.NotifyChannelAsync(
            @event.BroadcasterId.ToString(),
            "user_messages_cleared",
            dto,
            ct
        );

        await _widgets.BroadcastOverlayEventAsync(
            @event.BroadcasterId.ToString(),
            new("UserMessagesCleared", JsonSerializer.Serialize(dto, OverlayJson)),
            ct
        );
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _widgets,
            @event.BroadcasterId,
            "UserMessagesCleared",
            dto,
            excludeWidgetId: null,
            @event.EventId.ToString(),
            ct
        );
    }
}
