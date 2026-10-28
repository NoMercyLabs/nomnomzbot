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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A scheduled one-shot task binds a command (or action) directly: scheduling validates and persists the target
/// through the shared binder, the sweep runs it through <see cref="ICommandInvoker"/> with no wrapper pipeline,
/// and the command guard lists a pending task that still runs the command.
/// </summary>
public sealed class ScheduledPipelineDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f201");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f202");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000f203");
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static Command NewCommand(Guid id, Guid channel, string name) =>
        new()
        {
            Id = id,
            BroadcasterId = channel,
            Name = name,
            NameNormalized = name,
            Tier = "template",
            TemplateResponse = "drink water",
        };

    private static (
        ScheduledPipelineService Sut,
        ScheduledPipelineDispatcher Dispatcher,
        AuthDbContext Db,
        FakeTimeProvider Clock,
        IPipelineEngine Engine,
        ICommandInvoker Invoker
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
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
            .AddSingleton(engine)
            .AddSingleton(invoker)
            .BuildServiceProvider();
        ScheduledPipelineDispatcher dispatcher = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledPipelineDispatcher>.Instance
        );
        FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = new(
            db,
            dispatcher,
            clock,
            NullLogger<ScheduledPipelineService>.Instance
        );
        return (sut, dispatcher, db, clock, engine, invoker);
    }

    private static Task<Result<ScheduledPipelineTaskDto>> ScheduleAsync(
        ScheduledPipelineService sut,
        BindingTarget target
    ) =>
        sut.ScheduleTargetAsync(
            Channel,
            target,
            30,
            new Dictionary<string, string> { ["note"] = "hello" },
            "twitch-viewer-1",
            "Viewer"
        );

    [Fact]
    public async Task A_command_target_persists_as_a_command_binding_with_no_pipeline()
    {
        (ScheduledPipelineService sut, _, AuthDbContext db, _, _, _) = Build();
        db.Commands.Add(NewCommand(CommandId, Channel, "hydrate"));
        await db.SaveChangesAsync();
        BindingTarget target = new(BindingTargetKind.Command, CommandId.ToString());

        Result<ScheduledPipelineTaskDto> result = await ScheduleAsync(sut, target);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Target.Should().Be(target);
        ScheduledPipelineTask stored = await db.ScheduledPipelineTasks.SingleAsync();
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(CommandId);
        stored.PipelineId.Should().Be(Guid.Empty);
        stored.Status.Should().Be(ScheduledPipelineTaskStatus.Pending);
        stored.DueAt.Should().Be(Start.AddSeconds(30));
    }

    [Fact]
    public async Task A_command_of_another_channel_is_refused_and_nothing_is_stored()
    {
        (ScheduledPipelineService sut, _, AuthDbContext db, _, _, _) = Build();
        db.Commands.Add(NewCommand(CommandId, OtherChannel, "elsewhere"));
        await db.SaveChangesAsync();

        Result<ScheduledPipelineTaskDto> result = await ScheduleAsync(
            sut,
            new(BindingTargetKind.Command, CommandId.ToString())
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_due_command_task_runs_through_the_invoker_as_scheduled_and_never_the_engine()
    {
        (
            ScheduledPipelineService sut,
            ScheduledPipelineDispatcher dispatcher,
            AuthDbContext db,
            FakeTimeProvider clock,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = Build();
        db.Commands.Add(NewCommand(CommandId, Channel, "hydrate"));
        await db.SaveChangesAsync();
        await ScheduleAsync(sut, new(BindingTargetKind.Command, CommandId.ToString()));
        clock.Advance(TimeSpan.FromSeconds(31));

        int fired = await sut.FireDueAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await dispatcher.DrainAsync(timeout.Token);

        fired.Should().Be(1);
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
                    && c.TriggerSource == "scheduled"
                    && c.Variables != null
                    && c.Variables["note"] == "hello"
                ),
                Arg.Any<CancellationToken>()
            );
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Fired);
    }

    [Fact]
    public async Task A_due_pipeline_task_still_runs_the_engine_and_never_the_invoker()
    {
        (
            ScheduledPipelineService sut,
            ScheduledPipelineDispatcher dispatcher,
            AuthDbContext db,
            FakeTimeProvider clock,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = Build();
        Guid pipelineId = Guid.CreateVersion7();
        db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = Channel,
                Name = "airhorn",
                IsEnabled = true,
                TriggerKind = "manual",
            }
        );
        await db.SaveChangesAsync();
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
        Result<ScheduledPipelineTaskDto> scheduled = await ScheduleAsync(
            sut,
            new(BindingTargetKind.Pipeline, pipelineId.ToString())
        );
        clock.Advance(TimeSpan.FromSeconds(31));

        await sut.FireDueAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await dispatcher.DrainAsync(timeout.Token);

        scheduled.Value!.PipelineId.Should().Be(pipelineId);
        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.PipelineId == pipelineId),
                Arg.Any<CancellationToken>()
            );
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Deleting_a_command_a_pending_task_runs_is_refused_and_lists_the_scheduled_task()
    {
        (ScheduledPipelineService sut, _, AuthDbContext db, _, _, _) = Build();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "scheduled-binding",
                NameNormalized = "scheduled-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new ScheduledTaskBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;
        Result<ScheduledPipelineTaskDto> scheduled = await ScheduleAsync(
            sut,
            new(BindingTargetKind.Command, created.Id.ToString())
        );

        Result result = await commands.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .ContainSingle()
            .Which.Should()
            .Match<BindingUsage>(u =>
                u.Surface == "scheduled-task" && u.Id == scheduled.Value!.Id.ToString()
            );
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
    }
}
