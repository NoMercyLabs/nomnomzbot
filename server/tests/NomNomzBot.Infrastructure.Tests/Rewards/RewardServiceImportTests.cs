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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the reward IMPORT + CONVERT capability (rewards.md §3.1). Import pulls the FULL reward set
/// (<c>only_manageable_rewards=false</c>) so externally-created rewards land locally recorded as read-only
/// (<see cref="Reward.IsManageable"/> = false); convert recreates an external reward under the bot's client
/// (Twitch does not allow taking over a reward another client_id created) and re-points the SAME local row at
/// it, keeping its configuration; it refuses to "convert" a reward the bot already manages.
/// </summary>
public sealed class RewardServiceImportTests
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
        // The real clock: these tests complete in milliseconds, and the throttle-specific tests below set
        // Reward.RewardsSyncedAt relative to DateTime.UtcNow directly rather than needing a fake to advance.
        RewardService sut = new(
            db,
            points,
            TimeProvider.System,
            NullLogger<RewardService>.Instance
        );
        return (sut, db, points);
    }

    // The production context (soft-delete interceptor + global soft-delete filter + the real unique index on
    // (BroadcasterId, TwitchRewardId)) — AuthDbContext above has neither the filter nor soft delete, so a
    // deleted-row bug can never reproduce against it.
    private static (RewardService Sut, AppDbContext Db, ITwitchChannelPointsApi Points) BuildReal()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(
                new SoftDeleteInterceptor(TimeProvider.System, new NullCurrentUserService())
            )
            .Options;
        AppDbContext db = new(options);
        db.Database.EnsureCreated();

        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000d200");
        db.Users.Add(
            new()
            {
                Id = ownerId,
                TwitchUserId = "tw-owner",
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = ownerId,
                TwitchChannelId = "tw-channel",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();

        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        RewardService sut = new(
            db,
            points,
            TimeProvider.System,
            NullLogger<RewardService>.Instance
        );
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
            DefaultImage: new("1x", "2x", "4x"),
            BackgroundColor: "#000000",
            IsEnabled: enabled,
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
            .Returns(Result.Success(full));
        points
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: true,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(manageable));
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
    public async Task Recreate_repoints_the_same_row_at_the_new_bot_reward_and_keeps_its_configuration()
    {
        // Live 2026-09-29: taking control used to INSERT a second row (no pipeline, no response, no limits) and
        // leave the old external row showing "Take control" — the dashboard then listed the reward twice and
        // the streamer deleted the configured-less copy, orphaning it on Twitch. The local row IS the reward:
        // taking control re-points it at the bot-owned Twitch reward and keeps everything configured on it.
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
        db.Rewards.Add(
            new()
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
                Response = "Thanks {user}!",
                GlobalCooldownSeconds = 30,
                MaxPerStream = 5,
                PendingMigrationRequestedAt = DateTime.UtcNow.AddMinutes(-5),
            }
        );
        await db.SaveChangesAsync();

        // The streamer removed the original on Twitch, so the title is free.
        StubGetRewards(points, full: [], manageable: []);
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
        result.Value.Id.Should().Be(externalId.ToString());
        result.Value.IsManageable.Should().BeTrue();

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
            .Rewards.IgnoreQueryFilters()
            .Where(r => r.BroadcasterId == Channel)
            .ToListAsync();
        rewards.Should().ContainSingle("taking control never adds a second row");

        Reward taken = rewards[0];
        taken.Id.Should().Be(externalId);
        taken.TwitchRewardId.Should().Be("bot-1");
        taken.IsManageable.Should().BeTrue();
        taken.IsPlatform.Should().BeTrue();
        taken.PendingMigrationRequestedAt.Should().BeNull();
        taken.Response.Should().Be("Thanks {user}!");
        taken.GlobalCooldownSeconds.Should().Be(30);
        taken.MaxPerStream.Should().Be(5);
    }

    [Fact]
    public async Task Sync_skips_a_bot_reward_whose_local_row_was_deleted_instead_of_crashing()
    {
        // Live 2026-09-29: a deleted row still held its Twitch id; the soft-delete filter hid it from the
        // sync, so the sync title-linked that id onto the live same-titled row and the unique index
        // (BroadcasterId, TwitchRewardId) threw — killing every script that touched rewards mid-run.
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid deletedId = Guid.Parse("0192a000-0000-7000-8000-00000000e101");
        Guid liveId = Guid.Parse("0192a000-0000-7000-8000-00000000e102");
        db.Rewards.AddRange(
            new Reward
            {
                Id = deletedId,
                BroadcasterId = Channel,
                Title = "First",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "bot-old",
                IsManageable = true,
                IsPlatform = true,
                DeletedAt = DateTime.UtcNow.AddDays(-1),
            },
            new Reward
            {
                Id = liveId,
                BroadcasterId = Channel,
                Title = "First",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "ext-first",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();
        StubGetRewards(
            points,
            full: [],
            manageable: [TwitchReward("bot-old", "First", 1, enabled: true)]
        );

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        List<Reward> rows = await db
            .Rewards.IgnoreQueryFilters()
            .Where(r => r.BroadcasterId == Channel)
            .ToListAsync();
        rows.Should()
            .HaveCount(2, "a deleted row's Twitch reward is never resurrected as a new row");
        Reward deleted = rows.Single(r => r.Id == deletedId);
        deleted.DeletedAt.Should().NotBeNull();
        deleted.TwitchRewardId.Should().Be("bot-old");
        rows.Single(r => r.Id == liveId).TwitchRewardId.Should().Be("ext-first");
    }

    [Fact]
    public async Task Sync_links_a_parked_reward_to_the_bot_reward_under_its_title_and_clears_the_pending_marker()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid parkedId = Guid.Parse("0192a000-0000-7000-8000-00000000e103");
        db.Rewards.Add(
            new Reward
            {
                Id = parkedId,
                BroadcasterId = Channel,
                Title = "First",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = null,
                IsManageable = false,
                IsPlatform = false,
                PendingMigrationRequestedAt = DateTime.UtcNow.AddDays(-2),
            }
        );
        await db.SaveChangesAsync();
        StubGetRewards(
            points,
            full: [],
            manageable: [TwitchReward("bot-new", "First", 1, enabled: true)]
        );

        Result result = await sut.SyncWithTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Reward linked = await db.Rewards.SingleAsync(r => r.Id == parkedId);
        linked.TwitchRewardId.Should().Be("bot-new");
        linked.IsManageable.Should().BeTrue();
        linked.PendingMigrationRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_bot_managed_reward_removes_it_on_Twitch_and_releases_its_id()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid rewardId = Guid.Parse("0192a000-0000-7000-8000-00000000e104");
        db.Rewards.Add(
            new Reward
            {
                Id = rewardId,
                BroadcasterId = Channel,
                Title = "First",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "bot-1",
                IsManageable = true,
                IsPlatform = true,
            }
        );
        await db.SaveChangesAsync();
        points
            .DeleteCustomRewardAsync(Channel, "bot-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await sut.DeleteAsync(Channel.ToString(), rewardId.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await points
            .Received(1)
            .DeleteCustomRewardAsync(Channel, "bot-1", Arg.Any<CancellationToken>());
        Reward gone = await db.Rewards.IgnoreQueryFilters().SingleAsync(r => r.Id == rewardId);
        gone.DeletedAt.Should().NotBeNull();
        gone.TwitchRewardId.Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_bot_managed_reward_Twitch_already_lost_still_deletes_it_locally()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid rewardId = Guid.Parse("0192a000-0000-7000-8000-00000000e105");
        db.Rewards.Add(
            new Reward
            {
                Id = rewardId,
                BroadcasterId = Channel,
                Title = "Gone",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "bot-2",
                IsManageable = true,
                IsPlatform = true,
            }
        );
        await db.SaveChangesAsync();
        points
            .DeleteCustomRewardAsync(Channel, "bot-2", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Not found.", TwitchErrorCodes.NotFound));

        Result result = await sut.DeleteAsync(Channel.ToString(), rewardId.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await db.Rewards.IgnoreQueryFilters().SingleAsync(r => r.Id == rewardId))
            .DeletedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task Deleting_keeps_the_reward_when_Twitch_refuses_the_delete()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid rewardId = Guid.Parse("0192a000-0000-7000-8000-00000000e106");
        db.Rewards.Add(
            new Reward
            {
                Id = rewardId,
                BroadcasterId = Channel,
                Title = "Kept",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "bot-3",
                IsManageable = true,
                IsPlatform = true,
            }
        );
        await db.SaveChangesAsync();
        points
            .DeleteCustomRewardAsync(Channel, "bot-3", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Twitch rejected the token.", TwitchErrorCodes.Unauthorized));

        Result result = await sut.DeleteAsync(Channel.ToString(), rewardId.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        Reward kept = await db.Rewards.SingleAsync(r => r.Id == rewardId);
        kept.TwitchRewardId.Should().Be("bot-3");
    }

    [Fact]
    public async Task Import_finishes_a_parked_take_over_once_the_original_is_gone_from_Twitch()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid parkedId = Guid.Parse("0192a000-0000-7000-8000-00000000e107");
        db.Rewards.Add(
            new Reward
            {
                Id = parkedId,
                BroadcasterId = Channel,
                Title = "Hydrate!",
                Description = "drink",
                Cost = 100,
                IsEnabled = true,
                TwitchRewardId = "ext-hydrate",
                IsManageable = false,
                IsPlatform = false,
                Response = "Stay hydrated {user}",
                PendingMigrationRequestedAt = DateTime.UtcNow.AddDays(-1),
            }
        );
        await db.SaveChangesAsync();
        // The streamer deleted the original on Twitch; nothing carries the title any more.
        StubGetRewards(points, full: [], manageable: []);
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(TwitchReward("bot-hydrate", "Hydrate!", 100, enabled: true)));

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Reward taken = await db.Rewards.SingleAsync(r => r.Id == parkedId);
        taken.TwitchRewardId.Should().Be("bot-hydrate");
        taken.IsManageable.Should().BeTrue();
        taken.PendingMigrationRequestedAt.Should().BeNull();
        taken.Response.Should().Be("Stay hydrated {user}");
    }

    [Fact]
    public async Task Import_finishes_a_parked_take_over_once_the_original_was_renamed_on_Twitch()
    {
        (RewardService sut, AppDbContext db, ITwitchChannelPointsApi points) = BuildReal();
        Guid parkedId = Guid.Parse("0192a000-0000-7000-8000-00000000e108");
        db.Rewards.Add(
            new Reward
            {
                Id = parkedId,
                BroadcasterId = Channel,
                Title = "Time-out",
                Cost = 5000,
                IsEnabled = true,
                TwitchRewardId = "ext-timeout",
                IsManageable = false,
                IsPlatform = false,
                PendingMigrationRequestedAt = DateTime.UtcNow.AddDays(-1),
            }
        );
        await db.SaveChangesAsync();
        StubGetRewards(
            points,
            full: [TwitchReward("ext-timeout", "Time-out (old)", 5000, enabled: false)],
            manageable: []
        );
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(TwitchReward("bot-timeout", "Time-out", 5000, enabled: true)));

        Result result = await sut.ImportFromTwitchAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await points
            .Received(1)
            .CreateCustomRewardAsync(
                Channel,
                Arg.Is<CreateCustomRewardRequest>(r => r.Title == "Time-out"),
                Arg.Any<CancellationToken>()
            );
        Reward taken = await db.Rewards.SingleAsync(r => r.Id == parkedId);
        taken.Title.Should().Be("Time-out");
        taken.TwitchRewardId.Should().Be("bot-timeout");
        taken.IsManageable.Should().BeTrue();
    }

    [Fact]
    public async Task Recreate_refuses_a_reward_the_bot_already_manages()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid managedId = Guid.Parse("0192a000-0000-7000-8000-00000000e002");
        db.Rewards.Add(
            new()
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
    public async Task Recreate_parks_a_title_conflict_instead_of_losing_the_request_then_finalizes_once_cleared()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e003");
        db.Rewards.Add(
            new Reward
            {
                Id = externalId,
                BroadcasterId = Channel,
                Title = "Dunglish",
                Description = "redeem me",
                Cost = 250,
                IsEnabled = true,
                TwitchRewardId = "ext-2",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();

        // The title is still taken on Twitch by a SEPARATE reward (ext-2 is the one we're converting;
        // stray-1 is the actual conflict) — the real conflict that makes Twitch 400 the create. A stray
        // LOCAL row would never catch this: the local table can be stale or never knew about it at all.
        StubGetRewards(
            points,
            full:
            [
                TwitchReward("ext-2", "Dunglish", 250, enabled: true),
                TwitchReward("stray-1", "Dunglish", 250, enabled: true),
            ],
            manageable: []
        );

        Result<RewardDetail> parked = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            externalId.ToString()
        );

        // Parked, not lost: a specific, actionable error code — never a bare Twitch 400 — and nothing deleted.
        parked.IsFailure.Should().BeTrue();
        parked.ErrorCode.Should().Be("MIGRATION_PENDING_EXTERNAL_REMOVAL");
        await points
            .DidNotReceive()
            .CreateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            );

        Reward parkedExternal = await db.Rewards.SingleAsync(r => r.Id == externalId);
        parkedExternal.PendingMigrationRequestedAt.Should().NotBeNull();
        // Every field the eventual recreate needs is still exactly as it was — nothing was lost while parked.
        parkedExternal.Title.Should().Be("Dunglish");
        parkedExternal.Cost.Should().Be(250);
        parkedExternal.Description.Should().Be("redeem me");

        // The operator clears the title conflict on Twitch itself (deletes/renames the reward there) — we
        // simulate that by re-stubbing the live list to no longer carry that title, then retry the SAME
        // action to finalize.
        StubGetRewards(points, full: [], manageable: []);

        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(TwitchReward("bot-2", "Dunglish", 250, enabled: true)));

        Result<RewardDetail> finalized = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            externalId.ToString()
        );

        finalized.IsSuccess.Should().BeTrue(finalized.ErrorMessage);
        finalized.Value.TimerDurationSeconds.Should().BeNull(); // sanity: a real detail, not a stub
        Reward finalizedExternal = await db.Rewards.SingleAsync(r => r.Id == externalId);
        finalizedExternal
            .PendingMigrationRequestedAt.Should()
            .BeNull("the marker clears once it finalizes");
    }

    [Fact]
    public async Task Recreate_parks_a_conflict_the_local_table_never_knew_about()
    {
        // The exact bug this guards: no OTHER local row shares the title (the local table has nothing to
        // find), but Twitch's live list still carries a second reward under that title — something the bot
        // never imported. A local-only check would say "not taken" and sail into CreateCustomReward, which
        // Twitch then 400s with a raw CREATE_CUSTOM_REWARD_DUPLICATE_REWARD. This must park instead.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e005");
        db.Rewards.Add(
            new Reward
            {
                Id = externalId,
                BroadcasterId = Channel,
                Title = "Steal the Lucky Feather",
                Description = "redeem me",
                Cost = 1000,
                IsEnabled = true,
                TwitchRewardId = "ext-5",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();

        // Live Twitch shows the same title twice (ext-5 itself, plus an untracked stray reward) — the local
        // table only ever knew about ext-5.
        StubGetRewards(
            points,
            full:
            [
                TwitchReward("ext-5", "Steal the Lucky Feather", 1000, enabled: true),
                TwitchReward("untracked-1", "Steal the Lucky Feather", 250, enabled: true),
            ],
            manageable: []
        );

        Result<RewardDetail> result = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            externalId.ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MIGRATION_PENDING_EXTERNAL_REMOVAL");
        await points
            .DidNotReceive()
            .CreateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            );
        (await db.Rewards.SingleAsync(r => r.Id == externalId))
            .PendingMigrationRequestedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task Recreate_parks_a_conflict_the_live_list_pre_check_itself_missed()
    {
        // Verified against production (2026-09-10, channel 019f146e-8303-71ef-b698-18d1098d7d7e): the live-list
        // pre-check read no conflicting title, yet Twitch's own Create Custom Reward validation still rejected
        // the create with the raw message "CREATE_CUSTOM_REWARD_DUPLICATE_REWARD" — Twitch's duplicate check
        // sees more than the list endpoint reflects (a just-freed title held in reserve). Before this fix that
        // fell straight through as an opaque twitch_error/503 with no guidance; it must park instead, exactly
        // like the pre-check catching it directly.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e006");
        db.Rewards.Add(
            new Reward
            {
                Id = externalId,
                BroadcasterId = Channel,
                Title = "First",
                Description = "redeem me",
                Cost = 1,
                IsEnabled = true,
                TwitchRewardId = "ext-6",
                IsManageable = false,
                IsPlatform = false,
            }
        );
        await db.SaveChangesAsync();

        // The pre-check sees a clean list — only `external`'s own live entry, no other title match.
        StubGetRewards(
            points,
            full: [TwitchReward("ext-6", "First", 1, enabled: true)],
            manageable: []
        );

        // But Twitch's create call itself rejects it with its real, raw duplicate signal.
        points
            .CreateCustomRewardAsync(
                Channel,
                Arg.Any<CreateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchCustomReward>(
                    "Twitch request failed (400).",
                    TwitchErrorCodes.TwitchError,
                    "CREATE_CUSTOM_REWARD_DUPLICATE_REWARD"
                )
            );

        Result<RewardDetail> result = await sut.RecreateUnderBotAsync(
            Channel.ToString(),
            externalId.ToString()
        );

        // Parked with the same actionable code the pre-check path gives — never the raw twitch_error/503.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MIGRATION_PENDING_EXTERNAL_REMOVAL");
        result.ErrorMessage.Should().Contain("First").And.Contain("Finalize migration");

        Reward parked = await db.Rewards.SingleAsync(r => r.Id == externalId);
        parked.PendingMigrationRequestedAt.Should().NotBeNull();
        // Nothing configured was lost while parked.
        parked.Title.Should().Be("First");
        parked.Cost.Should().Be(1);
        parked.TwitchRewardId.Should().Be("ext-6");
        parked.IsManageable.Should().BeFalse();

        // No local bot-copy row was ever inserted from the rejected create.
        (await db.Rewards.CountAsync(r => r.BroadcasterId == Channel))
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task Importing_again_after_a_recreate_reconciles_both_rows_without_a_duplicate_title_crash()
    {
        // Taking control re-points the local row at the bot copy; if the original still exists on Twitch (e.g. it
        // was renamed rather than deleted), the next import records it as its own read-only row, matched by its
        // own Twitch id — never a crash, never a third row.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Guid externalId = Guid.Parse("0192a000-0000-7000-8000-00000000e003");
        db.Rewards.Add(
            new()
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

        StubGetRewards(
            points,
            full: [TwitchReward("ext-1", "First Light", 500, enabled: true)],
            manageable: []
        );
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

    // ── Onboarding: ListAsync auto-imports in the background, throttled by Channel.RewardsSyncedAt ──────────

    [Fact]
    public async Task List_triggers_an_import_the_first_time_a_channel_has_never_synced()
    {
        // Channel.RewardsSyncedAt is null (never synced) — a streamer's pre-existing Twitch-dashboard reward
        // must surface on the very first Rewards page load, without them ever finding the manual Import button.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        StubGetRewards(
            points,
            full: [TwitchReward("ext-1", "StreamElements Reward", 300, enabled: true)],
            manageable: []
        );

        Result<PagedList<RewardDetail>> result = await sut.ListAsync(
            Channel.ToString(),
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Items.Should()
            .ContainSingle(r => r.Title == "StreamElements Reward" && !r.IsManageable);

        Channel channel = await db.Channels.SingleAsync(c => c.Id == Channel);
        channel.RewardsSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task List_does_not_reimport_within_the_throttle_window()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Channel channel = await db.Channels.SingleAsync(c => c.Id == Channel);
        channel.RewardsSyncedAt = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();

        await sut.ListAsync(Channel.ToString(), new PaginationParams(1, 25));

        await points
            .DidNotReceive()
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task List_reimports_once_the_throttle_window_has_elapsed()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build();
        Channel channel = await db.Channels.SingleAsync(c => c.Id == Channel);
        DateTime staleSync = DateTime.UtcNow.AddHours(-7);
        channel.RewardsSyncedAt = staleSync;
        await db.SaveChangesAsync();
        StubGetRewards(points, full: [], manageable: []);

        await sut.ListAsync(Channel.ToString(), new PaginationParams(1, 25));

        await points
            .Received(1)
            .GetCustomRewardsAsync(
                Channel,
                Arg.Any<IReadOnlyList<string>?>(),
                onlyManageableRewards: false,
                Arg.Any<CancellationToken>()
            );
        Channel reloaded = await db.Channels.SingleAsync(c => c.Id == Channel);
        reloaded.RewardsSyncedAt.Should().BeAfter(staleSync);
    }

    [Fact]
    public async Task List_still_succeeds_and_advances_the_throttle_when_the_background_import_fails()
    {
        // A dead token or transient Helix error during the background sync must never break the reward list
        // itself, and must still advance the throttle stamp — otherwise a struggling channel gets retried on
        // every single Rewards page load instead of backing off like every other Twitch-facing poll in this
        // codebase.
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
                    "Twitch rejected the token.",
                    TwitchErrorCodes.Unauthorized
                )
            );

        Result<PagedList<RewardDetail>> result = await sut.ListAsync(
            Channel.ToString(),
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Channel channel = await db.Channels.SingleAsync(c => c.Id == Channel);
        channel.RewardsSyncedAt.Should().NotBeNull();
    }
}
