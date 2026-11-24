// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A reward with auto accept settles its own redemption once the run finishes: FULFILLED after a run that
/// worked, CANCELED (the points come back) after one that failed. A run that settled the redemption itself,
/// a reward without auto accept and a replay are left alone.
/// </summary>
public sealed class RewardRedeemedHandlerAutoAcceptTests
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

    private static async Task<IRewardService> RunAsync(
        bool autoAccept,
        Action<IPipelineEngine> arrange,
        bool replay = false
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
                Response = "drink water",
                AutoAccept = autoAccept,
            }
        );
        await db.SaveChangesAsync();
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(rewards);
        services.AddSingleton(
            Substitute.For<NomNomzBot.Application.Moderation.Services.IHeldTextService>()
        );
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        arrange(engine);
        RewardRedeemedHandler sut = new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            engine,
            new RecordingEventBus(),
            NullLogger<RewardRedeemedHandler>.Instance
        );

        if (replay)
            await sut.ReplayAsync(Redemption(), CancellationToken.None);
        else
            await sut.HandleAsync(Redemption());
        return rewards;
    }

    private static void EngineReturns(
        IPipelineEngine engine,
        PipelineOutcome outcome,
        params string[] stepActions
    ) =>
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = outcome,
                    Duration = TimeSpan.FromSeconds(1),
                    StepLogs = stepActions
                        .Select(
                            (action, index) =>
                                new StepExecutionLog
                                {
                                    StepIndex = index,
                                    ActionType = action,
                                    Succeeded = true,
                                    Duration = TimeSpan.Zero,
                                }
                        )
                        .ToList(),
                }
            );

    private static async Task AssertStatusAsync(IRewardService rewards, string status) =>
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Tenant.ToString(),
                "redemption-9",
                status,
                "twitch-reward-1",
                Arg.Any<CancellationToken>()
            );

    private static async Task AssertNoStatusAsync(IRewardService rewards) =>
        await rewards
            .DidNotReceiveWithAnyArgs()
            .SetRedemptionStatusAsync(default!, default!, default!);

    [Theory]
    [InlineData(PipelineOutcome.Completed)]
    [InlineData(PipelineOutcome.Stopped)]
    public async Task A_run_that_worked_marks_the_redemption_FULFILLED(PipelineOutcome outcome)
    {
        IRewardService rewards = await RunAsync(
            true,
            e => EngineReturns(e, outcome, "send_message")
        );

        await AssertStatusAsync(rewards, "FULFILLED");
    }

    [Theory]
    [InlineData(PipelineOutcome.Failed)]
    [InlineData(PipelineOutcome.PartiallyFailed)]
    [InlineData(PipelineOutcome.TimedOut)]
    [InlineData(PipelineOutcome.Cancelled)]
    public async Task A_run_that_failed_marks_the_redemption_CANCELED(PipelineOutcome outcome)
    {
        IRewardService rewards = await RunAsync(
            true,
            e => EngineReturns(e, outcome, "send_message")
        );

        await AssertStatusAsync(rewards, "CANCELED");
    }

    [Fact]
    public async Task A_throwing_engine_marks_the_redemption_CANCELED()
    {
        IRewardService rewards = await RunAsync(
            true,
            e =>
                e.ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
                    .Returns<Task<PipelineExecutionResult>>(_ =>
                        throw new InvalidOperationException("boom")
                    )
        );

        await AssertStatusAsync(rewards, "CANCELED");
    }

    [Theory]
    [InlineData("redemption_refund")]
    [InlineData("redemption_fulfill")]
    public async Task A_run_that_settled_the_redemption_itself_is_left_alone(string action)
    {
        IRewardService rewards = await RunAsync(
            true,
            e => EngineReturns(e, PipelineOutcome.Completed, "send_message", action)
        );

        await AssertNoStatusAsync(rewards);
    }

    [Fact]
    public async Task Without_auto_accept_no_status_is_set_after_a_successful_run()
    {
        IRewardService rewards = await RunAsync(
            false,
            e => EngineReturns(e, PipelineOutcome.Completed, "send_message")
        );

        await AssertNoStatusAsync(rewards);
    }

    [Fact]
    public async Task Without_auto_accept_no_status_is_set_after_a_failed_run()
    {
        IRewardService rewards = await RunAsync(
            false,
            e => EngineReturns(e, PipelineOutcome.Failed, "send_message")
        );

        await AssertNoStatusAsync(rewards);
    }

    [Fact]
    public async Task A_replay_never_settles_the_redemption()
    {
        IRewardService rewards = await RunAsync(
            true,
            e => EngineReturns(e, PipelineOutcome.Failed, "send_message"),
            replay: true
        );

        await AssertNoStatusAsync(rewards);
    }
}
