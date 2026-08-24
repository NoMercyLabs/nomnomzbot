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
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

public class OverlayHub : Hub<IOverlayClient>
{
    // connectionId -> the set of widget group names ("widget-{broadcasterId}-{widgetId}") this connection has
    // joined. A single browser source can host MANY widgets on one page (S035 item 2) — the old single-value
    // map remembered only the LAST widget joined, so earlier widgets on the same page silently stopped
    // receiving events once a second widget joined, and a disconnect leaked every earlier group.
    private static readonly ConcurrentDictionary<
        string,
        ConcurrentDictionary<string, byte>
    > _connectionWidgets = new();
    private readonly IApplicationDbContext _db;
    private readonly IWidgetService _widgetService;
    private readonly IOverlayTicketService _tickets;
    private readonly ILogger<OverlayHub> _logger;

    public OverlayHub(
        IApplicationDbContext db,
        IWidgetService widgetService,
        IOverlayTicketService tickets,
        ILogger<OverlayHub> logger
    )
    {
        _db = db;
        _widgetService = widgetService;
        _tickets = tickets;
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
        await Groups.AddToGroupAsync(Context.ConnectionId, $"overlay-{broadcasterId}");
        _logger.LogDebug("Overlay connected for channel {B}", broadcasterId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (
            _connectionWidgets.TryRemove(
                Context.ConnectionId,
                out ConcurrentDictionary<string, byte>? widgetGroups
            )
        )
            foreach (string groupName in widgetGroups.Keys)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<JoinWidgetResponse> JoinWidget(string widgetId)
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return new(false, "Not authenticated", null);

        string groupName = $"widget-{broadcasterId}-{widgetId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        ConcurrentDictionary<string, byte> widgetGroups = _connectionWidgets.GetOrAdd(
            Context.ConnectionId,
            static _ => new(StringComparer.Ordinal)
        );
        widgetGroups[groupName] = 0;
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
        string groupName = $"widget-{broadcasterId}-{widgetId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        if (
            _connectionWidgets.TryGetValue(
                Context.ConnectionId,
                out ConcurrentDictionary<string, byte>? widgetGroups
            )
        )
            widgetGroups.TryRemove(groupName, out _);
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
