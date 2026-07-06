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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the reward IMPORT + CONVERT capability (rewards.md §3.1). Import pulls the FULL reward set
/// (<c>only_manageable_rewards=false</c>) so externally-created rewards land locally recorded as read-only
/// (<see cref="Reward.IsManageable"/> = false); convert recreates an external reward under the bot's client as
/// a second, bot-managed row (new Twitch id) while leaving the original external row untouched — Twitch does not
/// allow taking over a reward another client_id created — and refuses to "convert" a reward the bot already
/// manages.
/// </summary>
public sealed class RewardServiceImportTests
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

    private static TwitchCustomReward TwitchReward(
        string id,
        string title,
        int cost,
        bool enabled,
        bool manageable
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

    private static void StubGetRewards(
        ITwitchChannelPointsApi points,
        params TwitchCustomReward[] rewards
    ) =>
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>(rewards));

    [Fact]
    public async Task Import_persists_an_external_reward_as_read_only_and_a_managed_one_as_manageable()
    {
        // Import pulls the full set. The bot-created reward comes back is_manageable=true; the reward some other
        // app / the Twitch UI created comes back is_manageable=false and MUST be recorded read-only so the
        // dashboard never offers an edit/delete Twitch would reject.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        StubGetRewards(
            points,
            TwitchReward("mine-1", "Bot Reward", 200, enabled: true, manageable: true),
            TwitchReward("ext-1", "StreamElements Reward", 999, enabled: true, manageable: false)
        );

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        List<Reward> rewards = await db
            .Rewards.Where(r => r.BroadcasterId == Channel)
            .ToListAsync();
        rewards.Should().HaveCount(2);

        Reward mine = rewards.Single(r => r.TwitchRewardId == "mine-1");
        mine.IsManageable.Should().BeTrue();
        mine.IsPlatform.Should().BeTrue();

        Reward external = rewards.Single(r => r.TwitchRewardId == "ext-1");
        external.Title.Should().Be("StreamElements Reward");
        external.Cost.Should().Be(999);
        external.IsManageable.Should().BeFalse();
        external.IsPlatform.Should().BeFalse();
    }

    [Fact]
    public async Task Import_verifies_it_requested_the_full_set_not_only_manageable()
    {
        // The whole point of import (vs sync) is only_manageable_rewards=FALSE — assert we asked Twitch for it.
        (RewardService sut, _, ITwitchChannelPointsApi points) = Build();
        StubGetRewards(
            points,
            TwitchReward("ext-1", "External", 100, enabled: true, manageable: false)
        );

        await sut.ImportFromTwitchAsync(Channel.ToString());

        await points
            .Received(1)
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Recreate_creates_a_second_bot_managed_reward_and_leaves_the_external_one()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
        db.Rewards.Add(
            new Reward
            {
                Id = externalId,
                BroadcasterId = Channel,
                Title = "First Light",
                Description = "redeem me",
                Cost = 500,
                IsEnabled = true,
                TwitchRewardId = "ext-1",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();

        // Twitch echoes the newly created reward with a brand-new id under OUR client.
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    TwitchReward("bot-1", "First Light", 500, enabled: true, manageable: true)
                )
            );

        Result<RewardDetail> result = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            externalId.ToString()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Title.Should().Be("First Light");
        result.Value.Cost.Should().Be(500);
        result.Value.IsManageable.Should().BeTrue();

        // The create request copied title/cost/prompt/enabled off the external reward.
        await points
            .Received(1)
            .CreateCustomRewardAsync(
                Channel,
                Arg.Is<CreateCustomRewardRequest>(r =>
                    r.Title == "First Light"
                    && r.Cost == 500
                    && r.Prompt == "redeem me"
                    && r.IsEnabled == true
                ),
                Arg.Any<CancellationToken>()
            );

        List<Reward> rewards = await db
            .Rewards.Where(r => r.BroadcasterId == Channel)
            .ToListAsync();
        rewards
            .Should()
            .HaveCount(2, "the external reward is left in place, the bot copy is added");

        // The original external row is untouched — still read-only, still its own Twitch id.
        Reward external = rewards.Single(r => r.Id == externalId);
        external.TwitchRewardId.Should().Be("ext-1");
        external.IsManageable.Should().BeFalse();
        external.IsPlatform.Should().BeFalse();

        // The new bot row carries the new Twitch id and is fully manageable.
        Reward bot = rewards.Single(r => r.Id != externalId);
        bot.TwitchRewardId.Should().Be("bot-1");
        bot.Title.Should().Be("First Light");
        bot.Cost.Should().Be(500);
        bot.IsManageable.Should().BeTrue();
        bot.IsPlatform.Should().BeTrue();
    }

    [Fact]
    public async Task Recreate_refuses_a_reward_the_bot_already_manages()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid managedId = Guid.Parse("0192a000-0000-7000-8000-00000000e002");
        db.Rewards.Add(
            new Reward
            {
                Id = managedId,
                BroadcasterId = Channel,
                Title = "Bot Reward",
                Cost = 100,
                IsEnabled = true,
                TwitchRewardId = "mine-1",
                IsManageable = true,
                IsPlatform = true,
            }
        );
        await db.SaveChangesAsync();

        Result<RewardDetail> result = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            managedId.ToString()
        );

        result.IsFailure.Should().BeTrue();
        // ALREADY_EXISTS → 409 Conflict via BaseController.ResultResponse (not a fall-through 500).
        result.ErrorCode.Should().Be("ALREADY_EXISTS");

        // No Twitch reward is created, and nothing is duplicated.
        await points
            .DidNotReceive()
            .CreateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            );
        (await db.Rewards.CountAsync(r => r.BroadcasterId == Channel)).Should().Be(1);
    }

    [Fact]
    public async Task Importing_again_after_a_recreate_reconciles_both_rows_without_a_duplicate_title_crash()
    {
        // Recreating leaves two rows that share a title (the external "First Light" and the bot copy). A second
        // import must reconcile both by their distinct Twitch ids, not choke building its title-match index.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e003");
        db.Rewards.Add(
            new Reward
            {
                Id = externalId,
                BroadcasterId = Channel,
                Title = "First Light",
                Description = "redeem me",
                Cost = 500,
                IsEnabled = true,
                TwitchRewardId = "ext-1",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();

        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    TwitchReward("bot-1", "First Light", 500, enabled: true, manageable: true)
                )
            );
        await sut.RecreateUnderBotAsync(Channel.ToString(), externalId.ToString());

        // Now both same-titled rewards come back from the full import.
        StubGetRewards(
            points,
            TwitchReward("ext-1", "First Light", 550, enabled: false, manageable: false),
            TwitchReward("bot-1", "First Light", 500, enabled: true, manageable: true)
        );

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        List<Reward> rewards = await db
            .Rewards.Where(r => r.BroadcasterId == Channel)
            .ToListAsync();
        rewards.Should().HaveCount(2, "reconciled by id, no third row created");

        // The external row updated in place and stayed read-only; the bot row stayed manageable.
        Reward external = rewards.Single(r => r.TwitchRewardId == "ext-1");
        external.Cost.Should().Be(550);
        external.IsManageable.Should().BeFalse();
        rewards.Single(r => r.TwitchRewardId == "bot-1").IsManageable.Should().BeTrue();
    }
}
