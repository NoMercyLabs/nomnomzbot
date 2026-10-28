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
        widgets.Where(w => w.IsEnabled && w.Wants(eventType));
}
