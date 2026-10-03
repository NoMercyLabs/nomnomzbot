// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
