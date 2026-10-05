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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A pause the bot makes itself (a script's <c>reward.update isPaused</c>, a dashboard patch) fires
/// <c>reward.paused</c> / <c>reward.resumed</c> exactly once. <see cref="RewardService.UpdateAsync"/> writes
/// <c>IsPaused</c> locally, so the Twitch echo that reaches <see cref="RewardLifecycleHandler"/> sees no
/// change and must not fire it a second time.
/// </summary>
public sealed class RewardServicePauseEventTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d201");
    private static readonly Guid RewardId = Guid.Parse("0192a000-0000-7000-8000-00000000d202");

    private sealed record Harness(
        RewardService Service,
        RewardLifecycleHandler Echo,
        AuthDbContext Db,
        IEventResponseExecutor Executor
    );

    private static Harness Build(bool isPaused)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Rewards.Add(
            new()
            {
                Id = RewardId,
                BroadcasterId = Channel,
                Title = "Lucky Feather",
                Cost = 500,
                IsEnabled = true,
                IsManageable = true,
                IsPaused = isPaused,
                TwitchRewardId = "tw-r1",
            }
        );
        db.SaveChanges();

        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();
        RewardService service = NewService(db, provider);
        RewardLifecycleHandler echo = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RewardLifecycleHandler>.Instance
        );
        return new(service, echo, db, executor);
    }

    private static RewardService NewService(AuthDbContext db, IServiceProvider provider)
    {
        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        points
            .UpdateCustomRewardAsync(
                Channel,
                "tw-r1",
                Arg.Any<UpdateCustomRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(HelixReward()));
        return new(db, points, TimeProvider.System, NullLogger<RewardService>.Instance, provider);
    }

    private static TwitchCustomReward HelixReward() =>
        new(
            BroadcasterId: "tw-channel",
            BroadcasterLogin: "stoney",
            BroadcasterName: "Stoney",
            Id: "tw-r1",
            Title: "Lucky Feather",
            Prompt: "steal the feather",
            Cost: 500,
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

    private static Task<Result<RewardDetail>> Patch(
        Harness h,
        bool? isPaused,
        string? title = null
    ) =>
        h.Service.UpdateAsync(
            Channel.ToString(),
            RewardId.ToString(),
            new() { IsPaused = isPaused, Title = title }
        );

    private static RewardUpdatedEvent TwitchEcho(bool isPaused) =>
        new()
        {
            BroadcasterId = Channel,
            TwitchRewardId = "tw-r1",
            Title = "Lucky Feather",
            Cost = 500,
            IsEnabled = true,
            IsPaused = isPaused,
        };

    private static async Task AssertFiredOnce(Harness h, string eventType)
    {
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                eventType,
                null,
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v["reward"] == "Lucky Feather" && v["cost"] == "500"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_script_driven_pause_stores_the_flag_and_fires_reward_paused_once()
    {
        Harness h = Build(isPaused: false);

        Result<RewardDetail> result = await Patch(h, isPaused: true);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.Rewards.AsNoTracking().SingleAsync()).IsPaused.Should().BeTrue();
        await AssertFiredOnce(h, "reward.paused");
        h.Executor.ReceivedCalls().Should().HaveCount(1);
    }

    [Fact]
    public async Task A_script_driven_resume_stores_the_flag_and_fires_reward_resumed_once()
    {
        Harness h = Build(isPaused: true);

        Result<RewardDetail> result = await Patch(h, isPaused: false);

        result.IsSuccess.Should().BeTrue();
        (await h.Db.Rewards.AsNoTracking().SingleAsync()).IsPaused.Should().BeFalse();
        await AssertFiredOnce(h, "reward.resumed");
        h.Executor.ReceivedCalls().Should().HaveCount(1);
    }

    [Fact]
    public async Task The_twitch_echo_after_a_bot_pause_does_not_fire_the_transition_a_second_time()
    {
        Harness h = Build(isPaused: false);
        await Patch(h, isPaused: true);

        await h.Echo.HandleAsync(TwitchEcho(isPaused: true));

        await AssertFiredOnce(h, "reward.paused");
        h.Executor.ReceivedCalls().Should().HaveCount(1);
    }

    [Fact]
    public async Task A_patch_that_leaves_the_pause_flag_alone_fires_nothing()
    {
        Harness h = Build(isPaused: false);

        await Patch(h, isPaused: null, title: "Golden Feather");
        await Patch(h, isPaused: false);

        h.Executor.ReceivedCalls().Should().BeEmpty();
    }
}
