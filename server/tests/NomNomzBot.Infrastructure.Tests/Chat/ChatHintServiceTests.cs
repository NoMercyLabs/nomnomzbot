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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Community;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The hints a chat line carries are facts worked out from the stored history: first message ever, first of the
/// calendar day in the channel's timezone, first of the stream, and the account's creation date. One call serves a
/// live line (one message, maybe not stored yet) and a history page (many messages, all stored).
/// </summary>
public sealed class ChatHintServiceTests
{
    private static readonly Guid Channel = Guid.Parse("019f4c00-0000-7000-8000-000000000001");
    private static readonly Guid OtherChannel = Guid.Parse("019f4c00-0000-7000-8000-000000000002");

    private static DateTime Utc(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ChatMessage Stored(
        string id,
        string userId,
        DateTime at,
        Guid? channel = null,
        string provider = "twitch"
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = channel ?? Channel,
            Provider = provider,
            UserId = userId,
            Username = $"user{userId}",
            DisplayName = $"User{userId}",
            UserType = "viewer",
            Message = "hello",
            CreatedAt = at,
        };

    private static ChatHintRequest Ask(ChatMessage m, DateTime? streamStart = null) =>
        new(m.Id, m.Provider, m.UserId, m.CreatedAt, streamStart);

    private static ViewerProfileServiceTestDbContext NewDb(params ChatMessage[] stored)
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        db.ChatMessages.AddRange(stored);
        db.SaveChanges();

        return db;
    }

