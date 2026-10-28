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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The chat-history write seam: every persisted <see cref="ChatMessage"/> row records the platform its
/// message arrived on, so a merged multi-platform history can label each line by source. A Kick message must
/// persist as <c>kick</c> (not the table's Twitch-legacy default); a Twitch message keeps <c>twitch</c>.
/// </summary>
public sealed class ChatMessagePersistenceHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static ChatMessageReceivedEvent Message(
        string provider,
        string messageId,
        DateTimeOffset? occurredAt = null
    ) =>
        new()
        {
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            BroadcasterId = Broadcaster,
            MessageId = messageId,
            Provider = provider,
            TwitchBroadcasterId = "998877",
            UserId = "u-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "hello chat",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task Persists_a_kick_message_tagged_with_its_kick_provider()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessagePersistenceHandler handler = new(db);

        await handler.HandleAsync(Message(AuthEnums.Platform.Kick, "m-kick"));

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Provider.Should().Be(AuthEnums.Platform.Kick);
        // The rest of the canonical shape rides through unchanged — the provider is an addition, not a swap.
        row.UserId.Should().Be("u-1");
        row.Message.Should().Be("hello chat");
    }

    [Fact]
    public async Task Stamps_the_row_with_the_event_time()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessagePersistenceHandler handler = new(db);
        DateTimeOffset sent = new(2025, 8, 14, 19, 30, 5, TimeSpan.Zero);

        await handler.HandleAsync(Message(AuthEnums.Platform.Twitch, "m-time", sent));

        (await db.ChatMessages.SingleAsync()).CreatedAt.Should().Be(sent.UtcDateTime);
    }

    [Fact]
    public async Task Persists_a_twitch_message_as_twitch()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessagePersistenceHandler handler = new(db);

        await handler.HandleAsync(Message(AuthEnums.Platform.Twitch, "m-twitch"));

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Provider.Should().Be(AuthEnums.Platform.Twitch);
    }
}
