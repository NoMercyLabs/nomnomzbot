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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A reloaded chat_box shows the conversation at once: the newest <c>maxMessages</c> stored messages of the last
/// 30 minutes, oldest first, in the same rendered shape as live chat. Deleted messages, older chat and other
/// channels never come back.
/// </summary>
public sealed class ChatBoxSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);

    private static ChatMessage Message(
        string id,
        Guid broadcasterId,
        int minutesAgo,
        DateTime? deletedAt = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcasterId,
            UserId = "u1",
            Username = "viewer",
            DisplayName = "Viewer",
            UserType = "viewer",
            Message = id,
            Fragments = [new() { Type = "text", Text = id }],
            Badges = [],
            CreatedAt = Now.AddMinutes(-minutesAgo).UtcDateTime,
            DeletedAt = deletedAt,
        };

    [Fact]
    public async Task Seed_returns_the_newest_recent_messages_oldest_first_as_chat_frames()
    {
        ChatControllerTestDbContext db = ChatControllerTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.ChatMessages.AddRange(
            Message("too-old", Broadcaster, 45),
            Message("first", Broadcaster, 20),
            Message("second", Broadcaster, 10),
            Message("deleted", Broadcaster, 8, Now.UtcDateTime),
            Message("other-channel", Guid.CreateVersion7(), 6),
            Message("third", Broadcaster, 5)
        );
        await db.SaveChangesAsync();

        IChatMessageDecorator decorator = Substitute.For<IChatMessageDecorator>();
        decorator
            .DecorateAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new DecoratedChatMessage { Fragments = [], Badges = [] });
        ChatBoxSeedProvider sut = new(
            db,
            decorator,
            Substitute.For<IHubUserEnricher>(),
            new FakeTimeProvider(Now)
        );

        IReadOnlyList<WidgetSeedFrame> frames = await sut.SeedAsync(
            Broadcaster,
            new Widget
            {
                BroadcasterId = Broadcaster,
                Settings = new Dictionary<string, object> { ["maxMessages"] = 2L },
            },
            CancellationToken.None
        );

        frames.Select(f => f.EventType).Should().Equal("ChatMessage", "ChatMessage");
        frames
            .Select(f => ((DashboardChatMessageDto)f.Data!).Message)
            .Should()
            .Equal("second", "third");
    }
}
