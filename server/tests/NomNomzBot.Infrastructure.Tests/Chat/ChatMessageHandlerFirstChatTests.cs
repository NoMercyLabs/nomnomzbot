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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// <see cref="UserFirstChatEvent"/>: raised by the chat handler the first time EVER a viewer chats in a
/// channel, decided from stored chat rows that existed before the message, checked once per viewer per
/// channel per process (never on every message).
/// </summary>
public sealed class ChatMessageHandlerFirstChatTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198a000-0000-7000-8000-00000000f001");

    private sealed class Harness
    {
        public required ChatMessageHandler Sut { get; init; }
        public required ChannelContext Ctx { get; init; }
        public required AuthDbContext Db { get; init; }
        public required List<UserFirstChatEvent> Raised { get; init; }
        public required CountingScopeFactory Scopes { get; init; }
    }

    /// <summary>Counts every scope opened, so the stored-chat lookup can be counted.</summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public int Created { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            return inner.CreateScope();
        }
    }

    private static Harness Build(bool live = false)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChannelContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-777",
            ChannelName = "stoney_eagle",
            IsLive = live,
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(
            Substitute.For<NomNomzBot.Application.Commands.Services.IEventResponseExecutor>()
        );
        ServiceProvider provider = services.BuildServiceProvider();
        CountingScopeFactory scopes = new(provider.GetRequiredService<IServiceScopeFactory>());

        List<UserFirstChatEvent> raised = [];
        IEventBus bus = Substitute.For<IEventBus>();
        bus.When(b => b.PublishAsync(Arg.Any<UserFirstChatEvent>(), Arg.Any<CancellationToken>()))
            .Do(call => raised.Add(call.Arg<UserFirstChatEvent>()));

        ChatMessageHandler sut = new(
            registry,
            scopes,
            Substitute.For<ICooldownManager>(),
            Substitute.For<IInboundOriginChatSender>(),
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            Substitute.For<ITemplateResolver>(),
            bus,
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );

        return new()
        {
            Sut = sut,
            Ctx = ctx,
            Db = db,
            Raised = raised,
            Scopes = scopes,
        };
    }

    private static ChatMessageReceivedEvent Message(
        string messageId,
        string userId = "tw-viewer-1",
        string text = "hello chat"
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = messageId,
            TwitchBroadcasterId = "tw-777",
            UserId = userId,
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

    private static ChatMessage StoredRow(string id, string userId, Guid? broadcaster = null) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster ?? Broadcaster,
            Provider = "twitch",
            UserId = userId,
            Username = "viewer",
            DisplayName = "Viewer",
            UserType = "viewer",
            Message = "earlier",
        };

    [Fact]
    public async Task A_first_message_raises_UserFirstChat_once_with_every_field()
    {
        Harness h = Build();

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);

        UserFirstChatEvent raised = h.Raised.Should().ContainSingle().Subject;
        raised.BroadcasterId.Should().Be(Broadcaster);
        raised.ChannelId.Should().Be("tw-777");
        raised.UserId.Should().Be("tw-viewer-1");
        raised.Username.Should().Be("Viewer");
    }

    [Fact]
    public async Task The_same_viewers_second_message_raises_nothing()
    {
        Harness h = Build();

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);
        await h.Sut.HandleAsync(Message("m-2"), CancellationToken.None);

        h.Raised.Should().ContainSingle();
    }

    [Fact]
    public async Task A_viewer_with_earlier_stored_chat_raises_nothing()
    {
        Harness h = Build();
        h.Db.ChatMessages.Add(StoredRow("old-1", "tw-viewer-1"));
        await h.Db.SaveChangesAsync();

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);

        h.Raised.Should().BeEmpty();
    }

    [Fact]
    public async Task Chat_stored_in_another_channel_does_not_count_as_earlier_chat()
    {
        Harness h = Build();
        h.Db.ChatMessages.Add(StoredRow("old-1", "tw-viewer-1", Guid.CreateVersion7()));
        await h.Db.SaveChangesAsync();

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);

        h.Raised.Should().ContainSingle();
    }

    [Fact]
    public async Task The_message_itself_already_stored_is_not_earlier_chat()
    {
        // The persistence handler may write this very message before this handler runs: the check must
        // be correct either way.
        Harness h = Build();
        h.Db.ChatMessages.Add(StoredRow("m-1", "tw-viewer-1"));
        await h.Db.SaveChangesAsync();

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);

        h.Raised.Should().ContainSingle();
    }

    [Fact]
    public async Task The_stored_chat_lookup_runs_once_for_many_messages_of_one_viewer()
    {
        Harness h = Build();

        for (int i = 0; i < 6; i++)
            await h.Sut.HandleAsync(Message($"m-{i}"), CancellationToken.None);

        h.Scopes.Created.Should().Be(1, "only the first message of a viewer reads stored chat");
    }

    [Fact]
    public async Task Each_new_viewer_is_checked_and_raised_on_their_own()
    {
        Harness h = Build(live: true);

        await h.Sut.HandleAsync(Message("m-1", "tw-a"), CancellationToken.None);
        await h.Sut.HandleAsync(Message("m-2", "tw-b"), CancellationToken.None);
        await h.Sut.HandleAsync(Message("m-3", "tw-a"), CancellationToken.None);

        h.Raised.Select(e => e.UserId).Should().Equal("tw-a", "tw-b");
    }

    [Fact]
    public async Task A_message_the_bot_features_ignore_raises_nothing()
    {
        Harness h = Build();
        h.Ctx.ModerationStandings["twitch:tw-viewer-1"] = "muted";

        await h.Sut.HandleAsync(Message("m-1"), CancellationToken.None);

        h.Raised.Should().BeEmpty();
    }
}
