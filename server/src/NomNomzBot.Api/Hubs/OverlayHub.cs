// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

public class OverlayHub : Hub<IOverlayClient>
{
    // The widget subscription key WidgetNowPlayingHandler pushes under (WidgetAlertHandlers.cs) — a widget
    // that never subscribed to it renders no music state, so its connection has nothing to gain from a fast
    // poll cadence.
    private const string NowPlayingEventKey = "now_playing";

    private readonly IApplicationDbContext _db;
    private readonly IWidgetService _widgetService;
    private readonly IOverlayTicketService _tickets;
    private readonly OverlayPresenceRegistry _presence;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<OverlayHub> _logger;

    public OverlayHub(
        IApplicationDbContext db,
        IWidgetService widgetService,
        IOverlayTicketService tickets,
        OverlayPresenceRegistry presence,
        IChannelRegistry registry,
        ILogger<OverlayHub> logger
    )
    {
        _db = db;
        _widgetService = widgetService;
        _tickets = tickets;
        _presence = presence;
        _registry = registry;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // The long-lived overlay token never rides on this URL (S035 item 3, U·B5/B7): the SDK exchanges it
        // for a short-lived, single-use ticket via POST /overlay/ticket (header, not query string) first, and
        // only that ticket appears here.
        string? ticket = Context.GetHttpContext()?.Request.Query["ticket"].ToString();
        Guid? broadcasterId = _tickets.RedeemTicket(ticket);
        if (broadcasterId is null)
        {
            Context.Abort();
            return;
        }

        Context.Items["BroadcasterId"] = broadcasterId.Value;
        // All overlay connections for a broadcaster share the overlay group so sound play/stop
        // signals (and future broadcaster-wide overlay events) reach every browser source.
        string overlayGroup = OverlayPresenceRegistry.OverlayGroupName(broadcasterId.Value);
        await Groups.AddToGroupAsync(Context.ConnectionId, overlayGroup);
        // Tracked in the same presence registry as widget attachment, so IOverlayPresenceRegistry can
        // answer "is any browser source connected at all" for features (sound-clip stop) that push to the
        // shared bus rather than one specific widget.
        _presence.Attach(Context.ConnectionId, overlayGroup);
        _logger.LogDebug("Overlay connected for channel {B}", broadcasterId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (string groupName in _presence.Drop(Context.ConnectionId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        if (Context.Items["BroadcasterId"] is Guid broadcasterId)
            _registry.ReleaseMusicDemand(broadcasterId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinWidgetResponse> JoinWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return new(false, "Not authenticated", null);

        string groupName = OverlayPresenceRegistry.GroupName(broadcasterId, widgetId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _presence.Attach(Context.ConnectionId, groupName);
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

        // A real browser source just (re)connected — proof the widget is alive right now, so a fault stamped
        // by a past session (S-PL1: e.g. a one-off autoplay block on first load) must not keep painting the
        // dashboard row red forever. ReportRuntimeError re-stamps it below if the fault is still live.
        if (widget is not null)
            await _widgetService.ClearRuntimeErrorAsync(broadcasterId.ToString(), widgetId);

        // A now-playing widget just came alive on stream — keep the music poller at its fast cadence for
        // this channel for as long as this connection is here (see IChannelRegistry.TouchMusicDemand).
        if (widget?.IsEnabled == true && widget.EventSubscriptions.Contains(NowPlayingEventKey))
            _registry.TouchMusicDemand(broadcasterId, Context.ConnectionId);

        return new(true, null, widget?.Settings);
    }

    public async Task LeaveWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return;
        string groupName = OverlayPresenceRegistry.GroupName(broadcasterId, widgetId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _presence.Detach(Context.ConnectionId, groupName);
        _registry.ReleaseMusicDemand(broadcasterId, Context.ConnectionId);
    }

    public Task WidgetReady(string widgetId)
    {
        _logger.LogDebug("Widget {W} ready on connection {C}", widgetId, Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// An overlay-reported runtime fault for a widget (the SDK's <c>reportError</c> -> host -> here). Recorded
    /// against the widget (audit B5). Best-effort: an unparseable id or a widget the token's channel does not own is
    /// ignored by the service; the message is truncated so a looping widget cannot bloat the row.
    /// </summary>
    public async Task ReportRuntimeError(string widgetId, string error)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return;
        string trimmed = error.Length > 2000 ? error[..2000] : error;
        await _widgetService.RecordRuntimeErrorAsync(broadcasterId.ToString(), widgetId, trimmed);
    }
}
