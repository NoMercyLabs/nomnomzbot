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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Alerts.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// Fans one fully-decorated alert DTO to every overlay surface at once, so an OBS overlay never sees a thin or raw
/// payload for a user-facing event: (a) the generic overlay feed — the same feed the raw journaled event would have
/// ridden — now carrying the decorated dto as a camelCase JSON string keyed by the event type; (b) the ONE
/// cross-platform alert queue (widgets-overlays.md §1.2), written FIRST via <see cref="IAlertQueueService"/> exactly
/// as <c>SupporterWidgetEventHandler</c> writes a supporter event, with the SAME presence-checked delivery straight
/// to the auto-provisioned "alerts" system surface (S059b — platform-native alerts previously bypassed this queue
/// entirely); and (c) every OTHER widget subscribed to that event type, via the shared <see cref="WidgetAlertDispatch"/>
/// routing — with the alerts surface excluded from this fan-out, since its delivery already happened in (b) and a
/// second push would double-fire the identical OverlayHub group. A dashboard alert handler calls this right after
/// its existing dashboard push, reusing the SAME decorated dto — so widgets, the generic feed, the queue, and the
/// dashboard all render from one shape (avatar/pronouns/community standing plus the event's resolved fields).
/// <c>OverlayEventFilter.DecoratedElsewhere</c> drops the raw journaled duplicate so the generic feed carries only
/// this decorated form.
/// </summary>
internal static class OverlayAlertBroadcast
{
    // Not a gallery item any more (widgets-overlays.md §1.2 header) — matches FirstPartyWidgetCatalogue's
    // "alerts" entry and AlertQueueService's own constant.
    private const string AlertsSurfaceNaturalKey = "alerts";

    // camelCase so the overlay-feed payload byte-matches the frontend alert shape the dashboard receives over
    // SignalR (mirrors ChatMessageBroadcastHandler.OverlayJson) — one shared options instance, reused for every alert.
    private static readonly JsonSerializerOptions OverlayJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task ToOverlaysAsync(
        IApplicationDbContext db,
        IWidgetNotifier notifier,
        IAlertQueueService alertQueue,
        IWidgetService widgetService,
        Guid broadcasterId,
        string provider,
        string eventType,
        object decoratedData,
        string? channelEventId,
        CancellationToken cancellationToken
    )
    {
        if (broadcasterId == Guid.Empty)
            return;

        // (a) Generic overlay feed — one decorated event, replacing the raw journaled form the filter now drops.
        await notifier.BroadcastOverlayEventAsync(
            broadcasterId.ToString(),
            new(eventType, JsonSerializer.Serialize(decoratedData, OverlayJson)),
            cancellationToken
        );

        // (b) The one alert queue across every platform connection — written FIRST, regardless of whether any
        //     widget is installed or attached, with `provider` as the cross-provider attribution (twitch/kick/
        //     patreon/shopify/…). Mirrors SupporterWidgetEventHandler's EnqueueAsync call exactly.
        await alertQueue.EnqueueAsync(
            broadcasterId,
            provider,
            eventType,
            decoratedData,
            cancellationToken
        );

        // Resolve the auto-provisioned alerts surface purely to EXCLUDE its widget id from the fan-out below —
        // its delivery (presence-checked, honest Delivered/Queued status) already happened inside EnqueueAsync
        // above. Idempotent get-or-create, same as SupporterWidgetEventHandler's post-enqueue resolve. A null
        // or failed result (never returned by the real WidgetService, but possible from an unconfigured test
        // double) simply means nothing is excluded — the fan-out then behaves exactly as it did before S059b.
        Result<WidgetDetail>? alertsSurface = await widgetService.EnsureSystemWidgetAsync(
            broadcasterId.ToString(),
            AlertsSurfaceNaturalKey,
            cancellationToken
        );
        Guid? alertsSurfaceId = alertsSurface is { IsSuccess: true }
            ? alertsSurface.Value.Id
            : null;

        // (c) Each OTHER widget subscribed to this event type — the SAME decorated dto, routed by the shared
        //     dispatcher (WidgetAlertRouting.Subscribers → SendWidgetEventAsync), not a second minimal payload.
        await WidgetAlertDispatch.RouteAsync(
            db,
            notifier,
            broadcasterId,
            eventType,
            decoratedData,
            alertsSurfaceId,
            channelEventId,
            cancellationToken
        );
    }
}
