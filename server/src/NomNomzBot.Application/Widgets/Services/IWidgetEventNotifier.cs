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
