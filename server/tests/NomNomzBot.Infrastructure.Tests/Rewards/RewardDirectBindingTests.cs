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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A reward binds a command (or action) directly: redeeming runs it through <see cref="ICommandInvoker"/> with
/// no wrapper pipeline, a pipeline-bound reward keeps its old path, the command guard lists the reward, and the
/// create/update request refuses a command that is not in the channel.
/// </summary>
public sealed class RewardDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f102");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000f103");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000f104");

    private static RewardRedeemedEvent Redemption() =>
        new()
        {
            BroadcasterId = Channel,
            RewardId = "tw-reward-1",
            RewardTitle = "Hydrate",
            RedemptionId = "redemption-1",
            UserId = "twitch-viewer-1",
            UserDisplayName = "Viewer",
            UserInput = "two glasses",
            Cost = 100,
        };

    private static (
        RewardRedeemedHandler Handler,
        AuthDbContext Db,
        IPipelineEngine Engine,
        ICommandInvoker Invoker
    ) BuildHandler()
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
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, "drink water", null)));

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(invoker)
            .AddSingleton(Substitute.For<IEventResponseExecutor>())
            .AddSingleton(Substitute.For<IRedemptionTimerService>())
            .BuildServiceProvider();

        RewardRedeemedHandler handler = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            engine,
            new RecordingEventBus(),
            NullLogger<RewardRedeemedHandler>.Instance
        );
        return (handler, db, engine, invoker);
    }

    [Fact]
    public async Task A_reward_bound_to_a_command_runs_that_command_with_no_pipeline()
    {
        (
            RewardRedeemedHandler handler,
            AuthDbContext db,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = BuildHandler();
        Reward reward = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Title = "Hydrate",
            TwitchRewardId = "tw-reward-1",
        };
        reward.SetBindingTarget(new(BindingTargetKind.Command, CommandId.ToString()));
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        await handler.HandleAsync(Redemption());

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == CommandId.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "twitch-viewer-1"
                    && c.DisplayName == "Viewer"
                    && c.Args == "two glasses"
                    && c.TriggerSource == "reward"
                ),
                Arg.Any<CancellationToken>()
            );
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        (await db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_reward_bound_to_a_pipeline_still_runs_the_pipeline_and_never_the_invoker()
    {
        (
            RewardRedeemedHandler handler,
            AuthDbContext db,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = BuildHandler();
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "airhorn",
                TriggerKind = "reward",
                GraphJsonCache = """{"actions":[{"type":"play_sound","clip":"airhorn"}]}""",
            }
        );
        Reward reward = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Title = "Hydrate",
            TwitchRewardId = "tw-reward-1",
        };
        reward.SetBindingTarget(new(BindingTargetKind.Pipeline, PipelineId.ToString()));
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        await handler.HandleAsync(Redemption());

        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.PipelineId == PipelineId),
                Arg.Any<CancellationToken>()
            );
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Deleting_a_command_a_reward_runs_is_refused_and_lists_the_reward()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "reward-binding",
                NameNormalized = "reward-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new RewardBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;
        Reward bound = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Title = "Hydrate",
        };
        bound.SetBindingTarget(new(BindingTargetKind.Command, created.Id.ToString()));
        db.Rewards.Add(bound);
        db.Rewards.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Title = "Unrelated",
            }
        );
        await db.SaveChangesAsync();

        Result result = await commands.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .Equal(new BindingUsage("reward", bound.Id.ToString(), "Hydrate"));
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Update_with_a_command_of_another_channel_is_refused_and_the_binding_stays()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid rewardId = Guid.CreateVersion7();
        Guid foreignCommand = Guid.CreateVersion7();
        db.Rewards.Add(
            new()
            {
                Id = rewardId,
                BroadcasterId = Channel,
                Title = "Hydrate",
            }
        );
        db.Commands.Add(
            new()
            {
                Id = foreignCommand,
                BroadcasterId = OtherChannel,
                Name = "elsewhere",
                NameNormalized = "elsewhere",
                Tier = "template",
                TemplateResponse = "hi",
            }
        );
        await db.SaveChangesAsync();
        RewardService sut = new(
            db,
            Substitute.For<ITwitchChannelPointsApi>(),
            TimeProvider.System,
            NullLogger<RewardService>.Instance,
            RewardServiceTestProvider.Create()
        );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            rewardId.ToString(),
            new() { Target = new(BindingTargetKind.Command, foreignCommand.ToString()) }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Rewards.SingleAsync(r => r.Id == rewardId)).TargetCommandId.Should().BeNull();
    }

    [Fact]
    public async Task Update_with_a_command_of_this_channel_persists_it_and_the_detail_shows_the_target()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid rewardId = Guid.CreateVersion7();
        db.Rewards.Add(
            new()
            {
                Id = rewardId,
                BroadcasterId = Channel,
                Title = "Hydrate",
            }
        );
        db.Commands.Add(
            new()
            {
                Id = CommandId,
                BroadcasterId = Channel,
                Name = "hydrate",
                NameNormalized = "hydrate",
                Tier = "template",
                TemplateResponse = "drink water",
            }
        );
        await db.SaveChangesAsync();
        RewardService sut = new(
            db,
            Substitute.For<ITwitchChannelPointsApi>(),
            TimeProvider.System,
            NullLogger<RewardService>.Instance,
            RewardServiceTestProvider.Create()
        );

        Result<RewardDetail> result = await sut.UpdateAsync(
            Channel.ToString(),
            rewardId.ToString(),
            new() { Target = new(BindingTargetKind.Command, CommandId.ToString()) }
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value!.Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandId.ToString()));
        Reward stored = await db.Rewards.SingleAsync(r => r.Id == rewardId);
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(CommandId);
        stored.PipelineId.Should().BeNull();
    }
}
