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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Quotes.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Quotes;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Twitch puts "@&lt;parent name&gt; " in front of a reply's text, so a command sent as a reply arrives as
/// "@DukaSoft !quote". The handler strips that one leading mention (replies only) before it parses the command.
/// </summary>
public sealed class ChatMessageHandlerReplyCommandTests
{
    private const string UptimeResponse = "Live for 1h 23m!";

    private static readonly FakeTimeProvider Clock = new(
        new(2026, 10, 11, 16, 40, 0, TimeSpan.Zero)
    );

    [Fact]
    public async Task A_reply_with_the_parent_mention_in_front_of_quote_saves_the_parent_message_credited_to_its_author()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(
            channel,
            NewQuoteBuiltin(quotes)
        );

        await sut.HandleAsync(
            Event(channel, "@DukaSoft !quote", reply: true),
            CancellationToken.None
        );

        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("the parent line");
        stored.Value.QuotedDisplayName.Should().Be("DukaSoft");
        await chat.Received(1)
            .SendReplyAsync(
                channel,
                Arg.Any<string>(),
                "msg-1",
                Arg.Is<string>(s => s.Contains("the parent line")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_parent_login_in_any_case_is_stripped_too()
    {
        using QuoteSqliteTestDatabase database = QuoteSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await using QuoteTestDbContext db = database.NewContext();
        IQuoteService quotes = NewQuoteService(db);
        (ChatMessageHandler sut, _) = Build(channel, NewQuoteBuiltin(quotes));

        await sut.HandleAsync(
            Event(
                channel,
                "@DUKASOFT !quote",
                reply: true,
                parentName: "Duka Soft",
                parentLogin: "dukasoft"
            ),
            CancellationToken.None
        );

        Result<QuoteDto> stored = await quotes.GetAsync(channel, 1);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.Text.Should().Be("the parent line");
    }

    [Fact]
    public async Task A_reply_with_the_parent_mention_in_front_of_uptime_runs_uptime()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(channel, new UptimeStub());

        await sut.HandleAsync(
            Event(channel, "@DukaSoft !uptime", reply: true),
            CancellationToken.None
        );

        await chat.Received(1)
            .SendReplyAsync(
                channel,
                Arg.Any<string>(),
                "msg-1",
                UptimeResponse,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_message_that_is_not_a_reply_with_a_leading_mention_is_not_a_command()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(channel, new UptimeStub());

        await sut.HandleAsync(
            Event(channel, "@DukaSoft !uptime", reply: false),
            CancellationToken.None
        );

        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default!);
        await chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_reply_that_starts_with_another_mention_than_the_parent_is_not_a_command()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(channel, new UptimeStub());

        await sut.HandleAsync(
            Event(channel, "@SomeoneElse !uptime", reply: true),
            CancellationToken.None
        );

        await chat.DidNotReceiveWithAnyArgs().SendReplyAsync(default, default!, default!, default!);
    }

    [Fact]
    public async Task A_reply_that_is_already_a_bare_command_still_runs()
    {
        Guid channel = Guid.CreateVersion7();
        (ChatMessageHandler sut, IInboundOriginChatSender chat) = Build(channel, new UptimeStub());

        await sut.HandleAsync(Event(channel, "!uptime", reply: true), CancellationToken.None);

        await chat.Received(1)
            .SendReplyAsync(
                channel,
                Arg.Any<string>(),
                "msg-1",
                UptimeResponse,
                Arg.Any<CancellationToken>()
            );
    }

    private static QuoteService NewQuoteService(QuoteTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new QuoteTestUnitOfWork(db),
            new RecordingEventBus(),
            Clock
        );

    private static QuoteBuiltin NewQuoteBuiltin(IQuoteService quotes) =>
        new(
            quotes,
            NewUsers(),
            NewRoles(),
            Substitute.For<ITtsDispatchService>(),
            TestBuiltinComposer.Create()
        );

    private static async Task<Guid> SeedChannelAsync(QuoteSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using QuoteTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "tw-777",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static IUserService NewUsers()
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
            .Returns(
                Result.Success(
                    new UserDto(
                        Id: Guid.CreateVersion7().ToString(),
                        Username: "viewer",
                        DisplayName: "Viewer",
                        ProfileImageUrl: null,
                        Email: null,
                        CreatedAt: DateTime.UnixEpoch,
                        LastLoginAt: DateTime.UnixEpoch
                    )
                )
            );
        return users;
    }

    private static IRoleResolver NewRoles()
    {
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(true));
        return roles;
    }

    private static (ChatMessageHandler Sut, IInboundOriginChatSender Chat) Build(
        Guid channel,
        IBuiltinCommand builtin
    )
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = channel,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(channel).Returns(ctx);

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
            chat,
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
        string message,
        bool reply,
        string parentName = "DukaSoft",
        string parentLogin = "dukasoft"
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
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
            ReplyParentMessageId = reply ? "parent-1" : null,
            ReplyParentMessageBody = reply ? "the parent line" : null,
            ReplyParentUserName = reply ? parentName : null,
            ReplyParentUserLogin = reply ? parentLogin : null,
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
