// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Old-bot parity (TwitchCommandService.ExecuteCommand): a command the viewer may not use is answered in chat AND
/// the denial line is spoken with TTS in the broadcaster's voice. The spoken line is the chat line, the speaker is
/// the channel owner, and a per-viewer cooldown keeps a spammed denial from flooding the TTS queue.
/// </summary>
public sealed class ChatMessageHandlerDenialTtsTests
{
    private const string DeniedText = "You don't have permission to use that command.";
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d002");
    private static readonly Guid OwnerUser = Guid.Parse("0198a000-0000-7000-8000-00000000d0aa");

    [Fact]
    public async Task A_denied_command_is_spoken_once_in_the_broadcasters_voice_and_replied_in_chat()
    {
        Fixture f = NewFixture();

        await f.Sut.HandleAsync(DeniedMessage("tw-viewer-1"), CancellationToken.None);

        await f
            .Chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                Arg.Any<string>(),
                "msg-1",
                DeniedText,
                Arg.Any<CancellationToken>()
            );
        await f
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.BroadcasterId == Broadcaster
                    && r.Text == DeniedText
                    && r.RequestedByUserId == OwnerUser
                    && r.RequestedByTwitchUserId == "tw-777"
                    && r.VoiceIdOverride == null
                    && r.BitsAmount == 0
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_same_viewer_denied_again_inside_the_cooldown_gets_chat_but_no_second_speech()
    {
        Fixture f = NewFixture();

        await f.Sut.HandleAsync(DeniedMessage("tw-viewer-1"), CancellationToken.None);
        await f.Sut.HandleAsync(DeniedMessage("tw-viewer-1"), CancellationToken.None);

        await f
            .Chat.Received(2)
            .SendReplyAsync(
                Broadcaster,
                Arg.Any<string>(),
                "msg-1",
                DeniedText,
                Arg.Any<CancellationToken>()
            );
        await f
            .Tts.Received(1)
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_speech_never_costs_the_chat_notice()
    {
        Fixture f = NewFixture();
        f.Tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TtsDispatchOutcome>("TTS is off for this channel"));

        await f.Sut.HandleAsync(DeniedMessage("tw-viewer-1"), CancellationToken.None);

        await f
            .Chat.Received(1)
            .SendReplyAsync(
                Broadcaster,
                Arg.Any<string>(),
                "msg-1",
                DeniedText,
                Arg.Any<CancellationToken>()
            );
    }

    private sealed record Fixture(
        ChatMessageHandler Sut,
        IInboundOriginChatSender Chat,
        ITtsDispatchService Tts
    );

    private static Fixture NewFixture()
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };
        ctx.Commands["modonly"] = new()
        {
            Name = "modonly",
            TemplateResponses = ["Hi"],
            GlobalCooldown = 0,
            UserCooldown = 0,
            MinPermissionLevel = 10,
            Tier = "template",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                "tw-777",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        OwnerUser.ToString(),
                        "stoney_eagle",
                        "Stoney_Eagle",
                        null,
                        null,
                        DateTime.UnixEpoch,
                        DateTime.UnixEpoch
                    )
                )
            );
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v",
                        "p",
                        10,
                        100,
                        "u"
                    )
                )
            );
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(users)
            .AddSingleton(tts)
            .BuildServiceProvider();

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
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CooldownManager(TimeProvider.System),
            chat,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
        return new(sut, chat, tts);
    }

    private static ChatMessageReceivedEvent DeniedMessage(string userId) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-777",
            UserId = userId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "!modonly",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
