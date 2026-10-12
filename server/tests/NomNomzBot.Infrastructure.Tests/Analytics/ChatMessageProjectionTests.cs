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
using Newtonsoft.Json;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the chat-message projection: the journal (live and imported alike) fills the ChatMessages table keyed
/// on the platform message id, stamped with the time the message was sent, one row per message, and a reset
/// followed by a re-fold does not collide with the rows it just removed.
/// </summary>
public sealed class ChatMessageProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000004001");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-000000004002");
    private static int _seq;

    private static ChatMessageReceivedEvent Message(
        string messageId,
        string text = "hello chat",
        string provider = "twitch",
        Guid? channel = null
    ) =>
        new()
        {
            BroadcasterId = channel ?? Channel,
            MessageId = messageId,
            Provider = provider,
            TwitchBroadcasterId = "998877",
            UserId = "u-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [new() { Type = "text", Text = text }],
            Badges = [new("moderator", "1")],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = true,
            IsBroadcaster = false,
            Bits = 0,
            ReplyParentMessageId = "parent-9",
        };

    private static EventRecord Record(
        ChatMessageReceivedEvent chat,
        DateTime occurredAt,
        string source = "import"
    ) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            chat.BroadcasterId,
            _seq,
            "ChatMessageReceivedEvent",
            1,
            source,
            JsonConvert.SerializeObject(chat),
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            occurredAt,
            occurredAt.AddMinutes(1)
        );

    [Fact]
    public async Task Folds_an_imported_message_into_a_row_stamped_with_the_event_time()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);
        DateTime sent = new(2025, 8, 14, 19, 30, 5, DateTimeKind.Utc);

        await sut.ApplyAsync(Record(Message("tw-msg-1"), sent));

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Id.Should().Be("tw-msg-1");
        row.BroadcasterId.Should().Be(Channel);
        row.CreatedAt.Should().Be(sent);
        row.Provider.Should().Be("twitch");
        row.UserId.Should().Be("u-1");
        row.Username.Should().Be("viewer");
        row.DisplayName.Should().Be("Viewer");
        row.UserType.Should().Be("moderator");
        row.Message.Should().Be("hello chat");
        row.Fragments.Should().ContainSingle().Which.Text.Should().Be("hello chat");
        row.Badges.Should().ContainSingle().Which.SetId.Should().Be("moderator");
        row.ReplyToMessageId.Should().Be("parent-9");
        row.IsCommand.Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_the_provider_a_kick_message_arrived_on()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);

        await sut.ApplyAsync(
            Record(Message("kick-1", provider: AuthEnums.Platform.Kick), DateTime.UtcNow)
        );

        (await db.ChatMessages.SingleAsync()).Provider.Should().Be(AuthEnums.Platform.Kick);
    }

    [Fact]
    public async Task Folding_the_imported_and_the_live_copy_of_one_message_leaves_one_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);
        DateTime sent = new(2026, 7, 6, 12, 0, 0, DateTimeKind.Utc);

        await sut.ApplyAsync(Record(Message("tw-dup", "first copy"), sent));
        await sut.ApplyAsync(
            Record(Message("tw-dup", "second copy"), sent.AddSeconds(2), "domain")
        );

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Id.Should().Be("tw-dup");
        row.Message.Should().Be("first copy");
        row.CreatedAt.Should().Be(sent);
    }

    [Fact]
    public async Task Marks_a_bang_message_as_a_command()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);

        await sut.ApplyAsync(Record(Message("tw-cmd", "!quote add"), DateTime.UtcNow));

        (await db.ChatMessages.SingleAsync()).IsCommand.Should().BeTrue();
    }

    [Fact]
    public async Task Skips_a_payload_that_carries_no_message_id()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);
        EventRecord broken = Record(Message("x"), DateTime.UtcNow) with { PayloadJson = "{}" };

        (await sut.ApplyAsync(broken)).IsSuccess.Should().BeTrue();

        (await db.ChatMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reset_removes_only_the_scoped_channel_and_a_refold_does_not_collide()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessageProjection sut = new(db);
        DateTime sent = new(2025, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        EventRecord mine = Record(Message("mine-1"), sent);
        await sut.ApplyAsync(mine);
        await sut.ApplyAsync(Record(Message("theirs-1", channel: OtherChannel), sent));
        // The projection runner clears the change tracker after every committed batch; do the same here.
        db.ChangeTracker.Clear();

        (await sut.ResetAsync(Channel)).IsSuccess.Should().BeTrue();

        List<string> left = await db.ChatMessages.Select(m => m.Id).ToListAsync();
        left.Should().Equal("theirs-1");

        (await sut.ApplyAsync(mine)).IsSuccess.Should().BeTrue();
        (await db.ChatMessages.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync())
            .Should()
            .Equal("mine-1", "theirs-1");
    }
}
