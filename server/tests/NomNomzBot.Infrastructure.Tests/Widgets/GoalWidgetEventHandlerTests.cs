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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the goal-event → overlay routing gap the widget-quality audit (§1) found is closed: a creator-goal
/// domain event reaches every enabled widget subscribed to the <c>goal</c> event type, over the real
/// <see cref="IWidgetEventNotifier"/> seam — with the exact method, widget id, event type, and payload shape
/// <c>goal_bar.vue</c>/<c>labels.vue</c> read (<c>{ metric, value, target }</c>), not merely "the notifier was
/// called".
/// </summary>
public sealed class GoalWidgetEventHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000c2"
    );

    private readonly IWidgetEventNotifier _overlay = Substitute.For<IWidgetEventNotifier>();

    private static Widget NewWidget(
        Guid broadcasterId,
        bool enabled,
        params string[] subscriptions
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            Name = "test-widget",
            IsEnabled = enabled,
            EventSubscriptions = [.. subscriptions],
        };

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = channelId.ToString("N")[..12],
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = channelId.ToString("N"),
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Goal_began_event_pushes_the_authoritative_value_to_every_subscribed_widget()
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        await SeedChannelAsync(db, OtherBroadcaster);
        Widget goalBar = NewWidget(Broadcaster, true, "goal");
        Widget label = NewWidget(Broadcaster, true, "goal", "follow");
        Widget notSubscribed = NewWidget(Broadcaster, true, "follow");
        Widget disabled = NewWidget(Broadcaster, false, "goal");
        Widget otherChannel = NewWidget(OtherBroadcaster, true, "goal");

        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.AddRange(goalBar, label, notSubscribed, disabled, otherChannel);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        GoalWidgetEventHandler handler = new(readCtx, _overlay);

        GoalBeganEvent began = new()
        {
            BroadcasterId = Broadcaster,
            OccurredAt = DateTimeOffset.UtcNow,
            GoalId = "g1",
            Type = "follower",
            Description = "100 followers",
            CurrentAmount = 37,
            TargetAmount = 100,
            StartedAt = DateTimeOffset.UtcNow,
        };

        await handler.HandleAsync(began);

        // Exactly the two enabled, subscribed widgets on the right channel — not the unsubscribed widget, the
        // disabled widget, or the other channel's widget.
        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                goalBar.Id,
                "goal",
                Arg.Is<object?>(p => IsExpectedPayload(p, "followers", 37, 100)),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                label.Id,
                "goal",
                Arg.Is<object?>(p => IsExpectedPayload(p, "followers", 37, 100)),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Broadcaster,
                notSubscribed.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Broadcaster,
                disabled.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                OtherBroadcaster,
                otherChannel.Id,
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("subscription", "subs")]
    [InlineData("subscription_count", "subs")]
    [InlineData("new_subscription", "subs")]
    [InlineData("new_subscription_count", "subs")]
    [InlineData("follower", "followers")]
    public async Task Goal_progress_event_maps_the_twitch_goal_type_to_the_widget_metric_vocabulary(
        string twitchGoalType,
        string expectedMetric
    )
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        Widget goalBar = NewWidget(Broadcaster, true, "goal");
        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.Add(goalBar);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        GoalWidgetEventHandler handler = new(readCtx, _overlay);

        await handler.HandleAsync(
            new GoalProgressEvent
            {
                BroadcasterId = Broadcaster,
                OccurredAt = DateTimeOffset.UtcNow,
                GoalId = "g1",
                Type = twitchGoalType,
                Description = "d",
                CurrentAmount = 5,
                TargetAmount = 10,
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                goalBar.Id,
                "goal",
                Arg.Is<object?>(p => IsExpectedPayload(p, expectedMetric, 5, 10)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unmapped_twitch_goal_type_never_pushes_a_guessed_metric()
    {
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(db, Broadcaster);
        Widget goalBar = NewWidget(Broadcaster, true, "goal");
        using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Widgets.Add(goalBar);
            await ctx.SaveChangesAsync();
        }

        using WidgetTestDbContext readCtx = db.NewContext();
        GoalWidgetEventHandler handler = new(readCtx, _overlay);

        await handler.HandleAsync(
            new GoalEndedEvent
            {
                BroadcasterId = Broadcaster,
                OccurredAt = DateTimeOffset.UtcNow,
                GoalId = "g1",
                Type = "some_future_twitch_goal_type",
                Description = "d",
                CurrentAmount = 5,
                TargetAmount = 10,
                IsAchieved = false,
                StartedAt = DateTimeOffset.UtcNow,
                EndedAt = DateTimeOffset.UtcNow,
            }
        );

        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static bool IsExpectedPayload(object? payload, string metric, int value, int target) =>
        payload is GoalWidgetEventPayload p
        && p.Metric == metric
        && p.Value == value
        && p.Target == target;
}
