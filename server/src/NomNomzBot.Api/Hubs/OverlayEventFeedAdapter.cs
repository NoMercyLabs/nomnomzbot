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
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IOverlayEventFeed"/> abstraction to the <see cref="IWidgetNotifier"/>
/// SignalR hub — bridges the Infrastructure→API boundary so the post-commit fan-out hook never takes a direct
/// reference to the hub. Every journaled event arrives here: it goes to the generic feed (pages that host no
/// widget) as JSON text, and to each widget that subscribes to its type as a <c>WidgetEvent</c> object.
/// </summary>
internal sealed class OverlayEventFeedAdapter : IOverlayEventFeed
{
    private readonly IWidgetNotifier _notifier;
    private readonly IApplicationDbContext _db;

    public OverlayEventFeedAdapter(IWidgetNotifier notifier, IApplicationDbContext db)
    {
        _notifier = notifier;
        _db = db;
    }

    public async Task BroadcastEventAsync(
        Guid broadcasterId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default
    )
    {
        await _notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new(eventType, payloadJson),
            ct
        );
        await WidgetAlertDispatch.PushToSubscribersAsync(
            _db,
            _notifier,
            broadcasterId,
            eventType,
            JsonSerializer.Deserialize<JsonElement>(payloadJson),
            ct
        );
    }
}
