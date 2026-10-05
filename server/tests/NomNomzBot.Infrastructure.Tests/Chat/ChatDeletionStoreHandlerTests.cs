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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The chat-history delete seam: a Twitch chat clear, a single message delete and a per-user purge each
/// soft-delete the stored <see cref="ChatMessage"/> rows (legacy ledger rows 123 and 131), scoped to one channel.
/// </summary>
public sealed class ChatDeletionStoreHandlerTests
{
    private static readonly Guid ChannelA = Guid.CreateVersion7();
    private static readonly Guid ChannelB = Guid.CreateVersion7();
    private static readonly DateTime FirstDelete = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly FakeTimeProvider Clock = new(new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    private static ChatMessage Row(
        Guid channel,
        string id,
        string userId,
        DateTime? deletedAt = null
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = channel,
            Provider = "twitch",
            UserId = userId,
            Username = userId,
            DisplayName = userId,
            UserType = "viewer",
            Message = "hi",
            DeletedAt = deletedAt,
        };

    private static async Task<AuthDbContext> SeedAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.ChatMessages.AddRange(
            Row(ChannelA, "a-1", "u-1"),
            Row(ChannelA, "a-2", "u-2"),
            Row(ChannelA, "a-old", "u-1", FirstDelete),
            Row(ChannelB, "b-1", "u-1")
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static Task<List<ChatMessage>> AllAsync(AuthDbContext db) =>
        db.ChatMessages.IgnoreQueryFilters().AsNoTracking().ToListAsync();

    [Fact]
    public async Task Chat_clear_soft_deletes_every_live_message_of_that_channel_only()
    {
        AuthDbContext db = await SeedAsync();
        ChatDeletionStoreHandler handler = new(db, Clock);

        await handler.HandleAsync(
            new ChatClearedEvent { BroadcasterId = ChannelA, ClearedByUserId = "mod-1" }
        );

        List<ChatMessage> rows = await AllAsync(db);
        DateTime now = Clock.GetUtcNow().UtcDateTime;
        foreach (string id in new[] { "a-1", "a-2" })
        {
            ChatMessage row = rows.Single(r => r.Id == id);
            row.DeletedAt.Should().Be(now);
            row.UpdatedAt.Should().Be(now);
        }
        rows.Single(r => r.Id == "b-1").DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Chat_clear_keeps_the_first_deleted_time_of_an_already_deleted_row()
    {
        AuthDbContext db = await SeedAsync();
        ChatDeletionStoreHandler handler = new(db, Clock);

        await handler.HandleAsync(
            new ChatClearedEvent { BroadcasterId = ChannelA, ClearedByUserId = "mod-1" }
        );

        List<ChatMessage> rows = await AllAsync(db);
        rows.Single(r => r.Id == "a-old").DeletedAt.Should().Be(FirstDelete);
    }

    [Fact]
    public async Task Message_delete_soft_deletes_only_that_message()
    {
        AuthDbContext db = await SeedAsync();
        ChatDeletionStoreHandler handler = new(db, Clock);

        await handler.HandleAsync(
            new ChatMessageDeletedEvent
            {
                BroadcasterId = ChannelA,
                MessageId = "a-1",
                DeletedByUserId = "mod-1",
                TargetUserId = "u-1",
            }
        );

        List<ChatMessage> rows = await AllAsync(db);
        rows.Single(r => r.Id == "a-1").DeletedAt.Should().Be(Clock.GetUtcNow().UtcDateTime);
        rows.Single(r => r.Id == "a-2").DeletedAt.Should().BeNull();
        rows.Single(r => r.Id == "b-1").DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Message_delete_of_an_already_deleted_message_keeps_its_first_time()
    {
        AuthDbContext db = await SeedAsync();
        ChatDeletionStoreHandler handler = new(db, Clock);

        await handler.HandleAsync(
            new ChatMessageDeletedEvent
            {
                BroadcasterId = ChannelA,
                MessageId = "a-old",
                DeletedByUserId = "mod-1",
                TargetUserId = "u-1",
            }
        );

        (await AllAsync(db)).Single(r => r.Id == "a-old").DeletedAt.Should().Be(FirstDelete);
    }

    [Fact]
    public async Task User_purge_soft_deletes_that_chatters_live_messages_in_that_channel_only()
    {
        AuthDbContext db = await SeedAsync();
        ChatDeletionStoreHandler handler = new(db, Clock);

        await handler.HandleAsync(
            new ChatUserMessagesClearedEvent
            {
                BroadcasterId = ChannelA,
                TargetUserId = "u-1",
                TargetUserDisplayName = "u-1",
                TargetUserLogin = "u-1",
            }
        );

        List<ChatMessage> rows = await AllAsync(db);
        DateTime now = Clock.GetUtcNow().UtcDateTime;
        rows.Single(r => r.Id == "a-1").DeletedAt.Should().Be(now);
        rows.Single(r => r.Id == "a-1").UpdatedAt.Should().Be(now);
        rows.Single(r => r.Id == "a-old").DeletedAt.Should().Be(FirstDelete);
        rows.Single(r => r.Id == "a-2").DeletedAt.Should().BeNull();
        rows.Single(r => r.Id == "b-1").DeletedAt.Should().BeNull();
    }
}
