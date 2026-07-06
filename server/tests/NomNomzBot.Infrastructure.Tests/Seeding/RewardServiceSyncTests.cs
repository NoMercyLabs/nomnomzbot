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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Seeding;

/// <summary>
/// Proves <see cref="RewardService.SyncWithTwitchAsync"/> distinguishes the two outcomes the old code
/// conflated into a misleading success: a genuinely empty managed-reward set (the bot created none yet —
/// success, nothing seeded) versus a failed Helix read (no token / missing scope / Twitch error — surfaced
/// as a failure carrying the real error code, not a silent "no rewards"). It also proves a successful read
/// actually upserts the returned rewards into the local table the dashboard lists.
/// </summary>
public sealed class RewardServiceSyncTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d001");

    private static (RewardService Sut, AuthDbContext Db, ITwitchChannelPointsApi Points) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000d000"),
                TwitchChannelId = "tw-channel",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();

        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        RewardService sut = new(db, points, NullLogger<RewardService>.Instance);
        return (sut, db, points);
    }

    private static TwitchCustomReward Reward(
        string id,
        string title,
        int cost,
        bool enabled,
        bool manageable = true
    ) =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: id,
            Title: title,
            Prompt: "redeem me",
            Cost: cost,
            Image: null,
            DefaultImage: new TwitchCustomRewardImage("1x", "2x", "4x"),
            BackgroundColor: "#000000",
            IsEnabled: enabled,
            IsManageable: manageable,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new TwitchCustomRewardMaxPerStreamSetting(false, 0),
            MaxPerUserPerStreamSetting: new TwitchCustomRewardMaxPerUserPerStreamSetting(false, 0),
            GlobalCooldownSetting: new TwitchCustomRewardGlobalCooldownSetting(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: 0,
            CooldownExpiresAt: null
        );

    [Fact]
    public async Task Sync_surfaces_the_helix_failure_instead_of_reporting_no_rewards()
    {
        // The real bug: a failed read (e.g. token rejected / missing scope) was swallowed into an empty list
        // and reported as Result.Success — the dashboard then shows "0 rewards" for a channel whose reward
        // read actually FAILED. The sync must now propagate the failure with its real error code so the
        // onboarding seed handler logs a warning and the cause is visible.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<IReadOnlyList<TwitchCustomReward>>(
                    "Twitch rejected the token.",
                    TwitchErrorCodes.Unauthorized
                )
            );

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        (await db.Rewards.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_succeeds_with_nothing_seeded_when_the_bot_manages_no_rewards()
    {
        // A genuinely empty managed-reward set (Twitch 200 with data:[]) is NOT a failure — the bot has
        // created no managed rewards and unmanaged ones surface at redemption time, not via sync.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        (await db.Rewards.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_upserts_the_rewards_twitch_returns_into_the_local_table()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchCustomReward>>([
                    Reward("tw-reward-1", "First Light", 500, enabled: true),
                    Reward("tw-reward-2", "Hydrate", 100, enabled: false),
                ])
            );

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();

        List<Reward> rewards = await db
            .Rewards.Where(r => r.BroadcasterId == Channel)
            .OrderBy(r => r.Title)
            .ToListAsync();
        rewards.Should().HaveCount(2);

        Reward first = rewards.Single(r => r.TwitchRewardId == "tw-reward-1");
        first.Title.Should().Be("First Light");
        first.Cost.Should().Be(500);
        first.IsEnabled.Should().BeTrue();
        // Sync reads only_manageable_rewards=true, so everything it upserts is bot-manageable.
        first.IsManageable.Should().BeTrue();

        Reward second = rewards.Single(r => r.TwitchRewardId == "tw-reward-2");
        second.Title.Should().Be("Hydrate");
        second.Cost.Should().Be(100);
        second.IsEnabled.Should().BeFalse();
    }
}
