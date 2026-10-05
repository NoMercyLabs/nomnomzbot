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
            new()
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
        RewardService sut = new(
            db,
            points,
            TimeProvider.System,
            NullLogger<RewardService>.Instance,
            RewardServiceTestProvider.Create()
        );
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
            new()
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
    public async Task Update_pushes_input_requirement_and_stream_limits_to_helix()
    {
        (RewardService sut, _, ITwitchChannelPointsApi points) = Build(
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
            new()
            {
                IsUserInputRequired = true,
                MaxPerStream = 5,
                MaxPerUserPerStream = 1,
                GlobalCooldownSeconds = 60,
                BackgroundColor = "#772ce8",
            }
        );

        result.IsSuccess.Should().BeTrue();
        pushed.Should().NotBeNull();
        pushed!.IsUserInputRequired.Should().BeTrue();
        pushed.MaxPerStream.Should().Be(5);
        pushed.IsMaxPerStreamEnabled.Should().BeTrue();
        pushed.MaxPerUserPerStream.Should().Be(1);
        pushed.IsMaxPerUserPerStreamEnabled.Should().BeTrue();
        pushed.GlobalCooldownSeconds.Should().Be(60);
        pushed.IsGlobalCooldownEnabled.Should().BeTrue();
        pushed.BackgroundColor.Should().Be("#772ce8");
        // The read-side must reflect what was actually pushed to Twitch — not a hardcoded stale value.
        result.Value.IsUserInputRequired.Should().BeTrue();
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
            new() { Cost = 750 }
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
            new() { Cost = 750 }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
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
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Channel,
                Name = "bot-local pipeline",
                TriggerKind = "reward",
            }
        );
        await db.SaveChangesAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { PipelineId = pipelineId, TimerDurationSeconds = 60 }
        );

        result.IsSuccess.Should().BeTrue();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.PipelineId.Should().Be(pipelineId);
        row.TimerDurationSeconds.Should().Be(60);
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
    }

    [Fact]
    public async Task Update_refuses_a_pipeline_id_from_another_channel_and_leaves_the_reward_unchanged()
    {
        (RewardService sut, AuthDbContext db, _) = Build(manageable: true, twitchRewardId: null);
        Guid otherChannel = Guid.NewGuid();
        Guid foreignPipelineId = Guid.NewGuid();
        db.Pipelines.Add(
            new()
            {
                Id = foreignPipelineId,
                BroadcasterId = otherChannel,
                Name = "someone else's pipeline",
                TriggerKind = "reward",
            }
        );
        await db.SaveChangesAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { PipelineId = foreignPipelineId }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PIPELINE_NOT_IN_CHANNEL");
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.PipelineId.Should().BeNull();
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
            new() { Title = "Renamed", Cost = 100 }
        );

        result.IsSuccess.Should().BeTrue();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Title.Should().Be("Renamed");
        row.Cost.Should().Be(100);
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
    }

    [Fact]
    public async Task Update_sets_the_on_redeem_response_text_bot_locally_without_a_helix_call()
    {
        // Response never went to Twitch (it's the bot's own chat message, not a Twitch reward field) and was
        // never applied in UpdateAsync at all — the patch was silently accepted and dropped.
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: false,
            twitchRewardId: null
        );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { Response = "Thanks for the redeem, {{user}}!" }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Should().Be("Thanks for the redeem, {{user}}!");
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Response.Should().Be("Thanks for the redeem, {{user}}!");
        await points
            .DidNotReceiveWithAnyArgs()
            .UpdateCustomRewardAsync(default, default!, default!);
    }

    [Fact]
    public async Task Update_with_no_response_field_leaves_the_existing_response_text_unchanged()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: false,
            twitchRewardId: null
        );
        (await db.Rewards.SingleAsync(r => r.Id == RewardId)).Response = "Original message";
        await db.SaveChangesAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { Title = "Renamed" }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Response.Should().Be("Original message");
    }

    // Twitch's own view of a reward whose three limits are all on (10 per stream, 2 per user, 30 s cooldown).
    // The stub applies an incoming PATCH the way Helix does: a field left out stays as it is, an explicit
    // is_*_enabled flag (true or false) wins.
    private static void TwitchHoldsLimitsAndAppliesPatches(ITwitchChannelPointsApi points)
    {
        points
            .UpdateCustomRewardAsync(
                Channel,
                "tw-reward-1",
                Arg.Any<UpdateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                UpdateCustomRewardRequest r = call.Arg<UpdateCustomRewardRequest>();
                return Result.Success(
                    TwitchReward() with
                    {
                        Cost = r.Cost ?? 500,
                        MaxPerStreamSetting = new(
                            r.IsMaxPerStreamEnabled ?? true,
                            r.MaxPerStream ?? 10
                        ),
                        MaxPerUserPerStreamSetting = new(
                            r.IsMaxPerUserPerStreamEnabled ?? true,
                            r.MaxPerUserPerStream ?? 2
                        ),
                        GlobalCooldownSetting = new(
                            r.IsGlobalCooldownEnabled ?? true,
                            r.GlobalCooldownSeconds ?? 30
                        ),
                    }
                );
            });
    }

    private static async Task<(
        RewardService Sut,
        AuthDbContext Db,
        ITwitchChannelPointsApi Points
    )> BuildWithLimitsAsync()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) = Build(
            manageable: true,
            twitchRewardId: "tw-reward-1"
        );
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.MaxPerStream = 10;
        row.MaxPerUserPerStream = 2;
        row.GlobalCooldownSeconds = 30;
        await db.SaveChangesAsync();
        TwitchHoldsLimitsAndAppliesPatches(points);
        return (sut, db, points);
    }

    [Fact]
    public async Task Update_that_leaves_the_limits_out_keeps_all_three_on_twitch_and_locally()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) =
            await BuildWithLimitsAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { Cost = 600 }
        );

        result.IsSuccess.Should().BeTrue();
        UpdateCustomRewardRequest sent = points
            .ReceivedCalls()
            .Select(c => c.GetArguments().OfType<UpdateCustomRewardRequest>().SingleOrDefault())
            .Single(r => r is not null)!;
        sent.Cost.Should().Be(600);
        // Left out of the patch means left out of the Helix body (null is dropped by the transport), never false.
        sent.IsMaxPerStreamEnabled.Should().BeNull();
        sent.IsMaxPerUserPerStreamEnabled.Should().BeNull();
        sent.IsGlobalCooldownEnabled.Should().BeNull();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.Cost.Should().Be(600);
        row.MaxPerStream.Should().Be(10);
        row.MaxPerUserPerStream.Should().Be(2);
        row.GlobalCooldownSeconds.Should().Be(30);
        result.Value.MaxPerStream.Should().Be(10);
        result.Value.MaxPerUserPerStream.Should().Be(2);
        result.Value.GlobalCooldownSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Update_with_zero_turns_that_one_limit_off_and_leaves_the_others_on()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) =
            await BuildWithLimitsAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { MaxPerStream = 0 }
        );

        result.IsSuccess.Should().BeTrue();
        UpdateCustomRewardRequest sent = points
            .ReceivedCalls()
            .Select(c => c.GetArguments().OfType<UpdateCustomRewardRequest>().SingleOrDefault())
            .Single(r => r is not null)!;
        sent.IsMaxPerStreamEnabled.Should().BeFalse();
        sent.IsMaxPerUserPerStreamEnabled.Should().BeNull();
        sent.IsGlobalCooldownEnabled.Should().BeNull();
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.MaxPerStream.Should().BeNull();
        row.MaxPerUserPerStream.Should().Be(2);
        row.GlobalCooldownSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Update_with_a_value_sets_that_limit_and_turns_it_on()
    {
        (RewardService sut, AuthDbContext db, ITwitchChannelPointsApi points) =
            await BuildWithLimitsAsync();

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { GlobalCooldownSeconds = 90 }
        );

        result.IsSuccess.Should().BeTrue();
        UpdateCustomRewardRequest sent = points
            .ReceivedCalls()
            .Select(c => c.GetArguments().OfType<UpdateCustomRewardRequest>().SingleOrDefault())
            .Single(r => r is not null)!;
        sent.IsGlobalCooldownEnabled.Should().BeTrue();
        sent.GlobalCooldownSeconds.Should().Be(90);
        Reward row = await db.Rewards.SingleAsync(r => r.Id == RewardId);
        row.GlobalCooldownSeconds.Should().Be(90);
        row.MaxPerStream.Should().Be(10);
    }
}
