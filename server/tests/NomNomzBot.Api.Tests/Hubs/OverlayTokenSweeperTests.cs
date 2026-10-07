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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S-ROTATED-TOKEN-OPEN-PAGE: a page that connected on a widget token the widget no longer accepts must be
/// closed. Proves the consequence — the old-token connection is aborted, the current-token connection stays —
/// at grace end, at once on a second rotation, for a deleted widget, and that the hub records the token a page
/// came in on.
/// </summary>
public sealed class OverlayTokenSweeperTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f31");

    private sealed class Rig : IDisposable
    {
        public WidgetTestDbContext Db { get; } = WidgetTestDbContext.New();
        public FakeTimeProvider Clock { get; } = new(new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        public OverlayPresenceRegistry Presence { get; } = new();
        public OverlayTokenSweeper Sweeper { get; }

        public Rig()
        {
            ServiceCollection services = new();
            services.AddSingleton<IApplicationDbContext>(Db);
            Sweeper = new(
                Presence,
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
                Clock
            );
        }

        public async Task<Widget> SeedWidgetAsync(
            string token,
            string? previous = null,
            TimeSpan? previousLeft = null
        )
        {
            Widget widget = new()
            {
                BroadcasterId = Broadcaster,
                Name = "Alerts",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
                OverlayToken = token,
                PreviousOverlayToken = previous,
                PreviousOverlayTokenExpiresAt = previous is null
                    ? null
                    : Clock.GetUtcNow().Add(previousLeft!.Value).UtcDateTime,
            };
            Db.Widgets.Add(widget);
            await Db.SaveChangesAsync();
            return widget;
        }

        public HubCallerContext Bind(string connectionId, Guid widgetId, string token)
        {
            HubCallerContext context = Substitute.For<HubCallerContext>();
            context.ConnectionId.Returns(connectionId);
            Presence.BindToken(connectionId, widgetId, token, context);
            return context;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Old_token_connection_is_aborted_once_the_grace_window_ends_and_the_current_one_stays()
    {
        using Rig rig = new();
        Widget widget = await rig.SeedWidgetAsync("new-tok", "old-tok", TimeSpan.FromMinutes(15));
        HubCallerContext onOld = rig.Bind("conn-old", widget.Id, "old-tok");
        HubCallerContext onNew = rig.Bind("conn-new", widget.Id, "new-tok");

        // Inside the grace window both URLs are still valid: nothing is closed.
        (await rig.Sweeper.SweepAsync(null, CancellationToken.None))
            .Should()
            .Be(0);
        onOld.DidNotReceive().Abort();

        rig.Clock.Advance(TimeSpan.FromMinutes(16));
        int closed = await rig.Sweeper.SweepAsync(null, CancellationToken.None);

        closed.Should().Be(1);
        onOld.Received(1).Abort();
        onNew.DidNotReceive().Abort();
    }

    [Fact]
    public async Task A_second_rotation_aborts_the_oldest_token_at_once_and_keeps_the_grace_token()
    {
        using Rig rig = new();
        Widget widget = await rig.SeedWidgetAsync("mid-tok", "old-tok", TimeSpan.FromMinutes(10));
        HubCallerContext onOld = rig.Bind("conn-old", widget.Id, "old-tok");
        HubCallerContext onMid = rig.Bind("conn-mid", widget.Id, "mid-tok");

        // The second rotation: the live token is retired into the grace slot, the oldest drops out.
        widget.PreviousOverlayToken = "mid-tok";
        widget.PreviousOverlayTokenExpiresAt = rig.Clock.GetUtcNow().AddMinutes(15).UtcDateTime;
        widget.OverlayToken = "newest-tok";
        await rig.Db.SaveChangesAsync();

        int closed = await rig.Sweeper.SweepAsync(widget.Id, CancellationToken.None);

        closed.Should().Be(1);
        onOld.Received(1).Abort();
        onMid.DidNotReceive().Abort();
    }

    [Fact]
    public async Task A_deleted_widget_has_no_valid_token_so_every_connection_is_aborted()
    {
        using Rig rig = new();
        Widget widget = await rig.SeedWidgetAsync("live-tok");
        HubCallerContext conn = rig.Bind("conn-live", widget.Id, "live-tok");
        widget.DeletedAt = rig.Clock.GetUtcNow().UtcDateTime;
        await rig.Db.SaveChangesAsync();

        (await rig.Sweeper.SweepAsync(widget.Id, CancellationToken.None)).Should().Be(1);

        conn.Received(1).Abort();
    }

    [Fact]
    public async Task The_rotated_event_handler_sweeps_that_widget_straight_away()
    {
        using Rig rig = new();
        Widget widget = await rig.SeedWidgetAsync("newest-tok");
        HubCallerContext onRetired = rig.Bind("conn-retired", widget.Id, "old-tok");
        HubCallerContext onLive = rig.Bind("conn-live", widget.Id, "newest-tok");

        await new OverlayTokenRotatedHandler(rig.Sweeper).HandleAsync(
            new WidgetOverlayTokenRotatedEvent { BroadcasterId = Broadcaster, WidgetId = widget.Id }
        );

        onRetired.Received(1).Abort();
        onLive.DidNotReceive().Abort();
    }

    [Fact]
    public async Task Hub_connect_binds_the_widget_token_and_disconnect_forgets_it()
    {
        using Rig rig = new();
        Guid widgetId = Guid.CreateVersion7();
        OverlayTicketService tickets = new(rig.Clock);
        string ticket = tickets.IssueTicket(
            new OverlayTokenScope(Broadcaster, widgetId, "old-tok")
        );
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("conn-1");
        DefaultHttpContext http = new();
        http.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
        context.GetHttpContext().Returns(http);
        context.Items.Returns(new Dictionary<object, object?>());
        OverlayHub hub = new(
            rig.Db,
            Substitute.For<IWidgetService>(),
            tickets,
            rig.Presence,
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            [],
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
        };

        await hub.OnConnectedAsync();

        OverlayPresenceRegistry.BoundConnection bound = rig
            .Presence.BoundConnections(widgetId)
            .Should()
            .ContainSingle()
            .Subject;
        bound.ConnectionId.Should().Be("conn-1");
        bound.Token.Should().Be("old-tok");

        await hub.OnDisconnectedAsync(null);

        rig.Presence.BoundConnections(widgetId).Should().BeEmpty();
    }
}
