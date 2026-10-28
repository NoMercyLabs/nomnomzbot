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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A chat command of tier "code" runs its channel script directly: no pipeline sits between the command and the
/// script. The runner gets the script id, the chat words after the command and the chat variables; a clean run
/// sends nothing of its own (the script talks to chat itself) and records a successful execution; a script that
/// does not complete gets the one failure notice and a failed execution.
/// </summary>
public sealed class ChatMessageHandlerCodeCommandTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d101");
    private static readonly Guid Script = Guid.Parse("0198a000-0000-7000-8000-00000000d102");

    private sealed record Rig(
        ChatMessageHandler Sut,
        IInboundOriginChatSender Chat,
        IEventBus Bus,
        IScriptRunner Runner,
        IPipelineEngine Engine
    );

    private static Rig Build(ScriptExecutionOutcome outcome)
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };
        ctx.Commands["lucky"] = new()
        {
            Name = "lucky",
            TemplateResponses = [],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 0,
            Tier = "code",
            CodeScriptId = Script,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        IScriptRunner runner = Substitute.For<IScriptRunner>();
        runner
            .RunAsync(Arg.Any<Guid>(), Arg.Any<ScriptInvocation>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ScriptRunResult(
                        outcome,
                        new Dictionary<string, string>(),
                        null,
                        false,
                        outcome == ScriptExecutionOutcome.Success ? null : "boom",
                        null
                    )
                )
            );
        ServiceCollection services = new();
        services.AddSingleton(runner);
        IServiceScopeFactory scopes = services
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
        return new Rig(sut, chat, bus, runner, engine);
    }

    private static ChatMessageReceivedEvent Message(string text) =>
        new()
        {
            Provider = "twitch",
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-777",
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
    public async Task A_code_command_runs_its_script_with_the_chat_args_and_variables_and_sends_nothing_itself()
    {
        Rig rig = Build(ScriptExecutionOutcome.Success);

        await rig.Sut.HandleAsync(Message("!lucky @kitte hello"), CancellationToken.None);

        rig.Runner.ReceivedCalls().Should().ContainSingle();
        object?[] call = rig.Runner.ReceivedCalls().Single().GetArguments();
        call[0].Should().Be(Script);
        ScriptInvocation invocation = (ScriptInvocation)call[1]!;
        invocation.Args.Should().Equal("@kitte", "hello");
        invocation.TriggeredByUserId.Should().Be("tw-viewer-1");
        invocation.TriggeredByDisplayName.Should().Be("Viewer");
        invocation.Variables["args"].Should().Be("@kitte hello");
        invocation.Variables["user"].Should().Be("Viewer");
        invocation.Variables["user.role"].Should().Be("viewer");
        invocation.ReplyTo.Should().Be(new ScriptReplyTarget("msg-1", "Viewer"));
        rig.Engine.ReceivedCalls().Should().BeEmpty();
        rig.Chat.ReceivedCalls().Should().BeEmpty();
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "lucky" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_script_that_does_not_complete_gets_one_failure_notice_and_a_failed_execution()
    {
        Rig rig = Build(ScriptExecutionOutcome.Faulted);

        await rig.Sut.HandleAsync(Message("!lucky"), CancellationToken.None);

        rig.Runner.ReceivedCalls().Should().ContainSingle();
        rig.Chat.ReceivedCalls().Should().ContainSingle();
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "lucky" && !e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }
}
