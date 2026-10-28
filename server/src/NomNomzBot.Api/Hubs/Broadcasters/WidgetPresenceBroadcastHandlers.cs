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

using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A widget's browser source attached: tells the channel's dashboards the <c>widgets</c> list changed, so
/// "Loaded in OBS" updates as the streamer adds the source instead of on the next manual refresh.
/// </summary>
public sealed class WidgetConnectedBroadcastHandler : IEventHandler<WidgetConnectedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public WidgetConnectedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(WidgetConnectedEvent @event, CancellationToken ct = default) =>
        WidgetPresenceSignal.SendAsync(_notifier, @event.BroadcasterId, @event.WidgetId, ct);
}

/// <summary>A widget's browser source detached: the same <c>widgets</c> change signal as the connect.</summary>
public sealed class WidgetDisconnectedBroadcastHandler : IEventHandler<WidgetDisconnectedEvent>
{
    private readonly IDashboardNotifier _notifier;

    public WidgetDisconnectedBroadcastHandler(IDashboardNotifier notifier) => _notifier = notifier;

    public Task HandleAsync(WidgetDisconnectedEvent @event, CancellationToken ct = default) =>
        WidgetPresenceSignal.SendAsync(_notifier, @event.BroadcasterId, @event.WidgetId, ct);
}

internal static class WidgetPresenceSignal
{
    public static Task SendAsync(
        IDashboardNotifier notifier,
        Guid broadcasterId,
        Guid widgetId,
        CancellationToken ct
    )
    {
        if (broadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = broadcasterId.ToString();
        return notifier.SendConfigChangedAsync(
            channel,
            new(channel, "widgets", widgetId.ToString(), "updated"),
            ct
        );
    }
}
