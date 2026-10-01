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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
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
    private const string WrongWidgetError = "This overlay token is scoped to a different widget";

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
        OverlayTokenScope? scope = _tickets.RedeemTicket(ticket);
        if (scope is null)
        {
            Context.Abort();
            return;
        }

        Context.Items["BroadcasterId"] = scope.BroadcasterId;
        // A widget-scoped ticket (audit S-OVERLAY-1: one widget's leaked token must never unlock another
        // widget on the same connection) confines every later JoinWidget call to exactly this widget.
        Context.Items["WidgetId"] = scope.WidgetId;
        // All overlay connections for a broadcaster share the overlay group so sound play/stop
        // signals (and future broadcaster-wide overlay events) reach every browser source.
        string overlayGroup = OverlayPresenceRegistry.OverlayGroupName(scope.BroadcasterId);
        await Groups.AddToGroupAsync(Context.ConnectionId, overlayGroup);
        // Tracked in the same presence registry as widget attachment, so IOverlayPresenceRegistry can
        // answer "is any browser source connected at all" for features (sound-clip stop) that push to the
        // shared bus rather than one specific widget.
        _presence.Attach(Context.ConnectionId, overlayGroup);
        _logger.LogDebug("Overlay connected for channel {B}", scope.BroadcasterId);
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

        // A widget-scoped connection (audit S-OVERLAY-1) may only ever join its OWN widget — never another
        // one on the same channel, even though the broadcaster id matches. Channel-wide connections (the
        // WidgetId scope item is null) keep joining any widget id, as before.
        if (!MayActFor(widgetId))
            return new(false, WrongWidgetError, null);

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

    /// <summary>
    /// Runs one pipeline action for a widget, as the channel owner (widget-sdk.md §8): the SDK's
    /// <c>actions.invoke</c> -> here. Only the widget the token names may act, and only within the owner's IAM.
    /// </summary>
    public async Task<WidgetActionResponse> InvokeAction(
        string widgetId,
        string actionType,
        Dictionary<string, JsonElement>? parameters,
        Dictionary<string, string>? variables,
        [FromServices] IWidgetActionService actions
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            return Refused("Not authenticated", "AUTH_REQUIRED");
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            return Refused(WrongWidgetError, "FORBIDDEN");

        Result<WidgetActionOutcome> outcome = await actions.InvokeAsync(
            new(broadcasterId, parsedWidgetId, actionType, parameters, variables),
            Context.ConnectionAborted
        );
        return outcome.IsSuccess
            ? new(
                outcome.Value.Succeeded,
                outcome.Value.Output,
                outcome.Value.Error,
                null,
                outcome.Value.Variables
            )
            : Refused(outcome.ErrorMessage ?? "Action refused", outcome.ErrorCode);
    }

    /// <summary>
    /// The SDK's <c>actions.claim</c>: true only for the first open copy of the widget to claim
    /// <paramref name="key"/>, so a widget open in two OBS sources acts on an event once.
    /// </summary>
    public async Task<bool> ClaimOnce(
        string widgetId,
        string key,
        [FromServices] IWidgetActionService actions
    )
    {
        if (Context.Items["BroadcasterId"] is not Guid broadcasterId)
            throw new HubException("Not authenticated");
        if (!MayActFor(widgetId) || !Guid.TryParse(widgetId, out Guid parsedWidgetId))
            throw new HubException(WrongWidgetError);

        Result<bool> claimed = await actions.ClaimAsync(
            broadcasterId,
            parsedWidgetId,
            key,
            Context.ConnectionAborted
        );
        return claimed.IsSuccess
            ? claimed.Value
            : throw new HubException(claimed.ErrorMessage ?? "Claim refused");
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

    /// <summary>False when the connection's ticket names one widget and <paramref name="widgetId"/> is another.</summary>
    private bool MayActFor(string widgetId) =>
        Context.Items["WidgetId"] is not Guid scopedWidgetId
        || (
            Guid.TryParse(widgetId, out Guid requestedWidgetId)
            && requestedWidgetId == scopedWidgetId
        );

    private static WidgetActionResponse Refused(string error, string? code) =>
        new(false, null, error, code, new Dictionary<string, string>());
}
