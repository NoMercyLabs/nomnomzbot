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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Identity.Jobs;
using NomNomzBot.Infrastructure.Tests.Community;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Users.Color and Users.LastSeenAt were never written, so profiles read as half empty. A chat message now stamps
/// both on the sender's row (write throttled to once a minute), and the profile refresh backfills a missing color
/// from Helix Get User Chat Color, 100 ids per call.
/// </summary>
public sealed class UserSeenOnChatTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ChatMessageReceivedEvent Message(string? color, DateTime at) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString(),
            BroadcasterId = Guid.NewGuid(),
            TwitchBroadcasterId = "1",
            UserId = "42",
            UserDisplayName = "Chatter",
            UserLogin = "chatter",
            Message = "hi",
            Fragments = [],
            ColorHex = color,
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
            OccurredAt = new DateTimeOffset(at),
        };

    private static User NewUser(string twitchId, string? color = null, DateTime? lastSeen = null) =>
        new()
        {
            TwitchUserId = twitchId,
            Username = $"u{twitchId}",
            UsernameNormalized = $"u{twitchId}",
            DisplayName = $"u{twitchId}",
            Color = color,
            LastSeenAt = lastSeen,
        };

    private static async Task<ViewerProfileServiceTestDbContext> SeedAsync(User user)
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_chat_message_writes_the_senders_color_and_last_seen_time()
    {
        User user = NewUser("42");
        ViewerProfileServiceTestDbContext db = await SeedAsync(user);
        UserSeenOnChatRecorder recorder = new(db);

        await recorder.HandleAsync(Message("#9146FF", Now));

        User stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.Color.Should().Be("#9146FF");
        stored.LastSeenAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_message_inside_a_minute_skips_last_seen_but_a_color_change_still_lands()
    {
        User user = NewUser("42", color: "#9146FF", lastSeen: Now);
        ViewerProfileServiceTestDbContext db = await SeedAsync(user);
        UserSeenOnChatRecorder recorder = new(db);

        await recorder.HandleAsync(Message("#9146FF", Now.AddSeconds(20)));
        User first = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        first.LastSeenAt.Should().Be(Now, "a write inside the throttle window is skipped");

        await recorder.HandleAsync(Message("#FF0000", Now.AddSeconds(30)));
        User second = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        second.Color.Should().Be("#FF0000");
    }

    [Fact]
    public async Task A_message_after_the_window_moves_last_seen_and_an_empty_color_keeps_the_old_one()
    {
        User user = NewUser("42", color: "#9146FF", lastSeen: Now);
        ViewerProfileServiceTestDbContext db = await SeedAsync(user);
        UserSeenOnChatRecorder recorder = new(db);

        await recorder.HandleAsync(Message(null, Now.AddMinutes(2)));

        User stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.LastSeenAt.Should().Be(Now.AddMinutes(2));
        stored.Color.Should().Be("#9146FF");
    }

    [Fact]
    public async Task The_refresh_fills_a_missing_color_from_helix_and_leaves_other_users_alone()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        User none = NewUser("1");
        User unset = NewUser("2");
        User has = NewUser("3", color: "#111111");
        IReadOnlyList<TwitchUserChatColor> reply =
        [
            new("1", "u1", "u1", "#00FF7F"),
            new("2", "u2", "u2", ""),
        ];
        chat.GetUserChatColorsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(reply));

        int filled = await UserProfileHydrationService.BackfillColorsAsync(
            chat,
            [none, unset, has],
            default
        );

        filled.Should().Be(1);
        none.Color.Should().Be("#00FF7F");
        unset.Color.Should().BeNull("Twitch has no color for this user");
        has.Color.Should().Be("#111111");
        await chat.Received(1)
            .GetUserChatColorsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "1", "2" })),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_refresh_asks_helix_for_colors_100_ids_per_call()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        IReadOnlyList<TwitchUserChatColor> empty = [];
        chat.GetUserChatColorsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(empty));
        List<User> users = [.. Enumerable.Range(1, 250).Select(i => NewUser(i.ToString()))];

        await UserProfileHydrationService.BackfillColorsAsync(chat, users, default);

        List<int> sizes = [];
        foreach (NSubstitute.Core.ICall call in chat.ReceivedCalls())
            sizes.Add(((IReadOnlyList<string>)call.GetArguments()[0]!).Count);
        sizes.Should().Equal(100, 100, 50);
    }
}
