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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A keyword chat trigger's template reply goes where its <see cref="ReplyDelivery"/> says: the platform
/// chat, the chat overlay, both or nowhere. What counts is where the text lands.
/// </summary>
public sealed class ChatTriggerReplyDeliveryTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0197d6a0-0000-7000-8000-0000000000bb");
    private const string Template = "<b>welcome {user}</b>";

    [Fact]
    public async Task Overlay_only_html_reply_reaches_the_overlay_attributed_and_chat_gets_nothing()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Overlay, Html: true));

        await rig.Sut.HandleAsync(Line("hello there"), CancellationToken.None);

        rig.Overlay.Lines.Should()
            .ContainSingle()
            .Which.Text.Should()
            .Be("<b>welcome Viewer</b><br><small>requested by Viewer</small>");
        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
        rig.Provider.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Both_sends_the_plain_text_to_chat_and_the_line_to_the_overlay()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.Both, Html: true));

        await rig.Sut.HandleAsync(Line("hello there"), CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendMessageAsync(
                Broadcaster,
                "twitch",
                "<b>welcome Viewer</b>",
                Arg.Any<CancellationToken>()
            );
        rig.Overlay.Lines.Should().ContainSingle();
    }

    [Fact]
    public async Task None_sends_nothing_anywhere()
    {
        Rig rig = new(new ReplyDelivery(ReplyTarget.None));

        await rig.Sut.HandleAsync(Line("hello there"), CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
        rig.Overlay.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task A_trigger_with_no_delivery_answers_in_chat_exactly_as_before()
    {
        Rig rig = new(delivery: null);

        await rig.Sut.HandleAsync(Line("hello there"), CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendMessageAsync(
                Broadcaster,
                "twitch",
                "<b>welcome Viewer</b>",
                Arg.Any<CancellationToken>()
            );
        rig.Overlay.Lines.Should().BeEmpty();
    }

    private static ChatMessageReceivedEvent Line(string text) =>
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

    private sealed class Rig
    {
        public IInboundOriginChatSender Chat { get; } = Substitute.For<IInboundOriginChatSender>();
        public IChatProvider Provider { get; } = Substitute.For<IChatProvider>();
        public RecordingChatOverlayLineSender Overlay { get; } = new();
        public ChatMessageHandler Sut { get; }

        public Rig(ReplyDelivery? delivery)
        {
            ChannelContext ctx = new()
            {
                BroadcasterId = Broadcaster,
                TwitchChannelId = "tw-777",
                ChannelName = "stoney_eagle",
            };
            CachedChatTrigger trigger = new()
            {
                Id = Guid.CreateVersion7(),
                Pattern = "hello",
                MatchType = "contains",
                CaseSensitive = false,
                Response = Template,
                ReplyDelivery = delivery,
                CooldownSeconds = 0,
                MinPermissionLevel = 0,
            };
            ctx.ChatTriggers[trigger.Id] = trigger;
            IChannelRegistry registry = Substitute.For<IChannelRegistry>();
            registry.Get(Broadcaster).Returns(ctx);

            IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
            builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);
            Chat.SendMessageAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Result.Success());

            Sut = new(
                registry,
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ICooldownManager>(),
                TestReplyDispatcher.Over(
                    Chat,
                    Overlay,
                    Provider,
                    Substitute.For<IObsBrowserSourceRecoveryScheduler>()
                ),
                Substitute.For<IPipelineEngine>(),
                builtins,
                new SubstitutingTemplateResolver(),
                Substitute.For<IEventBus>(),
                new(),
                TimeProvider.System,
                new OutboundSanctionAccessor(),
                TestBuiltinComposer.Create(),
                NullLogger<ChatMessageHandler>.Instance
            );
        }
    }
}
