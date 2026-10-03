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
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A redemption that runs a reward pipeline raises <see cref="AfterRewardProcessedEvent"/> once the
/// pipeline finished, with the verdict and the duration the engine reported, so a script can trigger on it.
/// </summary>
public sealed class RewardRedeemedHandlerAfterProcessedTests
{
    private static readonly Guid Tenant = Guid.Parse("019f5c00-5555-7000-8000-000000000001");

    private static RewardRedeemedEvent Redemption() =>
        new()
        {
            BroadcasterId = Tenant,
            RewardId = "twitch-reward-1",
            RewardTitle = "Hydrate",
            RedemptionId = "redemption-9",
            UserId = "viewer-1",
            UserDisplayName = "Alice",
            Cost = 100,
        };

    private static async Task<RecordingEventBus> RunAsync(
        Action<IPipelineEngine> arrange,
        string? response = "drink water"
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Rewards.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Tenant,
                TwitchRewardId = "twitch-reward-1",
                Title = "Hydrate",
                Response = response,
            }
        );
        await db.SaveChangesAsync();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        arrange(engine);
        RecordingEventBus bus = new();
        RewardRedeemedHandler sut = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            engine,
            bus,
            NullLogger<RewardRedeemedHandler>.Instance
        );

        await sut.HandleAsync(Redemption());
        return bus;
    }

    private static void EngineReturns(
        IPipelineEngine engine,
        PipelineOutcome outcome,
        TimeSpan duration
    ) =>
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = outcome,
                    Duration = duration,
                }
            );

    [Fact]
    public async Task A_completed_reward_pipeline_raises_After_with_the_engine_duration()
    {
        RecordingEventBus bus = await RunAsync(engine =>
            EngineReturns(engine, PipelineOutcome.Completed, TimeSpan.FromMilliseconds(1500))
        );

        AfterRewardProcessedEvent raised = bus
            .Published.OfType<AfterRewardProcessedEvent>()
            .Single();
        raised.BroadcasterId.Should().Be(Tenant);
        raised.RewardId.Should().Be("twitch-reward-1");
        raised.RedemptionId.Should().Be("redemption-9");
        raised.Succeeded.Should().BeTrue();
        raised.Duration.Should().Be(TimeSpan.FromMilliseconds(1500));
    }

    [Theory]
    [InlineData(PipelineOutcome.Failed)]
    [InlineData(PipelineOutcome.PartiallyFailed)]
    public async Task A_failed_reward_pipeline_raises_After_as_not_succeeded(
        PipelineOutcome outcome
    )
    {
        RecordingEventBus bus = await RunAsync(engine =>
            EngineReturns(engine, outcome, TimeSpan.FromSeconds(2))
        );

        bus.Published.OfType<AfterRewardProcessedEvent>().Single().Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_throwing_engine_raises_After_as_not_succeeded()
    {
        RecordingEventBus bus = await RunAsync(engine =>
            engine
                .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
                .Returns<Task<PipelineExecutionResult>>(_ =>
                    throw new InvalidOperationException("boom")
                )
        );

        AfterRewardProcessedEvent raised = bus
            .Published.OfType<AfterRewardProcessedEvent>()
            .Single();
        raised.Succeeded.Should().BeFalse();
        raised.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
