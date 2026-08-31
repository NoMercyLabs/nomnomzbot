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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the OTHER direction of <c>SyncWithTwitchAsync</c>: pushing a bot-manageable LOCAL reward Twitch has
/// never seen (no <see cref="Reward.TwitchRewardId"/>) — the exact state a bundle-import-created reward is left
/// in on purpose (D2: never blocked by Helix being down at import time). Before this, nothing ever picked that
/// reward back up; sync only pulled FROM Twitch. This is what makes "sync pushes it to Twitch later" true.
/// </summary>
public sealed class RewardServiceSyncTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e301");

    private static (RewardService Sut, AuthDbContext Db, ITwitchChannelPointsApi Points) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000e300"),
                TwitchChannelId = "tw-channel",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();

        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: true,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        RewardService sut = new(db, points, NullLogger<RewardService>.Instance);
        return (sut, db, points);
    }

    private static TwitchCustomReward Confirmed(string id, string title, int cost) =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: id,
            Title: title,
            Prompt: "redeem me",
            Cost: cost,
            Image: null,
            DefaultImage: new("1x", "2x", "4x"),
            BackgroundColor: "#112233",
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new(false, 0),
            MaxPerUserPerStreamSetting: new(false, 0),
            GlobalCooldownSetting: new(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: 0,
            CooldownExpiresAt: null
        );

    [Fact]
    public async Task Sync_pushes_a_local_only_manageable_reward_to_twitch_and_records_the_confirmed_id()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        db.Rewards.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Title = "Hydrate!",
                Cost = 250,
                IsEnabled = true,
                IsManageable = true,
                IsPlatform = true,
                TwitchRewardId = null,
            }
        );
        await db.SaveChangesAsync();
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Confirmed("tw-reward-1", "Hydrate!", 250)));

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        Reward reward = db.Rewards.Single();
        reward.TwitchRewardId.Should().Be("tw-reward-1");
        reward.BackgroundColor.Should().Be("#112233");
        await points
            .Received(1)
            .CreateCustomRewardAsync(
                Channel,
                Arg.Is<CreateCustomRewardRequest>(r => r.Title == "Hydrate!" && r.Cost == 250),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Sync_leaves_a_local_only_reward_untouched_when_twitch_refuses_the_push_and_still_succeeds()
    {
        // The whole point of the local-only path (D2) is resilience: one reward Helix refuses must not fail
        // the rest of the sync, and must not be silently deleted or corrupted — it just stays local-only for
        // the next sync attempt to retry.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        db.Rewards.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Title = "Duplicate Title",
                Cost = 100,
                IsEnabled = true,
                IsManageable = true,
                IsPlatform = true,
                TwitchRewardId = null,
            }
        );
        await db.SaveChangesAsync();
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchCustomReward>("already exists", "ALREADY_EXISTS"));

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        Reward reward = db.Rewards.Single();
        reward.TwitchRewardId.Should().BeNull();
        reward.Title.Should().Be("Duplicate Title");
    }

    [Fact]
    public async Task Sync_does_not_push_a_reward_that_is_already_synced_or_not_bot_manageable()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        db.Rewards.AddRange(
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Title = "Already synced",
                Cost = 100,
                IsEnabled = true,
                IsManageable = true,
                IsPlatform = true,
                TwitchRewardId = "tw-existing",
            },
            new Reward
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Title = "External, read-only",
                Cost = 100,
                IsEnabled = true,
                IsManageable = false,
                IsPlatform = false,
                TwitchRewardId = null,
            }
        );
        await db.SaveChangesAsync();

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        await points
            .DidNotReceive()
            .CreateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            );
    }
}
