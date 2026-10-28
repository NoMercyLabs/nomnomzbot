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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Games;
using NomNomzBot.Infrastructure.Identity.Builtins;
using NomNomzBot.Infrastructure.MediaShare.Builtins;
using NomNomzBot.Infrastructure.Moderation.Builtins;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Quotes.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tts.Builtins;
using NomNomzBot.Infrastructure.ViewerData.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A built-in that answers about the caller (<c>!lurk</c>, <c>!accountage</c>, ...) holds each chatter on their
/// own clock. One viewer's use never blocks another viewer from the same command (Stoney, 2026-10-28).
/// </summary>
public sealed class BuiltinUserCooldownScopeTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000d004");

    [Fact]
    public async Task Two_chatters_can_lurk_back_to_back_and_both_are_answered_and_flagged()
    {
        await using CommandsTestDbContext db = NewDbWithViewers("viewer-1", "viewer-2");
        List<string> sent = [];
        ChatMessageHandler sut = Build(db, sent);

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);
        await sut.HandleAsync(Message("viewer-2"), CancellationToken.None);

        sent.Should().HaveCount(2);
        sent[0].Should().Contain("viewer-1").And.Contain("lurking");
        sent[1].Should().Contain("viewer-2").And.Contain("lurking");
        db.Users.Single(u => u.TwitchUserId == "tw-viewer-1").IsLurking.Should().BeTrue();
        db.Users.Single(u => u.TwitchUserId == "tw-viewer-2").IsLurking.Should().BeTrue();
    }

    [Fact]
    public async Task The_same_chatter_twice_in_a_row_is_held_and_told_so()
    {
        await using CommandsTestDbContext db = NewDbWithViewers("viewer-1");
        List<string> sent = [];
        ChatMessageHandler sut = Build(db, sent);

        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);
        await sut.HandleAsync(Message("viewer-1"), CancellationToken.None);

        sent.Should().HaveCount(2);
        sent[0].Should().Contain("lurking");
        sent[1].Should().Contain("cooldown");
    }

    // Built-ins that answer about, or act for, the caller: no channel-wide window, a per-chatter one instead.
    // The 0/0 rows pace themselves elsewhere (the game config, the media service, the data-subject rights floor).
    private static readonly Dictionary<Type, (int Global, int User)> CallerBuiltins = new()
    {
        [typeof(LurkBuiltin)] = (0, 30),
        [typeof(UnlurkBuiltin)] = (0, 30),
        [typeof(AccountAgeBuiltin)] = (0, 30),
        [typeof(FollowAgeBuiltin)] = (0, 30),
        [typeof(UpdateUserInfoBuiltin)] = (0, 60),
        [typeof(StatsBuiltin)] = (0, 10),
        [typeof(ProfileBuiltin)] = (0, 10),
        [typeof(VoiceBuiltin)] = (0, 5),
        [typeof(WhisperBuiltin)] = (0, 3),
        [typeof(QueueBuiltin)] = (0, 10),
        [typeof(SongRequestBuiltin)] = (0, 5),
        [typeof(WrongSongBuiltin)] = (0, 5),
        [typeof(CoinflipBuiltin)] = (0, 0),
        [typeof(DiceBuiltin)] = (0, 0),
        [typeof(SlotsBuiltin)] = (0, 0),
        [typeof(MediaBuiltin)] = (0, 0),
        [typeof(ForgetMeBuiltin)] = (0, 0),
        [typeof(MyDataBuiltin)] = (0, 0),
        [typeof(GdprBuiltin)] = (0, 0),
    };

    // Built-ins that give every chatter the same answer or change channel state: a shared window is right.
    private static readonly HashSet<Type> SharedBuiltins =
    [
        typeof(SkipBuiltin),
        typeof(CurrentSongBuiltin),
        typeof(PlaylistBuiltin),
        typeof(BangerBuiltin),
        typeof(BanSongBuiltin),
        typeof(ClearSongsBuiltin),
        typeof(VolumeBuiltin),
        typeof(PauseBuiltin),
        typeof(ResumeBuiltin),
        typeof(CloseQueueBuiltin),
        typeof(OpenQueueBuiltin),
        typeof(UptimeBuiltin),
        typeof(DiscordInviteBuiltin),
        typeof(HelpBuiltin),
        typeof(CommandsBuiltin),
        typeof(LeaderboardBuiltin),
        typeof(QuoteBuiltin),
        typeof(PermitBuiltin),
        typeof(UnpermitBuiltin),
        typeof(WhitelistBuiltin),
        typeof(UnwhitelistBuiltin),
        typeof(AllowBuiltin),
        typeof(DisallowBuiltin),
    ];

    [Fact]
    public void Every_shipped_builtin_is_classified_and_every_caller_builtin_has_no_channel_wide_window()
    {
        List<Type> shipped =
        [
            .. typeof(LurkBuiltin)
                .Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Where(t => typeof(IBuiltinCommand).IsAssignableFrom(t)),
        ];

        shipped
            .Where(t => !CallerBuiltins.ContainsKey(t) && !SharedBuiltins.Contains(t))
            .Should()
            .BeEmpty("a new built-in must be declared caller-specific or shared in this table");

        foreach ((Type type, (int Global, int User) expected) in CallerBuiltins)
        {
            IBuiltinCommand instance = (IBuiltinCommand)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            (instance.DefaultCooldownSeconds, instance.DefaultUserCooldownSeconds)
                .Should()
                .Be(expected, $"{type.Name} answers about the caller");
        }
    }

    private static CommandsTestDbContext NewDbWithViewers(params string[] logins)
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        foreach (string login in logins)
            db.Users.Add(
                new User
                {
                    TwitchUserId = $"tw-{login}",
                    Username = login,
                    UsernameNormalized = login,
                    DisplayName = login,
                    IsLurking = false,
                }
            );
        db.SaveChanges();
        return db;
    }

    private static ChatMessageHandler Build(CommandsTestDbContext db, List<string> sent)
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(
                    new UserDto(
                        Guid.CreateVersion7().ToString(),
                        call.ArgAt<string>(1),
                        call.ArgAt<string>(2),
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UtcNow,
                        LastLoginAt: DateTime.UtcNow
                    )
                )
            );
        LurkBuiltin lurk = new(
            users,
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestBuiltinComposer.Create()
        );

        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-888",
            ChannelName = "stoney_eagle",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get("lurk").Returns(lurk);

        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                sent.Add(call.ArgAt<string>(3));
                return Task.FromResult(Result.Success());
            });
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                sent.Add(call.ArgAt<string>(2));
                return Task.FromResult(Result.Success());
            });

        return new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            new CooldownManager(TimeProvider.System),
            TestReplyDispatcher.Over(chat),
            Substitute.For<IPipelineEngine>(),
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new LiveGameSessionRegistry(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
    }

    private static ChatMessageReceivedEvent Message(string login) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = $"msg-{Guid.NewGuid()}",
            TwitchBroadcasterId = "tw-888",
            UserId = $"tw-{login}",
            UserDisplayName = login,
            UserLogin = login,
            Message = "!lurk",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
