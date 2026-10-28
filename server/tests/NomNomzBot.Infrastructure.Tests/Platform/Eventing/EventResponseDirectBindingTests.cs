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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Chat;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// An event response binds a command (or action) directly: the executor runs it through
/// <see cref="ICommandInvoker"/> with no wrapper pipeline, a pipeline-bound row keeps its old path, the command
/// guard lists the event response, and the save refuses a command that is not in the channel.
/// </summary>
public sealed class EventResponseDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e102");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000e103");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000e104");

    private static (
        EventResponseExecutor Executor,
        AuthDbContext Db,
        IPipelineEngine Engine,
        ICommandInvoker Invoker
    ) BuildExecutor()
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
            .Returns(Result.Success(new InvocationOutcome(true, "hello", null)));
        EventResponseExecutor executor = new(
            db,
            engine,
            Substitute.For<ITemplateResolver>(),
            TestReplyDispatcher.OverProvider(Substitute.For<IChatProvider>()),
            new OutboundSanctionAccessor(),
            Substitute.For<IEventResponseOverlayNotifier>(),
            Substitute.For<ITtsDispatchService>(),
            invoker,
            NullLogger<EventResponseExecutor>.Instance
        );
        return (executor, db, engine, invoker);
    }

    private static Dictionary<string, string> Variables() =>
        new(StringComparer.OrdinalIgnoreCase) { ["user"] = "Raider" };

    [Fact]
    public async Task A_row_bound_to_a_command_runs_that_command_with_no_pipeline()
    {
        (
            EventResponseExecutor executor,
            AuthDbContext db,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = BuildExecutor();
        EventResponse row = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            EventType = "channel.raid",
            ResponseType = "pipeline",
            IsEnabled = true,
            FollowsPlatformDefault = false,
        };
        row.SetBindingTarget(new(BindingTargetKind.Command, CommandId.ToString()));
        db.EventResponses.Add(row);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(Channel, "channel.raid", "viewer-1", "Raider", Variables());

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == CommandId.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "viewer-1"
                    && c.DisplayName == "Raider"
                    && c.TriggerSource == "event"
                    && c.Variables != null
                    && c.Variables["event.name"] == "channel.raid"
                    && c.Variables["user"] == "Raider"
                ),
                Arg.Any<CancellationToken>()
            );
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        (await db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_row_bound_to_a_pipeline_still_runs_the_pipeline_and_never_the_invoker()
    {
        (
            EventResponseExecutor executor,
            AuthDbContext db,
            IPipelineEngine engine,
            ICommandInvoker invoker
        ) = BuildExecutor();
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
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "airhorn",
                TriggerKind = "event",
                IsEnabled = true,
                GraphJsonCache = """{"actions":[{"type":"play_sound","clip":"airhorn"}]}""",
            }
        );
        EventResponse row = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            EventType = "channel.raid",
            ResponseType = "pipeline",
            IsEnabled = true,
            FollowsPlatformDefault = false,
        };
        row.SetBindingTarget(new(BindingTargetKind.Pipeline, PipelineId.ToString()));
        db.EventResponses.Add(row);
        await db.SaveChangesAsync();

        await executor.ExecuteAsync(Channel, "channel.raid", "viewer-1", "Raider", Variables());

        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.PipelineId == PipelineId),
                Arg.Any<CancellationToken>()
            );
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Deleting_a_command_an_event_response_runs_is_refused_and_lists_the_event_response()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "event-binding",
                NameNormalized = "event-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new EventResponseBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "welcome", TemplateResponse = "hi raiders" }
            )
        ).Value;
        EventResponse bound = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            EventType = "channel.raid",
            ResponseType = "pipeline",
            FollowsPlatformDefault = false,
        };
        bound.SetBindingTarget(new(BindingTargetKind.Command, created.Id.ToString()));
        db.EventResponses.Add(bound);
        db.EventResponses.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                EventType = "channel.follow",
                ResponseType = "chat_message",
                FollowsPlatformDefault = false,
            }
        );
        await db.SaveChangesAsync();

        Result result = await commands.DeleteAsync(Channel.ToString(), "welcome", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .Equal(new BindingUsage("event-response", bound.Id.ToString(), "channel.raid"));
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
    }

    private static EventResponseService BuildService(AuthDbContext db) =>
        new(db, new RecordingEventBus(), new TemplateHelperValidator());

    [Fact]
    public async Task Upsert_with_a_command_of_another_channel_is_refused_and_nothing_is_stored()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid foreignCommand = Guid.CreateVersion7();
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

        Result<EventResponseDto> result = await BuildService(db)
            .UpsertAsync(
                Channel.ToString(),
                "channel.raid",
                new() { Target = new(BindingTargetKind.Command, foreignCommand.ToString()) }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.EventResponses.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Upsert_with_a_command_of_this_channel_persists_it_and_the_dto_shows_the_target()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Commands.Add(
            new()
            {
                Id = CommandId,
                BroadcasterId = Channel,
                Name = "welcome",
                NameNormalized = "welcome",
                Tier = "template",
                TemplateResponse = "hi raiders",
            }
        );
        await db.SaveChangesAsync();

        Result<EventResponseDto> result = await BuildService(db)
            .UpsertAsync(
                Channel.ToString(),
                "channel.raid",
                new() { Target = new(BindingTargetKind.Command, CommandId.ToString()) }
            );

        result.IsSuccess.Should().BeTrue();
        result.Value!.ResponseType.Should().Be("pipeline");
        result
            .Value.Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandId.ToString()));
        EventResponse stored = await db.EventResponses.SingleAsync();
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(CommandId);
        stored.PipelineId.Should().BeNull();
    }
}