    [Fact]
    public async Task Only_the_earliest_stored_message_of_a_chatter_is_the_first_ever()
    {
        ChatMessage first = Stored("m1", "7", Utc(11, 1, 10));
        ChatMessage second = Stored("m2", "7", Utc(11, 1, 11));
        using ViewerProfileServiceTestDbContext db = NewDb(first, second);

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(first), Ask(second)]
        );

        hints["m1"].FirstEver.Should().BeTrue();
        hints["m2"].FirstEver.Should().BeFalse();
    }

    [Fact]
    public async Task A_message_not_stored_yet_is_first_ever_only_when_nothing_older_is_stored()
    {
        using ViewerProfileServiceTestDbContext db = NewDb(Stored("old", "7", Utc(10, 30, 9)));
        ChatMessage livenew = Stored("live-new", "8", Utc(11, 1, 12));
        ChatMessage liveOld = Stored("live-old", "7", Utc(11, 1, 12));

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(livenew), Ask(liveOld)]
        );

        hints["live-new"].FirstEver.Should().BeTrue();
        hints["live-old"].FirstEver.Should().BeFalse();
    }

    [Fact]
    public async Task First_of_day_follows_the_channel_timezone_not_utc()
    {
        // Amsterdam is UTC+1 on 2026-11-01. 20:00Z is 21:00 on Nov 1; 23:30Z is 00:30 on Nov 2, a new local day.
        ChatMessage evening = Stored("evening", "7", Utc(11, 1, 20));
        ChatMessage afterMidnight = Stored("after-midnight", "7", Utc(11, 1, 23, 30));
        ChatMessage nextMorning = Stored("next-morning", "7", Utc(11, 2, 8));
        using ViewerProfileServiceTestDbContext db = NewDb(evening, afterMidnight, nextMorning);

        IReadOnlyDictionary<string, ChatHints> amsterdam = await new ChatHintService(
            db
        ).ResolveAsync(
            Channel,
            "Europe/Amsterdam",
            [Ask(evening), Ask(afterMidnight), Ask(nextMorning)]
        );
        IReadOnlyDictionary<string, ChatHints> utc = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(evening), Ask(afterMidnight), Ask(nextMorning)]
        );

        amsterdam["evening"].FirstOfDay.Should().BeTrue();
        amsterdam["after-midnight"].FirstOfDay.Should().BeTrue();
        amsterdam["next-morning"].FirstOfDay.Should().BeFalse();

        utc["evening"].FirstOfDay.Should().BeTrue();
        utc["after-midnight"].FirstOfDay.Should().BeFalse();
        utc["next-morning"].FirstOfDay.Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_timezone_reads_as_utc()
    {
        ChatMessage a = Stored("a", "7", Utc(11, 1, 20));
        ChatMessage b = Stored("b", "7", Utc(11, 1, 23, 30));
        using ViewerProfileServiceTestDbContext db = NewDb(a, b);

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            "Not/AZone",
            [Ask(a), Ask(b)]
        );

        hints["a"].FirstOfDay.Should().BeTrue();
        hints["b"].FirstOfDay.Should().BeFalse();
    }

    [Fact]
    public async Task First_of_stream_is_the_first_stored_message_of_that_stream()
    {
        DateTime stream1 = Utc(11, 1, 9);
        DateTime stream2 = Utc(11, 1, 17);
        ChatMessage s1a = Stored("s1a", "7", Utc(11, 1, 10));
        ChatMessage s1b = Stored("s1b", "7", Utc(11, 1, 10, 5));
        ChatMessage s2a = Stored("s2a", "7", Utc(11, 1, 18));
        using ViewerProfileServiceTestDbContext db = NewDb(s1a, s1b, s2a);

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(s1a, stream1), Ask(s1b, stream1), Ask(s2a, stream2)]
        );

        hints["s1a"].FirstOfStream.Should().BeTrue();
        hints["s1b"].FirstOfStream.Should().BeFalse();
        hints["s2a"].FirstOfStream.Should().BeTrue();
    }

    [Fact]
    public async Task A_message_outside_any_stream_is_never_first_of_stream()
    {
        ChatMessage offline = Stored("offline", "7", Utc(11, 1, 10));
        using ViewerProfileServiceTestDbContext db = NewDb(offline);

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(offline)]
        );

        hints["offline"].FirstOfStream.Should().BeFalse();
    }

    [Fact]
    public async Task Chatters_and_channels_are_counted_apart()
    {
        ChatMessage mine = Stored("mine", "7", Utc(11, 1, 12));
        ChatMessage otherChatterEarlier = Stored("other", "8", Utc(11, 1, 8));
        ChatMessage sameChatterOtherChannel = Stored(
            "elsewhere",
            "7",
            Utc(10, 1, 8),
            channel: OtherChannel
        );
        using ViewerProfileServiceTestDbContext db = NewDb(
            mine,
            otherChatterEarlier,
            sameChatterOtherChannel
        );

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(mine)]
        );

        hints["mine"].FirstEver.Should().BeTrue();
        hints["mine"].FirstOfDay.Should().BeTrue();
    }

    [Fact]
    public async Task The_account_creation_date_comes_from_the_user_record_of_a_twitch_chatter()
    {
        DateTime created = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        ChatMessage twitch = Stored("t", "7", Utc(11, 1, 12));
        ChatMessage youtube = Stored("y", "7", Utc(11, 1, 12), provider: "youtube");
        ChatMessage unknown = Stored("u", "9", Utc(11, 1, 12));
        using ViewerProfileServiceTestDbContext db = NewDb(twitch, youtube, unknown);
        db.Users.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                TwitchUserId = "7",
                Username = "user7",
                UsernameNormalized = "user7",
                DisplayName = "User7",
                AccountCreatedAt = created,
            }
        );
        db.SaveChanges();

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            [Ask(twitch), Ask(youtube), Ask(unknown)]
        );

        hints["t"].AccountCreatedAtUtc.Should().Be(created);
        hints["y"].AccountCreatedAtUtc.Should().BeNull();
        hints["u"].AccountCreatedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_request_answers_empty_without_touching_the_history()
    {
        using ViewerProfileServiceTestDbContext db = NewDb();

        IReadOnlyDictionary<string, ChatHints> hints = await new ChatHintService(db).ResolveAsync(
            Channel,
            null,
            []
        );

        hints.Should().BeEmpty();
    }
}
