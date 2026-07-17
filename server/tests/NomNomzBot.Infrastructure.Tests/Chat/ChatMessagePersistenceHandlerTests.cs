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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The chat-history write seam: every persisted <see cref="ChatMessage"/> row records the platform its
/// message arrived on, so a merged multi-platform history can label each line by source. A Kick message must
/// persist as <c>kick</c> (not the table's Twitch-legacy default); a Twitch message keeps <c>twitch</c>.
/// </summary>
public sealed class ChatMessagePersistenceHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static ChatMessageReceivedEvent Message(string provider, string messageId) =>
        new()
        {
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
    public async Task Persists_a_twitch_message_as_twitch()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessagePersistenceHandler handler = new(db);

        await handler.HandleAsync(Message(AuthEnums.Platform.Twitch, "m-twitch"));

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Provider.Should().Be(AuthEnums.Platform.Twitch);
    }
}
