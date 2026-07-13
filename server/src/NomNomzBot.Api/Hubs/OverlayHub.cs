// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

public class OverlayHub : Hub<IOverlayClient>
{
    private static readonly ConcurrentDictionary<string, string> _connectionWidget = new(); // connectionId -> widgetId
    private readonly IChannelRegistry _registry;
    private readonly IApplicationDbContext _db;
    private readonly ILogger<OverlayHub> _logger;

    public OverlayHub(
        IChannelRegistry registry,
        IApplicationDbContext db,
        ILogger<OverlayHub> logger
    )
    {
        _registry = registry;
        _db = db;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Validate overlay token from query string
        string? token = Context.GetHttpContext()?.Request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            Context.Abort();
            return;
        }

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(c => c.OverlayToken == token);
        if (channel == null)
        {
            Context.Abort();
            return;
        }

        Context.Items["BroadcasterId"] = channel.Id;
        // All overlay connections for a broadcaster share the overlay group so sound play/stop
        // signals (and future broadcaster-wide overlay events) reach every browser source.
        await Groups.AddToGroupAsync(Context.ConnectionId, $"overlay-{channel.Id}");
        _logger.LogDebug("Overlay connected for channel {B}", channel.Id);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionWidget.TryRemove(Context.ConnectionId, out string? widgetId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"widget-{widgetId}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinWidgetResponse> JoinWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return new(false, "Not authenticated", null);

        string groupName = $"widget-{broadcasterId}-{widgetId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _connectionWidget[Context.ConnectionId] = $"{broadcasterId}-{widgetId}";
        _logger.LogDebug(
            "Overlay connection {C} joined widget {W}",
            Context.ConnectionId,
            widgetId
        );

        // Hand the browser-source its saved appearance settings up front, so it can style itself
        // before the first event arrives (the page applies the keys it understands, ignores the rest).
        Widget? widget = Guid.TryParse(widgetId, out Guid parsedWidgetId)
            ? await _db
                .Widgets.AsNoTracking()
                .FirstOrDefaultAsync(w =>
                    w.Id == parsedWidgetId && w.BroadcasterId == broadcasterId
                )
            : null;
        return new(true, null, widget?.Settings);
    }

    public async Task LeaveWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return;
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            $"widget-{broadcasterId}-{widgetId}"
        );
        _connectionWidget.TryRemove(Context.ConnectionId, out _);
    }

    public Task WidgetReady(string widgetId)
    {
        _logger.LogDebug("Widget {W} ready on connection {C}", widgetId, Context.ConnectionId);
        return Task.CompletedTask;
    }
}
