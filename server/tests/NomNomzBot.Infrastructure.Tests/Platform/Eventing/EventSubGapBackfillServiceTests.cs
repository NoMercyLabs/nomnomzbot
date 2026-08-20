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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Proves the reconnect gap backfill (twitch-eventsub §7): only redemptions/follows whose Twitch timestamp
/// falls inside <c>[gapStart, gapEnd]</c> are republished, an item already in the journal (by its deterministic
/// id) is never republished a second time, and each source's own read failure never blocks the other source.
/// </summary>
public sealed class EventSubGapBackfillServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly DateTimeOffset GapStart = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GapEnd = new(2026, 6, 20, 10, 30, 0, TimeSpan.Zero);

    private static (
        EventSubGapBackfillService Sut,
        ITwitchChannelPointsApi Points,
        ITwitchChannelsApi Channels,
        IEventJournal Journal,
        RecordingEventBus Bus
    ) Build()
    {
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetExistingEventIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlySet<Guid>>(new HashSet<Guid>()));
        RecordingEventBus bus = new();

        points
            .GetCustomRewardsAsync(
                Channel,
                onlyManageableRewards: true,
                ct: Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        channels
            .GetChannelFollowersAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchPage<TwitchChannelFollower>([], null, 0)));

        return (
            new EventSubGapBackfillService(
                points,
                channels,
                journal,
                bus,
                NullLogger<EventSubGapBackfillService>.Instance
            ),
            points,
            channels,
            journal,
            bus
        );
    }

    private static TwitchChannelFollower Follower(string userId, DateTimeOffset at) =>
        new(userId, "login" + userId, "Name" + userId, at);

    [Fact]
    public async Task BackfillGapAsync_FollowInsideWindow_IsPublished()
    {
        (EventSubGapBackfillService sut, _, ITwitchChannelsApi channels, _, RecordingEventBus bus) =
            Build();
        channels
            .GetChannelFollowersAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchChannelFollower>(
                        [Follower("u1", GapStart.AddMinutes(5))],
                        null,
                        1
                    )
                )
            );

        Result<int> result = await sut.BackfillGapAsync(Channel, GapStart, GapEnd);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(1);
        bus.Published.Should().ContainSingle();
        bus.Published[0].Should().BeOfType<FollowEvent>();
    }

    [Fact]
    public async Task BackfillGapAsync_FollowBeforeGapStart_IsExcluded()
    {
        (EventSubGapBackfillService sut, _, ITwitchChannelsApi channels, _, RecordingEventBus bus) =
            Build();
        channels
            .GetChannelFollowersAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchChannelFollower>(
                        [Follower("old", GapStart.AddHours(-2))],
                        null,
                        1
                    )
                )
            );

        Result<int> result = await sut.BackfillGapAsync(Channel, GapStart, GapEnd);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(0);
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task BackfillGapAsync_RedemptionAlreadyInJournal_IsNeverRepublished()
    {
        (
            EventSubGapBackfillService sut,
            ITwitchChannelPointsApi points,
            _,
            IEventJournal journal,
            RecordingEventBus bus
        ) = Build();

        TwitchCustomReward reward = new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "reward-1",
            Title: "BSOD",
            Prompt: "",
            Cost: 7500,
            Image: null,
            DefaultImage: new TwitchCustomRewardImage("1x", "2x", "4x"),
            BackgroundColor: "#000",
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new TwitchCustomRewardMaxPerStreamSetting(false, 0),
            MaxPerUserPerStreamSetting: new TwitchCustomRewardMaxPerUserPerStreamSetting(false, 0),
            GlobalCooldownSetting: new TwitchCustomRewardGlobalCooldownSetting(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: null,
            CooldownExpiresAt: null
        );
        points
            .GetCustomRewardsAsync(
                Channel,
                onlyManageableRewards: true,
                ct: Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([reward]));

        TwitchCustomRewardRedemption redemption = new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "redemption-1",
            UserId: "u1",
            UserName: "Viewer1",
            UserLogin: "viewer1",
            Reward: new TwitchRedemptionReward("reward-1", "BSOD", "", 7500),
            UserInput: "",
            Status: "UNFULFILLED",
            RedeemedAt: GapStart.AddMinutes(5)
        );
        points
            .GetCustomRewardRedemptionsAsync(
                Channel,
                "reward-1",
                Arg.Any<string?>(),
                null,
                "NEWEST",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchPage<TwitchCustomRewardRedemption>([redemption], null, 1))
            );

        // Every candidate this sweep would produce is already journaled — nothing new to replay.
        journal
            .GetExistingEventIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                Result.Success((IReadOnlySet<Guid>)ci.Arg<IReadOnlyCollection<Guid>>().ToHashSet())
            );

        Result<int> result = await sut.BackfillGapAsync(Channel, GapStart, GapEnd);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(0);
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task BackfillGapAsync_FollowerReadFails_RedemptionSweepStillRuns()
    {
        (
            EventSubGapBackfillService sut,
            ITwitchChannelPointsApi points,
            ITwitchChannelsApi channels,
            _,
            RecordingEventBus bus
        ) = Build();

        channels
            .GetChannelFollowersAsync(
                Channel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchChannelFollower>>("no scope", "FORBIDDEN"));

        TwitchCustomReward reward = new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "reward-1",
            Title: "BSOD",
            Prompt: "",
            Cost: 7500,
            Image: null,
            DefaultImage: new TwitchCustomRewardImage("1x", "2x", "4x"),
            BackgroundColor: "#000",
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new TwitchCustomRewardMaxPerStreamSetting(false, 0),
            MaxPerUserPerStreamSetting: new TwitchCustomRewardMaxPerUserPerStreamSetting(false, 0),
            GlobalCooldownSetting: new TwitchCustomRewardGlobalCooldownSetting(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: null,
            CooldownExpiresAt: null
        );
        points
            .GetCustomRewardsAsync(
                Channel,
                onlyManageableRewards: true,
                ct: Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([reward]));

        TwitchCustomRewardRedemption redemption = new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "redemption-1",
            UserId: "u1",
            UserName: "Viewer1",
            UserLogin: "viewer1",
            Reward: new TwitchRedemptionReward("reward-1", "BSOD", "", 7500),
            UserInput: "",
            Status: "UNFULFILLED",
            RedeemedAt: GapStart.AddMinutes(5)
        );
        points
            .GetCustomRewardRedemptionsAsync(
                Channel,
                "reward-1",
                Arg.Any<string?>(),
                null,
                "NEWEST",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchPage<TwitchCustomRewardRedemption>([redemption], null, 1))
            );

        Result<int> result = await sut.BackfillGapAsync(Channel, GapStart, GapEnd);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be(1); // redemption sweep unaffected by the follower read failure
        bus.Published.Should().ContainSingle();
        bus.Published[0].Should().BeOfType<RewardRedeemedEvent>();
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }
}
