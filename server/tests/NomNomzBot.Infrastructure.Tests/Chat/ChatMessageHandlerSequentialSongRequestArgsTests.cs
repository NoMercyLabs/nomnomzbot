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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Games;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// S-SR-STALE-2 — the owner's live transcript showed <c>!sr</c> answering with the PREVIOUS request's
/// track (e.g. typing <c>!sr 9 to 5</c> got back the confirmation for the immediately preceding
/// <c>!sr joliene</c>). The Music layer (<c>MusicService</c>/<c>SpotifyMusicProvider</c>) was already
/// traced and proven to hold no cross-request state (see <c>MusicServiceSequentialRequestsTests</c>), so
/// this proves — or disproves — the fault sits UPSTREAM of it, at the point <see cref="ChatMessageHandler"/>
/// parses each chat line into the <c>Args</c> handed to a builtin. It drives the REAL chat-message path
/// (<see cref="ChatMessageHandler.HandleAsync"/>) with a capturing stand-in builtin at the <c>!sr</c> key,
/// exactly the way <c>SongRequestBuiltin</c> is registered — never the builtin directly — so a bug in the
/// handler's own text-to-args parsing, or in anything it holds across calls, would show up here.
/// </summary>
public sealed class ChatMessageHandlerSequentialSongRequestArgsTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d002");
    private const string SongRequestKey = "sr";

    [Fact]
    public async Task Two_song_requests_back_to_back_each_reach_the_builtin_with_their_own_query()
    {
        (ChatMessageHandler sut, RecordingSongRequestBuiltin builtin) = Build();

        await sut.HandleAsync(MessageEvent("viewer-1", "!sr joliene"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("viewer-1", "!sr 9 to 5"), CancellationToken.None);

        builtin.SeenArgs.Should().Equal("joliene", "9 to 5");
    }

    [Fact]
    public async Task A_plain_chat_line_between_two_song_requests_does_not_shift_the_args()
    {
        (ChatMessageHandler sut, RecordingSongRequestBuiltin builtin) = Build();

        await sut.HandleAsync(MessageEvent("viewer-1", "!sr joliene"), CancellationToken.None);
        // Another viewer talking in chat in between — no command prefix, matches nothing.
        await sut.HandleAsync(MessageEvent("viewer-2", "lol nice pick"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("viewer-1", "!sr 9 to 5"), CancellationToken.None);

        builtin.SeenArgs.Should().Equal("joliene", "9 to 5");
    }

    [Fact]
    public async Task Two_different_users_each_get_their_own_query_answered()
    {
        (ChatMessageHandler sut, RecordingSongRequestBuiltin builtin) = Build();

        await sut.HandleAsync(MessageEvent("viewer-1", "!sr joliene"), CancellationToken.None);
        await sut.HandleAsync(MessageEvent("viewer-2", "!sr 9 to 5"), CancellationToken.None);

        builtin.SeenArgs.Should().Equal("joliene", "9 to 5");
        builtin.SeenUsers.Should().Equal("viewer-1", "viewer-2");
    }

    private static (ChatMessageHandler Sut, RecordingSongRequestBuiltin Builtin) Build()
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-888",
            ChannelName = "stoney_eagle",
        };

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        RecordingSongRequestBuiltin builtin = new();
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(SongRequestKey).Returns(builtin);

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
            chat,
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );

        return (sut, builtin);
    }

    private static ChatMessageReceivedEvent MessageEvent(string userLogin, string message) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = $"msg-{Guid.NewGuid()}",
            TwitchBroadcasterId = "tw-888",
            UserId = $"tw-{userLogin}",
            UserDisplayName = userLogin,
            UserLogin = userLogin,
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    /// <summary>Stands in for <c>SongRequestBuiltin</c> at the <c>!sr</c> key: records exactly the
    /// <see cref="BuiltinCommandContext.Args"/> (the query text) and requesting user each invocation was
    /// handed, in call order — the same shape the real builtin composes its confirmation from.</summary>
    private sealed class RecordingSongRequestBuiltin : IBuiltinCommand
    {
        public List<string> SeenArgs { get; } = [];
        public List<string> SeenUsers { get; } = [];

        public string BuiltinKey => SongRequestKey;
        public int DefaultCooldownSeconds => 0;
        public int DefaultMinPermissionLevel => 0;

        public Task<Result<string>> ExecuteAsync(
            BuiltinCommandContext context,
            CancellationToken ct = default
        )
        {
            SeenArgs.Add(context.Args);
            SeenUsers.Add(context.TriggeringUserLogin);
            return Task.FromResult(Result.Success($"Queued: {context.Args}"));
        }
    }
}
