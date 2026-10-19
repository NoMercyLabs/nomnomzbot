// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.AutomationApi;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves a direct binding run: a pipeline target reaches the engine with its own graph and the variables a
/// chat command would carry; a run with no human is the channel's bot identity with the broadcaster role; an
/// action target reaches the single-action runner with its type and params and hands back its result; a
/// command target is not supported yet.
/// </summary>
public sealed class CommandInvokerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f301");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000f302");
    private const string Graph = "{\"steps\":[{\"type\":\"send_message\"}]}";

    private sealed class Harness
    {
        public required CommandInvoker Invoker { get; init; }
        public required AutomationTestDbContext Db { get; init; }
        public required IPipelineEngine Engine { get; init; }
        public required IOwnerActionService Actions { get; init; }
        public required IChannelTwitchBotResolver Bots { get; init; }
    }

    private static Harness Build(PipelineOutcome outcome = PipelineOutcome.Completed)
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();
        db.Pipelines.Add(
            new PipelineEntity
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "welcome",
                IsEnabled = true,
                GraphJsonCache = Graph,
            }
        );
        db.SaveChanges();

        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = outcome,
                    Duration = TimeSpan.Zero,
                    ErrorMessage = outcome == PipelineOutcome.Failed ? "step broke" : null,
                }
            );

        IOwnerActionService actions = Substitute.For<IOwnerActionService>();
        IChannelTwitchBotResolver bots = Substitute.For<IChannelTwitchBotResolver>();
        bots.ResolveAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(new ChannelTwitchBot("bot-9", "NomNomzBotLive"));

        return new Harness
        {
            Invoker = new CommandInvoker(db, engine, actions, bots),
            Db = db,
            Engine = engine,
            Actions = actions,
            Bots = bots,
        };
    }

    private static BindingTarget PipelineTarget() =>
        new(BindingTargetKind.Pipeline, PipelineId.ToString());

    private static PipelineRequest SentToEngine(Harness h) =>
        (PipelineRequest)h.Engine.ReceivedCalls().Single().GetArguments()[0]!;

    [Fact]
    public async Task Pipeline_target_runs_its_own_graph_with_the_human_and_argument_variables()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            PipelineTarget(),
            new InvocationContext("u-1", "Viewer One", "@kitte hello", "reward", "vip")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Succeeded.Should().BeTrue();
        result.Value.Output.Should().Be("Completed");
        PipelineRequest sent = SentToEngine(h);
        sent.BroadcasterId.Should().Be(Channel);
        sent.PipelineId.Should().Be(PipelineId);
        sent.PipelineJson.Should().Be(Graph);
        sent.TriggeredByUserId.Should().Be("u-1");
        sent.TriggeredByDisplayName.Should().Be("Viewer One");
        sent.RawMessage.Should().Be("@kitte hello");
        sent.InitialVariables["trigger.source"].Should().Be("reward");
        sent.InitialVariables["user.role"].Should().Be("vip");
        sent.InitialVariables["user.id"].Should().Be("u-1");
        sent.InitialVariables["args"].Should().Be("@kitte hello");
        sent.InitialVariables["args.count"].Should().Be("2");
        sent.InitialVariables["args.1"].Should().Be("@kitte");
        sent.InitialVariables["target"].Should().Be("kitte");
        await h.Bots.DidNotReceiveWithAnyArgs().ResolveAsync(default, default);
    }

    [Fact]
    public async Task Pipeline_target_with_no_human_runs_as_the_bot_identity_with_the_broadcaster_role()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            PipelineTarget(),
            new InvocationContext(null, null, "", "timer")
        );

        result.IsSuccess.Should().BeTrue();
        PipelineRequest sent = SentToEngine(h);
        sent.TriggeredByUserId.Should().Be("bot-9");
        sent.TriggeredByDisplayName.Should().Be("NomNomzBotLive");
        sent.InitialVariables["user.role"].Should().Be("broadcaster");
        sent.InitialVariables["user.id"].Should().Be("bot-9");
        sent.InitialVariables["trigger.source"].Should().Be("timer");
        sent.InitialVariables["args.count"].Should().Be("0");
    }

    [Fact]
    public async Task Pipeline_target_for_a_viewer_still_runs_because_no_permission_gate_applies()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            PipelineTarget(),
            new InvocationContext("u-2", "Plain Viewer", "", "event")
        );

        result.IsSuccess.Should().BeTrue();
        SentToEngine(h).InitialVariables["user.role"].Should().Be("viewer");
    }

    [Fact]
    public async Task Pipeline_target_that_fails_in_the_engine_reports_the_failure_in_the_outcome()
    {
        Harness h = Build(PipelineOutcome.Failed);

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            PipelineTarget(),
            new InvocationContext(null, null, "", "schedule")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Succeeded.Should().BeFalse();
        result.Value.Error.Should().Be("step broke");
    }

    [Fact]
    public async Task Pipeline_target_of_another_channel_or_unknown_never_reaches_the_engine()
    {
        Harness h = Build();

        Result<InvocationOutcome> otherChannel = await h.Invoker.InvokeAsync(
            Guid.NewGuid(),
            PipelineTarget(),
            new InvocationContext(null, null, "", "timer")
        );
        Result<InvocationOutcome> malformed = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Pipeline, "not-a-guid"),
            new InvocationContext(null, null, "", "timer")
        );

        otherChannel.ErrorCode.Should().Be("NOT_FOUND");
        malformed.ErrorCode.Should().Be("VALIDATION_FAILED");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task Action_target_runs_the_action_with_its_params_and_returns_its_result()
    {
        Harness h = Build();
        Dictionary<string, JsonElement> parameters = new()
        {
            ["name"] = JsonSerializer.SerializeToElement("airhorn"),
        };
        h.Actions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetActionOutcome(true, "played", null, new Dictionary<string, string>())
                )
            );

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Action, null, "play_sound", parameters),
            new InvocationContext(null, null, "loud", "timer")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new InvocationOutcome(true, "played", null));
        OwnerActionRequest sent = (OwnerActionRequest)
            h.Actions.ReceivedCalls().Single().GetArguments()[0]!;
        sent.BroadcasterId.Should().Be(Channel);
        sent.ActionType.Should().Be("play_sound");
        sent.Parameters!["name"].GetString().Should().Be("airhorn");
        sent.Variables!["trigger.source"].Should().Be("timer");
        sent.Variables["user.role"].Should().Be("broadcaster");
        sent.Variables["args"].Should().Be("loud");
        sent.Caller.Should().Be("timer");
        sent.RateLimitPartition.Should().Be($"binding-action:{Channel}");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task Action_target_keeps_the_runner_failure_code()
    {
        Harness h = Build();
        h.Actions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Errors.NotFound<WidgetActionOutcome>("Action", "nope"));

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Action, null, "nope"),
            new InvocationContext(null, null, "", "timer")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Action_target_without_an_action_type_is_rejected_before_the_runner()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Action),
            new InvocationContext(null, null, "", "timer")
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await h.Actions.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    [Fact]
    public async Task Command_target_is_not_supported_yet_and_runs_nothing()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, "hug"),
            new InvocationContext(null, null, "", "reward")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_SUPPORTED");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await h.Actions.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }
}
