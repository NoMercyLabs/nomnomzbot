// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// <see cref="WidgetConnectedEvent"/> / <see cref="WidgetDisconnectedEvent"/>: the overlay hub publishes one
/// per widget a browser source attaches or detaches, with the widget id and the connection id.
/// </summary>
public sealed class OverlayHubWidgetEventsTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f31");

    private sealed class Published
    {
        public List<WidgetConnectedEvent> Connected { get; } = [];
        public List<WidgetDisconnectedEvent> Disconnected { get; } = [];
    }

    private static async Task<(OverlayHub Hub, Published Events)> ConnectedHubAsync(
        WidgetTestDbContext db,
        string connectionId = "obs-1"
    )
    {
        OverlayTicketService tickets = new(new FakeTimeProvider());
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.Items.Returns(new Dictionary<object, object?>());
        DefaultHttpContext http = new();
        http.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
        context.GetHttpContext().Returns(http);

        IHubCallerClients<IOverlayClient> clients = Substitute.For<
            IHubCallerClients<IOverlayClient>
        >();
        clients.Caller.Returns(Substitute.For<IOverlayClient>());

        Published events = new();
        IEventBus bus = Substitute.For<IEventBus>();
        bus.When(b => b.PublishAsync(Arg.Any<WidgetConnectedEvent>(), Arg.Any<CancellationToken>()))
            .Do(call => events.Connected.Add(call.Arg<WidgetConnectedEvent>()));
        bus.When(b =>
                b.PublishAsync(Arg.Any<WidgetDisconnectedEvent>(), Arg.Any<CancellationToken>())
            )
            .Do(call => events.Disconnected.Add(call.Arg<WidgetDisconnectedEvent>()));

        OverlayHub hub = new(
            db,
            Substitute.For<IWidgetService>(),
            tickets,
            new(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            bus,
            [],
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = clients,
        };
        await hub.OnConnectedAsync();
        return (hub, events);
    }

    [Fact]
    public async Task Joining_a_widget_raises_WidgetConnected_with_the_widget_and_connection_id()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);
        Guid widgetId = Guid.CreateVersion7();

        await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        WidgetConnectedEvent raised = events.Connected.Should().ContainSingle().Subject;
        raised.WidgetId.Should().Be(widgetId);
        raised.ConnectionId.Should().Be("obs-1");
        raised.BroadcasterId.Should().Be(Broadcaster);
        events.Disconnected.Should().BeEmpty();
    }

    [Fact]
    public async Task Joining_the_same_widget_twice_on_one_connection_raises_once()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);
        Guid widgetId = Guid.CreateVersion7();

        await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");
        await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        events.Connected.Should().ContainSingle();
    }

    [Fact]
    public async Task A_widget_id_that_is_not_a_guid_raises_nothing()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);

        await hub.JoinWidgetWithSdk("not-a-guid", "test");
        await hub.LeaveWidget("not-a-guid");
        await hub.OnDisconnectedAsync(null);

        events.Connected.Should().BeEmpty();
        events.Disconnected.Should().BeEmpty();
    }

    [Fact]
    public async Task Leaving_a_joined_widget_raises_WidgetDisconnected_once()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);
        Guid widgetId = Guid.CreateVersion7();
        await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        await hub.LeaveWidget(widgetId.ToString());
        await hub.OnDisconnectedAsync(null);

        WidgetDisconnectedEvent raised = events.Disconnected.Should().ContainSingle().Subject;
        raised.WidgetId.Should().Be(widgetId);
        raised.ConnectionId.Should().Be("obs-1");
        raised.BroadcasterId.Should().Be(Broadcaster);
    }

    [Fact]
    public async Task Leaving_a_widget_that_was_never_joined_raises_nothing()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);

        await hub.LeaveWidget(Guid.CreateVersion7().ToString());

        events.Disconnected.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dropped_connection_holding_two_widgets_raises_two_WidgetDisconnected()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        (OverlayHub hub, Published events) = await ConnectedHubAsync(db);
        Guid widgetA = Guid.CreateVersion7();
        Guid widgetB = Guid.CreateVersion7();
        await hub.JoinWidgetWithSdk(widgetA.ToString(), "test");
        await hub.JoinWidgetWithSdk(widgetB.ToString(), "test");

        await hub.OnDisconnectedAsync(null);

        events.Disconnected.Select(e => e.WidgetId).Should().BeEquivalentTo([widgetA, widgetB]);
        events.Disconnected.Should().OnlyContain(e => e.ConnectionId == "obs-1");
        events.Disconnected.Should().OnlyContain(e => e.BroadcasterId == Broadcaster);
    }

    [Fact]
    public void A_widget_group_name_maps_back_to_its_widget_id_and_other_groups_do_not()
    {
        Guid widgetId = Guid.CreateVersion7();

        OverlayPresenceRegistry
            .TryParseWidgetId(
                Broadcaster,
                OverlayPresenceRegistry.GroupName(Broadcaster, widgetId.ToString()),
                out Guid parsed
            )
            .Should()
            .BeTrue();
        parsed.Should().Be(widgetId);
        OverlayPresenceRegistry
            .TryParseWidgetId(
                Broadcaster,
                OverlayPresenceRegistry.OverlayGroupName(Broadcaster),
                out _
            )
            .Should()
            .BeFalse();
        OverlayPresenceRegistry
            .TryParseWidgetId(
                Guid.CreateVersion7(),
                OverlayPresenceRegistry.GroupName(Broadcaster, widgetId.ToString()),
                out _
            )
            .Should()
            .BeFalse();
    }
}
