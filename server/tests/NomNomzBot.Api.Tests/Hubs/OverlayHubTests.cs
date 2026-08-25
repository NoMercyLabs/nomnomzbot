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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// S035 items 2 + 3 — <see cref="OverlayHub"/> connection security and multi-widget fan-in.
///
/// Item 2: a single browser-source connection can host MANY widgets on one page. Proves
/// <see cref="OverlayHub.JoinWidget"/> for two widget ids adds the connection to BOTH groups (never replacing
/// the first), and disconnect leaves every joined group — not just the last one.
///
/// Item 3: the long-lived overlay token no longer authenticates the hub connection directly — only a
/// short-lived, single-use ticket minted by <see cref="IOverlayTicketService"/> does. Proves a connection
/// authenticates via a ticket (no token in the query string reaching the hub), an invalid/expired/reused
/// ticket is rejected (<c>Context.Abort()</c>), and <see cref="OverlayConnectionThrottle"/> rejects the
/// (N+1)th attempt from one source within its window.
/// </summary>
public sealed class OverlayHubTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f21");

    private sealed record Fixture(
        OverlayHub Hub,
        IGroupManager Groups,
        HubCallerContext Context,
        FakeHttpContextAccessor HttpAccessor
    );

    /// <summary>A minimal stand-in that lets the test control what <c>Context.GetHttpContext()</c> returns
    /// (the hub reads the <c>ticket</c> query parameter from it) without booting a real Kestrel pipeline.</summary>
    private sealed class FakeHttpContextAccessor
    {
        public HttpContext BuildContext(string? ticket)
        {
            DefaultHttpContext context = new();
            if (ticket != null)
                context.Request.QueryString = new("?ticket=" + Uri.EscapeDataString(ticket));
            return context;
        }
    }

    private static Fixture Build(
        WidgetTestDbContext db,
        IOverlayTicketService tickets,
        string? ticket,
        string connectionId = "obs-conn"
    )
    {
        FakeHttpContextAccessor accessor = new();
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.GetHttpContext().Returns(accessor.BuildContext(ticket));

        IGroupManager groups = Substitute.For<IGroupManager>();
        IWidgetService widgetService = Substitute.For<IWidgetService>();

        OverlayHub hub = new(db, widgetService, tickets, new(), NullLogger<OverlayHub>.Instance)
        {
            Context = context,
            Groups = groups,
        };
        return new(hub, groups, context, accessor);
    }

    // ── Item 3: ticket-based auth (no query-string token) ──────────────────────────────────────

    [Fact]
    public async Task OnConnectedAsync_with_a_valid_ticket_authenticates_and_joins_the_overlay_group()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        string ticket = tickets.IssueTicket(Broadcaster);
        Fixture f = Build(db, tickets, ticket);

        await f.Hub.OnConnectedAsync();

        await f
            .Groups.Received(1)
            .AddToGroupAsync("obs-conn", $"overlay-{Broadcaster}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_with_a_missing_ticket_is_rejected()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(db, tickets, ticket: null);

        await f.Hub.OnConnectedAsync();

        await f
            .Groups.DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.Context.Received(1).Abort();
    }

    [Fact]
    public async Task OnConnectedAsync_with_an_unknown_ticket_is_rejected()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(db, tickets, ticket: "not-a-real-ticket");

        await f.Hub.OnConnectedAsync();

        f.Context.Received(1).Abort();
    }

    [Fact]
    public async Task A_ticket_cannot_be_used_twice()
    {
        // The whole point of a burn-on-use ticket: intercepting one in flight buys the attacker exactly one
        // connection attempt, not standing access like the old long-lived token-in-the-URL did.
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        string ticket = tickets.IssueTicket(Broadcaster);
        Fixture first = Build(db, tickets, ticket, connectionId: "conn-1");
        await first.Hub.OnConnectedAsync();

        Fixture second = Build(db, tickets, ticket, connectionId: "conn-2");
        await second.Hub.OnConnectedAsync();

        await first
            .Groups.Received(1)
            .AddToGroupAsync("conn-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await second
            .Groups.DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        second.Context.Received(1).Abort();
    }

    [Fact]
    public async Task An_expired_ticket_is_rejected()
    {
        FakeTimeProvider clock = new();
        OverlayTicketService tickets = new(clock);
        string ticket = tickets.IssueTicket(Broadcaster);
        clock.Advance(TimeSpan.FromSeconds(31)); // past the 30s ticket lifetime

        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Fixture f = Build(db, tickets, ticket);

        await f.Hub.OnConnectedAsync();

        f.Context.Received(1).Abort();
    }

    [Fact]
    public void OverlayConnectionThrottle_rejects_the_Nplus1th_attempt_from_one_source()
    {
        FakeTimeProvider clock = new();
        OverlayConnectionThrottle throttle = new(clock);
        const string source = "leaked-token";

        List<bool> results = [.. Enumerable.Range(0, 11).Select(_ => throttle.TryAcquire(source))];

        results.Take(10).Should().AllSatisfy(allowed => allowed.Should().BeTrue());
        results[10].Should().BeFalse("the 11th attempt inside the same window must be throttled");
    }

    [Fact]
    public void OverlayConnectionThrottle_allows_a_fresh_attempt_once_the_window_rolls_over()
    {
        FakeTimeProvider clock = new();
        OverlayConnectionThrottle throttle = new(clock);
        const string source = "reconnecting-obs-source";

        for (int i = 0; i < 10; i++)
            throttle.TryAcquire(source).Should().BeTrue();
        throttle.TryAcquire(source).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(11)); // past the 10s window

        throttle.TryAcquire(source).Should().BeTrue("a new window resets the budget");
    }

    [Fact]
    public void OverlayConnectionThrottle_tracks_different_sources_independently()
    {
        FakeTimeProvider clock = new();
        OverlayConnectionThrottle throttle = new(clock);

        for (int i = 0; i < 10; i++)
            throttle.TryAcquire("source-a").Should().BeTrue();
        throttle.TryAcquire("source-a").Should().BeFalse();

        // A DIFFERENT source is not penalized by source-a's exhausted budget.
        throttle.TryAcquire("source-b").Should().BeTrue();
    }

    // ── Item 2: many widgets per connection ─────────────────────────────────────────────────────

    [Fact]
    public async Task JoinWidget_for_two_widgets_subscribes_the_connection_to_both_groups()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(db, tickets, ticket: tickets.IssueTicket(Broadcaster));
        await f.Hub.OnConnectedAsync();

        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();

        JoinWidgetResponse joinA = await f.Hub.JoinWidget(widgetA.ToString());
        JoinWidgetResponse joinB = await f.Hub.JoinWidget(widgetB.ToString());

        joinA.Success.Should().BeTrue();
        joinB.Success.Should().BeTrue();
        await f
            .Groups.Received(1)
            .AddToGroupAsync(
                "obs-conn",
                $"widget-{Broadcaster}-{widgetA}",
                Arg.Any<CancellationToken>()
            );
        await f
            .Groups.Received(1)
            .AddToGroupAsync(
                "obs-conn",
                $"widget-{Broadcaster}-{widgetB}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Disconnecting_leaves_every_joined_widget_group_not_just_the_last_one()
    {
        // Regression guard for the old single-value connectionId -> widgetId map: joining a SECOND widget used
        // to silently forget the first, so its group was never left on disconnect (a leaked subscription) and
        // — worse — the first widget stopped receiving pushes the moment a second one joined on the same page.
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(Broadcaster),
            connectionId: "multi-widget-conn"
        );
        await f.Hub.OnConnectedAsync();
        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();
        Guid widgetC = Guid.NewGuid(); // never joined — must never be touched
        await f.Hub.JoinWidget(widgetA.ToString());
        await f.Hub.JoinWidget(widgetB.ToString());

        await f.Hub.OnDisconnectedAsync(null);

        await f
            .Groups.Received(1)
            .RemoveFromGroupAsync(
                "multi-widget-conn",
                $"widget-{Broadcaster}-{widgetA}",
                Arg.Any<CancellationToken>()
            );
        await f
            .Groups.Received(1)
            .RemoveFromGroupAsync(
                "multi-widget-conn",
                $"widget-{Broadcaster}-{widgetB}",
                Arg.Any<CancellationToken>()
            );
        await f
            .Groups.DidNotReceive()
            .RemoveFromGroupAsync(
                "multi-widget-conn",
                $"widget-{Broadcaster}-{widgetC}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LeaveWidget_drops_only_that_widget_keeping_the_others_joined()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(Broadcaster),
            connectionId: "leave-one-conn"
        );
        await f.Hub.OnConnectedAsync();
        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();
        await f.Hub.JoinWidget(widgetA.ToString());
        await f.Hub.JoinWidget(widgetB.ToString());

        await f.Hub.LeaveWidget(widgetA.ToString());
        f.Groups.ClearReceivedCalls();

        // Widget B is still joined: a later disconnect drops exactly it, and never re-touches the left widget.
        await f.Hub.OnDisconnectedAsync(null);
        await f
            .Groups.Received(1)
            .RemoveFromGroupAsync(
                "leave-one-conn",
                $"widget-{Broadcaster}-{widgetB}",
                Arg.Any<CancellationToken>()
            );
        await f
            .Groups.DidNotReceive()
            .RemoveFromGroupAsync(
                "leave-one-conn",
                $"widget-{Broadcaster}-{widgetA}",
                Arg.Any<CancellationToken>()
            );
    }
}
