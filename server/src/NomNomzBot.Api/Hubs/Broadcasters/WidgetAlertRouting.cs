// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The overlay alert-routing decision: which of a channel's widgets render a given event. A widget renders an event
/// type only when it is enabled and declares that type in its <see cref="Widget.EventSubscriptions"/> — the
/// browser-source then receives the matching <c>WidgetEvent</c> over <c>OverlayHub</c>. Kept as a pure function so
/// the routing is unit-tested without a database.
/// </summary>
public static class WidgetAlertRouting
{
    public static IEnumerable<Widget> Subscribers(IEnumerable<Widget> widgets, string eventType) =>
        widgets.Where(w => w.IsEnabled && w.EventSubscriptions.Contains(eventType));
}
