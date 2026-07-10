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
        bool enabled
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

    // Mirror Twitch: the FULL reward set answers `only_manageable_rewards=false`, the manageable subset (the
    // rewards THIS client_id created) answers `=true`. Manageability is thus expressed ONLY by set membership,
    // exactly as the live API does — the reward payload carries no is_manageable field to lean on.
    private static void StubGetRewards(
        ITwitchChannelPointsApi points,
        IReadOnlyList<TwitchCustomReward> full,
        IReadOnlyList<TwitchCustomReward> manageable
    )
    {
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>(full));
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: true,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchCustomReward>>(manageable));
    }

    [Fact]
    public async Task Import_persists_an_external_reward_as_read_only_and_a_managed_one_as_manageable()
    {
        // Import pulls the full set, then the manageable subset, and derives manageability from set membership.
        // "mine-1" is in the manageable subset (the bot created it) → stored manageable + platform. "ext-1" is
        // in the full set but NOT the manageable subset (another app / the Twitch UI created it) → stored
        // read-only, so the dashboard never offers an edit/delete Twitch would reject and instead shows
        // "take control".
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        TwitchCustomReward mineReward = TwitchReward("mine-1", "Bot Reward", 200, enabled: true);
        TwitchCustomReward extReward = TwitchReward(
            "ext-1",
            "StreamElements Reward",
            999,
            enabled: true
        );
        StubGetRewards(points, full: [mineReward, extReward], manageable: [mineReward]);

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
    public async Task Import_stores_our_own_rewards_manageable_even_though_the_wire_carries_no_manageable_flag()
    {
        // Regression guard for the data-truthfulness bug: Twitch's Get Custom Rewards response never emits
        // is_manageable, so nothing on the reward payload can mark a reward manageable. Before the fix EVERY
        // imported reward was stored unmanaged and the dashboard offered "take control" on rewards we already
        // own. Manageability MUST resolve from the only_manageable_rewards subset alone: an id in that subset is
        // stored manageable + platform; an id absent from it is stored read-only.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        TwitchCustomReward ours = TwitchReward("ours-1", "Follow Alert", 50, enabled: true);
        TwitchCustomReward theirs = TwitchReward("theirs-1", "Nightbot Reward", 300, enabled: true);
        StubGetRewards(points, full: [ours, theirs], manageable: [ours]);

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        Reward storedOurs = await db.Rewards.SingleAsync(r => r.TwitchRewardId == "ours-1");
        storedOurs.IsManageable.Should().BeTrue();
        storedOurs.IsPlatform.Should().BeTrue();

        Reward storedTheirs = await db.Rewards.SingleAsync(r => r.TwitchRewardId == "theirs-1");
        storedTheirs.IsManageable.Should().BeFalse();
        storedTheirs.IsPlatform.Should().BeFalse();
    }

    [Fact]
    public async Task Import_surfaces_the_manageable_subset_read_failure_instead_of_marking_everything_unmanaged()
    {
        // The manageable subset is a SECOND Helix read. If it fails, import must NOT fall back to "everything
        // unmanaged" (that is exactly the wrong-data outcome the fix exists to prevent) — it must fail carrying
        // the real Helix error, and persist nothing.
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
                    TwitchReward("ext-1", "External", 100, enabled: true),
                ])
            );
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: true,
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<IReadOnlyList<TwitchCustomReward>>(
                    "Twitch rejected the token.",
                    TwitchErrorCodes.Unauthorized
                )
            );

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        (await db.Rewards.AnyAsync(r => r.BroadcasterId == Channel)).Should().BeFalse();
    }

    [Fact]
    public async Task Import_verifies_it_requested_the_full_set_not_only_manageable()
    {
        // The whole point of import (vs sync) is only_manageable_rewards=FALSE — assert we asked Twitch for it.
        (RewardService sut, _, ITwitchChannelPointsApi points) = Build();
        StubGetRewards(
            points,
            full: [TwitchReward("ext-1", "External", 100, enabled: true)],
            manageable: []
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
            .Returns(Result.Success(TwitchReward("bot-1", "First Light", 500, enabled: true)));

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
            .Returns(Result.Success(TwitchReward("bot-1", "First Light", 500, enabled: true)));
        await sut.RecreateUnderBotAsync(Channel.ToString(), externalId.ToString());

        // Now both same-titled rewards come back from the full import; only bot-1 is in the manageable subset.
        TwitchCustomReward extAgain = TwitchReward("ext-1", "First Light", 550, enabled: false);
        TwitchCustomReward botAgain = TwitchReward("bot-1", "First Light", 500, enabled: true);
        StubGetRewards(points, full: [extAgain, botAgain], manageable: [botAgain]);

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
