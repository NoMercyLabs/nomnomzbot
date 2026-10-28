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

using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
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
        string connectionId = "obs-conn",
        IChannelRegistry? registry = null,
        IEventBus? bus = null
    )
    {
        FakeHttpContextAccessor accessor = new();
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.GetHttpContext().Returns(accessor.BuildContext(ticket));

        IGroupManager groups = Substitute.For<IGroupManager>();
        IWidgetService widgetService = Substitute.For<IWidgetService>();

        IChannelRegistry channelRegistry = registry ?? Substitute.For<IChannelRegistry>();
        OverlayHub hub = new(
            db,
            widgetService,
            tickets,
            new(),
            channelRegistry,
            Substitute.For<IActionRequiredChangeNotifier>(),
            bus ?? Substitute.For<IEventBus>(),
            [],
            NullLogger<OverlayHub>.Instance
        )
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
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
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
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
        Fixture first = Build(db, tickets, ticket, connectionId: "conn-1");
        await first.Hub.OnConnectedAsync();

        Fixture second = Build(db, tickets, ticket, connectionId: "conn-2");
        await second.Hub.OnConnectedAsync();

        await first
            .Groups.Received(1)
            .AddToGroupAsync("conn-1", $"overlay-{Broadcaster}", Arg.Any<CancellationToken>());
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
        string ticket = tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null));
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
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null))
        );
        await f.Hub.OnConnectedAsync();

        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();

        JoinWidgetResponse joinA = await f.Hub.JoinWidgetWithSdk(widgetA.ToString(), "test");
        JoinWidgetResponse joinB = await f.Hub.JoinWidgetWithSdk(widgetB.ToString(), "test");

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
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null)),
            connectionId: "multi-widget-conn"
        );
        await f.Hub.OnConnectedAsync();
        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();
        Guid widgetC = Guid.NewGuid(); // never joined — must never be touched
        await f.Hub.JoinWidgetWithSdk(widgetA.ToString(), "test");
        await f.Hub.JoinWidgetWithSdk(widgetB.ToString(), "test");

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
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null)),
            connectionId: "leave-one-conn"
        );
        await f.Hub.OnConnectedAsync();
        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();
        await f.Hub.JoinWidgetWithSdk(widgetA.ToString(), "test");
        await f.Hub.JoinWidgetWithSdk(widgetB.ToString(), "test");

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

    // ── Audit S-OVERLAY-1: a widget-scoped token must never unlock another widget ──────────────────

    [Fact]
    public async Task A_widget_scoped_connection_can_join_its_own_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid widgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
        );
        await f.Hub.OnConnectedAsync();

        JoinWidgetResponse join = await f.Hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        join.Success.Should().BeTrue();
        await f
            .Groups.Received(1)
            .AddToGroupAsync(
                "obs-conn",
                $"widget-{Broadcaster}-{widgetId}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_scoped_connection_cannot_join_a_different_widget()
    {
        // The actual bug this slice fixes: one widget's leaked overlay token used to unlock every widget's
        // manifest AND hub group on the same channel. Prove the connection refuses to join a widget id other
        // than the one its ticket was scoped to, and joins NO group for the attempt.
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid ownWidgetId = Guid.NewGuid();
        Guid otherWidgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, ownWidgetId))
        );
        await f.Hub.OnConnectedAsync();

        JoinWidgetResponse join = await f.Hub.JoinWidgetWithSdk(otherWidgetId.ToString(), "test");

        join.Success.Should().BeFalse();
        join.Error.Should().NotBeNullOrWhiteSpace();
        await f
            .Groups.DidNotReceive()
            .AddToGroupAsync(
                "obs-conn",
                $"widget-{Broadcaster}-{otherWidgetId}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_channel_scoped_connection_may_join_any_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, null))
        );
        await f.Hub.OnConnectedAsync();

        Guid widgetA = Guid.NewGuid();
        Guid widgetB = Guid.NewGuid();

        JoinWidgetResponse joinA = await f.Hub.JoinWidgetWithSdk(widgetA.ToString(), "test");
        JoinWidgetResponse joinB = await f.Hub.JoinWidgetWithSdk(widgetB.ToString(), "test");

        joinA.Success.Should().BeTrue();
        joinB.Success.Should().BeTrue();
    }

    // ── widget-sdk.md §8: actions run only for the widget the ticket names ───────────────────────

    private static IWidgetActionService ActionsReturning(WidgetActionOutcome outcome)
    {
        IWidgetActionService actions = Substitute.For<IWidgetActionService>();
        actions
            .InvokeAsync(Arg.Any<WidgetActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(outcome));
        return actions;
    }

    [Fact]
    public async Task A_widget_scoped_connection_runs_an_action_for_its_own_widget_and_returns_the_outputs()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid widgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = ActionsReturning(
            new(true, "paused", null, new Dictionary<string, string> { ["tts.durationMs"] = "900" })
        );
        Dictionary<string, JsonElement> parameters = new()
        {
            ["text"] = JsonSerializer.SerializeToElement("hello"),
        };
        Dictionary<string, string> variables = new() { ["redemption.id"] = "r-1" };

        WidgetActionResponse response = await f.Hub.InvokeAction(
            widgetId.ToString(),
            "song_pause",
            parameters,
            variables,
            actions
        );

        response.Success.Should().BeTrue();
        response.Output.Should().Be("paused");
        response.ErrorCode.Should().BeNull();
        response.Variables["tts.durationMs"].Should().Be("900");
        await actions
            .Received(1)
            .InvokeAsync(
                Arg.Is<WidgetActionRequest>(r =>
                    r.BroadcasterId == Broadcaster
                    && r.WidgetId == widgetId
                    && r.ActionType == "song_pause"
                    && r.Parameters == parameters
                    && r.Variables == variables
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_scoped_connection_cannot_run_an_action_for_a_different_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, Guid.NewGuid()))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = ActionsReturning(
            new(true, null, null, new Dictionary<string, string>())
        );

        WidgetActionResponse response = await f.Hub.InvokeAction(
            Guid.NewGuid().ToString(),
            "send_message",
            null,
            null,
            actions
        );

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("FORBIDDEN");
        await actions
            .DidNotReceive()
            .InvokeAsync(Arg.Any<WidgetActionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_refused_action_carries_the_service_reason_and_code_back_to_the_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid widgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = Substitute.For<IWidgetActionService>();
        actions
            .InvokeAsync(Arg.Any<WidgetActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<WidgetActionOutcome>("This widget is turned off.", "FORBIDDEN")
            );

        WidgetActionResponse response = await f.Hub.InvokeAction(
            widgetId.ToString(),
            "song_pause",
            null,
            null,
            actions
        );

        response.Success.Should().BeFalse();
        response.Error.Should().Be("This widget is turned off.");
        response.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task A_widget_scoped_connection_claims_a_key_for_its_own_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid widgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = Substitute.For<IWidgetActionService>();
        actions
            .ClaimAsync(Broadcaster, widgetId, "redemption:r-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success(true), Result.Success(false));

        bool first = await f.Hub.ClaimOnce(widgetId.ToString(), "redemption:r-1", actions);
        bool second = await f.Hub.ClaimOnce(widgetId.ToString(), "redemption:r-1", actions);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task A_widget_scoped_connection_cannot_claim_for_a_different_widget()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, Guid.NewGuid()))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = Substitute.For<IWidgetActionService>();

        Func<Task> claim = () =>
            f.Hub.ClaimOnce(Guid.NewGuid().ToString(), "redemption:r-1", actions);

        await claim.Should().ThrowAsync<HubException>().WithMessage("*different widget*");
        await actions
            .DidNotReceive()
            .ClaimAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_refused_claim_rejects_with_the_service_reason()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        OverlayTicketService tickets = new(new FakeTimeProvider());
        Guid widgetId = Guid.NewGuid();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
        );
        await f.Hub.OnConnectedAsync();
        IWidgetActionService actions = Substitute.For<IWidgetActionService>();
        actions
            .ClaimAsync(Broadcaster, widgetId, "", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<bool>("A claim key is 1 to 200 characters.", "VALIDATION_FAILED")
            );

        Func<Task> claim = () => f.Hub.ClaimOnce(widgetId.ToString(), "", actions);

        await claim.Should().ThrowAsync<HubException>().WithMessage("A claim key is 1 to 200*");
    }

    // ── Subscribe: the page tells the hub which events its code listens to ──────────────

    private sealed record SubscribeFixture(
        Fixture Hub,
        Widget Widget,
        IChannelRegistry Registry,
        IEventBus Bus,
        int[] Saves
    );

    private static async Task<SubscribeFixture> SubscribeFixtureAsync(
        WidgetTestDbContext db,
        List<string> stored,
        Guid? ticketWidget = null,
        string? galleryKey = null,
        string trustTier = "first_party"
    )
    {
        Widget widget = new()
        {
            BroadcasterId = Broadcaster,
            Name = "Bot Audio",
            EventSubscriptions = stored,
        };
        if (galleryKey is not null)
        {
            WidgetGalleryItem item = new()
            {
                Name = galleryKey,
                NaturalKey = galleryKey,
                TrustTier = trustTier,
            };
            db.WidgetGalleryItems.Add(item);
            widget.GalleryItemId = item.Id;
        }
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        int[] saves = [0];
        db.SavedChanges += (_, _) => saves[0]++;

        OverlayTicketService tickets = new(new FakeTimeProvider());
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        IEventBus bus = Substitute.For<IEventBus>();
        Fixture f = Build(
            db,
            tickets,
            ticket: tickets.IssueTicket(
                new OverlayTokenScope(Broadcaster, ticketWidget ?? widget.Id)
            ),
            registry: registry,
            bus: bus
        );
        await f.Hub.OnConnectedAsync();
        return new(f, widget, registry, bus, saves);
    }

    private static async Task<List<string>> StoredAsync(WidgetTestDbContext db, Guid widgetId)
    {
        db.ChangeTracker.Clear();
        return (
            await db.Widgets.AsNoTracking().SingleAsync(w => w.Id == widgetId)
        ).EventSubscriptions;
    }

    [Fact]
    public async Task Subscribing_only_tts_speak_replaces_a_custom_widgets_list_and_releases_music_demand()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            ["tts_speak", "now_playing", "youtube.play"]
        );

        bool accepted = await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["tts_speak"]);

        accepted.Should().BeTrue();
        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak");
        d.Registry.Received(1).ReleaseMusicDemand(Broadcaster, "obs-conn");
        d.Registry.DidNotReceive().TouchMusicDemand(Arg.Any<Guid>(), Arg.Any<string>());
        await d
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<ChannelConfigChangedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.Domain == "widgets"
                    && e.EntityId == d.Widget.Id.ToString()
                    && e.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Subscribing_to_now_playing_touches_music_demand()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, ["tts_speak"]);

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["tts_speak", "now_playing"]);

        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak", "now_playing");
        d.Registry.Received(1).TouchMusicDemand(Broadcaster, "obs-conn");
    }

    [Fact]
    public async Task A_widget_scoped_connection_subscribing_for_another_widget_changes_nothing()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            ["tts_speak", "now_playing"],
            ticketWidget: Guid.NewGuid()
        );

        bool accepted = await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["follow"]);

        accepted.Should().BeFalse();
        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak", "now_playing");
        d.Saves[0].Should().Be(0);
        d.Registry.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("follow", "has space")]
    [InlineData("follow", "")]
    [InlineData("follow", "semi;colon")]
    [InlineData("follow", "star*")]
    public async Task An_invalid_name_refuses_the_whole_set_and_changes_nothing(
        string good,
        string bad
    )
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, ["tts_speak"]);

        bool accepted = await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), [good, bad]);

        accepted.Should().BeFalse();
        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak");
        d.Saves[0].Should().Be(0);
    }

    [Fact]
    public async Task An_oversized_set_is_refused_whole()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, ["tts_speak"]);
        string[] tooMany = Enumerable
            .Range(0, WidgetEventDeclaration.MaxEvents + 1)
            .Select(i => $"event_{i}")
            .ToArray();

        bool accepted = await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), tooMany);

        accepted.Should().BeFalse();
        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak");
    }

    [Fact]
    public async Task An_identical_set_is_not_saved_and_the_wildcard_is_stored_as_given()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, ["tts_speak", "follow"]);

        bool same = await d.Hub.Hub.Subscribe(
            d.Widget.Id.ToString(),
            ["follow", "tts_speak", "follow"]
        );

        same.Should().BeTrue();
        d.Saves[0].Should().Be(0);
        await d
            .Bus.DidNotReceive()
            .PublishAsync(Arg.Any<ChannelConfigChangedEvent>(), Arg.Any<CancellationToken>());

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["*"]);

        (await StoredAsync(db, d.Widget.Id)).Should().Equal("*");
        d.Saves[0].Should().Be(1);
        d.Registry.Received().TouchMusicDemand(Broadcaster, "obs-conn");
    }

    // The Alerts template binds its events through a variable (NomNomz.on(e, t) in a loop), which the
    // save-time scan cannot see: its stored list starts empty and the page's declaration fills it in.
    [Fact]
    public async Task After_subscribing_to_follow_a_follow_alert_reaches_the_widget_and_a_raid_does_not()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, []);

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["follow"]);

        List<Widget> widgets = await db.Widgets.AsNoTracking().ToListAsync();
        WidgetAlertRouting
            .Subscribers(widgets, "follow")
            .Select(w => w.Id)
            .Should()
            .Equal(d.Widget.Id);
        WidgetAlertRouting.Subscribers(widgets, "raid").Should().BeEmpty();
    }

    [Fact]
    public async Task A_widget_that_subscribed_to_the_wildcard_receives_every_alert()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(db, []);

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["*"]);

        List<Widget> widgets = await db.Widgets.AsNoTracking().ToListAsync();
        WidgetAlertRouting
            .Subscribers(widgets, "follow")
            .Select(w => w.Id)
            .Should()
            .Equal(d.Widget.Id);
        WidgetAlertRouting
            .Subscribers(widgets, "raid")
            .Select(w => w.Id)
            .Should()
            .Equal(d.Widget.Id);
    }

    // The server acts on a Redemption Alert's reward_redeemed (the redemption sound) whether or not the page
    // registers it, so the catalogue default stays; what the page adds is joined on; the old list is not kept.
    [Fact]
    public async Task A_redemption_alert_subscribing_to_follow_keeps_reward_redeemed_and_gains_follow()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            ["reward_redeemed", "stale_event"],
            galleryKey: "redemption_alert"
        );

        bool accepted = await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["follow"]);

        accepted.Should().BeTrue();
        (await StoredAsync(db, d.Widget.Id)).Should().Equal("reward_redeemed", "follow");
        d.Saves[0].Should().Be(1);
    }

    // Production audio installs still listed now_playing and youtube.* that their code never uses; the
    // catalogue default for audio is tts_speak alone, so a page that subscribes to it stores exactly that.
    [Theory]
    [InlineData("audio")]
    [InlineData("tts_audio")]
    public async Task An_audio_install_subscribing_to_tts_speak_stores_exactly_tts_speak(
        string galleryKey
    )
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            ["tts_speak", "now_playing", "youtube.play", "youtube.pause"],
            galleryKey: galleryKey
        );

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["tts_speak"]);

        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak");
        d.Registry.Received(1).ReleaseMusicDemand(Broadcaster, "obs-conn");
    }

    // The server speaks TTS to an audio install whether or not its page registers tts_speak, so the catalogue
    // default stays when the page subscribes to something else.
    [Theory]
    [InlineData("audio")]
    [InlineData("tts_audio")]
    public async Task An_audio_install_subscribing_to_follow_keeps_tts_speak_and_gains_follow(
        string galleryKey
    )
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            ["now_playing"],
            galleryKey: galleryKey
        );

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["follow"]);

        (await StoredAsync(db, d.Widget.Id)).Should().Equal("tts_speak", "follow");
    }

    [Fact]
    public async Task A_gallery_row_that_is_not_first_party_gets_no_catalogue_default()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        SubscribeFixture d = await SubscribeFixtureAsync(
            db,
            [],
            galleryKey: "redemption_alert",
            trustTier: "unverified"
        );

        await d.Hub.Hub.Subscribe(d.Widget.Id.ToString(), ["follow"]);

        (await StoredAsync(db, d.Widget.Id)).Should().Equal("follow");
    }
}
