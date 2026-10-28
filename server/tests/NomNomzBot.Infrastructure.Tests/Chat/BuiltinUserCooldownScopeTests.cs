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
using NomNomzBot.Application.Music.Services;
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
using NomNomzBot.Infrastructure.Tests.Music;
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

    // The one per-person window every built-in uses (Stoney, 2026-10-28).
    private const int PerPerson = 12;

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
        [typeof(LurkBuiltin)] = (0, PerPerson),
        [typeof(UnlurkBuiltin)] = (0, PerPerson),
        [typeof(AccountAgeBuiltin)] = (0, PerPerson),
        [typeof(FollowAgeBuiltin)] = (0, PerPerson),
        [typeof(UpdateUserInfoBuiltin)] = (0, PerPerson),
        [typeof(StatsBuiltin)] = (0, PerPerson),
        [typeof(ProfileBuiltin)] = (0, PerPerson),
        [typeof(VoiceBuiltin)] = (0, PerPerson),
        [typeof(WhisperBuiltin)] = (0, PerPerson),
        [typeof(QueueBuiltin)] = (0, PerPerson),
        [typeof(SongRequestBuiltin)] = (0, PerPerson),
        [typeof(WrongSongBuiltin)] = (0, PerPerson),
        [typeof(CoinflipBuiltin)] = (0, 0),
        [typeof(DiceBuiltin)] = (0, 0),
        [typeof(SlotsBuiltin)] = (0, 0),
        [typeof(MediaBuiltin)] = (0, 0),
        [typeof(ForgetMeBuiltin)] = (0, 0),
        [typeof(MyDataBuiltin)] = (0, 0),
        [typeof(GdprBuiltin)] = (0, 0),
    };

    // Shared answers every chatter hears: a global window sized by how spammy or distracting the reply is.
    private static readonly Dictionary<Type, (int Global, int User)> SharedChatBuiltins = new()
    {
        [typeof(UptimeBuiltin)] = (30, 0),
        [typeof(CurrentSongBuiltin)] = (30, 0),
        [typeof(BangerBuiltin)] = (30, 0),
        [typeof(QuoteBuiltin)] = (45, 0),
        [typeof(DiscordInviteBuiltin)] = (60, 0),
        [typeof(PlaylistBuiltin)] = (60, 0),
        [typeof(HelpBuiltin)] = (60, 0),
        [typeof(LeaderboardBuiltin)] = (90, 0),
        [typeof(CommandsBuiltin)] = (120, 0),
        [typeof(PauseBuiltin)] = (30, 0),
        [typeof(ResumeBuiltin)] = (30, 0),
        [typeof(VolumeBuiltin)] = (30, 0),
        [typeof(BanSongBuiltin)] = (30, 0),
        [typeof(CloseQueueBuiltin)] = (60, 0),
        [typeof(OpenQueueBuiltin)] = (60, 0),
        [typeof(ClearSongsBuiltin)] = (60, 0),
    };

    // Moderation tools keep today's values: a cooldown would block a moderator in a hurry.
    // !skip holds its moderator window inside the builtin; the catalogue value is the per-person one.
    private static readonly Dictionary<Type, (int Global, int User)> OtherSharedBuiltins = new()
    {
        [typeof(SkipBuiltin)] = (0, PerPerson),
        [typeof(PermitBuiltin)] = (0, 0),
        [typeof(UnpermitBuiltin)] = (0, 0),
        [typeof(WhitelistBuiltin)] = (0, 0),
        [typeof(UnwhitelistBuiltin)] = (0, 0),
        [typeof(AllowBuiltin)] = (3, 0),
        [typeof(DisallowBuiltin)] = (3, 0),
    };

    private static List<Type> ShippedBuiltins() =>
        [
            .. typeof(LurkBuiltin)
                .Assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Where(t => typeof(IBuiltinCommand).IsAssignableFrom(t)),
        ];

    private static IBuiltinCommand Uninitialized(Type type) =>
        (IBuiltinCommand)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);

    [Fact]
    public void Every_shipped_builtin_is_classified_and_carries_its_policy_cooldowns()
    {
        ShippedBuiltins()
            .Where(t =>
                !CallerBuiltins.ContainsKey(t)
                && !SharedChatBuiltins.ContainsKey(t)
                && !OtherSharedBuiltins.ContainsKey(t)
            )
            .Should()
            .BeEmpty("a new built-in must be declared caller-specific or shared in this table");

        foreach (
            (Type type, (int Global, int User) expected) in CallerBuiltins
                .Concat(SharedChatBuiltins)
                .Concat(OtherSharedBuiltins)
        )
        {
            IBuiltinCommand instance = Uninitialized(type);
            (instance.DefaultCooldownSeconds, instance.DefaultUserCooldownSeconds)
                .Should()
                .Be(expected, $"{type.Name} follows the cooldown policy");
        }
    }

    [Fact]
    public void Every_per_person_cooldown_is_twelve_seconds_and_no_other_value_is_used()
    {
        Dictionary<string, int> perPerson = ShippedBuiltins()
            .Select(Uninitialized)
            .Where(b => b.DefaultUserCooldownSeconds > 0)
            .ToDictionary(b => b.BuiltinKey, b => b.DefaultUserCooldownSeconds);

        perPerson.Should().NotBeEmpty();
        perPerson.Values.Should().OnlyContain(seconds => seconds == PerPerson);
    }

    [Fact]
    public void Every_shared_chat_answer_has_a_global_cooldown_from_30_to_500_seconds()
    {
        foreach (Type type in SharedChatBuiltins.Keys)
            Uninitialized(type)
                .DefaultCooldownSeconds.Should()
                .BeInRange(30, 500, $"{type.Name} answers the whole channel");
    }

    [Fact]
    public async Task A_moderators_bare_skip_twice_in_a_row_is_held_the_second_time()
    {
        IMusicService music = SkippingMusic();
        List<string> sent = [];
        await using CommandsTestDbContext db = NewDbWithViewers();
        ChatMessageHandler sut = BuildSkip(db, sent, music);

        await sut.HandleAsync(SkipMessage("mod-1", moderator: true), CancellationToken.None);
        await sut.HandleAsync(SkipMessage("mod-1", moderator: true), CancellationToken.None);

        await music
            .Received(1)
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        sent.Should().HaveCount(2);
        sent[1].Should().Contain("cooldown");
    }

    [Fact]
    public async Task A_second_moderator_is_held_while_the_first_moderators_skip_window_is_open()
    {
        IMusicService music = SkippingMusic();
        List<string> sent = [];
        await using CommandsTestDbContext db = NewDbWithViewers();
        ChatMessageHandler sut = BuildSkip(db, sent, music);

        await sut.HandleAsync(SkipMessage("mod-1", moderator: true), CancellationToken.None);
        await sut.HandleAsync(SkipMessage("mod-2", moderator: true), CancellationToken.None);

        await music
            .Received(1)
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        sent.Should().HaveCount(2);
        sent[1].Should().Contain("cooldown");
    }

    [Fact]
    public async Task A_viewers_own_skip_is_answered_while_the_moderator_window_is_open()
    {
        IMusicService music = SkippingMusic();
        music
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    null,
                    null,
                    1000,
                    0,
                    true,
                    50,
                    "viewer-1",
                    "spotify"
                )
            );
        List<string> sent = [];
        await using CommandsTestDbContext db = NewDbWithViewers();
        ChatMessageHandler sut = BuildSkip(db, sent, music);

        await sut.HandleAsync(SkipMessage("mod-1", moderator: true), CancellationToken.None);
        await sut.HandleAsync(SkipMessage("viewer-1", moderator: false), CancellationToken.None);

        await music
            .Received(2)
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        sent.Should().HaveCount(2);
        sent[1].Should().NotContain("cooldown");
    }

    [Fact]
    public async Task A_viewers_skip_never_opens_the_moderator_window_and_the_viewer_is_held_on_their_own_clock()
    {
        IMusicService music = SkippingMusic();
        music
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    null,
                    null,
                    1000,
                    0,
                    true,
                    50,
                    "viewer-1",
                    "spotify"
                )
            );
        List<string> sent = [];
        await using CommandsTestDbContext db = NewDbWithViewers();
        ChatMessageHandler sut = BuildSkip(db, sent, music);

        await sut.HandleAsync(SkipMessage("viewer-1", moderator: false), CancellationToken.None);
        await sut.HandleAsync(SkipMessage("viewer-1", moderator: false), CancellationToken.None);
        await sut.HandleAsync(SkipMessage("mod-1", moderator: true), CancellationToken.None);

        await music
            .Received(2)
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        sent.Should().HaveCount(3);
        sent[1].Should().Contain("cooldown");
        sent[2].Should().NotContain("cooldown");
    }

    private static IMusicService SkippingMusic()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .SkipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return music;
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

    private static ChatMessageHandler BuildSkip(
        CommandsTestDbContext db,
        List<string> sent,
        IMusicService music
    )
    {
        CooldownManager cooldowns = new(TimeProvider.System);
        SkipBuiltin skip = new(
            music,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(holdsGrant: false),
            cooldowns
        );
        return BuildWith(db, sent, skip, cooldowns);
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
        return BuildWith(db, sent, lurk, new CooldownManager(TimeProvider.System));
    }

    private static ChatMessageHandler BuildWith(
        CommandsTestDbContext db,
        List<string> sent,
        IBuiltinCommand builtin,
        CooldownManager cooldowns
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-888",
            ChannelName = "stoney_eagle",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);
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
            cooldowns,
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
        ChatLine(login, "!lurk", moderator: false);

    private static ChatMessageReceivedEvent SkipMessage(string login, bool moderator) =>
        ChatLine(login, "!skip", moderator);

    private static ChatMessageReceivedEvent ChatLine(string login, string text, bool moderator) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = $"msg-{Guid.NewGuid()}",
            TwitchBroadcasterId = "tw-888",
            UserId = $"tw-{login}",
            UserDisplayName = login,
            UserLogin = login,
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = moderator,
            IsBroadcaster = false,
        };
}
