// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using FluentAssertions;
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

/// <summary>
/// Proves the P/Invoke-free tray model: the dashboard URL and tooltip are built from the bound port, the menu lists
/// the actionable items in order, and a Win32 command id resolves back to the correct action (the lookup the
/// message loop relies on to route a click). The Win32 message loop itself is not unit-testable and is excluded by
/// design; everything decidable lives here and is asserted.
/// </summary>
public sealed class SystemTrayMenuTests
{
    [Theory]
    [InlineData(5080, "http://localhost:5080")]
    [InlineData(51234, "http://localhost:51234")]
    public void DashboardUrl_is_the_loopback_url_for_the_bound_port(int port, string expected)
    {
        SystemTrayMenu.DashboardUrl(port).Should().Be(expected);
    }

    [Theory]
    [InlineData(5080, "NomNomzBot — running on http://localhost:5080")]
    [InlineData(51234, "NomNomzBot — running on http://localhost:51234")]
    public void Tooltip_names_the_product_and_the_bound_url(int port, string expected)
    {
        SystemTrayMenu.Tooltip(port).Should().Be(expected);
    }

    [Fact]
    public void Items_are_open_dashboard_then_stop_in_order()
    {
        IReadOnlyList<TrayMenuItem> items = SystemTrayMenu.Items;

        items.Should().HaveCount(2);
        items[0].Command.Should().Be(TrayCommand.OpenDashboard);
        items[0].Label.Should().Be("Open dashboard");
        items[1].Command.Should().Be(TrayCommand.StopApplication);
        items[1].Label.Should().Be("Stop NomNomzBot");
    }

    [Fact]
    public void ResolveCommand_maps_open_dashboard_id_to_open_dashboard()
    {
        SystemTrayMenu
            .ResolveCommand(SystemTrayMenu.OpenDashboardCommandId)
            .Should()
            .Be(TrayCommand.OpenDashboard);
    }

    [Fact]
    public void ResolveCommand_maps_stop_id_to_stop_application()
    {
        SystemTrayMenu
            .ResolveCommand(SystemTrayMenu.StopCommandId)
            .Should()
            .Be(TrayCommand.StopApplication);
    }

    [Fact]
    public void ResolveCommand_is_null_for_an_unknown_id()
    {
        // The menu's disabled header / separators carry no command id; an unknown id (e.g. 0) must not map to an
        // action so the message loop ignores it rather than firing OpenDashboard/Stop by accident.
        SystemTrayMenu.ResolveCommand(0).Should().BeNull();
        SystemTrayMenu.ResolveCommand(0xFFFF).Should().BeNull();
    }

    [Fact]
    public void Each_item_id_resolves_back_to_its_own_command()
    {
        // The mapping is data-driven; assert every item round-trips so the menu render and the click handler can
        // never drift apart.
        foreach (TrayMenuItem item in SystemTrayMenu.Items)
            SystemTrayMenu.ResolveCommand(item.CommandId).Should().Be(item.Command);
    }
}
