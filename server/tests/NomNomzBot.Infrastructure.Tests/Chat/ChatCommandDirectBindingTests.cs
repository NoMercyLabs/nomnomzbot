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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A chat command bound straight to an action or to another command runs it through
/// <see cref="ICommandInvoker"/> as the sender, with no pipeline. A command with a pipeline and no direct target
/// keeps the pipeline path exactly as it was.
/// </summary>
public sealed class ChatCommandDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0198a000-0000-7000-8000-00000000d201");
    private static readonly Guid HugCommand = Guid.Parse("0198a000-0000-7000-8000-00000000d202");
    private static readonly Guid RaidPipeline = Guid.Parse("0198a000-0000-7000-8000-00000000d203");

    private sealed record Rig(
        ChatMessageHandler Sut,
        ICommandInvoker Invoker,
        IPipelineEngine Engine,
        IInboundOriginChatSender Chat,
        IEventBus Bus
    );

    private static Rig Build(CachedCommand command, bool invokerSucceeds = true)
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-778",
            ChannelName = "stoney_eagle",
        };
        ctx.Commands[command.Name] = command;
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(ctx);

        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new InvocationOutcome(invokerSucceeds, null, invokerSucceeds ? null : "boom")
                )
            );
        IServiceScopeFactory scopes = new ServiceCollection()
            .AddSingleton(invoker)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IEventBus bus = Substitute.For<IEventBus>();
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();

        ChatMessageHandler sut = new(
            registry,
            scopes,
            Substitute.For<ICooldownManager>(),
            TestReplyDispatcher.Over(chat),
            engine,
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            bus,
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
        return new Rig(sut, invoker, engine, chat, bus);
    }

    private static CachedCommand Command(string name, BindingTarget? target) =>
        new()
        {
            Name = name,
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            Target = target,
        };

    private static ChatMessageReceivedEvent Message(string text) =>
        new()
        {
            Provider = "twitch",
            BroadcasterId = Channel,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-778",
            UserId = "tw-viewer-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task A_command_bound_to_another_command_runs_it_as_the_sender_with_no_pipeline()
    {
        Rig rig = Build(Command("cuddle", new(BindingTargetKind.Command, HugCommand.ToString())));

        await rig.Sut.HandleAsync(Message("!cuddle @kitte hello"), CancellationToken.None);

        await rig
            .Invoker.Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == HugCommand.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "tw-viewer-1"
                    && c.DisplayName == "Viewer"
                    && c.Args == "@kitte hello"
                    && c.TriggerSource == "chat-command"
                    && c.Variables != null
                    && c.Variables["user.role"] == "viewer"
                    && c.Variables["args"] == "@kitte hello"
                ),
                Arg.Any<CancellationToken>()
            );
        rig.Engine.ReceivedCalls().Should().BeEmpty();
        rig.Chat.ReceivedCalls().Should().BeEmpty();
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "cuddle" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_command_bound_to_an_action_runs_the_action_with_its_params()
    {
        Rig rig = Build(
            Command(
                "airhorn",
                new(
                    BindingTargetKind.Action,
                    ActionType: "play_sound",
                    Params: new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonSerializer.SerializeToElement("airhorn"),
                    }
                )
            )
        );

        await rig.Sut.HandleAsync(Message("!airhorn"), CancellationToken.None);

        await rig
            .Invoker.Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Action
                    && t.ActionType == "play_sound"
                    && t.Params!["name"].GetString() == "airhorn"
                ),
                Arg.Is<InvocationContext>(c => c.TriggerSource == "chat-command"),
                Arg.Any<CancellationToken>()
            );
        rig.Engine.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_bound_target_that_fails_gets_one_failure_notice_and_a_failed_execution()
    {
        Rig rig = Build(
            Command("cuddle", new(BindingTargetKind.Command, HugCommand.ToString())),
            invokerSucceeds: false
        );

        await rig.Sut.HandleAsync(Message("!cuddle"), CancellationToken.None);

        rig.Chat.ReceivedCalls().Should().ContainSingle();
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "cuddle" && !e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_pipeline_command_with_no_direct_target_still_runs_its_pipeline_and_never_the_invoker()
    {
        const string graph = """{"steps":[{"action":"wait","params":{"seconds":88}}]}""";
        CachedCommand raid = new()
        {
            Name = "raid",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "pipeline",
            PipelineGraphJson = graph,
            PipelineId = RaidPipeline,
        };
        Rig rig = Build(raid);

        await rig.Sut.HandleAsync(Message("!raid somebody"), CancellationToken.None);

        await rig
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.PipelineId == RaidPipeline
                    && r.PipelineJson == graph
                    && r.TriggeredByUserId == "tw-viewer-1"
                    && r.InitialVariables["args"] == "somebody"
                ),
                Arg.Any<CancellationToken>()
            );
        rig.Invoker.ReceivedCalls().Should().BeEmpty();
    }
}
