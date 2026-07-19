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
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the reward-state trigger source inside <see cref="RewardLifecycleHandler"/>'s update leg: a pause
/// flip fires <c>reward.paused</c>/<c>reward.resumed</c> and an enable flip fires
/// <c>reward.enabled</c>/<c>reward.disabled</c> through <see cref="IEventResponseExecutor"/> with the reward
/// variables — AFTER persisting the new state (the row is the last-known truth) — while a title/cost-only
/// update syncs the row and fires nothing.
/// </summary>
public sealed class RewardLifecycleHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c501");

    private sealed record Harness(
        RewardLifecycleHandler Handler,
        AuthDbContext Db,
        IEventResponseExecutor Executor
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .BuildServiceProvider();
        RewardLifecycleHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RewardLifecycleHandler>.Instance
        );
        return new Harness(handler, db, executor);
    }

    private static async Task SeedRewardAsync(
        AuthDbContext db,
        bool isEnabled = true,
        bool isPaused = false
    )
    {
        db.Rewards.Add(
            new Reward
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Title = "Lucky Feather",
                Cost = 500,
                TwitchRewardId = "tw-r1",
                IsEnabled = isEnabled,
                IsPaused = isPaused,
            }
        );
        await db.SaveChangesAsync();
    }

    private static RewardUpdatedEvent Update(
        bool isEnabled = true,
        bool isPaused = false,
        string title = "Lucky Feather",
        int cost = 500
    ) =>
        new()
        {
            BroadcasterId = Channel,
            TwitchRewardId = "tw-r1",
            Title = title,
            Cost = cost,
            IsEnabled = isEnabled,
            IsPaused = isPaused,
        };

    [Fact]
    public async Task Pausing_the_reward_persists_the_flag_and_fires_reward_paused()
    {
        Harness h = Build();
        await SeedRewardAsync(h.Db, isPaused: false);

        await h.Handler.HandleAsync(Update(isPaused: true));

        Reward row = await h.Db.Rewards.AsNoTracking().SingleAsync();
        row.IsPaused.Should()
            .BeTrue("the row is the last-known state the next transition compares against");
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "reward.paused",
                null,
                null,
                Arg.Is<Dictionary<string, string>>(v =>
                    v["reward"] == "Lucky Feather"
                    && v["reward.id"] == "tw-r1"
                    && v["cost"] == "500"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unpausing_the_reward_fires_reward_resumed()
    {
        Harness h = Build();
        await SeedRewardAsync(h.Db, isPaused: true);

        await h.Handler.HandleAsync(Update(isPaused: false));

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "reward.resumed",
                null,
                null,
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Disabling_and_reenabling_fire_their_transitions()
    {
        Harness h = Build();
        await SeedRewardAsync(h.Db, isEnabled: true);

        await h.Handler.HandleAsync(Update(isEnabled: false));
        await h.Handler.HandleAsync(Update(isEnabled: true));

        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "reward.disabled",
                null,
                null,
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "reward.enabled",
                null,
                null,
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_title_and_cost_only_update_syncs_the_row_and_fires_nothing()
    {
        Harness h = Build();
        await SeedRewardAsync(h.Db);

        await h.Handler.HandleAsync(Update(title: "Golden Feather", cost: 750));

        Reward row = await h.Db.Rewards.AsNoTracking().SingleAsync();
        row.Title.Should().Be("Golden Feather");
        row.Cost.Should().Be(750);
        await h
            .Executor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default, default!, default, default, default!, default);
    }

    [Fact]
    public async Task An_untracked_reward_fires_nothing()
    {
        Harness h = Build(); // no local row — no last-known state, so no honest transition exists

        await h.Handler.HandleAsync(Update(isPaused: true));

        await h
            .Executor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default, default!, default, default, default!, default);
    }
}
