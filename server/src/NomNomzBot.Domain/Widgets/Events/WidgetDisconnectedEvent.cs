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
