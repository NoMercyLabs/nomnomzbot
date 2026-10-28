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
/// Raised by the overlay hub when a connection that held a widget lets it go: an explicit leave of a
/// widget it had joined, or the connection dropping. One event per widget: a dropped connection that held
/// two widgets raises two. Leaving a widget that was never joined raises nothing, and a leave followed by
/// the drop raises only once. A reconnect raises a new pair (this one for the old connection, a
/// connected event for the new one).
/// </summary>
public sealed class WidgetDisconnectedEvent : DomainEventBase
{
    /// <summary>The id of the widget that disconnected.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The id of the live connection (the connection id of the widget).</summary>
    public required string ConnectionId { get; init; } // SignalR connection id
}
