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

using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Adapts the Application-layer <see cref="IWidgetEventNotifier"/> abstraction to the
/// <see cref="IWidgetNotifier"/> SignalR hub — bridges the Infrastructure→API dependency boundary so the
/// <c>widget_event</c> pipeline action never takes a direct reference to the SignalR layer.
/// </summary>
internal sealed class WidgetEventNotifierAdapter : IWidgetEventNotifier
{
    private readonly IWidgetNotifier _notifier;

    public WidgetEventNotifierAdapter(IWidgetNotifier notifier)
    {
        _notifier = notifier;
    }

    public Task SendWidgetEventAsync(
        Guid broadcasterId,
        Guid widgetId,
        string eventType,
        object? data,
        CancellationToken ct = default
    ) =>
        eventType
            is "youtube.play"
                or "youtube.pause"
                or "youtube.resume"
                or "youtube.stop"
                or "youtube.seek"
                or "youtube.volume"
            ? _notifier.SendMusicWidgetEventAsync(
                broadcasterId.ToString(),
                new(widgetId.ToString(), eventType, data),
                ct
            )
            : _notifier.SendWidgetEventAsync(
                broadcasterId.ToString(),
                widgetId.ToString(),
                new(widgetId.ToString(), eventType, data),
                ct
            );
}
