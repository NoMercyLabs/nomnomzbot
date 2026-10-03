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

/// <summary>When a widget connects to the server.</summary>
public sealed class WidgetConnectedEvent : DomainEventBase
{
    /// <summary>The id of the widget that connected.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The id of the live connection (the connection id of the widget).</summary>
    public required string ConnectionId { get; init; } // SignalR connection id
}
