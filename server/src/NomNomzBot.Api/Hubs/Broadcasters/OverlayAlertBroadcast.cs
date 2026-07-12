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

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans one fully-decorated alert DTO to BOTH overlay surfaces at once, so an OBS overlay never sees a thin or raw
/// payload for a user-facing event: (a) the generic overlay feed — the same feed the raw journaled event would have
/// ridden — now carrying the decorated dto as a camelCase JSON string keyed by the event type; and
/// (b) every widget subscribed to that event type, via the shared <see cref="WidgetAlertDispatch"/> routing. A
/// dashboard alert handler calls this right after its existing dashboard push, reusing the SAME decorated dto — so
/// widgets, the generic feed, and the dashboard all render from one shape (avatar/pronouns/community standing plus
/// the event's resolved fields). <c>OverlayEventFilter.DecoratedElsewhere</c> drops the raw journaled duplicate so
/// the generic feed carries only this decorated form.
/// </summary>
internal static class OverlayAlertBroadcast
{
    // camelCase so the overlay-feed payload byte-matches the frontend alert shape the dashboard receives over
    // SignalR (mirrors ChatMessageBroadcastHandler.OverlayJson) — one shared options instance, reused for every alert.
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ToOverlaysAsync(
        IApplicationDbContext db,
        IWidgetNotifier notifier,
        Guid broadcasterId,
        string eventType,
        object decoratedData,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        // (a) Generic overlay feed — one decorated event, replacing the raw journaled form the filter now drops.
        await notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new OverlayEventDto(eventType, JsonSerializer.Serialize(decoratedData, OverlayJson)),
            cancellationToken
        );

        // (b) Each widget subscribed to this event type — the SAME decorated dto, routed by the shared dispatcher
        //     (WidgetAlertRouting.Subscribers → SendWidgetEventAsync), not a second minimal payload.
        await WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            broadcasterId,
            eventType,
            decoratedData,
            cancellationToken
        );
    }
}
