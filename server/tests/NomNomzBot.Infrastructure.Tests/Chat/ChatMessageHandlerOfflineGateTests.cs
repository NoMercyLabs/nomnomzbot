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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A channel that has not opted to keep commands active while offline answers no viewer command while it is not
/// live. The broadcaster and moderators still get their answer.
/// </summary>
public sealed class ChatMessageHandlerOfflineGateTests
{
    private const string UptimeResponse = "Live for 1h 23m!";

    [Fact]
    public async Task An_offline_channel_that_did_not_opt_in_answers_no_viewer_command()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            isLive: false,
            keepActive: false
        );

        await sut.HandleAsync(Event(channel), CancellationToken.None);

        await AssertNothingSentAsync(chat);
    }

    [Fact]
    public async Task An_offline_channel_that_did_not_opt_in_still_answers_a_moderator()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            isLive: false,
            keepActive: false
        );

        await sut.HandleAsync(Event(channel, isModerator: true), CancellationToken.None);

        await AssertUptimeSentAsync(chat, channel);
    }

    [Fact]
    public async Task An_offline_channel_that_did_not_opt_in_still_answers_the_broadcaster()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            isLive: false,
            keepActive: false
        );

        await sut.HandleAsync(Event(channel, isBroadcaster: true), CancellationToken.None);

        await AssertUptimeSentAsync(chat, channel);
    }

    [Fact]
    public async Task A_live_channel_answers_a_viewer_command()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            isLive: true,
            keepActive: false
        );

        await sut.HandleAsync(Event(channel), CancellationToken.None);

        await AssertUptimeSentAsync(chat, channel);
    }

    [Fact]
    public async Task An_offline_channel_that_opted_in_answers_a_viewer_command()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            isLive: false,
            keepActive: true
        );

        await sut.HandleAsync(Event(channel), CancellationToken.None);

        await AssertUptimeSentAsync(chat, channel);
    }

    private static async Task AssertNothingSentAsync(IInboundOriginChatSender chat)
    {
        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default!);
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
    }

    private static async Task AssertUptimeSentAsync(IInboundOriginChatSender chat, Guid channel) =>
        await chat.Received(1)
            .SendReplyAsync(
                channel,
                Arg.Any<string>(),
                "msg-1",
                UptimeResponse,
                Arg.Any<CancellationToken>()
            );

    private static (ChatMessageHandler Sut, IInboundOriginChatSender Chat) Build(
        Guid channel,
        bool isLive,
        bool keepActive
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = channel,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
            IsLive = isLive,
            KeepActiveWhileOffline = keepActive,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(channel).Returns(ctx);

        IBuiltinCommand builtin = new UptimeStub();
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(builtin.BuiltinKey).Returns(builtin);

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

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ICooldownManager>(),
            TestReplyDispatcher.Over(chat),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, chat);
    }

    private static ChatMessageReceivedEvent Event(
        Guid channel,
        bool isModerator = false,
        bool isBroadcaster = false
    ) =>
        new()
        {
            Provider = AuthEnums.Platform.Twitch,
            BroadcasterId = channel,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-777",
            UserId = "tw-viewer-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "!uptime",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = isModerator,
            IsBroadcaster = isBroadcaster,
        };

    private sealed class UptimeStub : IBuiltinCommand
    {
        public string BuiltinKey => "uptime";
        public int DefaultCooldownSeconds => 0;
        public int DefaultMinPermissionLevel => 0;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(UptimeResponse));
    }
}
