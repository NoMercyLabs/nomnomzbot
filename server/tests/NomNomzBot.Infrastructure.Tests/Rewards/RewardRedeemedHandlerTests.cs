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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the reward→pipeline binding (handoff qtkitte item): a reward carrying a <c>PipelineId</c> dispatches
/// that saved pipeline's compiled graph through <see cref="IPipelineEngine"/> on redemption (the path a
/// reward-triggered <c>play_sound</c> takes), while a reward with no bound pipeline / inline response falls
/// through to the generic redemption event response — unchanged behavior.
/// </summary>
public sealed class RewardRedeemedHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c401");
    private static readonly Guid BoundPipelineId = Guid.Parse(
        "0192a000-0000-7000-8000-00000000c402"
    );

    private sealed record Harness(
        RewardRedeemedHandler Handler,
        AuthDbContext Db,
        IPipelineEngine Engine,
        IEventResponseExecutor Executor
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "x1",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                }
            );
        IEventResponseExecutor executor = Substitute.For<IEventResponseExecutor>();
        IRedemptionTimerService timers = Substitute.For<IRedemptionTimerService>();

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(executor)
            .AddSingleton(timers)
            .BuildServiceProvider();

        RewardRedeemedHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            engine,
            NullLogger<RewardRedeemedHandler>.Instance
        );

        return new Harness(handler, db, engine, executor);
    }

    private static RewardRedeemedEvent Redemption(string twitchRewardId) =>
        new()
        {
            BroadcasterId = Channel,
            RewardId = twitchRewardId,
            RewardTitle = "Play a sound",
            RedemptionId = "redemption-1",
            UserId = "twitch-viewer-1",
            UserDisplayName = "Viewer",
            Cost = 100,
        };

    [Fact]
    public async Task A_reward_bound_to_a_pipeline_dispatches_that_pipelines_graph_on_redemption()
    {
        Harness h = Build();
        const string graph = """{"actions":[{"type":"play_sound","clip":"airhorn"}]}""";
        h.Db.Pipelines.Add(
            new Pipeline
            {
                Id = BoundPipelineId,
                BroadcasterId = Channel,
                Name = "airhorn",
                TriggerKind = "reward",
                GraphJsonCache = graph,
            }
        );
        h.Db.Rewards.Add(
            new Reward
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Title = "Play a sound",
                TwitchRewardId = "tw-reward-1",
                PipelineId = BoundPipelineId,
            }
        );
        await h.Db.SaveChangesAsync();

        await h.Handler.HandleAsync(Redemption("tw-reward-1"));

        // The consequence: the bound pipeline's compiled graph runs, attributed to the redeeming viewer —
        // not the inline-response / generic fallbacks.
        await h
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Channel
                    && r.PipelineJson == graph
                    && r.RewardId == "tw-reward-1"
                    && r.RedemptionId == "redemption-1"
                    && r.TriggeredByUserId == "twitch-viewer-1"
                ),
                Arg.Any<CancellationToken>()
            );
        await h
            .Executor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default, default!, default, default, default!, default);
    }

    [Fact]
    public async Task A_reward_with_no_bound_pipeline_falls_through_to_the_generic_redemption_response()
    {
        Harness h = Build();
        h.Db.Rewards.Add(
            new Reward
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Title = "Just points",
                TwitchRewardId = "tw-reward-2",
            }
        );
        await h.Db.SaveChangesAsync();

        await h.Handler.HandleAsync(Redemption("tw-reward-2"));

        // No PipelineId / PipelineJson / Response → the shared executor runs the generic redemption event
        // response, keyed on the redemption topic; the pipeline engine is never invoked.
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await h
            .Executor.Received(1)
            .ExecuteAsync(
                Channel,
                "channel.channel_points_custom_reward_redemption.add",
                "twitch-viewer-1",
                "Viewer",
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }
}
