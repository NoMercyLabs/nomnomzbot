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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>
/// Raised by the overlay hub when a browser-source connection newly attaches to a widget (the widget join).
/// One event per widget: a channel-wide page that joins three widgets raises three. Joining a widget the
/// connection already holds raises nothing. A reconnect is a new connection, so it raises a new pair
/// (disconnected for the old connection, connected for the new one). Never raised for a widget id that is
/// not a Guid.
/// </summary>
public sealed class WidgetConnectedEvent : DomainEventBase
{
    /// <summary>The id of the widget that connected.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The id of the live connection (the connection id of the widget).</summary>
    public required string ConnectionId { get; init; } // SignalR connection id
}
