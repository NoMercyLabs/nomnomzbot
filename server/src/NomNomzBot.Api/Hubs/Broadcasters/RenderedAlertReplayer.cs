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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// <see cref="IRenderedAlertReplayer"/> over the <see cref="RenderedAlertCapture"/> rows
/// <see cref="WidgetAlertDispatch"/> wrote at the original push: each captured payload goes out verbatim, in
/// capture order, through the same <see cref="WidgetAlertDispatch.PushAsync"/> a live alert uses, to the widgets
/// subscribed to it now. It writes nothing — a replay is not a new alert, so it is never captured again.
/// </summary>
public sealed class RenderedAlertReplayer(IApplicationDbContext db, IWidgetNotifier notifier)
    : IRenderedAlertReplayer
{
    public async Task<int> ResendAsync(
        Guid broadcasterId,
        string channelEventId,
        bool includeTts,
        CancellationToken ct = default
    )
    {
        List<RenderedAlertCapture> captures = await db
            .RenderedAlertCaptures.AsNoTracking()
            .Where(c =>
                c.BroadcasterId == broadcasterId
                && c.ChannelEventId == channelEventId
                && (includeTts || c.EventType != TtsSpeakBroadcastHandler.WidgetEventType)
            )
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        if (captures.Count == 0)
            return 0;

        List<Widget> widgets = await db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId)
            .ToListAsync(ct);

        int pushed = 0;
        foreach (RenderedAlertCapture capture in captures)
            pushed += await WidgetAlertDispatch.PushAsync(
                notifier,
                broadcasterId,
                WidgetAlertRouting.Subscribers(widgets, capture.EventType),
                capture.EventType,
                JsonSerializer.Deserialize<JsonElement>(capture.Payload),
                ct
            );

        return pushed;
    }
}
