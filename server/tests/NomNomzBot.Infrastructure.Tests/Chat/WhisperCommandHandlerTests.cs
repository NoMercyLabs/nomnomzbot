// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Chat.Whispers;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A whisper to the bot runs only whisper-safe built-ins, answers by whisper (never chat), picks the channel
/// by name or by the sender's single channel, and falls back to a chat pointer past Twitch's new-recipient cap.
/// </summary>
public sealed class WhisperCommandHandlerTests
{
    private static readonly Guid StoneyId = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherId = Guid.Parse("0199c000-0000-7000-8000-0000000000a2");
    private const string BotId = "999";

    private readonly IBuiltinCommandCatalog _catalog = Substitute.For<IBuiltinCommandCatalog>();
    private readonly IChannelRegistry _registry = Substitute.For<IChannelRegistry>();
    private readonly ITwitchWhispersApi _whispers = Substitute.For<ITwitchWhispersApi>();
    private readonly IChatProvider _chat = Substitute.For<IChatProvider>();
    private readonly IOutboundSanctionAccessor _sanctions =
        Substitute.For<IOutboundSanctionAccessor>();
    private readonly FakeTimeProvider _clock = new();
    private readonly WhisperCommandHandler _handler;
    private readonly WhisperRecipientCap _cap;

    public WhisperCommandHandlerTests()
    {
        _cap = new(_clock);
        _whispers
            .SendAsBotAsync(default, default!, default!, default!, default)
            .ReturnsForAnyArgs(Result.Success());
        _chat.SendMessageAsync(default, default!, default).ReturnsForAnyArgs(true);
        _sanctions.Begin(default!).ReturnsForAnyArgs(Substitute.For<IDisposable>());
        _handler = new(
            _catalog,
            _registry,
            _whispers,
            _chat,
            _sanctions,
            _cap,
            NullLogger<WhisperCommandHandler>.Instance
        );
    }

