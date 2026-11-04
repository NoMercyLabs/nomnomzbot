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

    [Fact]
    public async Task Persists_announcement_as_typed_feed_line_with_color_and_source()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatNativeFeedPersistenceHandler handler = new(db);
        ChatNotificationEvent notice = new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "notice-1",
            ChatterUserId = "u-1",
            ChatterLogin = "viewer",
            ChatterDisplayName = "Viewer",
            IsAnonymous = false,
            NoticeType = "announcement",
            SystemMessage = "Viewer made an announcement",
            MessageText = "Hello everyone",
            AnnouncementColor = "PURPLE",
            SourceChannelId = "source-1",
            SourceChannelName = "Other Stream",
        };

        await handler.HandleAsync(notice);

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Kind.Should().Be("notice");
        row.NoticeType.Should().Be("announcement");
        row.SystemText.Should().Be("Viewer made an announcement");
        row.AnnouncementColor.Should().Be("PURPLE");
        row.SourceChannelId.Should().Be("source-1");
        row.SourceChannelName.Should().Be("Other Stream");
        row.Message.Should().Be("Hello everyone");
    }

    [Fact]
    public async Task Persists_clear_and_held_lines_with_distinct_kinds()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatNativeFeedPersistenceHandler handler = new(db);

        await handler.HandleAsync(
            new ChatClearedEvent { BroadcasterId = Broadcaster, ClearedByUserId = string.Empty }
        );
        await handler.HandleAsync(
            new ChatUserMessageHeldEvent
            {
                BroadcasterId = Broadcaster,
                UserId = "u-2",
                UserDisplayName = "Another Viewer",
                UserLogin = "another_viewer",
                MessageId = "held-1",
                Text = "held text",
            }
        );

        List<ChatMessage> rows = await db.ChatMessages.OrderBy(m => m.CreatedAt).ToListAsync();
        rows.Should().ContainSingle(m => m.Kind == "moderation" && m.NoticeType == "chat_clear");
        rows.Should().ContainSingle(m => m.Kind == "held" && m.Message == "held text");
    }

    [Fact]
    public async Task Persists_reply_parent_context_for_history()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatMessagePersistenceHandler handler = new(db);
        ChatMessageReceivedEvent reply = new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "reply-1",
            TwitchBroadcasterId = "998877",
            UserId = "u-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "I agree",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
            ReplyParentMessageId = "parent-1",
            ReplyThreadParentMessageId = "root-1",
            ReplyParentMessageBody = "Parent text",
            ReplyParentUserName = "Other Viewer",
        };

        await handler.HandleAsync(reply);

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.ReplyToMessageId.Should().Be("parent-1");
        row.ReplyThreadParentMessageId.Should().Be("root-1");
        row.ReplyParentMessageBody.Should().Be("Parent text");
        row.ReplyParentUserName.Should().Be("Other Viewer");
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("resub")]
    [InlineData("sub_gift")]
    [InlineData("community_sub_gift")]
    [InlineData("gift_paid_upgrade")]
    [InlineData("prime_paid_upgrade")]
    [InlineData("pay_it_forward")]
    [InlineData("raid")]
    [InlineData("unraid")]
    [InlineData("announcement")]
    [InlineData("bits_badge_tier")]
    [InlineData("charity_donation")]
    [InlineData("watch_streak")]
    [InlineData("modiversary")]
    [InlineData("gifted_drops_summary")]
    [InlineData("shared_chat_sub")]
    [InlineData("shared_chat_resub")]
    [InlineData("shared_chat_sub_gift")]
    [InlineData("shared_chat_community_sub_gift")]
    [InlineData("shared_chat_gift_paid_upgrade")]
    [InlineData("shared_chat_prime_paid_upgrade")]
    [InlineData("shared_chat_pay_it_forward")]
    [InlineData("shared_chat_raid")]
    [InlineData("shared_chat_unraid")]
    [InlineData("shared_chat_announcement")]
    [InlineData("shared_chat_bits_badge_tier")]
    [InlineData("shared_chat_charity_donation")]
    [InlineData("shared_chat_modiversary")]
    [InlineData("shared_chat_gifted_drops_summary")]
    [InlineData("unknown")]
    public async Task Persists_each_documented_notice_type_as_a_typed_feed_line(string noticeType)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatNativeFeedPersistenceHandler handler = new(db);

        await handler.HandleAsync(
            new ChatNotificationEvent
            {
                BroadcasterId = Broadcaster,
                MessageId = "notice-1",
                ChatterUserId = "u-1",
                ChatterLogin = "viewer",
                ChatterDisplayName = "Viewer",
                IsAnonymous = false,
                NoticeType = noticeType,
                SystemMessage = "A Twitch notice",
                MessageText = string.Empty,
            }
        );

        ChatMessage row = await db.ChatMessages.SingleAsync();
        row.Kind.Should().Be("notice");
        row.NoticeType.Should().Be(noticeType);
        row.SystemText.Should().Be("A Twitch notice");
    }

    [Fact]
    public async Task Persists_targeted_clear_and_held_update_as_feed_lines()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ChatNativeFeedPersistenceHandler handler = new(db);
        await handler.HandleAsync(
            new ChatUserMessagesClearedEvent
            {
                BroadcasterId = Broadcaster,
                TargetUserId = "u-1",
                TargetUserLogin = "viewer",
                TargetUserDisplayName = "Viewer",
            }
        );
        await handler.HandleAsync(
            new ChatUserMessageUpdatedEvent
            {
                BroadcasterId = Broadcaster,
                UserId = "u-1",
                UserLogin = "viewer",
                UserDisplayName = "Viewer",
                MessageId = "held-1",
                Status = "approved",
                Text = "approved text",
            }
        );

        List<ChatMessage> rows = await db.ChatMessages.ToListAsync();
        rows.Should()
            .ContainSingle(m =>
                m.Kind == "moderation" && m.NoticeType == "chat_clear_user" && m.UserId == "u-1"
            );
        rows.Should()
            .ContainSingle(m =>
                m.Kind == "held" && m.HeldStatus == "approved" && m.Message == "approved text"
            );
    }
}
