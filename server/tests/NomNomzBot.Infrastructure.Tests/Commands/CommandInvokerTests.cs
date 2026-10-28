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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.AutomationApi;
using NomNomzBot.Infrastructure.Tests.Chat;
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
        public required IChatProvider Chat { get; init; }
        public required ITemplateResolver Templates { get; init; }
        public required IBuiltinCommandCatalog Catalog { get; init; }
        public required RecordingChatOverlayLineSender Overlay { get; init; }
        public required IObsBrowserSourceRecoveryScheduler Recovery { get; init; }
    }

    private static Guid AddCommand(
        Harness h,
        string tier,
        string[]? responses = null,
        bool tts = false,
        Guid? pipelineId = null,
        Guid? channel = null,
        bool enabled = true,
        ReplyDelivery? delivery = null
    )
    {
        Guid id = Guid.NewGuid();
        h.Db.Commands.Add(
            new Command
            {
                Id = id,
                BroadcasterId = channel ?? Channel,
                Name = "hug",
                NameNormalized = "hug",
                Tier = tier,
                TemplateResponses = responses?.ToList(),
                SpeakWithTts = tts,
                ReplyDelivery = delivery,
                PipelineId = pipelineId,
                IsEnabled = enabled,
                CooldownSeconds = 60,
                UserCooldownSeconds = 60,
                MinPermissionLevel = 100,
            }
        );
        h.Db.SaveChanges();
        return id;
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

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => "resolved:" + call.Arg<string>());
        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

        RecordingChatOverlayLineSender overlay = new();
        IObsBrowserSourceRecoveryScheduler recovery =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();

        return new Harness
        {
            Overlay = overlay,
            Recovery = recovery,
            Invoker = new CommandInvoker(
                db,
                engine,
                actions,
                bots,
                templates,
                chat,
                TestReplyDispatcher.Over(
                    Substitute.For<IInboundOriginChatSender>(),
                    overlay,
                    chat,
                    recovery,
                    bots
                ),
                catalog,
                new OutboundSanctionAccessor(),
                Substitute.For<IScriptRunner>()
            ),
            Catalog = catalog,
            Db = db,
            Chat = chat,
            Templates = templates,
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
        result.Value.Succeeded.Should().BeTrue();
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
        await h.Bots.DidNotReceiveWithAnyArgs().ResolveAsync(default);
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
        result.Value.Succeeded.Should().BeFalse();
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
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
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
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Fact]
    public async Task Action_target_without_an_outer_sanction_runs_under_the_channels_action_basis()
    {
        Harness h = Build();
        OutboundSanction? seen = null;
        h.Actions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                seen = new OutboundSanctionAccessor().Current;
                return Result.Success(
                    new WidgetActionOutcome(true, null, null, new Dictionary<string, string>())
                );
            });

        await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Action, null, "music_play"),
            new InvocationContext(null, null, "", "timer")
        );

        seen.Should().Be(OutboundSanction.ChannelConfiguration("action:music_play"));
        new OutboundSanctionAccessor().Current.Should().BeNull("the claim ends with the run");
    }

    [Fact]
    public async Task Action_target_keeps_the_outer_sanction_when_one_is_already_in_force()
    {
        Harness h = Build();
        OutboundSanction? seen = null;
        h.Actions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                seen = new OutboundSanctionAccessor().Current;
                return Result.Success(
                    new WidgetActionOutcome(true, null, null, new Dictionary<string, string>())
                );
            });
        OutboundSanction outer = OutboundSanction.UserAction("music:control:write", Guid.NewGuid());

        using (new OutboundSanctionAccessor().Begin(outer))
            await h.Invoker.InvokeAsync(
                Channel,
                new BindingTarget(BindingTargetKind.Action, null, "music_play"),
                new InvocationContext(null, null, "", "automation")
            );

        seen.Should().Be(outer);
    }

    [Fact]
    public async Task Action_target_receives_the_surface_variables_but_never_lets_them_forge_identity()
    {
        Harness h = Build();
        h.Actions.RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetActionOutcome(true, null, null, new Dictionary<string, string>())
                )
            );

        await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Action, null, "obs_switch_scene"),
            new InvocationContext(
                null,
                null,
                "",
                "automation",
                Variables: new Dictionary<string, string>
                {
                    ["scene"] = "BRB",
                    ["user.role"] = "viewer",
                    ["trigger.source"] = "forged",
                }
            )
        );

        OwnerActionRequest sent = (OwnerActionRequest)
            h.Actions.ReceivedCalls().Single().GetArguments()[0]!;
        sent.Variables!["scene"].Should().Be("BRB", "the key's own setting reaches the action");
        sent.Variables["user.role"].Should().Be("broadcaster", "identity is never caller-supplied");
        sent.Variables["trigger.source"].Should().Be("automation");
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
        await h.Actions.DidNotReceiveWithAnyArgs().RunAsync(default!);
    }

    private static IBuiltinCommand AddBuiltin(Harness h, string key, Result<string> reply)
    {
        IBuiltinCommand builtin = Substitute.For<IBuiltinCommand>();
        builtin.BuiltinKey.Returns(key);
        builtin.DefaultMinPermissionLevel.Returns(40);
        builtin.DefaultCooldownSeconds.Returns(30);
        builtin
            .ExecuteAsync(Arg.Any<BuiltinCommandContext>(), Arg.Any<CancellationToken>())
            .Returns(reply);
        h.Catalog.Get(key).Returns(builtin);
        return builtin;
    }

    private static BuiltinCommandContext RanWith(IBuiltinCommand builtin) =>
        (BuiltinCommandContext)
            builtin
                .ReceivedCalls()
                .First(call => call.GetMethodInfo().Name == nameof(IBuiltinCommand.ExecuteAsync))
                .GetArguments()[0]!;

    [Fact]
    public async Task Built_in_target_runs_with_the_bot_identity_and_args_and_delivers_the_output()
    {
        Harness h = Build();
        IBuiltinCommand builtin = AddBuiltin(h, "followage", Result.Success("kitte follows 3y"));

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, "followage"),
            new InvocationContext(null, null, "@kitte", "reward")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeTrue();
        result.Value.Output.Should().Be("kitte follows 3y");
        BuiltinCommandContext ran = RanWith(builtin);
        ran.BroadcasterId.Should().Be(Channel);
        ran.TriggeringUserId.Should().Be("bot-9");
        ran.TriggeringUserDisplayName.Should().Be("NomNomzBotLive");
        ran.RoleLevel.Should().Be(40);
        ran.Args.Should().Be("@kitte");
        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "kitte follows 3y", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Built_in_target_with_a_human_keeps_that_user_and_role_and_skips_floor_and_cooldown()
    {
        Harness h = Build();
        IBuiltinCommand builtin = AddBuiltin(h, "uptime", Result.Success("up 2h"));
        InvocationContext context = new("u-1", "Viewer One", "", "reward", "vip");
        BindingTarget target = new(BindingTargetKind.Command, "uptime");

        Result<InvocationOutcome> first = await h.Invoker.InvokeAsync(Channel, target, context);
        Result<InvocationOutcome> second = await h.Invoker.InvokeAsync(Channel, target, context);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        await builtin
            .Received(2)
            .ExecuteAsync(Arg.Any<BuiltinCommandContext>(), Arg.Any<CancellationToken>());
        BuiltinCommandContext ran = RanWith(builtin);
        ran.TriggeringUserId.Should().Be("u-1");
        ran.RoleLevel.Should().BeLessThan(40);
        await h.Bots.DidNotReceiveWithAnyArgs().ResolveAsync(default);
    }

    [Fact]
    public async Task Unknown_built_in_is_not_found_and_sends_nothing()
    {
        Harness h = Build();

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, "nosuchbuiltin"),
            new InvocationContext(null, null, "", "reward")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task Built_in_that_fails_returns_its_error_and_sends_nothing()
    {
        Harness h = Build();
        AddBuiltin(h, "so", Result.Failure<string>("no such streamer", "NOT_FOUND"));

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, "so"),
            new InvocationContext(null, null, "x", "reward")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("no such streamer");
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task Built_in_command_tier_is_not_supported_yet_and_runs_nothing()
    {
        Harness h = Build();
        Guid id = AddCommand(h, "builtin");

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext(null, null, "", "reward")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_SUPPORTED");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task Template_command_sends_the_resolved_reply_as_the_bot_and_requests_no_tts_when_off()
    {
        Harness h = Build();
        Guid id = AddCommand(h, "template", ["hug {user}"]);

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext(null, null, "@kitte", "reward")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeTrue();
        result.Value.Output.Should().Be("resolved:hug {user}");
        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "resolved:hug {user}", Arg.Any<CancellationToken>());
        IDictionary<string, string> seed =
            (IDictionary<string, string>)h.Templates.ReceivedCalls().Single().GetArguments()[1]!;
        seed["user"].Should().Be("NomNomzBotLive");
        seed["user.name"].Should().Be("nomnomzbotlive");
        seed["user.role"].Should().Be("broadcaster");
        seed["target"].Should().Be("kitte");
        seed["trigger.source"].Should().Be("reward");
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Fact]
    public async Task Template_command_with_tts_queues_the_reply_under_the_bot_identity_not_the_invoker()
    {
        Harness h = Build();
        Guid id = AddCommand(h, "template", ["hello"], tts: true);

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext("u-1", "Viewer One", "", "reward", "viewer")
        );

        result.Value.Succeeded.Should().BeTrue();
        PipelineRequest tts = SentToEngine(h);
        tts.InitialVariables["tts.reply"].Should().Be("resolved:hello");
        tts.TriggeredByUserId.Should().Be("bot-9");
        tts.TriggeredByDisplayName.Should().Be("NomNomzBotLive");
    }

    [Fact]
    public async Task Template_command_whose_send_fails_reports_failure_and_queues_no_tts()
    {
        Harness h = Build();
        Guid id = AddCommand(h, "template", ["hello"], tts: true);
        h.Chat.SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext(null, null, "", "reward")
        );

        result.Value.Succeeded.Should().BeFalse();
        result.Value.Error.Should().NotBeNullOrEmpty();
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Fact]
    public async Task Overlay_only_template_command_puts_the_line_on_the_overlay_attributed_and_sends_nothing_to_chat()
    {
        Harness h = Build();
        Guid id = AddCommand(
            h,
            "template",
            ["<marquee>CHOO CHOO</marquee>"],
            delivery: new ReplyDelivery(ReplyTarget.Overlay, Html: true)
        );

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext("u-1", "Viewer One", "", "reward", "viewer")
        );

        result.Value.Succeeded.Should().BeTrue();
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
        ChatOverlayLine line = h.Overlay.Lines.Should().ContainSingle().Subject;
        line.BroadcasterId.Should().Be(Channel);
        line.BotUserId.Should().Be("bot-9");
        line.BotDisplayName.Should().Be("NomNomzBotLive");
        line.IsHtml.Should().BeTrue();
        line.Text.Should().Be("resolved:<marquee>CHOO CHOO</marquee>");
        line.RequestedBy.Should().Be("Viewer One");
    }

    [Fact]
    public async Task Overlay_only_template_command_with_no_overlay_open_fails_loudly_without_a_chat_fallback()
    {
        Harness h = Build();
        h.Overlay.Result = ChatOverlayDelivery.NoChatOverlayOpen;
        Guid id = AddCommand(
            h,
            "template",
            ["hi"],
            tts: true,
            delivery: new ReplyDelivery(ReplyTarget.Overlay)
        );

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext(null, null, "", "reward")
        );

        result.Value.Succeeded.Should().BeFalse();
        result.Value.Error.Should().Be("No chat overlay is open.");
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
        h.Recovery.Received(1).Schedule(Channel);
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Theory]
    [InlineData("pipeline")]
    [InlineData("code")]
    public async Task Pipeline_and_code_commands_run_their_bound_graph_with_the_command_variables(
        string tier
    )
    {
        Harness h = Build();
        Guid id = AddCommand(h, tier, pipelineId: PipelineId, tts: true);

        Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, id.ToString()),
            new InvocationContext(null, null, "a b", "timer")
        );

        result.Value.Succeeded.Should().BeTrue();
        PipelineRequest sent = SentToEngine(h);
        sent.PipelineId.Should().Be(PipelineId);
        sent.PipelineJson.Should().Be(Graph);
        sent.SpeakWithTts.Should().BeTrue();
        sent.InitialVariables["args.1"].Should().Be("a");
        sent.InitialVariables["args.2"].Should().Be("b");
        sent.InitialVariables["user.role"].Should().Be("broadcaster");
        sent.InitialVariables["trigger.source"].Should().Be("timer");
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task Disabled_and_other_channel_commands_are_rejected_and_run_nothing()
    {
        Harness h = Build();
        Guid disabled = AddCommand(h, "template", ["x"], enabled: false);
        Guid foreign = AddCommand(h, "template", ["x"], channel: Guid.NewGuid());

        foreach (Guid id in new[] { disabled, foreign })
        {
            Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
                Channel,
                new BindingTarget(BindingTargetKind.Command, id.ToString()),
                new InvocationContext(null, null, "", "reward")
            );
            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("NOT_FOUND");
        }

        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task Direct_run_ignores_cooldown_and_permission_so_a_viewer_can_run_it_repeatedly()
    {
        Harness h = Build();
        Guid id = AddCommand(h, "template", ["hi"]);

        for (int i = 0; i < 2; i++)
        {
            Result<InvocationOutcome> result = await h.Invoker.InvokeAsync(
                Channel,
                new BindingTarget(BindingTargetKind.Command, id.ToString()),
                new InvocationContext("u-1", "Viewer", "", "reward", "viewer")
            );
            result.Value.Succeeded.Should().BeTrue();
        }

        await h
            .Chat.Received(2)
            .SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
