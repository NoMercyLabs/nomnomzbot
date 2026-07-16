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
        _notifier.SendWidgetEventAsync(
            broadcasterId.ToString(),
            widgetId.ToString(),
            new WidgetEventDto(widgetId.ToString(), eventType, data),
            ct
        );
}
