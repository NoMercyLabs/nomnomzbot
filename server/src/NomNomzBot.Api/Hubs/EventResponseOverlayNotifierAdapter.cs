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

using System.Text.Json;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IEventResponseOverlayNotifier"/> abstraction to the
/// <see cref="IWidgetNotifier"/> SignalR hub — bridges the Infrastructure→API dependency boundary so
/// <c>EventResponseExecutor</c>'s <c>overlay</c> ResponseType never takes a direct reference to the
/// SignalR layer. Broadcasts through the generic overlay event feed (<see cref="OverlayEventDto"/>,
/// type <c>event_response</c>) rather than a dedicated hub method, since the payload shape is entirely
/// operator-configured (message + free-form metadata) instead of a fixed contract. Widgets that subscribe to
/// <c>event_response</c> get the same payload as a <c>WidgetEvent</c> object.
/// </summary>
internal sealed class EventResponseOverlayNotifierAdapter : IEventResponseOverlayNotifier
{
    private const string EventType = "event_response";

    private readonly IWidgetNotifier _notifier;
    private readonly IApplicationDbContext _db;

    public EventResponseOverlayNotifierAdapter(IWidgetNotifier notifier, IApplicationDbContext db)
    {
        _notifier = notifier;
        _db = db;
    }

    public async Task NotifyAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string resolvedMessage,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default
    )
    {
        JsonElement payload = JsonSerializer.SerializeToElement(
            new
            {
                eventType = eventTypeKey,
                message = resolvedMessage,
                metadata,
            }
        );

        await _notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new(EventType, payload.GetRawText()),
            ct
        );
        await WidgetAlertDispatch.PushToSubscribersAsync(
            _db,
            _notifier,
            broadcasterId,
            EventType,
            payload,
            ct
        );
    }
}
