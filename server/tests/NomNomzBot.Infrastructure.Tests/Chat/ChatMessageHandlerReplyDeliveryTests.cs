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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The <c>!train</c> command end to end through the chat handler: a template command whose
/// <see cref="ReplyDelivery"/> says where the reply goes. What counts is where the text lands: the platform
/// chat, the chat overlay, both or neither, and what the command records afterwards.
/// </summary>
public sealed class ChatMessageHandlerReplyDeliveryTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197d6a0-0000-7000-8000-0000000000aa");
    private const string TrainTemplate = "<marquee>CHOO CHOO {user}</marquee>";

    [Fact]
    public async Task Overlay_only_html_reply_reaches_the_overlay_sanitized_and_attributed_and_chat_gets_nothing()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Overlay, Html: true));

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
        await rig
            .Chat.DidNotReceiveWithAnyArgs()
            .SendReplyAsync(default, default!, default!, default!);
        rig.Provider.ReceivedCalls().Should().BeEmpty();
        ChatOverlayLine line = rig.Overlay.Lines.Should().ContainSingle().Subject;
        line.IsHtml.Should().BeTrue();
        line.BroadcasterId.Should().Be(Broadcaster);
        line.Text.Should().Be("<marquee>CHOO CHOO Viewer</marquee>");
        line.RequestedBy.Should().Be("Viewer");
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_viewer_name_with_markup_reaches_the_overlay_as_text_never_as_a_tag()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Overlay, Html: true));

        await rig.Sut.HandleAsync(
            Message("!train", displayName: "<script>x()</script>"),
            CancellationToken.None
        );

        ChatOverlayLine line = rig.Overlay.Lines.Should().ContainSingle().Subject;
        line.Text.Should().NotContain("<script");
        line.Text.Should().Be("<marquee>CHOO CHOO &lt;script&gt;x()&lt;/script&gt;</marquee>");
        line.RequestedBy.Should().Be("<script>x()</script>", "the widget draws the name as text");
    }

    [Fact]
    public async Task With_no_overlay_open_nothing_goes_to_chat_the_command_is_recorded_not_delivered_and_recovery_runs()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Overlay, Html: true));
        rig.Overlay.Result = ChatOverlayDelivery.NoChatOverlayOpen;

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
        await rig
            .Chat.DidNotReceiveWithAnyArgs()
            .SendReplyAsync(default, default!, default!, default!);
        rig.Provider.ReceivedCalls().Should().BeEmpty();
        rig.Recovery.Received(1).Schedule(Broadcaster);
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && !e.Succeeded),
                Arg.Any<CancellationToken>()
            );
        await rig.Pipeline.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task Both_answers_in_chat_with_plain_text_and_puts_the_html_line_on_the_overlay()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Both, Html: true));

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "twitch",
                "msg-1",
                "<marquee>CHOO CHOO Viewer</marquee>",
                Arg.Any<CancellationToken>()
            );
        ChatOverlayLine line = rig.Overlay.Lines.Should().ContainSingle().Subject;
        line.Text.Should().Be("<marquee>CHOO CHOO Viewer</marquee>");
        line.RequestedBy.Should().Be("Viewer");
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task None_sends_nothing_anywhere_speaks_nothing_and_still_counts_as_run()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.None), speakWithTts: true);

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
        await rig
            .Chat.DidNotReceiveWithAnyArgs()
            .SendReplyAsync(default, default!, default!, default!);
        rig.Overlay.Lines.Should().BeEmpty();
        await rig.Pipeline.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(ReplyTarget.Chat, true)]
    [InlineData(ReplyTarget.Both, true)]
    [InlineData(ReplyTarget.Overlay, false)]
    [InlineData(ReplyTarget.None, false)]
    public async Task Text_to_speech_runs_only_when_the_reply_reached_the_platform_chat(
        ReplyTarget target,
        bool speaks
    )
    {
        Rig rig = new(new ReplyDelivery(target), speakWithTts: true);

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        int calls = rig.Pipeline.ReceivedCalls().Count();
        calls.Should().Be(speaks ? 1 : 0);
        if (speaks)
            await rig
                .Pipeline.Received(1)
                .ExecuteAsync(
                    Arg.Is<PipelineRequest>(r =>
                        r.InitialVariables["tts.reply"] == "<marquee>CHOO CHOO Viewer</marquee>"
                    ),
                    Arg.Any<CancellationToken>()
                );
    }

    [Fact]
    public async Task A_command_with_no_delivery_set_answers_in_chat_exactly_as_before()
    {
        Rig rig = new(delivery: null);

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                "twitch",
                "msg-1",
                "<marquee>CHOO CHOO Viewer</marquee>",
                Arg.Any<CancellationToken>()
            );
        rig.Overlay.Lines.Should().BeEmpty();
        rig.Recovery.ReceivedCalls().Should().BeEmpty();
        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_chat_reply_the_platform_rejects_is_still_recorded_as_failed()
    {
        Rig rig = new(delivery: null);
        rig.Chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("rejected", "SEND_FAILED"));
        rig.Chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("rejected", "SEND_FAILED"));

        await rig.Sut.HandleAsync(Message("!train"), CancellationToken.None);

        await rig
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<CommandExecutedEvent>(e => e.CommandName == "train" && !e.Succeeded),
                Arg.Any<CancellationToken>()
            );
    }

    private static ChatMessageReceivedEvent Message(string text, string displayName = "Viewer") =>
        new()
        {
            Provider = "twitch",
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-777",
            UserId = "tw-viewer-1",
            UserDisplayName = displayName,
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private sealed class Rig
    {
        public IInboundOriginChatSender Chat { get; } = Substitute.For<IInboundOriginChatSender>();
        public IChatProvider Provider { get; } = Substitute.For<IChatProvider>();
        public RecordingChatOverlayLineSender Overlay { get; } = new();
        public IObsBrowserSourceRecoveryScheduler Recovery { get; } =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();
        public IPipelineEngine Pipeline { get; } = Substitute.For<IPipelineEngine>();
        public IEventBus Bus { get; } = Substitute.For<IEventBus>();
        public ChatMessageHandler Sut { get; }

        public Rig(ReplyDelivery? delivery, bool speakWithTts = false)
        {
            ChannelContext ctx = new()
            {
                BroadcasterId = Broadcaster,
                TwitchChannelId = "tw-777",
                ChannelName = "stoney_eagle",
            };
            ctx.Commands["train"] = new()
            {
                Name = "train",
                TemplateResponses = [TrainTemplate],
                GlobalCooldown = 0,
                UserCooldown = 0,
                MinPermissionLevel = 0,
                Tier = "template",
                ReplyDelivery = delivery,
                SpeakWithTts = speakWithTts,
            };
            IChannelRegistry registry = Substitute.For<IChannelRegistry>();
            registry.Get(Broadcaster).Returns(ctx);

            IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
            builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

            Chat.SendReplyAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success());
            Chat.SendMessageAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success());
            Provider
                .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            Sut = new(
                registry,
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ICooldownManager>(),
                TestReplyDispatcher.Over(Chat, Overlay, Provider, Recovery),
                Pipeline,
                builtins,
                TestTemplateResolver.Create(),
                Bus,
                new(),
                TimeProvider.System,
                new OutboundSanctionAccessor(),
                TestBuiltinComposer.Create(),
                NullLogger<ChatMessageHandler>.Instance
            );
        }
    }
}