    private static ChannelContext Channel(Guid id, string name, params string[] chatters)
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = id,
            TwitchChannelId = id.ToString(),
            ChannelName = name,
        };
        foreach (string chatter in chatters)
            ctx.SessionChatters[chatter] = chatter;
        return ctx;
    }

    private IBuiltinCommand Builtin(string key, bool whisperSafe, string reply)
    {
        IBuiltinCommand builtin = Substitute.For<IBuiltinCommand>();
        builtin.BuiltinKey.Returns(key);
        builtin.WhisperSafe.Returns(whisperSafe);
        builtin.DefaultMinPermissionLevel.Returns(0);
        builtin.ExecuteAsync(default!, default).ReturnsForAnyArgs(Result.Success(reply));
        _catalog.Get(key).Returns(builtin);
        return builtin;
    }

    private static WhisperReceivedEvent Whisper(string text, string fromId = "42") =>
        new()
        {
            BroadcasterId = Guid.Empty,
            WhisperId = "w1",
            FromUserId = fromId,
            FromUserDisplayName = "Bamo16",
            FromUserLogin = "bamo16",
            ToUserId = BotId,
            Text = text,
        };

    [Fact]
    public async Task WhisperedQueue_AnswersByWhisper_AndNeverPostsToChat()
    {
        IBuiltinCommand queue = Builtin("queue", true, "Queue: Song A, Song B");
        _registry.GetAll().Returns([Channel(StoneyId, "stoney_eagle", "42")]);

        await _handler.HandleAsync(Whisper("!queue"));

        await _whispers
            .Received(1)
            .SendAsBotAsync(StoneyId, BotId, "42", "Queue: Song A, Song B", default);
        await _chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        await queue
            .Received(1)
            .ExecuteAsync(
                Arg.Is<BuiltinCommandContext>(c =>
                    c.BroadcasterId == StoneyId && c.TriggeringUserId == "42" && c.RoleLevel == 0
                ),
                default
            );
    }

    [Fact]
    public async Task NamedChannel_WinsOverSenderChannels_AndIsRemovedFromArgs()
    {
        IBuiltinCommand queue = Builtin("queue", true, "other queue");
        _registry
            .GetAll()
            .Returns([Channel(StoneyId, "stoney_eagle", "42"), Channel(OtherId, "kitte", "42")]);

        await _handler.HandleAsync(Whisper("!queue kitte"));

        await queue
            .Received(1)
            .ExecuteAsync(
                Arg.Is<BuiltinCommandContext>(c =>
                    c.BroadcasterId == OtherId && c.Args == string.Empty
                ),
                default
            );
        await _whispers.Received(1).SendAsBotAsync(OtherId, BotId, "42", "other queue", default);
    }

    [Fact]
    public async Task AmbiguousChannel_WhispersHowToNameOne_AndRunsNothing()
    {
        IBuiltinCommand queue = Builtin("queue", true, "never");
        _registry
            .GetAll()
            .Returns([Channel(StoneyId, "stoney_eagle", "42"), Channel(OtherId, "kitte", "42")]);

        await _handler.HandleAsync(Whisper("!queue"));

        await queue.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await _whispers
            .Received(1)
            .SendAsBotAsync(
                Guid.Empty,
                BotId,
                "42",
                Arg.Is<string>(m => m.Contains("Which channel?")),
                default
            );
    }

    [Fact]
    public async Task CommandNotWhisperSafe_IsRefusedByWhisper_AndNeverRuns()
    {
        IBuiltinCommand skip = Builtin("skip", false, "skipped");
        _registry.GetAll().Returns([Channel(StoneyId, "stoney_eagle", "42")]);

        await _handler.HandleAsync(Whisper("!skip"));

        await skip.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await _whispers
            .Received(1)
            .SendAsBotAsync(
                Guid.Empty,
                BotId,
                "42",
                Arg.Is<string>(m => m.Contains("works in chat only")),
                default
            );
        await _chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task OverTheNewRecipientCap_PointsToChat_InsteadOfWhispering()
    {
        Builtin("queue", true, "Queue: Song A");
        _registry.GetAll().Returns([Channel(StoneyId, "stoney_eagle", "9000")]);
        for (int i = 0; i < WhisperRecipientCap.DailyNewRecipientLimit; i++)
            _cap.TryAdmit($"seen{i}");

        await _handler.HandleAsync(Whisper("!queue", fromId: "9000"));

        await _whispers
            .DidNotReceiveWithAnyArgs()
            .SendAsBotAsync(default, default!, default!, default!, default);
        await _chat
            .Received(1)
            .SendMessageAsync(
                StoneyId,
                Arg.Is<string>(m => m.Contains("whisper limit") && !m.Contains("Song A")),
                default
            );
    }

    [Fact]
    public void Cap_AdmitsKnownRecipients_RefusesNewOnes_AndResetsNextUtcDay()
    {
        for (int i = 0; i < WhisperRecipientCap.DailyNewRecipientLimit; i++)
            Assert.True(_cap.TryAdmit($"u{i}"));

        Assert.False(_cap.TryAdmit("brand-new"));
        Assert.True(_cap.TryAdmit("u0"));

        _clock.Advance(TimeSpan.FromDays(1));
        Assert.True(_cap.TryAdmit("brand-new"));
    }

    [Fact]
    public void QueueAndSongBuiltins_AreWhisperSafe_ButOtherBuiltinsAreNot()
    {
        IMusicService music = Substitute.For<IMusicService>();
        IBuiltinResponseComposer composer = Substitute.For<IBuiltinResponseComposer>();

        Assert.True(new QueueBuiltin(music, composer).WhisperSafe);
        Assert.True(new CurrentSongBuiltin(music, composer).WhisperSafe);
        IBuiltinCommand commands = new CommandsBuiltin(
            Substitute.For<ICommandService>(),
            Substitute.For<IServiceProvider>(),
            composer
        );
        Assert.False(commands.WhisperSafe);
    }
}
