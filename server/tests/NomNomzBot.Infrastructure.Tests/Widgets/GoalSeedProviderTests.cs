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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// After an OBS reload the goal bar must show the real Twitch goal value, not its start value. The seed provider
/// reads the creator goal from Twitch and hands the widget one <c>goal</c> frame in the exact shape the live
/// handler sends. A failed call or a goal of another metric gives no frame, never a made-up 0.
/// </summary>
public sealed class GoalSeedProviderTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000d2"
    );
    private static readonly DateTime GoalStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ImportedAt = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    private readonly ITwitchGoalsApi _goals = Substitute.For<ITwitchGoalsApi>();
    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly WidgetTestDbContext _ctx;

    public GoalSeedProviderTests() => _ctx = _database.NewContext();

    public void Dispose()
    {
        _ctx.Dispose();
        _database.Dispose();
    }

    private static ChannelEvent Event(Guid channelId, string type, string dataJson, string id) =>
        new()
        {
            Id = id,
            ChannelId = channelId,
            Type = type,
            Data = dataJson,
            CreatedAt = ImportedAt,
        };

    private static ChannelEvent Cheer(Guid channelId, int bits, DateTime occurredAt, string id) =>
        Event(
            channelId,
            "channel.cheer",
            $"{{\"bits\":{bits},\"occurredAt\":\"{occurredAt:O}\"}}",
            id
        );

    private static TwitchCreatorGoal Goal(string type, int current, int target) =>
        new("g1", "1", "Streamer", "streamer", type, "d", current, target, DateTimeOffset.UtcNow);

    private static Widget NewWidget(string? metric)
    {
        Widget widget = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Broadcaster,
            Name = "goal bar",
        };
        if (metric is not null)
            widget.Settings["metric"] = metric;
        return widget;
    }

    private void GoalsReturn(params TwitchCreatorGoal[] goals) =>
        _goals
            .GetCreatorGoalsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchCreatorGoal>>(goals));

    [Fact]
    public async Task A_follower_goal_seeds_one_goal_frame_with_the_real_value()
    {
        GoalsReturn(Goal("follower", 4, 100));
        GoalSeedProvider provider = new(_goals, _ctx);

        IReadOnlyList<WidgetSeedFrame> frames = await provider.SeedAsync(
            Broadcaster,
            NewWidget("followers"),
            CancellationToken.None
        );

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("goal");
        frames[0].Data.Should().BeEquivalentTo(new GoalWidgetEventPayload("followers", 4, 100));
        provider.NaturalKey.Should().Be("goal_bar");
    }

    [Fact]
    public async Task A_bits_goal_sums_this_channels_stored_cheers_from_the_goal_start()
    {
        _ctx.ChannelEvents.AddRange(
            Cheer(Broadcaster, 100, GoalStart.AddDays(-1), "c-before"),
            Cheer(Broadcaster, 10, GoalStart, "c-at-start"),
            Cheer(Broadcaster, 50, GoalStart.AddDays(1), "c-after-1"),
            Cheer(Broadcaster, 25, GoalStart.AddDays(4), "c-after-2"),
            Cheer(OtherBroadcaster, 999, GoalStart.AddDays(2), "c-other-channel"),
            Event(
                Broadcaster,
                "channel.subscribe",
                $"{{\"bits\":7,\"occurredAt\":\"{GoalStart.AddDays(2):O}\"}}",
                "e-not-a-cheer"
            )
        );
        await _ctx.SaveChangesAsync();
        Widget widget = NewWidget("bits");
        widget.Settings["target"] = 500;
        widget.Settings["startDate"] = GoalStart.ToString("O");

        IReadOnlyList<WidgetSeedFrame> frames = await new GoalSeedProvider(_goals, _ctx).SeedAsync(
            Broadcaster,
            widget,
            CancellationToken.None
        );

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("goal");
        frames[0].Data.Should().BeEquivalentTo(new GoalWidgetEventPayload("bits", 85, 500));
        await _goals
            .DidNotReceiveWithAnyArgs()
            .GetCreatorGoalsAsync(default, CancellationToken.None);
    }

    [Fact]
    public async Task A_bits_goal_without_a_start_date_gives_no_frame_never_a_guessed_total()
    {
        _ctx.ChannelEvents.Add(Cheer(Broadcaster, 50, GoalStart.AddDays(1), "c1"));
        await _ctx.SaveChangesAsync();
        Widget widget = NewWidget("bits");
        widget.Settings["target"] = 500;

        IReadOnlyList<WidgetSeedFrame> frames = await new GoalSeedProvider(_goals, _ctx).SeedAsync(
            Broadcaster,
            widget,
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task A_widget_without_a_metric_setting_reads_the_follower_goal()
    {
        GoalsReturn(Goal("follower", 4, 100));
        GoalSeedProvider provider = new(_goals, _ctx);

        IReadOnlyList<WidgetSeedFrame> frames = await provider.SeedAsync(
            Broadcaster,
            NewWidget(null),
            CancellationToken.None
        );

        frames.Should().ContainSingle();
    }

    [Fact]
    public async Task A_failed_goals_call_gives_no_frame_never_a_zero()
    {
        _goals
            .GetCreatorGoalsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchCreatorGoal>>(
                    "missing scope",
                    TwitchErrorCodes.MissingScope
                )
            );
        GoalSeedProvider provider = new(_goals, _ctx);

        IReadOnlyList<WidgetSeedFrame> frames = await provider.SeedAsync(
            Broadcaster,
            NewWidget("followers"),
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("subs")]
    [InlineData("bits")]
    public async Task A_goal_of_another_metric_gives_no_frame(string widgetMetric)
    {
        GoalsReturn(Goal("follower", 4, 100));
        GoalSeedProvider provider = new(_goals, _ctx);

        IReadOnlyList<WidgetSeedFrame> frames = await provider.SeedAsync(
            Broadcaster,
            NewWidget(widgetMetric),
            CancellationToken.None
        );

        frames.Should().BeEmpty();
    }

    [Theory]
    [InlineData("follower", "followers")]
    [InlineData("followers", "followers")]
    [InlineData("subscription", "subs")]
    [InlineData("subscription_count", "subs")]
    [InlineData("new_subscription", "subs")]
    [InlineData("new_subscription_count", "subs")]
    [InlineData("some_future_twitch_goal_type", null)]
    public async Task The_live_handler_and_the_seed_map_a_twitch_goal_type_to_the_same_metric(
        string twitchGoalType,
        string? expectedMetric
    )
    {
        // Live: what the handler pushes for this goal type.
        IWidgetEventNotifier overlay = Substitute.For<IWidgetEventNotifier>();
        using WidgetSqliteTestDatabase db = WidgetSqliteTestDatabase.Open();
        Guid channelId = Broadcaster;
        Widget subscribed = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channelId,
            Name = "goal bar",
            IsEnabled = true,
            EventSubscriptions = ["goal"],
        };
        await using (WidgetTestDbContext ctx = db.NewContext())
        {
            ctx.Channels.Add(
                new()
                {
                    Id = channelId,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = "d1d1d1d1d1d1",
                    Name = "teststreamer",
                    NameNormalized = "teststreamer",
                    OverlayToken = channelId.ToString("N"),
                }
            );
            ctx.Widgets.Add(subscribed);
            await ctx.SaveChangesAsync();
        }
        await using WidgetTestDbContext readCtx = db.NewContext();
        await new GoalWidgetEventHandler(readCtx, overlay).HandleAsync(
            new GoalProgressEvent
            {
                BroadcasterId = channelId,
                OccurredAt = DateTimeOffset.UtcNow,
                GoalId = "g1",
                Type = twitchGoalType,
                Description = "d",
                CurrentAmount = 5,
                TargetAmount = 10,
                StartedAt = DateTimeOffset.UtcNow,
            }
        );

        string? liveMetric = overlay
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWidgetEventNotifier.SendWidgetEventAsync))
            .Select(c => (c.GetArguments()[3] as GoalWidgetEventPayload)?.Metric)
            .SingleOrDefault();

        // Seed: a widget set to the metric the live handler used.
        string? seedMetric = null;
        if (expectedMetric is not null)
        {
            GoalsReturn(Goal(twitchGoalType, 5, 10));
            IReadOnlyList<WidgetSeedFrame> frames = await new GoalSeedProvider(
                _goals,
                _ctx
            ).SeedAsync(Broadcaster, NewWidget(expectedMetric), CancellationToken.None);
            seedMetric = (frames.SingleOrDefault()?.Data as GoalWidgetEventPayload)?.Metric;
        }

        liveMetric.Should().Be(expectedMetric);
        seedMetric.Should().Be(expectedMetric);
    }
}
