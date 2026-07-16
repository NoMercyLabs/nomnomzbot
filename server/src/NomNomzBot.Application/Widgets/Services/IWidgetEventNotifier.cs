// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Abstraction that lets pipeline actions (Infrastructure) push a widget event to the overlay group without
/// taking a direct dependency on the API layer's SignalR hub. Implemented by <c>WidgetEventNotifierAdapter</c>
/// in the API layer; a no-op <c>NullWidgetEventNotifier</c> stands in for host-less contexts (workers, tests).
/// </summary>
public interface IWidgetEventNotifier
{
    Task SendWidgetEventAsync(
        Guid broadcasterId,
        Guid widgetId,
        string eventType,
        object? data,
        CancellationToken ct = default
    );
}
