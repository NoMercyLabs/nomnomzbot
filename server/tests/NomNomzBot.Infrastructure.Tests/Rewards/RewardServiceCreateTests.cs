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
using NomNomzBot.Application.Rewards.Dtos;
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
            new()
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
            DefaultImage: new("1x", "2x", "4x"),
            BackgroundColor: "#000000",
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: new(false, 0),
            MaxPerUserPerStreamSetting: new(false, 0),
            GlobalCooldownSetting: new(false, 0),
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
            new() { Title = "windows says nope", Cost = 7500 }
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
            new() { Title = "Brand New Reward", Cost = 100 }
        );

        result.IsFailure.Should().BeTrue();
        db.Rewards.Should().BeEmpty();
    }

    // The Twitch state CreateCustomRewardAsync confirms for a newly-created reward. Cost/BackgroundColor/the
    // limit settings mirror what the caller asked for, since Helix normally just accepts what it's given.
    private static TwitchCustomReward CreatedReward(
        string title,
        int cost,
        string backgroundColor = "#000000",
        TwitchCustomRewardMaxPerStreamSetting? maxPerStream = null,
        TwitchCustomRewardMaxPerUserPerStreamSetting? maxPerUserPerStream = null,
        TwitchCustomRewardGlobalCooldownSetting? globalCooldown = null
    ) =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "tw-reward-new",
            Title: title,
            Prompt: "",
            Cost: cost,
            Image: null,
            DefaultImage: new("1x", "2x", "4x"),
            BackgroundColor: backgroundColor,
            IsEnabled: true,
            IsUserInputRequired: false,
            MaxPerStreamSetting: maxPerStream ?? new(false, 0),
            MaxPerUserPerStreamSetting: maxPerUserPerStream ?? new(false, 0),
            GlobalCooldownSetting: globalCooldown ?? new(false, 0),
            IsPaused: false,
            IsInStock: true,
            ShouldRedemptionsSkipRequestQueue: false,
            RedemptionsRedeemedCurrentStream: null,
            CooldownExpiresAt: null
        );

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
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(CreatedReward("Brand New Reward", 100)));

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Title = "Brand New Reward", Cost = 100 }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Brand New Reward");
        db.Rewards.Should().ContainSingle(r => r.Title == "Brand New Reward");
    }

    [Fact]
    public async Task CreateAsync_persists_the_on_redeem_response_text_and_returns_it()
    {
        // Response was write-only: accepted here, stored on the entity, but ToDetail never returned it — an
        // edit dialog reading the create result back would always see it blank even right after saving one.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(CreatedReward("Hydrate!", 100)));

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Title = "Hydrate!",
                Cost = 100,
                Response = "{{user}} redeemed a hydration break!",
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Should().Be("{{user}} redeemed a hydration break!");
        db.Rewards.Should()
            .ContainSingle(r => r.Response == "{{user}} redeemed a hydration break!");
    }

    [Fact]
    public async Task CreateAsync_actually_creates_the_reward_on_twitch_and_stores_its_confirmed_state()
    {
        // The core bug this closes: Create used to insert ONLY a local row and never call Helix at all, so
        // a "new reward" from the dashboard never became redeemable — CreateCustomRewardAsync had exactly
        // one caller in the whole service (RecreateUnderBotAsync) and this was never it.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        CreateCustomRewardRequest? pushed = null;
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Do<CreateCustomRewardRequest>(r => pushed = r),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    CreatedReward(
                        "Hydrate!",
                        100,
                        backgroundColor: "#00FF00",
                        maxPerStream: new(true, 3),
                        globalCooldown: new(true, 60)
                    )
                )
            );

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Title = "Hydrate!",
                Cost = 100,
                BackgroundColor = "#00FF00",
                MaxPerStream = 3,
                GlobalCooldownSeconds = 60,
            }
        );

        result.IsSuccess.Should().BeTrue();
        // The request Helix actually received carried the reward's real shape, not a default/empty one.
        pushed.Should().NotBeNull();
        pushed!.Title.Should().Be("Hydrate!");
        pushed.MaxPerStream.Should().Be(3);
        pushed.IsMaxPerStreamEnabled.Should().BeTrue();
        pushed.GlobalCooldownSeconds.Should().Be(60);
        pushed.IsGlobalCooldownEnabled.Should().BeTrue();
        // The local row mirrors what Helix CONFIRMED (a real Twitch id, the settings echoed back) — the
        // reward is now genuinely live and every field the row shows is real Twitch state.
        Reward row = await db.Rewards.SingleAsync(r => r.Title == "Hydrate!");
        row.TwitchRewardId.Should().Be("tw-reward-new");
        row.BackgroundColor.Should().Be("#00FF00");
        row.MaxPerStream.Should().Be(3);
        row.GlobalCooldownSeconds.Should().Be(60);
        result.Value.BackgroundColor.Should().Be("#00FF00");
        result.Value.MaxPerStream.Should().Be(3);
        result.Value.GlobalCooldownSeconds.Should().Be(60);
    }

    [Fact]
    public async Task CreateAsync_fails_closed_without_inserting_when_twitch_refuses_the_create()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>([]));
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchCustomReward>(
                    "You have exceeded the maximum number of custom rewards.",
                    "TWITCH_ERROR"
                )
            );

        Result<RewardDetail> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Title = "One Too Many", Cost = 100 }
        );

        result.IsFailure.Should().BeTrue();
        db.Rewards.Should().BeEmpty();
    }
}
