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
/// Proves <see cref="RewardService.UpdateAsync"/> keeps the local reward and Twitch in step: a Twitch-facing
/// patch (title/cost/prompt/enabled/paused) on a bot-manageable synced reward is PATCHed to Helix first and only
/// then persisted locally; a Helix refusal leaves the local row untouched; an externally-created (non-manageable)
/// synced reward is read-only for those fields (fail-closed FORBIDDEN) while its bot-local bindings stay editable;
/// and a local-only reward (no Twitch id yet) updates locally without any Helix call.
/// </summary>
public sealed class RewardServiceUpdateTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d101");
    private static readonly Guid RewardId = Guid.Parse("0192a000-0000-7000-8000-00000000d102");

    private static (RewardService Sut, AuthDbContext Db, ITwitchChannelPointsApi Points) Build(
        bool manageable,
        string? twitchRewardId
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Rewards.Add(
            new Reward
            {
                Id = RewardId,
                BroadcasterId = Channel,
                Title = "Lucky Feather",
                Description = "Steal the feather",
                Cost = 500,
                IsEnabled = true,
                IsManageable = manageable,
                TwitchRewardId = twitchRewardId,
            }
        );
        db.SaveChanges();

        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        RewardService sut = new(db, points, NullLogger<RewardService>.Instance);
        return (sut, db, points);
    }

    private static TwitchCustomReward TwitchReward() =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "tw-reward-1",
            Title: "Luckier Feather",
            Prompt: "redeem me",
            Cost: 750,
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
            RedemptionsRedeemedCurrentStream: 0,
            CooldownExpiresAt: null
        );

    [Fact]
    public async Task Update_pushes_the_patch_to_helix_then_persists_it_locally()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: true,
            twitchRewardId: "tw-reward-1"
        );
        UpdateCustomRewardRequest? pushed = null;
        points
            .UpdateCustomRewardAsync(
                Channel,
                "tw-reward-1",
                Arg.Do<UpdateCustomRewardRequest>(r => pushed = r),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(TwitchReward()));

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new UpdateRewardRequest
            {
                Title = "Luckier Feather",
                Cost = 750,
                Prompt = "steal harder",
                IsPaused = true,
            }
        );

        result.IsSuccess.Should().BeTrue();
        // The Helix-facing call received exactly the declared patch (nulls omitted by the transport).
        pushed.Should().NotBeNull();
        pushed!.Title.Should().Be("Luckier Feather");
        pushed.Cost.Should().Be(750);
        pushed.Prompt.Should().Be("steal harder");
        pushed.IsPaused.Should().BeTrue();
        pushed.IsEnabled.Should().BeNull();
        // The local row now carries the patch (IsPaused lives only on Twitch).
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Title.Should().Be("Luckier Feather");
        row.Cost.Should().Be(750);
        row.Description.Should().Be("steal harder");
    }

    [Fact]
    public async Task Update_does_not_persist_locally_when_helix_refuses()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: true,
            twitchRewardId: "tw-reward-1"
        );
        points
            .UpdateCustomRewardAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<UpdateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchCustomReward>(
                    "Twitch rejected the token.",
                    TwitchErrorCodes.Unauthorized
                )
            );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new UpdateRewardRequest { Cost = 750 }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.Unauthorized);
        // The local copy never drifted from what is live on Twitch.
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Cost.Should().Be(500);
    }

    [Fact]
    public async Task Update_refuses_twitch_facing_fields_on_an_external_reward()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: false,
            twitchRewardId: "tw-external-1"
        );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new UpdateRewardRequest { Cost = 750 }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!, default);
        (await db.Rewards.SingleAsync(r => r.Id == RewardId)).Cost.Should().Be(500);
    }

    [Fact]
    public async Task Update_still_allows_bot_local_bindings_on_an_external_reward()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: false,
            twitchRewardId: "tw-external-1"
        );
        Guid pipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000d103");

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new UpdateRewardRequest { PipelineId = pipelineId, TimerDurationSeconds = 60 }
        );

        result.IsSuccess.Should().BeTrue();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.PipelineId.Should().Be(pipelineId);
        row.TimerDurationSeconds.Should().Be(60);
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Update_on_a_local_only_reward_persists_without_a_helix_call()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: false,
            twitchRewardId: null
        );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new UpdateRewardRequest { Title = "Renamed", Cost = 100 }
        );

        result.IsSuccess.Should().BeTrue();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Title.Should().Be("Renamed");
        row.Cost.Should().Be(100);
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!, default);
    }
}
