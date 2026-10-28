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

using FluentAssertions;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the overlay alert-routing decision (which widgets render an event): only enabled widgets that declare the
/// event type in their subscriptions are selected — a disabled widget or one that does not subscribe is skipped.
/// </summary>
public sealed class WidgetAlertRoutingTests
{
    // The id argument is a readable label for the assertions; routing keys off EventSubscriptions + IsEnabled,
    // never the (now Guid) Id, so the label lives on Name and the assertions compare Name.
    private static Widget Widget(string name, bool enabled, params string[] subscriptions) =>
        new()
        {
            Name = name,
            IsEnabled = enabled,
            EventSubscriptions = [.. subscriptions],
        };

    [Fact]
    public void Subscribers_selects_only_enabled_widgets_that_declare_the_event()
    {
        List<Widget> widgets =
        [
            Widget("a", enabled: true, "subscription", "follow"),
            Widget("b", enabled: true, "follow"), // does not subscribe
            Widget("c", enabled: false, "subscription"), // disabled
        ];

        List<string> selected =
        [
            .. WidgetAlertRouting.Subscribers(widgets, "subscription").Select(w => w.Name),
        ];

        selected.Should().Equal("a");
    }

    [Fact]
    public void Subscribers_is_empty_when_no_widget_declares_the_event()
    {
        List<Widget> widgets = [Widget("a", enabled: true, "follow", "cheer")];

        WidgetAlertRouting.Subscribers(widgets, "subscription").Should().BeEmpty();
    }
}
