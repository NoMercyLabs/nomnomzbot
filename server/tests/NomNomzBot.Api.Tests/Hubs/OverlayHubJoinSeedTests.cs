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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The join answer carries the frames the widget's seed provider rebuilt, found by the widget's gallery natural
/// key, so a widget that lost its page state gets it back on every join.
/// </summary>
public sealed class OverlayHubJoinSeedTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f23");
    private static readonly DateTimeOffset First = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Join_returns_the_seed_frames_of_the_provider_for_the_widgets_natural_key_in_order()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid widgetId = AddWidget(db, "goal_bar");
        await db.SaveChangesAsync();
        FakeProvider goalBar = new(
            "goal_bar",
            new WidgetSeedFrame("goal_progress", new { current = 3 }, First),
            new WidgetSeedFrame("goal_progress", new { current = 5 }, First.AddMinutes(1))
        );
        FakeProvider other = new("labels", new WidgetSeedFrame("labels", new { }, First));

        JoinWidgetResponse join = await JoinAsync(db, widgetId, [other, goalBar]);

        join.Success.Should().BeTrue();
        join.Seed.Should().NotBeNull();
        join.Seed!.Select(f => f.EventType).Should().Equal("goal_progress", "goal_progress");
        join.Seed!.Select(f => f.OccurredAt).Should().Equal(First, First.AddMinutes(1));
        goalBar.Calls.Should().Be(1);
        goalBar.SeenBroadcaster.Should().Be(Broadcaster);
        goalBar.SeenWidget!.Id.Should().Be(widgetId);
        other.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Join_returns_an_empty_seed_when_no_provider_matches_the_widgets_natural_key()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid widgetId = AddWidget(db, "heist");
        await db.SaveChangesAsync();
        FakeProvider goalBar = new(
            "goal_bar",
            new WidgetSeedFrame("goal_progress", new { current = 3 }, First)
        );

        JoinWidgetResponse join = await JoinAsync(db, widgetId, [goalBar]);

        join.Success.Should().BeTrue();
        join.Seed.Should().NotBeNull().And.BeEmpty();
        goalBar.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_provider_that_throws_gives_an_empty_seed_and_the_join_still_succeeds()
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid widgetId = AddWidget(db, "goal_bar");
        await db.SaveChangesAsync();
        FakeProvider broken = new("goal_bar") { Throws = true };

        JoinWidgetResponse join = await JoinAsync(db, widgetId, [broken]);

        join.Success.Should().BeTrue();
        join.Seed.Should().NotBeNull().And.BeEmpty();
    }

    private static Guid AddWidget(WidgetTestDbContext db, string naturalKey)
    {
        WidgetGalleryItem item = new() { Name = naturalKey, NaturalKey = naturalKey };
        Guid widgetId = Guid.CreateVersion7();
        db.WidgetGalleryItems.Add(item);
        db.Widgets.Add(
            new()
            {
                Id = widgetId,
                BroadcasterId = Broadcaster,
                Name = naturalKey,
                IsEnabled = true,
                GalleryItemId = item.Id,
                Settings = new(),
            }
        );
        return widgetId;
    }

    private static async Task<JoinWidgetResponse> JoinAsync(
        WidgetTestDbContext db,
        Guid widgetId,
        IEnumerable<IWidgetSeedProvider> providers
    )
    {
        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .GetEffectiveSettingsAsync(Broadcaster, widgetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Dictionary<string, object>()));

        OverlayTicketService tickets = new(new FakeTimeProvider());
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("seed-conn");
        DefaultHttpContext http = new();
        http.Request.QueryString = new(
            "?ticket="
                + Uri.EscapeDataString(
                    tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
                )
        );
        context.GetHttpContext().Returns(http);
        OverlayHub hub = new(
            db,
            widgetService,
            tickets,
            new(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            providers,
            NullLogger<OverlayHub>.Instance
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
        };
        await hub.OnConnectedAsync();
        return await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");
    }

    private sealed class FakeProvider(string naturalKey, params WidgetSeedFrame[] frames)
        : IWidgetSeedProvider
    {
        public string NaturalKey => naturalKey;
        public bool Throws { get; init; }
        public int Calls { get; private set; }
        public Guid SeenBroadcaster { get; private set; }
        public Widget? SeenWidget { get; private set; }

        public Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
            Guid broadcasterId,
            Widget widget,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            SeenBroadcaster = broadcasterId;
            SeenWidget = widget;
            if (Throws)
                throw new InvalidOperationException("source down");
            return Task.FromResult<IReadOnlyList<WidgetSeedFrame>>(frames);
        }
    }
}
