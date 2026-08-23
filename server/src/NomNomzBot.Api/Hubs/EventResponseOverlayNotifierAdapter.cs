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
using NomNomzBot.Application.Commands.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IEventResponseOverlayNotifier"/> abstraction to the
/// <see cref="IWidgetNotifier"/> SignalR hub — bridges the Infrastructure→API dependency boundary so
/// <c>EventResponseExecutor</c>'s <c>overlay</c> ResponseType never takes a direct reference to the
/// SignalR layer. Broadcasts through the generic overlay event feed (<see cref="OverlayEventDto"/>,
/// type <c>event_response</c>) rather than a dedicated hub method, since the payload shape is entirely
/// operator-configured (message + free-form metadata) instead of a fixed contract.
/// </summary>
internal sealed class EventResponseOverlayNotifierAdapter : IEventResponseOverlayNotifier
{
    private readonly IWidgetNotifier _notifier;

    public EventResponseOverlayNotifierAdapter(IWidgetNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task NotifyAsync(
        Guid broadcasterId,
        string eventTypeKey,
        string resolvedMessage,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default
    )
    {
        string payload = JsonSerializer.Serialize(
            new
            {
                eventType = eventTypeKey,
                message = resolvedMessage,
                metadata,
            }
        );

        return _notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new OverlayEventDto("event_response", payload),
            ct
        );
    }
}
