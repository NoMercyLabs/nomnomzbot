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
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves <see cref="RewardService.CreateAsync"/> checks the LIVE Twitch reward list (not just the local
/// table, which can be stale or never-imported) before ever inserting a new reward. Twitch has no
/// server-side uniqueness on titles, so a blind create would silently duplicate an existing reward and
/// burn the streamer's channel-point economy — this is a generic safeguard for every reward creation, not
/// specific to any one title. A duplicate title (case-insensitive) fails closed with ALREADY_EXISTS and no
/// local row is ever persisted; a Twitch read failure also fails closed rather than risking a duplicate; a
/// genuinely new title creates the local definition as before.
/// </summary>
public sealed class RewardServiceCreateTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d201");

    private static (RewardService Sut, AuthDbContext Db, ITwitchChannelPointsApi Points) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000d200"),
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

    private static TwitchCustomReward ExistingReward(string title) =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "tw-reward-existing",
            Title: title,
            Prompt: "already here",
            Cost: 7500,
            Image: null,
            DefaultImage: new TwitchCustomRewardImage("1x", "2x", "4x"),
            BackgroundColor: "#000000",
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

    [Fact]
    public async Task CreateAsync_TitleMatchesExistingTwitchReward_FailsClosedWithoutInserting()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchCustomReward>>([
                    ExistingReward("Windows Says Nope"),
                ])
            );

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new CreateRewardRequest { Title = "windows says nope", Cost = 7500 }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("ALREADY_EXISTS");
        db.Rewards.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_TwitchReadFails_FailsClosedWithoutInserting()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<IReadOnlyList<TwitchCustomReward>>(
                    "Twitch is unreachable.",
                    "EXTERNAL_SERVICE_UNAVAILABLE"
                )
            );

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new CreateRewardRequest { Title = "Brand New Reward", Cost = 100 }
        );

        result.IsFailure.Should().BeTrue();
        db.Rewards.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_NoTitleCollision_CreatesTheLocalDefinition()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchCustomReward>>([
                    ExistingReward("Some Other Reward"),
                ])
            );

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new CreateRewardRequest { Title = "Brand New Reward", Cost = 100 }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Brand New Reward");
        db.Rewards.Should().ContainSingle(r => r.Title == "Brand New Reward");
    }
}
