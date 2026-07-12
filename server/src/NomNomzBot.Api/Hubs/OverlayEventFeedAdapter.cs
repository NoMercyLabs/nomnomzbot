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
using NomNomzBot.Application.Overlays.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IOverlayEventFeed"/> abstraction to the <see cref="IWidgetNotifier"/>
/// SignalR hub — bridges the Infrastructure→API boundary so the post-commit fan-out hook never takes a direct
/// reference to the hub. Every journaled event arrives here and is pushed to the channel's overlay group.
/// </summary>
internal sealed class OverlayEventFeedAdapter : IOverlayEventFeed
{
    private readonly IWidgetNotifier _notifier;

    public OverlayEventFeedAdapter(IWidgetNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task BroadcastEventAsync(
        Guid broadcasterId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default
    ) =>
        _notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new OverlayEventDto(eventType, payloadJson),
            ct
        );
}
